using System.Collections.Generic;
using System.Linq;
using CashRegister;
using Commoners;
using RimWorld;
using Verse;

namespace CommonersWages
{
    // Adds payroll columns (wage, rent, owed, savings, last paid) and colony payroll totals to the Castes tab.
    public static class PayrollView
    {
        private const string None = "-";

        public static void Register()
        {
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CW_ColWage".Translate(),
                value = p => WageUtility.EarnsWages(p) ? WageUtility.DailyWage(p).ToStringMoney() : None
            });
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CW_ColRent".Translate(),
                value = p =>
                {
                    if (!WageUtility.EarnsWages(p))
                    {
                        return None;
                    }
                    if (SharingPartner(p) != null)
                    {
                        return "CW_ColRentShared".Translate().ToString();
                    }
                    return Rent(p) > 0 ? Rent(p).ToStringMoney() : None;
                },
                tooltip = p =>
                {
                    Building_Bed bed = p.ownership?.OwnedBed;
                    Pawn partner = SharingPartner(p);
                    if (partner != null)
                    {
                        return "CW_ColRentSharedTip".Translate(partner.Named("PARTNER"), RentUtility.RentOf(bed).ToStringMoney()).ToString();
                    }
                    Zone_Lot lot = bed != null && bed.Spawned ? LotUtility.LotOf(bed) : null;
                    if (lot?.tenant != null && Rent(p) == 0)
                    {
                        return "CW_ColRentLeaseTip".Translate(lot.label).ToString();
                    }
                    Pawn landlord = lot?.owner;
                    if (landlord == null)
                    {
                        return null;
                    }
                    return Rent(p) > 0
                        ? "CW_ColRentLandlordTip".Translate(landlord.Named("PAWN")).ToString()
                        : "CW_ColRentHomeTip".Translate(landlord.Named("PAWN")).ToString();
                }
            });
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CW_ColHome".Translate(),
                value = Home,
                tooltip = HomeTip
            });
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CW_ColOwed".Translate(),
                value = p => Owed(p) > 0 ? Owed(p).ToStringMoney() : None,
                tooltip = p =>
                {
                    CompWages comp = WageUtility.WagesComp(p);
                    return comp != null && comp.owed > 0 ? "CW_ColOwedTip".Translate(comp.DaysOwed.ToString("0.#")).ToString() : null;
                }
            });
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CW_ColTax".Translate(),
                value = p =>
                {
                    int tax = TaxUtility.TaxFor(p);
                    int debt = TaxUtility.Debt(p);
                    string text = tax > 0 ? tax.ToStringMoney() : None;
                    return debt > 0 ? text + " (" + "CW_ColTaxOwes".Translate(debt.ToStringMoney()) + ")" : text;
                },
                tooltip = p => TaxUtility.TaxFor(p) > 0
                    ? "CW_ColTaxTip".Translate(GenDate.DaysPerQuadrum, TaxUtility.Saved(p).ToStringMoney(), TaxUtility.TaxFor(p).ToStringMoney()).ToString()
                    : null
            });
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CW_ColSavings".Translate(),
                value = p => WageUtility.KeepsSavings(p) ? WageUtility.TotalSavings(p).ToStringMoney() : None,
                tooltip = SavingsTip
            });
            CastesTabExtensions.Columns.Add(new CastesTabColumn
            {
                label = "CW_ColLastPaid".Translate(),
                width = 100f,
                value = LastPaid
            });
            CastesTabExtensions.SummaryLines.Add(PayrollLine);
            CastesTabExtensions.SummaryLines.Add(FundsLine);
            CastesTabExtensions.SummaryLines.Add(LotsLine);
            CastesTabExtensions.SummaryLines.Add(TaxLine);
            CastesTabExtensions.SummaryLines.Add(() => EconomyLedger.Get()?.SummaryLine());
            CastesTabExtensions.RowButtons.Add(new CastesTabButton
            {
                width = 70f,
                label = pawns => TaxUtility.TotalDebt(pawns) > 0 ? "CW_ForgiveTax".Translate().ToString() : null,
                tooltip = pawns => "CW_ForgiveTaxTip".Translate(pawns[0].Named("PAWN"), TaxUtility.TotalDebt(pawns).ToStringMoney()).ToString(),
                action = TaxUtility.Forgive
            });
            CastesTabExtensions.GroupButtons.Add(ForgiveAllButton("CW_ForgiveCasteTaxes", 170f));
            CastesTabExtensions.GroupButtons.Add(new CastesTabButton
            {
                width = 170f,
                label = WageTaxLabel,
                tooltip = pawns => "CW_WageTaxTip".Translate(CasteUtility.Label(CasteUtility.GetCaste(pawns[0]))).ToString(),
                action = pawns => Find.WindowStack.Add(new FloatMenu(WageTaxOptions(CasteUtility.GetCaste(pawns[0]))))
            });
            CastesTabExtensions.TopButtons.Add(ForgiveAllButton("CW_ForgiveAllTaxes", 200f));
            CastesTabExtensions.TopButtons.Add(new CastesTabButton
            {
                width = 110f,
                label = _ => "CW_EconomyButton".Translate().ToString(),
                tooltip = _ => EconomyLedger.Get()?.Report(EconomyLedger.ReportDays),
                action = _ =>
                {
                    EconomyLedger ledger = EconomyLedger.Get();
                    if (ledger != null)
                    {
                        Find.WindowStack.Add(new Dialog_MessageBox(ledger.Report(EconomyLedger.ReportDays)));
                    }
                }
            });
        }

        // The caste header's wage and tax button. Header buttons are only given the caste's members,
        // so the caste is read from the first one, and an empty caste has no button (use mod settings).
        private static string WageTaxLabel(List<Pawn> pawns)
        {
            if (pawns.Count == 0)
            {
                return null;
            }
            Caste caste = CasteUtility.GetCaste(pawns[0]);
            bool paid = WageUtility.IsPaidCaste(caste);
            bool taxed = TaxUtility.IsTaxedCaste(caste);
            if (paid && taxed)
            {
                return "CW_WageTaxLabel".Translate(WageUtility.WageFor(caste).ToStringMoney(), TaxUtility.TaxForCaste(caste).ToStringMoney()).ToString();
            }
            if (paid)
            {
                return "CW_WageLabel".Translate(WageUtility.WageFor(caste).ToStringMoney()).ToString();
            }
            return taxed ? "CW_TaxLabel".Translate(TaxUtility.TaxForCaste(caste).ToStringMoney()).ToString() : null;
        }

        private static readonly int[] WagePresets = { 0, 5, 10, 15, 20, 25, 30, 35, 40, 50, 60, 75, 100, 125, 150, 200 };
        private static readonly int[] TaxPresets = { 0, 10, 15, 20, 30, 45, 60, 80, 100, 125, 150, 200, 300, 500 };

        private static List<FloatMenuOption> WageTaxOptions(Caste caste)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (WageUtility.IsPaidCaste(caste))
            {
                options.Add(new FloatMenuOption("CW_SetWage".Translate(WageUtility.WageFor(caste).ToStringMoney()), () =>
                    Find.WindowStack.Add(new FloatMenu(Presets(WagePresets, WageUtility.WageFor(caste), v => WageUtility.SetWage(caste, v))))));
            }
            if (TaxUtility.IsTaxedCaste(caste))
            {
                options.Add(new FloatMenuOption("CW_SetTax".Translate(TaxUtility.TaxForCaste(caste).ToStringMoney(), GenDate.DaysPerQuadrum), () =>
                    Find.WindowStack.Add(new FloatMenu(Presets(TaxPresets, TaxUtility.TaxForCaste(caste), v => TaxUtility.SetTax(caste, v))))));
            }
            return options;
        }

        // One option per preset, plus the current value if it isn't one of them, marked as current.
        private static List<FloatMenuOption> Presets(int[] presets, int current, System.Action<int> set)
        {
            return presets.Union(new[] { current }).OrderBy(v => v)
                .Select(v => new FloatMenuOption(v == current ? "CW_PresetCurrent".Translate(v.ToStringMoney()).ToString() : v.ToStringMoney(), () => set(v)))
                .ToList();
        }

        // Pocket and strongbox, then the pawn's most recent payments.
        private static string SavingsTip(Pawn p)
        {
            string log = MoneyLog.LogText(p, MoneyLog.TooltipLines);
            string tip = WageUtility.KeepsSavings(p)
                ? "CW_ColSavingsTip".Translate(WageUtility.ProtectedSilver(p).ToStringMoney(), WageUtility.Stash(p).ToStringMoney()).ToString()
                : null;
            if (log == null)
            {
                return tip;
            }
            string recent = "CW_ColSavingsRecent".Translate().ToString() + "\n" + log;
            return tip == null ? recent : tip + "\n\n" + recent;
        }

        // Forgives everyone in the list who owes tax, after asking.
        private static CastesTabButton ForgiveAllButton(string key, float width)
        {
            return new CastesTabButton
            {
                width = width,
                label = pawns =>
                {
                    int debt = TaxUtility.TotalDebt(pawns);
                    return debt > 0 ? key.Translate(debt.ToStringMoney()).ToString() : null;
                },
                tooltip = pawns => "CW_ForgiveTaxesTip".Translate(pawns.Count(p => TaxUtility.Debt(p) > 0)).ToString(),
                action = pawns =>
                {
                    List<Pawn> debtors = pawns.Where(p => TaxUtility.Debt(p) > 0).ToList();
                    string text = "CW_ForgiveTaxesConfirm".Translate(TaxUtility.TotalDebt(debtors).ToStringMoney(), debtors.Count,
                        string.Join(", ", debtors.Select(p => p.LabelShort)));
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () => TaxUtility.Forgive(debtors)));
                }
            };
        }

        private static int Rent(Pawn p)
        {
            Building_Bed bed = p.ownership?.OwnedBed;
            return bed != null && bed.Spawned ? RentUtility.RentFor(p, bed) : RentUtility.RentOf(bed);
        }

        // The partner whose rented bed this pawn shares for free, if any.
        private static Pawn SharingPartner(Pawn p)
        {
            Building_Bed bed = p.ownership?.OwnedBed;
            return bed != null && bed.Spawned ? RentUtility.SharingPartner(p, bed) : null;
        }

        // Rent that goes back to the colony rather than to a landowner.
        private static int ColonyRent(Pawn p)
        {
            Building_Bed bed = p.ownership?.OwnedBed;
            return bed != null && bed.Spawned && RentUtility.LandlordFor(bed) != null ? 0 : Rent(p);
        }

        // The lot the pawn owns or rents, or the family lot they live on.
        private static string Home(Pawn p)
        {
            List<Zone_Lot> owned = LotUtility.LotsOwnedBy(p).ToList();
            if (owned.Count > 0)
            {
                return owned.Count == 1 ? owned[0].label : owned[0].label + " +" + (owned.Count - 1);
            }
            Zone_Lot rented = LeaseUtility.LotRentedBy(p);
            if (rented != null)
            {
                return "CW_ColHomeRented".Translate(rented.label);
            }
            Building_Bed bed = p.ownership?.OwnedBed;
            Zone_Lot lot = bed != null && bed.Spawned ? LotUtility.LotOf(bed) : null;
            return lot?.Resident != null && LotUtility.IsFamily(lot.Resident, p) ? lot.label : None;
        }

        private static string HomeTip(Pawn p)
        {
            List<Zone_Lot> owned = LotUtility.LotsOwnedBy(p).ToList();
            if (owned.Count > 0)
            {
                return "CW_ColHomeOwnedTip".Translate(string.Join(", ", owned.Select(l => l.label)), owned.Sum(l => l.rentCollected).ToStringMoney()).ToString();
            }
            Zone_Lot rented = LeaseUtility.LotRentedBy(p);
            if (rented != null)
            {
                return (rented.rentLate ? "CW_ColHomeRentedLateTip" : "CW_ColHomeRentedTip")
                    .Translate(rented.label, rented.LotRent.ToStringMoney(), LeaseUtility.RentPeriodDays, LeaseUtility.DueIn(rented)).ToString();
            }
            Building_Bed bed = p.ownership?.OwnedBed;
            Zone_Lot lot = bed != null && bed.Spawned ? LotUtility.LotOf(bed) : null;
            return lot?.Resident != null && LotUtility.IsFamily(lot.Resident, p) ? "CW_ColHomeFamilyTip".Translate(lot.Resident.Named("PAWN")).ToString() : null;
        }

        private static string LotsLine()
        {
            List<Zone_Lot> lots = LotUtility.AllLots().Where(l => l.Map.IsPlayerHome).ToList();
            if (lots.Count == 0)
            {
                return null;
            }
            int owned = lots.Count(l => l.owner != null);
            List<Zone_Lot> forSale = lots.Where(l => l.owner == null && l.forSale).ToList();
            string cheapest = forSale.Count > 0 ? forSale.Min(l => l.Price).ToStringMoney() : "-";
            string line = "CW_SummaryLots".Translate(lots.Count, owned, forSale.Count, cheapest).ToString();
            int rented = lots.Count(l => l.tenant != null);
            int forRent = lots.Count(l => l.forRent && l.tenant == null);
            if (rented > 0 || forRent > 0)
            {
                int toColony = lots.Where(l => l.tenant != null && l.owner == null).Sum(l => l.LotRent);
                line += " " + "CW_SummaryLotsRented".Translate(rented, forRent, toColony.ToStringMoney(), LeaseUtility.RentPeriodDays).ToString();
            }
            return line;
        }

        private static string TaxLine()
        {
            List<Pawn> payers = CasteUtility.ColonyMembers().Where(p => TaxUtility.TaxFor(p) > 0).ToList();
            int debt = CasteUtility.ColonyMembers().Sum(TaxUtility.Debt);
            if (payers.Count == 0 && debt == 0)
            {
                return null;
            }
            string line = "CW_SummaryTax".Translate(payers.Sum(TaxUtility.TaxFor).ToStringMoney(), payers.Count, TaxUtility.DaysToTaxDay()).ToString();
            return debt > 0 ? line + " " + "CW_SummaryTaxDebt".Translate(debt.ToStringMoney()).ToString() : line;
        }

        private static int Owed(Pawn p) => WageUtility.WagesComp(p)?.owed ?? 0;

        private static string LastPaid(Pawn p)
        {
            CompWages comp = WageUtility.WagesComp(p);
            if (comp == null || comp.lastCollectedTick < 0)
            {
                return None;
            }
            return "+" + comp.lastCollectedAmount.ToStringMoney() + ", " + MoneyLog.Ago(comp.lastCollectedTick);
        }

        private static List<Pawn> Paid() => CasteUtility.ColonyMembers().Where(WageUtility.EarnsWages).ToList();

        private static string PayrollLine()
        {
            List<Pawn> paid = Paid();
            int daily = paid.Sum(WageUtility.DailyWage);
            int rent = paid.Sum(ColonyRent);
            int landlordRent = paid.Sum(Rent) - rent;
            if (!WageUtility.AnyPayrollRegister())
            {
                return "CW_SummaryNoPayroll".Translate(daily.ToStringMoney()).ToString();
            }
            string line = "CW_SummaryPayroll".Translate(daily.ToStringMoney(), paid.Count, rent.ToStringMoney()).ToString();
            if (landlordRent > 0)
            {
                line += " " + "CW_SummaryLandlordRent".Translate(landlordRent.ToStringMoney()).ToString();
            }
            Map map = Find.AnyPlayerHomeMap;
            return map == null ? line : line + " " + "CW_SummaryNextPayday".Translate(WagesMod.Settings.PaydayInterval, NextPayday(map)).ToString();
        }

        private static string NextPayday(Map map)
        {
            int days = WageUtility.DaysToPayday(map);
            if (days == 0)
            {
                return "CW_PaydayToday".Translate(WagesMod.Settings.paydayHour).ToString();
            }
            return days == 1 ? "CW_PaydayTomorrow".Translate().ToString() : "CW_PaydayInDays".Translate(days).ToString();
        }

        private static string FundsLine()
        {
            if (!WageUtility.AnyPayrollRegister())
            {
                return null;
            }
            int inRegisters = Find.Maps.Where(m => m.IsPlayerHome).SelectMany(WageUtility.PayrollRegisters).Sum(WageUtility.SilverIn);
            int daily = Paid().Sum(WageUtility.DailyWage);
            int owed = Paid().Sum(Owed);
            string days = daily > 0 ? (inRegisters / (float)daily).ToString("0.#") : "-";
            return "CW_SummaryFunds".Translate(inRegisters.ToStringMoney(), days, owed.ToStringMoney()).ToString();
        }
    }
}
