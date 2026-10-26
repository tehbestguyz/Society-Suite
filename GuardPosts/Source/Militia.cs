using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace GuardPosts
{
    // Calling up the militia switches members to their militia apparel policy and CE loadout, then the muster
    // hands out gear jobs until there's nothing left to fetch and drafts them. Standing down undrafts them and restores their gear.
    public static class MilitiaUtility
    {

        private static JobGiver_OptimizeApparel fallbackApparelGiver;

        public static bool CanServe(Pawn pawn) => pawn != null && !pawn.Dead && !pawn.Destroyed && !pawn.WorkTagIsDisabled(WorkTags.Violent);

        // Guards have their own muster, so the militia leaves them out. Membership is kept in case they stop being guards.
        public static bool IsActiveMember(Pawn pawn, GuardAssignment assignment) =>
            assignment.militia && CanServe(pawn) && !GuardUtility.IsGuard(pawn);

        public static void Call()
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            List<Pawn> called = new List<Pawn>();
            foreach (KeyValuePair<Pawn, GuardAssignment> entry in comp.AllAssignments.ToList())
            {
                if (IsActiveMember(entry.Key, entry.Value))
                {
                    CallUp(entry.Key, entry.Value);
                    called.Add(entry.Key);
                }
            }
            if (called.Count == 0)
            {
                Messages.Message("GP_MilitiaNoMembers".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            comp.militiaActive = true;
            Messages.Message("GP_MilitiaCalled".Translate(called.Count), new LookTargets(called), MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public static void StandDown()
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            comp.militiaActive = false;
            foreach (KeyValuePair<Pawn, GuardAssignment> entry in comp.AllAssignments.ToList())
            {
                if (entry.Value.militiaCalled)
                {
                    Release(entry.Key, entry.Value);
                }
            }
            Messages.Message("GP_MilitiaStoodDown".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
        }

        // Joining or leaving while the militia is called up takes effect right away.
        public static void SetMember(Pawn pawn, bool member)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            GuardAssignment assignment = comp.GetAssignment(pawn);
            assignment.militia = member && CanServe(pawn);
            if (!comp.militiaActive)
            {
                return;
            }
            if (assignment.militia)
            {
                CallUp(pawn, assignment);
            }
            else if (assignment.militiaCalled)
            {
                Release(pawn, assignment);
            }
        }

        // Changing a called-up member's militia gear applies it and sends them to gear up again.
        public static void SetApparel(Pawn pawn, ApparelPolicy policy)
        {
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            if (assignment == null)
            {
                return;
            }
            assignment.militiaApparel = policy;
            if (assignment.militiaCalled && DutyUtility.PolicyExists(policy) && DutyUtility.CanChangeApparel(pawn))
            {
                pawn.outfits.CurrentApparelPolicy = policy;
                Remuster(assignment);
            }
        }

        public static void SetLoadout(Pawn pawn, int id)
        {
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            if (assignment == null)
            {
                return;
            }
            assignment.militiaLoadout = id;
            if (assignment.militiaCalled && CeLoadouts.Exists(id))
            {
                if (assignment.preMilitiaLoadout < 0)
                {
                    assignment.preMilitiaLoadout = CeLoadouts.Get(pawn);
                }
                CeLoadouts.Set(pawn, id);
                Remuster(assignment);
            }
        }

        // Still mustering: gear up again for the new gear. Already drafted members stay drafted and pick it up once undrafted.
        private static void Remuster(GuardAssignment assignment)
        {
            if (assignment.mustering)
            {
                assignment.musterGathering = false;
                assignment.musterStartTick = Find.TickManager.TicksGame;
            }
        }

        private static void CallUp(Pawn pawn, GuardAssignment assignment)
        {
            if (assignment.militiaCalled)
            {
                return;
            }
            assignment.militiaCalled = true;
            assignment.militiaForcedBefore = MusterSteps.ForcedApparelIds(pawn);
            if (DutyUtility.PolicyExists(assignment.militiaApparel) && DutyUtility.CanChangeApparel(pawn))
            {
                assignment.preMilitiaApparel = pawn.outfits.CurrentApparelPolicy;
                pawn.outfits.CurrentApparelPolicy = assignment.militiaApparel;
            }
            if (CeLoadouts.Exists(assignment.militiaLoadout))
            {
                assignment.preMilitiaLoadout = CeLoadouts.Get(pawn);
                CeLoadouts.Set(pawn, assignment.militiaLoadout);
            }
            assignment.mustering = true;
            assignment.musterGathering = false;
            assignment.musterStartTick = Find.TickManager.TicksGame;
            assignment.musterJob = null;
        }

        private static void Release(Pawn pawn, GuardAssignment assignment)
        {
            if (assignment.mustering)
            {
                MusterSteps.Abandon(pawn, assignment.musterJob);
            }
            assignment.militiaCalled = false;
            assignment.mustering = false;
            assignment.musterGathering = false;
            assignment.musterJob = null;
            if (DutyUtility.PolicyExists(assignment.preMilitiaApparel) && DutyUtility.CanChangeApparel(pawn))
            {
                pawn.outfits.CurrentApparelPolicy = assignment.preMilitiaApparel;
            }
            assignment.preMilitiaApparel = null;
            if (assignment.preMilitiaLoadout >= 0)
            {
                CeLoadouts.Set(pawn, assignment.preMilitiaLoadout);
            }
            assignment.preMilitiaLoadout = -1;
            MusterSteps.UnforceMusterApparel(pawn, assignment.militiaForcedBefore);
            assignment.militiaForcedBefore = null;
            if (pawn.Spawned && pawn.drafter != null && pawn.drafter.Drafted)
            {
                pawn.drafter.Drafted = false;
            }
        }

        public static void MusterTick()
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            List<Pawn> drafted = null;
            foreach (KeyValuePair<Pawn, GuardAssignment> entry in comp.AllAssignments)
            {
                Pawn pawn = entry.Key;
                GuardAssignment assignment = entry.Value;
                if (!assignment.mustering)
                {
                    continue;
                }
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    assignment.mustering = false;
                    continue;
                }
                // Away (in a caravan, say): they keep their militia gear and muster once they're back on a map.
                if (!pawn.Spawned || pawn.drafter == null)
                {
                    assignment.musterStartTick = now;
                    continue;
                }
                if (pawn.Drafted)
                {
                    assignment.mustering = false;
                    assignment.musterJob = null;
                    continue;
                }
                if (pawn.Downed || pawn.InMentalState)
                {
                    continue;
                }
                // Gear up, gather at the muster spot (if there is one), then draft.
                if (!MusterSteps.Step(pawn, ref assignment.musterGathering, ref assignment.musterStartTick, ref assignment.musterJob, gather: true))
                {
                    continue;
                }
                assignment.mustering = false;
                assignment.musterGathering = false;
                assignment.musterJob = null;
                pawn.drafter.Drafted = true;
                (drafted ??= new List<Pawn>()).Add(pawn);
            }
            if (drafted != null)
            {
                Messages.Message("GP_MilitiaDrafted".Translate(drafted.Count), new LookTargets(drafted), MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        // The next job to get the pawn into their loadout and apparel policy, or null when they're done.
        public static Job NextGearJob(Pawn pawn)
        {
            Job job = CeLoadouts.UpdateLoadoutJob(pawn);
            if (job != null)
            {
                return job;
            }
            // CE found nothing within its search range; if the loadout still needs something further away, walk over to it.
            Thing farItem = CeLoadouts.FarLoadoutItem(pawn);
            if (farItem != null)
            {
                IntVec3 near = CellFinder.RandomClosewalkCellNear(farItem.Position, farItem.Map, 6, c => pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly));
                Job walk = JobMaker.MakeJob(JobDefOf.Goto, near);
                walk.locomotionUrgency = LocomotionUrgency.Jog;
                return walk;
            }
            if (!DutyUtility.CanChangeApparel(pawn) || pawn.apparel == null || pawn.Faction != Faction.OfPlayer)
            {
                return null;
            }
            // Vanilla only re-checks apparel every few hours; make it look now.
            pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame;
            ThinkNode_JobGiver giver = pawn.thinker?.TryGetMainTreeThinkNode<JobGiver_OptimizeApparel>();
            giver ??= fallbackApparelGiver ??= new JobGiver_OptimizeApparel();
            return giver.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
        }

        public static string StatusLabel(Pawn pawn, GuardAssignment assignment)
        {
            if (!assignment.militia)
            {
                return null;
            }
            if (!assignment.militiaCalled)
            {
                return "GP_MilitiaStatusStandingBy".Translate();
            }
            if (!pawn.Spawned)
            {
                return "GP_MilitiaStatusAway".Translate();
            }
            if (assignment.mustering)
            {
                return (assignment.musterGathering ? "GP_MilitiaStatusGathering" : "GP_MilitiaStatusGearing").Translate();
            }
            return pawn.Drafted ? "GP_MilitiaStatusDrafted".Translate() : "GP_MilitiaStatusCalled".Translate();
        }

        public static int MemberCount() =>
            GameComponent_Guards.Instance?.AllAssignments.Count(e => IsActiveMember(e.Key, e.Value)) ?? 0;

        // The call-up / stand-down button, shared by the Guards tab and the Militia window.
        public static void DoCallButton(Rect rect)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            bool active = comp.militiaActive;
            GUI.color = active ? new Color(1f, 0.55f, 0.45f) : Color.white;
            if (Widgets.ButtonText(rect, (active ? "GP_StandDownMilitia" : "GP_CallMilitia").Translate()))
            {
                if (active)
                {
                    StandDown();
                }
                else
                {
                    Call();
                }
            }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(rect, "GP_CallMilitiaDesc".Translate());
        }
    }

    public class Window_Militia : Window
    {
        private const float TopBarHeight = 38f;
        private const float BottomHeight = 40f;

        private PawnTable table;
        private List<Pawn> lastPawns = new List<Pawn>();

        public Window_Militia()
        {
            doCloseX = true;
            doCloseButton = false;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
        }

        private static IEnumerable<Pawn> Pawns =>
            Find.CurrentMap?.mapPawns.FreeColonists.Where(p => !p.DevelopmentalStage.Baby() && !GuardUtility.IsGuard(p)) ?? Enumerable.Empty<Pawn>();

        public override Vector2 InitialSize
        {
            get
            {
                EnsureTable();
                Vector2 size = table.Size;
                return new Vector2(Mathf.Max(size.x, 640f) + Margin * 2f, size.y + TopBarHeight + BottomHeight + Margin * 2f);
            }
        }

        private void EnsureTable()
        {
            table ??= new PawnTable_PlayerPawns(GuardDefOf.GP_Militia, () => Pawns, UI.screenWidth - 60, UI.screenHeight - 200);
        }

        public override void PostOpen()
        {
            base.PostOpen();
            EnsureTable();
            table.SetDirty();
            SetInitialSizeAndPosition();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (GameComponent_Guards.Instance == null || Find.CurrentMap == null)
            {
                Close();
                return;
            }
            EnsureTable();
            List<Pawn> pawns = Pawns.ToList();
            if (!pawns.SequenceEqual(lastPawns))
            {
                lastPawns = pawns;
                table.SetDirty();
                SetInitialSizeAndPosition();
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, 160f, 34f), "GP_MilitiaTitle".Translate());
            Text.Font = GameFont.Small;
            MilitiaUtility.DoCallButton(new Rect(inRect.x + 170f, inRect.y + 2f, 140f, 28f));
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x + 326f, inRect.y, 200f, 32f), "GP_MilitiaMembers".Translate(MilitiaUtility.MemberCount()));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            table.PawnTableOnGUI(new Vector2(inRect.x, inRect.y + TopBarHeight));

            Rect hint = new Rect(inRect.x, inRect.yMax - BottomHeight + 4f, inRect.width, BottomHeight - 4f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Widgets.Label(hint, "GP_MilitiaHint".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }

    public class PawnColumnWorker_MilitiaMember : PawnColumnWorker_Checkbox
    {
        protected override bool HasCheckbox(Pawn pawn) => MilitiaUtility.CanServe(pawn);

        protected override bool GetValue(Pawn pawn) => GameComponent_Guards.Instance?.GetAssignment(pawn).militia ?? false;

        protected override void SetValue(Pawn pawn, bool value, PawnTable table) => MilitiaUtility.SetMember(pawn, value);

        protected override string GetTip(Pawn pawn) => "GP_MilitiaMemberTip".Translate();
    }

    public class PawnColumnWorker_Weapon : PawnColumnWorker_Text
    {
        protected override int Width => 170;

        protected override string GetTextFor(Pawn pawn)
        {
            ThingWithComps weapon = pawn.equipment?.Primary;
            return weapon != null ? weapon.LabelCap : "GP_NoWeapon".Translate().ToString();
        }

        protected override string GetTip(Pawn pawn) => GetTextFor(pawn);

        public override int Compare(Pawn a, Pawn b) => string.Compare(GetTextFor(a), GetTextFor(b), StringComparison.OrdinalIgnoreCase);
    }

    public class PawnColumnWorker_MilitiaStatus : PawnColumnWorker_Text
    {
        protected override int Width => 130;

        protected override string GetTextFor(Pawn pawn)
        {
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            return assignment == null ? null : MilitiaUtility.StatusLabel(pawn, assignment);
        }
    }

    public class PawnColumnWorker_MilitiaApparel : PawnColumnWorker_DutyChoice
    {
        protected override bool InUse(GuardAssignment assignment) => assignment.militiaCalled;

        protected override bool Available(Pawn pawn) => DutyUtility.CanChangeApparel(pawn);

        protected override string ChoiceLabel(GuardAssignment assignment) =>
            DutyUtility.PolicyExists(assignment.militiaApparel) ? assignment.militiaApparel.label : null;

        protected override IEnumerable<(string label, Action select)> Choices(Pawn pawn)
        {
            yield return ("GP_OffDutyNone".Translate(), () => MilitiaUtility.SetApparel(pawn, null));
            foreach (ApparelPolicy policy in Current.Game.outfitDatabase.AllOutfits)
            {
                ApparelPolicy p = policy;
                yield return (policy.label, () => MilitiaUtility.SetApparel(pawn, p));
            }
        }
    }

    public class PawnColumnWorker_MilitiaLoadout : PawnColumnWorker_DutyChoice
    {
        protected override bool InUse(GuardAssignment assignment) => assignment.militiaCalled;

        protected override bool Available(Pawn pawn) => CeLoadouts.Active;

        protected override string ChoiceLabel(GuardAssignment assignment) => CeLoadouts.Label(assignment.militiaLoadout);

        protected override IEnumerable<(string label, Action select)> Choices(Pawn pawn)
        {
            yield return ("GP_OffDutyNone".Translate(), () => MilitiaUtility.SetLoadout(pawn, -1));
            foreach ((int id, string label) loadout in CeLoadouts.All())
            {
                int id = loadout.id;
                yield return (loadout.label, () => MilitiaUtility.SetLoadout(pawn, id));
            }
        }
    }
}
