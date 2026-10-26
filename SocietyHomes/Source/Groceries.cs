using System.Collections.Generic;
using System.Linq;
using CashRegister;
using CommonersWages;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SocietyHomes
{
    // Slotted into the colonist need/work priority sorter, just below Wages' payday jobs: in free time,
    // an adult household member restocks a pantry that's running low.
    public class JobGiver_GroceryRun : ThinkNode_JobGiver
    {
        // After a failed look, a pawn doesn't look again for a while.
        private static readonly Dictionary<Pawn, int> nextLook = new Dictionary<Pawn, int>();

        public override float GetPriority(Pawn pawn)
        {
            return HomeErrands.FreeTimePriority(pawn, nextLook, 7.2f, 6.2f);
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            Job job = Groceries.TryMakeJob(pawn);
            if (job == null)
            {
                nextLook[pawn] = Find.TickManager.TicksGame + 2000;
            }
            return job;
        }
    }

    public static class HomeErrands
    {
        public static float FreeTimePriority(Pawn pawn, Dictionary<Pawn, int> nextLook, float joyPriority, float anythingPriority)
        {
            if (!Homes.AnyPantries || !pawn.Spawned || pawn.Drafted || !pawn.DevelopmentalStage.Adult())
            {
                return 0f;
            }
            HomeData home = Homes.HomeOf(pawn);
            if (home == null || home.pantries.Count == 0 || home.Map != pawn.Map)
            {
                return 0f;
            }
            if (nextLook.TryGetValue(pawn, out int tick) && Find.TickManager.TicksGame < tick)
            {
                return 0f;
            }
            TimeAssignmentDef assignment = pawn.timetable?.CurrentAssignment ?? TimeAssignmentDefOf.Anything;
            if (assignment == TimeAssignmentDefOf.Joy)
            {
                return joyPriority;
            }
            return assignment == TimeAssignmentDefOf.Anything ? anythingPriority : 0f;
        }
    }

    public static class Groceries
    {
        // Shop when the pantry is below this share of its target, and buy back up to the target.
        private const float RestockBelow = 0.6f;

        public static Job TryMakeJob(Pawn pawn)
        {
            HomeData home = Homes.HomeOf(pawn);
            Map map = pawn.Map;
            // Children don't shop; they already eat free anywhere.
            if (home == null || home.pantries.Count == 0 || home.Map != map || !pawn.DevelopmentalStage.Adult())
            {
                return null;
            }
            // One shopper per household at a time.
            if (home.members.Any(m => m != pawn && m.CurJobDef == SH_DefOf.SH_GroceryRun))
            {
                return null;
            }
            float target = home.TargetNutrition();
            home.Stock(out float meals, out float raw);
            if (target <= 0f || meals + raw >= target * RestockBelow)
            {
                return null;
            }
            float need = target - meals - raw;
            bool cooks = home.CooksAtHome();
            bool free = MarketUtility.TakesFree(pawn);
            int funds = free ? int.MaxValue : WageUtility.SpendableSavings(pawn);
            if (!free && !WageUtility.PayrollRegisters(map).Any())
            {
                return null;
            }
            bool haveMeat = false, haveVeg = false;
            if (cooks)
            {
                foreach (Thing t in home.pantries.SelectMany(g => g.HeldThings))
                {
                    haveMeat |= t.def.IsWithinCategory(ThingCategoryDefOf.MeatRaw);
                    haveVeg |= t.def.IsWithinCategory(ThingCategoryDefOf.PlantFoodRaw);
                }
            }

            MarketMapComponent market = map.GetComponent<MarketMapComponent>();
            List<(Thing thing, float score)> candidates = new List<(Thing, float)>();
            foreach (SlotGroup group in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (!Pantry.IsForSaleGroup(group, market) || Homes.PantryLotAt(map, group.CellsList.FirstOrDefault()) != null)
                {
                    continue;
                }
                foreach (Thing t in group.HeldThings)
                {
                    bool meal = Pantry.IsMeal(t.def);
                    bool ingredient = !meal && cooks && Pantry.IsRawFood(t.def) && home.UsefulIngredient(t.def);
                    if (!meal && !ingredient)
                    {
                        continue;
                    }
                    if (!Wanted(pawn, home, t, meal, free, funds))
                    {
                        continue;
                    }
                    float score = -(t.Position - pawn.Position).LengthHorizontal;
                    if (meal)
                    {
                        // Cooking households still buy meals, but only when no ingredients are for sale.
                        score += cooks ? -10000f : (int)t.def.ingestible.preferability * 20f;
                    }
                    else if ((!haveMeat && t.def.IsWithinCategory(ThingCategoryDefOf.MeatRaw)) || (!haveVeg && t.def.IsWithinCategory(ThingCategoryDefOf.PlantFoodRaw)))
                    {
                        // Something to go with what's already at home, for fine meals.
                        score += 30f;
                    }
                    candidates.Add((t, score));
                }
            }

            foreach ((Thing t, float _) in candidates.OrderByDescending(c => c.score).Take(8))
            {
                if (!pawn.CanReach(t, PathEndMode.ClosestTouch, Danger.Some))
                {
                    continue;
                }
                int count = CountToBuy(pawn, t, need, free, funds);
                if (count <= 0)
                {
                    continue;
                }
                IntVec3 cell = FindPantryCell(pawn, home, t);
                if (!cell.IsValid)
                {
                    continue;
                }
                Job job = JobMaker.MakeJob(SH_DefOf.SH_GroceryRun, t, cell);
                job.count = count;
                return job;
            }
            return null;
        }

        private static bool Wanted(Pawn pawn, HomeData home, Thing t, bool meal, bool free, int funds)
        {
            if (t.IsForbidden(pawn) || !home.Accepts(t) || !pawn.CanReserve(t))
            {
                return false;
            }
            CompRottable rot = t.TryGetComp<CompRottable>();
            if (rot != null && rot.Stage != RotStage.Fresh)
            {
                return false;
            }
            if (meal && !(pawn.foodRestriction?.CurrentFoodPolicy?.Allows(t) ?? true))
            {
                return false;
            }
            if (DiningCheck.GastronomyLoaded && RestaurantStockGuard.IsStock(t))
            {
                return false;
            }
            return free || funds >= MarketUtility.UnitPrice(t);
        }

        private static int CountToBuy(Pawn pawn, Thing t, float need, bool free, int funds)
        {
            float perUnit = t.def.ingestible.CachedNutrition;
            int count = perUnit > 0f ? Mathf.CeilToInt(need / perUnit) : 1;
            count = Mathf.Min(count, t.stackCount, pawn.carryTracker.AvailableStackSpace(t.def));
            int unit = MarketUtility.UnitPrice(t);
            if (!free && unit > 0)
            {
                count = Mathf.Min(count, funds / unit);
            }
            return count;
        }

        public static IntVec3 FindPantryCell(Pawn pawn, HomeData home, Thing t)
        {
            HomesContext.storeInto = home.lot;
            try
            {
                return StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, home.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 cell)
                    ? cell
                    : IntVec3.Invalid;
            }
            finally
            {
                HomesContext.storeInto = null;
            }
        }
    }

    // Walk to the for-sale shelf, pay for the food (Wages' for-sale price, into a payroll register), carry
    // it home in hand and put it in the pantry. Carrying it in hand rather than inventory keeps it out of
    // Combat Extended loadouts, Pick Up And Haul and food packing.
    public class JobDriver_GroceryRun : JobDriver
    {
        private const TargetIndex FoodInd = TargetIndex.A;
        private const TargetIndex CellInd = TargetIndex.B;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(FoodInd), job, 1, job.count, null, errorOnFailed)
                && pawn.Reserve(job.GetTarget(CellInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Homes.HomeOf(pawn) == null);
            yield return Toils_Goto.GotoThing(FoodInd, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(FoodInd)
                .FailOn(() => !MarketUtility.IsForSale(job.GetTarget(FoodInd).Thing));
            Toil buy = ToilMaker.MakeToil("BuyGroceries");
            buy.initAction = () =>
            {
                if (!Buy())
                {
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            yield return buy;
            yield return Toils_Haul.CarryHauledThingToCell(CellInd);
            Toil stow = ToilMaker.MakeToil("StowGroceries");
            stow.initAction = Stow;
            yield return stow;
        }

        private bool Buy()
        {
            Thing food = job.GetTarget(FoodInd).Thing;
            if (food == null || !food.Spawned || !MarketUtility.IsForSale(food) || !pawn.DevelopmentalStage.Adult())
            {
                return false;
            }
            bool free = MarketUtility.TakesFree(pawn);
            int unit = MarketUtility.UnitPrice(food);
            Building_CashRegister register = WageUtility.PayrollRegisters(pawn.Map).FirstOrDefault();
            bool pays = !free && unit > 0;
            if (pays && register == null)
            {
                return false;
            }
            int count = Mathf.Min(job.count, food.stackCount);
            if (pays)
            {
                count = Mathf.Min(count, WageUtility.SpendableSavings(pawn) / unit);
            }
            if (count <= 0 || pawn.carryTracker.TryStartCarry(food, count) <= 0)
            {
                return false;
            }
            Thing carried = pawn.carryTracker.CarriedThing;
            if (carried == null)
            {
                return false;
            }
            job.SetTarget(FoodInd, carried);
            if (pays)
            {
                // Counted from what's actually in hand, not TryStartCarry's return.
                int paid = WageUtility.PaySavings(pawn, register.GetDirectlyHeldThings(), unit * carried.stackCount);
                MoneyLog.Note(pawn, MoneyKind.Purchase, -paid, carried.Label);
                if (paid > 0)
                {
                    MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "-" + paid.ToStringMoney(), 3.5f);
                }
            }
            return true;
        }

        // Into the pantry cell, or another pantry cell nearby if it filled up meanwhile. Anything that
        // still won't fit is set down at home.
        private void Stow()
        {
            HomeData home = Homes.HomeOf(pawn);
            Map map = pawn.Map;
            if (home != null)
            {
                HomesContext.storeInto = home.lot;
                try
                {
                    IntVec3 cell = job.GetTarget(CellInd).Cell;
                    for (int i = 0; i < 4 && pawn.carryTracker.CarriedThing != null; i++)
                    {
                        Thing carried = pawn.carryTracker.CarriedThing;
                        if (i > 0 || !StoreUtility.IsGoodStoreCell(cell, map, carried, pawn, pawn.Faction))
                        {
                            if (!StoreUtility.TryFindBestBetterStoreCellFor(carried, pawn, map, StoragePriority.Unstored, pawn.Faction, out cell)
                                || (cell - pawn.Position).LengthHorizontal > 12f)
                            {
                                break;
                            }
                        }
                        pawn.carryTracker.TryDropCarriedThing(cell, ThingPlaceMode.Direct, out Thing _);
                    }
                }
                finally
                {
                    HomesContext.storeInto = null;
                }
            }
            if (pawn.carryTracker.CarriedThing != null)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
            }
        }
    }
}
