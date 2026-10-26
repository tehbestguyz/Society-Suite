using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommonersWages
{
    // What a money log entry was for. Saved by name, so members can be added anywhere but never renamed.
    public enum MoneyKind
    {
        Wage,
        Commission,
        Collected,
        TaxHeld,
        TaxHeldReturned,
        Tax,
        TaxDebtPaid,
        TaxForgiven,
        Rent,
        RentLetOff,
        RentToLandlord,
        RentReceived,
        LotRent,
        LotRentToLandlord,
        LotRentReceived,
        Purchase,
        Citizenship,
        Nobility,
        NobilityForfeit,
        LotBought,
        LotBoughtBack,
        StrongboxReturned,
        Dev
    }

    // The colony-wide totals the economy summary adds up. Saved by position in EconomyDay, so new
    // members go at the end.
    public enum ColonyFlow
    {
        WagesOut,
        RentIn,
        LotRentIn,
        TaxIn,
        SalesIn,
        PurchasesIn,
        BuyBackOut,
        LandlordRent,
        TaxForgiven
    }

    // One line in a pawn's money log. `amount` is the change to the pawn's money (wages owed plus
    // savings): positive for money in, negative for money out. Some kinds only move money around
    // (collecting wages, holding some back for tax), and those are shown without a sign.
    public class MoneyEntry : IExposable
    {
        public int tick;
        public MoneyKind kind;
        public int amount;
        // Who or what it was with: an item, a lot, another pawn's name. Kept as text, so it still
        // reads right after that pawn or lot is gone.
        public string detail;

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref kind, "kind", MoneyKind.Wage);
            Scribe_Values.Look(ref amount, "amount", 0);
            Scribe_Values.Look(ref detail, "detail");
        }
    }

    // Records each payment in the pawn's money log and in the colony's economy ledger. Every place
    // that moves a pawn's money calls Note once, after the payment, with what was actually paid.
    public static class MoneyLog
    {
        public const int MaxEntries = 40;
        public const int TooltipLines = 10;

        public static void Note(Pawn pawn, MoneyKind kind, int amount, string detail = null)
        {
            if (amount == 0)
            {
                return;
            }
            CompWages comp = WageUtility.WagesComp(pawn);
            if (comp != null)
            {
                comp.moneyLog.Add(new MoneyEntry { tick = Find.TickManager.TicksGame, kind = kind, amount = amount, detail = detail });
                if (comp.moneyLog.Count > MaxEntries)
                {
                    comp.moneyLog.RemoveRange(0, comp.moneyLog.Count - MaxEntries);
                }
            }
            ColonyFlow? flow = FlowOf(kind);
            if (flow != null)
            {
                Current.Game?.GetComponent<EconomyLedger>()?.Add(flow.Value, Mathf.Abs(amount));
            }
        }

        // Which colony total a kind of payment counts toward. Payments between colonists (a tenant's
        // rent to a landowner) are counted once, on the tenant's side.
        private static ColonyFlow? FlowOf(MoneyKind kind)
        {
            switch (kind)
            {
                case MoneyKind.Wage:
                case MoneyKind.Commission:
                    return ColonyFlow.WagesOut;
                case MoneyKind.Rent:
                    return ColonyFlow.RentIn;
                case MoneyKind.LotRent:
                    return ColonyFlow.LotRentIn;
                case MoneyKind.Tax:
                case MoneyKind.TaxDebtPaid:
                    return ColonyFlow.TaxIn;
                case MoneyKind.Purchase:
                    return ColonyFlow.SalesIn;
                case MoneyKind.Citizenship:
                case MoneyKind.Nobility:
                case MoneyKind.LotBought:
                    return ColonyFlow.PurchasesIn;
                case MoneyKind.LotBoughtBack:
                    return ColonyFlow.BuyBackOut;
                case MoneyKind.RentToLandlord:
                case MoneyKind.LotRentToLandlord:
                    return ColonyFlow.LandlordRent;
                case MoneyKind.TaxForgiven:
                    return ColonyFlow.TaxForgiven;
                default:
                    return null;
            }
        }

        // Kinds that only move the pawn's money from one place to another, shown without a sign.
        private static bool Neutral(MoneyKind kind)
        {
            switch (kind)
            {
                case MoneyKind.Collected:
                case MoneyKind.TaxHeld:
                case MoneyKind.TaxHeldReturned:
                case MoneyKind.TaxForgiven:
                case MoneyKind.RentLetOff:
                case MoneyKind.StrongboxReturned:
                    return true;
                default:
                    return false;
            }
        }

        public static string Signed(int amount) => (amount < 0 ? "-" : "+") + Mathf.Abs(amount).ToStringMoney();

        public static string EntryText(MoneyEntry e)
        {
            string money = Neutral(e.kind) ? Mathf.Abs(e.amount).ToStringMoney() : Signed(e.amount);
            return ("CW_Log_" + e.kind).Translate(money, e.detail ?? "").ToString().Trim();
        }

        public static string Ago(int tick)
        {
            int ago = Find.TickManager.TicksGame - tick;
            return ago < GenDate.TicksPerDay
                ? "CW_HoursAgo".Translate(ago / GenDate.TicksPerHour).ToString()
                : "CW_DaysAgo".Translate((ago / (float)GenDate.TicksPerDay).ToString("0.#")).ToString();
        }

        // The pawn's log, newest first, with everything from the same moment (a payday's wage, rent
        // and tax share) on one line. `maxLines` of 0 means all of it.
        public static string LogText(Pawn pawn, int maxLines)
        {
            List<MoneyEntry> log = WageUtility.WagesComp(pawn)?.moneyLog;
            if (log == null || log.Count == 0)
            {
                return null;
            }
            List<string> lines = new List<string>();
            int i = log.Count - 1;
            while (i >= 0 && (maxLines <= 0 || lines.Count < maxLines))
            {
                int tick = log[i].tick;
                List<string> parts = new List<string>();
                while (i >= 0 && log[i].tick == tick)
                {
                    parts.Add(EntryText(log[i]));
                    i--;
                }
                parts.Reverse();
                lines.Add(Ago(tick) + ": " + string.Join(", ", parts));
            }
            return string.Join("\n", lines);
        }
    }

    // One day's colony totals, by ColonyFlow.
    public class EconomyDay : IExposable
    {
        public int day;
        public List<int> amounts = new List<int>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref day, "day", 0);
            Scribe_Collections.Look(ref amounts, "amounts", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                amounts ??= new List<int>();
            }
        }
    }

    // The colony's money in and out, day by day, for the Castes tab summary and the tax-day report.
    // It only knows what this mod moves: Gastronomy restaurant meals pay into the restaurant's own
    // register and aren't counted.
    public class EconomyLedger : GameComponent
    {
        public const int KeepDays = 30;
        public const int ReportDays = 15;

        private List<EconomyDay> days = new List<EconomyDay>();

        public EconomyLedger(Game game)
        {
        }

        private static int Today => Find.TickManager.TicksGame / GenDate.TicksPerDay;

        public void Add(ColonyFlow flow, int amount)
        {
            int today = Today;
            EconomyDay entry = days.Count > 0 && days[days.Count - 1].day == today ? days[days.Count - 1] : null;
            if (entry == null)
            {
                entry = new EconomyDay { day = today };
                days.Add(entry);
                days.RemoveAll(d => d.day <= today - KeepDays);
            }
            while (entry.amounts.Count <= (int)flow)
            {
                entry.amounts.Add(0);
            }
            entry.amounts[(int)flow] += amount;
        }

        // Totals for the last `count` days, today included.
        public int[] Totals(int count)
        {
            int[] totals = new int[System.Enum.GetValues(typeof(ColonyFlow)).Length];
            int from = Today - count + 1;
            foreach (EconomyDay d in days.Where(d => d.day >= from))
            {
                for (int i = 0; i < d.amounts.Count && i < totals.Length; i++)
                {
                    totals[i] += d.amounts[i];
                }
            }
            return totals;
        }

        public bool AnyActivity(int count) => Totals(count).Any(t => t != 0);

        public static int In(int[] t) => t[(int)ColonyFlow.RentIn] + t[(int)ColonyFlow.LotRentIn] + t[(int)ColonyFlow.TaxIn]
            + t[(int)ColonyFlow.SalesIn] + t[(int)ColonyFlow.PurchasesIn];

        public static int Out(int[] t) => t[(int)ColonyFlow.WagesOut] + t[(int)ColonyFlow.BuyBackOut];

        // The one-line summary for the Castes tab.
        public string SummaryLine()
        {
            if (!AnyActivity(ReportDays))
            {
                return null;
            }
            int[] t = Totals(ReportDays);
            return "CW_SummaryEconomy".Translate(ReportDays, In(t).ToStringMoney(), Out(t).ToStringMoney(), MoneyLog.Signed(In(t) - Out(t))).ToString();
        }

        // The full breakdown: the Castes tab button's tooltip and window, and the tax-day letter.
        public string Report(int count)
        {
            int[] t = Totals(count);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CW_ReportPeriod".Translate(count).ToString());
            sb.AppendLine();
            sb.AppendLine("CW_ReportIn".Translate(In(t).ToStringMoney()).ToString());
            Line(sb, "CW_ReportRent", t[(int)ColonyFlow.RentIn]);
            Line(sb, "CW_ReportLotRent", t[(int)ColonyFlow.LotRentIn]);
            Line(sb, "CW_ReportTax", t[(int)ColonyFlow.TaxIn]);
            Line(sb, "CW_ReportSales", t[(int)ColonyFlow.SalesIn]);
            Line(sb, "CW_ReportPurchases", t[(int)ColonyFlow.PurchasesIn]);
            sb.AppendLine("CW_ReportOut".Translate(Out(t).ToStringMoney()).ToString());
            Line(sb, "CW_ReportWages", t[(int)ColonyFlow.WagesOut]);
            Line(sb, "CW_ReportBuyBack", t[(int)ColonyFlow.BuyBackOut]);
            sb.AppendLine("CW_ReportNet".Translate(MoneyLog.Signed(In(t) - Out(t))).ToString());
            if (t[(int)ColonyFlow.LandlordRent] > 0 || t[(int)ColonyFlow.TaxForgiven] > 0)
            {
                sb.AppendLine();
                if (t[(int)ColonyFlow.LandlordRent] > 0)
                {
                    sb.AppendLine("CW_ReportLandlordRent".Translate(t[(int)ColonyFlow.LandlordRent].ToStringMoney()).ToString());
                }
                if (t[(int)ColonyFlow.TaxForgiven] > 0)
                {
                    sb.AppendLine("CW_ReportForgiven".Translate(t[(int)ColonyFlow.TaxForgiven].ToStringMoney()).ToString());
                }
            }
            sb.AppendLine();
            sb.Append("CW_ReportNotCounted".Translate().ToString());
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string key, int amount)
        {
            sb.AppendLine("    " + key.Translate(amount.ToStringMoney()).ToString());
        }

        // Sent on tax day, after taxes are collected.
        public void SendReport()
        {
            if (!AnyActivity(ReportDays))
            {
                return;
            }
            Find.LetterStack.ReceiveLetter("CW_LetterEconomyLabel".Translate(), Report(ReportDays), LetterDefOf.NeutralEvent);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref days, "cwEconomyDays", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                days ??= new List<EconomyDay>();
            }
        }

        public static EconomyLedger Get() => Current.Game?.GetComponent<EconomyLedger>();
    }
}
