using HarmonyLib;
using Commoners;
using RimWorld;
using UnityEngine;
using Verse;

namespace CommonersWages
{
    public class WagesSettings : ModSettings
    {
        // Daily wages per caste. The commoner wage keeps its v1 name so saved settings carry over.
        public int dailyWage = 25;
        public int citizenWage = 40;
        public int artisanWage = 60;
        // Scholars earn a little less than artisans, with no commissions, and pay less tax.
        public int scholarWage = 50;
        // Soldiers are paid like commoners and pay no tax.
        public int soldierWage = 25;
        public int commissionExcellent = 10;
        public int commissionMasterwork = 25;
        public int commissionLegendary = 60;
        public int paydayHour = 7;
        // Days between paydays. Each payday pays the daily wage for every day since the last one.
        public int paydayInterval = 5;
        public int defaultPayrollTarget = 500;
        public int citizenshipPrice = 1500;
        public int nobilityPrice = 5000;
        public int rentBufferDays = 5;
        public bool gastronomyPays = true;
        public bool registersPullSilver = true;
        public int pocketMoney = 50;
        public int landPricePerCell = 10;
        public int buildingValuePercent = 100;
        public bool autoBuyLots = true;
        public bool restaurantStockForSale = true;
        // Lot rent per 15-day period, as a percentage of the lot's appraised value.
        public int lotRentPercent = 10;
        public bool autoRentLots = true;
        // Commoners must own or rent a lot before they can rise to a higher caste.
        public bool citizenshipNeedsHome = true;
        // Price of food on a for-sale shelf, as a percentage of its market value.
        public int foodMarkupPercent = 100;
        // Tax per caste, paid every quadrum (15 days).
        public int taxCommoner = 30;
        public int taxCitizen = 60;
        public int taxArtisan = 100;
        public int taxScholar = 80;
        public int taxNoble = 0;
        // Pawns in tax debt move out of rooms whose rent leaves them too little to pay it.
        public bool debtorsMoveDown = true;

        public const int MaxOwedDays = 10;

        // Every choice divides the 15-day quadrum, so tax day (its first day) is always a payday.
        public static readonly int[] PaydayIntervals = { 1, 3, 5, 15 };

        public int PaydayInterval => System.Array.IndexOf(PaydayIntervals, paydayInterval) >= 0 ? paydayInterval : 5;

        // Unpaid wages stop building up after this many days' worth, or two paydays' worth if that's more.
        public int OwedCapDays => Mathf.Max(MaxOwedDays, 2 * PaydayInterval);

        public void Reset()
        {
            dailyWage = 25;
            citizenWage = 40;
            artisanWage = 60;
            scholarWage = 50;
            soldierWage = 25;
            commissionExcellent = 10;
            commissionMasterwork = 25;
            commissionLegendary = 60;
            paydayHour = 7;
            paydayInterval = 5;
            defaultPayrollTarget = 500;
            citizenshipPrice = 1500;
            nobilityPrice = 5000;
            rentBufferDays = 5;
            gastronomyPays = true;
            registersPullSilver = true;
            pocketMoney = 50;
            landPricePerCell = 10;
            buildingValuePercent = 100;
            autoBuyLots = true;
            restaurantStockForSale = true;
            lotRentPercent = 10;
            autoRentLots = true;
            citizenshipNeedsHome = true;
            foodMarkupPercent = 100;
            taxCommoner = 30;
            taxCitizen = 60;
            taxArtisan = 100;
            taxScholar = 80;
            taxNoble = 0;
            debtorsMoveDown = true;
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref dailyWage, "dailyWage", 25);
            Scribe_Values.Look(ref citizenWage, "citizenWage", 40);
            Scribe_Values.Look(ref artisanWage, "artisanWage", 60);
            Scribe_Values.Look(ref scholarWage, "scholarWage", 50);
            Scribe_Values.Look(ref soldierWage, "soldierWage", 25);
            Scribe_Values.Look(ref commissionExcellent, "commissionExcellent", 10);
            Scribe_Values.Look(ref commissionMasterwork, "commissionMasterwork", 25);
            Scribe_Values.Look(ref commissionLegendary, "commissionLegendary", 60);
            Scribe_Values.Look(ref paydayHour, "paydayHour", 7);
            Scribe_Values.Look(ref paydayInterval, "paydayInterval", 5);
            Scribe_Values.Look(ref defaultPayrollTarget, "defaultPayrollTarget", 500);
            Scribe_Values.Look(ref citizenshipPrice, "citizenshipPrice", 1500);
            Scribe_Values.Look(ref nobilityPrice, "nobilityPrice", 5000);
            Scribe_Values.Look(ref rentBufferDays, "rentBufferDays", 5);
            Scribe_Values.Look(ref gastronomyPays, "gastronomyPays", true);
            Scribe_Values.Look(ref registersPullSilver, "registersPullSilver", true);
            Scribe_Values.Look(ref pocketMoney, "pocketMoney", 50);
            Scribe_Values.Look(ref landPricePerCell, "landPricePerCell", 10);
            Scribe_Values.Look(ref buildingValuePercent, "buildingValuePercent", 100);
            Scribe_Values.Look(ref autoBuyLots, "autoBuyLots", true);
            Scribe_Values.Look(ref restaurantStockForSale, "restaurantStockForSale", true);
            Scribe_Values.Look(ref lotRentPercent, "lotRentPercent", 10);
            Scribe_Values.Look(ref autoRentLots, "autoRentLots", true);
            Scribe_Values.Look(ref citizenshipNeedsHome, "citizenshipNeedsHome", true);
            Scribe_Values.Look(ref foodMarkupPercent, "foodMarkupPercent", 100);
            Scribe_Values.Look(ref taxCommoner, "taxCommoner", 30);
            Scribe_Values.Look(ref taxCitizen, "taxCitizen", 60);
            Scribe_Values.Look(ref taxArtisan, "taxArtisan", 100);
            Scribe_Values.Look(ref taxScholar, "taxScholar", 80);
            Scribe_Values.Look(ref taxNoble, "taxNoble", 0);
            Scribe_Values.Look(ref debtorsMoveDown, "debtorsMoveDown", true);
        }
    }

    public class WagesMod : Mod
    {
        public static WagesSettings Settings;

        public WagesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WagesSettings>();
            new Harmony("cashew.commoners.wages").PatchAll();
        }

        public override string SettingsCategory() => "Society: Wages";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard { ColumnWidth = (inRect.width - 34f) / 2f };
            list.Begin(inRect);
            Settings.dailyWage = Money(list, "CW_SettingsWageCommoner", Settings.dailyWage, 0, 200, 1);
            Settings.citizenWage = Money(list, "CW_SettingsWageCitizen", Settings.citizenWage, 0, 200, 1);
            Settings.artisanWage = Money(list, "CW_SettingsWageArtisan", Settings.artisanWage, 0, 300, 1);
            Settings.scholarWage = Money(list, "CW_SettingsWageScholar", Settings.scholarWage, 0, 300, 1);
            Settings.soldierWage = Money(list, "CW_SettingsWageSoldier", Settings.soldierWage, 0, 200, 1);
            Settings.commissionExcellent = Money(list, "CW_SettingsCommissionExcellent", Settings.commissionExcellent, 0, 200, 1);
            Settings.commissionMasterwork = Money(list, "CW_SettingsCommissionMasterwork", Settings.commissionMasterwork, 0, 500, 5);
            Settings.commissionLegendary = Money(list, "CW_SettingsCommissionLegendary", Settings.commissionLegendary, 0, 1000, 5);
            Settings.paydayHour = Mathf.RoundToInt(list.SliderLabeled("CW_SettingsPaydayHour".Translate(Settings.paydayHour + ":00"), Settings.paydayHour, 0f, 23f));
            int intervalIndex = System.Array.IndexOf(WagesSettings.PaydayIntervals, Settings.PaydayInterval);
            intervalIndex = Mathf.RoundToInt(list.SliderLabeled("CW_SettingsPaydayInterval".Translate(Settings.PaydayInterval, GenDate.DaysPerQuadrum),
                intervalIndex, 0f, WagesSettings.PaydayIntervals.Length - 1));
            Settings.paydayInterval = WagesSettings.PaydayIntervals[intervalIndex];
            Settings.defaultPayrollTarget = Money(list, "CW_SettingsDefaultTarget", Settings.defaultPayrollTarget, 50, 5000, 50);
            Settings.citizenshipPrice = Money(list, "CW_SettingsCitizenship", Settings.citizenshipPrice, 100, 10000, 100);
            Settings.nobilityPrice = Money(list, "CW_SettingsNobility", Settings.nobilityPrice, 500, 30000, 250);
            Settings.rentBufferDays = Mathf.RoundToInt(list.SliderLabeled("CW_SettingsRentBuffer".Translate(Settings.rentBufferDays), Settings.rentBufferDays, 1f, 30f));
            Settings.pocketMoney = Money(list, "CW_SettingsPocketMoney", Settings.pocketMoney, 0, 500, 10);
            list.Gap();
            Settings.taxCommoner = Money(list, "CW_SettingsTaxCommoner", Settings.taxCommoner, 0, 1000, 5);
            Settings.taxCitizen = Money(list, "CW_SettingsTaxCitizen", Settings.taxCitizen, 0, 1000, 5);
            Settings.taxArtisan = Money(list, "CW_SettingsTaxArtisan", Settings.taxArtisan, 0, 1000, 5);
            Settings.taxScholar = Money(list, "CW_SettingsTaxScholar", Settings.taxScholar, 0, 1000, 5);
            Settings.taxNoble = Money(list, "CW_SettingsTaxNoble", Settings.taxNoble, 0, 2000, 5);
            list.CheckboxLabeled("CW_SettingsDebtorsMoveDown".Translate(), ref Settings.debtorsMoveDown, "CW_SettingsDebtorsMoveDownDesc".Translate());
            list.NewColumn();
            Settings.landPricePerCell = Money(list, "CW_SettingsLandPrice", Settings.landPricePerCell, 0, 100, 1);
            Settings.buildingValuePercent = Mathf.RoundToInt(list.SliderLabeled("CW_SettingsBuildingValue".Translate(Settings.buildingValuePercent), Settings.buildingValuePercent, 0f, 300f) / 5f) * 5;
            list.CheckboxLabeled("CW_SettingsAutoBuyLots".Translate(), ref Settings.autoBuyLots, "CW_SettingsAutoBuyLotsDesc".Translate());
            Settings.lotRentPercent = Mathf.RoundToInt(list.SliderLabeled("CW_SettingsLotRent".Translate(Settings.lotRentPercent, LeaseUtility.RentPeriodDays), Settings.lotRentPercent, 1f, 50f));
            list.CheckboxLabeled("CW_SettingsAutoRentLots".Translate(), ref Settings.autoRentLots, "CW_SettingsAutoRentLotsDesc".Translate());
            list.CheckboxLabeled("CW_SettingsCitizenNeedsHome".Translate(), ref Settings.citizenshipNeedsHome, "CW_SettingsCitizenNeedsHomeDesc".Translate());
            list.Gap();
            list.CheckboxLabeled("CW_SettingsGastronomy".Translate(), ref Settings.gastronomyPays);
            list.CheckboxLabeled("CW_SettingsStockForSale".Translate(), ref Settings.restaurantStockForSale, "CW_SettingsStockForSaleDesc".Translate());
            Settings.foodMarkupPercent = Mathf.RoundToInt(list.SliderLabeled("CW_SettingsFoodMarkup".Translate(Settings.foodMarkupPercent), Settings.foodMarkupPercent, 0f, 300f) / 5f) * 5;
            list.CheckboxLabeled("CW_SettingsPullSilver".Translate(), ref Settings.registersPullSilver);
            list.Gap();
            if (list.ButtonText("CW_SettingsReset".Translate()))
            {
                Settings.Reset();
            }
            list.End();
        }

        private static int Money(Listing_Standard list, string key, int value, int min, int max, int step)
        {
            float raw = list.SliderLabeled(key.Translate(value.ToStringMoney()), value, min, max);
            return Mathf.RoundToInt(raw / step) * step;
        }
    }

    [DefOf]
    public static class CW_DefOf
    {
        public static JobDef CW_CollectWages;
        public static JobDef CW_BuyCitizenship;
        public static JobDef CW_BuyNobility;
        public static JobDef CW_DepositSavings;
        public static JobDef CW_WithdrawSavings;
        public static JobDef CW_BuyLot;
        public static ThingDef CW_Strongbox;
        public static ThoughtDef CW_Payday;
        public static ThoughtDef CW_BoughtCitizenship;
        public static ThoughtDef CW_BoughtNobility;
        public static ThoughtDef CW_Evicted;
        public static ThoughtDef CW_BoughtHome;
        public static ThoughtDef CW_GrantedHome;
        public static ThoughtDef CW_HomeSeized;
        public static ThoughtDef CW_TaxesForgiven;

        static CW_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(CW_DefOf));
        }
    }

    [StaticConstructorOnStartup]
    public static class WagesStartup
    {
        static WagesStartup()
        {
            // Noble seats are sold for savings, so Commoners hides its free "make noble" option.
            CasteUtility.NobilityMustBePurchased = true;
            CasteUtility.ArtisanMadeItem += WageUtility.OnArtisanMadeItem;

            PayrollView.Register();

            // Every colonist bed can carry a rent. Hospitality guest beds have their own fee system, so they're skipped.
            System.Type guestBed = AccessTools.TypeByName("Hospitality.Building_GuestBed");
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                // Every humanlike race carries a wage ledger. It's inert unless the pawn is in a paid caste.
                if (def.race != null && def.race.Humanlike)
                {
                    def.comps ??= new System.Collections.Generic.List<CompProperties>();
                    if (!def.comps.Any(c => c is CompProperties_Wages))
                    {
                        def.comps.Add(new CompProperties_Wages());
                    }
                    continue;
                }
                // Shelves, fridges and other storage buildings can be marked for sale.
                if (def.thingClass != null && typeof(Building_Storage).IsAssignableFrom(def.thingClass))
                {
                    def.comps ??= new System.Collections.Generic.List<CompProperties>();
                    if (!def.comps.Any(c => c is CompProperties_ForSale))
                    {
                        def.comps.Add(new CompProperties_ForSale());
                    }
                    continue;
                }
                if (!def.IsBed || def.building == null || !def.building.bed_humanlike)
                {
                    continue;
                }
                if (guestBed != null && guestBed.IsAssignableFrom(def.thingClass))
                {
                    continue;
                }
                def.comps ??= new System.Collections.Generic.List<CompProperties>();
                if (!def.comps.Any(c => c is CompProperties_Rent))
                {
                    def.comps.Add(new CompProperties_Rent());
                }
                LinkStrongbox(def);
            }
        }

        // Strongboxes link to beds the same way end tables do. Both sides need to list each other,
        // and the facility side's list is normally built during def loading, which has already happened.
        private static void LinkStrongbox(ThingDef bed)
        {
            CompProperties_Facility facility = CW_DefOf.CW_Strongbox.GetCompProperties<CompProperties_Facility>();
            CompProperties_AffectedByFacilities affected = bed.GetCompProperties<CompProperties_AffectedByFacilities>();
            if (facility == null)
            {
                return;
            }
            if (affected == null)
            {
                affected = new CompProperties_AffectedByFacilities();
                bed.comps.Add(affected);
            }
            affected.linkableFacilities ??= new System.Collections.Generic.List<ThingDef>();
            if (!affected.linkableFacilities.Contains(CW_DefOf.CW_Strongbox))
            {
                affected.linkableFacilities.Add(CW_DefOf.CW_Strongbox);
            }
            facility.linkableBuildings ??= new System.Collections.Generic.List<ThingDef>();
            if (!facility.linkableBuildings.Contains(bed))
            {
                facility.linkableBuildings.Add(bed);
            }
        }
    }
}

namespace CommonersWages
{
    public static class MoneyExtensions
    {
        public static string ToStringMoney(this int value) => ((float)value).ToStringMoney();
    }
}
