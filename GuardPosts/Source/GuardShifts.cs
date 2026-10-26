using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace GuardPosts
{
    // Guard shifts: hours, set per guard, when they're on duty whatever their schedule says. When a shift starts they drop
    // what they're doing, gear up in their on-duty apparel and loadout, then hold their post or walk their route.
    // They still eat or sleep if it's urgent. When it ends, they go back to their schedule and duty setting.
    // Musters, militia call-ups and drafting take precedence.
    public static class GuardShifts
    {
        private static WorkGiver_Guard guardGiver;

        private static WorkGiver_Guard GuardGiver => guardGiver ??= new WorkGiver_Guard();

        // The brush painted onto the shift grid: true paints shift hours, false clears them.
        public static bool paintShift = true;

        public static bool IsShiftHour(GuardAssignment assignment, int hour) => (assignment.shiftMask & (1 << hour)) != 0;

        public static void SetShiftHour(GuardAssignment assignment, int hour, bool shift)
        {
            if (shift)
            {
                assignment.shiftMask |= 1 << hour;
            }
            else
            {
                assignment.shiftMask &= ~(1 << hour);
            }
        }

        private static bool ShouldBeOnShift(Pawn pawn, GuardAssignment assignment) =>
            assignment.shiftMask != 0 && MilitiaUtility.CanServe(pawn) && GuardUtility.IsGuard(pawn)
            && IsShiftHour(assignment, GenLocalDate.HourOfDay(pawn));

        public static void Tick()
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            foreach (KeyValuePair<Pawn, GuardAssignment> entry in comp.AllAssignments.ToList())
            {
                Pawn pawn = entry.Key;
                GuardAssignment assignment = entry.Value;
                if (assignment.shiftMask == 0 && !assignment.onShift)
                {
                    continue;
                }
                if (pawn == null || pawn.Dead || pawn.Destroyed)
                {
                    continue;
                }
                bool shouldBe = ShouldBeOnShift(pawn, assignment);
                if (shouldBe && !assignment.onShift)
                {
                    StartShift(pawn, assignment);
                }
                else if (!shouldBe && assignment.onShift)
                {
                    EndShift(pawn, assignment);
                    continue;
                }
                if (!assignment.onShift)
                {
                    continue;
                }
                // Musters, the militia and the player (drafting) take precedence; so do rituals.
                if (!pawn.Spawned || assignment.guardMustered || assignment.militiaCalled || pawn.Drafted || pawn.Downed
                    || pawn.InMentalState || pawn.GetLord() != null)
                {
                    continue;
                }
                if (assignment.shiftGearing)
                {
                    if (!MusterSteps.Step(pawn, ref assignment.shiftGathering, ref assignment.shiftStartTick, ref assignment.shiftJob, gather: false))
                    {
                        continue;
                    }
                    assignment.shiftGearing = false;
                    assignment.shiftGathering = false;
                    assignment.shiftJob = null;
                }
                HoldPost(pawn);
            }
        }

        private static void StartShift(Pawn pawn, GuardAssignment assignment)
        {
            assignment.onShift = true;
            assignment.shiftGearing = true;
            assignment.shiftGathering = false;
            assignment.shiftStartTick = Find.TickManager.TicksGame;
            assignment.shiftJob = null;
            if (!assignment.militiaCalled && !assignment.guardMustered)
            {
                DutyUtility.PutOnDuty(pawn, assignment);
            }
        }

        private static void EndShift(Pawn pawn, GuardAssignment assignment)
        {
            bool busyElsewhere = !pawn.Spawned || assignment.guardMustered || assignment.militiaCalled || pawn.Drafted;
            if (!busyElsewhere && assignment.shiftGearing)
            {
                MusterSteps.Abandon(pawn, assignment.shiftJob);
            }
            assignment.onShift = false;
            assignment.shiftGearing = false;
            assignment.shiftGathering = false;
            assignment.shiftJob = null;
            // Leave the post now, so the think tree picks whatever their schedule says.
            if (!busyElsewhere && (pawn.CurJobDef == GuardDefOf.GP_GuardPost || pawn.CurJobDef == GuardDefOf.GP_Patrol))
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        // Keeps the guard at their post or on their route for the rest of the shift.
        private static void HoldPost(Pawn pawn)
        {
            bool guarding = pawn.CurJobDef == GuardDefOf.GP_GuardPost || pawn.CurJobDef == GuardDefOf.GP_Patrol;
            if (UrgentNeed(pawn))
            {
                // Shift guard jobs don't expire, so step off the post to let the think tree feed or rest them.
                if (guarding)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                return;
            }
            if (guarding)
            {
                return;
            }
            Job guardJob = GuardGiver.ShouldSkip(pawn) ? null : GuardGiver.NonScanJob(pawn);
            if (guardJob != null)
            {
                // Held until the shift ends (or an urgent need), instead of re-checking the schedule every few seconds.
                guardJob.expiryInterval = -1;
                guardJob.checkOverrideOnExpire = false;
                pawn.jobs.StartJob(guardJob, JobCondition.InterruptForced);
            }
        }

        // Guards on shift still eat and sleep when they really must.
        private static bool UrgentNeed(Pawn pawn)
        {
            Need_Food food = pawn.needs?.food;
            Need_Rest rest = pawn.needs?.rest;
            return (food != null && food.CurCategory >= HungerCategory.UrgentlyHungry)
                || (rest != null && rest.CurCategory >= RestCategory.VeryTired);
        }
    }

    public class Window_GuardShifts : Window
    {
        private const float TopBarHeight = 38f;
        private const float BottomHeight = 40f;

        private PawnTable table;
        private List<Pawn> lastPawns = new List<Pawn>();

        public Window_GuardShifts()
        {
            doCloseX = true;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
        }

        private static IEnumerable<Pawn> Pawns =>
            Find.CurrentMap?.mapPawns.FreeColonists.Where(p => GuardUtility.IsGuard(p) && MilitiaUtility.CanServe(p)) ?? Enumerable.Empty<Pawn>();

        public override Vector2 InitialSize
        {
            get
            {
                EnsureTable();
                Vector2 size = table.Size;
                return new Vector2(Mathf.Max(size.x, 640f) + Margin * 2f, Mathf.Max(size.y, 120f) + TopBarHeight + BottomHeight + Margin * 2f);
            }
        }

        private void EnsureTable()
        {
            table ??= new PawnTable_PlayerPawns(GuardDefOf.GP_Shifts, () => Pawns, UI.screenWidth - 60, UI.screenHeight - 200);
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
            Widgets.Label(new Rect(inRect.x, inRect.y, 180f, 34f), "GP_ShiftsTitle".Translate());
            Text.Font = GameFont.Small;
            DrawBrush(new Rect(inRect.x + 190f, inRect.y + 3f, 130f, 28f), true, "GP_ShiftBrushOn".Translate());
            DrawBrush(new Rect(inRect.x + 326f, inRect.y + 3f, 130f, 28f), false, "GP_ShiftBrushOff".Translate());

            table.PawnTableOnGUI(new Vector2(inRect.x, inRect.y + TopBarHeight));

            if (pawns.Count == 0)
            {
                Rect empty = new Rect(inRect.x, inRect.y + TopBarHeight + 40f, inRect.width, 30f);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(empty, "GP_ShiftsNoGuards".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Rect hint = new Rect(inRect.x, inRect.yMax - BottomHeight + 4f, inRect.width, BottomHeight - 4f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Widgets.Label(hint, "GP_ShiftsHint".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawBrush(Rect rect, bool shift, string label)
        {
            Widgets.DrawBoxSolid(rect.ContractedBy(2f), shift ? PawnColumnWorker_GuardShift.ShiftColor : PawnColumnWorker_GuardShift.OffColor);
            if (Widgets.ButtonInvisible(rect))
            {
                GuardShifts.paintShift = shift;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            if (GuardShifts.paintShift == shift)
            {
                Widgets.DrawBox(rect, 2);
            }
        }
    }

    // A 24-hour row painted like the vanilla schedule: click or drag with the selected brush.
    public class PawnColumnWorker_GuardShift : PawnColumnWorker
    {
        public static readonly Color ShiftColor = new Color(0.62f, 0.22f, 0.18f);
        public static readonly Color OffColor = new Color(0.22f, 0.22f, 0.22f);

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            if (assignment == null)
            {
                return;
            }
            float width = rect.width / 24f;
            int nowHour = GenLocalDate.HourOfDay(pawn);
            bool mouseDown = Input.GetMouseButton(0);
            for (int hour = 0; hour < 24; hour++)
            {
                Rect cell = new Rect(rect.x + hour * width, rect.y, width, rect.height).ContractedBy(1f);
                bool shift = GuardShifts.IsShiftHour(assignment, hour);
                Widgets.DrawBoxSolid(cell, shift ? ShiftColor : OffColor);
                if (hour == nowHour)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    Widgets.DrawBox(cell, 1);
                    GUI.color = Color.white;
                }
                if (!mouseDown)
                {
                    MouseoverSounds.DoRegion(cell);
                }
                if (!Mouse.IsOver(cell))
                {
                    continue;
                }
                Widgets.DrawBox(cell, 2);
                if (mouseDown && shift != GuardShifts.paintShift)
                {
                    SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
                    GuardShifts.SetShiftHour(assignment, hour, GuardShifts.paintShift);
                }
            }
        }

        public override void DoHeader(Rect rect, PawnTable table)
        {
            float width = rect.width / 24f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            for (int hour = 0; hour < 24; hour++)
            {
                Widgets.Label(new Rect(rect.x + hour * width, rect.y, width, rect.height + 3f), hour.ToString());
            }
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 360);

        public override int GetOptimalWidth(PawnTable table) => Mathf.Clamp(504, GetMinWidth(table), GetMaxWidth(table));

        public override int GetMaxWidth(PawnTable table) => Mathf.Min(base.GetMaxWidth(table), 600);

        public override int GetMinHeaderHeight(PawnTable table) => Mathf.Max(base.GetMinHeaderHeight(table), 15);

        public override int Compare(Pawn a, Pawn b) => FirstShiftHour(a).CompareTo(FirstShiftHour(b));

        private static int FirstShiftHour(Pawn pawn)
        {
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            if (assignment == null || assignment.shiftMask == 0)
            {
                return 24;
            }
            for (int hour = 0; hour < 24; hour++)
            {
                if (GuardShifts.IsShiftHour(assignment, hour))
                {
                    return hour;
                }
            }
            return 24;
        }
    }

    public class PawnColumnWorker_CopyPasteGuardShift : PawnColumnWorker_CopyPaste
    {
        private static int? clipboard;

        protected override bool AnythingInClipboard => clipboard != null;

        protected override void CopyFrom(Pawn p)
        {
            clipboard = GameComponent_Guards.Instance?.GetAssignment(p).shiftMask;
        }

        protected override void PasteTo(Pawn p)
        {
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(p);
            if (assignment != null && clipboard != null)
            {
                assignment.shiftMask = clipboard.Value;
            }
        }
    }

    public class PawnColumnWorker_ShiftStatus : PawnColumnWorker_Text
    {
        protected override int Width => 140;

        protected override string GetTextFor(Pawn pawn)
        {
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            if (assignment == null || assignment.shiftMask == 0)
            {
                return "GP_ShiftStatusNone".Translate();
            }
            if (!assignment.onShift)
            {
                return "GP_ShiftStatusOff".Translate();
            }
            return (assignment.shiftGearing ? "GP_ShiftStatusGearing" : "GP_ShiftStatusOn").Translate();
        }
    }
}
