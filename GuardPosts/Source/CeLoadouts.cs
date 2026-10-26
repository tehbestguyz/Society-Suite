using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace GuardPosts
{
    // Combat Extended loadouts through reflection, so Guard Posts has no reference to CE.
    // Loadouts are tracked by CE's unique ID; -1 means none.
    public static class CeLoadouts
    {
        private static bool initialized;
        private static MethodInfo getLoadoutId;
        private static MethodInfo setLoadoutById;
        private static PropertyInfo loadoutsProperty;
        private static PropertyInfo uniqueIdProperty;
        private static PropertyInfo labelCapProperty;

        public static bool Active
        {
            get
            {
                Init();
                return setLoadoutById != null;
            }
        }

        private static void Init()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;
            if (!ModsConfig.IsActive("CETeam.CombatExtended"))
            {
                return;
            }
            try
            {
                Type utility = GenTypes.GetTypeInAnyAssembly("CombatExtended.Utility_Loadouts");
                Type manager = GenTypes.GetTypeInAnyAssembly("CombatExtended.LoadoutManager");
                Type loadout = GenTypes.GetTypeInAnyAssembly("CombatExtended.Loadout");
                MethodInfo get = utility?.GetMethod("GetLoadoutId", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn) }, null);
                MethodInfo set = utility?.GetMethod("SetLoadoutById", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn), typeof(int) }, null);
                loadoutsProperty = manager?.GetProperty("Loadouts", BindingFlags.Public | BindingFlags.Static);
                uniqueIdProperty = loadout?.GetProperty("UniqueID");
                labelCapProperty = loadout?.GetProperty("LabelCap");
                if (get == null || set == null || loadoutsProperty == null || uniqueIdProperty == null || labelCapProperty == null)
                {
                    Log.Warning("[Guard Posts] Combat Extended is active, but its loadout API wasn't found. Off-duty loadouts are disabled.");
                    return;
                }
                getLoadoutId = get;
                setLoadoutById = set;
            }
            catch (Exception e)
            {
                Log.Warning("[Guard Posts] Couldn't read Combat Extended's loadout API: " + e.Message);
            }
        }

        public static List<(int id, string label)> All()
        {
            List<(int, string)> result = new List<(int, string)>();
            if (!Active || !(loadoutsProperty.GetValue(null) is IList loadouts))
            {
                return result;
            }
            foreach (object loadout in loadouts)
            {
                result.Add(((int)uniqueIdProperty.GetValue(loadout), (string)labelCapProperty.GetValue(loadout)));
            }
            return result;
        }

        public static string Label(int id)
        {
            foreach ((int id, string label) loadout in All())
            {
                if (loadout.id == id)
                {
                    return loadout.label;
                }
            }
            return null;
        }

        public static bool Exists(int id) => id >= 0 && Label(id) != null;

        private static Type updateLoadoutGiverType;
        private static MethodInfo tryGetGiver;
        private static ThinkNode_JobGiver fallbackGiver;

        // The job CE would give to bring the pawn's inventory in line with their loadout, or null when it's done (or nothing can be found).
        public static Job UpdateLoadoutJob(Pawn pawn)
        {
            if (!Active)
            {
                return null;
            }
            try
            {
                if (updateLoadoutGiverType == null)
                {
                    updateLoadoutGiverType = GenTypes.GetTypeInAnyAssembly("CombatExtended.JobGiver_UpdateLoadout");
                    if (updateLoadoutGiverType == null)
                    {
                        return null;
                    }
                    tryGetGiver = typeof(Pawn_Thinker).GetMethod("TryGetMainTreeThinkNode")?.MakeGenericMethod(updateLoadoutGiverType);
                }
                ThinkNode_JobGiver giver = pawn.thinker != null ? tryGetGiver?.Invoke(pawn.thinker, null) as ThinkNode_JobGiver : null;
                giver ??= fallbackGiver ??= (ThinkNode_JobGiver)Activator.CreateInstance(updateLoadoutGiverType);
                return giver.TryIssueJobPackage(pawn, default(JobIssueParams)).Job;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Guard Posts] Couldn't get " + pawn + "'s CE loadout job: " + (e.InnerException ?? e).Message, 0x5C0D9 ^ pawn.thingIDNumber);
                return null;
            }
        }

        public static int Get(Pawn pawn)
        {
            if (!Active)
            {
                return -1;
            }
            try
            {
                return (int)getLoadoutId.Invoke(null, new object[] { pawn });
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Guard Posts] Couldn't read " + pawn + "'s CE loadout: " + e.InnerException?.Message, 0x5C0D7 ^ pawn.thingIDNumber);
                return -1;
            }
        }

        public static bool Set(Pawn pawn, int id)
        {
            if (!Active || !Exists(id))
            {
                return false;
            }
            try
            {
                setLoadoutById.Invoke(null, new object[] { pawn, id });
                return true;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Guard Posts] Couldn't set " + pawn + "'s CE loadout: " + e.InnerException?.Message, 0x5C0D8 ^ pawn.thingIDNumber);
                return false;
            }
        }

        // CE only looks for loadout items within 80 cells of the pawn, so a pawn mustered from a far post finds nothing
        // and would set off without their weapon. Beyond this range, the muster walks them over first.
        public const float CeSearchRange = 75f;

        private static bool farItemInit;
        private static MethodInfo getLoadout;
        private static MethodInfo getStorageByThingDef;
        private static MethodInfo getSlotsFor;
        private static FieldInfo defaultLoadoutField;
        private static FieldInfo integerValueField;
        private static PropertyInfo slotCount;
        private static PropertyInfo slotCountType;
        private static PropertyInfo slotThingDef;
        private static PropertyInfo slotGenericDef;
        private static PropertyInfo genericLambda;
        private static FieldInfo genericRequestGroup;

        private static bool InitFarItem()
        {
            if (farItemInit)
            {
                return getLoadout != null;
            }
            farItemInit = true;
            Type utility = GenTypes.GetTypeInAnyAssembly("CombatExtended.Utility_Loadouts");
            Type holdTracker = GenTypes.GetTypeInAnyAssembly("CombatExtended.Utility_HoldTracker");
            Type loadout = GenTypes.GetTypeInAnyAssembly("CombatExtended.Loadout");
            Type slot = GenTypes.GetTypeInAnyAssembly("CombatExtended.LoadoutSlot");
            Type generic = GenTypes.GetTypeInAnyAssembly("CombatExtended.LoadoutGenericDef");
            Type integer = GenTypes.GetTypeInAnyAssembly("CombatExtended.Integer");
            MethodInfo get = utility?.GetMethod("GetLoadout", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn) }, null);
            getStorageByThingDef = holdTracker?.GetMethod("GetStorageByThingDef", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Pawn) }, null);
            getSlotsFor = loadout?.GetMethod("GetSlotsFor", new[] { typeof(Pawn) });
            defaultLoadoutField = loadout?.GetField("defaultLoadout");
            integerValueField = integer?.GetField("value");
            slotCount = slot?.GetProperty("count");
            slotCountType = slot?.GetProperty("countType");
            slotThingDef = slot?.GetProperty("thingDef");
            slotGenericDef = slot?.GetProperty("genericDef");
            genericLambda = generic?.GetProperty("lambda");
            genericRequestGroup = generic?.GetField("thingRequestGroup");
            if (get == null || getStorageByThingDef == null || getSlotsFor == null || defaultLoadoutField == null || integerValueField == null
                || slotCount == null || slotCountType == null || slotThingDef == null || slotGenericDef == null || genericLambda == null || genericRequestGroup == null)
            {
                Log.Warning("[Guard Posts] Couldn't read Combat Extended's loadout slots; mustered pawns won't walk to far-away loadout items.");
                return false;
            }
            getLoadout = get;
            return true;
        }

        // The nearest item the pawn's loadout still needs, if it's out of CE's search range; otherwise null.
        // Counts what the pawn already carries the way CE's own pickup logic does.
        public static Thing FarLoadoutItem(Pawn pawn)
        {
            if (!Active || pawn.Map == null || !InitFarItem())
            {
                return null;
            }
            try
            {
                object loadout = getLoadout.Invoke(null, new object[] { pawn });
                if (loadout == null || (bool)defaultLoadoutField.GetValue(loadout))
                {
                    return null;
                }
                Dictionary<ThingDef, int> carried = new Dictionary<ThingDef, int>();
                if (getStorageByThingDef.Invoke(null, new object[] { pawn }) is IDictionary storage)
                {
                    foreach (DictionaryEntry entry in storage)
                    {
                        carried[(ThingDef)entry.Key] = (int)integerValueField.GetValue(entry.Value);
                    }
                }
                foreach (object slot in (IEnumerable)getSlotsFor.Invoke(loadout, new object[] { pawn }))
                {
                    if (slotCountType.GetValue(slot).ToString() == "dropExcess")
                    {
                        continue;
                    }
                    int needed = (int)slotCount.GetValue(slot);
                    ThingDef thingDef = slotThingDef.GetValue(slot) as ThingDef;
                    object generic = slotGenericDef.GetValue(slot);
                    Predicate<ThingDef> matches = generic != null
                        ? (Predicate<ThingDef>)genericLambda.GetValue(generic)
                        : td => td == thingDef;
                    if (matches == null)
                    {
                        continue;
                    }
                    foreach (ThingDef def in carried.Keys.Where(td => matches(td)).ToList())
                    {
                        int used = Mathf.Min(carried[def], needed);
                        carried[def] -= used;
                        needed -= used;
                        if (needed <= 0)
                        {
                            break;
                        }
                    }
                    if (needed <= 0)
                    {
                        continue;
                    }
                    ThingRequest request = generic != null
                        ? ThingRequest.ForGroup((ThingRequestGroup)genericRequestGroup.GetValue(generic))
                        : thingDef.Minifiable ? ThingRequest.ForGroup(ThingRequestGroup.MinifiedThing) : ThingRequest.ForDef(thingDef);
                    Thing item = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, request, PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn, Danger.Some), 9999f,
                        t => matches(t.GetInnerIfMinified().def) && !t.IsForbidden(pawn) && pawn.CanReserve(t, 10, 1)
                            && EquipmentUtility.CanEquip(t, pawn));
                    if (item != null && !pawn.Position.InHorDistOf(item.Position, CeSearchRange))
                    {
                        return item;
                    }
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[Guard Posts] Couldn't check " + pawn + "'s CE loadout for far-away items: " + (e.InnerException ?? e).Message, 0x5C0DA ^ pawn.thingIDNumber);
            }
            return null;
        }
    }

}
