using System.Collections.Generic;
using System.Linq;
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
    // Goods for sale: anything kept in a stockpile or storage building marked "For sale" costs its market
    // value times the markup when a pawn takes it for themselves: food and drink, drugs, and clothes picked
    // by their outfit policy. Commoners, soldiers, citizens, artisans and scholars pay from their savings
    // into a payroll register; nobles and children take it free. Slaves, prisoners, guests and animals
    // can't take it, and anyone who can't afford it goes without (a starving pawn still eats). Colony work
    // (hauling, cooking, crafting with stored materials) uses it freely.
    public class CompProperties_ForSale : CompProperties
    {
        public CompProperties_ForSale()
        {
            compClass = typeof(CompForSale);
        }
    }

    // On storage buildings (shelves, fridges): whether what's stored here is for sale.
    public class CompForSale : ThingComp
    {
        public bool forSale;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }
            yield return MarketUtility.ForSaleToggle(() => forSale, v => forSale = v);
        }

        public override string CompInspectStringExtra()
        {
            return forSale ? "CW_ForSaleInspect".Translate(WagesMod.Settings.foodMarkupPercent).ToString() : null;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref forSale, "cwForSale", false);
        }
    }

    // Which stockpile zones are for sale, by zone ID.
    public class MarketMapComponent : MapComponent
    {
        private HashSet<int> forSaleZones = new HashSet<int>();

        public MarketMapComponent(Map map) : base(map)
        {
        }

        public bool IsForSale(Zone zone) => zone != null && forSaleZones.Contains(zone.ID);

        public void SetForSale(Zone zone, bool value)
        {
            if (value)
            {
                forSaleZones.Add(zone.ID);
            }
            else
            {
                forSaleZones.Remove(zone.ID);
            }
        }

        public override void ExposeData()
        {
            List<int> list = forSaleZones.ToList();
            Scribe_Collections.Look(ref list, "cwForSaleZones", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                forSaleZones = list == null ? new HashSet<int>() : new HashSet<int>(list);
            }
        }
    }

    public static class MarketUtility
    {
        // Set while a pawn looks for something for themselves (food, drugs, clothes), for the pawn it's for.
        // See Patch_FoodUtility_BestFoodSourceOnMap and Patch_PersonalSearchGuard.
        [System.ThreadStatic]
        public static Pawn searchEater;
        [System.ThreadStatic]
        public static bool searchActive;
        // Set while pawns pick food up to carry (packing food, CE loadouts): for-sale food stays on the shelf.
        [System.ThreadStatic]
        public static bool packing;

        // A pending purchase: the eater's job that will eat food taken from a for-sale shelf.
        private struct Sale
        {
            public int jobID;
            public int unitPrice;
            public int tick;
            public string label;
        }

        private static readonly Dictionary<Pawn, Sale> pending = new Dictionary<Pawn, Sale>();

        public static Command_Toggle ForSaleToggle(System.Func<bool> get, System.Action<bool> set)
        {
            return new Command_Toggle
            {
                defaultLabel = "CW_ForSale".Translate(),
                defaultDesc = "CW_ForSaleDesc".Translate(WagesMod.Settings.foodMarkupPercent),
                icon = WagesTextures.ForSale,
                isActive = get,
                toggleAction = () => set(!get())
            };
        }

        // An item stored somewhere marked for sale.
        public static bool IsForSale(Thing t)
        {
            if (t == null || !t.Spawned || t.def.category != ThingCategory.Item)
            {
                return false;
            }
            SlotGroup group = t.GetSlotGroup();
            switch (group?.parent)
            {
                case Zone_Stockpile zone:
                    return zone.Map?.GetComponent<MarketMapComponent>()?.IsForSale(zone) ?? false;
                case Thing building:
                    return building.TryGetComp<CompForSale>()?.forSale ?? false;
                default:
                    return false;
            }
        }

        public static int UnitPrice(Thing t)
        {
            return Mathf.Max(0, Mathf.RoundToInt(t.MarketValue * WagesMod.Settings.foodMarkupPercent / 100f));
        }

        // Nobles and the colony's children take goods for sale free.
        public static bool TakesFree(Pawn pawn)
        {
            return CasteUtility.IsColonyMember(pawn) && (CasteUtility.IsNoble(pawn) || !pawn.DevelopmentalStage.Adult());
        }

        // Whether the pawn may take this for-sale item for themselves (outside a starving food search).
        public static bool CanTake(Pawn eater, Thing t)
        {
            if (eater == null || TakesFree(eater))
            {
                return true;
            }
            if (!WageUtility.EarnsWages(eater))
            {
                return false;
            }
            return WageUtility.SpendableSavings(eater) >= UnitPrice(t);
        }

        // Called when a pawn starts a job: eating, or feeding a patient, from a for-sale shelf opens a sale.
        public static void NoteJobStarted(Pawn pawn, Job job)
        {
            Pawn eater;
            if (job.def == JobDefOf.Ingest)
            {
                eater = pawn;
            }
            else if (job.def == JobDefOf.FeedPatient)
            {
                eater = job.targetB.Pawn;
            }
            else
            {
                return;
            }
            Thing food = job.targetA.Thing;
            if (eater == null || !WageUtility.EarnsWages(eater) || !IsForSale(food))
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            pending[eater] = new Sale { jobID = job.loadID, unitPrice = UnitPrice(food), tick = now, label = food.LabelNoCount };
            // Sales whose meal never got eaten (the job was interrupted) are forgotten after a day.
            foreach (Pawn stale in pending.Where(kv => now - kv.Value.tick > GenDate.TicksPerDay).Select(kv => kv.Key).ToList())
            {
                pending.Remove(stale);
            }
        }

        // Called after food is eaten: if it came off a for-sale shelf, the eater pays for what they ate.
        public static void NoteIngested(Pawn ingester, int count)
        {
            if (ingester == null || count <= 0 || !pending.TryGetValue(ingester, out Sale sale))
            {
                return;
            }
            // The eater's own job (eating) or a feeder's (a patient being fed) must be the one that took it.
            if (ingester.CurJob?.loadID != sale.jobID && !(ingester.MapHeld?.mapPawns.AllPawnsSpawned.Any(p => p.CurJob?.loadID == sale.jobID) ?? false))
            {
                return;
            }
            pending.Remove(ingester);
            int price = sale.unitPrice * count;
            Building_CashRegister register = WageUtility.PayrollRegisters(ingester.MapHeld).FirstOrDefault();
            if (price <= 0 || register == null)
            {
                return;
            }
            // A starving pawn may have taken it without the money; they pay what they have.
            int paid = WageUtility.PaySavings(ingester, register.GetDirectlyHeldThings(), price);
            MoneyLog.Note(ingester, MoneyKind.Purchase, -paid, count > 1 ? sale.label + " x" + count : sale.label);
            if (paid > 0 && ingester.Spawned)
            {
                MoteMaker.ThrowText(ingester.DrawPos, ingester.Map, "-" + paid.ToStringMoney(), 3.5f);
            }
        }

        // Called as a pawn puts on clothes, while the item is still on its shelf: if the shelf is for
        // sale, they buy it. Covers both outfit-policy changes and player orders; a pawn ordered to wear
        // something they can't afford pays what they have.
        public static void NoteWearing(Pawn pawn, Apparel apparel)
        {
            if (pawn == null || apparel == null || !WageUtility.EarnsWages(pawn) || !IsForSale(apparel))
            {
                return;
            }
            int price = UnitPrice(apparel) * apparel.stackCount;
            Building_CashRegister register = WageUtility.PayrollRegisters(pawn.MapHeld).FirstOrDefault();
            if (price <= 0 || register == null)
            {
                return;
            }
            int paid = WageUtility.PaySavings(pawn, register.GetDirectlyHeldThings(), price);
            MoneyLog.Note(pawn, MoneyKind.Purchase, -paid, apparel.LabelNoCount);
            if (paid > 0 && pawn.Spawned)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "-" + paid.ToStringMoney(), 3.5f);
            }
        }
    }

    // Pawns looking for something for themselves (clothes for their outfit, drugs for their drug policy
    // or an addiction, a beer or snack for recreation): for-sale goods they can't take are forbidden to
    // them for the search, like the food search above.
    [HarmonyPatch]
    public static class Patch_PersonalSearchGuard
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(JobGiver_OptimizeApparel), "TryGiveJob");
            yield return AccessTools.Method(typeof(JobGiver_TakeDrugsForDrugPolicy), "TryGiveJob");
            yield return AccessTools.Method(typeof(JobGiver_SatisfyChemicalNeed), "TryGiveJob");
            yield return AccessTools.Method(typeof(JoyGiver_Ingest), nameof(JoyGiver_Ingest.TryGiveJob));
            yield return AccessTools.Method(typeof(JoyGiver_Ingest), nameof(JoyGiver_Ingest.TryGiveJobInGatheringArea));
            yield return AccessTools.Method(typeof(JoyGiver_SocialRelax), nameof(JoyGiver_SocialRelax.TryGiveJob));
            yield return AccessTools.Method(typeof(JoyGiver_SocialRelax), nameof(JoyGiver_SocialRelax.TryGiveJobInGatheringArea));
        }

        public static void Prefix(Pawn pawn, out (bool active, Pawn pawn) __state)
        {
            __state = (MarketUtility.searchActive, MarketUtility.searchEater);
            MarketUtility.searchActive = true;
            MarketUtility.searchEater = pawn;
        }

        public static void Finalizer((bool active, Pawn pawn) __state)
        {
            MarketUtility.searchActive = __state.active;
            MarketUtility.searchEater = __state.pawn;
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear))]
    public static class Patch_Pawn_ApparelTracker_Wear_Market
    {
        public static void Prefix(Pawn_ApparelTracker __instance, Apparel newApparel)
        {
            MarketUtility.NoteWearing(__instance.pawn, newApparel);
        }
    }

    // Shelves and other storage buildings, including modded ones, get the "For sale" toggle at startup
    // (see WagesStartup). Stockpile zones get it here.
    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.GetGizmos))]
    public static class Patch_Zone_Stockpile_GetGizmos
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
            MarketMapComponent market = zone.Map?.GetComponent<MarketMapComponent>();
            if (market != null)
            {
                yield return MarketUtility.ForSaleToggle(() => market.IsForSale(zone), v => market.SetForSale(zone, v));
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_Pawn_JobTracker_StartJob_Market
    {
        public static void Postfix(Pawn ___pawn, Job newJob)
        {
            if (___pawn != null && newJob?.def != null)
            {
                MarketUtility.NoteJobStarted(___pawn, newJob);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Patch_Thing_Ingested_Market
    {
        public static void Prefix(Thing __instance, out int __state)
        {
            __state = __instance.stackCount;
        }

        public static void Postfix(Thing __instance, Pawn ingester, int __state)
        {
            int eaten = __instance.Destroyed ? __state : __state - __instance.stackCount;
            MarketUtility.NoteIngested(ingester, eaten);
        }
    }

    // Packing food into inventory would carry for-sale goods off the shelf for free.
    [HarmonyPatch(typeof(JobGiver_PackFood), "TryGiveJob")]
    public static class Patch_JobGiver_PackFood_TryGiveJob
    {
        public static void Prefix(out bool __state)
        {
            __state = MarketUtility.packing;
            MarketUtility.packing = true;
        }

        public static void Finalizer(bool __state)
        {
            MarketUtility.packing = __state;
        }
    }

    // So would Combat Extended's loadouts, which pick up whatever a pawn's loadout lists.
    [HarmonyPatch]
    public static class Patch_CE_UpdateLoadout_TryGiveJob
    {
        private static MethodBase target;

        public static bool Prepare()
        {
            System.Type type = AccessTools.TypeByName("CombatExtended.JobGiver_UpdateLoadout");
            target = type == null ? null : AccessTools.Method(type, "TryGiveJob");
            return target != null;
        }

        public static MethodBase TargetMethod() => target;

        public static void Prefix(out bool __state)
        {
            __state = MarketUtility.packing;
            MarketUtility.packing = true;
        }

        public static void Finalizer(bool __state)
        {
            MarketUtility.packing = __state;
        }
    }
}
