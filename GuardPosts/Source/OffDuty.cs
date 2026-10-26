using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace GuardPosts
{
    // Duty is tracked per guard (GuardAssignment.offDuty), so auto duty can follow each guard's schedule.
    // Each guard has an explicit on-duty and off-duty apparel policy and CE loadout (each optional), and the ones for their current duty are kept set.
    public static class DutyUtility
    {
        public static void SetMode(DutyMode mode)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            comp.dutyMode = mode;
            UpdateAll(announce: true);
        }

        // Whether this guard should be off duty now; null leaves them as they are.
        // Auto: on duty in work and "anything" hours, off duty in recreation (or meditation, or modded) hours, unchanged while asleep.
        public static bool? WantsOffDuty(Pawn pawn, DutyMode mode)
        {
            switch (mode)
            {
                case DutyMode.OnDuty:
                    return false;
                case DutyMode.OffDuty:
                    return true;
            }
            TimeAssignmentDef current = pawn.timetable?.CurrentAssignment;
            if (current == null || current == TimeAssignmentDefOf.Sleep)
            {
                return null;
            }
            return current != TimeAssignmentDefOf.Work && current != TimeAssignmentDefOf.Anything;
        }

        public static void UpdateAll(bool announce)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            int toOff = 0;
            int toOn = 0;
            foreach (KeyValuePair<Pawn, GuardAssignment> entry in comp.AllAssignments)
            {
                Pawn pawn = entry.Key;
                GuardAssignment assignment = entry.Value;
                // Only current guards: a colonist taken off guard work keeps their duty choices, but they stop being applied.
                if (pawn == null || pawn.Destroyed || pawn.Dead || assignment.militiaCalled || !GuardUtility.IsGuard(pawn))
                {
                    continue;
                }
                bool? wanted = assignment.guardMustered || assignment.onShift ? false : WantsOffDuty(pawn, comp.dutyMode);
                if (wanted == null || wanted == assignment.offDuty)
                {
                    // No change of duty, but put the right gear back if something else changed it.
                    ApplyApparel(pawn, assignment);
                    ApplyLoadout(pawn, assignment);
                    continue;
                }
                if (SetPawnOffDuty(pawn, assignment, wanted.Value))
                {
                    if (wanted.Value)
                    {
                        toOff++;
                    }
                    else
                    {
                        toOn++;
                    }
                }
            }
            if (announce && toOff > 0)
            {
                Messages.Message("GP_SwitchedOffDuty".Translate(toOff), MessageTypeDefOf.SilentInput, historical: false);
            }
            if (announce && toOn > 0)
            {
                Messages.Message("GP_SwitchedOnDuty".Translate(toOn), MessageTypeDefOf.SilentInput, historical: false);
            }
        }

        public static void PutOnDuty(Pawn pawn, GuardAssignment assignment) => SetPawnOffDuty(pawn, assignment, false);

        // Returns whether any gear choice changed.
        private static bool SetPawnOffDuty(Pawn pawn, GuardAssignment assignment, bool offDuty)
        {
            assignment.offDuty = offDuty;
            return ApplyApparel(pawn, assignment) | ApplyLoadout(pawn, assignment);
        }

        // Changing a choice for the guard's current duty applies it right away.
        public static void SetLoadout(Pawn pawn, bool offDuty, int id)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            GuardAssignment assignment = comp.GetAssignment(pawn);
            if (offDuty)
            {
                assignment.offDutyLoadout = id;
            }
            else
            {
                assignment.onDutyLoadout = id;
            }
            if (!assignment.militiaCalled && GuardUtility.IsGuard(pawn))
            {
                ApplyLoadout(pawn, assignment);
            }
        }

        public static void SetApparel(Pawn pawn, bool offDuty, ApparelPolicy policy)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            GuardAssignment assignment = comp.GetAssignment(pawn);
            if (offDuty)
            {
                assignment.offDutyApparel = policy;
            }
            else
            {
                assignment.onDutyApparel = policy;
            }
            if (!assignment.militiaCalled && GuardUtility.IsGuard(pawn))
            {
                ApplyApparel(pawn, assignment);
            }
        }

        public static bool PolicyExists(ApparelPolicy policy) =>
            policy != null && Current.Game?.outfitDatabase?.AllOutfits.Contains(policy) == true;

        public static bool CanChangeApparel(Pawn pawn) => pawn.outfits != null && !pawn.IsQuestLodger();

        // Sets the apparel policy chosen for the guard's current duty, if one is chosen.
        private static bool ApplyApparel(Pawn pawn, GuardAssignment assignment)
        {
            ApparelPolicy policy = assignment.offDuty ? assignment.offDutyApparel : assignment.onDutyApparel;
            if (!PolicyExists(policy) || !CanChangeApparel(pawn) || pawn.outfits.CurrentApparelPolicy == policy)
            {
                return false;
            }
            pawn.outfits.CurrentApparelPolicy = policy;
            return true;
        }

        // Sets the CE loadout chosen for the guard's current duty, if one is chosen.
        private static bool ApplyLoadout(Pawn pawn, GuardAssignment assignment)
        {
            int id = assignment.offDuty ? assignment.offDutyLoadout : assignment.onDutyLoadout;
            if (!CeLoadouts.Exists(id) || CeLoadouts.Get(pawn) == id)
            {
                return false;
            }
            return CeLoadouts.Set(pawn, id);
        }
    }

    // A dropdown of on- or off-duty gear. A null label means "keep current": the gear isn't changed for that duty.
    // The column matching the guard's current duty is highlighted.
    public abstract class PawnColumnWorker_DutyChoice : PawnColumnWorker
    {
        private static readonly Color ActiveBackground = new Color(1f, 1f, 1f, 0.08f);

        // Whether this choice is the one in use right now.
        protected abstract bool InUse(GuardAssignment assignment);

        protected virtual bool Available(Pawn pawn) => true;

        protected abstract string ChoiceLabel(GuardAssignment assignment);

        protected abstract IEnumerable<(string label, Action select)> Choices(Pawn pawn);

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null || !Available(pawn) || pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return;
            }
            GuardAssignment assignment = comp.GetAssignment(pawn);
            string label = ChoiceLabel(assignment);
            Rect button = new Rect(rect.x, rect.y + 2f, rect.width - 4f, rect.height - 4f);
            if (InUse(assignment))
            {
                Widgets.DrawBoxSolid(rect, ActiveBackground);
            }
            if (label == null)
            {
                GUI.color = Color.gray;
            }
            if (Widgets.ButtonText(button, (label ?? "GP_OffDutyNone".Translate().ToString()).Truncate(button.width - 12f)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach ((string choiceLabel, Action select) in Choices(pawn))
                {
                    options.Add(new FloatMenuOption(choiceLabel, select));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            GUI.color = Color.white;
        }

        public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 140);

        public override int GetOptimalWidth(PawnTable table) => Mathf.Clamp(170, GetMinWidth(table), GetMaxWidth(table));

        public override int Compare(Pawn a, Pawn b)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return 0;
            }
            return string.Compare(ChoiceLabel(comp.GetAssignment(a)), ChoiceLabel(comp.GetAssignment(b)), StringComparison.OrdinalIgnoreCase);
        }
    }

    public abstract class PawnColumnWorker_DutyLoadout : PawnColumnWorker_DutyChoice
    {
        protected abstract bool ForOffDuty { get; }

        protected override bool InUse(GuardAssignment assignment) => assignment.offDuty == ForOffDuty && !assignment.militiaCalled;

        protected override bool Available(Pawn pawn) => CeLoadouts.Active;

        protected override string ChoiceLabel(GuardAssignment assignment) =>
            CeLoadouts.Label(ForOffDuty ? assignment.offDutyLoadout : assignment.onDutyLoadout);

        protected override IEnumerable<(string label, Action select)> Choices(Pawn pawn)
        {
            bool offDuty = ForOffDuty;
            yield return ("GP_OffDutyNone".Translate(), () => DutyUtility.SetLoadout(pawn, offDuty, -1));
            foreach ((int id, string label) loadout in CeLoadouts.All())
            {
                int id = loadout.id;
                yield return (loadout.label, () => DutyUtility.SetLoadout(pawn, offDuty, id));
            }
        }
    }

    public class PawnColumnWorker_OnDutyLoadout : PawnColumnWorker_DutyLoadout
    {
        protected override bool ForOffDuty => false;
    }

    public class PawnColumnWorker_OffDutyLoadout : PawnColumnWorker_DutyLoadout
    {
        protected override bool ForOffDuty => true;
    }

    public abstract class PawnColumnWorker_DutyApparel : PawnColumnWorker_DutyChoice
    {
        protected abstract bool ForOffDuty { get; }

        protected override bool InUse(GuardAssignment assignment) => assignment.offDuty == ForOffDuty && !assignment.militiaCalled;

        protected override bool Available(Pawn pawn) => DutyUtility.CanChangeApparel(pawn);

        private ApparelPolicy Policy(GuardAssignment assignment) => ForOffDuty ? assignment.offDutyApparel : assignment.onDutyApparel;

        protected override string ChoiceLabel(GuardAssignment assignment)
        {
            ApparelPolicy policy = Policy(assignment);
            return DutyUtility.PolicyExists(policy) ? policy.label : null;
        }

        protected override IEnumerable<(string label, Action select)> Choices(Pawn pawn)
        {
            bool offDuty = ForOffDuty;
            yield return ("GP_OffDutyNone".Translate(), () => DutyUtility.SetApparel(pawn, offDuty, null));
            foreach (ApparelPolicy policy in Current.Game.outfitDatabase.AllOutfits)
            {
                ApparelPolicy p = policy;
                yield return (policy.label, () => DutyUtility.SetApparel(pawn, offDuty, p));
            }
        }
    }

    public class PawnColumnWorker_OnDutyApparel : PawnColumnWorker_DutyApparel
    {
        protected override bool ForOffDuty => false;
    }

    public class PawnColumnWorker_OffDutyApparel : PawnColumnWorker_DutyApparel
    {
        protected override bool ForOffDuty => true;
    }
}
