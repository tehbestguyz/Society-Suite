using System.Collections.Generic;
using CommonersWages;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SocietyHomes
{
    // Eating a meal from their own pantry cheers a household member up. The meal is picked up (split off
    // the pantry stack) before it's eaten, so the eating job is noted when it starts, while the meal is
    // still on the shelf, and the thought comes when that same job finishes eating it.
    public static class HomeMeals
    {
        private static readonly Dictionary<Pawn, int> pendingJob = new Dictionary<Pawn, int>();

        public static void NoteJobStarted(Pawn pawn, Job job)
        {
            if (!Homes.AnyPantries || job.def != JobDefOf.Ingest)
            {
                return;
            }
            Thing food = job.targetA.Thing;
            if (food == null || !Pantry.IsMeal(food.def))
            {
                return;
            }
            Zone_Lot lot = Homes.PantryLotOf(food);
            if (lot != null && Homes.HomeOf(pawn)?.lot == lot)
            {
                pendingJob[pawn] = job.loadID;
            }
            else
            {
                pendingJob.Remove(pawn);
            }
        }

        public static void NoteIngested(Thing food, Pawn ingester)
        {
            if (ingester == null || !pendingJob.TryGetValue(ingester, out int jobID))
            {
                return;
            }
            if (ingester.CurJob?.loadID != jobID || !Pantry.IsMeal(food.def))
            {
                return;
            }
            pendingJob.Remove(ingester);
            ingester.needs?.mood?.thoughts?.memories?.TryGainMemory(SH_DefOf.SH_AteAtHome);
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_Pawn_JobTracker_StartJob_HomeMeals
    {
        public static void Postfix(Pawn ___pawn, Job newJob)
        {
            if (___pawn != null && newJob?.def != null)
            {
                HomeMeals.NoteJobStarted(___pawn, newJob);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Patch_Thing_Ingested_HomeMeals
    {
        public static void Postfix(Thing __instance, Pawn ingester)
        {
            HomeMeals.NoteIngested(__instance, ingester);
        }
    }
}
