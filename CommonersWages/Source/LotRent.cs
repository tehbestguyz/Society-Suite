using System.Collections.Generic;
using System.Linq;
using CashRegister;
using Commoners;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CommonersWages
{
    // Renting out a whole lot. The tenant and their family live on it as if they owned it: every bed is
    // free and private to them. Rent is due every 15 days, paid to the owner's savings, or into a payroll
    // register if the colony owns the lot. Two days late, the tenant is evicted.
    public static class LeaseUtility
    {
        public const int RentPeriodDays = 15;
        public const int GraceDays = 2;

        public static readonly int[] RentPresets = { 50, 100, 150, 200, 300, 400, 500, 750, 1000, 1500, 2000, 3000 };

        private static int PeriodTicks => RentPeriodDays * GenDate.TicksPerDay;

        // Owns a lot, or rents one. Living free on family land doesn't count.
        public static bool HasOwnHome(Pawn pawn) => LotUtility.OwnsAnyLot(pawn) || LotRentedBy(pawn) != null;

        // An adult commoner can only rise out of the commoner caste while they own or rent a lot.
        // Once risen, losing the lot doesn't demote them.
        public static AcceptanceReport CanLeaveCommoners(Pawn pawn)
        {
            if (!WagesMod.Settings.citizenshipNeedsHome || !CasteUtility.IsCommoner(pawn) || !pawn.DevelopmentalStage.Adult())
            {
                return true;
            }
            return HasOwnHome(pawn) ? (AcceptanceReport)true : "CW_CitizenNeedsHome".Translate();
        }

        public static Zone_Lot LotRentedBy(Pawn pawn)
        {
            return pawn == null || Current.Game == null ? null : LotUtility.AllLots().FirstOrDefault(l => l.tenant == pawn);
        }

        public static AcceptanceReport CanRent(Pawn pawn, Zone_Lot lot, bool requireForRent)
        {
            if (lot.tenant != null)
            {
                return lot.tenant == pawn ? "CW_LeaseAlready".Translate() : "CW_LeaseTaken".Translate(lot.tenant.Named("PAWN"));
            }
            if (requireForRent && !lot.forRent)
            {
                return "CW_LeaseNotForRent".Translate();
            }
            if (lot.owner != null && LotUtility.IsFamily(lot.owner, pawn))
            {
                return "CW_LeaseOwnFamily".Translate();
            }
            if (!WageUtility.EarnsWages(pawn))
            {
                return "CW_LeaseNotPaid".Translate();
            }
            if (pawn.MapHeld != lot.Map)
            {
                return "CW_LeaseOtherMap".Translate();
            }
            if (!LotUtility.UsableBedsOn(lot).Any())
            {
                return "CW_LotNoBed".Translate();
            }
            int funds = WageUtility.TotalSavings(pawn) + (WageUtility.WagesComp(pawn)?.owed ?? 0);
            if (funds < lot.LotRent)
            {
                return "CW_BuyNeedMore".Translate((lot.LotRent - funds).ToStringMoney(), funds.ToStringMoney());
            }
            if (!WageUtility.PayrollRegisters(lot.Map).Any())
            {
                return "CW_NoPayrollRegister".Translate();
            }
            return true;
        }

        // Signs the lease: the first period's rent is paid up front, everyone outside the tenant's family
        // gives up their bed on the lot, and the tenant moves into the best one.
        public static bool StartLease(Pawn pawn, Zone_Lot lot, bool ordered)
        {
            if (lot.tenant != null || !TryCharge(pawn, lot))
            {
                return false;
            }
            Zone_Lot old = LotRentedBy(pawn);
            if (old != null)
            {
                EndLease(old, "CW_MessageLeaseMovedOn", evicted: false);
            }
            int now = Find.TickManager.TicksGame;
            lot.tenant = pawn;
            lot.leaseSinceTick = now;
            lot.rentDueTick = now + PeriodTicks;
            lot.rentLate = false;
            foreach (Building_Bed bed in LotUtility.UsableBedsOn(lot).ToList())
            {
                foreach (Pawn sleeper in bed.OwnersForReading.Where(o => !LotUtility.IsFamily(pawn, o)).ToList())
                {
                    sleeper.ownership.UnclaimBed();
                    Messages.Message("CW_MessageLotMovedOutTenant".Translate(sleeper.Named("PAWN"), pawn.Named("TENANT"), lot.label.Named("LOT")),
                        new LookTargets(sleeper), MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
            LotUtility.MoveIn(pawn, lot, displace: true);
            Messages.Message("CW_MessageRentedLot".Translate(pawn.Named("PAWN"), lot.label.Named("LOT"), lot.LotRent.ToStringMoney().Named("RENT"), RentPeriodDays.Named("DAYS")),
                new LookTargets(pawn), ordered ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent);
            return true;
        }

        // Forgets the lease without touching anyone's bed (the tenant has just become the owner).
        public static void ClearLease(Zone_Lot lot)
        {
            lot.tenant = null;
            lot.leaseSinceTick = -1;
            lot.rentDueTick = -1;
            lot.rentLate = false;
        }

        // Ends the lease: the tenant's family gives up their beds on the lot, and the owner (if any) moves back in.
        public static void EndLease(Zone_Lot lot, string messageKey, bool evicted)
        {
            Pawn tenant = lot.tenant;
            if (tenant == null)
            {
                return;
            }
            ClearLease(lot);
            if (lot.Map != null)
            {
                foreach (Building_Bed bed in LotUtility.UsableBedsOn(lot).ToList())
                {
                    foreach (Pawn sleeper in bed.OwnersForReading.ToList())
                    {
                        if (LotUtility.IsFamily(tenant, sleeper) && !LotUtility.IsFamily(lot.owner, sleeper))
                        {
                            sleeper.ownership.UnclaimBed();
                        }
                    }
                }
                if (lot.owner != null && lot.owner.Spawned && lot.owner.Map == lot.Map)
                {
                    LotUtility.MoveIn(lot.owner, lot, displace: false);
                }
            }
            if (evicted && !tenant.Dead)
            {
                tenant.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_Evicted);
            }
            if (messageKey != null)
            {
                Messages.Message(messageKey.Translate(tenant.Named("PAWN"), lot.label.Named("LOT")), new LookTargets(tenant),
                    evicted ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent);
            }
        }

        // Takes one period's rent from the tenant: today's unpaid wages first, then their savings.
        // Returns false, moving nothing, if they can't cover all of it.
        public static bool TryCharge(Pawn pawn, Zone_Lot lot)
        {
            CompWages comp = WageUtility.WagesComp(pawn);
            int rent = lot.LotRent;
            if (comp == null)
            {
                return false;
            }
            if (rent <= 0)
            {
                return true;
            }
            if (lot.owner != null && lot.owner != pawn)
            {
                int paid = RentUtility.PayLandlord(pawn, comp, lot.Map, lot.owner, rent);
                if (paid < 0)
                {
                    return false;
                }
                lot.rentCollected += paid;
                MoneyLog.Note(pawn, MoneyKind.LotRentToLandlord, -paid, lot.owner.LabelShort);
                MoneyLog.Note(lot.owner, MoneyKind.LotRentReceived, paid, pawn.LabelShort);
                return true;
            }
            // The colony's lot: rent goes to payroll. The wage part never leaves the register.
            Building_CashRegister register = WageUtility.PayrollRegisters(lot.Map).FirstOrDefault();
            if (register == null)
            {
                return false;
            }
            int fromWages = Mathf.Min(comp.owed, rent);
            int fromSavings = rent - fromWages;
            if (WageUtility.TotalSavings(pawn) < fromSavings)
            {
                return false;
            }
            if (fromSavings > 0)
            {
                WageUtility.PaySavings(pawn, register.GetDirectlyHeldThings(), fromSavings);
            }
            comp.ReduceOwed(fromWages);
            lot.rentCollected += rent;
            MoneyLog.Note(pawn, MoneyKind.LotRent, -rent, lot.label);
            return true;
        }

        // Every 250 ticks: leases end when the tenant leaves the colony, and rent falls due.
        public static void CheckLeases()
        {
            int now = Find.TickManager.TicksGame;
            foreach (Zone_Lot lot in LotUtility.AllLots().Where(l => l.tenant != null).ToList())
            {
                Pawn tenant = lot.tenant;
                if (tenant.Dead || tenant.Discarded || tenant.IsSlave || (tenant.Faction != Faction.OfPlayer && !tenant.IsPrisonerOfColony))
                {
                    EndLease(lot, "CW_MessageLeaseEndedLeft", evicted: false);
                    continue;
                }
                if (lot.owner == tenant)
                {
                    ClearLease(lot);
                    continue;
                }
                if (now < lot.rentDueTick)
                {
                    continue;
                }
                // Rent to the colony waits while there's no payroll register to pay it into.
                if (!WageUtility.PayrollRegisters(lot.Map).Any())
                {
                    continue;
                }
                if (TryCharge(tenant, lot))
                {
                    lot.rentDueTick += PeriodTicks;
                    if (lot.rentDueTick <= now)
                    {
                        lot.rentDueTick = now + PeriodTicks;
                    }
                    if (lot.rentLate)
                    {
                        lot.rentLate = false;
                        Messages.Message("CW_MessageLateRentPaid".Translate(tenant.Named("PAWN"), lot.label.Named("LOT")), new LookTargets(tenant), MessageTypeDefOf.NeutralEvent);
                    }
                    continue;
                }
                if (now >= lot.rentDueTick + GraceDays * GenDate.TicksPerDay)
                {
                    EndLease(lot, "CW_MessageLeaseEvicted", evicted: true);
                    continue;
                }
                if (!lot.rentLate)
                {
                    lot.rentLate = true;
                    Messages.Message("CW_MessageRentLate".Translate(tenant.Named("PAWN"), lot.label.Named("LOT"), lot.LotRent.ToStringMoney().Named("RENT"), GraceDays.Named("DAYS")),
                        new LookTargets(tenant), MessageTypeDefOf.NegativeEvent);
                }
            }
        }

        // On payday: a paid colonist with no home of their own rents the best lot for rent that's better
        // than their current room, if they can pay the first period now and their wages cover the rent.
        public static void TryAutoRent(Pawn pawn)
        {
            if (!WagesMod.Settings.autoRentLots || !pawn.Spawned || !pawn.Map.IsPlayerHome || !WageUtility.EarnsWages(pawn))
            {
                return;
            }
            // Pawns in tax debt pay it off before taking on a lease.
            if (TaxUtility.Debt(pawn) > 0 || LotUtility.OwnsAnyLot(pawn) || LotRentedBy(pawn) != null || LotUtility.FamilyLot(pawn, pawn.Map) != null)
            {
                return;
            }
            Building_Bed current = pawn.ownership?.OwnedBed;
            // Don't split up couples sharing a bed, unless the partner could come along.
            if (current != null && current.OwnersForReading.Any(o => o != pawn && !LovePartnerRelationUtility.LovePartnerRelationExists(pawn, o)))
            {
                return;
            }
            int wagesPerPeriod = WageUtility.DailyWage(pawn) * RentPeriodDays;
            float currentScore = current == null ? float.MinValue : LotUtility.RoomScore(current);
            Zone_Lot best = LotUtility.AllLots(pawn.Map)
                .Where(l => l.forRent && l.tenant == null && l.LotRent <= wagesPerPeriod && CanRent(pawn, l, requireForRent: true).Accepted)
                .Where(l => LotUtility.BestBedOn(l, pawn, displace: true) != null && LotUtility.Desirability(l) > currentScore)
                .OrderByDescending(LotUtility.Desirability).ThenBy(l => l.LotRent)
                .FirstOrDefault();
            if (best != null)
            {
                StartLease(pawn, best, ordered: false);
            }
        }

        // ===== Menus =====

        public static List<FloatMenuOption> RentToOptions(Zone_Lot lot)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn pawn in CasteUtility.ColonyMembers().Where(p => p.Spawned && p.Map == lot.Map).OrderBy(p => p.LabelShort))
            {
                AcceptanceReport can = CanRent(pawn, lot, requireForRent: false);
                int funds = WageUtility.TotalSavings(pawn) + (WageUtility.WagesComp(pawn)?.owed ?? 0);
                string label = pawn.LabelShort + " (" + funds.ToStringMoney() + ")";
                if (!can.Accepted)
                {
                    if (!can.Reason.NullOrEmpty())
                    {
                        options.Add(new FloatMenuOption(label + ": " + can.Reason, null));
                    }
                    continue;
                }
                options.Add(new FloatMenuOption(label, () => StartLease(pawn, lot, ordered: true)));
            }
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("CW_LotNoBuyers".Translate(), null));
            }
            return options;
        }

        public static List<FloatMenuOption> RentOptions(Pawn pawn)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Zone_Lot lot in LotUtility.AllLots(pawn.Map).Where(l => l.forRent && l.tenant == null).OrderByDescending(LotUtility.Desirability))
            {
                int beds = LotUtility.UsableBedsOn(lot).Count();
                string label = "CW_LotRentOption".Translate(lot.label, lot.LotRent.ToStringMoney(), RentPeriodDays, beds);
                AcceptanceReport can = CanRent(pawn, lot, requireForRent: true);
                options.Add(can.Accepted
                    ? new FloatMenuOption(label, () => StartLease(pawn, lot, ordered: true))
                    : new FloatMenuOption(label + ": " + can.Reason, null));
            }
            return options;
        }

        public static string DueIn(Zone_Lot lot)
        {
            int ticks = lot.rentDueTick - Find.TickManager.TicksGame;
            if (lot.rentLate)
            {
                ticks += GraceDays * GenDate.TicksPerDay;
            }
            return (Mathf.Max(0, ticks) / (float)GenDate.TicksPerDay).ToString("0.#");
        }
    }
}
