using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace GuardPosts
{
    public class WorkGiver_Guard : WorkGiver
    {
        // How often a guard re-checks the think tree, so needs, schedules and higher-priority work can take over.
        public const int OverrideCheckInterval = 300;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !pawn.Spawned || pawn.WorkTagIsDisabled(WorkTags.Violent) || !GuardUtility.AllSpots(pawn.Map).Any();
        }

        public override Job NonScanJob(Pawn pawn)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                return null;
            }
            GuardAssignment assignment = comp.GetAssignment(pawn);
            Job job;
            if (assignment.mode == GuardMode.Patrol)
            {
                List<Building_GuardSpot> route = GuardUtility.PatrolRoute(pawn.Map, assignment.color);
                if (!route.Any(s => GuardUtility.CanUseSpot(pawn, s)))
                {
                    return null;
                }
                job = JobMaker.MakeJob(GuardDefOf.GP_Patrol);
                job.locomotionUrgency = LocomotionUrgency.Walk;
            }
            else
            {
                GuardColor? color = assignment.mode == GuardMode.Post ? assignment.color : (GuardColor?)null;
                Building_GuardSpot post = GuardUtility.GuardPosts(pawn.Map, color)
                    .Where(s => !s.IsForbidden(pawn) && pawn.CanReserveAndReach(s, PathEndMode.OnCell, Danger.Deadly))
                    .OrderBy(s => s.Position.DistanceToSquared(pawn.Position))
                    .FirstOrDefault();
                if (post == null)
                {
                    return null;
                }
                job = JobMaker.MakeJob(GuardDefOf.GP_GuardPost, post);
            }
            job.expiryInterval = OverrideCheckInterval;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    public static class GuardCombatUtility
    {
        public const int ScanInterval = 10;

        // Melee guards leave their post to fight hostiles this close to it.
        public const float MeleeEngageRadius = 12f;

        /// <summary>Fights the best nearby hostile. Returns true if the guard started an attack.</summary>
        public static bool TryEngage(JobDriver driver, IntVec3 anchor)
        {
            Pawn pawn = driver.pawn;
            Map map = pawn.Map;
            if (map == null || pawn.Downed || pawn.stances.FullBodyBusy || pawn.IsCarryingPawn()
                || pawn.WorkTagIsDisabled(WorkTags.Violent) || !pawn.kindDef.canMeleeAttack)
            {
                return false;
            }

            for (int i = 0; i < 8; i++)
            {
                IntVec3 cell = pawn.Position + GenAdj.AdjacentCells[i];
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int j = 0; j < things.Count; j++)
                {
                    if (things[j] is Pawn other && IsThreat(pawn, other))
                    {
                        pawn.meleeVerbs.TryMeleeAttack(other);
                        return true;
                    }
                }
            }

            Verb verb = pawn.CurrentEffectiveVerb;
            if (verb != null && !verb.verbProps.IsMeleeAttack)
            {
                TargetScanFlags flags = TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;
                if (verb.IsIncendiary_Ranged())
                {
                    flags |= TargetScanFlags.NeedNonBurning;
                }
                Thing shootTarget = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(pawn, flags);
                if (shootTarget == null)
                {
                    return false;
                }
                return pawn.TryStartAttack(shootTarget);
            }

            Thing meleeTarget = (Thing)AttackTargetFinder.BestAttackTarget(pawn,
                TargetScanFlags.NeedReachable | TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable | TargetScanFlags.NeedLOSToPawns,
                null, 0f, MeleeEngageRadius * 2f, anchor, MeleeEngageRadius);
            if (meleeTarget == null)
            {
                return false;
            }
            // Queue the attack and end the guard job; the work giver sends the guard back once the fight is over.
            Job attack = JobMaker.MakeJob(JobDefOf.AttackMelee, meleeTarget);
            attack.expiryInterval = 600;
            attack.checkOverrideOnExpire = true;
            attack.expireRequiresEnemiesNearby = true;
            pawn.jobs.jobQueue.EnqueueFirst(attack);
            driver.EndJobWith(JobCondition.InterruptForced);
            return true;
        }

        private static bool IsThreat(Pawn pawn, Pawn other)
        {
            return !other.Dead && !other.ThreatDisabled(pawn) && pawn.HostileTo(other)
                && GenHostility.IsActiveThreatTo(other, pawn.Faction, ignoreHives: false);
        }
    }

    public class JobDriver_GuardPost : JobDriver
    {
        private Building_GuardSpot Post => job.GetTarget(TargetIndex.A).Thing as Building_GuardSpot;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        public override string GetReport()
        {
            Building_GuardSpot post = Post;
            if (post == null)
            {
                return base.GetReport();
            }
            return "GP_ReportGuarding".Translate(GuardUtility.ColorLabel(post.SpotColor));
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);

            Toil gotoPost = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            gotoPost.tickIntervalAction = delta =>
            {
                if (pawn.IsHashIntervalTick(GuardCombatUtility.ScanInterval * 3, delta))
                {
                    GuardCombatUtility.TryEngage(this, pawn.Position);
                }
            };
            yield return gotoPost;

            Toil guard = ToilMaker.MakeToil("GuardPost");
            guard.initAction = () =>
            {
                pawn.pather.StopDead();
                Map.pawnDestinationReservationManager.Reserve(pawn, job, pawn.Position);
                GuardCombatUtility.TryEngage(this, Post.Position);
            };
            guard.tickIntervalAction = delta =>
            {
                if (pawn.IsHashIntervalTick(GuardCombatUtility.ScanInterval, delta))
                {
                    GuardCombatUtility.TryEngage(this, Post.Position);
                }
            };
            guard.handlingFacing = true;
            guard.tickAction = () =>
            {
                if (!pawn.stances.FullBodyBusy)
                {
                    pawn.rotationTracker.FaceCell(pawn.Position + Post.Rotation.FacingCell);
                }
            };
            guard.defaultCompleteMode = ToilCompleteMode.Never;
            guard.socialMode = RandomSocialMode.Normal;
            yield return guard;
        }
    }

    public class JobDriver_Patrol : JobDriver
    {
        private const int PauseTicks = 150;
        private const int LookAroundInterval = 50;

        private int stopsVisited;

        private GuardAssignment Assignment => GameComponent_Guards.Instance?.GetAssignment(pawn);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref stopsVisited, "stopsVisited");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        public override string GetReport()
        {
            GuardAssignment assignment = Assignment;
            if (assignment == null || assignment.mode != GuardMode.Patrol)
            {
                return base.GetReport();
            }
            return "GP_ReportPatrolling".Translate(GuardUtility.ColorLabel(assignment.color));
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil pickStop = ToilMaker.MakeToil("PickPatrolStop");
            pickStop.initAction = () =>
            {
                GuardAssignment assignment = Assignment;
                if (assignment == null || assignment.mode != GuardMode.Patrol)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                List<Building_GuardSpot> route = GuardUtility.PatrolRoute(Map, assignment.color);
                if (route.Count == 0)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                if (assignment.patrolIndex < 0 || assignment.patrolIndex >= route.Count)
                {
                    // Join the route at the closest stop.
                    assignment.patrolIndex = route
                        .Select((spot, i) => (spot, i))
                        .OrderBy(x => x.spot.Position.DistanceToSquared(pawn.Position))
                        .First().i;
                }
                // Skip stops outside the pawn's allowed area or out of reach; twice the route covers a back-and-forth pass.
                bool pingPong = GameComponent_Guards.Instance.IsPingPong(assignment.color);
                for (int tries = 0; tries < route.Count * 2; tries++)
                {
                    Building_GuardSpot stop = route[assignment.patrolIndex];
                    if (GuardUtility.CanUseSpot(pawn, stop))
                    {
                        job.SetTarget(TargetIndex.A, stop.Position);
                        return;
                    }
                    GuardUtility.AdvancePatrol(assignment, route.Count, pingPong);
                }
                EndJobWith(JobCondition.Incompletable);
            };
            pickStop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return pickStop;

            Toil walk = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            walk.tickIntervalAction = delta =>
            {
                if (pawn.IsHashIntervalTick(GuardCombatUtility.ScanInterval * 3, delta))
                {
                    GuardCombatUtility.TryEngage(this, pawn.Position);
                }
            };
            yield return walk;

            Toil pause = ToilMaker.MakeToil("PatrolPause");
            pause.initAction = () => pawn.pather.StopDead();
            pause.tickIntervalAction = delta =>
            {
                if (pawn.IsHashIntervalTick(GuardCombatUtility.ScanInterval, delta))
                {
                    GuardCombatUtility.TryEngage(this, pawn.Position);
                }
                if (pawn.IsHashIntervalTick(LookAroundInterval, delta) && !pawn.stances.FullBodyBusy)
                {
                    pawn.rotationTracker.FaceCell(pawn.Position + Rot4.Random.FacingCell);
                }
            };
            pause.handlingFacing = true;
            pause.defaultCompleteMode = ToilCompleteMode.Delay;
            pause.defaultDuration = PauseTicks;
            pause.socialMode = RandomSocialMode.Normal;
            yield return pause;

            Toil advance = ToilMaker.MakeToil("AdvancePatrol");
            advance.initAction = () =>
            {
                GuardAssignment assignment = Assignment;
                if (assignment == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                int routeCount = GuardUtility.PatrolRoute(Map, assignment.color).Count;
                GuardUtility.AdvancePatrol(assignment, routeCount, GameComponent_Guards.Instance.IsPingPong(assignment.color));
                stopsVisited++;
                // Ending now and then lets the work giver re-check the route.
                if (stopsVisited >= System.Math.Max(4, routeCount * 2))
                {
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                JumpToToil(pickStop);
            };
            advance.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return advance;
        }
    }
}
