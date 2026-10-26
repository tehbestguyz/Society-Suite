using System;
using System.Collections.Generic;
using System.Linq;
using CommonersWages;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SocietyHomes
{
    // The private bill a home cook works from. It's never in the stove's bill list, so the player and
    // colony cooks don't see it; vanilla's DoBill job runs it as usual (ingredient hauling, skill,
    // quality, food poisoning). It's saved in HomesMapComponent, so a job holding it loads cleanly.
    public class HouseholdBill : Bill_Production
    {
        public Building stove;

        public HouseholdBill()
        {
        }

        public HouseholdBill(RecipeDef recipe, Building stove) : base(recipe)
        {
            this.stove = stove;
            billStack = ((IBillGiver)stove).BillStack;
            repeatMode = BillRepeatModeDefOf.Forever;
        }

        public Zone_Lot Lot => LotUtility.LotOf(stove);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref stove, "shStove");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && stove is IBillGiver giver)
            {
                billStack = giver.BillStack;
            }
        }
    }

    // In free time, a household member who can cook makes a meal at a stove on their lot from pantry
    // ingredients, while the pantry is short of cooked meals.
    public class JobGiver_HomeCooking : ThinkNode_JobGiver
    {
        private static readonly Dictionary<Pawn, int> nextLook = new Dictionary<Pawn, int>();

        public override float GetPriority(Pawn pawn)
        {
            if (!HomesMod.Settings.homeCooking || !HomeCooking.CanCook(pawn))
            {
                return 0f;
            }
            return HomeErrands.FreeTimePriority(pawn, nextLook, 7.1f, 6.1f);
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            Job job = HomeCooking.TryMakeJob(pawn);
            if (job == null)
            {
                nextLook[pawn] = Find.TickManager.TicksGame + 2000;
            }
            return job;
        }
    }

    public static class HomeCooking
    {
        // Cook while cooked meals are below this share of the pantry's target.
        private const float MealsShare = 0.5f;

        private static readonly Dictionary<ThingDef, List<RecipeDef>> mealRecipes = new Dictionary<ThingDef, List<RecipeDef>>();

        private static readonly Func<Bill, Pawn, Thing, List<ThingCount>, List<IngredientCount>, bool> findIngredients =
            AccessTools.MethodDelegate<Func<Bill, Pawn, Thing, List<ThingCount>, List<IngredientCount>, bool>>(
                AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients"));

        public static bool CanCook(Pawn pawn)
        {
            return pawn.DevelopmentalStage.Adult() && !pawn.WorkTypeIsDisabled(SH_DefOf.Cooking);
        }

        // Single meals (not bulk batches or survival meals) this building can cook, best first.
        public static List<RecipeDef> MealRecipes(ThingDef def)
        {
            if (!mealRecipes.TryGetValue(def, out List<RecipeDef> list))
            {
                list = (def.AllRecipes ?? new List<RecipeDef>())
                    .Where(IsHomeMeal)
                    .OrderByDescending(r => r.products[0].thingDef.ingestible.preferability)
                    .ThenByDescending(r => r.products[0].thingDef.ingestible.CachedNutrition)
                    .ToList();
                mealRecipes[def] = list;
            }
            return list;
        }

        private static bool IsHomeMeal(RecipeDef r)
        {
            if (r.products == null || r.products.Count != 1 || r.products[0].count != 1 || r.ingredients.NullOrEmpty())
            {
                return false;
            }
            ThingDef product = r.products[0].thingDef;
            // Survival meals don't rot and cost more ingredients; they're for caravans, not dinner.
            return Pantry.IsMeal(product) && product.HasComp(typeof(CompRottable))
                && product.ingestible.preferability >= FoodPreferability.MealSimple
                && product.ingestible.preferability <= FoodPreferability.MealLavish;
        }

        public static Job TryMakeJob(Pawn pawn)
        {
            HomeData home = Homes.HomeOf(pawn);
            if (home == null || home.stoves.Count == 0 || home.pantries.Count == 0 || home.Map != pawn.Map)
            {
                return null;
            }
            // One cook per household at a time.
            if (home.members.Any(m => m != pawn && m.CurJob?.bill is HouseholdBill))
            {
                return null;
            }
            home.Stock(out float meals, out float raw);
            if (raw <= 0f || meals >= home.TargetNutrition() * MealsShare)
            {
                return null;
            }
            HomesMapComponent comp = pawn.Map.GetComponent<HomesMapComponent>();
            List<ThingCount> chosen = new List<ThingCount>();
            List<IngredientCount> missing = new List<IngredientCount>();
            foreach (Building stove in home.stoves.OrderBy(s => (s.Position - pawn.Position).LengthHorizontalSquared))
            {
                IBillGiver giver = (IBillGiver)stove;
                if (!stove.Spawned || stove.IsForbidden(pawn) || stove.IsBurning() || !giver.CurrentlyUsableForBills()
                    || !pawn.CanReserve(stove) || !pawn.CanReach(stove, PathEndMode.InteractionCell, Danger.Some))
                {
                    continue;
                }
                foreach (RecipeDef recipe in MealRecipes(stove.def))
                {
                    if (!recipe.AvailableNow || !recipe.PawnSatisfiesSkillRequirements(pawn) || !home.Accepts(recipe.products[0].thingDef))
                    {
                        continue;
                    }
                    HouseholdBill bill = comp.BillFor(stove, recipe);
                    bool found;
                    HomesContext.pantryOnly = home.lot;
                    try
                    {
                        missing.Clear();
                        found = findIngredients(bill, pawn, stove, chosen, missing);
                    }
                    finally
                    {
                        HomesContext.pantryOnly = null;
                    }
                    if (found)
                    {
                        return WorkGiver_DoBill.TryStartNewDoBillJob(pawn, bill, giver, chosen, out Job _);
                    }
                }
            }
            return null;
        }
    }
}
