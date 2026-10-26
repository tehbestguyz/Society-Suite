using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SocietyHomes
{
    public class HomesSettings : ModSettings
    {
        public float daysOfFood = 2f;
        public int pantryBonus = 40;
        public bool homeCooking = true;
        public bool homeRec = true;
        public float homeRecFactor = 4f;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref daysOfFood, "daysOfFood", 2f);
            Scribe_Values.Look(ref pantryBonus, "pantryBonus", 40);
            Scribe_Values.Look(ref homeCooking, "homeCooking", true);
            Scribe_Values.Look(ref homeRec, "homeRec", true);
            Scribe_Values.Look(ref homeRecFactor, "homeRecFactor", 4f);
        }
    }

    public class HomesMod : Mod
    {
        public static HomesSettings Settings;

        public HomesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<HomesSettings>();
            new Harmony("cashew.society.homes").PatchAll();
        }

        public override string SettingsCategory() => "Society: Homes";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("SH_SettingDaysOfFood".Translate(Settings.daysOfFood.ToString("0.#")), tooltip: "SH_SettingDaysOfFoodDesc".Translate());
            Settings.daysOfFood = Mathf.Round(list.Slider(Settings.daysOfFood, 0.5f, 7f) * 2f) / 2f;
            list.Label("SH_SettingPantryBonus".Translate(Settings.pantryBonus), tooltip: "SH_SettingPantryBonusDesc".Translate());
            Settings.pantryBonus = Mathf.RoundToInt(list.Slider(Settings.pantryBonus, 0f, 150f));
            list.CheckboxLabeled("SH_SettingHomeCooking".Translate(), ref Settings.homeCooking, "SH_SettingHomeCookingDesc".Translate());
            list.CheckboxLabeled("SH_SettingHomeRec".Translate(), ref Settings.homeRec, "SH_SettingHomeRecDesc".Translate());
            if (Settings.homeRec)
            {
                list.Label("SH_SettingHomeRecFactor".Translate(Settings.homeRecFactor.ToString("0.#")), tooltip: "SH_SettingHomeRecFactorDesc".Translate());
                Settings.homeRecFactor = Mathf.Round(list.Slider(Settings.homeRecFactor, 1f, 10f) * 2f) / 2f;
            }
            list.End();
        }
    }

    // Not readonly: on .NET 10 DefOf binding skips static readonly fields.
    [DefOf]
    public static class SH_DefOf
    {
        public static JobDef SH_GroceryRun;
        public static WorkTypeDef Cooking;
        public static ThoughtDef SH_AteAtHome;

        static SH_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SH_DefOf));
        }
    }

    [StaticConstructorOnStartup]
    public static class HomesStartup
    {
        static HomesStartup()
        {
            // Shelves, fridges and other storage buildings, modded ones included, can be made a pantry.
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.thingClass != null && typeof(Building_Storage).IsAssignableFrom(def.thingClass))
                {
                    def.comps ??= new System.Collections.Generic.List<CompProperties>();
                    if (!def.comps.Any(c => c is CompProperties_Pantry))
                    {
                        def.comps.Add(new CompProperties_Pantry());
                    }
                }
            }
        }
    }
}
