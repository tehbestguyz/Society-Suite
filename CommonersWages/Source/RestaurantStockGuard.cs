using System;
using System.Reflection;
using Gastronomy.Restaurant;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CommonersWages
{
    // Gastronomy's stock is simply every unforbidden menu item inside a restaurant register's radius,
    // and nothing stops the ordinary food search from finding it. Once the food policy allows those
    // meals, colonists help themselves off the restaurant's shelves for free, especially while it's
    // closed. With this guard, restaurant stock only reaches people through the restaurant (Gastronomy's
    // dining and serving don't use the general food search). A starving pawn may still take it.
    //
    // While a general food search runs (FoodUtility.BestFoodSourceOnMap), IsForbidden reports restaurant
    // stock as forbidden. Both methods are far too big for the JIT to inline, so the patches always apply.
    public static class RestaurantStockGuard
    {
        [ThreadStatic]
        public static bool active;

        // Kept apart from the patch classes so their signatures never mention Gastronomy types.
        public static bool IsStock(Thing t)
        {
            if (!t.Spawned || t.def?.ingestible == null)
            {
                return false;
            }
            RestaurantsManager manager = t.Map.GetRestaurantsManager();
            if (manager?.restaurants == null)
            {
                return false;
            }
            foreach (RestaurantController restaurant in manager.restaurants)
            {
                if (restaurant.Stock.IsAvailable(t))
                {
                    return true;
                }
            }
            return false;
        }
    }

    // While a general food search runs (FoodUtility.BestFoodSourceOnMap), IsForbidden reports two kinds of
    // food as forbidden: Gastronomy restaurant stock (above), and food on a for-sale shelf (Market.cs) that
    // the eater can't take. Neither applies to a starving pawn. While pawns pack food to carry, for-sale
    // food is forbidden to everyone.
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap))]
    public static class Patch_FoodUtility_BestFoodSourceOnMap
    {
        public static void Prefix(Pawn eater, bool desperate, out (bool restaurant, bool market, Pawn eater) __state)
        {
            __state = (RestaurantStockGuard.active, MarketUtility.searchActive, MarketUtility.searchEater);
            RestaurantStockGuard.active = DiningCheck.GastronomyLoaded && WagesMod.Settings.restaurantStockForSale && !desperate;
            MarketUtility.searchActive = !desperate;
            MarketUtility.searchEater = eater;
        }

        public static void Finalizer((bool restaurant, bool market, Pawn eater) __state)
        {
            RestaurantStockGuard.active = __state.restaurant;
            MarketUtility.searchActive = __state.market;
            MarketUtility.searchEater = __state.eater;
        }
    }

    [HarmonyPatch]
    public static class Patch_ForbidUtility_IsForbidden
    {
        public static MethodBase TargetMethod() => AccessTools.Method(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new[] { typeof(Thing), typeof(Pawn) });

        public static void Postfix(Thing t, ref bool __result)
        {
            if (__result || t == null)
            {
                return;
            }
            if (RestaurantStockGuard.active && RestaurantStockGuard.IsStock(t))
            {
                __result = true;
                return;
            }
            if ((MarketUtility.packing || MarketUtility.searchActive) && MarketUtility.IsForSale(t)
                && (MarketUtility.packing || !MarketUtility.CanTake(MarketUtility.searchEater, t)))
            {
                __result = true;
            }
        }
    }
}
