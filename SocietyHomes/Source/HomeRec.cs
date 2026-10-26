using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CommonersWages;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SocietyHomes
{
    // Household members relax at home when their lot has something to do. Two parts:
    // - Picking a kind of recreation: a joy giver with a usable building or artwork on their lot is
    //   homeRecFactor times as likely to be picked (vanilla still rolls, so walks and the rest still happen).
    // - Picking where: a building game (TV, chess, billiards, horseshoes, telescope...) or artwork on their
    //   own lot wins over a closer one elsewhere.
    // Only ordinary free time counts: not joy in bed, at a party or for a pawn in a lord (rituals, guests).
    public static class HomeRec
    {
        private static readonly Action<JoyGiver, Pawn, List<Thing>> getSearchSet =
            AccessTools.MethodDelegate<Action<JoyGiver, Pawn, List<Thing>>>(AccessTools.Method(typeof(JoyGiver), "GetSearchSet"));

        private static readonly Func<JoyGiver_InteractBuilding, Pawn, Thing, bool, bool> canInteractWith =
            AccessTools.MethodDelegate<Func<JoyGiver_InteractBuilding, Pawn, Thing, bool, bool>>(AccessTools.Method(typeof(JoyGiver_InteractBuilding), "CanInteractWith"));

        private static readonly List<Thing> tmpCandidates = new List<Thing>();

        // The pawn's home, when home recreation applies to them right now.
        public static HomeData HomeFor(Pawn pawn)
        {
            if (!HomesMod.Settings.homeRec || pawn?.Map == null || pawn.InBed() || pawn.GetLord() != null)
            {
                return null;
            }
            HomeData home = Homes.HomeOf(pawn);
            return home != null && home.Map == pawn.Map ? home : null;
        }

        // Replaces JoyGiver.GetChance in JobGiver_GetJoy.TryGiveJob.
        public static float Chance(JoyGiver worker, Pawn pawn, JobGiver_GetJoy giver)
        {
            float chance = worker.GetChance(pawn);
            if (chance <= 0f || giver is JobGiver_GetJoyInBed || giver is JobGiver_GetJoyInGatheringArea)
            {
                return chance;
            }
            HomeData home = HomeFor(pawn);
            if (home != null && HasHomeSpot(worker, pawn, home))
            {
                chance *= HomesMod.Settings.homeRecFactor;
            }
            return chance;
        }

        private static bool HasHomeSpot(JoyGiver worker, Pawn pawn, HomeData home)
        {
            if (worker is JoyGiver_InteractBuilding building)
            {
                return BestHomeGame(building, pawn, home, reach: false) != null;
            }
            if (worker is JoyGiver_ViewArt)
            {
                return AnyHomeArt(pawn, home);
            }
            return false;
        }

        // The closest building on the pawn's lot this joy giver can use, or null.
        public static Thing BestHomeGame(JoyGiver_InteractBuilding worker, Pawn pawn, HomeData home, bool reach)
        {
            try
            {
                getSearchSet(worker, pawn, tmpCandidates);
                tmpCandidates.RemoveAll(t => LotUtility.LotOf(t) != home.lot);
                if (tmpCandidates.Count == 0)
                {
                    return null;
                }
                Predicate<Thing> validator = t => canInteractWith(worker, pawn, t, false);
                if (!reach)
                {
                    return tmpCandidates.Find(validator);
                }
                return GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, tmpCandidates, PathEndMode.OnCell, TraverseParms.For(pawn), 9999f, validator);
            }
            finally
            {
                tmpCandidates.Clear();
            }
        }

        private static bool AnyHomeArt(Pawn pawn, HomeData home)
        {
            foreach (Thing t in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Art))
            {
                if (LotUtility.LotOf(t) == home.lot && CanViewArt(pawn, t, reach: false))
                {
                    return true;
                }
            }
            return false;
        }

        // Artwork on the pawn's lot, picked the way vanilla picks (weighted by beauty), or null.
        public static Thing RandomHomeArt(Pawn pawn, HomeData home)
        {
            try
            {
                foreach (Thing t in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Art))
                {
                    if (LotUtility.LotOf(t) == home.lot && CanViewArt(pawn, t, reach: true))
                    {
                        tmpCandidates.Add(t);
                    }
                }
                return tmpCandidates.TryRandomElementByWeight(t => Mathf.Max(t.GetStatValue(StatDefOf.Beauty), 0.5f), out Thing art) ? art : null;
            }
            finally
            {
                tmpCandidates.Clear();
            }
        }

        // JoyGiver_ViewArt's own checks.
        private static bool CanViewArt(Pawn pawn, Thing t, bool reach)
        {
            if (t.Faction != Faction.OfPlayer || t.Fogged() || t.VacuumConcernTo(pawn) || !t.IsPoliticallyProper(pawn) || t.IsForbidden(pawn))
            {
                return false;
            }
            if (!JoyUtility.EnjoyableOutsideNow(pawn) && !t.Position.Roofed(t.Map))
            {
                return false;
            }
            CompArt art = t.TryGetComp<CompArt>();
            if (art == null || !art.CanShowArt || !art.Props.canBeEnjoyedAsArt)
            {
                return false;
            }
            Room room = t.GetRoom();
            if (room == null || (room.Role.avoidViewingArtIfUnowned && pawn.ownership?.OwnedRoom != room))
            {
                return false;
            }
            return !reach || pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.None);
        }
    }

    [HarmonyPatch(typeof(JobGiver_GetJoy), "TryGiveJob")]
    public static class Patch_JobGiver_GetJoy_TryGiveJob_HomeRec
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getChance = AccessTools.Method(typeof(JoyGiver), nameof(JoyGiver.GetChance));
            MethodInfo chance = AccessTools.Method(typeof(HomeRec), nameof(HomeRec.Chance));
            bool found = false;
            foreach (CodeInstruction ins in instructions)
            {
                if (ins.Calls(getChance))
                {
                    found = true;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, chance).WithLabels(ins.labels).WithBlocks(ins.blocks);
                    continue;
                }
                yield return ins;
            }
            if (!found)
            {
                Log.Warning("[Society: Homes] JobGiver_GetJoy.TryGiveJob has no GetChance call; home recreation won't be favored.");
            }
        }
    }

    [HarmonyPatch(typeof(JoyGiver_InteractBuilding), "FindBestGame")]
    public static class Patch_JoyGiver_InteractBuilding_FindBestGame_HomeRec
    {
        public static void Postfix(JoyGiver_InteractBuilding __instance, Pawn pawn, bool inBed, IntVec3 gatheringSpot, ref Thing __result)
        {
            if (inBed || gatheringSpot.IsValid || !Homes.AnyHomes)
            {
                return;
            }
            HomeData home = HomeRec.HomeFor(pawn);
            if (home == null || (__result != null && LotUtility.LotOf(__result) == home.lot))
            {
                return;
            }
            Thing atHome = HomeRec.BestHomeGame(__instance, pawn, home, reach: true);
            if (atHome != null)
            {
                __result = atHome;
            }
        }
    }

    [HarmonyPatch(typeof(JoyGiver_ViewArt), nameof(JoyGiver_ViewArt.TryGiveJob))]
    public static class Patch_JoyGiver_ViewArt_TryGiveJob_HomeRec
    {
        public static void Postfix(JoyGiver_ViewArt __instance, Pawn pawn, ref Job __result)
        {
            if (!Homes.AnyHomes)
            {
                return;
            }
            HomeData home = HomeRec.HomeFor(pawn);
            if (home == null || (__result != null && LotUtility.LotOf(__result.targetA.Thing) == home.lot))
            {
                return;
            }
            Thing art = HomeRec.RandomHomeArt(pawn, home);
            if (art != null)
            {
                __result = JobMaker.MakeJob(__instance.def.jobDef, art);
            }
        }
    }
}
