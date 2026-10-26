using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Commoners
{
    // Tells a shared thought worker which caste a def is about.
    public class CasteThoughtExtension : DefModExtension
    {
        public Caste caste = Caste.Citizen;
        // For social thoughts: applies toward anyone of a higher caste than the thinker, instead of one caste.
        public bool anyHigher;
    }

    public static class CasteExpectations
    {
        // Whether the vanilla expectations thought is replaced by a caste version. Empire nobles keep
        // the vanilla one, since their titles already raise their expectations.
        public static bool HasCasteExpectations(Pawn p)
        {
            switch (CasteUtility.GetCaste(p))
            {
                case Caste.Commoner:
                case Caste.Soldier:
                case Caste.Artisan:
                case Caste.Scholar:
                    return true;
                case Caste.Noble:
                    return CasteUtility.IsColonyNoble(p);
                default:
                    return false;
            }
        }
    }

    // Replaces the normal expectations thought for commoners (and soldiers, who expect as little).
    // One stage per wealth tier, halfway between the colonist and slave versions.
    public class ThoughtWorker_ExpectationsCommoner : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Caste caste = def.GetModExtension<CasteThoughtExtension>()?.caste ?? Caste.Commoner;
            if (CasteUtility.GetCaste(p) != caste)
            {
                return ThoughtState.Inactive;
            }
            ExpectationDef expectation = ExpectationsUtility.CurrentExpectationFor(p);
            if (expectation == null)
            {
                return ThoughtState.Inactive;
            }
            return ThoughtState.ActiveAtStage(Mathf.Clamp(expectation.order, 0, def.stages.Count - 1));
        }
    }

    // Artisan and noble expectations. Stages follow the vanilla expectations thought's stage order,
    // with every value shifted down, so these castes are harder to please at every wealth tier.
    public class ThoughtWorker_ExpectationsCaste : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Caste caste = def.GetModExtension<CasteThoughtExtension>()?.caste ?? Caste.Citizen;
            if (CasteUtility.GetCaste(p) != caste || !CasteExpectations.HasCasteExpectations(p))
            {
                return ThoughtState.Inactive;
            }
            ExpectationDef expectation = ExpectationsUtility.CurrentExpectationFor(p);
            if (expectation == null)
            {
                return ThoughtState.Inactive;
            }
            return ThoughtState.ActiveAtStage(Mathf.Clamp(expectation.thoughtStage, 0, def.stages.Count - 1));
        }
    }

    // ===== Opinion between castes =====

    // Looking down on someone of a lower caste. The stage names the other pawn's caste;
    // the offset grows with each tier of difference.
    public class ThoughtWorker_LowerStation : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn otherPawn)
        {
            if (CommonersMod.Settings.lookDownPerTier == 0 || !CasteUtility.IsColonyMember(p) || !CasteUtility.IsColonyMember(otherPawn))
            {
                return false;
            }
            Caste other = CasteUtility.GetCaste(otherPawn);
            Caste mine = CasteUtility.GetCaste(p);
            if (CasteUtility.Tier(mine) <= CasteUtility.Tier(other))
            {
                return false;
            }
            // Nobles look up to soldiers rather than down on them (see ThoughtWorker_OurDefender).
            if (other == Caste.Soldier && CasteUtility.RespectsSoldiers(mine))
            {
                return false;
            }
            // Stages: commoner, citizen, artisan, scholar, soldier.
            return ThoughtState.ActiveAtStage(Mathf.Clamp(other - Caste.Commoner, 0, def.stages.Count - 1));
        }
    }

    public class Thought_LowerStation : Thought_SituationalSocial
    {
        public override float OpinionOffset()
        {
            if (ThoughtUtility.ThoughtNullified(pawn, def) || otherPawn == null)
            {
                return 0f;
            }
            int gap = CasteUtility.Tier(pawn) - CasteUtility.Tier(otherPawn);
            return gap > 0 ? CommonersMod.Settings.lookDownPerTier * gap : 0f;
        }
    }

    public class ThoughtWorker_FellowCommoner : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn otherPawn)
        {
            if (CommonersMod.Settings.fellowCommonerOpinion == 0)
            {
                return false;
            }
            return CasteUtility.IsCommoner(p) && CasteUtility.IsCommoner(otherPawn);
        }
    }

    public class ThoughtWorker_Guildmate : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn otherPawn)
        {
            if (CommonersMod.Settings.guildmateOpinion == 0)
            {
                return false;
            }
            CM_SpecialtyDef mine = CasteUtility.Specialty(p);
            return mine != null && CasteUtility.Specialty(otherPawn) == mine;
        }
    }

    // Commoners, citizens and nobles think well of the colony's soldiers: they're its defenders.
    public class ThoughtWorker_OurDefender : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn otherPawn)
        {
            if (CommonersMod.Settings.soldierRespectOpinion == 0 || !CasteUtility.IsColonyMember(p) || !CasteUtility.IsColonyMember(otherPawn))
            {
                return false;
            }
            return CasteUtility.IsSoldier(otherPawn) && CasteUtility.RespectsSoldiers(CasteUtility.GetCaste(p));
        }
    }

    public class ThoughtWorker_FellowNoble : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn otherPawn)
        {
            if (CommonersMod.Settings.fellowNobleOpinion == 0 || !CasteUtility.IsColonyMember(p) || !CasteUtility.IsColonyMember(otherPawn))
            {
                return false;
            }
            return CasteUtility.IsNoble(p) && CasteUtility.IsNoble(otherPawn);
        }
    }

    // ===== Traits =====

    // Jealous pawns resent anyone above their station.
    public class ThoughtWorker_ResentsStation : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn otherPawn)
        {
            if (!CasteUtility.IsColonyMember(p) || !CasteUtility.IsColonyMember(otherPawn))
            {
                return false;
            }
            return CasteUtility.Tier(otherPawn) > CasteUtility.Tier(p);
        }
    }

    // Greedy pawns want to rise above commoner or citizen.
    public class ThoughtWorker_Ambition : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!CasteUtility.IsColonyMember(p))
            {
                return false;
            }
            Caste caste = CasteUtility.GetCaste(p);
            return caste == Caste.Commoner || caste == Caste.Citizen || caste == Caste.Soldier;
        }
    }

    // ===== Nobles =====

    // Lower castes near a noble: a content noble lifts their mood (more with a higher Social skill),
    // a miserable one sours it.
    public class ThoughtWorker_Lordship : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            float radius = CommonersMod.Settings.lordshipRadius;
            if (radius <= 0f || !p.Spawned || !CasteUtility.IsColonyMember(p) || CasteUtility.IsNoble(p))
            {
                return false;
            }
            int best = -1;
            foreach (Pawn noble in p.Map.mapPawns.FreeColonistsSpawned)
            {
                if (noble == p || !CasteUtility.IsNoble(noble) || !CasteUtility.IsColonyMember(noble)
                    || !noble.Position.InHorDistOf(p.Position, radius) || noble.needs?.mood == null)
                {
                    continue;
                }
                int stage;
                if (noble.needs.mood.CurLevel < noble.mindState.mentalBreaker.BreakThresholdMinor)
                {
                    stage = 0;
                }
                else
                {
                    int social = noble.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                    stage = social >= 15 ? 3 : social >= 8 ? 2 : 1;
                }
                // A good lord nearby outweighs a bad one.
                best = Mathf.Max(best, stage);
            }
            return best < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(best);
        }
    }

    // Colony nobles expect a private, impressive bedroom.
    public class ThoughtWorker_NobleQuarters : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!CasteUtility.IsColonyNoble(p) || !p.DevelopmentalStage.Adult())
            {
                return false;
            }
            Building_Bed bed = p.ownership?.OwnedBed;
            if (bed == null || !bed.Spawned)
            {
                return false;
            }
            Room room = bed.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || room.Role != RoomRoleDefOf.Bedroom)
            {
                return ThoughtState.ActiveAtStage(0);
            }
            if (room.GetStat(RoomStatDefOf.Impressiveness) < CommonersMod.Settings.nobleBedroomImpressiveness)
            {
                return ThoughtState.ActiveAtStage(1);
            }
            return false;
        }
    }

    public class ThoughtWorker_NeedStanding : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            Need_Standing need = p.needs?.TryGetNeed<Need_Standing>();
            if (need == null)
            {
                return false;
            }
            float level = need.CurLevel;
            if (level < 0.15f)
            {
                return ThoughtState.ActiveAtStage(0);
            }
            if (level < 0.35f)
            {
                return ThoughtState.ActiveAtStage(1);
            }
            if (level > 0.8f)
            {
                return ThoughtState.ActiveAtStage(2);
            }
            return false;
        }
    }

    // ===== Ideoligion precepts =====

    // Egalitarian: any caste other than citizen in the colony.
    public class ThoughtWorker_Precept_CastesInColony : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (!CasteUtility.IsColonyMember(p))
            {
                return false;
            }
            return CasteUtility.ColonyMembers().Any(m => CasteUtility.HasCommonerMark(m) || CasteUtility.HasArtisanMark(m) || CasteUtility.HasScholarMark(m) || CasteUtility.HasSoldierMark(m) || CasteUtility.IsColonyNoble(m));
        }
    }

    // Hierarchical: the colony has nobility.
    public class ThoughtWorker_Precept_ProperOrder : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (!CasteUtility.IsColonyMember(p))
            {
                return false;
            }
            return CasteUtility.ColonyMembers().Any(CasteUtility.IsNoble);
        }
    }

    // Opinion of one caste (or of anyone higher), as set by the def's CasteThoughtExtension.
    public class ThoughtWorker_Precept_TowardCaste : ThoughtWorker_Precept_Social
    {
        protected override ThoughtState ShouldHaveThought(Pawn p, Pawn otherPawn)
        {
            if (!CasteUtility.IsColonyMember(p) || !CasteUtility.IsColonyMember(otherPawn))
            {
                return false;
            }
            CasteThoughtExtension ext = def.GetModExtension<CasteThoughtExtension>();
            if (ext == null)
            {
                return false;
            }
            // A thought about artisans covers scholars too: same tier, both earned through skill.
            Caste other = CasteUtility.GetCaste(otherPawn);
            return ext.anyHigher ? CasteUtility.Tier(other) > CasteUtility.Tier(p) : CasteUtility.Tier(other) == CasteUtility.Tier(ext.caste);
        }
    }
}
