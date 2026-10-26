using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Commoners
{
    // The military parade ritual (Militarist meme). The speaker gives a speech by the ritual spot, soldiers
    // form up at the "parade start" marker and march slowly to the "parade end" marker, and everyone else
    // watches. The stages, duties and outcome are in Defs/Ritual_MilitaryParade.xml; this file has the pieces
    // vanilla doesn't provide.
    public static class ParadeUtility
    {
        // The start and end markers must be at least this far apart.
        public const int MinRouteLength = 5;

        // Soldiers stand at most this many side by side in a rank.
        public const int RankWidth = 3;

        // How far a soldier's cell may move when the formation cell is blocked.
        private const float FreeCellRadius = 4f;

        // Finds the route for a parade held at this spot: the nearest start and end markers inside the spot's
        // gathering area (the same room indoors, or within 18 cells outdoors). Returns why there's no usable
        // route, or null. Marchers outside the gathering area wouldn't count as attending, so markers outside
        // it are ignored.
        public static string RouteProblem(Map map, IntVec3 spot, out IntVec3 start, out IntVec3 end)
        {
            start = ClosestMarker(map, spot, CM_DefOf.CM_ParadeStart);
            end = ClosestMarker(map, spot, CM_DefOf.CM_ParadeEnd);
            if (!start.IsValid || !end.IsValid)
            {
                return "CM_ParadeNeedsMarkers".Translate();
            }
            if (start.InHorDistOf(end, MinRouteLength))
            {
                return "CM_ParadeRouteTooShort".Translate(MinRouteLength);
            }
            return null;
        }

        private static IntVec3 ClosestMarker(Map map, IntVec3 spot, ThingDef markerDef)
        {
            IntVec3 best = IntVec3.Invalid;
            if (markerDef == null)
            {
                return best;
            }
            int bestDistance = int.MaxValue;
            foreach (Thing marker in map.listerThings.ThingsOfDef(markerDef))
            {
                if (!GatheringsUtility.InGatheringArea(marker.Position, spot, map))
                {
                    continue;
                }
                int distance = (marker.Position - spot).LengthHorizontalSquared;
                if (distance < bestDistance)
                {
                    best = marker.Position;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // The march direction, snapped to one of the eight compass directions so the ranks line up with the
        // grid. A route within about 27 degrees of straight counts as straight.
        public static IntVec3 MarchDirection(IntVec3 start, IntVec3 end)
        {
            int dx = end.x - start.x;
            int dz = end.z - start.z;
            int x = Math.Abs(dx) * 2 >= Math.Abs(dz) ? Math.Sign(dx) : 0;
            int z = Math.Abs(dz) * 2 >= Math.Abs(dx) ? Math.Sign(dz) : 0;
            return new IntVec3(x, 0, z);
        }

        // Cells for a column of soldiers whose front rank is on 'head', facing 'forward'. Each rank holds up
        // to RankWidth soldiers (centre, then left, then right) and stands one step behind the one before it.
        // A blocked cell moves to the nearest free standable cell, so no two soldiers share a spot.
        public static List<IntVec3> FormationCells(Map map, IntVec3 head, IntVec3 forward, int count)
        {
            IntVec3 back = -forward;
            IntVec3 side = new IntVec3(-forward.z, 0, forward.x);
            int width = Math.Max(1, Math.Min(RankWidth, count));
            List<IntVec3> cells = new List<IntVec3>(count);
            HashSet<IntVec3> used = new HashSet<IntVec3>();
            for (int i = 0; i < count; i++)
            {
                int rank = i / width;
                int file = i % width;
                int sideways = (file + 1) / 2 * (file % 2 == 1 ? -1 : 1);
                IntVec3 cell = NearestFreeCell(map, head + back * rank + side * sideways, used);
                used.Add(cell);
                cells.Add(cell);
            }
            return cells;
        }

        private static IntVec3 NearestFreeCell(Map map, IntVec3 cell, HashSet<IntVec3> used)
        {
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(cell, FreeCellRadius, useCenter: true))
            {
                if (candidate.InBounds(map) && candidate.Standable(map) && !candidate.Fogged(map) && !used.Contains(candidate))
                {
                    return candidate;
                }
            }
            return cell;
        }

        // Soldier-caste pawns and shooting or melee specialists: the pawns a parade is for.
        public static bool IsCareerSoldier(Pawn pawn)
        {
            if (CasteUtility.IsSoldier(pawn))
            {
                return true;
            }
            List<string> roleTags = pawn.Ideo?.GetRole(pawn)?.def.roleTags;
            return roleTags != null && (roleTags.Contains("ShootingSpecialist") || roleTags.Contains("MeleeSpecialist"));
        }
    }

    // Checks the route before the parade can start, and draws it while the ritual's button is hovered.
    public class RitualBehaviorWorker_MilitaryParade : RitualBehaviorWorker
    {
        public RitualBehaviorWorker_MilitaryParade()
        {
        }

        public RitualBehaviorWorker_MilitaryParade(RitualBehaviorDef def)
            : base(def)
        {
        }

        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual, Pawn selectedPawn = null, Dictionary<string, Pawn> forcedForRole = null)
        {
            if (target.IsValid && target.Map != null)
            {
                string problem = ParadeUtility.RouteProblem(target.Map, target.Cell, out _, out _);
                if (problem != null)
                {
                    return problem;
                }
            }
            return base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
        }

        public override void DrawPreviewOnTarget(TargetInfo targetInfo)
        {
            base.DrawPreviewOnTarget(targetInfo);
            if (targetInfo.Map == null || ParadeUtility.RouteProblem(targetInfo.Map, targetInfo.Cell, out IntVec3 start, out IntVec3 end) != null)
            {
                return;
            }
            GenDraw.DrawLineBetween(start.ToVector3Shifted(), end.ToVector3Shifted(), SimpleColor.Yellow);
            GenDraw.DrawTargetHighlight(start);
            GenDraw.DrawTargetHighlight(end);
        }
    }

    // Any free colonist who can do violence can march; slaves can't. Vanilla fills a role with every pawn
    // that fits, up to maxCount, which would put the whole colony in the parade. So the dialog starts with
    // just the career soldiers, or the three best fighters if there are none, and the player can add more.
    public class RitualRole_ParadeSoldier : RitualRoleColonist
    {
        private const int DefaultMarchers = 3;

        public override bool AppliesToPawn(Pawn p, out string reason, TargetInfo selectedTarget, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
        {
            if (!base.AppliesToPawn(p, out reason, selectedTarget, ritual, assignments, precept, skipReason))
            {
                return false;
            }
            if (p.IsSlave)
            {
                if (!skipReason)
                {
                    reason = "CM_ParadeSoldierSlave".Translate(p.Named("PAWN"));
                }
                return false;
            }
            if (p.WorkTagIsDisabled(WorkTags.Violent))
            {
                if (!skipReason)
                {
                    reason = "CM_ParadeSoldierNotViolent".Translate(p.Named("PAWN"));
                }
                return false;
            }
            return true;
        }

        protected override int PawnDesirability(Pawn pawn)
        {
            int shooting = pawn.skills?.GetSkill(SkillDefOf.Shooting).Level ?? 0;
            int melee = pawn.skills?.GetSkill(SkillDefOf.Melee).Level ?? 0;
            int score = Math.Max(shooting, melee);
            if (ParadeUtility.IsCareerSoldier(pawn))
            {
                score += 100;
            }
            return score;
        }

        // Only Dialog_BeginRitual's starting fill calls this, so trimming the list only changes who's
        // pre-selected.
        public override void OrderByDesirability(List<Pawn> pawns)
        {
            base.OrderByDesirability(pawns);
            int keep = Math.Max(pawns.Count(ParadeUtility.IsCareerSoldier), DefaultMarchers);
            if (pawns.Count > keep)
            {
                pawns.RemoveRange(keep, pawns.Count - keep);
            }
        }
    }

    // Where a soldier stands: in the column formed up behind the start marker, or halted with its front rank
    // on the end marker (atEnd). Soldiers face the march direction, or the ritual spot if faceSpot is set.
    // Vanilla works out every stage's positions once, when the ritual starts.
    public class RitualPosition_ParadeFormation : RitualPosition
    {
        public bool atEnd;
        public bool faceSpot;

        public override PawnStagePosition GetCell(IntVec3 spot, Pawn p, LordJob_Ritual ritual)
        {
            Map map = ritual.Map;
            RitualRole role = ritual.RoleFor(p, includeForced: true);
            if (role == null || ParadeUtility.RouteProblem(map, spot, out IntVec3 start, out IntVec3 end) != null)
            {
                return new PawnStagePosition(spot, null, Rot4.Invalid, highlight);
            }
            List<Pawn> marchers = ritual.assignments.AssignedPawns(role).ToList();
            int index = marchers.IndexOf(p);
            if (index < 0)
            {
                return new PawnStagePosition(spot, null, Rot4.Invalid, highlight);
            }
            IntVec3 forward = ParadeUtility.MarchDirection(start, end);
            IntVec3 cell = ParadeUtility.FormationCells(map, atEnd ? end : start, forward, marchers.Count)[index];
            Rot4 facing = faceSpot ? Rot4.FromAngleFlat((spot - cell).AngleFlat) : Rot4.FromAngleFlat((end - start).AngleFlat);
            return new PawnStagePosition(cell, null, facing, highlight);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref atEnd, "atEnd", defaultValue: false);
            Scribe_Values.Look(ref faceSpot, "faceSpot", defaultValue: false);
        }
    }

    // Ends a stage once every pawn in the listed roles has the tag. Vanilla's StageEndTrigger_RolesArrived
    // ends it as soon as one pawn per role has it, which is too early for a whole column of soldiers. Pawns
    // that left the ritual or are downed don't hold it up.
    public class StageEndTrigger_AllRolePawnsTagged : StageEndTrigger
    {
        [NoTranslate]
        public List<string> roleIds = new List<string>();

        [NoTranslate]
        public string tag;

        public override Trigger MakeTrigger(LordJob_Ritual ritual, TargetInfo spot, IEnumerable<TargetInfo> foci, RitualStage stage)
        {
            return new Trigger_TickCondition(() => AllTagged(ritual), 30);
        }

        private bool AllTagged(LordJob_Ritual ritual)
        {
            foreach (string roleId in roleIds)
            {
                foreach (Pawn pawn in ritual.assignments.AssignedPawns(roleId))
                {
                    if (ritual.lord.ownedPawns.Contains(pawn) && !pawn.Downed && !ritual.PawnTagSet(pawn, tag))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref roleIds, "roleIds", LookMode.Value);
            Scribe_Values.Look(ref tag, "tag");
        }
    }

    // Parade quality from how many soldiers march. Soldier-caste pawns count casteWeight times each.
    public class RitualOutcomeComp_ParadeSoldiers : RitualOutcomeComp_Quality
    {
        [NoTranslate]
        public string roleId = "soldier";

        public float casteWeight = 2f;

        public override bool DataRequired => false;

        public override float Count(LordJob_Ritual ritual, RitualOutcomeComp_Data data)
        {
            // Only soldiers still in the parade count.
            return Weighted(ritual.assignments.AssignedPawns(roleId).Where(p => ritual.lord?.ownedPawns.Contains(p) ?? true));
        }

        public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget, RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
        {
            float count = Weighted(assignments.AssignedPawns(roleId));
            float quality = curve.Evaluate(count);
            return new QualityFactor
            {
                label = label.CapitalizeFirst(),
                count = count + " / " + MaxValue,
                qualityChange = ExpectedOffsetDesc(positive: true, quality),
                quality = quality,
                positive = true,
                priority = 4f
            };
        }

        private float Weighted(IEnumerable<Pawn> pawns)
        {
            return pawns.Sum(p => CasteUtility.IsSoldier(p) ? casteWeight : 1f);
        }
    }
}
