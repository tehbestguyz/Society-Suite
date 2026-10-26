using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Commoners
{
    // "Day off": the Castes tab button under the title buttons sets every colony member's schedule to
    // Anything until their next midnight, then puts the old schedule back. Each pawn's old schedule is saved
    // with the game. An hour the player changes during the day off keeps the player's change. The schedule
    // is edited once on click and once at midnight; between those, the tick check is one comparison.
    public class DayOff : GameComponent
    {
        private List<DayOffEntry> entries = new List<DayOffEntry>();

        // The earliest restoreTick among the entries, or -1 when there's no day off.
        private int nextRestoreTick = -1;

        public DayOff(Game game)
        {
        }

        public static DayOff Get() => Current.Game?.GetComponent<DayOff>();

        public bool Active => entries.Count > 0;

        public void Start(List<Pawn> pawns)
        {
            int count = 0;
            foreach (Pawn pawn in pawns)
            {
                List<TimeAssignmentDef> times = pawn.timetable?.times;
                if (times == null)
                {
                    continue;
                }
                // Pressing it twice in one day keeps the first saved schedule, not the all-Anything one.
                DayOffEntry entry = entries.Find(e => e.pawn == pawn);
                if (entry == null)
                {
                    entry = new DayOffEntry { pawn = pawn, saved = new List<TimeAssignmentDef>(times) };
                    entries.Add(entry);
                }
                entry.restoreTick = NextMidnight(pawn);
                for (int hour = 0; hour < times.Count; hour++)
                {
                    times[hour] = TimeAssignmentDefOf.Anything;
                }
                count++;
            }
            UpdateNextRestoreTick();
            Messages.Message("CM_MessageDayOff".Translate(count), MessageTypeDefOf.NeutralEvent, historical: false);
        }

        // Ends the day off early.
        public void EndNow()
        {
            foreach (DayOffEntry entry in entries)
            {
                entry.Restore();
            }
            entries.Clear();
            nextRestoreTick = -1;
        }

        public override void GameComponentTick()
        {
            if (nextRestoreTick < 0 || Find.TickManager.TicksGame < nextRestoreTick)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            bool restored = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].restoreTick <= now)
                {
                    entries[i].Restore();
                    entries.RemoveAt(i);
                    restored = true;
                }
            }
            UpdateNextRestoreTick();
            if (restored && entries.Count == 0)
            {
                Messages.Message("CM_MessageDayOffOver".Translate(), MessageTypeDefOf.SilentInput, historical: false);
            }
        }

        // The tick of the pawn's next midnight, by the local time where the pawn is.
        private static int NextMidnight(Pawn pawn)
        {
            int ticksLeftToday = (int)((1f - GenLocalDate.DayPercent(pawn)) * GenDate.TicksPerDay);
            return Find.TickManager.TicksGame + ticksLeftToday + 1;
        }

        private void UpdateNextRestoreTick()
        {
            nextRestoreTick = -1;
            foreach (DayOffEntry entry in entries)
            {
                if (nextRestoreTick < 0 || entry.restoreTick < nextRestoreTick)
                {
                    nextRestoreTick = entry.restoreTick;
                }
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                entries ??= new List<DayOffEntry>();
                // A pawn that no longer exists loads as null.
                entries.RemoveAll(e => e.pawn == null);
                UpdateNextRestoreTick();
            }
        }
    }

    public class DayOffEntry : IExposable
    {
        public Pawn pawn;
        public List<TimeAssignmentDef> saved;
        public int restoreTick;

        // Puts back the saved schedule, except hours the player has changed since (those aren't Anything).
        public void Restore()
        {
            List<TimeAssignmentDef> times = pawn?.timetable?.times;
            if (times == null || saved == null)
            {
                return;
            }
            for (int hour = 0; hour < times.Count && hour < saved.Count; hour++)
            {
                if (times[hour] == TimeAssignmentDefOf.Anything && saved[hour] != null)
                {
                    times[hour] = saved[hour];
                }
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Collections.Look(ref saved, "saved", LookMode.Def);
            Scribe_Values.Look(ref restoreTick, "restoreTick", 0);
        }
    }

    [StaticConstructorOnStartup]
    public static class DayOffButton
    {
        static DayOffButton()
        {
            CastesTabExtensions.TopButtonsBelow.Add(new CastesTabButton
            {
                width = 170f,
                label = _ => (DayOff.Get()?.Active == true ? "CM_DayOffEnd" : "CM_DayOff").Translate().ToString(),
                tooltip = _ => (DayOff.Get()?.Active == true ? "CM_DayOffEndTip" : "CM_DayOffTip").Translate().ToString(),
                action = pawns =>
                {
                    DayOff dayOff = DayOff.Get();
                    if (dayOff == null)
                    {
                        return;
                    }
                    if (dayOff.Active)
                    {
                        dayOff.EndNow();
                    }
                    else
                    {
                        dayOff.Start(pawns);
                    }
                }
            });
        }
    }
}
