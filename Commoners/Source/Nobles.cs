using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Commoners
{
    // A colony noble's sense of being given their due. It drains over about six days. Deference from
    // lower castes, ceremonies and a fine bedroom fill it; insults from lower castes knock it down.
    public class Need_Standing : Need
    {
        private const float FallPerDay = 1f / 6f;
        private const float IntervalDays = 150f / GenDate.TicksPerDay;

        public Need_Standing(Pawn pawn) : base(pawn)
        {
            threshPercents = new List<float> { 0.15f, 0.35f, 0.8f };
        }

        public override void NeedInterval()
        {
            if (IsFrozen || !pawn.Spawned)
            {
                return;
            }
            float change = -FallPerDay;
            if (HasFineQuarters())
            {
                change += FallPerDay * 0.5f;
            }
            CurLevel += change * IntervalDays;
        }

        private bool HasFineQuarters()
        {
            Room room = pawn.ownership?.OwnedRoom;
            return room != null && room.Role == RoomRoleDefOf.Bedroom
                && room.GetStat(RoomStatDefOf.Impressiveness) >= CommonersMod.Settings.nobleBedroomImpressiveness;
        }

        public void Gain(float amount)
        {
            CurLevel += amount;
        }

        public static void TryGain(Pawn p, float amount)
        {
            p?.needs?.TryGetNeed<Need_Standing>()?.Gain(amount);
        }
    }

    // Only colony nobles have the standing need.
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    public static class Patch_ShouldHaveNeed
    {
        public static void Postfix(Pawn ___pawn, NeedDef nd, ref bool __result)
        {
            if (__result && nd == CM_DefOf.CM_Standing && !CasteUtility.IsColonyNoble(___pawn))
            {
                __result = false;
            }
        }
    }

    // Lower castes talking to a noble is deference; insulting one is the opposite.
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class Patch_TryInteractWith
    {
        public static void Postfix(Pawn ___pawn, Pawn recipient, InteractionDef intDef, bool __result)
        {
            if (!__result || recipient == null || !CasteUtility.IsColonyNoble(recipient) || !CasteUtility.IsColonyMember(___pawn))
            {
                return;
            }
            if (CasteUtility.GetCaste(___pawn) >= Caste.Noble)
            {
                return;
            }
            float amount;
            if (intDef == InteractionDefOf.Insult)
            {
                amount = -0.06f;
            }
            else if (intDef == InteractionDefOf.DeepTalk)
            {
                amount = 0.05f;
            }
            else
            {
                amount = 0.025f;
            }
            Need_Standing.TryGain(recipient, amount);
        }
    }

    // Nobles who take part in a finished ritual gain standing, the organizer most of all.
    [HarmonyPatch(typeof(LordJob_Ritual), nameof(LordJob_Ritual.ApplyOutcome))]
    public static class Patch_LordJob_Ritual_ApplyOutcome
    {
        private static readonly FieldInfo OrganizerField = AccessTools.Field(typeof(LordJob_Ritual), "organizer");

        public static void Prefix(bool ___ended, out bool __state)
        {
            __state = ___ended;
        }

        public static void Postfix(LordJob_Ritual __instance, bool cancelled, bool __state)
        {
            if (__state || cancelled || __instance.lord?.ownedPawns == null)
            {
                return;
            }
            Pawn organizer = OrganizerField?.GetValue(__instance) as Pawn;
            foreach (Pawn p in __instance.lord.ownedPawns)
            {
                if (CasteUtility.IsColonyNoble(p))
                {
                    Need_Standing.TryGain(p, p == organizer ? 0.4f : 0.25f);
                }
            }
        }
    }
}
