using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Commoners
{
    public class CommonersSettings : ModSettings
    {
        public static readonly string[] DefaultRestrictedWork = { "Research", "Art", "DarkStudy" };
        public static readonly string[] DefaultMenialWork = { "Hauling", "Cleaning", "PlantCutting", "Mining" };
        public static readonly string[] DefaultSkilledWork = { "Crafting", "Smithing", "Tailoring", "Art", "Research", "DarkStudy", "Doctor" };
        public const int DefaultSkillCap = 6;
        public static readonly Dictionary<string, int> DefaultSkillCapOverrides = new Dictionary<string, int>
        {
            { "Plants", 12 },
            { "Animals", 12 },
        };

        // Commoners
        public List<string> restrictedWork = DefaultRestrictedWork.ToList();
        public Dictionary<string, int> skillCaps = new Dictionary<string, int>();

        // Artisans
        public int artisanMinSkill = 10;
        public float artisanLearnFactor = 1.5f;
        public float artisanQualityChance = 0.2f;
        public float artisanInspirationFactor = 2f;
        public float artisanWorkshopImpressiveness = 30f;

        // Soldiers
        public float soldierLearnFactor = 1.5f;
        public int soldierSkillCap = 8;
        public List<string> skilledWork = DefaultSkilledWork.ToList();

        // Nobles (and the menial work artisans dislike)
        public List<string> menialWork = DefaultMenialWork.ToList();
        public int colonistsPerNobleSeat = 10;
        public bool hereditaryNobility = true;
        public bool nobleOnlyLeadership = false;
        public float nobleBedroomImpressiveness = 50f;
        public float lordshipRadius = 15f;

        // Social
        public int lookDownPerTier = -3;
        public int fellowCommonerOpinion = 3;
        public int guildmateOpinion = 3;
        public int fellowNobleOpinion = 2;
        public int soldierRespectOpinion = 4;
        public float crossCasteRomanceFactor = 0.5f;

        // Joining
        public float arrivalWindowDays = 3f;
        public float recruitResistanceFactor = 1.5f;

        // Interface
        public bool showColonistBarIcon = true;

        private List<WorkTypeDef> cachedRestrictedWork;
        private List<WorkTypeDef> cachedMenialWork;
        private List<WorkTypeDef> cachedSkilledWork;

        public List<WorkTypeDef> RestrictedWork => cachedRestrictedWork ??= ResolveWork(restrictedWork);

        public List<WorkTypeDef> MenialWork => cachedMenialWork ??= ResolveWork(menialWork);

        public List<WorkTypeDef> SkilledWork => cachedSkilledWork ??= ResolveWork(skilledWork);

        public void SetWorkSkilled(WorkTypeDef work, bool skilled)
        {
            Toggle(skilledWork, work, skilled);
            cachedSkilledWork = null;
        }

        private static List<WorkTypeDef> ResolveWork(List<string> names)
        {
            return names.Select(n => DefDatabase<WorkTypeDef>.GetNamedSilentFail(n)).Where(d => d != null).ToList();
        }

        public int SkillCap(SkillDef skill)
        {
            if (skillCaps.TryGetValue(skill.defName, out int cap))
            {
                return cap;
            }
            return DefaultSkillCapOverrides.TryGetValue(skill.defName, out int def) ? def : DefaultSkillCap;
        }

        public void SetSkillCap(SkillDef skill, int cap)
        {
            skillCaps[skill.defName] = cap;
        }

        public void SetWorkRestricted(WorkTypeDef work, bool restricted)
        {
            Toggle(restrictedWork, work, restricted);
            cachedRestrictedWork = null;
        }

        public void SetWorkMenial(WorkTypeDef work, bool menial)
        {
            Toggle(menialWork, work, menial);
            cachedMenialWork = null;
        }

        private static void Toggle(List<string> list, WorkTypeDef work, bool on)
        {
            list.Remove(work.defName);
            if (on)
            {
                list.Add(work.defName);
            }
        }

        public void Reset()
        {
            restrictedWork = DefaultRestrictedWork.ToList();
            skillCaps.Clear();
            artisanMinSkill = 10;
            artisanLearnFactor = 1.5f;
            artisanQualityChance = 0.2f;
            artisanInspirationFactor = 2f;
            artisanWorkshopImpressiveness = 30f;
            soldierLearnFactor = 1.5f;
            soldierSkillCap = 8;
            skilledWork = DefaultSkilledWork.ToList();
            cachedSkilledWork = null;
            menialWork = DefaultMenialWork.ToList();
            colonistsPerNobleSeat = 10;
            hereditaryNobility = true;
            nobleOnlyLeadership = false;
            nobleBedroomImpressiveness = 50f;
            lordshipRadius = 15f;
            lookDownPerTier = -3;
            fellowCommonerOpinion = 3;
            guildmateOpinion = 3;
            fellowNobleOpinion = 2;
            soldierRespectOpinion = 4;
            crossCasteRomanceFactor = 0.5f;
            arrivalWindowDays = 3f;
            recruitResistanceFactor = 1.5f;
            showColonistBarIcon = true;
            cachedRestrictedWork = null;
            cachedMenialWork = null;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref restrictedWork, "restrictedWork", LookMode.Value);
            Scribe_Collections.Look(ref skillCaps, "skillCaps", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref artisanMinSkill, "artisanMinSkill", 10);
            Scribe_Values.Look(ref artisanLearnFactor, "artisanLearnFactor", 1.5f);
            Scribe_Values.Look(ref artisanQualityChance, "artisanQualityChance", 0.2f);
            Scribe_Values.Look(ref artisanInspirationFactor, "artisanInspirationFactor", 2f);
            Scribe_Values.Look(ref artisanWorkshopImpressiveness, "artisanWorkshopImpressiveness", 30f);
            Scribe_Collections.Look(ref menialWork, "menialWork", LookMode.Value);
            Scribe_Values.Look(ref soldierLearnFactor, "soldierLearnFactor", 1.5f);
            Scribe_Values.Look(ref soldierSkillCap, "soldierSkillCap", 8);
            Scribe_Collections.Look(ref skilledWork, "skilledWork", LookMode.Value);
            Scribe_Values.Look(ref colonistsPerNobleSeat, "colonistsPerNobleSeat", 10);
            Scribe_Values.Look(ref hereditaryNobility, "hereditaryNobility", true);
            Scribe_Values.Look(ref nobleOnlyLeadership, "nobleOnlyLeadership", false);
            Scribe_Values.Look(ref nobleBedroomImpressiveness, "nobleBedroomImpressiveness", 50f);
            Scribe_Values.Look(ref lordshipRadius, "lordshipRadius", 15f);
            Scribe_Values.Look(ref lookDownPerTier, "lookDownPerTier", -3);
            Scribe_Values.Look(ref fellowCommonerOpinion, "fellowCommonerOpinion", 3);
            Scribe_Values.Look(ref guildmateOpinion, "guildmateOpinion", 3);
            Scribe_Values.Look(ref fellowNobleOpinion, "fellowNobleOpinion", 2);
            Scribe_Values.Look(ref soldierRespectOpinion, "soldierRespectOpinion", 4);
            Scribe_Values.Look(ref crossCasteRomanceFactor, "crossCasteRomanceFactor", 0.5f);
            Scribe_Values.Look(ref arrivalWindowDays, "arrivalWindowDays", 3f);
            Scribe_Values.Look(ref recruitResistanceFactor, "recruitResistanceFactor", 1.5f);
            Scribe_Values.Look(ref showColonistBarIcon, "showColonistBarIcon", true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                restrictedWork ??= DefaultRestrictedWork.ToList();
                menialWork ??= DefaultMenialWork.ToList();
                skilledWork ??= DefaultSkilledWork.ToList();
                cachedSkilledWork = null;
                skillCaps ??= new Dictionary<string, int>();
                cachedRestrictedWork = null;
                cachedMenialWork = null;
            }
        }
    }

    public class CommonersMod : Mod
    {
        public static CommonersSettings Settings;

        private Vector2 scrollPos;
        private float viewHeight = 2000f;

        public CommonersMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<CommonersSettings>();
            new Harmony("cashew.commoners").PatchAll();
        }

        public override string SettingsCategory() => "Society: Castes";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect view = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scrollPos, view);
            Listing_Standard list = new Listing_Standard();
            list.Begin(view);
            List<WorkTypeDef> workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderBy(w => w.labelShort).ToList();

            Header(list, "CM_SettingsWork");
            foreach (WorkTypeDef work in workTypes)
            {
                bool restricted = Settings.restrictedWork.Contains(work.defName);
                bool before = restricted;
                list.CheckboxLabeled(work.labelShort.CapitalizeFirst(), ref restricted);
                if (restricted != before)
                {
                    Settings.SetWorkRestricted(work, restricted);
                }
            }
            list.Gap();

            Header(list, "CM_SettingsSkills");
            foreach (SkillDef skill in DefDatabase<SkillDef>.AllDefsListForReading.OrderBy(s => s.listOrder))
            {
                int cap = Settings.SkillCap(skill);
                int newCap = Mathf.RoundToInt(list.SliderLabeled(skill.skillLabel.CapitalizeFirst() + ": " + cap, cap, 0f, 20f));
                if (newCap != cap)
                {
                    Settings.SetSkillCap(skill, newCap);
                }
            }
            list.Gap();

            Header(list, "CM_SettingsArtisans");
            Settings.artisanMinSkill = Mathf.RoundToInt(list.SliderLabeled("CM_SettingsArtisanMinSkill".Translate(Settings.artisanMinSkill), Settings.artisanMinSkill, 0f, 20f));
            Settings.artisanLearnFactor = Snap(list.SliderLabeled("CM_SettingsArtisanLearn".Translate(Settings.artisanLearnFactor.ToStringPercent()), Settings.artisanLearnFactor, 1f, 3f), 0.05f);
            Settings.artisanQualityChance = Snap(list.SliderLabeled("CM_SettingsArtisanQuality".Translate(Settings.artisanQualityChance.ToStringPercent()), Settings.artisanQualityChance, 0f, 1f), 0.05f);
            Settings.artisanInspirationFactor = Snap(list.SliderLabeled("CM_SettingsArtisanInspiration".Translate(Settings.artisanInspirationFactor.ToString("0.##")), Settings.artisanInspirationFactor, 1f, 5f), 0.25f);
            Settings.artisanWorkshopImpressiveness = Mathf.Round(list.SliderLabeled("CM_SettingsArtisanWorkshop".Translate(Settings.artisanWorkshopImpressiveness.ToString("0")), Settings.artisanWorkshopImpressiveness, -50f, 120f));
            list.Gap();

            Header(list, "CM_SettingsSoldiers");
            Settings.soldierLearnFactor = Snap(list.SliderLabeled("CM_SettingsSoldierLearn".Translate(Settings.soldierLearnFactor.ToStringPercent()), Settings.soldierLearnFactor, 1f, 3f), 0.05f);
            Settings.soldierSkillCap = Mathf.RoundToInt(list.SliderLabeled("CM_SettingsSoldierCap".Translate(Settings.soldierSkillCap), Settings.soldierSkillCap, 0f, 20f));
            list.Label("CM_SettingsSkilledWork".Translate());
            foreach (WorkTypeDef work in workTypes)
            {
                bool skilled = Settings.skilledWork.Contains(work.defName);
                bool before = skilled;
                list.CheckboxLabeled(work.labelShort.CapitalizeFirst(), ref skilled);
                if (skilled != before)
                {
                    Settings.SetWorkSkilled(work, skilled);
                }
            }
            list.Gap();

            Header(list, "CM_SettingsMenial");
            foreach (WorkTypeDef work in workTypes)
            {
                bool menial = Settings.menialWork.Contains(work.defName);
                bool before = menial;
                list.CheckboxLabeled(work.labelShort.CapitalizeFirst(), ref menial);
                if (menial != before)
                {
                    Settings.SetWorkMenial(work, menial);
                }
            }
            list.Gap();

            Header(list, "CM_SettingsNobles");
            Settings.colonistsPerNobleSeat = Mathf.RoundToInt(list.SliderLabeled("CM_SettingsSeats".Translate(Settings.colonistsPerNobleSeat), Settings.colonistsPerNobleSeat, 2f, 30f));
            list.CheckboxLabeled("CM_SettingsHereditary".Translate(), ref Settings.hereditaryNobility);
            if (ModsConfig.IdeologyActive)
            {
                list.CheckboxLabeled("CM_SettingsNobleLeadership".Translate(), ref Settings.nobleOnlyLeadership);
            }
            Settings.nobleBedroomImpressiveness = Mathf.Round(list.SliderLabeled("CM_SettingsNobleBedroom".Translate(Settings.nobleBedroomImpressiveness.ToString("0")), Settings.nobleBedroomImpressiveness, 0f, 170f));
            Settings.lordshipRadius = Mathf.Round(list.SliderLabeled("CM_SettingsLordshipRadius".Translate(Settings.lordshipRadius.ToString("0")), Settings.lordshipRadius, 0f, 40f));
            list.Gap();

            Header(list, "CM_SettingsSocial");
            Settings.lookDownPerTier = Mathf.RoundToInt(list.SliderLabeled("CM_SettingsLookDown".Translate(Settings.lookDownPerTier.ToStringWithSign()), Settings.lookDownPerTier, -10f, 0f));
            Settings.fellowCommonerOpinion = Mathf.RoundToInt(list.SliderLabeled("CM_SettingsFellow".Translate(Settings.fellowCommonerOpinion.ToStringWithSign()), Settings.fellowCommonerOpinion, 0f, 20f));
            Settings.guildmateOpinion = Mathf.RoundToInt(list.SliderLabeled("CM_SettingsGuildmate".Translate(Settings.guildmateOpinion.ToStringWithSign()), Settings.guildmateOpinion, 0f, 20f));
            Settings.fellowNobleOpinion = Mathf.RoundToInt(list.SliderLabeled("CM_SettingsFellowNoble".Translate(Settings.fellowNobleOpinion.ToStringWithSign()), Settings.fellowNobleOpinion, 0f, 20f));
            Settings.soldierRespectOpinion = Mathf.RoundToInt(list.SliderLabeled("CM_SettingsSoldierRespect".Translate(Settings.soldierRespectOpinion.ToStringWithSign()), Settings.soldierRespectOpinion, 0f, 20f));
            Settings.crossCasteRomanceFactor = Snap(list.SliderLabeled("CM_SettingsRomance".Translate(Settings.crossCasteRomanceFactor.ToStringPercent()), Settings.crossCasteRomanceFactor, 0f, 1f), 0.05f);
            list.Gap();

            Header(list, "CM_SettingsJoining");
            Settings.arrivalWindowDays = Snap(list.SliderLabeled("CM_SettingsArrival".Translate(Settings.arrivalWindowDays.ToString("0.#")), Settings.arrivalWindowDays, 0f, 15f), 0.5f);
            Settings.recruitResistanceFactor = Snap(list.SliderLabeled("CM_SettingsResistance".Translate(Settings.recruitResistanceFactor.ToStringPercent()), Settings.recruitResistanceFactor, 1f, 3f), 0.05f);
            list.Gap();

            Header(list, "CM_SettingsInterface");
            list.CheckboxLabeled("CM_SettingsBarIcon".Translate(), ref Settings.showColonistBarIcon);
            list.Gap();

            if (list.ButtonText("CM_SettingsReset".Translate()))
            {
                Settings.Reset();
            }

            viewHeight = list.CurHeight + 20f;
            list.End();
            Widgets.EndScrollView();
        }

        private static void Header(Listing_Standard list, string key)
        {
            list.Label(key.Translate().Colorize(ColoredText.TipSectionTitleColor));
        }

        private static float Snap(float value, float step) => Mathf.Round(value / step) * step;

        public override void WriteSettings()
        {
            base.WriteSettings();
            CasteUtility.ApplySettings();
        }
    }

    [StaticConstructorOnStartup]
    public static class CommonersStartup
    {
        static CommonersStartup()
        {
            // Every ideoligion role gets a "can't be a commoner" requirement, which the
            // role assignment UI and validation already know how to show and enforce.
            foreach (PreceptDef def in DefDatabase<PreceptDef>.AllDefsListForReading)
            {
                if (def.preceptClass != null && typeof(Precept_Role).IsAssignableFrom(def.preceptClass))
                {
                    def.roleRequirements ??= new List<RoleRequirement>();
                    def.roleRequirements.Add(RoleRequirement_NotCommoner.Requirement);
                }
            }
            CasteUtility.ApplySettings();
        }
    }
}
