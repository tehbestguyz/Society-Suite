using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Commoners
{
    // Where landed-title prestige comes from (see DESIGN.md "Landed titles"). Everything goes through
    // LandedTitles.AddPrestige, which ignores anyone who isn't a colony noble and posts a message with the
    // reason. Patronage (spending savings) lives in the Wages add-on.
    public static class PrestigeSources
    {
        // The same kind of ritual gives a noble prestige at most once per this many days.
        public const float RitualCooldownDays = 5f;

        // Hosting happy visitors gives the host prestige at most once per this many days.
        public const float HostCooldownDays = 3f;

        // Ritual roles that count as leading the ritual: speakers, the moral guide at a funeral, the judge
        // at a trial, the investor at an investiture.
        private static readonly HashSet<string> LeadingRoles = new HashSet<string> { "speaker", "leader", "moralist", "investor", "organizer" };

        private static HashSet<MentalStateDef> extremeBreakStates;

        public static Hediff_Noble NobleData(Pawn p) => LandedTitles.Data(p);

        // ===== Rituals =====

        // A finished ritual's leaders gain prestige by its outcome: worst −1, poor 0, good +1, best +2.
        public static void OnRitualOutcome(LordJob_Ritual ritual, RitualOutcomePossibility outcome, Pawn organizer)
        {
            if (ritual?.Ritual == null || outcome == null || ritual.assignments == null)
            {
                return;
            }
            int amount = outcome.positivityIndex <= -2 ? -1 : outcome.positivityIndex >= 2 ? 2 : outcome.positivityIndex > 0 ? 1 : 0;
            if (amount == 0)
            {
                return;
            }
            HashSet<Pawn> leaders = new HashSet<Pawn>();
            if (organizer != null)
            {
                leaders.Add(organizer);
            }
            foreach (RitualRole role in ritual.assignments.AllRolesForReading)
            {
                if (LeadingRoles.Contains(role.id))
                {
                    leaders.AddRange(ritual.assignments.AssignedPawns(role));
                }
            }
            string ritualKey = ritual.Ritual.def.defName;
            int now = Find.TickManager.TicksGame;
            foreach (Pawn leader in leaders)
            {
                Hediff_Noble data = NobleData(leader);
                if (data == null)
                {
                    continue;
                }
                if (data.ritualPrestigeTicks.TryGetValue(ritualKey, out int last) && now - last < RitualCooldownDays * GenDate.TicksPerDay)
                {
                    continue;
                }
                data.ritualPrestigeTicks[ritualKey] = now;
                LandedTitles.AddPrestige(leader, amount, "CM_PrestigeRitual".Translate(outcome.label.ToLower(), ritual.Ritual.Label));
            }
        }

        // ===== Losses =====

        public static bool IsExtremeBreak(MentalStateDef state)
        {
            if (extremeBreakStates == null)
            {
                extremeBreakStates = new HashSet<MentalStateDef>(DefDatabase<MentalBreakDef>.AllDefs
                    .Where(b => b.intensity == MentalBreakIntensity.Extreme && b.mentalState != null)
                    .Select(b => b.mentalState));
            }
            return extremeBreakStates.Contains(state);
        }

        // ===== Diplomacy =====

        // A successful quest gives +1 to colony nobles who took part: the pawn who accepted it, pawns the
        // quest itself points at, and nobles at one of its sites (on its map, or in a caravan on its tile).
        public static void OnQuestSucceeded(Quest quest)
        {
            List<GlobalTargetInfo> targets = quest.QuestLookTargets.ToList();
            HashSet<Pawn> credited = new HashSet<Pawn>();
            if (quest.AccepterPawn != null)
            {
                credited.Add(quest.AccepterPawn);
            }
            foreach (GlobalTargetInfo target in targets)
            {
                if (target.Thing is Pawn pawn)
                {
                    credited.Add(pawn);
                }
            }
            List<WorldObject> sites = targets.Where(t => t.WorldObject != null).Select(t => t.WorldObject).ToList();
            if (sites.Count > 0)
            {
                foreach (Pawn noble in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
                {
                    if (NobleData(noble) == null)
                    {
                        continue;
                    }
                    MapParent parent = noble.MapHeld?.Parent;
                    Caravan caravan = noble.GetCaravan();
                    if ((parent != null && sites.Contains(parent)) || (caravan != null && sites.Any(s => s.Tile == caravan.Tile)))
                    {
                        credited.Add(noble);
                    }
                }
            }
            foreach (Pawn pawn in credited)
            {
                LandedTitles.AddPrestige(pawn, 1, "CM_PrestigeQuest".Translate(quest.name));
            }
        }

        // Visitors who leave with good mood and nobody hurt give +1 to their host: the highest-titled
        // colony noble on the map.
        public static void OnVisitorsLeaving(Lord lord, Map map)
        {
            if (lord == null || map == null || lord.numPawnsLostViolently > 0 || lord.faction == null || lord.faction.IsPlayer)
            {
                return;
            }
            List<float> moods = lord.ownedPawns.Where(p => p.needs?.mood != null).Select(p => p.needs.mood.CurLevelPercentage).ToList();
            if (moods.Count == 0 || moods.Average() < 0.5f)
            {
                return;
            }
            Pawn host = null;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                LandedTitleDef title = LandedTitles.Title(p);
                if (title != null && (host == null || title.prestige > LandedTitles.Title(host).prestige))
                {
                    host = p;
                }
            }
            Hediff_Noble data = NobleData(host);
            int now = Find.TickManager.TicksGame;
            if (data == null || now - data.lastHostTick < HostCooldownDays * GenDate.TicksPerDay)
            {
                return;
            }
            data.lastHostTick = now;
            LandedTitles.AddPrestige(host, 1, "CM_PrestigeHosted".Translate(lord.faction.Name));
        }
    }

    // ===== Defence: one battle per map =====

    // A battle starts the first time a colonist hurts a hostile pawn, or when a hostile threat appears,
    // and ends once no hostile threat is left on the map. If a hostile faction was involved, nobles who
    // fought get +1, and +1 more if soldier-caste pawns fought too. If colonists died, nobles who could
    // fight but didn't lose 1. The threat check runs every 250 ticks.
    public class MapComponent_RealmDefence : MapComponent
    {
        private const int CheckInterval = 250;

        private bool active;
        private bool factionInvolved;
        private bool soldiersFought;
        private int colonistsDied;
        private HashSet<Pawn> nobleFighters = new HashSet<Pawn>();

        public MapComponent_RealmDefence(Map map) : base(map)
        {
        }

        public static MapComponent_RealmDefence For(Map map) => map?.GetComponent<MapComponent_RealmDefence>();

        public override void MapComponentTick()
        {
            if (!LandedTitles.Active || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }
            bool threat = GenHostility.AnyHostileActiveThreatToPlayer(map);
            if (threat && !active)
            {
                Begin();
            }
            else if (!threat && active)
            {
                End();
            }
        }

        private void Begin()
        {
            active = true;
            factionInvolved = false;
            soldiersFought = false;
            colonistsDied = 0;
            nobleFighters.Clear();
        }

        public void NoteHit(Pawn attacker, Pawn victim)
        {
            if (!active)
            {
                Begin();
            }
            if (victim.Faction != null && victim.Faction.HostileTo(Faction.OfPlayer))
            {
                factionInvolved = true;
            }
            if (CasteUtility.IsSoldier(attacker))
            {
                soldiersFought = true;
            }
            if (LandedTitles.Data(attacker) != null)
            {
                nobleFighters.Add(attacker);
            }
        }

        public void NoteColonistDied()
        {
            if (active)
            {
                colonistsDied++;
            }
        }

        private void End()
        {
            active = false;
            if (!factionInvolved)
            {
                return;
            }
            foreach (Pawn noble in nobleFighters)
            {
                if (noble.Dead)
                {
                    continue;
                }
                LandedTitles.AddPrestige(noble, soldiersFought ? 2 : 1,
                    (soldiersFought ? "CM_PrestigeDefendedWithSoldiers" : "CM_PrestigeDefended").Translate());
            }
            if (colonistsDied > 0)
            {
                foreach (Pawn noble in map.mapPawns.FreeColonistsSpawned.ToList())
                {
                    if (nobleFighters.Contains(noble) || noble.Downed || noble.WorkTagIsDisabled(WorkTags.Violent) || LandedTitles.Data(noble) == null)
                    {
                        continue;
                    }
                    LandedTitles.AddPrestige(noble, -1, "CM_PrestigeStayedOut".Translate(colonistsDied));
                }
            }
            nobleFighters.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref active, "active", false);
            Scribe_Values.Look(ref factionInvolved, "factionInvolved", false);
            Scribe_Values.Look(ref soldiersFought, "soldiersFought", false);
            Scribe_Values.Look(ref colonistsDied, "colonistsDied", 0);
            Scribe_Collections.Look(ref nobleFighters, "nobleFighters", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                nobleFighters ??= new HashSet<Pawn>();
                nobleFighters.RemoveWhere(p => p == null);
            }
        }
    }

    // ===== The realm: once a quadrum =====

    // Every hour, notes any colony noble whose standing has fallen below half. When a new quadrum begins,
    // nobles whose standing never did gain +1, and +1 more if the colony's home maps grew in wealth.
    public class GameComponent_RealmPrestige : GameComponent
    {
        private int lastQuadrum = -1;
        private float lastWealth = -1f;

        public GameComponent_RealmPrestige(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (!LandedTitles.Active || Find.TickManager.TicksGame % GenDate.TicksPerHour != 0)
            {
                return;
            }
            List<Pawn> nobles = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Where(p => LandedTitles.Data(p) != null).ToList();
            foreach (Pawn noble in nobles)
            {
                Need_Standing standing = noble.needs?.TryGetNeed<Need_Standing>();
                if (standing != null && standing.CurLevel < 0.5f)
                {
                    LandedTitles.Data(noble).standingDipped = true;
                }
            }
            int quadrum = (int)(Find.TickManager.TicksAbs / GenDate.TicksPerQuadrum);
            if (lastQuadrum < 0)
            {
                lastQuadrum = quadrum;
                lastWealth = ColonyWealth();
                return;
            }
            if (quadrum == lastQuadrum)
            {
                return;
            }
            lastQuadrum = quadrum;
            float wealth = ColonyWealth();
            bool grew = wealth > lastWealth;
            lastWealth = wealth;
            foreach (Pawn noble in nobles)
            {
                Hediff_Noble data = LandedTitles.Data(noble);
                if (!data.standingDipped)
                {
                    LandedTitles.AddPrestige(noble, grew ? 2 : 1, (grew ? "CM_PrestigeRealmGrew" : "CM_PrestigeRealm").Translate());
                }
                data.standingDipped = false;
            }
        }

        private static float ColonyWealth()
        {
            return Find.Maps.Where(m => m.IsPlayerHome).Sum(m => m.wealthWatcher.WealthTotal);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lastQuadrum, "lastQuadrum", -1);
            Scribe_Values.Look(ref lastWealth, "lastWealth", -1f);
        }
    }

    // ===== Hooks =====

    // FromQuality.Apply picks the outcome with GetOutcome, and only finished rituals get there, so this is
    // where we learn how a ritual went. (Rituals with their own outcome logic, like childbirth, are skipped.)
    [HarmonyPatch(typeof(RitualOutcomeEffectWorker_FromQuality), nameof(RitualOutcomeEffectWorker_FromQuality.GetOutcome))]
    public static class Patch_RitualOutcome_GetOutcome
    {
        private static readonly System.Reflection.FieldInfo OrganizerField = AccessTools.Field(typeof(LordJob_Ritual), "organizer");

        public static void Postfix(LordJob_Ritual ritual, RitualOutcomePossibility __result)
        {
            if (ritual == null || !LandedTitles.Active)
            {
                return;
            }
            PrestigeSources.OnRitualOutcome(ritual, __result, OrganizerField?.GetValue(ritual) as Pawn);
        }
    }

    // Records who hurt hostile pawns, for the battle on that map.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_Pawn_PostApplyDamage
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (!LandedTitles.Active || !(dinfo.Instigator is Pawn attacker) || attacker.Faction != Faction.OfPlayer || !__instance.HostileTo(Faction.OfPlayer))
            {
                return;
            }
            MapComponent_RealmDefence.For(__instance.MapHeld)?.NoteHit(attacker, __instance);
        }
    }

    // Counts colonists dying in a battle, and gives +1 to a colony noble who kills an enemy faction's leader.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill
    {
        public static void Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            if (!LandedTitles.Active || __instance.Dead)
            {
                return;
            }
            if (__instance.IsColonist)
            {
                MapComponent_RealmDefence.For(__instance.MapHeld)?.NoteColonistDied();
                return;
            }
            Faction faction = __instance.Faction;
            bool leader = faction != null && (faction.leader == __instance || __instance.kindDef.factionLeader);
            if (leader && faction.HostileTo(Faction.OfPlayer) && dinfo?.Instigator is Pawn killer)
            {
                LandedTitles.AddPrestige(killer, 1, "CM_PrestigeKilledLeader".Translate(__instance.LabelShort, faction.Name));
            }
        }
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    public static class Patch_Quest_End
    {
        public static void Prefix(Quest __instance, out bool __state)
        {
            __state = __instance.State == QuestState.Ongoing;
        }

        public static void Postfix(Quest __instance, QuestEndOutcome outcome, bool __state)
        {
            if (__state && outcome == QuestEndOutcome.Success && LandedTitles.Active)
            {
                PrestigeSources.OnQuestSucceeded(__instance);
            }
        }
    }

    // Vanilla runs this when a visit ends normally (not when visitors flee danger or the weather).
    [HarmonyPatch(typeof(TransitionAction_CheckGiveGift), nameof(TransitionAction_CheckGiveGift.DoAction))]
    public static class Patch_TransitionAction_CheckGiveGift
    {
        public static void Postfix(Transition trans)
        {
            if (LandedTitles.Active)
            {
                PrestigeSources.OnVisitorsLeaving(trans.target?.lord, trans.Map);
            }
        }
    }

    // An extreme mental break from low mood costs 1.
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_MentalStateHandler_TryStartMentalState
    {
        public static void Postfix(Pawn ___pawn, MentalStateDef stateDef, bool causedByMood, bool __result)
        {
            if (__result && causedByMood && LandedTitles.Active && PrestigeSources.IsExtremeBreak(stateDef))
            {
                LandedTitles.AddPrestige(___pawn, -1, "CM_PrestigeBreak".Translate(stateDef.label));
            }
        }
    }

    // Being carried off by raiders costs 2.
    [HarmonyPatch(typeof(KidnappedPawnsTracker), nameof(KidnappedPawnsTracker.Kidnap))]
    public static class Patch_KidnappedPawnsTracker_Kidnap
    {
        public static void Prefix(Pawn pawn)
        {
            if (LandedTitles.Active)
            {
                LandedTitles.AddPrestige(pawn, -2, "CM_PrestigeKidnapped".Translate());
            }
        }
    }

    // Being imprisoned (arrested at home or captured) costs 2. Checked before the change, while the pawn is
    // still a free colony noble.
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class Patch_Pawn_GuestTracker_SetGuestStatus
    {
        public static void Prefix(Pawn ___pawn, GuestStatus guestStatus)
        {
            if (guestStatus == GuestStatus.Prisoner && LandedTitles.Active && !___pawn.IsPrisoner)
            {
                LandedTitles.AddPrestige(___pawn, -2, "CM_PrestigeImprisoned".Translate());
            }
        }
    }
}
