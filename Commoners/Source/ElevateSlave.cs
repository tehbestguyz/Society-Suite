using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Commoners
{
    // Mirrors vanilla WorkGiver_Warden_EmancipateSlave for the "elevate to commoner" slave mode.
    public class WorkGiver_Warden_ElevateSlave : WorkGiver_Warden
    {
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!ModsConfig.IdeologyActive || !(t is Pawn slave))
            {
                return null;
            }
            if (!ShouldTakeCareOfSlave(pawn, slave, forced))
            {
                return null;
            }
            if (slave.guest.slaveInteractionMode != CM_DefOf.CM_ElevateToCommoner || slave.Downed || !slave.Awake())
            {
                return null;
            }
            Job job = JobMaker.MakeJob(CM_DefOf.CM_ElevateSlave, slave);
            job.count = 1;
            return job;
        }
    }

    public class JobDriver_ElevateSlave : JobDriver
    {
        private Pawn Slave => (Pawn)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Slave, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Slave.guest.slaveInteractionMode != CM_DefOf.CM_ElevateToCommoner);
            this.FailOnDowned(TargetIndex.A);
            this.FailOnAggroMentalState(TargetIndex.A);
            this.FailOnForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOn(() => !Slave.IsSlaveOfColony || !Slave.guest.SlaveIsSecure)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            Toil elevate = ToilMaker.MakeToil("ElevateSlave");
            elevate.initAction = () => CasteUtility.ElevateSlave(pawn, Slave);
            yield return elevate;
        }
    }
}
