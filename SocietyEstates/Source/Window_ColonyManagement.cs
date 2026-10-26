using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SocietyEstates
{
    // Opened from the Castes tab. Two tabs: Colony needs, the items royals keep stocked by buying from
    // visiting merchants, and Colony sells, the items they sell off above a level. Both tabs are drawn
    // by the same code; `selling` says which list it's working on.
    public class Window_ColonyManagement : Window
    {
        private enum Tab
        {
            Needs,
            Sells
        }

        private const float RowHeight = 28f;
        private const float SearchWidth = 300f;

        private Tab tab = Tab.Needs;
        private string search = "";
        private Vector2 searchScroll;
        private Vector2 needsScroll;
        private Vector2 salesScroll;
        private string reserveBuffer;
        private readonly Dictionary<ColonyNeed, string> keepBuffers = new Dictionary<ColonyNeed, string>();
        private readonly Dictionary<ColonyNeed, string> capBuffers = new Dictionary<ColonyNeed, string>();

        // The sells tab's "only items we have" toggle. Static, so it's remembered while the game runs.
        private static bool onlyHeld = true;
        // What the colony has, by item, for that toggle. Counting means looking at every item on the
        // map, so it's refreshed every 120 draws (a couple of seconds) rather than every frame.
        private Dictionary<ThingDef, int> held;
        private int drawsSinceCount;

        public override Vector2 InitialSize => new Vector2(1000f, 700f);

        public Window_ColonyManagement()
        {
            doCloseX = true;
            draggable = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
        }

        private static Map CurrentMap => Find.CurrentMap ?? Find.AnyPlayerHomeMap;

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "SE_ColonyManagement".Translate());
            Text.Font = GameFont.Small;
            Rect body = new Rect(inRect.x, inRect.y + 40f + TabDrawer.TabHeight, inRect.width, inRect.height - 40f - TabDrawer.TabHeight);
            Widgets.DrawMenuSection(body);
            List<TabRecord> tabs = new List<TabRecord>
            {
                new TabRecord("SE_TabNeeds".Translate(), () => SwitchTo(Tab.Needs), tab == Tab.Needs),
                new TabRecord("SE_TabSells".Translate(), () => SwitchTo(Tab.Sells), tab == Tab.Sells)
            };
            TabDrawer.DrawTabs(body, tabs);
            ColonyNeeds needs = ColonyNeeds.Get();
            if (needs == null)
            {
                return;
            }
            DrawListTab(body.ContractedBy(10f), needs, selling: tab == Tab.Sells);
        }

        // The search results are a different list on each tab, so start them from the top.
        private void SwitchTo(Tab newTab)
        {
            if (tab != newTab)
            {
                tab = newTab;
                searchScroll = Vector2.zero;
            }
        }

        private void DrawListTab(Rect rect, ColonyNeeds needs, bool selling)
        {
            DrawSearch(new Rect(rect.x, rect.y, SearchWidth, rect.height), needs, selling);
            Rect right = new Rect(rect.x + SearchWidth + 16f, rect.y, rect.width - SearchWidth - 16f, rect.height);
            DrawNeedsList(right, needs, selling);
        }

        // The search box and every item matching it: what a merchant could sell you on the needs tab,
        // what you could sell a merchant on the sells tab. Clicking one adds it to that tab's list.
        private void DrawSearch(Rect rect, ColonyNeeds needs, bool selling)
        {
            List<ThingDef> source = selling ? NeedsUtility.SellableDefs : NeedsUtility.BuyableDefs;
            Rect box = new Rect(rect.x, rect.y, rect.width, 30f);
            search = Widgets.TextField(box, search);
            if (search.NullOrEmpty())
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(box.x + 6f, box.y + 4f, box.width, box.height), "SE_SearchHint".Translate());
                GUI.color = Color.white;
            }
            float top = 36f;
            bool filterHeld = false;
            if (selling)
            {
                Widgets.CheckboxLabeled(new Rect(rect.x, rect.y + top, rect.width, 26f), "SE_OnlyHeld".Translate(), ref onlyHeld);
                TooltipHandler.TipRegion(new Rect(rect.x, rect.y + top, rect.width, 26f), "SE_OnlyHeldTip".Translate());
                top += 30f;
                filterHeld = onlyHeld;
                if (held == null || ++drawsSinceCount >= 120)
                {
                    held = NeedsUtility.AllOnHand(CurrentMap);
                    drawsSinceCount = 0;
                }
            }
            IEnumerable<ThingDef> filtered = source;
            if (filterHeld)
            {
                filtered = filtered.Where(d => held.ContainsKey(d));
            }
            if (!search.NullOrEmpty())
            {
                filtered = filtered.Where(d => d.label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }
            List<ThingDef> matches = filtered as List<ThingDef> ?? filtered.ToList();
            Rect outRect = new Rect(rect.x, rect.y + top, rect.width, rect.height - top);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, matches.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref searchScroll, view);
            // Only the rows in sight are drawn: the full list has hundreds of items.
            int first = Mathf.Max(0, Mathf.FloorToInt(searchScroll.y / RowHeight));
            int last = Mathf.Min(matches.Count - 1, Mathf.CeilToInt((searchScroll.y + outRect.height) / RowHeight));
            for (int i = first; i <= last; i++)
            {
                ThingDef def = matches[i];
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                bool listed = needs.IsListed(def);
                Widgets.DrawHighlightIfMouseover(row);
                Widgets.DefIcon(new Rect(row.x + 2f, row.y + 2f, 24f, 24f), def, GenStuff.DefaultStuffFor(def));
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = listed ? Color.gray : Color.white;
                // On the sells tab, how many the colony has sits at the right of the row.
                float countWidth = 0f;
                if (selling && held.TryGetValue(def, out int have))
                {
                    countWidth = 60f;
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(new Rect(row.xMax - countWidth - 4f, row.y, countWidth, row.height), have.ToString());
                    Text.Anchor = TextAnchor.MiddleLeft;
                }
                float labelWidth = row.width - 32f - countWidth;
                Widgets.Label(new Rect(row.x + 32f, row.y, labelWidth, row.height), def.LabelCap.ToString().Truncate(labelWidth));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                string tip;
                if (listed)
                {
                    tip = (needs.Has(def) ? "SE_AlreadyOnNeeds" : "SE_AlreadyOnSells").Translate();
                }
                else
                {
                    tip = (selling ? "SE_ClickToAddSale" : "SE_ClickToAdd").Translate(def.LabelCap);
                }
                TooltipHandler.TipRegion(row, tip);
                if (!listed && Widgets.ButtonInvisible(row))
                {
                    if (selling)
                    {
                        needs.AddSale(def);
                    }
                    else
                    {
                        needs.Add(def);
                    }
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawNeedsList(Rect rect, ColonyNeeds needs, bool selling)
        {
            Map map = CurrentMap;
            List<ColonyNeed> lines = selling ? needs.sales : needs.needs;
            float y = rect.y;
            // The silver reserve, and what the royals can spend above it.
            Rect reserveLabel = new Rect(rect.x, y, 160f, 30f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(reserveLabel, "SE_Reserve".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            reserveBuffer ??= needs.reserve.ToString();
            Widgets.TextFieldNumeric(new Rect(rect.x + 160f, y + 2f, 90f, 26f), ref needs.reserve, ref reserveBuffer, 0f, 1000000f);
            TooltipHandler.TipRegion(reserveLabel, "SE_ReserveTip".Translate());
            int silver = NeedsUtility.ColonySilver(map);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 270f, y, rect.width - 270f, 30f),
                "SE_ColonySilver".Translate(silver.ToStringMoney(), Mathf.Max(0, silver - needs.reserve).ToStringMoney()));
            Text.Anchor = TextAnchor.UpperLeft;
            y += 36f;

            // Item name, then Have, Keep on hand, Price cap and the row buttons; 16 for the scrollbar.
            float itemWidth = rect.width - 70f - 110f - 170f - 76f - 16f;
            float xHave = rect.x + itemWidth;
            float xKeep = xHave + 70f;
            float xCap = xKeep + 110f;
            float xButtons = xCap + 170f;
            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(new Rect(rect.x + 32f, y, itemWidth - 32f, 24f), "SE_ColItem".Translate());
            Widgets.Label(new Rect(xHave, y, 70f, 24f), "SE_ColHave".Translate());
            Widgets.Label(new Rect(xKeep, y, 110f, 24f), (selling ? "SE_ColKeepSell" : "SE_ColKeep").Translate());
            Widgets.Label(new Rect(xCap, y, 170f, 24f), (selling ? "SE_ColFloor" : "SE_ColCap").Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(xHave, y, 70f, 24f), "SE_ColHaveTip".Translate());
            TooltipHandler.TipRegion(new Rect(xKeep, y, 110f, 24f), (selling ? "SE_ColKeepSellTip" : "SE_ColKeepTip").Translate());
            TooltipHandler.TipRegion(new Rect(xCap, y, 170f, 24f), (selling ? "SE_ColFloorTip" : "SE_ColCapTip").Translate());
            y += 26f;
            Widgets.DrawLineHorizontal(rect.x, y, rect.width);
            y += 4f;

            float footerHeight = 60f;
            Rect outRect = new Rect(rect.x, y, rect.width, rect.yMax - y - footerHeight);
            if (lines.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(outRect.x, outRect.y + 8f, outRect.width, 60f), (selling ? "SE_NoSales" : "SE_NoNeeds").Translate());
                GUI.color = Color.white;
            }
            Rect view = new Rect(0f, 0f, outRect.width - 16f, lines.Count * RowHeight);
            // Removing or moving a line waits until the list is drawn.
            System.Action change = null;
            // Each tab keeps its own scroll position. A ref local points at whichever field it is.
            ref Vector2 scroll = ref (selling ? ref salesScroll : ref needsScroll);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            for (int i = 0; i < lines.Count; i++)
            {
                ColonyNeed need = lines[i];
                int index = i;
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(row);
                }
                float ox = row.x - rect.x;
                Widgets.DefIcon(new Rect(row.x + 2f, row.y + 2f, 24f, 24f), need.def, GenStuff.DefaultStuffFor(need.def));
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x + 32f, row.y, itemWidth - 36f, row.height), need.def.LabelCap.ToString().Truncate(itemWidth - 36f));
                int have = NeedsUtility.OnHand(map, need.def);
                // Red: short of what's needed. Green: more than is kept back, so there's some to sell.
                if (selling)
                {
                    GUI.color = have > need.keep ? Color.green : Color.white;
                }
                else
                {
                    GUI.color = have < need.keep ? ColorLibrary.RedReadable : Color.white;
                }
                Widgets.Label(new Rect(xHave + ox, row.y, 70f, row.height), have.ToString());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                if (!keepBuffers.TryGetValue(need, out string keepBuffer))
                {
                    keepBuffer = need.keep.ToString();
                }
                Widgets.TextFieldNumeric(new Rect(xKeep + ox, row.y + 2f, 90f, 24f), ref need.keep, ref keepBuffer, 0f, 100000f);
                keepBuffers[need] = keepBuffer;

                Widgets.Checkbox(new Vector2(xCap + ox, row.y + 2f), ref need.capped, 24f);
                if (need.capped)
                {
                    if (!capBuffers.TryGetValue(need, out string capBuffer))
                    {
                        capBuffer = need.capPercent.ToString();
                    }
                    Widgets.TextFieldNumeric(new Rect(xCap + ox + 30f, row.y + 2f, 60f, 24f), ref need.capPercent, ref capBuffer, 1f, 1000f);
                    capBuffers[need] = capBuffer;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(xCap + ox + 94f, row.y, 70f, row.height), "SE_PercentOfValue".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                }

                float bx = xButtons + ox;
                if (i > 0 && Widgets.ButtonImage(new Rect(bx, row.y + 4f, 20f, 20f), TexButton.ReorderUp, tooltip: (selling ? "SE_MoveUpSell" : "SE_MoveUp").Translate()))
                {
                    change = () => Swap(lines, index, index - 1);
                }
                if (i < lines.Count - 1 && Widgets.ButtonImage(new Rect(bx + 24f, row.y + 4f, 20f, 20f), TexButton.ReorderDown, tooltip: (selling ? "SE_MoveDownSell" : "SE_MoveDown").Translate()))
                {
                    change = () => Swap(lines, index, index + 1);
                }
                if (Widgets.ButtonImage(new Rect(bx + 50f, row.y + 4f, 20f, 20f), TexButton.Delete, tooltip: "SE_Remove".Translate()))
                {
                    change = () =>
                    {
                        lines.Remove(need);
                        keepBuffers.Remove(need);
                        capBuffers.Remove(need);
                    };
                }
            }
            Widgets.EndScrollView();
            change?.Invoke();

            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, rect.yMax - footerHeight + 6f, rect.width, footerHeight - 6f), (selling ? "SE_SellsFooter" : "SE_NeedsFooter").Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private static void Swap(List<ColonyNeed> list, int a, int b)
        {
            (list[a], list[b]) = (list[b], list[a]);
        }
    }
}
