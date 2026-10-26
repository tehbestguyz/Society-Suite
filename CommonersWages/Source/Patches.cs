using System.Collections;
using System.Reflection;
using CashRegister;
using Commoners;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CommonersWages
{
    // ===== Savings stay in the commoner's pocket =====

    // The last line of defense: nothing (CE loadouts, other mods, vanilla) can drop a working pawn's
    // savings on the ground. Every inventory drop goes through one of these two base overloads; the
    // generic ThingOwner<T> versions call them. A blocked drop is logged once per pawn with its caller.
    public static class SavingsDropGuard
    {
        private static readonly System.Collections.Generic.HashSet<int> Reported = new System.Collections.Generic.HashSet<int>();

        // How many silver may leave this container from `thing`, or -1 if the guard doesn't apply.
        public static int AllowedDrop(ThingOwner owner, Thing thing)
        {
            if (thing?.def != ThingDefOf.Silver || !(owner?.Owner is Pawn_InventoryTracker inventory))
            {
                return -1;
            }
            Pawn pawn = inventory.pawn;
            // Dying, downed (stripped, rescued) or off-map pawns drop as normal.
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned)
            {
                return -1;
            }
            int protectedSilver = WageUtility.ProtectedSilver(pawn);
            if (protectedSilver <= 0)
            {
                return -1;
            }
            return Mathf.Max(0, WageUtility.SilverCount(pawn) - protectedSilver);
        }

        public static void Report(Pawn pawn, int wanted, int allowed)
        {
            if (!Reported.Add(pawn.thingIDNumber))
            {
                return;
            }
            string caller = new System.Diagnostics.StackTrace(2, false).ToString();
            string[] lines = caller.Split('\n');
            caller = string.Join("\n", lines, 0, Mathf.Min(8, lines.Length));
            Log.Warning($"[Commoners: Wages] Kept {pawn.LabelShort}'s savings from being dropped (tried {wanted}, allowed {allowed}). Called from:\n{caller}");
        }
    }

    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryDrop),
        new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(int), typeof(Thing), typeof(System.Action<Thing, int>), typeof(System.Predicate<IntVec3>) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
    public static class Patch_ThingOwner_TryDrop_Count
    {
        public static bool Prefix(ThingOwner __instance, Thing thing, ref int count, ref Thing resultingThing, ref bool __result)
        {
            int allowed = SavingsDropGuard.AllowedDrop(__instance, thing);
            if (allowed < 0 || count <= allowed)
            {
                return true;
            }
            SavingsDropGuard.Report(((Pawn_InventoryTracker)__instance.Owner).pawn, count, allowed);
            if (allowed == 0)
            {
                resultingThing = null;
                __result = false;
                return false;
            }
            count = allowed;
            return true;
        }
    }

    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryDrop),
        new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing), typeof(System.Action<Thing, int>), typeof(System.Predicate<IntVec3>), typeof(bool) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
    public static class Patch_ThingOwner_TryDrop_Whole
    {
        // Drops the whole stack, so a partly protected stack is redirected to the counted overload.
        public static bool Prefix(ThingOwner __instance, Thing thing, IntVec3 dropLoc, Map map, ThingPlaceMode mode,
            ref Thing lastResultingThing, System.Action<Thing, int> placedAction, System.Predicate<IntVec3> nearPlaceValidator, ref bool __result)
        {
            int allowed = SavingsDropGuard.AllowedDrop(__instance, thing);
            if (allowed < 0 || thing.stackCount <= allowed)
            {
                return true;
            }
            SavingsDropGuard.Report(((Pawn_InventoryTracker)__instance.Owner).pawn, thing.stackCount, allowed);
            if (allowed == 0)
            {
                lastResultingThing = null;
                __result = false;
                return false;
            }
            __result = __instance.TryDrop(thing, dropLoc, map, mode, allowed, out lastResultingThing, placedAction, nearPlaceValidator);
            return false;
        }
    }

    // Vanilla unloading (after caravans, transport pods, "unload everything") picks items through this.
    [HarmonyPatch(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.FirstUnloadableThing), MethodType.Getter)]
    public static class Patch_FirstUnloadableThing
    {
        public static void Prefix(Pawn_InventoryTracker __instance)
        {
            if (WageUtility.ProtectedSilver(__instance.pawn) > 0)
            {
                WageUtility.MoveSilverToEnd(__instance.innerContainer);
            }
        }

        public static void Postfix(Pawn_InventoryTracker __instance, ref ThingCount __result)
        {
            if (__result.Thing == null || __result.Thing.def != ThingDefOf.Silver)
            {
                return;
            }
            int protectedSilver = WageUtility.ProtectedSilver(__instance.pawn);
            if (protectedSilver <= 0)
            {
                return;
            }
            int unloadable = WageUtility.SilverCount(__instance.pawn) - protectedSilver;
            __result = unloadable <= 0 ? default : new ThingCount(__result.Thing, Mathf.Min(__result.Count, unloadable));
        }
    }

    // Combat Extended counts inventory per def for loadouts; savings are left out so they're never "excess".
    [HarmonyPatch]
    public static class Patch_CE_GetStorageByThingDef
    {
        private static FieldInfo integerValue;

        public static bool Prepare()
        {
            return AccessTools.TypeByName("CombatExtended.Utility_HoldTracker") != null;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method("CombatExtended.Utility_HoldTracker:GetStorageByThingDef");
        }

        public static void Postfix(Pawn pawn, IDictionary __result)
        {
            if (__result == null || !__result.Contains(ThingDefOf.Silver))
            {
                return;
            }
            int protectedSilver = WageUtility.ProtectedSilver(pawn);
            if (protectedSilver <= 0)
            {
                return;
            }
            object entry = __result[ThingDefOf.Silver];
            integerValue ??= AccessTools.Field(entry.GetType(), "value");
            int remaining = (int)integerValue.GetValue(entry) - protectedSilver;
            if (remaining <= 0)
            {
                __result.Remove(ThingDefOf.Silver);
            }
            else
            {
                integerValue.SetValue(entry, remaining);
            }
        }
    }

    // Combat Extended's own unload job picks what to drop through this.
    [HarmonyPatch]
    public static class Patch_CE_GetAnythingForDrop
    {
        public static bool Prepare()
        {
            return AccessTools.TypeByName("CombatExtended.Utility_HoldTracker") != null;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method("CombatExtended.Utility_HoldTracker:GetAnythingForDrop");
        }

        public static void Prefix(Pawn pawn)
        {
            if (WageUtility.ProtectedSilver(pawn) > 0)
            {
                WageUtility.MoveSilverToEnd(pawn.inventory?.innerContainer);
            }
        }

        public static void Postfix(Pawn pawn, ref Thing dropThing, ref int dropCount, ref bool __result)
        {
            if (!__result || dropThing == null || dropThing.def != ThingDefOf.Silver)
            {
                return;
            }
            int protectedSilver = WageUtility.ProtectedSilver(pawn);
            if (protectedSilver <= 0)
            {
                return;
            }
            int unloadable = WageUtility.SilverCount(pawn) - protectedSilver;
            if (unloadable <= 0)
            {
                __result = false;
                dropThing = null;
                dropCount = 0;
            }
            else
            {
                dropCount = Mathf.Min(dropCount, unloadable);
            }
        }
    }

    // ===== Payroll registers =====

    // Cash Register empties any register holding silver; payroll registers are meant to hold it.
    [HarmonyPatch(typeof(Building_CashRegister), nameof(Building_CashRegister.ShouldEmpty), MethodType.Getter)]
    public static class Patch_CashRegister_ShouldEmpty
    {
        public static void Postfix(Building_CashRegister __instance, ref bool __result)
        {
            if (__result && WageUtility.IsPayroll(__instance))
            {
                __result = false;
            }
        }
    }

    // ShouldEmpty is a one-line getter, and the JIT can inline it into Cash Register's work giver,
    // which skips the patch above. That let haulers empty payroll registers onto the floor while the
    // radius pull refilled them, in a loop. The work giver itself is patched too.
    // Commoners need a home of their own (owned or rented) before rising to any higher caste, except
    // enlisting as a soldier: military service is the other way up.
    [HarmonyPatch(typeof(CasteUtility), nameof(CasteUtility.CanChangeCaste))]
    public static class Patch_CasteUtility_CanChangeCaste
    {
        public static void Postfix(Pawn p, Caste target, ref AcceptanceReport __result)
        {
            if (!__result.Accepted || target <= Caste.Commoner || target == Caste.Soldier)
            {
                return;
            }
            AcceptanceReport home = LeaseUtility.CanLeaveCommoners(p);
            if (!home.Accepted)
            {
                __result = home;
            }
        }
    }

    [HarmonyPatch(typeof(WorkGiver_EmptyRegister), nameof(WorkGiver_EmptyRegister.HasJobOnThing))]
    public static class Patch_WorkGiver_EmptyRegister_HasJob
    {
        public static bool Prefix(Thing t, ref bool __result)
        {
            if (t is Building_CashRegister register && WageUtility.IsPayroll(register))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_EmptyRegister), nameof(WorkGiver_EmptyRegister.JobOnThing))]
    public static class Patch_WorkGiver_EmptyRegister_JobOn
    {
        private static JobDef emptyRegister;

        public static bool Prefix(Thing t, ref Verse.AI.Job __result)
        {
            if (t is Building_CashRegister register && WageUtility.IsPayroll(register))
            {
                __result = null;
                return false;
            }
            return true;
        }

        // Cash Register declares its DefOf fields `static readonly`. On .NET 10 (Waylith) reflection
        // can't set a readonly static once its class is initialized, so CashRegister_EmptyRegister can
        // stay null and the work giver hands out jobs with no def. The first time a restaurant register
        // had silver to empty, StartJob crashed and the pawn locked up the game. Fill the def in here.
        public static void Postfix(ref Verse.AI.Job __result)
        {
            if (__result == null || __result.def != null)
            {
                return;
            }
            emptyRegister ??= DefDatabase<JobDef>.GetNamedSilentFail("CashRegister_EmptyRegister");
            if (emptyRegister != null)
            {
                __result.def = emptyRegister;
            }
            else
            {
                __result = null;
            }
        }
    }

    // ===== Pick Up And Haul =====

    // PUAH unloads what it hauled into a pawn's inventory. Hauled silver merges into the savings stack,
    // and PUAH then unloads the whole stack (it uses the stack's full count), taking the savings to
    // storage. Here the unprotected part is split off into its own stack and only that is unloaded;
    // if nothing is unprotected, silver is dropped from PUAH's list of hauled things.
    [HarmonyPatch]
    public static class Patch_PUAH_FirstUnloadableThing
    {
        private const string TypeName = "PickUpAndHaul.JobDriver_UnloadYourHauledInventory";

        public static bool Prepare()
        {
            return AccessTools.TypeByName(TypeName) != null;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName(TypeName), "FirstUnloadableThing");
        }

        public static void Postfix(Pawn pawn, System.Collections.Generic.HashSet<Thing> carriedThings, ref ThingCount __result)
        {
            Thing thing = __result.Thing;
            if (thing == null || thing.def != ThingDefOf.Silver)
            {
                return;
            }
            int protectedSilver = WageUtility.ProtectedSilver(pawn);
            if (protectedSilver <= 0)
            {
                return;
            }
            int unloadable = WageUtility.SilverCount(pawn) - protectedSilver;
            if (unloadable <= 0)
            {
                // All of it is savings: forget the silver and pick the next hauled thing instead.
                carriedThings.RemoveWhere(t => t.def == ThingDefOf.Silver);
                __result = (ThingCount)TargetMethod().Invoke(null, new object[] { pawn, carriedThings });
                return;
            }
            if (thing.stackCount <= unloadable)
            {
                return;
            }
            Thing part = thing.SplitOff(unloadable);
            pawn.inventory.innerContainer.TryAdd(part, canMergeWithExistingStacks: false);
            carriedThings.Remove(thing);
            carriedThings.Add(part);
            __result = new ThingCount(part, part.stackCount);
        }
    }

    // ===== Build From Inventory =====

    // Build From Inventory delivers construction materials from the builder's own inventory first,
    // then from other colonists'. Its job starts by dropping the stack; when that's savings the drop
    // guard refuses, the job fails, and the same silver is picked again at once ("started 10 jobs in
    // one tick"). Silver that can't all be dropped is skipped, so the builder fetches stockpile silver.
    public static class BuildFromInventoryCompat
    {
        public const string TypeName = "Build_From_Inventory.UseInventory";

        // False for a silver stack the drop guard would block or cut short. A partly protected stack
        // is skipped too: Build From Inventory reserves the full count on the dropped stack.
        public static bool CanUse(Thing thing)
        {
            int allowed = SavingsDropGuard.AllowedDrop(thing.holdingOwner, thing);
            return allowed < 0 || allowed >= thing.stackCount;
        }
    }

    [HarmonyPatch]
    public static class Patch_BFI_FindInOwnInventory
    {
        public static bool Prepare()
        {
            return AccessTools.TypeByName(BuildFromInventoryCompat.TypeName) != null;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName(BuildFromInventoryCompat.TypeName), "FindInOwnInventory");
        }

        public static void Postfix(ref Thing __result)
        {
            if (__result != null && !BuildFromInventoryCompat.CanUse(__result))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_BFI_FindInOthersInventory
    {
        public static bool Prepare()
        {
            return AccessTools.TypeByName(BuildFromInventoryCompat.TypeName) != null;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName(BuildFromInventoryCompat.TypeName), "FindInOthersInventory");
        }

        // It returns the first match, so when that's someone's savings the search is redone without them,
        // with the same checks Build From Inventory makes.
        public static void Postfix(ThingDef def, Pawn worker, ref Thing __result)
        {
            if (__result == null || BuildFromInventoryCompat.CanUse(__result))
            {
                return;
            }
            __result = null;
            foreach (Pawn other in worker.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (other.Position.IsForbidden(worker) || other.InMentalState
                    || !worker.CanReach(other, PathEndMode.OnCell, Danger.Some))
                {
                    continue;
                }
                foreach (Thing thing in other.inventory.innerContainer)
                {
                    if (thing.def == def && worker.CanReserve(thing) && BuildFromInventoryCompat.CanUse(thing))
                    {
                        __result = thing;
                        return;
                    }
                }
            }
        }
    }

    // ===== Gastronomy: paid colonists pay for restaurant meals =====

    // Gastronomy only charges guests. Paying patrons also get its menu affordability check and
    // "bought food" thought, so a colonist who can't afford a meal just eats somewhere else.
    // Nobles never pay.
    [HarmonyPatch]
    public static class Patch_Gastronomy_HasToPay
    {
        public static bool Prepare()
        {
            return AccessTools.TypeByName("Gastronomy.Dining.DiningUtility") != null;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method("Gastronomy.Dining.DiningUtility:HasToPay");
        }

        public static void Postfix(Pawn patron, ref bool __result)
        {
            if (!__result && WagesMod.Settings.gastronomyPays && WageUtility.EarnsWages(patron))
            {
                __result = true;
            }
        }
    }
}
