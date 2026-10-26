using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommonersWages;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SocietyHomes
{
    public class CompProperties_Pantry : CompProperties
    {
        public CompProperties_Pantry()
        {
            compClass = typeof(CompPantry);
        }
    }

    // On storage buildings: whether this is the pantry of the household living on its lot.
    public class CompPantry : ThingComp
    {
        public bool pantry;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || LotUtility.LotOf(parent) == null)
            {
                yield break;
            }
            yield return Pantry.Toggle(() => pantry, v =>
            {
                pantry = v;
                if (v && parent is IStoreSettingsParent storage)
                {
                    Pantry.LimitToFood(storage.GetStoreSettings());
                }
                Homes.SetDirty();
            });
        }

        public override string CompInspectStringExtra()
        {
            return pantry ? Pantry.InspectLine(LotUtility.LotOf(parent)) : null;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref pantry, "shPantry", false);
        }
    }

    // While these are set, the forbid and storage checks below know whose errand it is.
    public static class HomesContext
    {
        // A pawn looking for food for themselves.
        [ThreadStatic]
        public static Pawn foodSearcher;
        // A home cook looking for ingredients: only this lot's pantry will do.
        [ThreadStatic]
        public static Zone_Lot pantryOnly;
        // Groceries being put away: only this lot's pantry takes them.
        [ThreadStatic]
        public static Zone_Lot storeInto;
    }

    public static class Pantry
    {
        public static bool IsMeal(ThingDef def)
        {
            return def.ingestible != null && def.IsNutritionGivingIngestible && def.ingestible.IsMeal && !def.IsDrug && def.ingestible.HumanEdible;
        }

        public static bool IsRawFood(ThingDef def)
        {
            return def.ingestible != null && def.IsNutritionGivingIngestible && !def.IsDrug && def.ingestible.HumanEdible
                && (def.ingestible.preferability == FoodPreferability.RawBad || def.ingestible.preferability == FoodPreferability.RawTasty);
        }

        public static bool IsMarked(ISlotGroupParent parent, HomesMapComponent comp)
        {
            switch (parent)
            {
                case Zone_Stockpile zone:
                    return comp != null && comp.IsPantry(zone);
                case ThingWithComps thing:
                    return thing.GetComp<CompPantry>()?.pantry ?? false;
                default:
                    return false;
            }
        }

        public static void ClearForSale(ISlotGroupParent parent, Map map)
        {
            switch (parent)
            {
                case Zone_Stockpile zone:
                    MarketMapComponent market = map.GetComponent<MarketMapComponent>();
                    if (market != null && market.IsForSale(zone))
                    {
                        market.SetForSale(zone, false);
                    }
                    break;
                case ThingWithComps thing:
                    CompForSale forSale = thing.GetComp<CompForSale>();
                    if (forSale != null)
                    {
                        forSale.forSale = false;
                    }
                    break;
            }
        }

        // For-sale storage: Wages' "For sale" toggle on a stockpile or storage building. Pantries never are.
        public static bool IsForSaleGroup(SlotGroup group, MarketMapComponent market)
        {
            switch (group.parent)
            {
                case Zone_Stockpile zone:
                    return market != null && market.IsForSale(zone);
                case ThingWithComps thing:
                    return thing.GetComp<CompForSale>()?.forSale ?? false;
                default:
                    return false;
            }
        }

        // A new pantry stops accepting anything that isn't human food (meals or raw ingredients), so
        // whatever else is on the shelf gets hauled off by the colony. The player can widen it again.
        public static void LimitToFood(StorageSettings settings)
        {
            ThingFilter filter = settings?.filter;
            if (filter == null)
            {
                return;
            }
            foreach (ThingDef def in filter.AllowedThingDefs.ToList())
            {
                if (!IsMeal(def) && !IsRawFood(def))
                {
                    filter.SetAllow(def, false);
                }
            }
        }

        public static Command_Toggle Toggle(Func<bool> get, Action<bool> set)
        {
            return new Command_Toggle
            {
                defaultLabel = "SH_Pantry".Translate(),
                defaultDesc = "SH_PantryDesc".Translate(),
                icon = ThingDefOf.MealSimple.uiIcon,
                isActive = get,
                toggleAction = () => set(!get())
            };
        }

        public static string InspectLine(Zone_Lot lot)
        {
            Pawn resident = lot?.Resident;
            return resident != null ? "SH_PantryInspect".Translate(resident.LabelShort).ToString() : "SH_PantryInspectVacant".Translate().ToString();
        }

        // The pawn is taking pantry food for their own household's use right now: looking for a meal,
        // eating, or on a grocery run or home cooking job.
        public static bool OnHouseholdErrand(Pawn pawn)
        {
            if (HomesContext.foodSearcher == pawn || HomesContext.storeInto != null)
            {
                return true;
            }
            Job job = pawn.CurJob;
            if (job == null)
            {
                return false;
            }
            return job.def == JobDefOf.Ingest || job.def == SH_DefOf.SH_GroceryRun || job.bill is HouseholdBill;
        }
    }

    // Pantry food (food only) is forbidden to everyone outside the household, and to the household too except on
    // their own errands, so it never ends up hauled, cooked into colony bills, packed or loaded out.
    // During a home cook's ingredient search, everything outside their own pantry is forbidden instead.
    [HarmonyPatch]
    public static class Patch_ForbidUtility_IsForbidden_Pantry
    {
        public static MethodBase TargetMethod() => AccessTools.Method(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new[] { typeof(Thing), typeof(Pawn) });

        public static void Postfix(Thing t, Pawn pawn, ref bool __result)
        {
            if (__result || t == null || pawn == null || !Homes.AnyPantries)
            {
                return;
            }
            if (HomesContext.pantryOnly != null)
            {
                if (Homes.PantryLotOf(t) != HomesContext.pantryOnly)
                {
                    __result = true;
                }
                return;
            }
            // Only food is the household's. Anything else left on a pantry shelf is the colony's to move.
            if (t.def.category != ThingCategory.Item || !t.def.IsNutritionGivingIngestible || !t.Spawned || pawn.Faction != Faction.OfPlayer)
            {
                return;
            }
            Zone_Lot lot = Homes.PantryLotAt(t.Map, t.Position);
            if (lot == null)
            {
                return;
            }
            if (Homes.HomeOf(pawn)?.lot != lot || !Pantry.OnHouseholdErrand(pawn))
            {
                __result = true;
            }
        }
    }

    // Nothing is hauled into a pantry except by its household's own jobs: groceries being put away, and a
    // home cook storing the meal. Those go only into that household's pantry. A pantry on a vacant lot
    // takes nothing at all.
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell))]
    public static class Patch_StoreUtility_IsGoodStoreCell
    {
        public static void Postfix(IntVec3 c, Map map, Pawn carrier, ref bool __result)
        {
            if (!__result || !Homes.AnyMarked)
            {
                return;
            }
            Zone_Lot target = HomesContext.storeInto ?? (carrier?.CurJob?.bill is HouseholdBill bill ? bill.Lot : null);
            if (target != null)
            {
                __result = Homes.PantryLotAt(map, c) == target;
            }
            else if (Homes.IsClosedCell(map, c))
            {
                __result = false;
            }
        }
    }

    // A hungry household member prefers their own pantry's food, worth a walk of `pantryBonus` extra cells.
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality))]
    public static class Patch_FoodUtility_FoodOptimality
    {
        public static void Postfix(Pawn eater, Thing foodSource, ref float __result)
        {
            if (!Homes.AnyPantries || eater == null || foodSource == null || !foodSource.Spawned)
            {
                return;
            }
            Zone_Lot lot = Homes.PantryLotAt(foodSource.Map, foodSource.Position);
            if (lot != null && Homes.HomeOf(eater)?.lot == lot)
            {
                __result += HomesMod.Settings.pantryBonus;
            }
        }
    }

    // Marks a pawn's search for their own meal, so their pantry counts as theirs during it.
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap))]
    public static class Patch_FoodUtility_BestFoodSourceOnMap_Pantry
    {
        public static void Prefix(Pawn getter, Pawn eater, out Pawn __state)
        {
            __state = HomesContext.foodSearcher;
            HomesContext.foodSearcher = getter == eater ? eater : null;
        }

        public static void Finalizer(Pawn __state)
        {
            HomesContext.foodSearcher = __state;
        }
    }

    // Stockpile zones on a lot get the pantry toggle.
    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.GetGizmos))]
    public static class Patch_Zone_Stockpile_GetGizmos_Pantry
    {
        public static void Postfix(Zone_Stockpile __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = Append(__result, __instance);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> gizmos, Zone_Stockpile zone)
        {
            foreach (Gizmo g in gizmos)
            {
                yield return g;
            }
            HomesMapComponent comp = zone.Map?.GetComponent<HomesMapComponent>();
            if (comp != null && zone.cells.Count > 0 && LotUtility.LotAt(zone.cells[0], zone.Map) != null)
            {
                yield return Pantry.Toggle(() => comp.IsPantry(zone), v =>
                {
                    if (v)
                    {
                        Pantry.LimitToFood(zone.settings);
                    }
                    comp.SetPantry(zone, v);
                });
            }
        }
    }

    // Zone_Stockpile doesn't override GetInspectString, so this patches Zone's.
    [HarmonyPatch(typeof(Zone), nameof(Zone.GetInspectString))]
    public static class Patch_Zone_GetInspectString_Pantry
    {
        public static void Postfix(Zone __instance, ref string __result)
        {
            if (!(__instance is Zone_Stockpile zone))
            {
                return;
            }
            HomesMapComponent comp = zone.Map?.GetComponent<HomesMapComponent>();
            if (comp == null || !comp.IsPantry(zone) || zone.cells.Count == 0)
            {
                return;
            }
            string line = Pantry.InspectLine(LotUtility.LotAt(zone.cells[0], zone.Map));
            __result = __result.NullOrEmpty() ? line : __result + "\n" + line;
        }
    }
}
