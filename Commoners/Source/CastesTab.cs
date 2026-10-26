using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Commoners
{
    // A column add-ons can append to the Castes tab (Wages adds its payroll columns).
    public class CastesTabColumn
    {
        public string label;
        public float width = 70f;
        public Func<Pawn, string> value;
        public Func<Pawn, string> tooltip;
    }

    // A button add-ons can put on the Castes tab, for one pawn's row, a caste's header, or the whole
    // colony (Wages adds "forgive taxes"). It's given the pawns it applies to, and is hidden when its
    // label comes back empty.
    public class CastesTabButton
    {
        public float width = 90f;
        public Func<List<Pawn>, string> label;
        public Func<List<Pawn>, string> tooltip;
        public Action<List<Pawn>> action;
    }

    public static class CastesTabExtensions
    {
        public static readonly List<CastesTabColumn> Columns = new List<CastesTabColumn>();

        // Extra lines shown under the seat count, such as payroll totals.
        public static readonly List<Func<string>> SummaryLines = new List<Func<string>>();

        // Buttons beside each row's "change caste" button, on each caste's header, and by the title.
        public static readonly List<CastesTabButton> RowButtons = new List<CastesTabButton>();
        public static readonly List<CastesTabButton> GroupButtons = new List<CastesTabButton>();
        public static readonly List<CastesTabButton> TopButtons = new List<CastesTabButton>();

        // Draws buttons right to left, ending at `right`. Returns the x the leftmost one starts at.
        public static float DrawButtons(List<CastesTabButton> buttons, List<Pawn> pawns, float right, float y, float height)
        {
            float x = right;
            for (int i = buttons.Count - 1; i >= 0; i--)
            {
                CastesTabButton button = buttons[i];
                string label = button.label?.Invoke(pawns);
                if (label.NullOrEmpty())
                {
                    continue;
                }
                x -= button.width;
                Rect rect = new Rect(x, y, button.width, height);
                string tip = button.tooltip?.Invoke(pawns);
                if (!tip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(rect, tip);
                }
                if (Widgets.ButtonText(rect, label))
                {
                    button.action?.Invoke(pawns);
                }
                x -= 4f;
            }
            return x;
        }
    }

    // Lists every free colonist by caste, highest first, with noble seat counts and a button to change caste.
    public class MainTabWindow_Castes : MainTabWindow
    {
        private const float RowHeight = 30f;
        private const float HeaderHeight = 30f;
        private const float LineHeight = 24f;
        private const float NameWidth = 170f;
        private const float DetailWidth = 150f;
        private const float ButtonWidth = 120f;
        private const float ColumnsStart = 36f + NameWidth + DetailWidth + 10f;

        private Vector2 scrollPos;
        private float viewHeight;

        private static float ColumnsWidth => CastesTabExtensions.Columns.Sum(c => c.width);

        private static float RowButtonsWidth => CastesTabExtensions.RowButtons.Sum(b => b.width + 4f);

        public override Vector2 RequestedTabSize => new Vector2(ColumnsStart + ColumnsWidth + RowButtonsWidth + ButtonWidth + 70f, 680f);

        public override void DoWindowContents(Rect inRect)
        {
            List<Pawn> pawns = CasteUtility.ColonyMembers().ToList();
            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "CM_CastesTitle".Translate());
            Text.Font = GameFont.Small;
            CastesTabExtensions.DrawButtons(CastesTabExtensions.TopButtons, pawns, inRect.xMax - 26f, y + 2f, 28f);
            y += 36f;
            string seats = "CM_CastesSeats".Translate(CasteUtility.NobleSeatsUsed(), CasteUtility.NobleSeats(), pawns.Count, CommonersMod.Settings.colonistsPerNobleSeat);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, LineHeight), seats);
            y += LineHeight;
            foreach (Func<string> line in CastesTabExtensions.SummaryLines)
            {
                string text = line();
                if (!text.NullOrEmpty())
                {
                    Widgets.Label(new Rect(inRect.x, y, inRect.width, LineHeight), text);
                    y += LineHeight;
                }
            }
            y += 6f;
            if (CastesTabExtensions.Columns.Count > 0)
            {
                DrawColumnHeaders(new Rect(inRect.x, y, inRect.width - 16f, LineHeight));
                y += LineHeight;
            }

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(outRect, ref scrollPos, view);
            float rowY = 0f;
            foreach (Caste caste in new[] { Caste.Noble, Caste.Artisan, Caste.Scholar, Caste.Citizen, Caste.Soldier, Caste.Commoner })
            {
                List<Pawn> members = pawns.Where(p => CasteUtility.GetCaste(p) == caste).OrderBy(p => p.LabelShort).ToList();
                rowY = DrawGroup(view.width, rowY, caste, members);
            }
            viewHeight = rowY;
            Widgets.EndScrollView();
        }

        private static void DrawColumnHeaders(Rect rect)
        {
            float x = rect.x + ColumnsStart;
            Text.Anchor = TextAnchor.LowerCenter;
            GUI.color = Color.gray;
            foreach (CastesTabColumn column in CastesTabExtensions.Columns)
            {
                Widgets.Label(new Rect(x, rect.y, column.width, rect.height), column.label);
                x += column.width;
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private float DrawGroup(float width, float y, Caste caste, List<Pawn> members)
        {
            Rect header = new Rect(0f, y, width, HeaderHeight);
            Texture2D icon = CommonersTextures.CasteIcon(caste);
            if (icon != null)
            {
                GUI.DrawTexture(new Rect(header.x, header.y + 3f, 24f, 24f), icon);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(header.x + 30f, header.y, width - 30f, HeaderHeight),
                (CasteUtility.Label(caste).CapitalizeFirst() + " (" + members.Count + ")").Colorize(ColoredText.TipSectionTitleColor));
            Text.Anchor = TextAnchor.UpperLeft;
            CastesTabExtensions.DrawButtons(CastesTabExtensions.GroupButtons, members, header.xMax - 10f, header.y + 3f, HeaderHeight - 6f);
            y += HeaderHeight;
            Widgets.DrawLineHorizontal(0f, y, width);
            y += 2f;
            foreach (Pawn p in members)
            {
                DrawRow(new Rect(0f, y, width, RowHeight), p);
                y += RowHeight;
            }
            return y + 10f;
        }

        private static void DrawRow(Rect row, Pawn p)
        {
            Widgets.DrawHighlightIfMouseover(row);
            Widgets.ThingIcon(new Rect(row.x + 4f, row.y + 2f, 26f, 26f), p);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x + 36f, row.y, NameWidth, RowHeight), p.LabelShort.Truncate(NameWidth));
            string detail = CasteUtility.HasRoyalTitle(p)
                ? p.royalty.MostSeniorTitle?.Label.CapitalizeFirst() ?? ""
                : CasteUtility.Specialty(p)?.LabelCap.ToString() ?? "";
            Widgets.Label(new Rect(row.x + 36f + NameWidth, row.y, DetailWidth, RowHeight), detail.Truncate(DetailWidth));
            float x = row.x + ColumnsStart;
            Text.Anchor = TextAnchor.MiddleCenter;
            foreach (CastesTabColumn column in CastesTabExtensions.Columns)
            {
                Rect cell = new Rect(x, row.y, column.width, RowHeight);
                Widgets.Label(cell, column.value?.Invoke(p) ?? "");
                string tip = column.tooltip?.Invoke(p);
                if (!tip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(cell, tip);
                }
                x += column.width;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Rect button = new Rect(row.xMax - ButtonWidth - 10f, row.y + 3f, ButtonWidth, RowHeight - 6f);
            CastesTabExtensions.DrawButtons(CastesTabExtensions.RowButtons, new List<Pawn> { p }, button.x - 4f, button.y, button.height);
            if (CasteUtility.HasRoyalTitle(p))
            {
                TooltipHandler.TipRegion(button, "CM_CannotTitle".Translate());
            }
            else if (Widgets.ButtonText(button, "CM_CastesChange".Translate()))
            {
                Find.WindowStack.Add(new FloatMenu(CasteMenu.Options(p)));
            }
            if (Widgets.ButtonInvisible(new Rect(row.x, row.y, 36f + NameWidth, RowHeight)))
            {
                CameraJumper.TryJumpAndSelect(p);
            }
        }
    }
}
