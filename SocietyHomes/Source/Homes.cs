using System.Collections.Generic;
using System.Linq;
using CommonersWages;
using RimWorld;
using Verse;

namespace SocietyHomes
{
    // A household: whoever lives on an occupied lot. That's the lot's resident (the tenant, or else the
    // owner) plus their family (Wages' rule: partner, parents, children) who sleep on the lot. The
    // resident's children count even without a bed there.
    public class HomeData
    {
        public Zone_Lot lot;
        public readonly List<Pawn> members = new List<Pawn>();
        public readonly List<SlotGroup> pantries = new List<SlotGroup>();
        // Buildings on the lot with a meal recipe (stoves, campfires, modded grills).
        public readonly List<Building> stoves = new List<Building>();
        private readonly Dictionary<ThingDef, bool> usefulIngredients = new Dictionary<ThingDef, bool>();

        public Map Map => lot.Map;

        // Nutrition the household eats in a day when fed.
        public float DailyNutrition()
        {
            float total = 0f;
            foreach (Pawn p in members)
            {
                Need_Food food = p.needs?.food;
                if (food != null)
                {
                    total += food.FoodFallPerTickAssumingCategory(HungerCategory.Fed) * GenDate.TicksPerDay;
                }
            }
            return total;
        }

        public float TargetNutrition() => DailyNutrition() * HomesMod.Settings.daysOfFood;

        // Food in the pantry, split into cooked meals and raw ingredients a home stove can use.
        public void Stock(out float meals, out float raw)
        {
            meals = 0f;
            raw = 0f;
            foreach (SlotGroup group in pantries)
            {
                foreach (Thing t in group.HeldThings)
                {
                    if (Pantry.IsMeal(t.def))
                    {
                        meals += t.def.ingestible.CachedNutrition * t.stackCount;
                    }
                    else if (Pantry.IsRawFood(t.def))
                    {
                        raw += t.def.ingestible.CachedNutrition * t.stackCount;
                    }
                }
            }
        }

        public bool Accepts(Thing t) => pantries.Any(g => g.Settings.AllowedToAccept(t));

        public bool Accepts(ThingDef def) => pantries.Any(g => g.Settings.AllowedToAccept(def));

        // Someone in the household can cook at a stove on the lot.
        public bool CooksAtHome()
        {
            return HomesMod.Settings.homeCooking && stoves.Count > 0 && members.Any(HomeCooking.CanCook);
        }

        // A raw food some meal recipe at a home stove takes.
        public bool UsefulIngredient(ThingDef def)
        {
            if (!usefulIngredients.TryGetValue(def, out bool useful))
            {
                useful = stoves.Any(s => HomeCooking.MealRecipes(s.def)
                    .Any(r => r.fixedIngredientFilter.Allows(def) && r.ingredients.Any(i => i.filter.Allows(def))));
                usefulIngredients[def] = useful;
            }
            return useful;
        }
    }

    // Households and pantry cells, rebuilt every 250 ticks (or right after a pantry is toggled). Lookups
    // swap in whole new tables, so readers never see one half built.
    public static class Homes
    {
        private class MapHomes
        {
            // Pantry cells on occupied lots: the lot whose household they belong to.
            public Zone_Lot[] pantryCells;
            // Every cell of storage marked as a pantry, lived in or not. Nothing is hauled into these.
            public bool[] closedCells;
        }

        private static Dictionary<Map, MapHomes> maps = new Dictionary<Map, MapHomes>();
        private static Dictionary<Pawn, HomeData> homeOf = new Dictionary<Pawn, HomeData>();
        private static Dictionary<Zone_Lot, HomeData> homes = new Dictionary<Zone_Lot, HomeData>();
        private static readonly Dictionary<ThingDef, bool> stoveDefs = new Dictionary<ThingDef, bool>();

        // Nothing to check anywhere until some occupied lot has a pantry.
        public static volatile bool AnyPantries;
        // Some storage anywhere is marked as a pantry (it may be on a vacant lot).
        public static volatile bool AnyMarked;
        // Someone lives on a lot somewhere.
        public static volatile bool AnyHomes;
        private static bool dirty = true;

        public static void SetDirty() => dirty = true;

        public static void Clear()
        {
            maps = new Dictionary<Map, MapHomes>();
            homeOf = new Dictionary<Pawn, HomeData>();
            homes = new Dictionary<Zone_Lot, HomeData>();
            AnyPantries = false;
            AnyMarked = false;
            AnyHomes = false;
            dirty = true;
        }

        public static HomeData HomeOf(Pawn pawn)
        {
            return pawn != null && homeOf.TryGetValue(pawn, out HomeData home) ? home : null;
        }

        public static HomeData HomeOfLot(Zone_Lot lot)
        {
            return lot != null && homes.TryGetValue(lot, out HomeData home) ? home : null;
        }

        // The lot whose pantry this cell is part of, if any.
        public static Zone_Lot PantryLotAt(Map map, IntVec3 c)
        {
            if (!AnyPantries || map == null || !maps.TryGetValue(map, out MapHomes m) || m.pantryCells == null)
            {
                return null;
            }
            int i = map.cellIndices.CellToIndex(c);
            return i >= 0 && i < m.pantryCells.Length ? m.pantryCells[i] : null;
        }

        // Part of storage marked as a pantry, whether or not anyone lives on its lot.
        public static bool IsClosedCell(Map map, IntVec3 c)
        {
            if (!AnyMarked || map == null || !maps.TryGetValue(map, out MapHomes m) || m.closedCells == null)
            {
                return false;
            }
            int i = map.cellIndices.CellToIndex(c);
            return i >= 0 && i < m.closedCells.Length && m.closedCells[i];
        }

        public static Zone_Lot PantryLotOf(Thing t)
        {
            return t != null && t.Spawned ? PantryLotAt(t.Map, t.Position) : null;
        }

        public static void TickCheck()
        {
            if (dirty || Find.TickManager.TicksGame % 250 == 113)
            {
                dirty = false;
                Rebuild();
            }
        }

        private static void Rebuild()
        {
            Dictionary<Map, MapHomes> newMaps = new Dictionary<Map, MapHomes>();
            Dictionary<Pawn, HomeData> newHomeOf = new Dictionary<Pawn, HomeData>();
            Dictionary<Zone_Lot, HomeData> newHomes = new Dictionary<Zone_Lot, HomeData>();
            bool any = false;
            bool anyMarked = false;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome)
                {
                    continue;
                }
                List<HomeData> mapHomes = LotUtility.AllLots(map).Where(l => l.Resident != null).Select(l => new HomeData { lot = l }).ToList();
                foreach (HomeData home in mapHomes)
                {
                    newHomes[home.lot] = home;
                }
                FindMembers(map, mapHomes, newHomeOf);
                MapHomes mh = new MapHomes();
                FindPantries(map, newHomes, mh);
                any |= mh.pantryCells != null;
                anyMarked |= mh.closedCells != null;
                newMaps[map] = mh;
                FindStoves(map, newHomes);
                map.GetComponent<HomesMapComponent>()?.PruneBills();
            }
            maps = newMaps;
            homeOf = newHomeOf;
            homes = newHomes;
            AnyPantries = any;
            AnyMarked = anyMarked;
            AnyHomes = newHomes.Count > 0;
        }

        private static void FindMembers(Map map, List<HomeData> mapHomes, Dictionary<Pawn, HomeData> result)
        {
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                if (p.IsSlave || p.IsPrisoner || p.Dead)
                {
                    continue;
                }
                HomeData home = mapHomes.FirstOrDefault(h => h.lot.Resident == p);
                if (home == null)
                {
                    Building_Bed bed = p.ownership?.OwnedBed;
                    Zone_Lot bedLot = bed != null && bed.Map == map ? LotUtility.LotOf(bed) : null;
                    bool child = !p.DevelopmentalStage.Adult();
                    home = mapHomes.FirstOrDefault(h => LotUtility.IsFamily(h.lot.Resident, p)
                        && (bedLot == h.lot || (child && (p.GetMother() == h.lot.Resident || p.GetFather() == h.lot.Resident))));
                }
                if (home != null)
                {
                    home.members.Add(p);
                    result[p] = home;
                }
            }
        }

        private static void FindPantries(Map map, Dictionary<Zone_Lot, HomeData> lotHomes, MapHomes mh)
        {
            HomesMapComponent comp = map.GetComponent<HomesMapComponent>();
            foreach (SlotGroup group in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (!Pantry.IsMarked(group.parent, comp) || group.CellsList.Count == 0)
                {
                    continue;
                }
                // A household doesn't pay for its own food, and nobody else may buy it.
                Pantry.ClearForSale(group.parent, map);
                mh.closedCells ??= new bool[map.cellIndices.NumGridCells];
                foreach (IntVec3 c in group.CellsList)
                {
                    mh.closedCells[map.cellIndices.CellToIndex(c)] = true;
                    // A stockpile is only a household's pantry where it lies on an occupied lot.
                    Zone_Lot lot = LotUtility.LotAt(c, map);
                    if (lot == null || !lotHomes.TryGetValue(lot, out HomeData home))
                    {
                        continue;
                    }
                    mh.pantryCells ??= new Zone_Lot[map.cellIndices.NumGridCells];
                    mh.pantryCells[map.cellIndices.CellToIndex(c)] = lot;
                    if (!home.pantries.Contains(group))
                    {
                        home.pantries.Add(group);
                    }
                }
            }
        }

        private static void FindStoves(Map map, Dictionary<Zone_Lot, HomeData> lotHomes)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (!(b is IBillGiver) || !IsStoveDef(b.def))
                {
                    continue;
                }
                Zone_Lot lot = LotUtility.LotOf(b);
                if (lot != null && lotHomes.TryGetValue(lot, out HomeData home))
                {
                    home.stoves.Add(b);
                }
            }
        }

        private static bool IsStoveDef(ThingDef def)
        {
            if (!stoveDefs.TryGetValue(def, out bool stove))
            {
                stove = HomeCooking.MealRecipes(def).Any();
                stoveDefs[def] = stove;
            }
            return stove;
        }
    }

    public class HomesTicker : GameComponent
    {
        public HomesTicker(Game game)
        {
            Homes.Clear();
        }

        public override void GameComponentTick()
        {
            Homes.TickCheck();
        }

        public override void LoadedGame()
        {
            Homes.Clear();
        }

        public override void StartedNewGame()
        {
            Homes.Clear();
        }
    }

    // Per map: which stockpile zones are pantries, and the private bills home cooks work from. The bills
    // are saved here so a cook's job (which refers to its bill) loads cleanly.
    public class HomesMapComponent : MapComponent
    {
        private HashSet<int> pantryZones = new HashSet<int>();
        private List<HouseholdBill> bills = new List<HouseholdBill>();

        public HomesMapComponent(Map map) : base(map)
        {
        }

        public bool IsPantry(Zone zone) => zone != null && pantryZones.Contains(zone.ID);

        public void SetPantry(Zone zone, bool value)
        {
            if (value)
            {
                pantryZones.Add(zone.ID);
            }
            else
            {
                pantryZones.Remove(zone.ID);
            }
            Homes.SetDirty();
        }

        public HouseholdBill BillFor(Building stove, RecipeDef recipe)
        {
            HouseholdBill bill = bills.FirstOrDefault(b => b.stove == stove && b.recipe == recipe);
            if (bill == null)
            {
                bill = new HouseholdBill(recipe, stove);
                bills.Add(bill);
            }
            return bill;
        }

        // Bills for stoves that are gone. A cook still holding one fails their job anyway (the bill giver is destroyed).
        public void PruneBills()
        {
            bills.RemoveAll(b => b.stove == null || b.stove.Destroyed);
        }

        public override void ExposeData()
        {
            List<int> zones = pantryZones.ToList();
            Scribe_Collections.Look(ref zones, "shPantryZones", LookMode.Value);
            Scribe_Collections.Look(ref bills, "shBills", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pantryZones = zones == null ? new HashSet<int>() : new HashSet<int>(zones);
                bills ??= new List<HouseholdBill>();
                bills.RemoveAll(b => b == null || b.stove == null);
            }
        }
    }
}
