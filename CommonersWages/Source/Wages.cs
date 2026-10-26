using System.Collections.Generic;
using System.Linq;
using CashRegister;
using Commoners;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace CommonersWages
{
    public class CompProperties_Wages : CompProperties
    {
        public CompProperties_Wages()
        {
            compClass = typeof(CompWages);
        }
    }

    // A pawn's wage ledger. It's on every humanlike pawn (added at startup), but only does anything
    // while the pawn is in a paid caste: commoner, soldier, citizen, artisan or scholar. Nobles aren't paid.
    public class CompWages : ThingComp
    {
        // Wages earned but not yet collected. The silver stays in the payroll register until then.
        public int owed;
        // How much of the silver in the pawn's inventory is their own savings.
        public int purse;
        public int owedSinceTick = -1;
        public int lastPaydayKey = -1;
        public bool notifiedCanBuyCitizenship;
        public bool notifiedCanBuyNobility;
        public bool notifiedCanBuyLot;
        // Savings kept in a strongbox rather than the pocket.
        public int stashed;
        public Building_Strongbox stashBox;
        // For the payroll view: the last time wages were collected, and how much.
        public int lastCollectedTick = -1;
        public int lastCollectedAmount;
        // Tax not yet paid (see TaxUtility).
        public int taxDebt;
        // Wages held back toward the next tax bill. The silver never left payroll.
        public int taxSaved;
        // Recent payments, oldest first (see MoneyLog).
        public List<MoneyEntry> moneyLog = new List<MoneyEntry>();

        private static WagesSettings Settings => WagesMod.Settings;

        public Pawn Pawn => parent as Pawn;

        // Payday falls on the same days for everyone: the first day of each quadrum and every
        // `PaydayInterval` days after it, at payday hour. `lastPaydayKey` is the last day already paid
        // for, so each payday pays for the days since then: a full period, or fewer for a pawn who
        // started being paid partway through one.
        public void CheckPayday()
        {
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead)
            {
                return;
            }
            if (!WageUtility.EarnsWages(pawn))
            {
                // Not paid right now: days aren't counted, and if they're paid again later they start fresh.
                lastPaydayKey = -1;
                return;
            }
            int hour = GenLocalDate.HourOfDay(pawn);
            int key = GenLocalDate.Year(pawn) * GenDate.DaysPerYear + GenLocalDate.DayOfYear(pawn);
            if (lastPaydayKey < 0)
            {
                // Newly paid: today counts if it's still before payday hour.
                lastPaydayKey = hour >= Settings.paydayHour ? key : key - 1;
                return;
            }
            if (key == lastPaydayKey || hour < Settings.paydayHour || !WageUtility.IsPaydayToday(pawn))
            {
                return;
            }
            int days = Mathf.Clamp(key - lastPaydayKey, 1, Settings.PaydayInterval);
            lastPaydayKey = key;
            // Payroll only starts once the colony has a payroll register.
            if (WageUtility.AnyPayrollRegister())
            {
                Payday(days);
            }
        }

        // Pays the wage for `days` days, then charges that many days of rent.
        public void Payday(int days)
        {
            int owedBefore = owed;
            AddOwed(WageUtility.DailyWage(Pawn) * days, capped: true);
            MoneyLog.Note(Pawn, MoneyKind.Wage, owed - owedBefore, days > 1 ? "CW_LogDays".Translate(days).ToString() : null);
            // Colony rent and tax both go to the colony, so the day's tax share comes off the wage first,
            // and rent it leaves unpaid is let off. Rent to a landowner is theirs, so it comes first and
            // the tax share is held back from what's left.
            if (RentUtility.PaysLandlord(Pawn))
            {
                RentUtility.ChargeRent(Pawn, this, 0, days);
                TaxUtility.Withhold(Pawn, this);
            }
            else
            {
                RentUtility.ChargeRent(Pawn, this, TaxUtility.Withhold(Pawn, this), days);
            }
            LotUtility.TryAutoBuy(Pawn);
            LeaseUtility.TryAutoRent(Pawn);
            RentUtility.TryMoveUp(Pawn, this);
            RentUtility.TryMoveDown(Pawn, this);
            CheckCanBuy();
        }

        public void AddOwed(int amount, bool capped)
        {
            if (amount <= 0)
            {
                return;
            }
            if (capped)
            {
                int cap = WageUtility.DailyWage(Pawn) * Settings.OwedCapDays;
                owed = Mathf.Min(owed + amount, Mathf.Max(cap, owed));
            }
            else
            {
                owed += amount;
            }
            if (owed > 0 && owedSinceTick < 0)
            {
                owedSinceTick = Find.TickManager.TicksGame;
            }
        }

        // One-time letters when savings first cover citizenship (commoners) or a noble seat.
        public void CheckCanBuy()
        {
            Pawn pawn = Pawn;
            if (!pawn.IsColonistPlayerControlled)
            {
                return;
            }
            int savings = WageUtility.SpendableSavings(pawn);
            if (!notifiedCanBuyCitizenship && CasteUtility.IsCommoner(pawn) && savings >= Settings.citizenshipPrice && LeaseUtility.CanLeaveCommoners(pawn).Accepted)
            {
                notifiedCanBuyCitizenship = true;
                Find.LetterStack.ReceiveLetter("CW_LetterCanBuyLabel".Translate(),
                    "CW_LetterCanBuyText".Translate(pawn.Named("PAWN"), Settings.citizenshipPrice.ToStringMoney().Named("PRICE")),
                    LetterDefOf.PositiveEvent, pawn);
            }
            if (!notifiedCanBuyNobility && savings >= Settings.nobilityPrice)
            {
                notifiedCanBuyNobility = true;
                string text = "CW_LetterCanBuyNobilityText".Translate(pawn.Named("PAWN"), Settings.nobilityPrice.ToStringMoney().Named("PRICE"));
                if (CasteUtility.FreeNobleSeats() <= 0)
                {
                    text += "\n\n" + "CW_LetterNoSeat".Translate(CommonersMod.Settings.colonistsPerNobleSeat);
                }
                Find.LetterStack.ReceiveLetter("CW_LetterCanBuyNobilityLabel".Translate(), text, LetterDefOf.PositiveEvent, pawn);
            }
            // With autonomous buying on, they'll just buy it on payday, so no letter is needed.
            if (!notifiedCanBuyLot && !WagesMod.Settings.autoBuyLots && pawn.Spawned && LotUtility.CanOwn(pawn).Accepted && !LotUtility.OwnsAnyLot(pawn))
            {
                Zone_Lot lot = LotUtility.AllLots(pawn.Map).Where(l => l.owner == null && l.forSale && l.Price <= savings).OrderBy(l => l.Price).FirstOrDefault();
                if (lot != null)
                {
                    notifiedCanBuyLot = true;
                    Find.LetterStack.ReceiveLetter("CW_LetterCanBuyLotLabel".Translate(),
                        "CW_LetterCanBuyLotText".Translate(pawn.Named("PAWN"), lot.label.Named("LOT"), lot.Price.ToStringMoney().Named("PRICE")),
                        LetterDefOf.PositiveEvent, pawn);
                }
            }
        }

        public void Spend(int amount)
        {
            purse = Mathf.Max(0, purse - amount);
        }

        public void ReduceOwed(int amount)
        {
            owed = Mathf.Max(0, owed - amount);
            if (owed == 0)
            {
                owedSinceTick = -1;
            }
        }

        public float DaysOwed => owed > 0 && owedSinceTick >= 0 ? (Find.TickManager.TicksGame - owedSinceTick) / (float)GenDate.TicksPerDay : 0f;

        public override string CompInspectStringExtra()
        {
            Pawn pawn = Pawn;
            if (pawn == null || !WageUtility.KeepsSavings(pawn) || !pawn.IsColonist)
            {
                return null;
            }
            string text = "CW_PurseInspect".Translate(WageUtility.TotalSavings(pawn).ToStringMoney());
            int stash = WageUtility.Stash(pawn);
            if (stash > 0)
            {
                text += " " + "CW_StashInspect".Translate(stash.ToStringMoney());
            }
            if (owed > 0)
            {
                text += "  " + "CW_OwedInspect".Translate(owed.ToStringMoney());
            }
            if (taxDebt > 0)
            {
                text += "  " + "CW_TaxDebtInspect".Translate(taxDebt.ToStringMoney());
            }
            return text;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Pawn pawn = Pawn;
            if (pawn == null || !pawn.IsColonistPlayerControlled)
            {
                yield break;
            }
            if (Prefs.DevMode && DiningCheck.GastronomyLoaded)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Restaurant check",
                    defaultDesc = "Explain whether this pawn would eat at a Gastronomy restaurant, and which check stops them.",
                    action = () => DiningCheck.Show(pawn)
                };
            }
            if (Prefs.DevMode && (WageUtility.KeepsSavings(pawn) || moneyLog.Count > 0))
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Wages...",
                    defaultDesc = "Testing tools for this pawn's wages, savings and taxes.",
                    action = () => Find.WindowStack.Add(new FloatMenu(WagesDevTools.PawnOptions(pawn)))
                };
            }
            if (!WageUtility.KeepsSavings(pawn))
            {
                yield break;
            }
            int savings = WageUtility.TotalSavings(pawn);
            if (LotUtility.CanOwn(pawn).Accepted)
            {
                Command_Action buyLot = new Command_Action
                {
                    defaultLabel = "CW_BuyLot".Translate(),
                    defaultDesc = "CW_BuyLotDesc".Translate(pawn.Named("PAWN")),
                    icon = WagesTextures.BuyLot,
                    action = () => Find.WindowStack.Add(new FloatMenu(LotUtility.BuyOptions(pawn)))
                };
                if (!LotUtility.AllLots(pawn.Map).Any(l => l.owner == null && l.forSale))
                {
                    buyLot.Disable("CW_NoLotsForSale".Translate());
                }
                else if (WageUtility.ClosestPayroll(pawn, _ => true) == null)
                {
                    buyLot.Disable("CW_NoPayrollRegister".Translate());
                }
                yield return buyLot;
            }
            if (!WageUtility.EarnsWages(pawn))
            {
                yield break;
            }
            Command_Action rentLot = new Command_Action
            {
                defaultLabel = "CW_RentLot".Translate(),
                defaultDesc = "CW_RentLotDesc".Translate(pawn.Named("PAWN"), LeaseUtility.RentPeriodDays, LeaseUtility.GraceDays),
                icon = WagesTextures.Rent,
                action = () => Find.WindowStack.Add(new FloatMenu(LeaseUtility.RentOptions(pawn)))
            };
            if (!LotUtility.AllLots(pawn.Map).Any(l => l.forRent && l.tenant == null))
            {
                rentLot.Disable("CW_NoLotsForRent".Translate());
            }
            yield return rentLot;
            if (CasteUtility.IsCommoner(pawn))
            {
                int price = Settings.citizenshipPrice;
                Command_Action citizenship = new Command_Action
                {
                    defaultLabel = "CW_BuyCitizenship".Translate() + " (" + price.ToStringMoney() + ")",
                    defaultDesc = "CW_BuyCitizenshipDesc".Translate(pawn.Named("PAWN"), price.ToStringMoney().Named("PRICE")),
                    icon = WagesTextures.BuyCitizenship,
                    action = () => StartPurchase(CW_DefOf.CW_BuyCitizenship)
                };
                AcceptanceReport home = LeaseUtility.CanLeaveCommoners(pawn);
                if (!home.Accepted)
                {
                    citizenship.Disable(home.Reason);
                }
                else
                {
                    DisableIfCantBuy(citizenship, savings, price);
                }
                yield return citizenship;
            }
            int noblePrice = Settings.nobilityPrice;
            Command_Action nobility = new Command_Action
            {
                defaultLabel = "CW_BuyNobility".Translate() + " (" + noblePrice.ToStringMoney() + ")",
                defaultDesc = "CW_BuyNobilityDesc".Translate(pawn.Named("PAWN"), noblePrice.ToStringMoney().Named("PRICE"), CommonersMod.Settings.colonistsPerNobleSeat),
                icon = WagesTextures.BuyNobility,
                action = () => StartPurchase(CW_DefOf.CW_BuyNobility)
            };
            AcceptanceReport seat = CasteUtility.CanChangeCaste(pawn, Caste.Noble, purchased: true);
            if (!seat.Accepted)
            {
                nobility.Disable(seat.Reason);
            }
            else
            {
                DisableIfCantBuy(nobility, savings, noblePrice);
            }
            yield return nobility;
        }

        private void DisableIfCantBuy(Command_Action command, int savings, int price)
        {
            if (savings < price)
            {
                command.Disable("CW_BuyNeedMore".Translate((price - savings).ToStringMoney(), savings.ToStringMoney()));
            }
            else if (WageUtility.ClosestPayroll(Pawn, _ => true) == null)
            {
                command.Disable("CW_NoPayrollRegister".Translate());
            }
        }

        private void StartPurchase(JobDef job)
        {
            Building_CashRegister register = WageUtility.ClosestPayroll(Pawn, _ => true);
            if (register != null)
            {
                Pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(job, register), JobTag.Misc);
            }
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref owed, "cwOwed", 0);
            Scribe_Values.Look(ref purse, "cwPurse", 0);
            Scribe_Values.Look(ref owedSinceTick, "cwOwedSinceTick", -1);
            Scribe_Values.Look(ref lastPaydayKey, "cwLastPaydayKey", -1);
            Scribe_Values.Look(ref notifiedCanBuyCitizenship, "cwNotifiedCanBuy", false);
            Scribe_Values.Look(ref notifiedCanBuyNobility, "cwNotifiedCanBuyNobility", false);
            Scribe_Values.Look(ref notifiedCanBuyLot, "cwNotifiedCanBuyLot", false);
            Scribe_Values.Look(ref stashed, "cwStashed", 0);
            Scribe_References.Look(ref stashBox, "cwStashBox");
            Scribe_Values.Look(ref lastCollectedTick, "cwLastCollectedTick", -1);
            Scribe_Values.Look(ref lastCollectedAmount, "cwLastCollectedAmount", 0);
            Scribe_Values.Look(ref taxDebt, "cwTaxDebt", 0);
            Scribe_Values.Look(ref taxSaved, "cwTaxSaved", 0);
            Scribe_Collections.Look(ref moneyLog, "cwMoneyLog", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                moneyLog ??= new List<MoneyEntry>();
            }
        }
    }

    public class HediffCompProperties_Wages : HediffCompProperties
    {
        public HediffCompProperties_Wages()
        {
            compClass = typeof(HediffComp_Wages);
        }
    }

    // The v1 ledger, which lived on the commoner marker. It's kept only so saves from v1 load with
    // their data; PayrollTicker moves anything left here onto the pawn's CompWages.
    public class HediffComp_Wages : HediffComp
    {
        public int owed;
        public int purse;
        public int owedSinceTick = -1;
        public int lastPaydayKey = -1;
        public bool notifiedCanBuy;

        public bool HasData => owed != 0 || purse != 0 || lastPaydayKey >= 0 || notifiedCanBuy;

        public void MigrateTo(CompWages comp)
        {
            comp.owed += owed;
            comp.purse += purse;
            if (comp.owedSinceTick < 0)
            {
                comp.owedSinceTick = owedSinceTick;
            }
            comp.lastPaydayKey = Mathf.Max(comp.lastPaydayKey, lastPaydayKey);
            comp.notifiedCanBuyCitizenship |= notifiedCanBuy;
            owed = 0;
            purse = 0;
            owedSinceTick = -1;
            lastPaydayKey = -1;
            notifiedCanBuy = false;
        }

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref owed, "owed", 0);
            Scribe_Values.Look(ref purse, "purse", 0);
            Scribe_Values.Look(ref owedSinceTick, "owedSinceTick", -1);
            Scribe_Values.Look(ref lastPaydayKey, "lastPaydayKey", -1);
            Scribe_Values.Look(ref notifiedCanBuy, "notifiedCanBuy", false);
        }
    }

    // Drives payday for every colonist, every 250 ticks, and migrates v1 ledgers.
    public class PayrollTicker : GameComponent
    {
        public PayrollTicker(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0)
            {
                return;
            }
            if (WagesMod.Settings.registersPullSilver)
            {
                foreach (Map map in Find.Maps)
                {
                    foreach (Building_CashRegister register in WageUtility.PayrollRegisters(map).ToList())
                    {
                        WageUtility.PullSilverInRadius(register);
                    }
                }
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.ToList())
            {
                CompWages comp = WageUtility.WagesComp(pawn);
                if (comp == null)
                {
                    continue;
                }
                HediffComp_Wages legacy = pawn.health?.hediffSet?.GetFirstHediffOfDef(CM_DefOf.CM_Commoner)?.TryGetComp<HediffComp_Wages>();
                if (legacy != null && legacy.HasData)
                {
                    legacy.MigrateTo(comp);
                }
                comp.CheckPayday();
            }
            LotUtility.CheckOwners();
            LeaseUtility.CheckLeases();
            RentUtility.CheckSoldiersBeds();
            Current.Game.GetComponent<TaxCollector>()?.Tick();
        }
    }

    public static class WageUtility
    {
        public static CompWages WagesComp(Pawn pawn)
        {
            return pawn?.GetComp<CompWages>();
        }

        // Adult commoners, soldiers, citizens, artisans and scholars in the colony are paid. Nobles own the place; slaves get nothing.
        public static bool EarnsWages(Pawn pawn)
        {
            if (!CasteUtility.IsColonyMember(pawn) || !pawn.DevelopmentalStage.Adult())
            {
                return false;
            }
            return IsPaidCaste(CasteUtility.GetCaste(pawn));
        }

        // Who keeps protected savings: everyone paid, plus nobles and landowners, who collect rent.
        public static bool KeepsSavings(Pawn pawn)
        {
            if (EarnsWages(pawn))
            {
                return true;
            }
            return CasteUtility.IsColonyMember(pawn) && (CasteUtility.IsNoble(pawn) || LotUtility.OwnsAnyLot(pawn));
        }

        public static int DailyWage(Pawn pawn) => WageFor(CasteUtility.GetCaste(pawn));

        // Payday is the first day of each quadrum and every PaydayInterval days after it.
        public static bool IsPaydayToday(Thing where) => GenLocalDate.DayOfQuadrum(where) % WagesMod.Settings.PaydayInterval == 0;

        // Days until the next payday on this map: 0 if it's today and payday hour hasn't come yet.
        public static int DaysToPayday(Map map)
        {
            int interval = WagesMod.Settings.PaydayInterval;
            int rest = GenLocalDate.DayOfQuadrum(map) % interval;
            if (rest == 0)
            {
                return GenLocalDate.HourOfDay(map) < WagesMod.Settings.paydayHour ? 0 : interval;
            }
            return interval - rest;
        }

        // Whether the caste has a wage setting at all (nobles and slaves don't).
        public static bool IsPaidCaste(Caste caste) => caste == Caste.Commoner || caste == Caste.Soldier || caste == Caste.Citizen
            || caste == Caste.Artisan || caste == Caste.Scholar;

        // Changes the caste's daily wage in the mod settings and saves them.
        public static void SetWage(Caste caste, int wage)
        {
            WagesSettings s = WagesMod.Settings;
            switch (caste)
            {
                case Caste.Commoner:
                    s.dailyWage = wage;
                    break;
                case Caste.Citizen:
                    s.citizenWage = wage;
                    break;
                case Caste.Artisan:
                    s.artisanWage = wage;
                    break;
                case Caste.Scholar:
                    s.scholarWage = wage;
                    break;
                case Caste.Soldier:
                    s.soldierWage = wage;
                    break;
                default:
                    return;
            }
            s.Write();
        }

        public static int WageFor(Caste caste)
        {
            switch (caste)
            {
                case Caste.Commoner:
                    return WagesMod.Settings.dailyWage;
                case Caste.Citizen:
                    return WagesMod.Settings.citizenWage;
                case Caste.Artisan:
                    return WagesMod.Settings.artisanWage;
                case Caste.Scholar:
                    return WagesMod.Settings.scholarWage;
                case Caste.Soldier:
                    return WagesMod.Settings.soldierWage;
                default:
                    return 0;
            }
        }

        public static int Commission(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Excellent:
                    return WagesMod.Settings.commissionExcellent;
                case QualityCategory.Masterwork:
                    return WagesMod.Settings.commissionMasterwork;
                case QualityCategory.Legendary:
                    return WagesMod.Settings.commissionLegendary;
                default:
                    return 0;
            }
        }

        // Artisans earn a commission on each excellent-or-better item in their specialty, on top of their wage.
        public static void OnArtisanMadeItem(Pawn pawn, QualityCategory quality)
        {
            int commission = Commission(quality);
            CompWages comp = WagesComp(pawn);
            if (commission <= 0 || comp == null || !EarnsWages(pawn) || !AnyPayrollRegister())
            {
                return;
            }
            comp.AddOwed(commission, capped: false);
            MoneyLog.Note(pawn, MoneyKind.Commission, commission, quality.GetLabel());
            if (pawn.Spawned)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "CW_CommissionMote".Translate(commission.ToStringMoney()), 3.5f);
            }
        }

        public static int SilverCount(Pawn pawn)
        {
            return pawn?.inventory?.innerContainer?.TotalStackCountOfDef(ThingDefOf.Silver) ?? 0;
        }

        // The part of the pawn's inventory silver that is their own savings. Spending elsewhere
        // (a restaurant, a dropped stack) is picked up here by clamping to what's actually carried.
        public static int ProtectedSilver(Pawn pawn)
        {
            if (!KeepsSavings(pawn))
            {
                return 0;
            }
            CompWages comp = WagesComp(pawn);
            if (comp == null)
            {
                return 0;
            }
            comp.purse = Mathf.Min(comp.purse, SilverCount(pawn));
            return comp.purse;
        }

        // Savings kept in the pawn's strongbox, clamped to what's actually in it.
        public static int Stash(Pawn pawn)
        {
            CompWages comp = WagesComp(pawn);
            if (comp == null || comp.stashed <= 0)
            {
                return 0;
            }
            if (comp.stashBox == null || comp.stashBox.Destroyed)
            {
                comp.stashed = 0;
                comp.stashBox = null;
                return 0;
            }
            comp.stashed = Mathf.Min(comp.stashed, comp.stashBox.SilverHeld);
            return comp.stashed;
        }

        public static int TotalSavings(Pawn pawn) => KeepsSavings(pawn) ? ProtectedSilver(pawn) + Stash(pawn) : 0;

        // What a pawn will spend on wants (food for sale, a better room, a lot): tax debt is put aside first.
        public static int SpendableSavings(Pawn pawn) => Mathf.Max(0, TotalSavings(pawn) - TaxUtility.Debt(pawn));

        // As SpendableSavings, counting today's uncollected wages too.
        public static int SpendableFunds(Pawn pawn) => Mathf.Max(0, TotalSavings(pawn) + (WagesComp(pawn)?.owed ?? 0) - TaxUtility.Debt(pawn));

        // The most daily rent that still leaves enough of the wage to pay the quadrum's tax.
        public static int AffordableRent(Pawn pawn)
        {
            int taxPerDay = Mathf.CeilToInt(TaxUtility.TaxFor(pawn) / (float)GenDate.DaysPerQuadrum);
            return DailyWage(pawn) - taxPerDay;
        }

        // The strongbox linked to the bed the pawn owns, if any.
        public static Building_Strongbox LinkedStrongbox(Pawn pawn)
        {
            Building_Bed bed = pawn.ownership?.OwnedBed;
            List<Thing> linked = bed?.TryGetComp<CompAffectedByFacilities>()?.LinkedFacilitiesListForReading;
            return linked?.OfType<Building_Strongbox>().FirstOrDefault(b => b.Spawned);
        }

        // Puts pocket savings above the pocket-money float into the box. Savings left in a box the pawn
        // no longer sleeps by are moved over too (they carry them across).
        public static void Deposit(Pawn pawn, Building_Strongbox box)
        {
            CompWages comp = WagesComp(pawn);
            if (comp == null || box == null)
            {
                return;
            }
            if (comp.stashBox != null && comp.stashBox != box)
            {
                int old = Stash(pawn);
                int moved = TransferSilver(comp.stashBox.GetDirectlyHeldThings(), box.GetDirectlyHeldThings(), old);
                comp.stashed = moved;
            }
            comp.stashBox = box;
            int amount = ProtectedSilver(pawn) - WagesMod.Settings.pocketMoney;
            if (amount <= 0)
            {
                return;
            }
            int deposited = TransferSilver(pawn.inventory.innerContainer, box.GetDirectlyHeldThings(), amount);
            comp.Spend(deposited);
            comp.stashed += deposited;
        }

        // Tops the pocket back up to the pocket-money float from the box.
        public static void Withdraw(Pawn pawn, Building_Strongbox box)
        {
            CompWages comp = WagesComp(pawn);
            if (comp == null || box == null || comp.stashBox != box)
            {
                return;
            }
            int amount = Mathf.Min(Stash(pawn), WagesMod.Settings.pocketMoney - ProtectedSilver(pawn));
            if (amount <= 0)
            {
                return;
            }
            int purseBefore = ProtectedSilver(pawn);
            int taken = TransferSilver(box.GetDirectlyHeldThings(), pawn.inventory.innerContainer, amount);
            comp.purse = purseBefore + taken;
            comp.stashed -= taken;
        }

        // Pays `amount` of the pawn's savings into `to`: pocket first, then the strongbox (paid by
        // note, so they don't have to fetch it). Returns how much was paid.
        public static int PaySavings(Pawn pawn, ThingOwner to, int amount)
        {
            CompWages comp = WagesComp(pawn);
            if (comp == null || amount <= 0)
            {
                return 0;
            }
            int fromPocket = Mathf.Min(amount, ProtectedSilver(pawn));
            int paid = TransferSilver(pawn.inventory.innerContainer, to, fromPocket);
            comp.Spend(paid);
            int fromStash = Mathf.Min(amount - paid, Stash(pawn));
            if (fromStash > 0)
            {
                int moved = TransferSilver(comp.stashBox.GetDirectlyHeldThings(), to, fromStash);
                comp.stashed -= moved;
                paid += moved;
            }
            return paid;
        }

        // Gives up the pawn's strongbox savings: the silver is put out next to the box for haulers.
        public static void ReleaseStash(Pawn pawn)
        {
            CompWages comp = WagesComp(pawn);
            int stash = Stash(pawn);
            if (stash > 0)
            {
                comp.stashed = 0;
                comp.stashBox.DropSilver(stash);
            }
            if (comp != null)
            {
                comp.stashBox = null;
            }
        }

        // Payroll registers top themselves up from stored silver inside their radius.
        public static void PullSilverInRadius(Building_CashRegister register)
        {
            Map map = register.Map;
            CompPayroll payroll = register.GetComp<CompPayroll>();
            if (map == null || payroll == null)
            {
                return;
            }
            int needed = payroll.Target - SilverIn(register);
            if (needed <= 0)
            {
                return;
            }
            ThingOwner held = register.GetDirectlyHeldThings();
            foreach (IntVec3 cell in register.Fields)
            {
                if (needed <= 0)
                {
                    break;
                }
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0 && needed > 0; i--)
                {
                    Thing silver = things[i];
                    if (silver.def != ThingDefOf.Silver || silver.IsForbidden(Faction.OfPlayer) || !silver.IsInAnyStorage()
                        || map.reservationManager.IsReservedByAnyoneOf(silver, Faction.OfPlayer))
                    {
                        continue;
                    }
                    Thing taken = silver.SplitOff(Mathf.Min(needed, silver.stackCount));
                    int count = taken.stackCount;
                    if (held.TryAdd(taken))
                    {
                        needed -= count;
                    }
                    else
                    {
                        GenPlace.TryPlaceThing(taken, cell, map, ThingPlaceMode.Near);
                    }
                }
            }
        }

        // Puts silver stacks last so inventory scans that return the first unloadable item
        // reach everything else before the savings.
        public static void MoveSilverToEnd(ThingOwner<Thing> owner)
        {
            List<Thing> list = owner?.InnerListForReading;
            if (list == null || list.Count < 2)
            {
                return;
            }
            bool sawSilver = false;
            bool needsMove = false;
            foreach (Thing t in list)
            {
                if (t.def == ThingDefOf.Silver)
                {
                    sawSilver = true;
                }
                else if (sawSilver)
                {
                    needsMove = true;
                    break;
                }
            }
            if (!needsMove)
            {
                return;
            }
            List<Thing> silver = list.Where(t => t.def == ThingDefOf.Silver).ToList();
            list.RemoveAll(t => t.def == ThingDefOf.Silver);
            list.AddRange(silver);
        }

        public static bool IsPayroll(Building_CashRegister register)
        {
            return register?.GetComp<CompPayroll>()?.isPayroll ?? false;
        }

        public static int SilverIn(Building_CashRegister register)
        {
            return register.GetDirectlyHeldThings()?.TotalStackCountOfDef(ThingDefOf.Silver) ?? 0;
        }

        public static IEnumerable<Building_CashRegister> PayrollRegisters(Map map)
        {
            if (map == null)
            {
                return Enumerable.Empty<Building_CashRegister>();
            }
            return map.listerBuildings.AllBuildingsColonistOfClass<Building_CashRegister>().Where(IsPayroll);
        }

        public static bool AnyPayrollRegister()
        {
            return Find.Maps.Any(m => m.IsPlayerHome && PayrollRegisters(m).Any());
        }

        public static Building_CashRegister ClosestPayroll(Pawn pawn, System.Predicate<Building_CashRegister> validator)
        {
            if (!pawn.Spawned)
            {
                return null;
            }
            Building_CashRegister best = null;
            float bestDist = float.MaxValue;
            foreach (Building_CashRegister register in PayrollRegisters(pawn.Map))
            {
                if (!validator(register) || register.IsForbidden(pawn) || !pawn.CanReach(register, PathEndMode.Touch, Danger.Some))
                {
                    continue;
                }
                float dist = (register.Position - pawn.Position).LengthHorizontalSquared;
                if (dist < bestDist)
                {
                    best = register;
                    bestDist = dist;
                }
            }
            return best;
        }

        // Moves up to `amount` silver between holders. Returns how much actually moved.
        // This counts what left `from` rather than trusting TryTransferToContainer's return value,
        // which is 0 whenever the silver merges into a stack already in `to` (the moved stack is
        // absorbed and destroyed before its count is read). Trusting it meant wages, rent and
        // deposits went unrecorded whenever the pawn already had silver on them.
        public static int TransferSilver(ThingOwner from, ThingOwner to, int amount)
        {
            int start = from.TotalStackCountOfDef(ThingDefOf.Silver);
            foreach (Thing silver in from.Where(t => t.def == ThingDefOf.Silver).ToList())
            {
                int moved = start - from.TotalStackCountOfDef(ThingDefOf.Silver);
                if (moved >= amount)
                {
                    break;
                }
                from.TryTransferToContainer(silver, to, Mathf.Min(silver.stackCount, amount - moved));
            }
            return start - from.TotalStackCountOfDef(ThingDefOf.Silver);
        }

        public static void CollectWages(Pawn pawn, Building_CashRegister register)
        {
            CompWages comp = WagesComp(pawn);
            if (comp == null || comp.owed <= 0)
            {
                return;
            }
            int purseBefore = ProtectedSilver(pawn);
            int taken = TransferSilver(register.GetDirectlyHeldThings(), pawn.inventory.innerContainer, Mathf.Min(comp.owed, SilverIn(register)));
            if (taken <= 0)
            {
                return;
            }
            comp.purse = purseBefore + taken;
            comp.ReduceOwed(taken);
            comp.lastCollectedTick = Find.TickManager.TicksGame;
            comp.lastCollectedAmount = taken;
            MoneyLog.Note(pawn, MoneyKind.Collected, taken);
            pawn.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_Payday);
            PlayKaching(register);
            MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "+" + taken.ToStringMoney(), 3.5f);
            comp.CheckCanBuy();
        }

        // Pays `amount` from the pawn's savings into a payroll register on their map. Returns false
        // (and moves nothing) if they can't cover it.
        public static bool PayFromSavings(Pawn pawn, int amount)
        {
            CompWages comp = WagesComp(pawn);
            if (comp == null || amount <= 0)
            {
                return amount <= 0;
            }
            if (TotalSavings(pawn) < amount)
            {
                return false;
            }
            Building_CashRegister register = PayrollRegisters(pawn.MapHeld).FirstOrDefault();
            if (register == null)
            {
                return false;
            }
            return PaySavings(pawn, register.GetDirectlyHeldThings(), amount) >= amount;
        }

        public static void PlayKaching(Thing at)
        {
            SoundDef kaching = DefDatabase<SoundDef>.GetNamedSilentFail("CashRegister_Register_Kaching");
            kaching?.PlayOneShot(new TargetInfo(at.Position, at.Map));
        }
    }

    public class ThoughtWorker_WagesOwed : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!WageUtility.EarnsWages(p))
            {
                return ThoughtState.Inactive;
            }
            CompWages comp = WageUtility.WagesComp(p);
            if (comp == null)
            {
                return ThoughtState.Inactive;
            }
            float days = comp.DaysOwed;
            if (days < 1f)
            {
                return ThoughtState.Inactive;
            }
            if (days < 2f)
            {
                return ThoughtState.ActiveAtStage(0);
            }
            return ThoughtState.ActiveAtStage(days < 4f ? 1 : 2);
        }
    }

    [StaticConstructorOnStartup]
    public static class WagesTextures
    {
        public static readonly Texture2D BuyCitizenship = ContentFinder<Texture2D>.Get("UI/CommonersWages/BuyCitizenship");
        public static readonly Texture2D BuyNobility = ContentFinder<Texture2D>.Get("UI/Commoners/Noble");
        public static readonly Texture2D Payroll = ContentFinder<Texture2D>.Get("UI/CommonersWages/Payroll");
        public static readonly Texture2D PayrollTarget = ContentFinder<Texture2D>.Get("UI/CommonersWages/PayrollTarget");
        public static readonly Texture2D Rent = ContentFinder<Texture2D>.Get("UI/CommonersWages/Rent");
        public static readonly Texture2D LotZone = ContentFinder<Texture2D>.Get("UI/CommonersWages/ZoneCreate_Lot");
        public static readonly Texture2D BuyLot = ContentFinder<Texture2D>.Get("UI/CommonersWages/BuyLot");
        public static readonly Texture2D ForSale = ContentFinder<Texture2D>.Get("UI/CommonersWages/ForSale");
        public static readonly Texture2D LotPrice = ContentFinder<Texture2D>.Get("UI/CommonersWages/LotPrice");
        public static readonly Texture2D Grant = ContentFinder<Texture2D>.Get("UI/CommonersWages/GrantLot");
        public static readonly Texture2D Repossess = ContentFinder<Texture2D>.Get("UI/CommonersWages/Repossess");
    }
}
