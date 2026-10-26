using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SocietyEstates
{
    // One line on the Colony needs list: keep this many of an item on hand, optionally paying no more
    // than a set percentage of its market value.
    // The Colony sells list uses the same lines the other way round: `keep` is how many to hold on to
    // (anything above it is sold), and the percentage is the least a royal will accept, compared with
    // what each item is worth.
    public class ColonyNeed : IExposable
    {
        public ThingDef def;
        public int keep = 10;
        public bool capped;
        public int capPercent = 150;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref keep, "keep", 10);
            Scribe_Values.Look(ref capped, "capped", false);
            Scribe_Values.Look(ref capPercent, "capPercent", 150);
        }
    }

    // The colony's needs list and silver reserve, kept with the save.
    public class ColonyNeeds : GameComponent
    {
        public const int DefaultReserve = 1000;

        public const int DefaultSaleFloor = 80;

        // In buying order: royals buy the first line first.
        public List<ColonyNeed> needs = new List<ColonyNeed>();
        // In selling order: royals sell the first line first, while the merchant still has silver.
        public List<ColonyNeed> sales = new List<ColonyNeed>();
        // Royals never spend the colony's silver below this.
        public int reserve = DefaultReserve;

        public ColonyNeeds(Game game)
        {
        }

        public static ColonyNeeds Get() => Current.Game?.GetComponent<ColonyNeeds>();

        public bool Has(ThingDef def) => needs.Any(n => n.def == def);

        public bool Sells(ThingDef def) => sales.Any(n => n.def == def);

        // An item goes on one list or the other: buying and selling the same thing would undo each other.
        public bool IsListed(ThingDef def) => Has(def) || Sells(def);

        public void Add(ThingDef def)
        {
            if (def != null && !IsListed(def))
            {
                needs.Add(new ColonyNeed { def = def });
            }
        }

        public void AddSale(ThingDef def)
        {
            if (def != null && !IsListed(def))
            {
                sales.Add(new ColonyNeed { def = def, keep = 0, capPercent = DefaultSaleFloor });
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref needs, "seNeeds", LookMode.Deep);
            Scribe_Collections.Look(ref sales, "seSales", LookMode.Deep);
            Scribe_Values.Look(ref reserve, "seReserve", DefaultReserve);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                needs ??= new List<ColonyNeed>();
                sales ??= new List<ColonyNeed>();
                // Items from a mod that's since been removed.
                needs.RemoveAll(n => n?.def == null);
                sales.RemoveAll(n => n?.def == null);
            }
        }
    }

    public static class NeedsUtility
    {
        private static List<ThingDef> buyable;

        // Every item a merchant could sell, by label: the list the needs search picks from. Silver is
        // the currency, so it's left out.
        public static List<ThingDef> BuyableDefs => buyable ??= DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.category == ThingCategory.Item && d.tradeability.TraderCanSell() && !d.IsCorpse && d != ThingDefOf.Silver
                && !typeof(MinifiedThing).IsAssignableFrom(d.thingClass) && !d.label.NullOrEmpty())
            .OrderBy(d => d.label)
            .ToList();

        private static List<ThingDef> sellable;

        // Every item the colony could sell to a merchant: the list the sells search picks from.
        public static List<ThingDef> SellableDefs => sellable ??= DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.category == ThingCategory.Item && d.tradeability.PlayerCanSell() && !d.IsCorpse && d != ThingDefOf.Silver
                && !typeof(MinifiedThing).IsAssignableFrom(d.thingClass) && !d.label.NullOrEmpty())
            .OrderBy(d => d.label)
            .ToList();

        // How many of an item the colony has on this map, counted the way a visiting merchant counts the
        // colony's goods: stored or in the home area, not fogged, plus what's inside storage containers.
        // Things carried or worn by pawns don't count.
        public static int OnHand(Map map, ThingDef def)
        {
            if (map == null || def == null)
            {
                return 0;
            }
            int count = 0;
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
            {
                if (t.Spawned && !t.Position.Fogged(map) && (map.areaManager.Home[t.Position] || t.IsInAnyStorage()))
                {
                    count += t.stackCount;
                }
            }
            foreach (IHaulSource source in map.listerBuildings.AllColonistBuildingsOfType<IHaulSource>())
            {
                foreach (Thing t in source.GetDirectlyHeldThings())
                {
                    if (t.def == def)
                    {
                        count += t.stackCount;
                    }
                }
            }
            return count;
        }

        public static int ColonySilver(Map map) => OnHand(map, ThingDefOf.Silver);

        // Everything the colony has on this map, counted by item the same way as OnHand, in one pass.
        // Calling OnHand once per item would be far slower for a list of hundreds.
        public static Dictionary<ThingDef, int> AllOnHand(Map map)
        {
            Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
            if (map == null)
            {
                return counts;
            }
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t.Spawned && !t.Position.Fogged(map) && (map.areaManager.Home[t.Position] || t.IsInAnyStorage()))
                {
                    counts.TryGetValue(t.def, out int n);
                    counts[t.def] = n + t.stackCount;
                }
            }
            foreach (IHaulSource source in map.listerBuildings.AllColonistBuildingsOfType<IHaulSource>())
            {
                foreach (Thing t in source.GetDirectlyHeldThings())
                {
                    counts.TryGetValue(t.def, out int n);
                    counts[t.def] = n + t.stackCount;
                }
            }
            return counts;
        }
    }
}
