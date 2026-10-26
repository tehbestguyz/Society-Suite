using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace GuardPosts
{
    // Where called-up militia and mustered guards gather once they have their gear, before they're drafted.
    public class Building_MusterSpot : Building
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (GuardUtility.ShouldDrawSpot(this))
            {
                base.DrawAt(drawLoc, flip);
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            GenDraw.DrawRadiusRing(Position, MusterSteps.GatherRadius);
        }

    }

    // The steps a mustering pawn goes through: gear up, then gather at the nearest muster spot.
    // Each step is a job that doesn't expire, followed by a short queued wait, so the think tree (schedule, recreation, bed)
    // never gets a turn between steps. The muster ticks hand out the next step when the wait starts.
    public static class MusterSteps
    {
        public const float GatherRadius = 4.9f;

        // After three hours of gearing up, or an hour of gathering, the pawn moves on with what they have.
        // A step already under way always gets to finish; the time limits only stop new steps being handed out.
        public const int GearTimeout = 7500;
        public const int GatherTimeout = 2500;

        private const int WaitTicks = 180;

        public static Building_MusterSpot NearestSpot(Pawn pawn) =>
            pawn.Map?.listerBuildings.AllBuildingsColonistOfClass<Building_MusterSpot>()
                .Where(s => !s.IsForbidden(pawn) && pawn.CanReach(s, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(s => s.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();

        public static bool AnySpot(Map map) => map != null && map.listerBuildings.AllBuildingsColonistOfClass<Building_MusterSpot>().Any();

        // Moves the pawn on to its next step. Returns true when it's geared up and gathered (or timed out).
        public static bool Step(Pawn pawn, ref bool gathering, ref int stepStartTick, ref Job current, bool gather)
        {
            int now = Find.TickManager.TicksGame;
            if (current != null && pawn.CurJob == current)
            {
                return false;
            }
            bool timedOut = now - stepStartTick > (gathering ? GatherTimeout : GearTimeout);
            if (!gathering)
            {
                Job gearJob = timedOut ? null : MilitiaUtility.NextGearJob(pawn);
                if (gearJob != null)
                {
                    // Not a player order: a player-ordered Wear job would lock the apparel on as forced.
                    Start(pawn, gearJob, ref current, playerOrder: false);
                    return false;
                }
                gathering = true;
                stepStartTick = now;
                timedOut = false;
            }
            if (!gather || timedOut)
            {
                return Finish(pawn, ref current);
            }
            Building_MusterSpot spot = NearestSpot(pawn);
            if (spot == null || pawn.Position.InHorDistOf(spot.Position, GatherRadius))
            {
                return Finish(pawn, ref current);
            }
            if (!CellFinder.TryFindRandomCellNear(spot.Position, spot.Map, Mathf.FloorToInt(GatherRadius),
                    c => c.InHorDistOf(spot.Position, GatherRadius) && c.Standable(spot.Map) && !c.IsForbidden(pawn)
                        && c.GetFirstPawn(spot.Map) == null && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly),
                    out IntVec3 cell))
            {
                cell = spot.Position;
            }
            Job gotoJob = JobMaker.MakeJob(JobDefOf.Goto, cell);
            gotoJob.locomotionUrgency = LocomotionUrgency.Jog;
            Start(pawn, gotoJob, ref current, playerOrder: true);
            return false;
        }

        private static bool Finish(Pawn pawn, ref Job current)
        {
            current = null;
            ClearWait(pawn);
            return true;
        }

        // The job never expires, so the think tree's override checks (schedule, recreation, bed) can't replace it.
        private static void Start(Pawn pawn, Job job, ref Job current, bool playerOrder)
        {
            job.expiryInterval = -1;
            job.checkOverrideOnExpire = false;
            job.playerForced = playerOrder;
            current = job;
            pawn.jobs.ClearQueuedJobs();
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            Job wait = JobMaker.MakeJob(JobDefOf.Wait);
            wait.expiryInterval = WaitTicks;
            wait.checkOverrideOnExpire = false;
            wait.playerForced = true;
            pawn.jobs.jobQueue.EnqueueLast(wait);
        }

        // Ends the muster's job and queued wait when a muster is over, so the pawn picks something new.
        public static void Abandon(Pawn pawn, Job current)
        {
            if (pawn == null || !pawn.Spawned || pawn.jobs == null)
            {
                return;
            }
            ClearWait(pawn);
            if (current != null && pawn.CurJob == current)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            else if (pawn.CurJobDef == JobDefOf.Wait && pawn.CurJob.playerForced && !pawn.Drafted)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        // Apparel the pawn had forced before the muster, so standing down only unforces what the muster forced.
        public static List<int> ForcedApparelIds(Pawn pawn) =>
            pawn.outfits?.forcedHandler?.ForcedApparel.Select(a => a.thingIDNumber).ToList() ?? new List<int>();

        // Unforces apparel forced since the muster began (older builds forced everything they put on), and has the pawn
        // re-check their apparel now instead of in a few hours, so they change out of muster-only gear.
        // forcedBefore is null for musters started by older builds: then all forced apparel is unforced.
        public static void UnforceMusterApparel(Pawn pawn, List<int> forcedBefore)
        {
            OutfitForcedHandler forced = pawn?.outfits?.forcedHandler;
            if (forced == null)
            {
                return;
            }
            foreach (Apparel apparel in forced.ForcedApparel.ToList())
            {
                if (forcedBefore == null || !forcedBefore.Contains(apparel.thingIDNumber))
                {
                    forced.SetForced(apparel, false);
                }
            }
            if (pawn.mindState != null)
            {
                pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame;
            }
        }

        private static void ClearWait(Pawn pawn)
        {
            pawn.jobs?.jobQueue?.RemoveAll(pawn, j => j.def == JobDefOf.Wait && j.playerForced);
        }
    }
}
