using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace GuardPosts
{
    // Mustering the guard works like calling up the militia, with each guard's on-duty gear: they drop what they're doing,
    // switch to their on-duty apparel and loadout, gear up, gather at the nearest muster spot, then get drafted.
    // Dismissing undrafts them and hands them back to their schedule and duty setting.
    public static class GuardMuster
    {
        private static bool CanMuster(Pawn pawn) => MilitiaUtility.CanServe(pawn) && GuardUtility.IsGuard(pawn);

        public static void Muster()
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            comp.guardsMustered = true;
            List<Pawn> mustered = Enroll(comp);
            if (mustered.Count == 0)
            {
                comp.guardsMustered = false;
                Messages.Message("GP_MusterNoGuards".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Tick();
            Messages.Message("GP_GuardsMustered".Translate(mustered.Count), new LookTargets(mustered), MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public static void Dismiss()
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            comp.guardsMustered = false;
            foreach (KeyValuePair<Pawn, GuardAssignment> entry in comp.AllAssignments.ToList())
            {
                if (entry.Value.guardMustered)
                {
                    Release(entry.Key, entry.Value);
                }
            }
            // Duty follows the duty mode (and schedule, in auto duty) again.
            DutyUtility.UpdateAll(announce: false);
            Messages.Message("GP_GuardsDismissed".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
        }

        private static void Release(Pawn pawn, GuardAssignment assignment)
        {
            if (assignment.guardGearing)
            {
                MusterSteps.Abandon(pawn, assignment.guardMusterJob);
            }
            assignment.guardMustered = false;
            assignment.guardGearing = false;
            assignment.guardGathering = false;
            assignment.guardMusterJob = null;
            // A called-up militia member stays drafted and keeps their gear until the militia stands down.
            if (pawn == null || assignment.militiaCalled)
            {
                assignment.guardForcedBefore = null;
                return;
            }
            MusterSteps.UnforceMusterApparel(pawn, assignment.guardForcedBefore);
            assignment.guardForcedBefore = null;
            if (pawn.Spawned && pawn.drafter != null && pawn.drafter.Drafted)
            {
                pawn.drafter.Drafted = false;
            }
        }

        // Adds guards who aren't mustered yet (including ones made guards since the muster) and puts them on duty.
        private static List<Pawn> Enroll(GameComponent_Guards comp)
        {
            List<Pawn> enrolled = new List<Pawn>();
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.ToList())
                {
                    if (!CanMuster(pawn))
                    {
                        continue;
                    }
                    GuardAssignment assignment = comp.GetAssignment(pawn);
                    if (!assignment.guardMustered)
                    {
                        assignment.guardMustered = true;
                        assignment.guardGearing = true;
                        assignment.guardGathering = false;
                        assignment.guardGearStartTick = Find.TickManager.TicksGame;
                        assignment.guardForcedBefore = MusterSteps.ForcedApparelIds(pawn);
                        assignment.guardMusterJob = null;
                        if (!assignment.militiaCalled)
                        {
                            DutyUtility.PutOnDuty(pawn, assignment);
                        }
                    }
                    enrolled.Add(pawn);
                }
            }
            return enrolled;
        }

        public static void Tick()
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null || !comp.guardsMustered)
            {
                return;
            }
            Enroll(comp);
            List<Pawn> drafted = null;
            foreach (KeyValuePair<Pawn, GuardAssignment> entry in comp.AllAssignments.ToList())
            {
                Pawn pawn = entry.Key;
                GuardAssignment assignment = entry.Value;
                if (!assignment.guardMustered || !assignment.guardGearing)
                {
                    continue;
                }
                if (!CanMuster(pawn))
                {
                    // No longer a guard (or can't be one): back to normal.
                    Release(pawn, assignment);
                    continue;
                }
                // The militia takes precedence; guards away from a map muster when they're back.
                if (assignment.militiaCalled || !pawn.Spawned || pawn.drafter == null)
                {
                    continue;
                }
                // Drafted by hand before they finished: they're the player's now.
                if (pawn.Drafted)
                {
                    assignment.guardGearing = false;
                    assignment.guardGathering = false;
                    assignment.guardMusterJob = null;
                    continue;
                }
                if (pawn.Downed || pawn.InMentalState)
                {
                    continue;
                }
                // Gear up, gather at the muster spot (if there is one), then draft.
                if (!MusterSteps.Step(pawn, ref assignment.guardGathering, ref assignment.guardGearStartTick, ref assignment.guardMusterJob, gather: true))
                {
                    continue;
                }
                assignment.guardGearing = false;
                assignment.guardGathering = false;
                assignment.guardMusterJob = null;
                pawn.drafter.Drafted = true;
                (drafted ??= new List<Pawn>()).Add(pawn);
            }
            if (drafted != null)
            {
                Messages.Message("GP_GuardsDrafted".Translate(drafted.Count), new LookTargets(drafted), MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        public static void DoMusterButton(Rect rect)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            bool mustered = comp.guardsMustered;
            GUI.color = mustered ? new Color(1f, 0.55f, 0.45f) : Color.white;
            if (Widgets.ButtonText(rect, (mustered ? "GP_DismissGuards" : "GP_MusterGuards").Translate()))
            {
                if (mustered)
                {
                    Dismiss();
                }
                else
                {
                    Muster();
                }
            }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, "GP_MusterGuardsDesc".Translate());
        }
    }
}
