using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Gastronomy.Dining;
using Gastronomy.Restaurant;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CommonersWages
{
    // Dev-mode "Restaurant check" on a colonist: walks through Gastronomy's own checks for choosing a
    // restaurant meal, in the order it makes them, and says which one stops this pawn from dining.
    // Only touched when Gastronomy is loaded (the JIT doesn't need Gastronomy until Explain runs).
    public static class DiningCheck
    {
        public static bool GastronomyLoaded => AccessTools.TypeByName("Gastronomy.Dining.DiningUtility") != null;

        public static void Show(Pawn pawn)
        {
            string text = Explain(pawn);
            Log.Message("[Commoners: Wages] Restaurant check for " + pawn.LabelShort + ":\n" + text);
            Find.WindowStack.Add(new Dialog_MessageBox(text));
        }

        private static string Explain(Pawn pawn)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Restaurant check: " + pawn.LabelShort + " (" + Commoners.CasteUtility.LabelFor(pawn) + ")");
            sb.AppendLine();

            Need_Food food = pawn.needs?.food;
            if (food != null)
            {
                bool hungry = food.CurCategory >= HungerCategory.Hungry;
                sb.AppendLine($"Food: {food.CurLevelPercentage:P0} ({food.CurCategory}). Pawns only look for a meal once hungry, below {food.PercentageThreshHungry:P0}. {(hungry ? "Hungry now." : "Not hungry yet, so they won't go now.")}");
                sb.AppendLine($"Nutrition wanted: {food.NutritionWanted:0.00} (meals score 0 below 0.10)");
            }
            bool able = pawn.IsAbleToDine();
            sb.AppendLine("Able to dine (can move, talk, use hands, not in a mental state): " + YesNo(able));
            List<RestaurantController> restaurants = pawn.Spawned ? pawn.GetAllRestaurants() : new List<RestaurantController>();
            bool staff = restaurants.Any(r => r.HasToWork(pawn));
            sb.AppendLine("Restaurant staff on shift (staff never dine): " + YesNo(staff));
            bool pays = pawn.HasToPay();
            int silver = pawn.GetSilver();
            sb.AppendLine($"Pays for meals: {YesNo(pays)}. Silver carried: {silver}");
            sb.AppendLine();

            if (restaurants.Count == 0)
            {
                sb.AppendLine("No restaurants on this map.");
            }
            List<DiningSpot> spots = pawn.Spawned ? DiningUtility.FindDiningSpotsFor(pawn, allowDrug: false).ToList() : new List<DiningSpot>();
            foreach (RestaurantController r in restaurants)
            {
                sb.AppendLine($"== {r.Name}");
                sb.AppendLine($"Open for business: {YesNo(r.openForBusiness)}. A register's shift is active and staffed: {YesNo(r.Registers.Any(reg => reg != null && reg.IsActive))}");
                sb.AppendLine($"Lets this pawn in (allow colonists/guests/...): {YesNo(r.MayDineHere(pawn))}");
                int usable = spots.Count(s => r.diningSpots.Contains(s));
                sb.AppendLine($"Dining spots: {r.diningSpots.Count}, usable by this pawn (reachable, reservable, safe, allowed): {usable}");
                sb.AppendLine($"Price: {r.guestPricePercentage:P0} of the guest price");
                if (r.Stock.AllStock.Count == 0)
                {
                    sb.AppendLine("Nothing in stock.");
                }
                foreach (RestaurantStock.Stock stock in r.Stock.AllStock.Values)
                {
                    Thing item = stock.items.FirstOrDefault();
                    float price = stock.def.GetPrice(r);
                    bool willEat = Call<bool>(typeof(RestaurantStock), null, "WillConsume", pawn, false, stock.def);
                    bool afford = Call<bool>(typeof(RestaurantStock), r.Stock, "CanAfford", pawn, stock.def);
                    bool orderable = r.Orders.CanBeOrdered(item);
                    float score = item == null ? 0f : Call<float>(typeof(RestaurantStock), r.Stock, "GetMealOptimalityScore", pawn, item, true, true);
                    sb.AppendLine($"  {stock.def.label} x{stock.items.Sum(t => t.stackCount)}: price {price:0}, will eat {YesNo(willEat)}, can afford {YesNo(afford)}, orderable {YesNo(orderable)}, score {score:0} (needs 50)");
                }
                sb.AppendLine();
            }

            Thing best = RestaurantStock.GetBestMealFor(spots.SelectMany(s => s.GetRestaurantsServing()).Distinct(), pawn, out RestaurantController chosen, allowDrug: false);
            sb.AppendLine(best != null
                ? $"Result: would order {best.def.label} at {chosen.Name} when hungry."
                : "Result: no restaurant meal qualifies right now, so they eat elsewhere.");
            sb.AppendLine();
            sb.AppendLine("Mods patching the food search (a prefix that runs before Gastronomy's can skip it):");
            AppendPatches(sb, AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.TryFindBestFoodSourceFor)));
            AppendPatches(sb, AccessTools.Method(typeof(JobGiver_GetFood), "TryGiveJob"));
            return sb.ToString();
        }

        private static void AppendPatches(StringBuilder sb, MethodBase method)
        {
            Patches info = method == null ? null : Harmony.GetPatchInfo(method);
            sb.AppendLine("  " + method?.DeclaringType?.Name + "." + method?.Name + ":");
            if (info == null)
            {
                sb.AppendLine("    (not patched)");
                return;
            }
            foreach (Patch p in info.Prefixes.OrderByDescending(p => p.priority))
            {
                sb.AppendLine($"    prefix  {p.owner} ({p.PatchMethod.DeclaringType?.FullName}, priority {p.priority})");
            }
            foreach (Patch p in info.Postfixes.OrderByDescending(p => p.priority))
            {
                sb.AppendLine($"    postfix {p.owner} ({p.PatchMethod.DeclaringType?.FullName})");
            }
            foreach (Patch p in info.Transpilers)
            {
                sb.AppendLine($"    transpiler {p.owner} ({p.PatchMethod.DeclaringType?.FullName})");
            }
        }

        private static T Call<T>(System.Type type, object instance, string name, params object[] args)
        {
            try
            {
                MethodInfo method = AccessTools.Method(type, name);
                return method == null ? default : (T)method.Invoke(instance, args);
            }
            catch (System.Exception e)
            {
                Log.Warning("[Commoners: Wages] Restaurant check couldn't call " + name + ": " + e.GetBaseException().Message);
                return default;
            }
        }

        private static string YesNo(bool value) => value ? "yes" : "NO";
    }
}
