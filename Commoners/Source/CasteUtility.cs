using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Commoners
{
    // Ordered lowest to highest, so comparisons and differences between castes mean something.
    public enum Caste
    {
        Slave,
        Commoner,
        Citizen,
        Artisan,
        // Same tier as artisans (see CasteUtility.Tier); the enum order is not the ranking.
        Scholar,
        // Same tier as citizens.
        Soldier,
        Noble
    }

    [DefOf]
    public static class CM_DefOf
    {
        public static HediffDef CM_Commoner;
        public static HediffDef CM_CommonerOffer;
        public static HediffDef CM_Artisan;
        public static HediffDef CM_Scholar;
        public static HediffDef CM_Soldier;
        public static HediffDef CM_Noble;
        public static ThoughtDef CM_Demoted;
        public static ThoughtDef CM_Promoted;
        public static ThoughtDef CM_RaisedFromSlavery;
        public static ThoughtDef CM_LowerStation;
        public static ThoughtDef CM_FellowCommoner;
        public static ThoughtDef CM_Guildmate;
        public static ThoughtDef CM_FellowNoble;
        public static ThoughtDef CM_OurDefender;
        public static ThoughtDef CM_BeneathMyStation;
        public static ThoughtDef CM_FinestWork;
        public static ThoughtDef CM_PoorWorkshop;
        public static ThoughtDef CM_PoorStudy;
        public static ThoughtDef CM_NotSoldiersWork;
        public static NeedDef CM_Standing;

        [MayRequireIdeology]
        public static SlaveInteractionModeDef CM_ElevateToCommoner;

        [MayRequireIdeology]
        public static JobDef CM_ElevateSlave;

        [MayRequireIdeology]
        public static ThingDef CM_ParadeStart;

        [MayRequireIdeology]
        public static ThingDef CM_ParadeEnd;

        static CM_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(CM_DefOf));
        }
    }

    // An artisan or scholar specialty: the skill it covers, and the stat bonuses the marker gives.
    public class CM_SpecialtyDef : Def
    {
        // Artisan or Scholar.
        public Caste caste = Caste.Artisan;
        public SkillDef skill;
        // For scholars: the work their "wants a proper study" thought watches.
        public WorkTypeDef workType;
        public List<StatModifier> statOffsets;
        public List<StatModifier> statFactors;
        // Whether items made with this skill can get the artisan quality bump and commissions.
        public bool makesQualityItems = true;
        // Whether a poorly furnished room with the workbench upsets the artisan.
        public bool caresAboutWorkshop = true;

        [Unsaved]
        private HediffStage stage;

        public HediffStage Stage
        {
            get
            {
                if (stage == null)
                {
                    stage = new HediffStage
                    {
                        statOffsets = statOffsets,
                        statFactors = statFactors
                    };
                }
                return stage;
            }
        }
    }

    // Base class for the caste markers. Adding or removing one refreshes the pawn's work bans and needs.
    public class Hediff_CasteMark : HediffWithComps
    {
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            pawn.Notify_DisabledWorkTypesChanged();
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            pawn.Notify_DisabledWorkTypesChanged();
        }
    }

    // The class name is kept from v1 so existing saves load.
    public class Hediff_Commoner : Hediff_CasteMark
    {
    }

    public class Hediff_Artisan : Hediff_CasteMark
    {
        public CM_SpecialtyDef specialty;

        public override HediffStage CurStage => specialty?.Stage ?? base.CurStage;

        public override string LabelInBrackets => specialty?.label;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref specialty, "specialty");
        }
    }

    // Scholars keep their specialty the same way artisans do.
    public class Hediff_Scholar : Hediff_Artisan
    {
    }

    public class Hediff_Noble : Hediff_CasteMark
    {
    }

    public class RoleRequirement_NotCommoner : RoleRequirement
    {
        public static readonly RoleRequirement_NotCommoner Requirement = new RoleRequirement_NotCommoner();

        public override string GetLabel(Precept_Role role) => "CM_RoleRequirementNotCommoner".Translate();

        public override bool Met(Pawn p, Precept_Role role) => !CasteUtility.IsCommoner(p);
    }

    // Only attached to the leader and moral guide roles while the "nobles only" setting is on.
    public class RoleRequirement_Noble : RoleRequirement
    {
        public static readonly RoleRequirement_Noble Requirement = new RoleRequirement_Noble();

        public override string GetLabel(Precept_Role role) => "CM_RoleRequirementNoble".Translate();

        public override bool Met(Pawn p, Precept_Role role) => CasteUtility.GetCaste(p) == Caste.Noble;
    }

    public static class CasteUtility
    {
        public static CommonersSettings Settings => CommonersMod.Settings;

        // Set by add-ons (Wages) that sell noble seats, which hides the free "ennoble" option.
        public static bool NobilityMustBePurchased;

        // Raised whenever an artisan makes something in their specialty. Wages pays commissions from it.
        public static event Action<Pawn, QualityCategory> ArtisanMadeItem;

        public static readonly string[] LeadershipRoles = { "IdeoRole_Leader", "IdeoRole_Moralist" };

        // ===== Reading castes =====

        public static bool HasCommonerMark(Pawn p) => HasMark(p, CM_DefOf.CM_Commoner);

        public static bool HasArtisanMark(Pawn p) => HasMark(p, CM_DefOf.CM_Artisan);

        public static bool HasScholarMark(Pawn p) => HasMark(p, CM_DefOf.CM_Scholar);

        public static bool HasSoldierMark(Pawn p) => HasMark(p, CM_DefOf.CM_Soldier);

        public static bool HasNobleMark(Pawn p) => HasMark(p, CM_DefOf.CM_Noble);

        private static bool HasMark(Pawn p, HediffDef def)
        {
            return p?.health?.hediffSet != null && p.health.hediffSet.HasHediff(def);
        }

        public static bool HasRoyalTitle(Pawn p)
        {
            return p?.royalty != null && p.royalty.AllTitlesForReading.Count > 0;
        }

        // The caste whose rules apply right now. Prisoners keep their markers (so an arrested pawn
        // returns to the same caste) but are treated as citizens, which means no caste rules at all.
        public static Caste GetCaste(Pawn p)
        {
            if (p == null)
            {
                return Caste.Citizen;
            }
            if (p.IsSlave)
            {
                return Caste.Slave;
            }
            if (p.IsPrisoner || p.health?.hediffSet == null)
            {
                return Caste.Citizen;
            }
            if (HasNobleMark(p) || HasRoyalTitle(p))
            {
                return Caste.Noble;
            }
            if (HasCommonerMark(p))
            {
                return Caste.Commoner;
            }
            if (HasScholarMark(p))
            {
                return Caste.Scholar;
            }
            if (HasSoldierMark(p))
            {
                return Caste.Soldier;
            }
            if (HasArtisanMark(p))
            {
                return Caste.Artisan;
            }
            return Caste.Citizen;
        }

        public static bool IsCommoner(Pawn p) => GetCaste(p) == Caste.Commoner;

        public static bool IsArtisan(Pawn p) => GetCaste(p) == Caste.Artisan;

        public static bool IsScholar(Pawn p) => GetCaste(p) == Caste.Scholar;

        public static bool IsSoldier(Pawn p) => GetCaste(p) == Caste.Soldier;

        // Commoners, citizens and nobles look up to the colony's soldiers. Artisans and scholars don't:
        // they look down on them as brutish, like anyone else below their tier.
        public static bool RespectsSoldiers(Caste caste) => caste == Caste.Commoner || caste == Caste.Citizen || caste == Caste.Noble;

        public static bool IsCombatSkill(SkillDef skill) => skill == SkillDefOf.Shooting || skill == SkillDefOf.Melee;

        // The highest level this pawn's caste lets them reach in a skill, or -1 for no cap. Commoners have
        // the per-skill caps from the settings; soldiers are capped outside Shooting and Melee.
        public static int SkillCapFor(Pawn p, SkillDef skill)
        {
            switch (GetCaste(p))
            {
                case Caste.Commoner:
                    return Settings.SkillCap(skill);
                case Caste.Soldier:
                    return IsCombatSkill(skill) ? -1 : Settings.soldierSkillCap;
                default:
                    return -1;
            }
        }

        // Artisans and scholars: castes earned through a specialty.
        public static bool IsSpecialistCaste(Caste caste) => caste == Caste.Artisan || caste == Caste.Scholar;

        public static bool IsSpecialist(Pawn p) => IsSpecialistCaste(GetCaste(p));

        // Rank for comparing castes: artisans and scholars share a tier.
        public static int Tier(Caste caste)
        {
            switch (caste)
            {
                case Caste.Slave:
                    return 0;
                case Caste.Commoner:
                    return 1;
                case Caste.Citizen:
                case Caste.Soldier:
                    return 2;
                case Caste.Artisan:
                case Caste.Scholar:
                    return 3;
                default:
                    return 4;
            }
        }

        public static int Tier(Pawn p) => Tier(GetCaste(p));

        public static bool IsNoble(Pawn p) => GetCaste(p) == Caste.Noble;

        // A noble by the colony's own ennoblement, not (only) by an Empire title. The colony's noble
        // costs and stat bonuses apply to these pawns; Empire nobles follow their title's rules instead.
        public static bool IsColonyNoble(Pawn p)
        {
            return HasNobleMark(p) && !HasRoyalTitle(p) && !p.IsSlave && !p.IsPrisoner;
        }

        public static CM_SpecialtyDef Specialty(Pawn p)
        {
            switch (GetCaste(p))
            {
                case Caste.Artisan:
                    return (p.health.hediffSet.GetFirstHediffOfDef(CM_DefOf.CM_Artisan) as Hediff_Artisan)?.specialty;
                case Caste.Scholar:
                    return (p.health.hediffSet.GetFirstHediffOfDef(CM_DefOf.CM_Scholar) as Hediff_Artisan)?.specialty;
                default:
                    return null;
            }
        }

        // Whether `p` is a free member of the player's colony that caste social rules apply to.
        public static bool IsColonyMember(Pawn p)
        {
            return p != null && p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer && p.IsFreeNonSlaveColonist && !p.IsQuestLodger();
        }

        public static bool IsCasteEligible(Pawn p)
        {
            return p != null && !p.Dead && p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer
                && p.IsFreeNonSlaveColonist && !p.IsQuestLodger() && !p.IsMutant;
        }

        public static bool HasCommonerOffer(Pawn p)
        {
            return p?.health?.hediffSet != null && p.health.hediffSet.HasHediff(CM_DefOf.CM_CommonerOffer);
        }

        public static string Label(Caste caste) => ("CM_Caste_" + caste).Translate();

        public static string LabelFor(Pawn p)
        {
            Caste caste = GetCaste(p);
            CM_SpecialtyDef specialty = IsSpecialistCaste(caste) ? Specialty(p) : null;
            return specialty != null ? LabelWithSpecialty(specialty) : Label(caste);
        }

        public static string LabelWithSpecialty(CM_SpecialtyDef specialty)
        {
            return (specialty.caste == Caste.Scholar ? "CM_ScholarWithSpecialty" : "CM_ArtisanWithSpecialty").Translate(specialty.label).ToString();
        }

        // ===== Noble seats =====

        public static IEnumerable<Pawn> ColonyMembers()
        {
            return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Where(IsColonyMember);
        }

        public static int NobleSeats()
        {
            int per = Math.Max(1, Settings.colonistsPerNobleSeat);
            return ColonyMembers().Count() / per;
        }

        // Empire-titled nobles don't take a seat.
        public static int NobleSeatsUsed()
        {
            return ColonyMembers().Count(IsColonyNoble);
        }

        public static int FreeNobleSeats() => Math.Max(0, NobleSeats() - NobleSeatsUsed());

        // ===== Changing caste =====

        public static bool InArrivalWindow(Pawn p)
        {
            float ticks = p.records?.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal) ?? 0f;
            return ticks < Settings.arrivalWindowDays * GenDate.TicksPerDay;
        }

        public static IEnumerable<CM_SpecialtyDef> QualifyingSpecialties(Pawn p, Caste caste)
        {
            return SpecialtiesOf(caste).Where(s => QualifiesFor(p, s).Accepted);
        }

        public static IEnumerable<CM_SpecialtyDef> SpecialtiesOf(Caste caste)
        {
            return DefDatabase<CM_SpecialtyDef>.AllDefsListForReading.Where(s => s.caste == caste);
        }

        public static AcceptanceReport QualifiesFor(Pawn p, CM_SpecialtyDef specialty)
        {
            SkillRecord skill = p.skills?.GetSkill(specialty.skill);
            if (skill == null || skill.TotallyDisabled)
            {
                return "CM_CannotSkillDisabled".Translate(specialty.skill.skillLabel);
            }
            if (skill.Level < Settings.artisanMinSkill)
            {
                return "CM_CannotSkillTooLow".Translate(specialty.skill.skillLabel, Settings.artisanMinSkill);
            }
            return true;
        }

        // `purchased` is true when an add-on is completing a paid ennoblement.
        public static AcceptanceReport CanChangeCaste(Pawn p, Caste target, CM_SpecialtyDef specialty = null, bool purchased = false)
        {
            if (!IsCasteEligible(p) || target == Caste.Slave)
            {
                return false;
            }
            Caste current = GetCaste(p);
            if (target == current && (!IsSpecialistCaste(target) || specialty == Specialty(p)))
            {
                return "CM_CannotAlready".Translate();
            }
            if (HasRoyalTitle(p))
            {
                return "CM_CannotTitle".Translate();
            }
            switch (target)
            {
                case Caste.Artisan:
                case Caste.Scholar:
                    if (specialty == null || specialty.caste != target)
                    {
                        return false;
                    }
                    return QualifiesFor(p, specialty);
                case Caste.Soldier:
                    if (p.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        return "CM_CannotNonViolent".Translate();
                    }
                    return true;
                case Caste.Noble:
                    if (NobilityMustBePurchased && !purchased)
                    {
                        return "CM_CannotMustBuy".Translate();
                    }
                    if (FreeNobleSeats() <= 0)
                    {
                        return "CM_CannotNoSeat".Translate(Settings.colonistsPerNobleSeat);
                    }
                    return true;
                default:
                    return true;
            }
        }

        // Moves a pawn to a caste. With `thoughts`, the pawn gets the promotion or demotion memory and a message.
        public static void SetCaste(Pawn p, Caste target, CM_SpecialtyDef specialty = null, bool thoughts = true)
        {
            Caste from = GetCaste(p);
            RemoveMark(p, CM_DefOf.CM_Commoner);
            RemoveMark(p, CM_DefOf.CM_Artisan);
            RemoveMark(p, CM_DefOf.CM_Scholar);
            RemoveMark(p, CM_DefOf.CM_Soldier);
            RemoveMark(p, CM_DefOf.CM_Noble);
            switch (target)
            {
                case Caste.Commoner:
                    p.health.AddHediff(CM_DefOf.CM_Commoner);
                    break;
                case Caste.Artisan:
                case Caste.Scholar:
                    Hediff_Artisan marker = (Hediff_Artisan)HediffMaker.MakeHediff(target == Caste.Scholar ? CM_DefOf.CM_Scholar : CM_DefOf.CM_Artisan, p);
                    marker.specialty = specialty ?? QualifyingSpecialties(p, target).FirstOrDefault() ?? SpecialtiesOf(target).First();
                    p.health.AddHediff(marker);
                    break;
                case Caste.Soldier:
                    p.health.AddHediff(CM_DefOf.CM_Soldier);
                    break;
                case Caste.Noble:
                    p.health.AddHediff(CM_DefOf.CM_Noble);
                    break;
            }
            p.needs?.AddOrRemoveNeedsAsAppropriate();
            StripForbiddenRoles(p);
            if (!thoughts || p.needs?.mood == null)
            {
                return;
            }
            MemoryThoughtHandler memories = p.needs.mood.thoughts.memories;
            if (Tier(target) < Tier(from))
            {
                if (!InArrivalWindow(p))
                {
                    int stage = Math.Min(Tier(from) - Tier(target), CM_DefOf.CM_Demoted.stages.Count) - 1;
                    memories.TryGainMemory(ThoughtMaker.MakeThought(CM_DefOf.CM_Demoted, stage));
                }
                Messages.Message("CM_MessageDemoted".Translate(p.Named("PAWN"), LabelFor(p).Named("CASTE")), p, MessageTypeDefOf.NeutralEvent, historical: false);
            }
            else if (Tier(target) > Tier(from) || IsSpecialistCaste(target))
            {
                memories.RemoveMemoriesOfDef(CM_DefOf.CM_Demoted);
                if (Tier(target) > Tier(from))
                {
                    memories.TryGainMemory(ThoughtMaker.MakeThought(CM_DefOf.CM_Promoted, Math.Min(PromotedStage(target), CM_DefOf.CM_Promoted.stages.Count - 1)));
                }
                Messages.Message("CM_MessagePromoted".Translate(p.Named("PAWN"), LabelFor(p).Named("CASTE")), p, MessageTypeDefOf.PositiveEvent, historical: false);
            }
            else if (target != from)
            {
                // A sideways move, such as citizen to soldier.
                Messages.Message("CM_MessageCasteChanged".Translate(p.Named("PAWN"), LabelFor(p).Named("CASTE")), p, MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        private static void RemoveMark(Pawn p, HediffDef def)
        {
            Hediff mark = p.health.hediffSet.GetFirstHediffOfDef(def);
            if (mark != null)
            {
                p.health.RemoveHediff(mark);
            }
        }

        // Commoners can't hold any ideoligion role, and with the setting on only nobles can lead.
        public static void StripForbiddenRoles(Pawn p)
        {
            if (!ModsConfig.IdeologyActive || p.Ideo == null)
            {
                return;
            }
            Precept_Role role = p.Ideo.GetRole(p);
            if (role == null)
            {
                return;
            }
            bool forbidden = IsCommoner(p) || (Settings.nobleOnlyLeadership && LeadershipRoles.Contains(role.def.defName) && !IsNoble(p));
            if (!forbidden)
            {
                return;
            }
            string roleLabel = role.LabelForPawn(p);
            role.Unassign(p, generateThoughts: true);
            Messages.Message("CM_MessageRoleLost".Translate(p.Named("PAWN"), roleLabel.Named("ROLE")), p, MessageTypeDefOf.NegativeEvent, historical: false);
        }

        // ===== v1 entry points, kept for the Wages add-on and the age 3 letter =====

        public static AcceptanceReport CanDemote(Pawn p) => CanChangeCaste(p, Caste.Commoner);

        public static void Demote(Pawn p) => SetCaste(p, Caste.Commoner);

        public static void Promote(Pawn p, bool withThought = true)
        {
            if (HasCommonerMark(p))
            {
                SetCaste(p, Caste.Citizen, thoughts: withThought);
            }
        }

        // Adds the commoner marker with no mood effects.
        public static void MakeCommoner(Pawn p)
        {
            if (!HasCommonerMark(p))
            {
                SetCaste(p, Caste.Commoner, thoughts: false);
            }
        }

        // With no warden (the age 3 letter) this happens quietly: no memories, no message.
        public static void ElevateSlave(Pawn warden, Pawn slave)
        {
            if (!slave.IsSlaveOfColony)
            {
                return;
            }
            slave.ownership?.UnclaimAll();
            slave.guest.slaveInteractionMode = SlaveInteractionModeDefOf.Suppress;
            // A null host with the pawn already in the player faction makes them a free colonist.
            slave.guest.SetGuestStatus(null);
            slave.needs?.AddOrRemoveNeedsAsAppropriate();
            MakeCommoner(slave);
            if (slave.Ideo != null)
            {
                slave.Ideo.RecacheColonistBelieverCount();
            }
            if (warden == null)
            {
                return;
            }
            if (slave.needs?.mood != null)
            {
                slave.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.WasEnslaved);
                slave.needs.mood.thoughts.memories.TryGainMemory(CM_DefOf.CM_RaisedFromSlavery);
            }
            Messages.Message("CM_MessageElevated".Translate(slave.Named("PAWN"), warden.Named("WARDEN")), new LookTargets(slave, warden), MessageTypeDefOf.PositiveEvent);
        }

        internal static void NotifyArtisanMadeItem(Pawn p, QualityCategory quality)
        {
            ArtisanMadeItem?.Invoke(p, quality);
        }

        // ===== Settings =====

        // Pushes the settings that live in defs (opinion offsets, role requirements) and refreshes cached work bans.
        public static void ApplySettings()
        {
            if (CM_DefOf.CM_FellowCommoner != null)
            {
                CM_DefOf.CM_FellowCommoner.stages[0].baseOpinionOffset = Settings.fellowCommonerOpinion;
                CM_DefOf.CM_Guildmate.stages[0].baseOpinionOffset = Settings.guildmateOpinion;
                CM_DefOf.CM_FellowNoble.stages[0].baseOpinionOffset = Settings.fellowNobleOpinion;
                CM_DefOf.CM_OurDefender.stages[0].baseOpinionOffset = Settings.soldierRespectOpinion;
            }
            foreach (string defName in LeadershipRoles)
            {
                PreceptDef def = DefDatabase<PreceptDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    continue;
                }
                def.roleRequirements ??= new List<RoleRequirement>();
                def.roleRequirements.Remove(RoleRequirement_Noble.Requirement);
                if (Settings.nobleOnlyLeadership)
                {
                    def.roleRequirements.Add(RoleRequirement_Noble.Requirement);
                }
            }
            if (Current.Game == null)
            {
                return;
            }
            foreach (Pawn p in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (HasCommonerMark(p) || HasNobleMark(p))
                {
                    p.Notify_DisabledWorkTypesChanged();
                }
            }
        }

        public static Pawn CasteParent(Pawn child)
        {
            Pawn mother = child.GetMother();
            if (mother != null && mother.Faction == child.Faction)
            {
                return mother;
            }
            Pawn father = child.GetFather();
            if (father != null && father.Faction == child.Faction)
            {
                return father;
            }
            return null;
        }

        // Stages of CM_Promoted: citizen, artisan, noble, scholar, soldier (new ones added last, so old saves keep their stage).
        private static int PromotedStage(Caste target)
        {
            switch (target)
            {
                case Caste.Artisan:
                    return 1;
                case Caste.Noble:
                    return 2;
                case Caste.Scholar:
                    return 3;
                case Caste.Soldier:
                    return 4;
                default:
                    return 0;
            }
        }

        // The caste a newborn gets: noble if either parent is (when nobility is hereditary), otherwise
        // commoner if the caste parent is one. Artisan status is earned, so it's never inherited.
        public static Caste BirthCaste(Pawn baby)
        {
            if (Settings.hereditaryNobility)
            {
                foreach (Pawn parent in new[] { baby.GetMother(), baby.GetFather() })
                {
                    if (parent != null && parent.Faction == baby.Faction && IsNoble(parent))
                    {
                        return Caste.Noble;
                    }
                }
            }
            Pawn casteParent = CasteParent(baby);
            return casteParent != null && IsCommoner(casteParent) ? Caste.Commoner : Caste.Citizen;
        }

        // The caste a child takes on coming of age (13 for humans), whatever they were as a child:
        // commoner, unless a parent in the colony is higher. Artisans', scholars' and soldiers' children count as citizens',
        // and nobles' too when nobility isn't hereditary. Parents who've left the colony don't count.
        public static Caste ComingOfAgeCaste(Pawn p)
        {
            Caste best = Caste.Commoner;
            foreach (Pawn parent in new[] { p.GetMother(), p.GetFather() })
            {
                if (parent == null || parent.Faction != Faction.OfPlayer)
                {
                    continue;
                }
                Caste caste = GetCaste(parent);
                if (IsSpecialistCaste(caste) || caste == Caste.Soldier || (caste == Caste.Noble && !Settings.hereditaryNobility))
                {
                    caste = Caste.Citizen;
                }
                if (Tier(caste) > Tier(best))
                {
                    best = caste;
                }
            }
            return best;
        }

        public static void ComeOfAge(Pawn p)
        {
            if (!IsCasteEligible(p) || HasRoyalTitle(p))
            {
                return;
            }
            Caste target = ComingOfAgeCaste(p);
            if (target != GetCaste(p))
            {
                // Not a promotion or demotion: no memories.
                SetCaste(p, target, thoughts: false);
            }
            Messages.Message("CM_MessageComeOfAge".Translate(p.Named("PAWN"), LabelFor(p).Named("CASTE")), p, MessageTypeDefOf.NeutralEvent);
        }
    }
}
