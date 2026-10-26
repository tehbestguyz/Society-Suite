using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CommonersWages
{
    // A lockbox that links to nearby beds like an end table. Whoever owns a linked bed keeps their savings
    // in it, apart from a little pocket money. It's not storage, so haulers never touch what's inside.
    // How much belongs to whom is tracked on each pawn's CompWages (stashed, stashBox).
    public class Building_Strongbox : Building, IThingHolder
    {
        private ThingOwner<Thing> innerContainer;
        private int rareTicks;

        public Building_Strongbox()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
        }

        public ThingOwner GetDirectlyHeldThings() => innerContainer;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public int SilverHeld => innerContainer.TotalStackCountOfDef(ThingDefOf.Silver);

        public IEnumerable<Pawn> Depositors()
        {
            return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive
                .Where(p => WageUtility.WagesComp(p) is CompWages comp && comp.stashBox == this && comp.stashed > 0);
        }

        // Silver nobody has a claim on (its owner died, left or was ennobled) is put out for the haulers.
        public override void TickRare()
        {
            base.TickRare();
            // Every eighth rare tick (2000 ticks) is plenty for this.
            if (++rareTicks % 8 != 0)
            {
                return;
            }
            int claimed = Depositors().Sum(p => WageUtility.WagesComp(p).stashed);
            int orphaned = SilverHeld - claimed;
            if (orphaned > 0)
            {
                DropSilver(orphaned);
            }
        }

        public void DropSilver(int amount)
        {
            if (!Spawned)
            {
                return;
            }
            int left = amount;
            foreach (Thing silver in innerContainer.Where(t => t.def == ThingDefOf.Silver).ToList())
            {
                if (left <= 0)
                {
                    break;
                }
                int count = Mathf.Min(left, silver.stackCount);
                if (innerContainer.TryDrop(silver, InteractionCell, Map, ThingPlaceMode.Near, count, out Thing _))
                {
                    left -= count;
                }
            }
        }

        // Removing the box (deconstructed, destroyed, uninstalled) hands its silver to the colony, and the
        // owners are owed the same amount from payroll instead, so nobody loses their savings.
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            foreach (Pawn owner in Depositors().ToList())
            {
                CompWages comp = WageUtility.WagesComp(owner);
                comp.AddOwed(comp.stashed, capped: false);
                MoneyLog.Note(owner, MoneyKind.StrongboxReturned, comp.stashed);
                comp.stashed = 0;
                comp.stashBox = null;
            }
            if (map != null && innerContainer.Count > 0)
            {
                innerContainer.TryDropAll(Position, map, ThingPlaceMode.Near);
                Messages.Message("CW_MessageStrongboxRemoved".Translate(), new TargetInfo(Position, map), MessageTypeDefOf.NeutralEvent, historical: false);
            }
            base.DeSpawn(mode);
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            List<string> lines = Depositors().Select(p => p.LabelShort + ": " + WageUtility.WagesComp(p).stashed.ToStringMoney()).ToList();
            string contents = lines.Count == 0
                ? "CW_StrongboxEmpty".Translate().ToString()
                : "CW_StrongboxContents".Translate(SilverHeld.ToStringMoney(), string.Join(", ", lines)).ToString();
            return text.NullOrEmpty() ? contents : text + "\n" + contents;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        }
    }

    // Free time only: deposit pocket savings above the pocket-money float into the bed's strongbox,
    // or top the pocket back up from it when it runs low.
    public class JobGiver_TendSavings : ThinkNode_JobGiver
    {
        public override float GetPriority(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Drafted || !WageUtility.KeepsSavings(pawn))
            {
                return 0f;
            }
            TimeAssignmentDef assignment = pawn.timetable?.CurrentAssignment ?? TimeAssignmentDefOf.Anything;
            if (assignment == TimeAssignmentDefOf.Joy)
            {
                return 7.4f;
            }
            if (assignment == TimeAssignmentDefOf.Anything)
            {
                return 6.4f;
            }
            return 0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            CompWages comp = WageUtility.WagesComp(pawn);
            Building_Strongbox box = WageUtility.LinkedStrongbox(pawn);
            if (comp == null || box == null || box.IsForbidden(pawn) || !pawn.CanReach(box, PathEndMode.Touch, Danger.Some))
            {
                return null;
            }
            int pocketMoney = WagesMod.Settings.pocketMoney;
            int pocket = WageUtility.ProtectedSilver(pawn);
            // Savings still in the box by a bed they no longer own get moved over on the next deposit.
            bool misplaced = WageUtility.Stash(pawn) > 0 && comp.stashBox != box;
            // A little slack either way, so pawns don't run back and forth over a few coins.
            if (pocket >= pocketMoney + 20 || misplaced)
            {
                return JobMaker.MakeJob(CW_DefOf.CW_DepositSavings, box);
            }
            if (pocket < pocketMoney / 2 && WageUtility.Stash(pawn) > 0)
            {
                return JobMaker.MakeJob(CW_DefOf.CW_WithdrawSavings, box);
            }
            return null;
        }
    }

    public class JobDriver_TendSavings : JobDriver
    {
        private Building_Strongbox Box => (Building_Strongbox)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(60).WithProgressBarToilDelay(TargetIndex.A);
            Toil tend = ToilMaker.MakeToil("TendSavings");
            tend.initAction = () =>
            {
                if (job.def == CW_DefOf.CW_DepositSavings)
                {
                    WageUtility.Deposit(pawn, Box);
                }
                else
                {
                    WageUtility.Withdraw(pawn, Box);
                }
            };
            yield return tend;
        }
    }
}
