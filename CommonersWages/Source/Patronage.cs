using Commoners;
using RimWorld;
using Verse;

namespace CommonersWages
{
    // Patronage: a colony noble spends their own savings on the colony for landed-title prestige (Commoners,
    // Ideology only). The silver goes into a payroll register, the way buying a noble seat does. Each point
    // bought costs more than the last: 500, 1000, 1500 and so on.
    public static class Patronage
    {
        public const int BaseCost = 500;

        public static bool CanOffer(Pawn pawn)
        {
            return LandedTitles.Data(pawn) != null && WageUtility.KeepsSavings(pawn);
        }

        public static int Cost(Pawn pawn)
        {
            return BaseCost * ((LandedTitles.Data(pawn)?.patronage ?? 0) + 1);
        }

        public static string Tip(Pawn pawn)
        {
            return "CW_PatronageTip".Translate(pawn.Named("PAWN"), Cost(pawn).ToStringMoney().Named("COST"), WageUtility.SpendableSavings(pawn).ToStringMoney().Named("SAVINGS")).ToString();
        }

        public static void Buy(Pawn pawn)
        {
            Hediff_Noble data = LandedTitles.Data(pawn);
            if (data == null)
            {
                return;
            }
            int cost = Cost(pawn);
            if (WageUtility.SpendableSavings(pawn) < cost)
            {
                Messages.Message("CW_PatronageCantAfford".Translate(pawn.Named("PAWN"), cost.ToStringMoney().Named("COST")), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (!WageUtility.PayFromSavings(pawn, cost))
            {
                Messages.Message("CW_PatronageNoRegister".Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            MoneyLog.Note(pawn, MoneyKind.Patronage, -cost);
            data.patronage++;
            LandedTitles.AddPrestige(pawn, 1, "CW_PrestigePatronage".Translate(cost.ToStringMoney()));
        }
    }
}
