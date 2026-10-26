using System.Collections.Generic;
using System.Linq;
using Commoners;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CommonersWages
{
    public class CompProperties_Rent : CompProperties
    {
        public CompProperties_Rent()
        {
            compClass = typeof(CompRent);
        }
    }

    // Daily rent a paid colonist (commoner, soldier, citizen, artisan or scholar) pays to sleep in this bed. Nobles sleep free.
    // A soldiers' bed is free, and only the Soldier caste may own or sleep in it.
    public class CompRent : ThingComp
    {
        public int rent;
        public bool soldiersOnly;

        private static readonly int[] RentPresets = { 0, 5, 10, 15, 20, 30, 50, 75, 100 };

        private Building_Bed Bed => parent as Building_Bed;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Building_Bed bed = Bed;
            if (bed == null || bed.Faction != Faction.OfPlayer || bed.Medical || bed.ForOwnerType != BedOwnerType.Colonist)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = soldiersOnly ? "CW_SetRentSoldiers".Translate() : rent > 0 ? "CW_SetRent".Translate(rent.ToStringMoney()) : "CW_SetRentFree".Translate(),
                defaultDesc = "CW_SetRentDesc".Translate(),
                icon = WagesTextures.Rent,
                action = () =>
                {
                    List<Building_Bed> beds = Find.Selector.SelectedObjects.OfType<Building_Bed>()
                        .Where(b => b.GetComp<CompRent>() != null).ToList();
                    List<FloatMenuOption> options = RentPresets
                        .Select(v => new FloatMenuOption(v == 0 ? "CW_SetRentFree".Translate().ToString() : "CW_PerDay".Translate(v.ToStringMoney()).ToString(), () =>
                        {
                            foreach (Building_Bed b in beds)
                            {
                                CompRent comp = b.GetComp<CompRent>();
                                comp.rent = v;
                                comp.soldiersOnly = false;
                            }
                        }))
                        .ToList();
                    options.Add(new FloatMenuOption("CW_RentSoldiersOption".Translate(), () =>
                    {
                        foreach (Building_Bed b in beds)
                        {
                            CompRent comp = b.GetComp<CompRent>();
                            comp.rent = 0;
                            comp.soldiersOnly = true;
                            RentUtility.EvictNonSoldiers(b);
                        }
                    }));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }

        public override string CompInspectStringExtra()
        {
            string text = RentInspect();
            Building_Bed bed = Bed;
            if (bed == null || soldiersOnly)
            {
                return text;
            }
            foreach (Pawn owner in bed.OwnersForReading)
            {
                Pawn partner = RentUtility.SharingPartner(owner, bed);
                if (partner != null)
                {
                    string line = "CW_RentInspectShared".Translate(owner.Named("PAWN"), partner.Named("PARTNER")).ToString();
                    text = text.NullOrEmpty() ? line : text + "\n" + line;
                }
            }
            return text;
        }

        private string RentInspect()
        {
            Building_Bed bed = Bed;
            if (soldiersOnly)
            {
                return "CW_RentInspectSoldiers".Translate().ToString();
            }
            Zone_Lot lot = bed != null && bed.Spawned ? LotUtility.LotOf(bed) : null;
            if (lot?.tenant != null)
            {
                return "CW_RentInspectTenant".Translate(lot.tenant.Named("PAWN"), lot.label.Named("LOT")).ToString();
            }
            if (lot != null && lot.OnMarket && rent == 0)
            {
                return "CW_RentInspectVacant".Translate(lot.label.Named("LOT")).ToString();
            }
            Pawn landlord = lot?.owner;
            if (landlord != null)
            {
                return rent > 0
                    ? "CW_RentInspectLandlord".Translate(rent.ToStringMoney(), landlord.Named("PAWN")).ToString()
                    : "CW_RentInspectPrivate".Translate(landlord.Named("PAWN")).ToString();
            }
            return rent > 0 ? "CW_RentInspect".Translate(rent.ToStringMoney()).ToString() : null;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref rent, "cwRent", 0);
            Scribe_Values.Look(ref soldiersOnly, "cwSoldiersOnly", false);
        }
    }

    public static class RentUtility
    {
        public static int RentOf(Building_Bed bed)
        {
            CompRent comp = bed?.GetComp<CompRent>();
            return comp == null || comp.soldiersOnly ? 0 : comp.rent;
        }

        public static bool IsSoldiersBed(Building_Bed bed) => bed?.GetComp<CompRent>()?.soldiersOnly ?? false;

        // Whether a soldiers' bed is closed to this pawn: anyone outside the Soldier caste.
        public static bool SoldiersOnlyBlocks(Pawn pawn, Building_Bed bed)
        {
            return IsSoldiersBed(bed) && CasteUtility.GetCaste(pawn) != Caste.Soldier;
        }

        // Unassigns everyone in a soldiers' bed who isn't a soldier (any more).
        public static void EvictNonSoldiers(Building_Bed bed)
        {
            foreach (Pawn owner in bed.OwnersForReading.Where(o => SoldiersOnlyBlocks(o, bed)).ToList())
            {
                owner.ownership.UnclaimBed();
                Messages.Message("CW_MessageSoldiersBedLeft".Translate(owner.Named("PAWN")), new LookTargets(owner, bed), MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        // Called every 250 ticks: a soldier who leaves the caste gives up their soldiers' bed.
        public static void CheckSoldiersBeds()
        {
            foreach (Map map in Find.Maps)
            {
                foreach (Building_Bed bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
                {
                    if (bed.OwnersForReading.Count > 0 && IsSoldiersBed(bed))
                    {
                        EvictNonSoldiers(bed);
                    }
                }
            }
        }

        private static float RoomScore(Building_Bed bed)
        {
            Room room = bed?.GetRoom();
            return room == null ? -1f : room.GetStat(RoomStatDefOf.Impressiveness);
        }

        // The landowner rent from this bed goes to, or null if it goes to the colony.
        public static Pawn LandlordFor(Building_Bed bed) => LotUtility.LandlordOf(bed);

        // What this pawn pays to sleep in the bed: nothing for nobles, on the lot their family owns or
        // rents (the lot's rent covers every bed on it), or in a bed their love partner rents.
        public static int RentFor(Pawn pawn, Building_Bed bed)
        {
            int rent = BaseRentFor(pawn, bed);
            return rent > 0 && SharingPartner(pawn, bed) != null ? 0 : rent;
        }

        private static int BaseRentFor(Pawn pawn, Building_Bed bed)
        {
            int rent = RentOf(bed);
            if (rent <= 0 || CasteUtility.IsNoble(pawn))
            {
                return 0;
            }
            Pawn resident = LotUtility.LotOf(bed)?.Resident;
            return resident != null && LotUtility.IsFamily(resident, pawn) ? 0 : rent;
        }

        // Who pays the rent on a shared bed: the first owner who'd be charged for it. Vanilla keeps a
        // bed's owners sorted by thing ID, so that's the lowest-ID paying owner, not whoever claimed first.
        public static Pawn RenterOf(Building_Bed bed)
        {
            return bed?.OwnersForReading.FirstOrDefault(o => BaseRentFor(o, bed) > 0);
        }

        // Who would pay the rent if these pawns joined the bed's owners.
        private static Pawn RenterIfJoined(Building_Bed bed, params Pawn[] joining)
        {
            return bed.OwnersForReading.Concat(joining).Where(o => BaseRentFor(o, bed) > 0).OrderBy(o => o.thingIDNumber).FirstOrDefault();
        }

        // The love partner (lover, fiancé(e) or spouse) whose rented bed this pawn shares, or would
        // share, for free. It's worked out fresh each time, so after a breakup, or once the renter
        // leaves the bed or dies, the other partner goes back to paying normal rent.
        public static Pawn SharingPartner(Pawn pawn, Building_Bed bed)
        {
            Pawn renter = RenterOf(bed);
            return renter != null && renter != pawn && LovePartnerRelationUtility.LovePartnerRelationExists(pawn, renter) ? renter : null;
        }

        // Whether today's rent goes to a landowner rather than the colony.
        public static bool PaysLandlord(Pawn pawn)
        {
            Building_Bed bed = pawn.ownership?.OwnedBed;
            return RentFor(pawn, bed) > 0 && LandlordFor(bed) != null;
        }

        // Rent comes out of today's wages first, then savings. Colony rent from wages never leaves
        // payroll. Rent on someone's lot goes to the landowner's savings: the wage part is paid out
        // of the payroll registers, the rest from the tenant's savings.
        // `withheld` is the tax share already taken off today's wage. The colony has that silver either
        // way, so colony rent the pawn can't cover is let off up to that much instead of evicting them.
        // Without this, a bed whose rent ate the whole wage left nothing for tax, and the debt grew forever.
        // Rent is a daily price, charged on payday for the `days` since the last one.
        public static void ChargeRent(Pawn pawn, CompWages comp, int withheld, int days)
        {
            Building_Bed bed = pawn.ownership?.OwnedBed;
            int rent = RentFor(pawn, bed) * days;
            if (rent <= 0)
            {
                return;
            }
            Pawn landlord = LandlordFor(bed);
            if (landlord != null)
            {
                ChargeLandlordRent(pawn, comp, bed, landlord, rent);
                return;
            }
            int fromWages = Mathf.Min(comp.owed, rent);
            int fromSavings = rent - fromWages;
            if (fromSavings > 0)
            {
                fromSavings -= Mathf.Min(withheld, Mathf.Max(0, fromSavings - WageUtility.TotalSavings(pawn)));
            }
            if (fromSavings > 0 && !WageUtility.PayFromSavings(pawn, fromSavings))
            {
                Evict(pawn, bed);
                return;
            }
            comp.ReduceOwed(fromWages);
            MoneyLog.Note(pawn, MoneyKind.Rent, -(fromWages + fromSavings));
            MoneyLog.Note(pawn, MoneyKind.RentLetOff, rent - fromWages - fromSavings);
        }

        private static void ChargeLandlordRent(Pawn pawn, CompWages comp, Building_Bed bed, Pawn landlord, int rent)
        {
            int paid = PayLandlord(pawn, comp, bed.Map, landlord, rent);
            if (paid < 0)
            {
                Evict(pawn, bed);
                return;
            }
            Zone_Lot lot = LotUtility.LotOf(bed);
            if (lot != null)
            {
                lot.rentCollected += paid;
            }
            MoneyLog.Note(pawn, MoneyKind.RentToLandlord, -paid, landlord.LabelShort);
            MoneyLog.Note(landlord, MoneyKind.RentReceived, paid, pawn.LabelShort);
        }

        // Pays rent into the landowner's savings: the wage part out of the payroll registers, the rest
        // from the tenant's pocket and then their strongbox. Returns how much was paid, or -1 (paying
        // nothing) if the tenant can't cover it.
        public static int PayLandlord(Pawn pawn, CompWages comp, Map map, Pawn landlord, int rent)
        {
            int fromWages = Mathf.Min(Mathf.Min(comp.owed, rent), LotUtility.PayrollSilver(map));
            if (WageUtility.TotalSavings(pawn) < rent - fromWages)
            {
                return -1;
            }
            int paid = 0;
            if (fromWages > 0)
            {
                paid = LotUtility.PayFromPayroll(map, landlord, fromWages);
                comp.ReduceOwed(paid);
            }
            int fromSavings = rent - paid;
            if (fromSavings > 0)
            {
                int pocket = Mathf.Min(fromSavings, WageUtility.ProtectedSilver(pawn));
                int fromPocket = LotUtility.CreditSavings(landlord, pawn.inventory.innerContainer, pocket);
                comp.Spend(fromPocket);
                paid += fromPocket;
                int rest = Mathf.Min(rent - paid, WageUtility.Stash(pawn));
                if (rest > 0)
                {
                    int fromBox = LotUtility.CreditSavings(landlord, comp.stashBox.GetDirectlyHeldThings(), rest);
                    comp.stashed -= fromBox;
                    paid += fromBox;
                }
            }
            return paid;
        }

        private static void Evict(Pawn pawn, Building_Bed bed)
        {
            pawn.ownership.UnclaimBed();
            pawn.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_Evicted);
            Messages.Message("CW_MessageEvicted".Translate(pawn.Named("PAWN")), new LookTargets(pawn, bed), MessageTypeDefOf.NegativeEvent);
        }

        // A paid colonist with enough put by moves into the most impressive rented room they can sustain.
        public static void TryMoveUp(Pawn pawn, CompWages comp)
        {
            if (!pawn.Spawned || !pawn.Map.IsPlayerHome || pawn.ownership == null)
            {
                return;
            }
            Building_Bed current = pawn.ownership.OwnedBed;
            // A family home is free and permanent: move in if there's room, and never leave it for a rented one.
            Zone_Lot home = LotUtility.FamilyLot(pawn, pawn.Map);
            if (home != null)
            {
                if (current != null && LotUtility.LotOf(current) == home)
                {
                    return;
                }
                // Don't split up couples sharing a bed (MoveIn brings a partner along when it can).
                if ((current == null || current.OwnersForReading.Count == 1 || home.Resident == pawn) && LotUtility.MoveIn(pawn, home, displace: false))
                {
                    Messages.Message("CW_MessageMovedHome".Translate(pawn.Named("PAWN"), home.label.Named("LOT")), new LookTargets(pawn), MessageTypeDefOf.PositiveEvent, historical: false);
                    return;
                }
            }
            // A couple already sharing a bed only moves together, to a nicer double.
            Pawn bedPartner = current?.OwnersForReading.FirstOrDefault(o => o != pawn && LovePartnerRelationUtility.LovePartnerRelationExists(pawn, o));
            if (bedPartner != null)
            {
                if (IsMovablePartner(pawn, bedPartner))
                {
                    TryMoveInTogether(pawn, bedPartner, current);
                }
                return;
            }
            // Don't split up others sharing a bed.
            if (current != null && current.OwnersForReading.Count > 1)
            {
                return;
            }
            // Someone with a partner looks for a double first: the partner's own bed, or one to share.
            // This comes before soldiers' beds, so a soldier with a partner outside the caste picks the couple.
            Pawn partner = LovePartnerRelationUtility.ExistingMostLikedLovePartner(pawn, false);
            if (partner != null && IsMovablePartner(pawn, partner) && (TryJoinPartner(pawn, partner, current) || TryMoveInTogether(pawn, partner, current)))
            {
                return;
            }
            if (TryMoveToSoldiersBed(pawn, current))
            {
                return;
            }
            // Tax debt is paid off before moving up, and the next tax bill is kept back too, so a
            // better room doesn't put them straight into debt.
            if (TaxUtility.Debt(pawn) > 0)
            {
                return;
            }
            int funds = WageUtility.SpendableFunds(pawn) - TaxUtility.TaxFor(pawn);
            int buffer = WagesMod.Settings.rentBufferDays;
            float currentScore = current == null ? float.MinValue : RoomScore(current);
            Building_Bed best = null;
            float bestScore = currentScore + 1f;
            foreach (Building_Bed bed in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                int rent = RentFor(pawn, bed);
                if (rent <= 0 || bed == current || funds < rent * buffer || !CanMoveInto(pawn, bed))
                {
                    continue;
                }
                float score = RoomScore(bed);
                if (score > bestScore)
                {
                    best = bed;
                    bestScore = score;
                }
            }
            if (best != null)
            {
                pawn.ownership.ClaimBedIfNonMedical(best);
                Messages.Message("CW_MessageMovedIn".Translate(pawn.Named("PAWN")), new LookTargets(pawn, best), MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }

        private static bool IsMovablePartner(Pawn pawn, Pawn partner)
        {
            return partner.Spawned && partner.Map == pawn.Map && partner.ownership != null && CasteUtility.IsColonyMember(partner)
                && partner.DevelopmentalStage.Adult() && !partner.Drafted;
        }

        // Whether the pawn can take on this much daily rent, by the same test moving up uses.
        private static bool CanAffordRent(Pawn pawn, int rent)
        {
            if (rent <= 0)
            {
                return true;
            }
            int funds = WageUtility.SpendableFunds(pawn) - TaxUtility.TaxFor(pawn);
            return TaxUtility.Debt(pawn) <= 0 && funds >= rent * WagesMod.Settings.rentBufferDays;
        }

        // Moves into the partner's bed if it has room. One of them pays the rent and the other sleeps free;
        // if that turns out to be the pawn (see RenterOf), they have to afford it.
        private static bool TryJoinPartner(Pawn pawn, Pawn partner, Building_Bed current)
        {
            Building_Bed bed = partner.ownership.OwnedBed;
            if (bed == null || bed == current || !CanMoveInto(pawn, bed))
            {
                return false;
            }
            if (RenterIfJoined(bed, pawn) == pawn && !CanAffordRent(pawn, BaseRentFor(pawn, bed)))
            {
                return false;
            }
            pawn.ownership.ClaimBedIfNonMedical(bed);
            Messages.Message("CW_MessageMovedInWithPartner".Translate(pawn.Named("PAWN"), partner.Named("PARTNER")), new LookTargets(pawn, bed), MessageTypeDefOf.PositiveEvent, historical: false);
            return true;
        }

        // Moves the couple into an empty double bed: the nicest one whichever of them will pay can afford,
        // no worse a room than either of them has now (or, if they already share a bed, a clearly nicer
        // one). One pays and the other sleeps free. The partner only comes along from a bed of their own,
        // never one shared with someone else or on their family's lot.
        private static bool TryMoveInTogether(Pawn pawn, Pawn partner, Building_Bed current)
        {
            Building_Bed partnerBed = partner.ownership.OwnedBed;
            bool together = current != null && current == partnerBed;
            if (!together && partnerBed != null && (partnerBed.OwnersForReading.Count > 1 || LotUtility.FamilyLot(partner, partner.Map) != null))
            {
                return false;
            }
            float minScore = together
                ? RoomScore(current) + 1f
                : Mathf.Min(current == null ? float.MinValue : RoomScore(current), partnerBed == null ? float.MinValue : RoomScore(partnerBed));
            Building_Bed best = null;
            float bestScore = float.MinValue;
            foreach (Building_Bed bed in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == current || bed.SleepingSlotsCount < 2 || bed.OwnersForReading.Count > 0
                    || !CanMoveInto(pawn, bed) || !CanMoveInto(partner, bed))
                {
                    continue;
                }
                Pawn payer = RenterIfJoined(bed, pawn, partner);
                if (payer != null && !CanAffordRent(payer, BaseRentFor(payer, bed)))
                {
                    continue;
                }
                float score = RoomScore(bed);
                if (score >= minScore && score > bestScore)
                {
                    best = bed;
                    bestScore = score;
                }
            }
            if (best == null)
            {
                return false;
            }
            pawn.ownership.ClaimBedIfNonMedical(best);
            partner.ownership.ClaimBedIfNonMedical(best);
            Messages.Message("CW_MessageMovedInTogether".Translate(pawn.Named("PAWN"), partner.Named("PARTNER")), new LookTargets(pawn, best), MessageTypeDefOf.PositiveEvent, historical: false);
            return true;
        }

        // A soldier moves into the nicest free soldiers' bed when it's at least as good a room as the rented
        // bed they're paying for, or better than the free bed they have. Moving up (above) only looks at
        // rented beds, and vanilla never moves a pawn who already owns a bed, so without this a soldier
        // kept paying rent next to empty, nicer soldiers' beds. A soldier who moves up to a nicer rented
        // room from here won't be pulled back, since the soldiers' bed is then the worse room.
        private static bool TryMoveToSoldiersBed(Pawn pawn, Building_Bed current)
        {
            if (CasteUtility.GetCaste(pawn) != Caste.Soldier || RentUtility.IsSoldiersBed(current))
            {
                return false;
            }
            float currentScore = current == null ? float.MinValue : RoomScore(current);
            float minScore = current != null && RentFor(pawn, current) > 0 ? currentScore : currentScore + 1f;
            Building_Bed best = null;
            float bestScore = float.MinValue;
            foreach (Building_Bed bed in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (!IsSoldiersBed(bed) || !CanMoveInto(pawn, bed))
                {
                    continue;
                }
                float score = RoomScore(bed);
                if (score >= minScore && score > bestScore)
                {
                    best = bed;
                    bestScore = score;
                }
            }
            if (best == null)
            {
                return false;
            }
            pawn.ownership.ClaimBedIfNonMedical(best);
            Messages.Message("CW_MessageMovedSoldiers".Translate(pawn.Named("PAWN")), new LookTargets(pawn, best), MessageTypeDefOf.PositiveEvent, historical: false);
            return true;
        }

        // A pawn in tax debt whose rent leaves too little of their wage to pay tax (or who has no bed) moves
        // somewhere cheaper: the best room within their budget, or failing that the cheapest bed that still
        // leaves them something.
        public static void TryMoveDown(Pawn pawn, CompWages comp)
        {
            if (!WagesMod.Settings.debtorsMoveDown || TaxUtility.Debt(pawn) <= 0 || !pawn.Spawned || !pawn.Map.IsPlayerHome || pawn.ownership == null)
            {
                return;
            }
            Building_Bed current = pawn.ownership.OwnedBed;
            // Couples sharing a bed stay together.
            if (current != null && current.OwnersForReading.Count > 1)
            {
                return;
            }
            // With no bed at all, they'd claim whatever they next lie down in; give them one they can afford.
            int currentRent = current == null ? int.MaxValue : RentFor(pawn, current);
            int budget = WageUtility.AffordableRent(pawn);
            if (currentRent <= budget)
            {
                return;
            }
            int maxRent = Mathf.Min(currentRent, WageUtility.DailyWage(pawn)) - 1;
            Building_Bed best = null;
            int bestRent = int.MaxValue;
            float bestScore = float.MinValue;
            foreach (Building_Bed bed in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == current || !CanMoveInto(pawn, bed))
                {
                    continue;
                }
                int rent = RentFor(pawn, bed);
                if (rent > maxRent)
                {
                    continue;
                }
                // Within budget, pick the nicest room; over it, the cheapest bed.
                int rentRank = rent <= budget ? 0 : rent;
                float score = RoomScore(bed);
                if (best == null || rentRank < bestRent || (rentRank == bestRent && score > bestScore))
                {
                    best = bed;
                    bestRent = rentRank;
                    bestScore = score;
                }
            }
            if (best != null)
            {
                pawn.ownership.ClaimBedIfNonMedical(best);
                Messages.Message("CW_MessageMovedDown".Translate(pawn.Named("PAWN")), new LookTargets(pawn, best), MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        // A pawn in tax debt won't take a bed whose rent leaves them too little to pay it off. Without this,
        // a debtor with no bed claims any rented bed they lie down in (vanilla claims beds on sleeping,
        // napping or relaxing), and the next payday's rent swallows the whole wage before the debt sweep.
        public static bool DebtorCantAfford(Pawn pawn, Building_Bed bed)
        {
            if (!WagesMod.Settings.debtorsMoveDown || TaxUtility.Debt(pawn) <= 0 || bed.IsOwner(pawn) || !WageUtility.EarnsWages(pawn))
            {
                return false;
            }
            int rent = RentFor(pawn, bed);
            return rent > 0 && rent > WageUtility.AffordableRent(pawn);
        }

        private static bool CanMoveInto(Pawn pawn, Building_Bed bed)
        {
            if (bed.Medical || bed.ForOwnerType != BedOwnerType.Colonist || !bed.def.building.bed_humanlike || !bed.AnyUnownedSleepingSlot)
            {
                return false;
            }
            return !bed.IsForbidden(pawn) && RestUtility.CanUseBedEver(pawn, bed.def) && !LotUtility.IsPrivateFrom(bed, pawn) && !SoldiersOnlyBlocks(pawn, bed)
                && pawn.CanReach(bed, PathEndMode.Touch, Danger.Some);
        }
    }
}
