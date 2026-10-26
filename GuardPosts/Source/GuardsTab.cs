using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace GuardPosts
{
    public class MainTabWindow_Guards : MainTabWindow_PawnTable
    {
        private const float TopBarHeight = 34f;

        private List<Pawn> lastPawns = new List<Pawn>();

        protected override PawnTableDef PawnTableDef => GuardDefOf.GP_Guards;

        protected override float ExtraTopSpace => TopBarHeight;

        protected override float ExtraBottomSpace => 34f;

        protected override IEnumerable<Pawn> Pawns
        {
            get
            {
                bool onlyGuards = GameComponent_Guards.Instance?.onlyShowGuards ?? true;
                return Find.CurrentMap.mapPawns.FreeColonists
                    .Where(p => !p.DevelopmentalStage.Baby() && (!onlyGuards || GuardUtility.IsGuard(p)));
            }
        }

        public override void DoWindowContents(Rect rect)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null || Find.CurrentMap == null)
            {
                return;
            }

            // Guard work toggled elsewhere (or from this table) changes who's listed.
            List<Pawn> pawns = Pawns.ToList();
            if (!pawns.SequenceEqual(lastPawns))
            {
                lastPawns = pawns;
                SetDirty();
            }

            Rect bar = new Rect(rect.x, rect.y, rect.width, TopBarHeight - 4f);
            float x = bar.x;

            Rect dutyRect = new Rect(x, bar.y + 2f, 120f, 26f);
            GUI.color = DutyColor(comp.dutyMode);
            if (Widgets.ButtonText(dutyRect, DutyLabel(comp.dutyMode)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (DutyMode mode in new[] { DutyMode.OnDuty, DutyMode.OffDuty, DutyMode.Auto })
                {
                    DutyMode m = mode;
                    options.Add(new FloatMenuOption(DutyLabel(mode), () => DutyUtility.SetMode(m)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(dutyRect, "GP_DutyDesc".Translate());
            x += dutyRect.width + 6f;

            Rect musterRect = new Rect(x, bar.y + 2f, 130f, 26f);
            GuardMuster.DoMusterButton(musterRect);
            x += musterRect.width + 6f;

            Rect callRect = new Rect(x, bar.y + 2f, 130f, 26f);
            MilitiaUtility.DoCallButton(callRect);
            x += callRect.width + 6f;

            Rect shiftsRect = new Rect(x, bar.y + 2f, 90f, 26f);
            if (Widgets.ButtonText(shiftsRect, "GP_ShiftsButton".Translate()))
            {
                Window_GuardShifts open = Find.WindowStack.WindowOfType<Window_GuardShifts>();
                if (open != null)
                {
                    open.Close();
                }
                else
                {
                    Find.WindowStack.Add(new Window_GuardShifts());
                }
            }
            TooltipHandler.TipRegion(shiftsRect, "GP_ShiftsButtonDesc".Translate());
            x += shiftsRect.width + 6f;

            Rect militiaRect = new Rect(x, bar.y + 2f, 90f, 26f);
            if (Widgets.ButtonText(militiaRect, "GP_MilitiaButton".Translate()))
            {
                Window_Militia open = Find.WindowStack.WindowOfType<Window_Militia>();
                if (open != null)
                {
                    open.Close();
                }
                else
                {
                    Find.WindowStack.Add(new Window_Militia());
                }
            }
            TooltipHandler.TipRegion(militiaRect, "GP_MilitiaButtonDesc".Translate());
            x += militiaRect.width + 16f;

            bool visible = comp.spotsVisible;
            Rect visRect = new Rect(x, bar.y + 3f, 190f, 24f);
            Widgets.CheckboxLabeled(visRect, "GP_ShowSpots".Translate(), ref visible, placeCheckboxNearText: true);
            TooltipHandler.TipRegion(visRect, "GP_ShowSpotsDesc".Translate());
            comp.spotsVisible = visible;
            x += visRect.width + 16f;

            bool onlyGuards = comp.onlyShowGuards;
            Rect onlyRect = new Rect(x, bar.y + 3f, 190f, 24f);
            Widgets.CheckboxLabeled(onlyRect, "GP_OnlyShowGuards".Translate(), ref onlyGuards, placeCheckboxNearText: true);
            TooltipHandler.TipRegion(onlyRect, "GP_OnlyShowGuardsDesc".Translate());
            if (onlyGuards != comp.onlyShowGuards)
            {
                comp.onlyShowGuards = onlyGuards;
                SetDirty();
            }
            x += onlyRect.width + 16f;

            DrawSpotCounts(new Rect(x, bar.y, bar.xMax - x, bar.height), Find.CurrentMap);

            base.DoWindowContents(rect);

            if (pawns.Count == 0)
            {
                Rect empty = new Rect(rect.x, rect.y + TopBarHeight + 40f, rect.width, 30f);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(empty, "GP_NoGuards".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Rect hint = new Rect(rect.x, rect.yMax - 28f, rect.width, 26f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.gray;
            Widgets.Label(hint, "GP_TabHint".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static string DutyLabel(DutyMode mode)
        {
            switch (mode)
            {
                case DutyMode.OffDuty:
                    return "GP_OffDuty".Translate();
                case DutyMode.Auto:
                    return "GP_AutoDuty".Translate();
                default:
                    return "GP_OnDuty".Translate();
            }
        }

        private static Color DutyColor(DutyMode mode)
        {
            switch (mode)
            {
                case DutyMode.OffDuty:
                    return new Color(0.6f, 0.85f, 1f);
                case DutyMode.Auto:
                    return new Color(1f, 0.9f, 0.55f);
                default:
                    return new Color(1f, 0.6f, 0.55f);
            }
        }

        private static void DrawSpotCounts(Rect rect, Map map)
        {
            List<Building_GuardSpot> spots = GuardUtility.AllSpots(map).ToList();
            float x = rect.x;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            foreach (GuardColor color in GuardUtility.AllColors)
            {
                int posts = spots.Count(s => s.SpotColor == color && s.Kind == GuardSpotKind.Guard);
                int stops = spots.Count(s => s.SpotColor == color && s.Kind == GuardSpotKind.Patrol);
                Rect swatch = new Rect(x, rect.y + (rect.height - 12f) / 2f, 12f, 12f);
                Widgets.DrawBoxSolid(swatch, GuardUtility.ColorOf(color));
                Rect label = new Rect(swatch.xMax + 4f, rect.y, 70f, rect.height);
                Widgets.Label(label, posts + " / " + stops);
                TooltipHandler.TipRegion(new Rect(swatch.x, rect.y, 86f, rect.height),
                    "GP_SpotCountTip".Translate(GuardUtility.ColorLabel(color), posts, stops));
                x += 90f;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }

    public class PawnColumnWorker_GuardAssignment : PawnColumnWorker
    {
        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return;
            }
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.gray;
                Widgets.Label(rect, "GP_IncapableOfGuarding".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            GuardAssignment assignment = comp.GetAssignment(pawn);
            Rect button = new Rect(rect.x, rect.y + 2f, rect.width - 4f, rect.height - 4f);
            Rect swatch = new Rect(button.x + 6f, button.y + (button.height - 12f) / 2f, 12f, 12f);
            if (Widgets.ButtonText(button, "      " + assignment.Label, drawBackground: true, doMouseoverSound: true, active: true))
            {
                Find.WindowStack.Add(new FloatMenu(Options(pawn, assignment)));
            }
            if (assignment.mode == GuardMode.AnyPost)
            {
                Widgets.DrawBoxSolid(swatch, Color.white);
            }
            else
            {
                Widgets.DrawBoxSolid(swatch, GuardUtility.ColorOf(assignment.color));
            }
        }

        private static List<FloatMenuOption> Options(Pawn pawn, GuardAssignment assignment)
        {
            Map map = pawn.MapHeld ?? Find.CurrentMap;
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("GP_AssignAnyPost".Translate(), () => assignment.Set(GuardMode.AnyPost, assignment.color),
                    GuardTextures.GuardSpot, Color.white)
            };
            foreach (GuardColor color in GuardUtility.AllColors)
            {
                int posts = map == null ? 0 : GuardUtility.GuardPosts(map, color).Count();
                GuardColor c = color;
                options.Add(new FloatMenuOption(
                    "GP_AssignPost".Translate(GuardUtility.ColorLabel(color)) + " (" + posts + ")",
                    () => assignment.Set(GuardMode.Post, c),
                    GuardTextures.GuardSpot, GuardUtility.ColorOf(color)));
            }
            foreach (GuardColor color in GuardUtility.AllColors)
            {
                int stops = map == null ? 0 : GuardUtility.PatrolRoute(map, color).Count;
                GuardColor c = color;
                options.Add(new FloatMenuOption(
                    "GP_AssignPatrol".Translate(GuardUtility.ColorLabel(color)) + " (" + stops + ")",
                    () => assignment.Set(GuardMode.Patrol, c),
                    GuardTextures.PatrolSpot, GuardUtility.ColorOf(color)));
            }
            return options;
        }

        public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 200);

        public override int GetOptimalWidth(PawnTable table) => Mathf.Clamp(230, GetMinWidth(table), GetMaxWidth(table));

        public override int Compare(Pawn a, Pawn b) => SortKey(a).CompareTo(SortKey(b));

        private static int SortKey(Pawn pawn)
        {
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            if (assignment == null)
            {
                return 0;
            }
            return (int)assignment.mode * 10 + (int)assignment.color;
        }
    }

    public class PawnColumnWorker_GuardStatus : PawnColumnWorker_Text
    {
        protected override int Width => 240;

        protected override string GetTextFor(Pawn pawn)
        {
            string text = ActivityText(pawn);
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            if (text == null || assignment == null)
            {
                return text;
            }
            if (assignment.onShift && !assignment.guardMustered)
            {
                return (assignment.shiftGearing ? "GP_StatusShiftGearing" : "GP_StatusOnShift").Translate(text);
            }
            if (assignment.guardMustered)
            {
                string key = !assignment.guardGearing ? "GP_StatusMustered" : assignment.guardGathering ? "GP_StatusMusterGathering" : "GP_StatusMusterGearing";
                return key.Translate(text);
            }
            return assignment.offDuty ? "GP_StatusOffDuty".Translate(text) : text;
        }

        private static string ActivityText(Pawn pawn)
        {
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return null;
            }
            if (!GuardUtility.IsGuard(pawn))
            {
                return "GP_StatusNotGuard".Translate();
            }
            GuardAssignment assignment = GameComponent_Guards.Instance?.GetAssignment(pawn);
            Map map = pawn.MapHeld;
            if (assignment != null && map != null && pawn.CurJobDef != GuardDefOf.GP_GuardPost && pawn.CurJobDef != GuardDefOf.GP_Patrol)
            {
                // Explain why a guard might be idle at their assignment.
                if (assignment.mode == GuardMode.Post && !GuardUtility.GuardPosts(map, assignment.color).Any())
                {
                    return "GP_StatusNoPosts".Translate(GuardUtility.ColorLabel(assignment.color));
                }
                if (assignment.mode == GuardMode.Patrol && GuardUtility.PatrolRoute(map, assignment.color).Count == 0)
                {
                    return "GP_StatusNoRoute".Translate(GuardUtility.ColorLabel(assignment.color));
                }
                if (assignment.mode == GuardMode.AnyPost && !GuardUtility.GuardPosts(map, null).Any())
                {
                    return "GP_StatusNoAnyPosts".Translate();
                }
            }
            string report = pawn.jobs?.curDriver?.GetReport();
            return report.NullOrEmpty() ? "-" : report.CapitalizeFirst();
        }

        protected override string GetTip(Pawn pawn) => GetTextFor(pawn);
    }
}
