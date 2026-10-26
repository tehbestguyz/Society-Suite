using System.Collections.Generic;
using System.Linq;
using CashRegister;
using Commoners;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CommonersWages
{
    public class CompProperties_Payroll : CompProperties
    {
        public CompProperties_Payroll()
        {
            compClass = typeof(CompPayroll);
        }
    }

    public class CompPayroll : ThingComp
    {
        public bool isPayroll;
        public int target = -1;

        private static readonly int[] TargetPresets = { 100, 250, 500, 1000, 2000, 5000, 10000 };

        public int Target => target < 0 ? WagesMod.Settings.defaultPayrollTarget : target;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }
            yield return new Command_Toggle
            {
                defaultLabel = "CW_PayrollToggle".Translate(),
                defaultDesc = "CW_PayrollToggleDesc".Translate(),
                icon = WagesTextures.Payroll,
                isActive = () => isPayroll,
                toggleAction = () => isPayroll = !isPayroll
            };
            if (!isPayroll)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "CW_PayrollTarget".Translate(Target.ToStringMoney()),
                defaultDesc = "CW_PayrollTargetDesc".Translate(),
                icon = WagesTextures.PayrollTarget,
                action = () =>
                {
                    List<FloatMenuOption> options = TargetPresets
                        .Select(v => new FloatMenuOption(v.ToStringMoney(), () => target = v))
                        .ToList();
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref isPayroll, "cwIsPayroll", false);
            Scribe_Values.Look(ref target, "cwPayrollTarget", -1);
        }
    }

    // Haulers top payroll registers up from the colony's silver.
    public class WorkGiver_StockPayroll : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return WageUtility.PayrollRegisters(pawn.Map).Cast<Thing>();
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !WageUtility.PayrollRegisters(pawn.Map).Any();
        }

        private static int Needed(Building_CashRegister register)
        {
            return register.GetComp<CompPayroll>().Target - WageUtility.SilverIn(register);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_CashRegister register) || !WageUtility.IsPayroll(register) || Needed(register) <= 0)
            {
                return false;
            }
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced))
            {
                return false;
            }
            return FindSilver(pawn) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_CashRegister register))
            {
                return null;
            }
            Thing silver = FindSilver(pawn);
            if (silver == null)
            {
                return null;
            }
            Job job = HaulAIUtility.HaulToContainerJob(pawn, silver, register);
            if (job != null)
            {
                job.count = Mathf.Min(job.count, Needed(register));
            }
            return job;
        }

        private static Thing FindSilver(Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(ThingDefOf.Silver), PathEndMode.ClosestTouch,
                TraverseParms.For(pawn), 9999f, s => !s.IsForbidden(pawn) && pawn.CanReserve(s) && s.IsInAnyStorage());
        }
    }

    // Slotted into the colonist need/work priority sorter: paid colonists go for their pay in free time,
    // or during work hours once it's more than a day late.
    public class JobGiver_CollectWages : ThinkNode_JobGiver
    {
        public override float GetPriority(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Drafted || !WageUtility.EarnsWages(pawn))
            {
                return 0f;
            }
            CompWages comp = WageUtility.WagesComp(pawn);
            if (comp == null || comp.owed <= 0)
            {
                return 0f;
            }
            TimeAssignmentDef assignment = pawn.timetable?.CurrentAssignment ?? TimeAssignmentDefOf.Anything;
            if (assignment == TimeAssignmentDefOf.Joy)
            {
                return 7.5f;
            }
            if (assignment == TimeAssignmentDefOf.Anything)
            {
                return 6.5f;
            }
            if (assignment == TimeAssignmentDefOf.Work && comp.DaysOwed >= 1f)
            {
                return 9.5f;
            }
            return 0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            CompWages comp = WageUtility.WagesComp(pawn);
            if (comp == null || comp.owed <= 0 || !WageUtility.EarnsWages(pawn))
            {
                return null;
            }
            Building_CashRegister register = WageUtility.ClosestPayroll(pawn, r => WageUtility.SilverIn(r) > 0);
            return register == null ? null : JobMaker.MakeJob(CW_DefOf.CW_CollectWages, register);
        }
    }

    public class JobDriver_CollectWages : JobDriver
    {
        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !WageUtility.IsPayroll(Register));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(90).WithProgressBarToilDelay(TargetIndex.A);
            Toil collect = ToilMaker.MakeToil("CollectWages");
            collect.initAction = () => WageUtility.CollectWages(pawn, Register);
            yield return collect;
        }
    }

    public class JobDriver_BuyCitizenship : JobDriver
    {
        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !WageUtility.IsPayroll(Register) || !CasteUtility.IsCommoner(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(180).WithProgressBarToilDelay(TargetIndex.A);
            Toil buy = ToilMaker.MakeToil("BuyCitizenship");
            buy.initAction = () =>
            {
                int price = WagesMod.Settings.citizenshipPrice;
                CompWages comp = WageUtility.WagesComp(pawn);
                if (comp == null)
                {
                    return;
                }
                // They're standing at payroll anyway: settle any wages owed before the ledger closes.
                WageUtility.CollectWages(pawn, Register);
                // Checked again at the register: they may have lost their lot on the way.
                AcceptanceReport home = LeaseUtility.CanLeaveCommoners(pawn);
                if (!home.Accepted)
                {
                    Messages.Message("CW_MessageCantBuyCitizenship".Translate(pawn.Named("PAWN"), home.Reason.Named("REASON")), pawn, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                if (WageUtility.TotalSavings(pawn) < price)
                {
                    return;
                }
                MoneyLog.Note(pawn, MoneyKind.Citizenship, -WageUtility.PaySavings(pawn, Register.GetDirectlyHeldThings(), price));
                CasteUtility.SetCaste(pawn, Caste.Citizen, thoughts: false);
                pawn.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_BoughtCitizenship);
                // Citizens are paid too, so leftover savings stay in their pocket.
                WageUtility.PlayKaching(Register);
                Messages.Message("CW_MessageBoughtCitizenship".Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.PositiveEvent);
            };
            yield return buy;
        }
    }

    public class JobDriver_BuyNobility : JobDriver
    {
        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !WageUtility.IsPayroll(Register) || !WageUtility.EarnsWages(pawn));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(240).WithProgressBarToilDelay(TargetIndex.A);
            Toil buy = ToilMaker.MakeToil("BuyNobility");
            buy.initAction = () =>
            {
                int price = WagesMod.Settings.nobilityPrice;
                CompWages comp = WageUtility.WagesComp(pawn);
                if (comp == null)
                {
                    return;
                }
                WageUtility.CollectWages(pawn, Register);
                // Checked again at the register: the last seat may have gone while they walked over.
                AcceptanceReport seat = CasteUtility.CanChangeCaste(pawn, Caste.Noble, purchased: true);
                if (!seat.Accepted)
                {
                    Messages.Message("CW_MessageNoSeat".Translate(pawn.Named("PAWN"), seat.Reason.Named("REASON")), pawn, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                if (WageUtility.TotalSavings(pawn) < price)
                {
                    return;
                }
                MoneyLog.Note(pawn, MoneyKind.Nobility, -WageUtility.PaySavings(pawn, Register.GetDirectlyHeldThings(), price));
                // Nobles own the colony's wealth: their savings and any unpaid wages go back to it.
                MoneyLog.Note(pawn, MoneyKind.NobilityForfeit, -(WageUtility.TotalSavings(pawn) + comp.owed));
                WageUtility.ReleaseStash(pawn);
                CasteUtility.SetCaste(pawn, Caste.Noble, thoughts: false);
                pawn.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_BoughtNobility);
                comp.purse = 0;
                comp.ReduceOwed(comp.owed);
                pawn.inventory.UnloadEverything = true;
                WageUtility.PlayKaching(Register);
                Messages.Message("CW_MessageBoughtNobility".Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.PositiveEvent);
            };
            yield return buy;
        }
    }
}
