using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Commoners
{
    // ===== Work restrictions =====

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes))]
    public static class Patch_Pawn_GetDisabledWorkTypes
    {
        public static void Postfix(Pawn __instance, List<WorkTypeDef> __result)
        {
            List<WorkTypeDef> banned = CasteWork.BannedWork(__instance);
            if (__result == null || banned == null)
            {
                return;
            }
            foreach (WorkTypeDef work in banned)
            {
                if (!__result.Contains(work))
                {
                    __result.Add(work);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetReasonsForDisabledWorkType))]
    public static class Patch_Pawn_GetReasonsForDisabledWorkType
    {
        public static void Postfix(Pawn __instance, WorkTypeDef workType, List<string> __result)
        {
            List<WorkTypeDef> banned = CasteWork.BannedWork(__instance);
            if (__result == null || banned == null || !banned.Contains(workType))
            {
                return;
            }
            string reason = "CM_WorkDisabledByCaste".Translate(__instance.Named("PAWN"), CasteUtility.Label(CasteUtility.GetCaste(__instance)).Named("CASTE"));
            if (!__result.Contains(reason))
            {
                __result.Add(reason);
            }
        }
    }

    // ===== Expectations =====

    [HarmonyPatch(typeof(ThoughtWorker_Expectations), "CurrentStateInternal")]
    public static class Patch_ThoughtWorker_Expectations
    {
        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            if (CasteExpectations.HasCasteExpectations(p))
            {
                __result = ThoughtState.Inactive;
            }
        }
    }

    // ===== Skill caps =====

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class Patch_SkillRecord_Learn
    {
        public static void Prefix(SkillRecord __instance, Pawn ___pawn, ref float xp, out int __state)
        {
            __state = __instance.levelInt;
            if (xp <= 0f)
            {
                return;
            }
            Caste caste = CasteUtility.GetCaste(___pawn);
            int cap = CasteUtility.SkillCapFor(___pawn, __instance.def);
            if (cap >= 0 && __instance.levelInt >= cap)
            {
                xp = 0f;
            }
            else if (CasteUtility.IsSpecialistCaste(caste) && CasteUtility.Specialty(___pawn)?.skill == __instance.def)
            {
                xp *= CommonersMod.Settings.artisanLearnFactor;
            }
            else if (caste == Caste.Soldier && CasteUtility.IsCombatSkill(__instance.def))
            {
                xp *= CommonersMod.Settings.soldierLearnFactor;
            }
        }

        public static void Postfix(SkillRecord __instance, Pawn ___pawn, int __state)
        {
            if (__instance.levelInt <= __state)
            {
                return;
            }
            int cap = CasteUtility.SkillCapFor(___pawn, __instance.def);
            if (cap >= 0 && __instance.levelInt > cap)
            {
                __instance.levelInt = Math.Max(cap, __state);
                __instance.xpSinceLastLevel = __instance.XpRequiredForLevelUp - 1f;
            }
        }
    }

    // ===== Romance =====

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.SecondaryRomanceChanceFactor))]
    public static class Patch_SecondaryRomanceChanceFactor
    {
        public static void Postfix(Pawn ___pawn, Pawn otherPawn, ref float __result)
        {
            if (__result <= 0f || otherPawn == null || ___pawn.Faction != Faction.OfPlayer || otherPawn.Faction != Faction.OfPlayer)
            {
                return;
            }
            if (___pawn.IsSlave || otherPawn.IsSlave)
            {
                return;
            }
            int gap = Math.Abs(CasteUtility.Tier(___pawn) - CasteUtility.Tier(otherPawn));
            if (gap > 0)
            {
                __result *= Mathf.Pow(CommonersMod.Settings.crossCasteRomanceFactor, gap);
            }
        }
    }

    // ===== Royalty =====

    [HarmonyPatch(typeof(Pawn_RoyaltyTracker), nameof(Pawn_RoyaltyTracker.GainFavor))]
    public static class Patch_GainFavor
    {
        public static bool Prefix(Pawn_RoyaltyTracker __instance, int amount)
        {
            if (amount > 0 && CasteUtility.IsCommoner(__instance.pawn))
            {
                Messages.Message("CM_MessageNoHonor".Translate(__instance.pawn.Named("PAWN")), __instance.pawn, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.CanPawnAcceptQuest))]
    public static class Patch_CanPawnAcceptQuest
    {
        public static void Postfix(Pawn p, Quest quest, ref bool __result)
        {
            if (__result && CasteUtility.IsCommoner(p)
                && quest.PartsListForReading.Any(part => part is QuestPart_GiveRoyalFavor favor && favor.giveToAccepter))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(RoyalTitleUtility), nameof(RoyalTitleUtility.ShouldGetBestowingCeremonyQuest),
        new[] { typeof(Pawn), typeof(Faction) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
    public static class Patch_ShouldGetBestowingCeremonyQuest_Out
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && CasteUtility.IsCommoner(pawn))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(RoyalTitleUtility), nameof(RoyalTitleUtility.ShouldGetBestowingCeremonyQuest),
        new[] { typeof(Pawn), typeof(Faction) })]
    public static class Patch_ShouldGetBestowingCeremonyQuest
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && CasteUtility.IsCommoner(pawn))
            {
                __result = false;
            }
        }
    }

    // ===== Status label =====

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.LegalStatus), MethodType.Getter)]
    public static class Patch_Pawn_LegalStatus
    {
        public static void Postfix(Pawn __instance, ref TaggedString __result)
        {
            if (CasteUtility.IsColonyMember(__instance))
            {
                __result = CasteUtility.Label(CasteUtility.GetCaste(__instance)).CapitalizeFirst();
            }
        }
    }

    // Enslaving a pawn ends their caste; emancipation or elevation decides what they become next.
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class Patch_SetGuestStatus
    {
        public static void Postfix(Pawn_GuestTracker __instance, Pawn ___pawn)
        {
            if (!__instance.IsSlave || ___pawn?.health?.hediffSet == null)
            {
                return;
            }
            foreach (HediffDef def in new[] { CM_DefOf.CM_Commoner, CM_DefOf.CM_Artisan, CM_DefOf.CM_Scholar, CM_DefOf.CM_Soldier, CM_DefOf.CM_Noble })
            {
                Hediff mark = ___pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (mark != null)
                {
                    ___pawn.health.RemoveHediff(mark);
                }
            }
            Hediff offer = ___pawn.health.hediffSet.GetFirstHediffOfDef(CM_DefOf.CM_CommonerOffer);
            if (offer != null)
            {
                ___pawn.health.RemoveHediff(offer);
            }
        }
    }

    // ===== Children =====

    [HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.ApplyBirthOutcome))]
    public static class Patch_ApplyBirthOutcome
    {
        public static void Postfix(Thing __result)
        {
            if (__result is Pawn baby && !baby.Dead && baby.Faction == Faction.OfPlayer && baby.RaceProps.Humanlike)
            {
                Caste caste = CasteUtility.BirthCaste(baby);
                if (caste != Caste.Citizen)
                {
                    CasteUtility.SetCaste(baby, caste, thoughts: false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ChoiceLetter_BabyToChild), nameof(ChoiceLetter_BabyToChild.Choices), MethodType.Getter)]
    public static class Patch_BabyToChild_Choices
    {
        public static void Postfix(ChoiceLetter_BabyToChild __instance, Pawn ___pawn, ref IEnumerable<DiaOption> __result)
        {
            __result = AddCasteOption(__result, __instance, ___pawn);
        }

        private static IEnumerable<DiaOption> AddCasteOption(IEnumerable<DiaOption> options, ChoiceLetter_BabyToChild letter, Pawn pawn)
        {
            bool add = pawn != null && !letter.ArchivedOnly && (pawn.Faction?.IsPlayer ?? false);
            int count = 0;
            foreach (DiaOption option in options)
            {
                yield return option;
                count++;
                // The first two vanilla options are the colonist/slave choices.
                if (add && count == 2)
                {
                    yield return CasteOption(letter, pawn);
                }
            }
        }

        private static DiaOption CasteOption(ChoiceLetter_BabyToChild letter, Pawn pawn)
        {
            if ((CasteUtility.HasCommonerMark(pawn) || CasteUtility.HasNobleMark(pawn)) && !pawn.IsSlave)
            {
                return new DiaOption("CM_RaiseAsCitizen".Translate())
                {
                    action = () =>
                    {
                        CasteUtility.SetCaste(pawn, Caste.Citizen, thoughts: false);
                        Find.LetterStack.RemoveLetter(letter);
                    },
                    resolveTree = true
                };
            }
            return new DiaOption("CM_RaiseAsCommoner".Translate())
            {
                action = () =>
                {
                    if (pawn.IsSlave)
                    {
                        CasteUtility.ElevateSlave(null, pawn);
                    }
                    else
                    {
                        CasteUtility.MakeCommoner(pawn);
                    }
                    Find.LetterStack.RemoveLetter(letter);
                },
                resolveTree = true
            };
        }
    }

    // Coming of age: on the birthday the game treats as adulthood (13 for humans), the child's caste
    // is reset to commoner or their parents' higher caste. The method is private, so it's named here.
    [HarmonyPatch(typeof(Pawn_AgeTracker), "BirthdayBiological")]
    public static class Patch_AgeTracker_BirthdayBiological
    {
        public static void Postfix(Pawn_AgeTracker __instance, Pawn ___pawn, int birthdayAge)
        {
            if (___pawn == null || !___pawn.RaceProps.Humanlike || birthdayAge != (int)__instance.AdultMinAge || __instance.AdultMinAge <= 0f)
            {
                return;
            }
            CasteUtility.ComeOfAge(___pawn);
        }
    }

    // ===== Recruiting prisoners as commoners =====

    [HarmonyPatch(typeof(InteractionWorker_RecruitAttempt), nameof(InteractionWorker_RecruitAttempt.Interacted))]
    public static class Patch_RecruitAttempt_Interacted
    {
        public static void Prefix(Pawn recipient, out float __state)
        {
            __state = recipient?.guest?.resistance ?? 0f;
        }

        public static void Postfix(Pawn initiator, Pawn recipient, float __state)
        {
            if (recipient?.guest == null || !recipient.IsPrisonerOfColony || !CasteUtility.HasCommonerOffer(recipient))
            {
                return;
            }
            float after = recipient.guest.resistance;
            float drop = __state - after;
            if (drop <= 0f || after <= 0f)
            {
                return;
            }
            float extra = drop * (CommonersMod.Settings.recruitResistanceFactor - 1f);
            recipient.guest.resistance = Mathf.Max(0f, after - extra);
            if (recipient.guest.resistance <= 0f)
            {
                Messages.Message("MessagePrisonerResistanceBroken".Translate(recipient.LabelShort, initiator.LabelShort, initiator.Named("WARDEN"), recipient.Named("PRISONER")),
                    recipient, MessageTypeDefOf.PositiveEvent);
            }
        }
    }

    [HarmonyPatch(typeof(RecruitUtility), nameof(RecruitUtility.Recruit))]
    public static class Patch_RecruitUtility_Recruit
    {
        public static void Postfix(Pawn pawn, Faction faction)
        {
            if (faction != Faction.OfPlayer || !CasteUtility.HasCommonerOffer(pawn))
            {
                return;
            }
            pawn.health.RemoveHediff(pawn.health.hediffSet.GetFirstHediffOfDef(CM_DefOf.CM_CommonerOffer));
            if (pawn.RaceProps.Humanlike)
            {
                CasteUtility.MakeCommoner(pawn);
            }
        }
    }

    // ===== Gizmos =====

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = CasteGizmos.Append(__result, __instance);
        }
    }

    // ===== Colonist bar =====

    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    public static class Patch_ColonistBar_DrawColonist
    {
        public static void Postfix(Rect rect, Pawn colonist)
        {
            if (!CommonersMod.Settings.showColonistBarIcon || colonist == null)
            {
                return;
            }
            Texture2D icon = CommonersTextures.CasteIcon(CasteUtility.GetCaste(colonist));
            if (icon == null)
            {
                return;
            }
            float size = Mathf.Max(10f, 16f * Find.ColonistBar.Scale);
            Rect iconRect = new Rect(rect.x + 1f, rect.y + 1f, size, size);
            GUI.DrawTexture(iconRect, icon);
            TooltipHandler.TipRegion(iconRect, CasteUtility.LabelFor(colonist).CapitalizeFirst());
        }
    }
}
