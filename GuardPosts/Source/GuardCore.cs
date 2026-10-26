using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace GuardPosts
{
    public enum GuardColor { Red, Blue, Green, Yellow }

    public enum GuardSpotKind { Guard, Patrol }

    public enum GuardMode { AnyPost, Post, Patrol }

    public enum DutyMode { OnDuty, OffDuty, Auto }

    [DefOf]
    public static class GuardDefOf
    {
        public static WorkTypeDef GP_Guarding;
        public static JobDef GP_GuardPost;
        public static JobDef GP_Patrol;
        public static PawnTableDef GP_Guards;
        public static PawnColumnDef GP_CombatSkill;
        public static PawnColumnDef GP_OffDutyLoadout;
        public static PawnColumnDef GP_OnDutyLoadout;
        public static PawnColumnDef GP_OffDutyApparel;
        public static PawnColumnDef GP_OnDutyApparel;
        public static PawnColumnDef GP_MilitiaApparel;
        public static PawnColumnDef GP_MilitiaLoadout;
        public static PawnTableDef GP_Militia;
        public static PawnTableDef GP_Shifts;

        static GuardDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(GuardDefOf));
        }
    }

    public class GuardSpotExtension : DefModExtension
    {
        public GuardSpotKind kind;
        public GuardColor color;
    }

    public class GuardAssignment : IExposable
    {
        public GuardMode mode = GuardMode.AnyPost;
        public GuardColor color = GuardColor.Red;

        // Patrol progress, kept here so a new patrol job picks up where the last one stopped.
        public int patrolIndex = -1;
        public int patrolDirection = 1;

        // Apparel policies for each duty (null = keep current).
        public ApparelPolicy onDutyApparel;
        public ApparelPolicy offDutyApparel;

        // CE loadout IDs for each duty (-1 = keep current).
        public int onDutyLoadout = -1;
        public int offDutyLoadout = -1;

        // Saves from before on-duty gear was chosen remembered the duty gear while off duty instead.
        private ApparelPolicy legacyDutyApparel;
        private int legacyDutyLoadout = -1;

        // Whether this guard is currently switched to off-duty gear.
        public bool offDuty;

        // Militia: membership and gear (null / -1 = keep current).
        public bool militia;
        public ApparelPolicy militiaApparel;
        public int militiaLoadout = -1;

        // While called up: the gear to restore on stand-down, and whether they're still gearing up before being drafted.
        public bool militiaCalled;
        public bool mustering;
        public bool musterGathering;
        public int musterStartTick;
        public ApparelPolicy preMilitiaApparel;
        public int preMilitiaLoadout = -1;
        public List<int> militiaForcedBefore;

        // The gear job the muster gave last, so it isn't interrupted. Not saved: after loading, the muster just gives the next one.
        public Job musterJob;

        // Guard muster: on duty whatever the schedule, gearing up first. The gear job isn't saved, like musterJob.
        public bool guardMustered;
        public bool guardGearing;
        public bool guardGathering;
        public int guardGearStartTick;
        public List<int> guardForcedBefore;

        // Guard shifts: bit n set = on shift during hour n. onShift and the gear-up state are tracked by GuardShifts.Tick.
        public int shiftMask;
        public bool onShift;
        public bool shiftGearing;
        public bool shiftGathering;
        public int shiftStartTick;
        public Job shiftJob;
        public Job guardMusterJob;

        public string Label
        {
            get
            {
                switch (mode)
                {
                    case GuardMode.Post:
                        return "GP_AssignPost".Translate(GuardUtility.ColorLabel(color));
                    case GuardMode.Patrol:
                        return "GP_AssignPatrol".Translate(GuardUtility.ColorLabel(color));
                    default:
                        return "GP_AssignAnyPost".Translate();
                }
            }
        }

        public void Set(GuardMode newMode, GuardColor newColor)
        {
            if (newMode != mode || newColor != color)
            {
                patrolIndex = -1;
                patrolDirection = 1;
            }
            mode = newMode;
            color = newColor;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", GuardMode.AnyPost);
            Scribe_Values.Look(ref color, "color", GuardColor.Red);
            Scribe_Values.Look(ref patrolIndex, "patrolIndex", -1);
            Scribe_Values.Look(ref patrolDirection, "patrolDirection", 1);
            Scribe_Values.Look(ref onDutyLoadout, "onDutyLoadout", -1);
            Scribe_Values.Look(ref offDutyLoadout, "offDutyLoadout", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref legacyDutyLoadout, "dutyLoadout", -1);
            }
            Scribe_Values.Look(ref offDuty, "offDuty", false);
            Scribe_Values.Look(ref militia, "militia", false);
            Scribe_Values.Look(ref militiaLoadout, "militiaLoadout", -1);
            Scribe_Values.Look(ref militiaCalled, "militiaCalled", false);
            Scribe_Values.Look(ref mustering, "mustering", false);
            Scribe_Values.Look(ref musterGathering, "musterGathering", false);
            Scribe_Values.Look(ref musterStartTick, "musterStartTick", 0);
            Scribe_Values.Look(ref preMilitiaLoadout, "preMilitiaLoadout", -1);
            Scribe_Values.Look(ref guardMustered, "guardMustered", false);
            Scribe_Values.Look(ref guardGearing, "guardGearing", false);
            Scribe_Values.Look(ref guardGathering, "guardGathering", false);
            Scribe_Values.Look(ref guardGearStartTick, "guardGearStartTick", 0);
            Scribe_Values.Look(ref shiftMask, "shiftMask", 0);
            Scribe_Values.Look(ref onShift, "onShift", false);
            Scribe_Values.Look(ref shiftGearing, "shiftGearing", false);
            Scribe_Values.Look(ref shiftGathering, "shiftGathering", false);
            Scribe_Values.Look(ref shiftStartTick, "shiftStartTick", 0);
            Scribe_Collections.Look(ref militiaForcedBefore, "militiaForcedBefore", LookMode.Value);
            Scribe_Collections.Look(ref guardForcedBefore, "guardForcedBefore", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // A deleted policy would save as an unresolvable reference.
                if (!DutyUtility.PolicyExists(onDutyApparel))
                {
                    onDutyApparel = null;
                }
                if (!DutyUtility.PolicyExists(offDutyApparel))
                {
                    offDutyApparel = null;
                }
                if (!DutyUtility.PolicyExists(militiaApparel))
                {
                    militiaApparel = null;
                }
                if (!DutyUtility.PolicyExists(preMilitiaApparel))
                {
                    preMilitiaApparel = null;
                }
            }
            Scribe_References.Look(ref onDutyApparel, "onDutyApparel");
            Scribe_References.Look(ref offDutyApparel, "offDutyApparel");
            Scribe_References.Look(ref militiaApparel, "militiaApparel");
            Scribe_References.Look(ref preMilitiaApparel, "preMilitiaApparel");
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Scribe_References.Look(ref legacyDutyApparel, "dutyApparel");
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (legacyDutyLoadout >= 0 || legacyDutyApparel != null)
                {
                    // Duty gear is only remembered while off duty (saves from before per-guard duty didn't store the flag).
                    offDuty = true;
                }
                if (onDutyApparel == null)
                {
                    onDutyApparel = legacyDutyApparel;
                }
                if (onDutyLoadout < 0)
                {
                    onDutyLoadout = legacyDutyLoadout;
                }
                legacyDutyApparel = null;
                legacyDutyLoadout = -1;
            }
        }
    }

    public class GameComponent_Guards : GameComponent
    {
        public bool spotsVisible = true;
        public bool onlyShowGuards = true;
        public DutyMode dutyMode = DutyMode.OnDuty;
        public bool militiaActive;
        public bool guardsMustered;

        private Dictionary<Pawn, GuardAssignment> assignments = new Dictionary<Pawn, GuardAssignment>();
        private List<GuardColor> pingPongRoutes = new List<GuardColor>();

        private List<Pawn> tmpPawns;
        private List<GuardAssignment> tmpAssignments;

        public static GameComponent_Guards Instance => Current.Game?.GetComponent<GameComponent_Guards>();

        public GameComponent_Guards(Game game)
        {
        }

        public GuardAssignment GetAssignment(Pawn pawn)
        {
            if (!assignments.TryGetValue(pawn, out GuardAssignment assignment))
            {
                assignment = new GuardAssignment();
                assignments[pawn] = assignment;
            }
            return assignment;
        }

        public IEnumerable<KeyValuePair<Pawn, GuardAssignment>> AllAssignments => assignments;

        public override void GameComponentTick()
        {
            // Auto duty follows each guard's schedule; this also switches guards added since the mode was set.
            if (Find.TickManager.TicksGame % 250 == 0)
            {
                DutyUtility.UpdateAll(announce: false);
            }
            if (militiaActive && Find.TickManager.TicksGame % 30 == 0)
            {
                MilitiaUtility.MusterTick();
            }
            if (guardsMustered && Find.TickManager.TicksGame % 30 == 15)
            {
                GuardMuster.Tick();
            }
            if (Find.TickManager.TicksGame % 30 == 7)
            {
                GuardShifts.Tick();
            }
        }

        public bool IsPingPong(GuardColor color) => pingPongRoutes.Contains(color);

        public void SetPingPong(GuardColor color, bool pingPong)
        {
            if (pingPong && !pingPongRoutes.Contains(color))
            {
                pingPongRoutes.Add(color);
            }
            else if (!pingPong)
            {
                pingPongRoutes.Remove(color);
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref spotsVisible, "spotsVisible", true);
            Scribe_Values.Look(ref onlyShowGuards, "onlyShowGuards", true);
            Scribe_Values.Look(ref dutyMode, "dutyMode", DutyMode.OnDuty);
            Scribe_Values.Look(ref militiaActive, "militiaActive", false);
            Scribe_Values.Look(ref guardsMustered, "guardsMustered", false);
            Scribe_Collections.Look(ref pingPongRoutes, "pingPongRoutes", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                foreach (Pawn pawn in assignments.Keys.Where(p => p == null || p.Discarded).ToList())
                {
                    assignments.Remove(pawn);
                }
            }
            Scribe_Collections.Look(ref assignments, "assignments", LookMode.Reference, LookMode.Deep, ref tmpPawns, ref tmpAssignments);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pingPongRoutes ??= new List<GuardColor>();
                // Scribe already skips null (unresolved) pawn keys; Dictionary.Remove(null) would throw.
                assignments ??= new Dictionary<Pawn, GuardAssignment>();
            }
        }
    }

    public static class GuardUtility
    {
        public static readonly GuardColor[] AllColors = { GuardColor.Red, GuardColor.Blue, GuardColor.Green, GuardColor.Yellow };

        public static Color ColorOf(GuardColor color)
        {
            switch (color)
            {
                case GuardColor.Red: return new Color(0.92f, 0.25f, 0.22f);
                case GuardColor.Blue: return new Color(0.25f, 0.50f, 0.95f);
                case GuardColor.Green: return new Color(0.30f, 0.80f, 0.30f);
                default: return new Color(0.96f, 0.84f, 0.22f);
            }
        }

        public static SimpleColor SimpleColorOf(GuardColor color)
        {
            switch (color)
            {
                case GuardColor.Red: return SimpleColor.Red;
                case GuardColor.Blue: return SimpleColor.Blue;
                case GuardColor.Green: return SimpleColor.Green;
                default: return SimpleColor.Yellow;
            }
        }

        public static string ColorLabel(GuardColor color) => ("GP_Color_" + color).Translate();

        public static bool IsGuard(Pawn pawn) =>
            pawn.workSettings != null && pawn.workSettings.EverWork && pawn.workSettings.WorkIsActive(GuardDefOf.GP_Guarding);

        public static IEnumerable<Building_GuardSpot> AllSpots(Map map) =>
            map.listerBuildings.AllBuildingsColonistOfClass<Building_GuardSpot>();

        public static IEnumerable<Building_GuardSpot> GuardPosts(Map map, GuardColor? color) =>
            AllSpots(map).Where(s => s.Kind == GuardSpotKind.Guard && (color == null || s.SpotColor == color));

        public static List<Building_GuardSpot> PatrolRoute(Map map, GuardColor color) =>
            AllSpots(map)
                .Where(s => s.Kind == GuardSpotKind.Patrol && s.SpotColor == color)
                .OrderBy(s => s.patrolOrder)
                .ThenBy(s => s.thingIDNumber)
                .ToList();

        /// <summary>
        /// Spots outside the pawn's allowed area are skipped: vanilla's SeekAllowedArea would pull the guard
        /// back inside at every override check, so they'd shuttle between the spot and the area.
        /// </summary>
        public static bool CanUseSpot(Pawn pawn, Building_GuardSpot spot) =>
            !spot.IsForbidden(pawn) && pawn.CanReach(spot, PathEndMode.OnCell, Danger.Deadly);

        public static bool ShouldDrawSpot(Building spot)
        {
            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null || comp.spotsVisible)
            {
                return true;
            }
            if (Find.Selector.IsSelected(spot))
            {
                return true;
            }
            // Show spots while the player is placing new ones.
            return Find.DesignatorManager.SelectedDesignator is Designator_Build build
                && build.PlacingDef is ThingDef placing
                && (placing.HasModExtension<GuardSpotExtension>() || typeof(Building_MusterSpot).IsAssignableFrom(placing.thingClass));
        }

        /// <summary>Patrol step after the current one, honoring loop or back-and-forth routes.</summary>
        public static void AdvancePatrol(GuardAssignment assignment, int routeCount, bool pingPong)
        {
            if (routeCount <= 1)
            {
                assignment.patrolIndex = 0;
                return;
            }
            if (!pingPong)
            {
                assignment.patrolDirection = 1;
                assignment.patrolIndex = (assignment.patrolIndex + 1) % routeCount;
                return;
            }
            int next = assignment.patrolIndex + assignment.patrolDirection;
            if (next < 0 || next >= routeCount)
            {
                assignment.patrolDirection = -assignment.patrolDirection;
                next = assignment.patrolIndex + assignment.patrolDirection;
            }
            assignment.patrolIndex = Mathf.Clamp(next, 0, routeCount - 1);
        }
    }

    [StaticConstructorOnStartup]
    public static class GuardStartup
    {
        static GuardStartup()
        {
            // Work priority columns are generated defs, so they can't be referenced from XML.
            PawnColumnDef priorityColumn = DefDatabase<PawnColumnDef>.GetNamedSilentFail("WorkPriority_" + GuardDefOf.GP_Guarding.defName);
            List<PawnColumnDef> columns = GuardDefOf.GP_Guards.columns;
            if (priorityColumn != null && !columns.Contains(priorityColumn))
            {
                columns.Insert(1, priorityColumn);
            }

            // With Combat Extended, add the on- and off-duty loadout choices. Only CE's loadout column def is looked up
            // (by name, to tell that CE is loaded), so there's no reference to CE's assembly.
            PawnColumnDef ceLoadoutColumn = DefDatabase<PawnColumnDef>.GetNamedSilentFail("Loadout");
            if (ceLoadoutColumn?.workerClass?.FullName == "CombatExtended.PawnColumnWorker_Loadout" && !columns.Contains(GuardDefOf.GP_OnDutyLoadout))
            {
                int afterIndex = columns.IndexOf(GuardDefOf.GP_OffDutyApparel);
                int index = afterIndex >= 0 ? afterIndex + 1 : columns.Count - 1;
                PawnColumnDef gap = DefDatabase<PawnColumnDef>.GetNamedSilentFail("GapTiny");
                columns.InsertRange(index, gap != null
                    ? new[] { gap, GuardDefOf.GP_OnDutyLoadout, gap, GuardDefOf.GP_OffDutyLoadout }
                    : new[] { GuardDefOf.GP_OnDutyLoadout, GuardDefOf.GP_OffDutyLoadout });

                // The militia table gets its own loadout choice the same way.
                List<PawnColumnDef> militiaColumns = GuardDefOf.GP_Militia.columns;
                int militiaIndex = militiaColumns.IndexOf(GuardDefOf.GP_MilitiaApparel);
                militiaIndex = militiaIndex >= 0 ? militiaIndex + 1 : militiaColumns.Count - 1;
                militiaColumns.InsertRange(militiaIndex, gap != null
                    ? new[] { gap, GuardDefOf.GP_MilitiaLoadout }
                    : new[] { GuardDefOf.GP_MilitiaLoadout });
            }
        }
    }

    [StaticConstructorOnStartup]
    public static class GuardTextures
    {
        public static readonly Texture2D ToggleVisibility = ContentFinder<Texture2D>.Get("GuardPosts/ToggleVisibility");
        public static readonly Texture2D RouteLoop = ContentFinder<Texture2D>.Get("GuardPosts/RouteLoop");
        public static readonly Texture2D RoutePingPong = ContentFinder<Texture2D>.Get("GuardPosts/RoutePingPong");
        public static readonly Texture2D GuardSpot = ContentFinder<Texture2D>.Get("GuardPosts/GuardSpot");
        public static readonly Texture2D PatrolSpot = ContentFinder<Texture2D>.Get("GuardPosts/PatrolSpot");
    }
}
