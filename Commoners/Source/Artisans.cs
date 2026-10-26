using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Commoners
{
    // Items made in an artisan's specialty can come out one quality level higher (up to masterwork).
    // Masterwork or better makes them proud; a shabby workshop makes them grumble.
    [HarmonyPatch(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn),
        new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) })]
    public static class Patch_GenerateQualityCreatedByPawn
    {
        public static void Postfix(Pawn pawn, SkillDef relevantSkill, ref QualityCategory __result)
        {
            CM_SpecialtyDef specialty = CasteUtility.Specialty(pawn);
            if (specialty == null || specialty.skill != relevantSkill || !specialty.makesQualityItems)
            {
                return;
            }
            if (__result < QualityCategory.Masterwork && Rand.Chance(CommonersMod.Settings.artisanQualityChance))
            {
                __result += 1;
            }
            MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts.memories;
            if (memories != null)
            {
                if (__result >= QualityCategory.Masterwork)
                {
                    memories.TryGainMemory(CM_DefOf.CM_FinestWork);
                }
                if (specialty.caresAboutWorkshop && pawn.Spawned)
                {
                    Room room = pawn.GetRoom();
                    if (room == null || room.PsychologicallyOutdoors
                        || room.GetStat(RoomStatDefOf.Impressiveness) < CommonersMod.Settings.artisanWorkshopImpressiveness)
                    {
                        memories.TryGainMemory(CM_DefOf.CM_PoorWorkshop);
                    }
                }
            }
            CasteUtility.NotifyArtisanMadeItem(pawn, __result);
        }
    }

    // Artisans and scholars get inspired more often.
    [HarmonyPatch(typeof(InspirationHandler), "StartInspirationMTBDays", MethodType.Getter)]
    public static class Patch_StartInspirationMTBDays
    {
        public static void Postfix(InspirationHandler __instance, ref float __result)
        {
            if (__result > 0f && CasteUtility.IsSpecialist(__instance.pawn))
            {
                __result /= UnityEngine.Mathf.Max(1f, CommonersMod.Settings.artisanInspirationFactor);
            }
        }
    }

    // Artisans and scholars resent menial work. Colony nobles can't be given it at all (see CasteWork).
    // Scholars also want an impressive room for their specialty's work: a lab, a hospital.
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_Pawn_JobTracker_StartJob
    {
        public static void Postfix(Pawn ___pawn, Job newJob)
        {
            WorkTypeDef work = newJob?.workGiverDef?.workType;
            if (work == null || ___pawn?.needs?.mood == null)
            {
                return;
            }
            // Soldiers dislike skilled labor: crafting, study, doctoring.
            if (CasteUtility.IsSoldier(___pawn))
            {
                if (CommonersMod.Settings.SkilledWork.Contains(work))
                {
                    ___pawn.needs.mood.thoughts.memories.TryGainMemory(CM_DefOf.CM_NotSoldiersWork);
                }
                return;
            }
            if (!CasteUtility.IsSpecialist(___pawn))
            {
                return;
            }
            if (CommonersMod.Settings.MenialWork.Contains(work))
            {
                ___pawn.needs.mood.thoughts.memories.TryGainMemory(CM_DefOf.CM_BeneathMyStation);
                return;
            }
            CM_SpecialtyDef specialty = CasteUtility.Specialty(___pawn);
            if (specialty?.workType != work || !___pawn.Spawned)
            {
                return;
            }
            // The room the work happens in: the bench, the patient or the entity.
            LocalTargetInfo target = newJob.targetA;
            Room room = target.Thing != null ? target.Thing.GetRoom() : target.Cell.GetRoom(___pawn.Map);
            if (room == null || room.PsychologicallyOutdoors || room.GetStat(RoomStatDefOf.Impressiveness) < CommonersMod.Settings.artisanWorkshopImpressiveness)
            {
                ___pawn.needs.mood.thoughts.memories.TryGainMemory(CM_DefOf.CM_PoorStudy);
            }
        }
    }

    public static class CasteWork
    {
        // The work types a pawn's caste bars them from, or null if none.
        public static List<WorkTypeDef> BannedWork(Pawn p)
        {
            switch (CasteUtility.GetCaste(p))
            {
                case Caste.Commoner:
                    return CommonersMod.Settings.RestrictedWork;
                case Caste.Noble:
                    return CasteUtility.IsColonyNoble(p) ? CommonersMod.Settings.MenialWork : null;
                default:
                    return null;
            }
        }
    }
}
