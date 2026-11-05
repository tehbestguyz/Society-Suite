using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommonersWages
{
    // Custom prices for goods for sale, set from the Prices window (right-click a "For sale" gizmo).
    // A price is per kind of item: the same item, made of the same material, at the same quality, costs
    // the same on every for-sale shelf and stockpile. Items without a custom price cost their market
    // value times the markup in the mod settings, as before.
    public class PriceList : GameComponent
    {
        private Dictionary<string, int> prices = new Dictionary<string, int>();

        public PriceList(Game game)
        {
        }

        public static PriceList Get() => Current.Game?.GetComponent<PriceList>();

        // Which price an item uses: its def, its stuff (material) and its quality, as text so it saves
        // simply. Two steel longswords of good quality share a key; a wooden one doesn't.
        public static string Key(Thing t)
        {
            string stuff = t.Stuff?.defName ?? "";
            int quality = t.TryGetQuality(out QualityCategory q) ? (int)q : -1;
            return t.def.defName + "|" + stuff + "|" + quality;
        }

        public bool TryGet(Thing t, out int price) => prices.TryGetValue(Key(t), out price);

        public void Set(Thing t, int price) => prices[Key(t)] = Mathf.Max(0, price);

        public void Clear(Thing t) => prices.Remove(Key(t));

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref prices, "cwPrices", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                prices ??= new Dictionary<string, int>();
            }
        }
    }

    // The "For sale" toggle, with a right-click option that opens the Prices window for what's stored here.
    public class Command_ForSale : Command_Toggle
    {
        // What's stored in the shelf or stockpile right now, or null once it's gone.
        public System.Func<IEnumerable<Thing>> contents;
        public string containerLabel;

        public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions
        {
            get
            {
                foreach (FloatMenuOption option in base.RightClickFloatMenuOptions)
                {
                    yield return option;
                }
                yield return new FloatMenuOption("CW_PricesOption".Translate(), () =>
                {
                    Find.WindowStack.Add(new Window_Prices(contents, containerLabel));
                });
            }
        }
    }

    // Lists what's in one shelf or stockpile, grouped by kind of item, with each one's price and a box
    // to change it.
    public class Window_Prices : Window
    {
        private const float RowHeight = 30f;

        private readonly System.Func<IEnumerable<Thing>> contents;
        private readonly string containerLabel;
        private Vector2 scroll;
        // What's typed in each price box, by price key, so a half-typed number isn't lost between frames.
        private readonly Dictionary<string, string> buffers = new Dictionary<string, string>();

        public override Vector2 InitialSize => new Vector2(680f, 560f);

        public Window_Prices(System.Func<IEnumerable<Thing>> contents, string containerLabel)
        {
            this.contents = contents;
            this.containerLabel = containerLabel;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            PriceList list = PriceList.Get();
            IEnumerable<Thing> held = contents?.Invoke();
            // The shelf was destroyed or the stockpile deleted.
            if (list == null || held == null)
            {
                Close();
                return;
            }
            // One row per kind of item: the first thing of each kind stands for the rest.
            List<IGrouping<string, Thing>> groups = held
                .Where(t => t.def.category == ThingCategory.Item)
                .GroupBy(PriceList.Key)
                .OrderBy(g => g.First().LabelCapNoCount.ToString())
                .ToList();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "CW_PricesTitle".Translate(containerLabel));
            Text.Font = GameFont.Small;
            float y = inRect.y + 38f;
            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "CW_PricesHelp".Translate(WagesMod.Settings.foodMarkupPercent));
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            y += 38f;

            // Columns: item, how many are here, the normal price, the price box and a reset button.
            float xCount = inRect.xMax - 16f - 30f - 110f - 100f - 70f;
            float xNormal = xCount + 70f;
            float xPrice = xNormal + 100f;
            float xReset = xPrice + 110f;
            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(new Rect(inRect.x + 34f, y, xCount - inRect.x - 34f, 24f), "CW_PricesColItem".Translate());
            Widgets.Label(new Rect(xCount, y, 70f, 24f), "CW_PricesColHere".Translate());
            Widgets.Label(new Rect(xNormal, y, 100f, 24f), "CW_PricesColNormal".Translate());
            Widgets.Label(new Rect(xPrice, y, 110f, 24f), "CW_PricesColPrice".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(new Rect(xNormal, y, 100f, 24f), "CW_PricesColNormalTip".Translate(WagesMod.Settings.foodMarkupPercent));
            TooltipHandler.TipRegion(new Rect(xPrice, y, 110f, 24f), "CW_PricesColPriceTip".Translate());
            y += 26f;
            Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);
            y += 4f;

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            if (groups.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(outRect.x, outRect.y + 8f, outRect.width, 40f), "CW_PricesEmpty".Translate());
                GUI.color = Color.white;
                return;
            }
            Rect view = new Rect(0f, 0f, outRect.width - 16f, groups.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            float ox = -inRect.x;
            for (int i = 0; i < groups.Count; i++)
            {
                Thing sample = groups[i].First();
                string key = groups[i].Key;
                int count = groups[i].Sum(t => t.stackCount);
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(row);
                }
                Widgets.ThingIcon(new Rect(row.x + 2f, row.y + 3f, 24f, 24f), sample);
                Text.Anchor = TextAnchor.MiddleLeft;
                float labelWidth = xCount + ox - 34f - 4f;
                Widgets.Label(new Rect(row.x + 34f, row.y, labelWidth, row.height), sample.LabelCapNoCount.Truncate(labelWidth));
                Widgets.Label(new Rect(xCount + ox, row.y, 70f, row.height), count.ToString());
                int normal = MarketUtility.MarkupPrice(sample);
                Widgets.Label(new Rect(xNormal + ox, row.y, 100f, row.height), normal.ToStringMoney());
                Text.Anchor = TextAnchor.UpperLeft;

                // The box shows the custom price, or the normal one if there isn't one. Typing a
                // different number sets a custom price.
                bool custom = list.TryGet(sample, out int price);
                int shown = custom ? price : normal;
                int value = shown;
                if (!buffers.TryGetValue(key, out string buffer))
                {
                    buffer = shown.ToString();
                }
                Widgets.TextFieldNumeric(new Rect(xPrice + ox, row.y + 3f, 90f, 24f), ref value, ref buffer, 0f, 1000000f);
                buffers[key] = buffer;
                if (value != shown)
                {
                    list.Set(sample, value);
                    custom = true;
                }
                if (custom)
                {
                    GUI.color = ColoredText.TipSectionTitleColor;
                    Widgets.DrawBox(new Rect(xPrice + ox - 1f, row.y + 2f, 92f, 26f));
                    GUI.color = Color.white;
                    if (Widgets.ButtonImage(new Rect(xReset + ox, row.y + 5f, 20f, 20f), TexButton.Delete, tooltip: "CW_PricesReset".Translate(normal.ToStringMoney())))
                    {
                        list.Clear(sample);
                        buffers.Remove(key);
                    }
                }
            }
            Widgets.EndScrollView();
        }
    }
}
