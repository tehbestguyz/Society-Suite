using System.Collections.Generic;
using System.Linq;
using CashRegister;
using Commoners;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommonersWages
{
    // Taxes: every quadrum (15 days), on its first day at payday hour, every adult colony member pays
    // their caste's tax (set in the mod settings). Each payday, part of the wage is held back toward it
    // (see TaxUtility.Withhold), so it can't all be spent on food first. Tax comes out of that first,
    // then today's unpaid wages, then savings, paid by note into a payroll register on their map, or as
    // silver placed in a stockpile if the map has none. Whatever they can't pay becomes tax debt,
    // collected each payday as money comes in, with a mood penalty until it's cleared or forgiven.
    public class TaxCollector : GameComponent
    {
        private int lastTaxKey = -1;
        private int lastDebtDayKey = -1;

        public TaxCollector(Game game)
        {
        }

        // Called from PayrollTicker every 250 ticks, after wages, so tax day can use the day's pay.
        public void Tick()
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
            {
                return;
            }
            int year = GenLocalDate.Year(map);
            int quadKey = year * 4 + GenLocalDate.DayOfYear(map) / GenDate.DaysPerQuadrum;
            int dayKey = year * GenDate.DaysPerYear + GenLocalDate.DayOfYear(map);
            bool pastPayday = GenLocalDate.HourOfDay(map) >= WagesMod.Settings.paydayHour;
            if (lastTaxKey < 0)
            {
                // Newly added to a game: the first tax day is the next quadrum's.
                lastTaxKey = quadKey;
            }
            else if (quadKey != lastTaxKey && pastPayday)
            {
                lastTaxKey = quadKey;
                lastDebtDayKey = dayKey;
                TaxUtility.CollectTaxes();
                EconomyLedger.Get()?.SendReport();
            }
            if (dayKey != lastDebtDayKey && pastPayday)
            {
                lastDebtDayKey = dayKey;
                TaxUtility.CollectDebts();
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastTaxKey, "cwLastTaxKey", -1);
            Scribe_Values.Look(ref lastDebtDayKey, "cwLastTaxDebtDayKey", -1);
        }
    }

    public static class TaxUtility
    {
        public static int TaxFor(Pawn pawn)
        {
            if (!CasteUtility.IsColonyMember(pawn) || !pawn.DevelopmentalStage.Adult())
            {
                return 0;
            }
            return TaxForCaste(CasteUtility.GetCaste(pawn));
        }

        // Whether the caste has a tax setting at all (soldiers and slaves don't).
        public static bool IsTaxedCaste(Caste caste) => caste == Caste.Commoner || caste == Caste.Citizen || caste == Caste.Artisan
            || caste == Caste.Scholar || caste == Caste.Noble;

        // Changes the caste's tax in the mod settings and saves them.
        public static void SetTax(Caste caste, int tax)
        {
            WagesSettings s = WagesMod.Settings;
            switch (caste)
            {
                case Caste.Commoner:
                    s.taxCommoner = tax;
                    break;
                case Caste.Citizen:
                    s.taxCitizen = tax;
                    break;
                case Caste.Artisan:
                    s.taxArtisan = tax;
                    break;
                case Caste.Scholar:
                    s.taxScholar = tax;
                    break;
                case Caste.Noble:
                    s.taxNoble = tax;
                    break;
                default:
                    return;
            }
            s.Write();
        }

        public static int TaxForCaste(Caste caste)
        {
            WagesSettings s = WagesMod.Settings;
            switch (caste)
            {
                case Caste.Commoner:
                    return s.taxCommoner;
                case Caste.Citizen:
                    return s.taxCitizen;
                case Caste.Artisan:
                    return s.taxArtisan;
                case Caste.Scholar:
                    return s.taxScholar;
                case Caste.Noble:
                    return s.taxNoble;
                default:
                    // Soldiers (and slaves) pay no tax.
                    return 0;
            }
        }

        public static int Debt(Pawn pawn) => WageUtility.WagesComp(pawn)?.taxDebt ?? 0;

        public static int Saved(Pawn pawn) => WageUtility.WagesComp(pawn)?.taxSaved ?? 0;

        // Called on payday: holds back part of the wage toward the next tax bill, what's still due
        // spread over the paydays left before tax day. The pawn never collects it, so it can't be spent first.
        // Before this, pawns spent every coin on food for sale and met each tax day broke. Returns how
        // much was held back.
        public static int Withhold(Pawn pawn, CompWages comp)
        {
            int tax = TaxFor(pawn);
            if (tax <= 0)
            {
                // No longer taxed (enlisted as a soldier, say): what was held back is theirs again.
                if (comp.taxSaved > 0)
                {
                    comp.AddOwed(comp.taxSaved, capped: false);
                    MoneyLog.Note(pawn, MoneyKind.TaxHeldReturned, comp.taxSaved);
                    comp.taxSaved = 0;
                }
                return 0;
            }
            int needed = tax - comp.taxSaved;
            int days = DaysToTaxDay();
            if (needed <= 0 || days <= 0)
            {
                return 0;
            }
            // Paydays from today up to tax day, today's included.
            int paydays = Mathf.CeilToInt(days / (float)WagesMod.Settings.PaydayInterval);
            int share = Mathf.Min(Mathf.CeilToInt(needed / (float)paydays), comp.owed);
            if (share <= 0)
            {
                return 0;
            }
            comp.ReduceOwed(share);
            comp.taxSaved += share;
            MoneyLog.Note(pawn, MoneyKind.TaxHeld, share);
            return share;
        }

        // Clears the tax debt of each pawn who owes, and they're grateful for it.
        public static void Forgive(IEnumerable<Pawn> pawns)
        {
            int total = 0;
            int count = 0;
            foreach (Pawn pawn in pawns.Where(p => Debt(p) > 0).ToList())
            {
                CompWages comp = WageUtility.WagesComp(pawn);
                total += comp.taxDebt;
                count++;
                MoneyLog.Note(pawn, MoneyKind.TaxForgiven, comp.taxDebt);
                comp.taxDebt = 0;
                pawn.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_TaxesForgiven);
            }
            if (count > 0)
            {
                Messages.Message("CW_MessageTaxesForgiven".Translate(total.ToStringMoney(), count), MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }

        public static int TotalDebt(IEnumerable<Pawn> pawns) => pawns.Sum(Debt);

        private static IEnumerable<Pawn> Taxpayers()
        {
            return CasteUtility.ColonyMembers().Where(p => TaxFor(p) > 0 || Debt(p) > 0).ToList();
        }

        public static int DaysToTaxDay()
        {
            Map map = Find.AnyPlayerHomeMap;
            return map == null ? -1 : GenDate.DaysPerQuadrum - GenLocalDate.DayOfQuadrum(map);
        }

        public static void CollectTaxes()
        {
            int collected = 0;
            int payers = 0;
            int shortCount = 0;
            int owed = 0;
            foreach (Pawn pawn in Taxpayers())
            {
                CompWages comp = WageUtility.WagesComp(pawn);
                int due = TaxFor(pawn) + comp.taxDebt;
                if (due <= 0)
                {
                    continue;
                }
                int paid = Pay(pawn, comp, due, MoneyKind.Tax);
                comp.taxDebt = due - paid;
                collected += paid;
                payers++;
                if (comp.taxDebt > 0)
                {
                    shortCount++;
                    owed += comp.taxDebt;
                }
            }
            if (payers == 0)
            {
                return;
            }
            string text = "CW_MessageTaxDay".Translate(collected.ToStringMoney(), payers);
            if (shortCount > 0)
            {
                text += " " + "CW_MessageTaxDayShort".Translate(shortCount, owed.ToStringMoney());
            }
            Messages.Message(text, MessageTypeDefOf.NeutralEvent);
        }

        // Each payday, anyone in tax debt pays off what they can.
        public static void CollectDebts()
        {
            foreach (Pawn pawn in CasteUtility.ColonyMembers().Where(p => Debt(p) > 0).ToList())
            {
                CompWages comp = WageUtility.WagesComp(pawn);
                comp.taxDebt -= Pay(pawn, comp, comp.taxDebt, MoneyKind.TaxDebtPaid);
                if (comp.taxDebt <= 0)
                {
                    comp.taxDebt = 0;
                    Messages.Message("CW_MessageTaxDebtCleared".Translate(pawn.Named("PAWN")), new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
        }

        // Pays up to `amount` of tax: wages held back for it first, then unpaid wages (that silver is
        // already in payroll), then savings. Returns how much was paid, and logs it as `kind`.
        private static int Pay(Pawn pawn, CompWages comp, int amount, MoneyKind kind)
        {
            int paid = PayFromAll(pawn, comp, amount);
            MoneyLog.Note(pawn, kind, -paid);
            return paid;
        }

        private static int PayFromAll(Pawn pawn, CompWages comp, int amount)
        {
            int fromSaved = Mathf.Min(comp.taxSaved, amount);
            comp.taxSaved -= fromSaved;
            int paid = fromSaved;
            int fromWages = Mathf.Min(comp.owed, amount - paid);
            comp.ReduceOwed(fromWages);
            paid += fromWages;
            int rest = Mathf.Min(amount - paid, WageUtility.TotalSavings(pawn));
            if (rest <= 0)
            {
                return paid;
            }
            Map map = pawn.MapHeld != null && pawn.MapHeld.IsPlayerHome ? pawn.MapHeld : Find.AnyPlayerHomeMap;
            Building_CashRegister register = WageUtility.PayrollRegisters(map).FirstOrDefault();
            paid += register != null
                ? WageUtility.PaySavings(pawn, register.GetDirectlyHeldThings(), rest)
                : PayToStockpile(pawn, comp, map, rest);
            return paid;
        }

        // No payroll register on the map: the silver is taken from the pawn's pocket, then their
        // strongbox, and put in the best stockpile that takes silver (or left by the strongbox for haulers).
        private static int PayToStockpile(Pawn pawn, CompWages comp, Map map, int amount)
        {
            if (map == null)
            {
                return 0;
            }
            int paid = 0;
            int fromPocket = Mathf.Min(amount, WageUtility.ProtectedSilver(pawn));
            int taken = TakeSilver(pawn.inventory.innerContainer, fromPocket, map, pawn.Spawned && pawn.Map == map ? pawn.Position : map.Center);
            comp.Spend(taken);
            paid += taken;
            int fromBox = Mathf.Min(amount - paid, WageUtility.Stash(pawn));
            if (fromBox > 0 && comp.stashBox.Map == map)
            {
                int boxTaken = TakeSilver(comp.stashBox.GetDirectlyHeldThings(), fromBox, map, comp.stashBox.Position);
                comp.stashed -= boxTaken;
                paid += boxTaken;
            }
            return paid;
        }

        private static int TakeSilver(ThingOwner from, int amount, Map map, IntVec3 near)
        {
            int taken = 0;
            foreach (Thing silver in from.Where(t => t.def == ThingDefOf.Silver).ToList())
            {
                if (taken >= amount)
                {
                    break;
                }
                // SplitOff takes the stack out of its holder when it's the whole stack, without a drop.
                Thing part = silver.SplitOff(Mathf.Min(silver.stackCount, amount - taken));
                int count = part.stackCount;
                if (!GenPlace.TryPlaceThing(part, near, map, ThingPlaceMode.Near))
                {
                    from.TryAdd(part);
                    continue;
                }
                taken += count;
                if (!part.Destroyed && part.Spawned
                    && StoreUtility.TryFindBestBetterStoreCellFor(part, null, map, StoragePriority.Unstored, Faction.OfPlayer, out IntVec3 cell))
                {
                    part.DeSpawn();
                    GenPlace.TryPlaceThing(part, cell, map, ThingPlaceMode.Near);
                }
            }
            return taken;
        }
    }

    public class ThoughtWorker_TaxDebt : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            return CasteUtility.IsColonyMember(p) && TaxUtility.Debt(p) > 0 ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }
}
