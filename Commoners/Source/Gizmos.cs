using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Commoners
{
    [StaticConstructorOnStartup]
    public static class CommonersTextures
    {
        public static readonly Texture2D Commoner = ContentFinder<Texture2D>.Get("UI/Commoners/Commoner");
        public static readonly Texture2D Artisan = ContentFinder<Texture2D>.Get("UI/Commoners/Artisan");
        public static readonly Texture2D Scholar = ContentFinder<Texture2D>.Get("UI/Commoners/Scholar");
        public static readonly Texture2D Soldier = ContentFinder<Texture2D>.Get("UI/Commoners/Soldier");
        public static readonly Texture2D Noble = ContentFinder<Texture2D>.Get("UI/Commoners/Noble");
        public static readonly Texture2D Station = ContentFinder<Texture2D>.Get("UI/Commoners/Station");
        public static readonly Texture2D Promote = ContentFinder<Texture2D>.Get("UI/Commoners/Promote");
        public static readonly Texture2D Demote = ContentFinder<Texture2D>.Get("UI/Commoners/Demote");
        public static readonly Texture2D RecruitAsCommoner = ContentFinder<Texture2D>.Get("UI/Commoners/RecruitAsCommoner");

        // The colonist bar icon. Citizens are the default and get none.
        public static Texture2D CasteIcon(Caste caste)
        {
            switch (caste)
            {
                case Caste.Commoner:
                    return Commoner;
                case Caste.Artisan:
                    return Artisan;
                case Caste.Scholar:
                    return Scholar;
                case Caste.Soldier:
                    return Soldier;
                case Caste.Noble:
                    return Noble;
                default:
                    return null;
            }
        }
    }

    public static class CasteGizmos
    {
        public static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> gizmos, Pawn pawn)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                yield return gizmo;
            }
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike)
            {
                yield break;
            }
            if (CasteUtility.IsCasteEligible(pawn))
            {
                yield return StationGizmo(pawn);
            }
            else if (pawn.IsPrisonerOfColony && pawn.guest.Recruitable)
            {
                yield return RecruitGizmo(pawn);
            }
        }

        private static Gizmo StationGizmo(Pawn pawn)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = "CM_Station".Translate(CasteUtility.LabelFor(pawn)),
                defaultDesc = "CM_StationDesc".Translate(pawn.Named("PAWN")),
                icon = CommonersTextures.CasteIcon(CasteUtility.GetCaste(pawn)) ?? CommonersTextures.Station,
                action = () => Find.WindowStack.Add(new FloatMenu(CasteMenu.Options(pawn)))
            };
            if (CasteUtility.HasRoyalTitle(pawn))
            {
                command.Disable("CM_CannotTitle".Translate());
            }
            return command;
        }

        private static Gizmo RecruitGizmo(Pawn pawn)
        {
            return new Command_Toggle
            {
                defaultLabel = "CM_RecruitAsCommoner".Translate(),
                defaultDesc = "CM_RecruitAsCommonerDesc".Translate(),
                icon = CommonersTextures.RecruitAsCommoner,
                isActive = () => CasteUtility.HasCommonerOffer(pawn),
                toggleAction = () =>
                {
                    Hediff offer = pawn.health.hediffSet.GetFirstHediffOfDef(CM_DefOf.CM_CommonerOffer);
                    if (offer != null)
                    {
                        pawn.health.RemoveHediff(offer);
                    }
                    else
                    {
                        pawn.health.AddHediff(CM_DefOf.CM_CommonerOffer);
                    }
                }
            };
        }
    }

    // The "change caste" menu, shared by the pawn gizmo and the Castes tab.
    public static class CasteMenu
    {
        public static List<FloatMenuOption> Options(Pawn pawn)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            Caste current = CasteUtility.GetCaste(pawn);
            CM_SpecialtyDef currentSpecialty = CasteUtility.Specialty(pawn);

            Add(options, pawn, Caste.Commoner, null, CasteUtility.Label(Caste.Commoner));
            Add(options, pawn, Caste.Citizen, null, CasteUtility.Label(Caste.Citizen));
            Add(options, pawn, Caste.Soldier, null, CasteUtility.Label(Caste.Soldier));
            foreach (CM_SpecialtyDef specialty in DefDatabase<CM_SpecialtyDef>.AllDefsListForReading)
            {
                if (current == specialty.caste && specialty == currentSpecialty)
                {
                    continue;
                }
                int level = pawn.skills?.GetSkill(specialty.skill)?.Level ?? 0;
                string key = specialty.caste == Caste.Scholar ? "CM_MenuScholar" : "CM_MenuArtisan";
                Add(options, pawn, specialty.caste, specialty, key.Translate(specialty.label, level));
            }
            if (!CasteUtility.NobilityMustBePurchased || current == Caste.Noble)
            {
                int seats = CasteUtility.NobleSeats();
                string label = "CM_MenuNoble".Translate(CasteUtility.FreeNobleSeats(), seats);
                Add(options, pawn, Caste.Noble, null, label);
            }
            else
            {
                options.Add(new FloatMenuOption(CasteUtility.Label(Caste.Noble).CapitalizeFirst() + " (" + "CM_CannotMustBuy".Translate() + ")", null));
            }
            return options;
        }

        private static void Add(List<FloatMenuOption> options, Pawn pawn, Caste target, CM_SpecialtyDef specialty, string label)
        {
            Caste current = CasteUtility.GetCaste(pawn);
            if (target == current && !CasteUtility.IsSpecialistCaste(target))
            {
                return;
            }
            AcceptanceReport report = CasteUtility.CanChangeCaste(pawn, target, specialty);
            label = label.CapitalizeFirst();
            if (!report.Accepted)
            {
                options.Add(new FloatMenuOption(report.Reason.NullOrEmpty() ? label : label + " (" + report.Reason + ")", null));
                return;
            }
            options.Add(new FloatMenuOption(label, () => Choose(pawn, target, specialty)));
        }

        private static void Choose(Pawn pawn, Caste target, CM_SpecialtyDef specialty)
        {
            Caste current = CasteUtility.GetCaste(pawn);
            if (CasteUtility.Tier(target) >= CasteUtility.Tier(current))
            {
                CasteUtility.SetCaste(pawn, target, specialty);
                return;
            }
            string targetLabel = CasteUtility.IsSpecialistCaste(target) ? CasteUtility.LabelWithSpecialty(specialty) : CasteUtility.Label(target);
            TaggedString text = "CM_DemoteConfirm".Translate(pawn.Named("PAWN"), targetLabel.Named("CASTE"));
            if (ModsConfig.IdeologyActive && pawn.Ideo?.GetRole(pawn) is Precept_Role role)
            {
                bool losesRole = target == Caste.Commoner
                    || (CommonersMod.Settings.nobleOnlyLeadership && CasteUtility.LeadershipRoles.Contains(role.def.defName));
                if (losesRole)
                {
                    text += "\n\n" + "CM_DemoteConfirmRole".Translate(pawn.Named("PAWN"), role.LabelForPawn(pawn).Named("ROLE"));
                }
            }
            bool nullified = pawn.story?.traits != null && CM_DefOf.CM_Demoted.nullifyingTraits != null
                && CM_DefOf.CM_Demoted.nullifyingTraits.Exists(t => pawn.story.traits.HasTrait(t));
            if (CasteUtility.InArrivalWindow(pawn))
            {
                text += "\n\n" + "CM_DemoteConfirmGrace".Translate(pawn.Named("PAWN"));
            }
            else if (!nullified)
            {
                text += "\n\n" + "CM_DemoteConfirmPenalty".Translate(pawn.Named("PAWN"));
            }
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () => CasteUtility.SetCaste(pawn, target, specialty), destructive: true));
        }
    }
}
