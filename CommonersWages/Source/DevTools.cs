using System.Collections.Generic;
using System.Linq;
using CashRegister;
using Commoners;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommonersWages
{
    // Testing tools, only shown in dev mode. Colony-wide ones are in the debug actions menu under
    // "Society: Wages"; one-pawn ones are on the pawn's "DEV: Wages..." button. They're in English
    // only, like vanilla's own debug tools.
    public static class WagesDevTools
    {
        private const string Category = "Society: Wages";

        public static List<FloatMenuOption> PawnOptions(Pawn pawn)
        {
            CompWages comp = WageUtility.WagesComp(pawn);
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (WageUtility.EarnsWages(pawn))
            {
                options.Add(new FloatMenuOption("Payday now (" + WagesMod.Settings.PaydayInterval + " days: wage, tax share, rent, moving)",
                    () => comp.Payday(WagesMod.Settings.PaydayInterval)));
            }
            if (comp.owed > 0)
            {
                options.Add(new FloatMenuOption("Collect owed wages now (" + comp.owed.ToStringMoney() + ")", () => CollectNow(pawn)));
            }
            if (WageUtility.KeepsSavings(pawn))
            {
                options.Add(new FloatMenuOption("Give 100 silver as savings", () => GiveSavings(pawn, 100)));
                options.Add(new FloatMenuOption("Give 500 silver as savings", () => GiveSavings(pawn, 500)));
                if (WageUtility.TotalSavings(pawn) > 0)
                {
                    options.Add(new FloatMenuOption("Clear savings (" + WageUtility.TotalSavings(pawn).ToStringMoney() + ")", () => ClearSavings(pawn)));
                }
            }
            if (comp.taxDebt > 0)
            {
                options.Add(new FloatMenuOption("Clear tax debt (" + comp.taxDebt.ToStringMoney() + ", no mood)", () => comp.taxDebt = 0));
            }
            if (comp.moneyLog.Count > 0)
            {
                options.Add(new FloatMenuOption("Show full money log", () =>
                    Find.WindowStack.Add(new Dialog_MessageBox(pawn.LabelShort + "\n\n" + MoneyLog.LogText(pawn, 0)))));
            }
            return options;
        }

        // Takes what's owed straight from the payroll registers on the pawn's map, without the walk.
        private static void CollectNow(Pawn pawn)
        {
            if (!pawn.Spawned)
            {
                Messages.Message("The pawn has to be on a map to collect.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            CompWages comp = WageUtility.WagesComp(pawn);
            foreach (Building_CashRegister register in WageUtility.PayrollRegisters(pawn.Map).ToList())
            {
                if (comp.owed <= 0)
                {
                    break;
                }
                WageUtility.CollectWages(pawn, register);
            }
            if (comp.owed > 0)
            {
                Messages.Message(pawn.LabelShort + " is still owed " + comp.owed.ToStringMoney() + ": the payroll registers ran out.",
                    MessageTypeDefOf.CautionInput, historical: false);
            }
        }

        // New silver, put in the pawn's pocket as their own savings.
        private static void GiveSavings(Pawn pawn, int amount)
        {
            CompWages comp = WageUtility.WagesComp(pawn);
            int purseBefore = WageUtility.ProtectedSilver(pawn);
            int carried = WageUtility.SilverCount(pawn);
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = amount;
            pawn.inventory.innerContainer.TryAdd(silver);
            // Measured, since the new stack merges into any silver already carried.
            int added = WageUtility.SilverCount(pawn) - carried;
            comp.purse = purseBefore + added;
            MoneyLog.Note(pawn, MoneyKind.Dev, added);
        }

        // Destroys the pawn's savings, pocket and strongbox both.
        private static void ClearSavings(Pawn pawn)
        {
            CompWages comp = WageUtility.WagesComp(pawn);
            int pocket = WageUtility.ProtectedSilver(pawn);
            int stash = WageUtility.Stash(pawn);
            DestroySilver(pawn.inventory.innerContainer, pocket);
            comp.purse = 0;
            if (stash > 0)
            {
                DestroySilver(comp.stashBox.GetDirectlyHeldThings(), stash);
                comp.stashed = 0;
            }
            MoneyLog.Note(pawn, MoneyKind.Dev, -(pocket + stash));
        }

        private static void DestroySilver(ThingOwner from, int amount)
        {
            foreach (Thing silver in from.Where(t => t.def == ThingDefOf.Silver).ToList())
            {
                if (amount <= 0)
                {
                    break;
                }
                int take = Mathf.Min(amount, silver.stackCount);
                silver.SplitOff(take).Destroy();
                amount -= take;
            }
        }

        private static List<Pawn> PaidPawns() => PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
            .Where(WageUtility.EarnsWages).ToList();

        [DebugAction(Category, "Payday now (everyone, a full period)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PaydayNow()
        {
            if (!WageUtility.AnyPayrollRegister())
            {
                Messages.Message("No payroll register yet, so a real payday wouldn't run. Running it anyway.", MessageTypeDefOf.CautionInput, historical: false);
            }
            List<Pawn> paid = PaidPawns();
            foreach (Pawn pawn in paid)
            {
                WageUtility.WagesComp(pawn)?.Payday(WagesMod.Settings.PaydayInterval);
            }
            Messages.Message("Payday run for " + paid.Count + " paid colonists.", MessageTypeDefOf.NeutralEvent, historical: false);
        }

        [DebugAction(Category, "Tax day now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void TaxDayNow()
        {
            TaxUtility.CollectTaxes();
            EconomyLedger.Get()?.SendReport();
        }

        [DebugAction(Category, "Collect tax debts now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CollectDebtsNow()
        {
            TaxUtility.CollectDebts();
        }

        [DebugAction(Category, "Clear all tax debt (no mood)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ClearAllDebt()
        {
            foreach (Pawn pawn in CasteUtility.ColonyMembers().Where(p => TaxUtility.Debt(p) > 0).ToList())
            {
                WageUtility.WagesComp(pawn).taxDebt = 0;
            }
        }

        [DebugAction(Category, "Pay out all owed wages now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CollectAllNow()
        {
            foreach (Pawn pawn in PaidPawns().Where(p => p.Spawned && WageUtility.WagesComp(p).owed > 0))
            {
                CollectNow(pawn);
            }
        }

        [DebugAction(Category, "Fill payroll registers to target", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FillRegisters()
        {
            foreach (Building_CashRegister register in WageUtility.PayrollRegisters(Find.CurrentMap).ToList())
            {
                int needed = register.GetComp<CompPayroll>().Target - WageUtility.SilverIn(register);
                if (needed > 0)
                {
                    Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silver.stackCount = needed;
                    register.GetDirectlyHeldThings().TryAdd(silver);
                }
            }
        }

        [DebugAction(Category, "Economy report now", allowedGameStates = AllowedGameStates.Playing)]
        private static void ReportNow()
        {
            EconomyLedger ledger = EconomyLedger.Get();
            if (ledger != null)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(ledger.Report(EconomyLedger.ReportDays)));
            }
        }
    }
}
