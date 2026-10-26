using System.Collections.Generic;
using System.Linq;
using CashRegister;
using Commoners;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CommonersWages
{
    // A plot of land the colony can sell (or grant) to a colonist. Any indoor room the zone touches is
    // part of the lot, walls and furniture included, so the player only needs to paint a few floor
    // cells of each room. Outdoor cells count only where they're painted.
    //
    // The owner and their family (partner, parents, children) sleep free in any bed on the lot, and
    // its rent-free beds are private to them. Rent from anyone else sleeping on the lot goes to the
    // owner's savings instead of payroll.
    public class Zone_Lot : Zone
    {
        public Pawn owner;
        public bool forSale;
        // -1 means the price is the appraised value.
        public int priceOverride = -1;
        public int ownedSinceTick = -1;
        public int rentCollected;

        // Renting out the whole lot (see LeaseUtility). An unowned lot is either for sale or for rent.
        public bool forRent;
        // -1 means the rent is a share of the appraised value.
        public int rentOverride = -1;
        public Pawn tenant;
        public int leaseSinceTick = -1;
        public int rentDueTick = -1;
        public bool rentLate;

        // The appraisal walks every room and building on the lot, so it's cached briefly.
        private int appraisedTick = -99999;
        private int appraisedLand;
        private int appraisedBuildings;

        private bool deleting;

        private static int nextColorIndex;

        private static readonly Color[] LotColors =
        {
            new Color(0.85f, 0.62f, 0.35f),
            new Color(0.78f, 0.38f, 0.32f),
            new Color(0.93f, 0.80f, 0.40f),
            new Color(0.72f, 0.50f, 0.78f),
            new Color(0.50f, 0.70f, 0.85f),
            new Color(0.88f, 0.55f, 0.65f)
        };

        public static readonly int[] PricePresets = { 250, 500, 1000, 1500, 2000, 3000, 5000, 7500, 10000, 15000, 20000 };

        public Zone_Lot()
        {
        }

        public Zone_Lot(ZoneManager zoneManager) : base("CW_LotBaseName".Translate(), zoneManager)
        {
        }

        protected override Color NextZoneColor
        {
            get
            {
                Color c = LotColors[nextColorIndex];
                nextColorIndex = (nextColorIndex + 1) % LotColors.Length;
                return new Color(c.r, c.g, c.b, 0.09f);
            }
        }

        public int AppraisedLand
        {
            get
            {
                Appraise();
                return appraisedLand;
            }
        }

        public int AppraisedBuildings
        {
            get
            {
                Appraise();
                return appraisedBuildings;
            }
        }

        public int AppraisedValue => AppraisedLand + AppraisedBuildings;

        public int Price => priceOverride >= 0 ? priceOverride : AppraisedValue;

        public int AppraisedRent => Mathf.Max(10, Mathf.RoundToInt(AppraisedValue * WagesMod.Settings.lotRentPercent / 1000f) * 10);

        // Rent for the whole lot, every LeaseUtility.RentPeriodDays days.
        public int LotRent => rentOverride >= 0 ? rentOverride : AppraisedRent;

        // Whose household lives here: the tenant if it's rented out, otherwise the owner.
        public Pawn Resident => tenant ?? owner;

        // Vacant and waiting for a buyer or tenant.
        public bool OnMarket => owner == null && tenant == null && (forSale || forRent);

        public void InvalidateAppraisal() => appraisedTick = -99999;

        private void Appraise()
        {
            int now = Find.TickManager.TicksGame;
            if (now - appraisedTick < 250 && now >= appraisedTick)
            {
                return;
            }
            appraisedTick = now;
            HashSet<IntVec3> land = new HashSet<IntVec3>(cells);
            HashSet<Thing> buildings = new HashSet<Thing>();
            foreach (Room room in LotUtility.RoomsOf(this))
            {
                land.UnionWith(room.Cells);
                foreach (Thing t in room.ContainedAndAdjacentThings)
                {
                    if (LotUtility.CountsAsProperty(t))
                    {
                        buildings.Add(t);
                    }
                }
            }
            foreach (IntVec3 c in cells)
            {
                foreach (Thing t in c.GetThingList(Map))
                {
                    if (LotUtility.CountsAsProperty(t))
                    {
                        buildings.Add(t);
                    }
                }
            }
            appraisedLand = land.Count * WagesMod.Settings.landPricePerCell;
            float value = buildings.Sum(t => t.MarketValue * t.stackCount) * WagesMod.Settings.buildingValuePercent / 100f;
            appraisedBuildings = Mathf.RoundToInt(value / 10f) * 10;
        }

        public override void AddCell(IntVec3 c)
        {
            base.AddCell(c);
            LotUtility.SetDirty(Map);
            InvalidateAppraisal();
        }

        public override void Delete(bool playSound)
        {
            deleting = true;
            // While the lot is still on the map, so the tenant's beds can be found.
            LeaseUtility.EndLease(this, "CW_MessageLeaseEndedDeleted", evicted: false);
            base.Delete(playSound);
        }

        public override void RemoveCell(IntVec3 c)
        {
            Map map = Map;
            // Building furniture or a wall on a lot's last painted cell would delete the lot (and, if
            // it's owned, seize it). Only the player deleting or shrinking it away can do that.
            if (cells.Count == 1 && cells.Contains(c) && !deleting && !(Find.DesignatorManager?.SelectedDesignator is Designator_ZoneDelete))
            {
                return;
            }
            if (cells.Count == 1 && cells.Contains(c))
            {
                LeaseUtility.EndLease(this, "CW_MessageLeaseEndedDeleted", evicted: false);
            }
            base.RemoveCell(c);
            LotUtility.SetDirty(map);
            InvalidateAppraisal();
        }

        // Deleting (or shrinking away) an owned lot takes it from its owner without payment.
        public override void PostDeregister()
        {
            base.PostDeregister();
            LeaseUtility.ClearLease(this);
            if (owner != null)
            {
                LotUtility.Seize(this, deleted: true);
            }
            LotUtility.SetDirty(Map);
        }

        public override IEnumerable<Gizmo> GetZoneAddGizmos()
        {
            yield return DesignatorUtility.FindAllowedDesignator<Designator_ZoneAdd_Lot_Expand>();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }
            if (owner == null)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "CW_LotForSale".Translate(),
                    defaultDesc = "CW_LotForSaleDesc".Translate(),
                    icon = WagesTextures.ForSale,
                    isActive = () => forSale,
                    toggleAction = () =>
                    {
                        forSale = !forSale;
                        forRent &= !forSale;
                    }
                };
            }
            foreach (Gizmo g in RentGizmos())
            {
                yield return g;
            }
            if (owner == null)
            {
                yield return new Command_Action
                {
                    defaultLabel = priceOverride >= 0
                        ? "CW_LotPrice".Translate(priceOverride.ToStringMoney())
                        : "CW_LotPriceAuto".Translate(AppraisedValue.ToStringMoney()),
                    defaultDesc = "CW_LotPriceDesc".Translate(),
                    icon = WagesTextures.LotPrice,
                    action = () =>
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>
                        {
                            new FloatMenuOption("CW_LotPriceAutoOption".Translate(AppraisedValue.ToStringMoney()), () => priceOverride = -1)
                        };
                        options.AddRange(PricePresets.Select(v => new FloatMenuOption(v.ToStringMoney(), () => priceOverride = v)));
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "CW_LotSellTo".Translate(),
                    defaultDesc = "CW_LotSellToDesc".Translate(),
                    icon = WagesTextures.BuyLot,
                    action = () => Find.WindowStack.Add(new FloatMenu(LotUtility.SellToOptions(this)))
                };
                yield return new Command_Action
                {
                    defaultLabel = "CW_LotGrant".Translate(),
                    defaultDesc = "CW_LotGrantDesc".Translate(),
                    icon = WagesTextures.Grant,
                    action = () => Find.WindowStack.Add(new FloatMenu(LotUtility.GrantOptions(this)))
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "CW_LotRepossess".Translate(),
                    defaultDesc = "CW_LotRepossessDesc".Translate(owner.Named("PAWN")),
                    icon = WagesTextures.Repossess,
                    action = () => Find.WindowStack.Add(new FloatMenu(LotUtility.RepossessOptions(this)))
                };
            }
        }

        private IEnumerable<Gizmo> RentGizmos()
        {
            yield return new Command_Toggle
            {
                defaultLabel = "CW_LotForRent".Translate(),
                defaultDesc = owner == null
                    ? "CW_LotForRentDesc".Translate(LeaseUtility.RentPeriodDays, LeaseUtility.GraceDays)
                    : "CW_LotForRentOwnedDesc".Translate(owner.Named("PAWN"), LeaseUtility.RentPeriodDays, LeaseUtility.GraceDays),
                icon = WagesTextures.Rent,
                isActive = () => forRent,
                toggleAction = () =>
                {
                    forRent = !forRent;
                    forSale &= !forRent;
                }
            };
            if (!forRent && tenant == null)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = rentOverride >= 0
                    ? "CW_LotRent".Translate(rentOverride.ToStringMoney())
                    : "CW_LotRentAuto".Translate(AppraisedRent.ToStringMoney()),
                defaultDesc = "CW_LotRentDesc".Translate(LeaseUtility.RentPeriodDays, WagesMod.Settings.lotRentPercent),
                icon = WagesTextures.LotPrice,
                action = () =>
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("CW_LotRentAutoOption".Translate(AppraisedRent.ToStringMoney()), () => rentOverride = -1)
                    };
                    options.AddRange(LeaseUtility.RentPresets.Select(v => new FloatMenuOption(v.ToStringMoney(), () => rentOverride = v)));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
            if (tenant == null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "CW_LotRentTo".Translate(),
                    defaultDesc = "CW_LotRentToDesc".Translate(),
                    icon = WagesTextures.BuyLot,
                    action = () => Find.WindowStack.Add(new FloatMenu(LeaseUtility.RentToOptions(this)))
                };
            }
            else
            {
                Pawn t = tenant;
                yield return new Command_Action
                {
                    defaultLabel = "CW_LotEndLease".Translate(),
                    defaultDesc = "CW_LotEndLeaseDesc".Translate(t.Named("PAWN")),
                    icon = WagesTextures.Repossess,
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CW_LotEndLeaseConfirm".Translate(t.Named("PAWN"), label.Named("LOT")),
                        () => LeaseUtility.EndLease(this, "CW_MessageLeaseEvictedByOrder", evicted: true), destructive: true))
                };
            }
        }

        public override string GetInspectString()
        {
            List<Room> rooms = LotUtility.RoomsOf(this).ToList();
            int beds = LotUtility.UsableBedsOn(this).Count();
            List<string> lines = new List<string>
            {
                "CW_LotInspectSize".Translate(CellCount, rooms.Count, beds)
            };
            if (owner != null)
            {
                string since = ownedSinceTick >= 0
                    ? ((Find.TickManager.TicksGame - ownedSinceTick) / (float)GenDate.TicksPerDay).ToString("0.#")
                    : "?";
                lines.Add("CW_LotInspectOwner".Translate(owner.Named("PAWN"), since));
                if (rentCollected > 0)
                {
                    lines.Add("CW_LotInspectRent".Translate(rentCollected.ToStringMoney()));
                }
            }
            else if (forSale)
            {
                lines.Add("CW_LotInspectForSale".Translate(Price.ToStringMoney()));
            }
            else if (!forRent && tenant == null)
            {
                lines.Add("CW_LotInspectNotForSale".Translate());
            }
            if (tenant != null)
            {
                lines.Add("CW_LotInspectTenant".Translate(tenant.Named("PAWN"), LotRent.ToStringMoney().Named("RENT"), LeaseUtility.RentPeriodDays.Named("DAYS")));
                lines.Add(rentLate
                    ? "CW_LotInspectRentLate".Translate(LeaseUtility.DueIn(this)).ToString()
                    : "CW_LotInspectRentDue".Translate(LeaseUtility.DueIn(this)).ToString());
            }
            else if (forRent)
            {
                lines.Add("CW_LotInspectForRent".Translate(LotRent.ToStringMoney(), LeaseUtility.RentPeriodDays));
            }
            lines.Add("CW_LotInspectValue".Translate(AppraisedValue.ToStringMoney(), AppraisedLand.ToStringMoney(), AppraisedBuildings.ToStringMoney()));
            return string.Join("\n", lines);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref owner, "cwOwner");
            Scribe_Values.Look(ref forSale, "cwForSale", false);
            Scribe_Values.Look(ref priceOverride, "cwPriceOverride", -1);
            Scribe_Values.Look(ref ownedSinceTick, "cwOwnedSinceTick", -1);
            Scribe_Values.Look(ref rentCollected, "cwRentCollected", 0);
            Scribe_Values.Look(ref forRent, "cwForRent", false);
            Scribe_Values.Look(ref rentOverride, "cwRentOverride", -1);
            Scribe_References.Look(ref tenant, "cwTenant");
            Scribe_Values.Look(ref leaseSinceTick, "cwLeaseSinceTick", -1);
            Scribe_Values.Look(ref rentDueTick, "cwRentDueTick", -1);
            Scribe_Values.Look(ref rentLate, "cwRentLate", false);
        }
    }

    public class Designator_ZoneAdd_Lot : Designator_ZoneAdd
    {
        protected override string NewZoneLabel => "CW_LotBaseName".Translate();

        public Designator_ZoneAdd_Lot()
        {
            zoneTypeToPlace = typeof(Zone_Lot);
            defaultLabel = "CW_LotDesignator".Translate();
            defaultDesc = "CW_LotDesignatorDesc".Translate();
            // Designators are built while defs load, before [StaticConstructorOnStartup] classes run.
            icon = ContentFinder<Texture2D>.Get("UI/CommonersWages/ZoneCreate_Lot");
            hotKey = null;
        }

        protected override Zone MakeNewZone()
        {
            return new Zone_Lot(Find.CurrentMap.zoneManager);
        }

        // A room can only belong to one lot.
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            AcceptanceReport report = base.CanDesignateCell(c);
            if (!report.Accepted)
            {
                return report;
            }
            Room room = c.GetRoom(Map);
            if (!LotUtility.IsIndoorRoom(room))
            {
                return true;
            }
            Zone_Lot other = LotUtility.LotOfRoom(Map, room);
            if (other != null && other != Find.Selector.SelectedZone)
            {
                return "CW_LotRoomTaken".Translate(other.label);
            }
            return true;
        }
    }

    public class Designator_ZoneAdd_Lot_Expand : Designator_ZoneAdd_Lot
    {
        public Designator_ZoneAdd_Lot_Expand()
        {
            defaultLabel = "DesignatorZoneExpand".Translate();
            hotKey = KeyBindingDefOf.Misc6;
        }
    }

    // Which lot each indoor room belongs to, per map. Rooms are rebuilt whenever walls change, so the
    // table is refreshed every 250 ticks, or straight away after a lot changes shape.
    public class LotMapComponent : MapComponent
    {
        private readonly Dictionary<Room, Zone_Lot> roomLots = new Dictionary<Room, Zone_Lot>();
        private int builtTick = -99999;

        public LotMapComponent(Map map) : base(map)
        {
        }

        public void SetDirty() => builtTick = -99999;

        private void EnsureFresh()
        {
            int now = Find.TickManager.TicksGame;
            if (now - builtTick < 250 && now >= builtTick)
            {
                return;
            }
            builtTick = now;
            roomLots.Clear();
            // Lowest ID first, so if two lots end up touching one room (a wall came down), the older lot keeps it.
            foreach (Zone_Lot lot in map.zoneManager.AllZones.OfType<Zone_Lot>().OrderBy(z => z.ID))
            {
                foreach (IntVec3 c in lot.cells)
                {
                    Room room = c.GetRoom(map);
                    if (LotUtility.IsIndoorRoom(room) && !roomLots.ContainsKey(room))
                    {
                        roomLots[room] = lot;
                    }
                }
            }
        }

        public Zone_Lot LotOfRoom(Room room)
        {
            if (room == null)
            {
                return null;
            }
            EnsureFresh();
            return roomLots.TryGetValue(room, out Zone_Lot lot) ? lot : null;
        }

        public IEnumerable<Room> RoomsOf(Zone_Lot lot)
        {
            EnsureFresh();
            return roomLots.Where(kv => kv.Value == lot).Select(kv => kv.Key).ToList();
        }
    }

    public static class LotUtility
    {
        // Who owns a lot, rebuilt at most once a tick: savings protection asks this on every silver drop.
        private static readonly HashSet<Pawn> owners = new HashSet<Pawn>();
        private static int ownersTick = -1;

        public static void InvalidateOwners() => ownersTick = -1;

        public static void SetDirty(Map map)
        {
            map?.GetComponent<LotMapComponent>()?.SetDirty();
        }

        public static IEnumerable<Zone_Lot> AllLots(Map map)
        {
            return map?.zoneManager?.AllZones.OfType<Zone_Lot>() ?? Enumerable.Empty<Zone_Lot>();
        }

        public static IEnumerable<Zone_Lot> AllLots()
        {
            return Find.Maps.SelectMany(AllLots);
        }

        public static bool OwnsAnyLot(Pawn pawn)
        {
            if (pawn == null || Current.Game == null)
            {
                return false;
            }
            int now = Find.TickManager.TicksGame;
            if (ownersTick != now)
            {
                ownersTick = now;
                owners.Clear();
                foreach (Zone_Lot lot in AllLots())
                {
                    if (lot.owner != null)
                    {
                        owners.Add(lot.owner);
                    }
                }
            }
            return owners.Contains(pawn);
        }

        public static IEnumerable<Zone_Lot> LotsOwnedBy(Pawn pawn)
        {
            return AllLots().Where(l => l.owner == pawn);
        }

        public static bool IsIndoorRoom(Room room)
        {
            return room != null && !room.PsychologicallyOutdoors && !room.TouchesMapEdge && !room.IsDoorway;
        }

        public static Zone_Lot LotOfRoom(Map map, Room room)
        {
            return map?.GetComponent<LotMapComponent>()?.LotOfRoom(room);
        }

        public static IEnumerable<Room> RoomsOf(Zone_Lot lot)
        {
            return lot.Map?.GetComponent<LotMapComponent>()?.RoomsOf(lot) ?? Enumerable.Empty<Room>();
        }

        // The lot a cell belongs to: the lot painted on it, or the lot that owns its room.
        public static Zone_Lot LotAt(IntVec3 c, Map map)
        {
            if (map == null || !c.InBounds(map))
            {
                return null;
            }
            if (map.zoneManager.ZoneAt(c) is Zone_Lot painted)
            {
                return painted;
            }
            Room room = c.GetRoom(map);
            return IsIndoorRoom(room) ? LotOfRoom(map, room) : null;
        }

        public static Zone_Lot LotOf(Thing thing)
        {
            return thing != null && thing.Spawned ? LotAt(thing.Position, thing.Map) : null;
        }

        public static Pawn LandlordOf(Building_Bed bed) => LotOf(bed)?.owner;

        // Buildings and furniture the colony built, which the appraisal counts towards the price.
        public static bool CountsAsProperty(Thing t)
        {
            return t is Building && t.def.category == ThingCategory.Building && t.Faction == Faction.OfPlayer && !(t is Building_CashRegister);
        }

        // The owner, their partner, parents and children.
        public static bool IsFamily(Pawn owner, Pawn p)
        {
            if (owner == null || p == null)
            {
                return false;
            }
            if (p == owner || LovePartnerRelationUtility.LovePartnerRelationExists(owner, p))
            {
                return true;
            }
            return p.GetMother() == owner || p.GetFather() == owner || owner.GetMother() == p || owner.GetFather() == p;
        }

        // A lot the pawn or someone in their family lives on as owner or tenant, on the given map.
        // Their own comes first. A lot the family owns but rents out isn't their home.
        public static Zone_Lot FamilyLot(Pawn pawn, Map map)
        {
            Zone_Lot best = null;
            foreach (Zone_Lot lot in AllLots(map))
            {
                Pawn resident = lot.Resident;
                if (resident == pawn)
                {
                    return lot;
                }
                if (best == null && resident != null && IsFamily(resident, pawn))
                {
                    best = lot;
                }
            }
            return best;
        }

        public static bool IsColonistBed(Building_Bed bed)
        {
            return bed != null && !bed.Medical && bed.ForOwnerType == BedOwnerType.Colonist && bed.def.building.bed_humanlike;
        }

        public static IEnumerable<Building_Bed> UsableBedsOn(Zone_Lot lot)
        {
            Map map = lot.Map;
            if (map == null)
            {
                return Enumerable.Empty<Building_Bed>();
            }
            return map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>().Where(b => IsColonistBed(b) && LotOf(b) == lot);
        }

        public static float RoomScore(Building_Bed bed)
        {
            Room room = bed?.GetRoom();
            return room == null || room.PsychologicallyOutdoors ? -1f : room.GetStat(RoomStatDefOf.Impressiveness);
        }

        public static float Desirability(Zone_Lot lot)
        {
            List<Building_Bed> beds = UsableBedsOn(lot).ToList();
            return beds.Count == 0 ? float.MinValue : beds.Max(RoomScore);
        }

        // Rent-free beds on someone else's lot are private: the AI won't pick them for anyone outside
        // the owner's family. On a rented lot every bed is the tenant's family's, and on a vacant lot
        // that's for sale or rent the free beds wait for the buyer or tenant. A pawn already assigned
        // to the bed (say, by the player) keeps it. Soldiers' beds follow their own rule instead (see RentUtility).
        public static bool IsPrivateFrom(Building_Bed bed, Pawn sleeper)
        {
            if (!IsColonistBed(bed) || bed.IsOwner(sleeper) || RentUtility.IsSoldiersBed(bed))
            {
                return false;
            }
            Zone_Lot lot = LotOf(bed);
            if (lot == null)
            {
                return false;
            }
            if (lot.tenant != null)
            {
                return !IsFamily(lot.tenant, sleeper);
            }
            if (RentUtility.RentOf(bed) > 0)
            {
                return false;
            }
            return lot.owner != null ? !IsFamily(lot.owner, sleeper) : lot.OnMarket;
        }

        // Nobody keeps a rent-free bed on a vacant lot that's for sale or rent. Beds with rent set are
        // ordinary rented rooms until someone takes the whole lot.
        private static void ClearVacantLot(Zone_Lot lot)
        {
            foreach (Building_Bed bed in UsableBedsOn(lot).Where(b => RentUtility.RentOf(b) == 0 && !RentUtility.IsSoldiersBed(b)).ToList())
            {
                foreach (Pawn sleeper in bed.OwnersForReading.ToList())
                {
                    sleeper.ownership.UnclaimBed();
                    Messages.Message("CW_MessageLotVacantMovedOut".Translate(sleeper.Named("PAWN"), lot.label.Named("LOT")),
                        new LookTargets(sleeper), MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
        }

        // The best bed on the lot the pawn could move into: a free slot, or a partner's bed with room.
        // With `displace`, a bed held by someone outside the family will do if nothing else is free.
        public static Building_Bed BestBedOn(Zone_Lot lot, Pawn pawn, bool displace)
        {
            Building_Bed best = null;
            float bestScore = float.MinValue;
            bool bestIsFree = false;
            foreach (Building_Bed bed in UsableBedsOn(lot))
            {
                if (!RestUtility.CanUseBedEver(pawn, bed.def) || bed.IsForbidden(pawn) || RentUtility.SoldiersOnlyBlocks(pawn, bed))
                {
                    continue;
                }
                if (pawn.Spawned && (pawn.Map != bed.Map || !pawn.CanReach(bed, PathEndMode.OnCell, Danger.Some)))
                {
                    continue;
                }
                bool free = bed.OwnersForReading.Count == 0
                    || (bed.AnyUnownedSleepingSlot && bed.OwnersForReading.All(o => LovePartnerRelationUtility.LovePartnerRelationExists(pawn, o)));
                if (!free && !(displace && bed.OwnersForReading.All(o => !IsFamily(lot.Resident, o))))
                {
                    continue;
                }
                float score = RoomScore(bed);
                if ((free && !bestIsFree) || ((free == bestIsFree) && score > bestScore))
                {
                    best = bed;
                    bestScore = score;
                    bestIsFree = free;
                }
            }
            return best;
        }

        // Moves the pawn (and a partner who shared their old bed) into the best bed on the lot.
        public static bool MoveIn(Pawn pawn, Zone_Lot lot, bool displace)
        {
            if (pawn.ownership == null)
            {
                return false;
            }
            Building_Bed current = pawn.ownership.OwnedBed;
            if (current != null && LotOf(current) == lot)
            {
                return false;
            }
            Building_Bed bed = BestBedOn(lot, pawn, displace);
            if (bed == null)
            {
                return false;
            }
            List<Pawn> partners = current?.OwnersForReading
                .Where(o => o != pawn && LovePartnerRelationUtility.LovePartnerRelationExists(pawn, o)).ToList() ?? new List<Pawn>();
            pawn.ownership.ClaimBedIfNonMedical(bed);
            foreach (Pawn partner in partners)
            {
                if (bed.AnyUnownedSleepingSlot && RestUtility.CanUseBedEver(partner, bed.def) && !RentUtility.SoldiersOnlyBlocks(partner, bed))
                {
                    partner.ownership.ClaimBedIfNonMedical(bed);
                }
            }
            return true;
        }

        public static void SetOwner(Zone_Lot lot, Pawn owner)
        {
            // A tenant buying (or being given) the lot they rent: the lease is over, and they stay put.
            if (owner != null && lot.tenant == owner)
            {
                LeaseUtility.ClearLease(lot);
            }
            lot.owner = owner;
            lot.ownedSinceTick = owner == null ? -1 : Find.TickManager.TicksGame;
            lot.rentCollected = 0;
            lot.forSale = false;
            // A lease carries over to the new owner (or the colony); otherwise the listing doesn't.
            lot.forRent &= lot.tenant != null;
            InvalidateOwners();
            if (owner == null || lot.tenant != null)
            {
                return;
            }
            // Rent-free beds are the new owner's family's now; anyone else sleeping in one has to go.
            foreach (Building_Bed bed in UsableBedsOn(lot).Where(b => RentUtility.RentOf(b) == 0 && !RentUtility.IsSoldiersBed(b)).ToList())
            {
                foreach (Pawn sleeper in bed.OwnersForReading.Where(o => !IsFamily(owner, o)).ToList())
                {
                    sleeper.ownership.UnclaimBed();
                    Messages.Message("CW_MessageLotMovedOut".Translate(sleeper.Named("PAWN"), owner.Named("OWNER"), lot.label.Named("LOT")),
                        new LookTargets(sleeper), MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
            MoveIn(owner, lot, displace: true);
            // Moving into their own home ends any lease they had elsewhere.
            Zone_Lot rented = LeaseUtility.LotRentedBy(owner);
            if (rented != null && rented != lot)
            {
                LeaseUtility.EndLease(rented, "CW_MessageLeaseMovedOn", evicted: false);
            }
        }

        public static AcceptanceReport CanOwn(Pawn pawn)
        {
            if (!CasteUtility.IsColonyMember(pawn))
            {
                return false;
            }
            if (CasteUtility.GetCaste(pawn) < Caste.Citizen)
            {
                return "CW_LotCasteTooLow".Translate();
            }
            if (!pawn.DevelopmentalStage.Adult())
            {
                return "CW_LotNotAdult".Translate();
            }
            return true;
        }

        public static AcceptanceReport CanBuy(Pawn pawn, Zone_Lot lot, bool requireForSale)
        {
            if (lot.owner != null)
            {
                return "CW_LotOwned".Translate(lot.owner.Named("PAWN"));
            }
            if (requireForSale && !lot.forSale)
            {
                return "CW_LotNotForSale".Translate();
            }
            AcceptanceReport own = CanOwn(pawn);
            if (!own.Accepted)
            {
                return own;
            }
            if (!UsableBedsOn(lot).Any())
            {
                return "CW_LotNoBed".Translate();
            }
            int savings = WageUtility.TotalSavings(pawn) + (WageUtility.WagesComp(pawn)?.owed ?? 0);
            if (savings < lot.Price)
            {
                return "CW_BuyNeedMore".Translate((lot.Price - savings).ToStringMoney(), savings.ToStringMoney());
            }
            if (!WageUtility.PayrollRegisters(lot.Map).Any())
            {
                return "CW_NoPayrollRegister".Translate();
            }
            return true;
        }

        // Pays for the lot from the buyer's savings into a payroll register, then hands it over.
        public static bool Buy(Pawn pawn, Zone_Lot lot, Building_CashRegister register, bool ordered)
        {
            int price = lot.Price;
            if (lot.owner != null || WageUtility.TotalSavings(pawn) < price)
            {
                return false;
            }
            int paid = WageUtility.PaySavings(pawn, register.GetDirectlyHeldThings(), price);
            MoneyLog.Note(pawn, MoneyKind.LotBought, -paid, lot.label);
            if (paid < price)
            {
                return false;
            }
            SetOwner(lot, pawn);
            pawn.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_BoughtHome);
            WageUtility.PlayKaching(register);
            Messages.Message("CW_MessageBoughtLot".Translate(pawn.Named("PAWN"), lot.label.Named("LOT"), price.ToStringMoney().Named("PRICE")),
                new LookTargets(pawn), ordered ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent);
            return true;
        }

        public static void Grant(Zone_Lot lot, Pawn pawn)
        {
            SetOwner(lot, pawn);
            pawn.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_GrantedHome);
            Messages.Message("CW_MessageGrantedLot".Translate(pawn.Named("PAWN"), lot.label.Named("LOT")), new LookTargets(pawn), MessageTypeDefOf.PositiveEvent);
        }

        public static void Seize(Zone_Lot lot, bool deleted)
        {
            Pawn owner = lot.owner;
            if (owner == null)
            {
                return;
            }
            if (!owner.Dead)
            {
                owner.needs?.mood?.thoughts.memories.TryGainMemory(CW_DefOf.CW_HomeSeized);
            }
            Messages.Message((deleted ? "CW_MessageLotDeleted" : "CW_MessageLotSeized").Translate(owner.Named("PAWN"), lot.label.Named("LOT")),
                new LookTargets(owner), MessageTypeDefOf.NegativeEvent);
            if (deleted)
            {
                lot.owner = null;
                InvalidateOwners();
            }
            else
            {
                SetOwner(lot, null);
            }
        }

        public static void BuyBack(Zone_Lot lot)
        {
            Pawn owner = lot.owner;
            int price = lot.Price;
            if (owner == null || PayrollSilver(lot.Map) < price)
            {
                return;
            }
            int paid = PayFromPayroll(lot.Map, owner, price);
            MoneyLog.Note(owner, MoneyKind.LotBoughtBack, paid, lot.label);
            SetOwner(lot, null);
            Messages.Message("CW_MessageLotBoughtBack".Translate(owner.Named("PAWN"), lot.label.Named("LOT"), paid.ToStringMoney().Named("PRICE")),
                new LookTargets(owner), MessageTypeDefOf.NeutralEvent);
        }

        public static int PayrollSilver(Map map) => WageUtility.PayrollRegisters(map).Sum(WageUtility.SilverIn);

        // Moves up to `amount` from the map's payroll registers into the pawn's savings.
        public static int PayFromPayroll(Map map, Pawn to, int amount)
        {
            int paid = 0;
            foreach (Building_CashRegister register in WageUtility.PayrollRegisters(map).ToList())
            {
                if (paid >= amount)
                {
                    break;
                }
                paid += CreditSavings(to, register.GetDirectlyHeldThings(), amount - paid);
            }
            return paid;
        }

        // Puts silver straight into the pawn's savings: their strongbox if they have one by their bed,
        // otherwise their pocket. Returns how much arrived.
        public static int CreditSavings(Pawn pawn, ThingOwner from, int amount)
        {
            CompWages comp = WageUtility.WagesComp(pawn);
            if (comp == null || amount <= 0)
            {
                return 0;
            }
            Building_Strongbox box = WageUtility.LinkedStrongbox(pawn);
            if (box != null && (comp.stashBox == box || WageUtility.Stash(pawn) == 0))
            {
                comp.stashBox = box;
                // A tenant paying out of a strongbox they share with the landlord: the silver stays put,
                // only whose share it is changes. The caller has already capped `amount` at their stash.
                if (from == box.GetDirectlyHeldThings())
                {
                    comp.stashed += amount;
                    return amount;
                }
                int stashed = WageUtility.TransferSilver(from, box.GetDirectlyHeldThings(), amount);
                comp.stashed += stashed;
                return stashed;
            }
            int purseBefore = WageUtility.ProtectedSilver(pawn);
            int moved = WageUtility.TransferSilver(from, pawn.inventory.innerContainer, amount);
            comp.purse = purseBefore + moved;
            return moved;
        }

        // On payday: a citizen or better with no home of their own buys the best lot for sale they can afford.
        public static void TryAutoBuy(Pawn pawn)
        {
            if (!WagesMod.Settings.autoBuyLots || !pawn.Spawned || !pawn.Map.IsPlayerHome || OwnsAnyLot(pawn) || !CanOwn(pawn).Accepted)
            {
                return;
            }
            Building_CashRegister register = WageUtility.PayrollRegisters(pawn.Map).FirstOrDefault();
            if (register == null)
            {
                return;
            }
            int savings = WageUtility.SpendableSavings(pawn);
            float current = pawn.ownership?.OwnedBed == null ? float.MinValue : RoomScore(pawn.ownership.OwnedBed);
            Zone_Lot best = AllLots(pawn.Map)
                .Where(l => l.owner == null && l.forSale && (l.tenant == null || l.tenant == pawn) && l.Price <= savings
                    && UsableBedsOn(l).Any() && BestBedOn(l, pawn, displace: true) != null)
                .Where(l => Desirability(l) >= current)
                .OrderByDescending(Desirability).ThenBy(l => l.Price)
                .FirstOrDefault();
            if (best != null)
            {
                Buy(pawn, best, register, ordered: false);
            }
        }

        // Owners who die leave their lots to family; owners who leave the colony or are enslaved lose them.
        public static void CheckOwners()
        {
            foreach (Zone_Lot lot in AllLots().ToList())
            {
                Pawn owner = lot.owner;
                if (lot.OnMarket)
                {
                    ClearVacantLot(lot);
                }
                if (owner == null)
                {
                    continue;
                }
                if (owner.Dead)
                {
                    Inherit(lot);
                }
                else if (owner.Discarded || owner.IsSlave || (owner.Faction != Faction.OfPlayer && !owner.IsPrisonerOfColony))
                {
                    Messages.Message("CW_MessageLotReverts".Translate(owner.Named("PAWN"), lot.label.Named("LOT")),
                        new TargetInfo(lot.Position, lot.Map), MessageTypeDefOf.NeutralEvent);
                    SetOwner(lot, null);
                }
            }
        }

        private static void Inherit(Zone_Lot lot)
        {
            Pawn dead = lot.owner;
            Pawn heir = FindHeir(dead);
            if (heir == null)
            {
                Messages.Message("CW_MessageLotNoHeir".Translate(dead.Named("PAWN"), lot.label.Named("LOT")),
                    new TargetInfo(lot.Position, lot.Map), MessageTypeDefOf.NeutralEvent);
                SetOwner(lot, null);
                return;
            }
            lot.owner = heir;
            lot.ownedSinceTick = Find.TickManager.TicksGame;
            if (lot.tenant == heir)
            {
                LeaseUtility.ClearLease(lot);
            }
            InvalidateOwners();
            Find.LetterStack.ReceiveLetter("CW_LetterInheritedLabel".Translate(),
                "CW_LetterInheritedText".Translate(heir.Named("HEIR"), dead.Named("PAWN"), lot.label.Named("LOT")),
                LetterDefOf.NeutralEvent, heir);
        }

        // Partner first (spouse, fiancé, lover), then children oldest first, then parents.
        private static Pawn FindHeir(Pawn dead)
        {
            List<Pawn> family = CasteUtility.ColonyMembers().Where(p => p != dead && !p.Dead).ToList();
            foreach (PawnRelationDef rel in new[] { PawnRelationDefOf.Spouse, PawnRelationDefOf.Fiance, PawnRelationDefOf.Lover })
            {
                Pawn partner = family.FirstOrDefault(p => dead.relations?.DirectRelationExists(rel, p) ?? false);
                if (partner != null)
                {
                    return partner;
                }
            }
            Pawn child = family.Where(p => p.GetMother() == dead || p.GetFather() == dead)
                .OrderByDescending(p => p.ageTracker.AgeBiologicalTicks).FirstOrDefault();
            if (child != null)
            {
                return child;
            }
            return family.FirstOrDefault(p => dead.GetMother() == p || dead.GetFather() == p);
        }

        // ===== Menus =====

        public static List<FloatMenuOption> SellToOptions(Zone_Lot lot)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn pawn in CasteUtility.ColonyMembers().Where(p => p.Spawned && p.Map == lot.Map).OrderBy(p => p.LabelShort))
            {
                AcceptanceReport can = CanBuy(pawn, lot, requireForSale: false);
                string label = pawn.LabelShort + " (" + WageUtility.TotalSavings(pawn).ToStringMoney() + ")";
                if (!can.Accepted)
                {
                    if (!can.Reason.NullOrEmpty())
                    {
                        options.Add(new FloatMenuOption(label + ": " + can.Reason, null));
                    }
                    continue;
                }
                options.Add(new FloatMenuOption(label, () => OrderPurchase(pawn, lot)));
            }
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("CW_LotNoBuyers".Translate(), null));
            }
            return options;
        }

        public static List<FloatMenuOption> GrantOptions(Zone_Lot lot)
        {
            List<FloatMenuOption> options = CasteUtility.ColonyMembers()
                .Where(p => p.MapHeld == lot.Map)
                .OrderBy(p => p.LabelShort)
                .Select(p => new FloatMenuOption(p.LabelShort + " (" + CasteUtility.LabelFor(p) + ")", () => Grant(lot, p)))
                .ToList();
            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("CW_LotNoBuyers".Translate(), null));
            }
            return options;
        }

        public static List<FloatMenuOption> RepossessOptions(Zone_Lot lot)
        {
            int price = lot.Price;
            FloatMenuOption buyBack = new FloatMenuOption("CW_LotBuyBack".Translate(price.ToStringMoney()), () => BuyBack(lot));
            if (PayrollSilver(lot.Map) < price)
            {
                buyBack.Label += ": " + "CW_LotPayrollShort".Translate(PayrollSilver(lot.Map).ToStringMoney());
                buyBack.action = null;
                buyBack.Disabled = true;
            }
            FloatMenuOption seize = new FloatMenuOption("CW_LotSeize".Translate(), () =>
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("CW_LotSeizeConfirm".Translate(lot.owner.Named("PAWN"), lot.label.Named("LOT")),
                    () => Seize(lot, deleted: false), destructive: true)));
            return new List<FloatMenuOption> { buyBack, seize };
        }

        public static List<FloatMenuOption> BuyOptions(Pawn pawn)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Zone_Lot lot in AllLots(pawn.Map).Where(l => l.owner == null && l.forSale).OrderByDescending(Desirability))
            {
                int beds = UsableBedsOn(lot).Count();
                string label = "CW_LotBuyOption".Translate(lot.label, lot.Price.ToStringMoney(), beds);
                AcceptanceReport can = CanBuy(pawn, lot, requireForSale: true);
                options.Add(can.Accepted
                    ? new FloatMenuOption(label, () => OrderPurchase(pawn, lot))
                    : new FloatMenuOption(label + ": " + can.Reason, null));
            }
            return options;
        }

        public static void OrderPurchase(Pawn pawn, Zone_Lot lot)
        {
            Building_CashRegister register = WageUtility.ClosestPayroll(pawn, _ => true);
            if (register == null)
            {
                Messages.Message("CW_NoPayrollRegister".Translate(), pawn, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            Job job = JobMaker.MakeJob(CW_DefOf.CW_BuyLot, register);
            // The lot isn't a Thing, so the job carries its zone ID.
            job.count = lot.ID;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static Zone_Lot LotByID(Map map, int id)
        {
            return AllLots(map).FirstOrDefault(l => l.ID == id);
        }
    }

    public class JobDriver_BuyLot : JobDriver
    {
        private Building_CashRegister Register => (Building_CashRegister)job.GetTarget(TargetIndex.A).Thing;

        private Zone_Lot Lot => LotUtility.LotByID(pawn.Map, job.count);

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !WageUtility.IsPayroll(Register) || Lot == null || Lot.owner != null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(180).WithProgressBarToilDelay(TargetIndex.A);
            Toil buy = ToilMaker.MakeToil("BuyLot");
            buy.initAction = () =>
            {
                Zone_Lot lot = Lot;
                if (lot == null)
                {
                    return;
                }
                WageUtility.CollectWages(pawn, Register);
                AcceptanceReport can = LotUtility.CanBuy(pawn, lot, requireForSale: false);
                if (!can.Accepted)
                {
                    Messages.Message("CW_MessageCantBuyLot".Translate(pawn.Named("PAWN"), lot.label.Named("LOT"), can.Reason.Named("REASON")),
                        pawn, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                LotUtility.Buy(pawn, lot, Register, ordered: true);
            };
            yield return buy;
        }
    }

    public class ThoughtWorker_Homeowner : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            return CasteUtility.IsColonyMember(p) && LotUtility.OwnsAnyLot(p) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    // Vanilla trims a zone back to one connected piece whenever a wall or furniture splits it, or the
    // player expands it. For a lot that would silently drop rooms the owner paid for, so lots are
    // allowed to be in pieces. (New lots are still painted as one connected area.)
    [HarmonyPatch(typeof(Zone), nameof(Zone.CheckContiguous))]
    public static class Patch_Zone_CheckContiguous
    {
        public static bool Prefix(Zone __instance)
        {
            return !(__instance is Zone_Lot);
        }
    }

    // Keeps the AI from picking a rent-free bed on someone else's lot, a bed a tax debtor can't afford,
    // or a soldiers' bed for anyone outside the Soldier caste. Sleeping, napping and relaxing in a bed
    // all claim it, and they find their bed through here.
    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.IsValidBedFor))]
    public static class Patch_RestUtility_IsValidBedFor
    {
        public static void Postfix(Thing bedThing, Pawn sleeper, ref bool __result)
        {
            if (!__result || !(bedThing is Building_Bed bed) || sleeper == null)
            {
                return;
            }
            if (RentUtility.SoldiersOnlyBlocks(sleeper, bed))
            {
                __result = false;
                return;
            }
            if (!sleeper.IsColonist || sleeper.IsPrisoner || sleeper.IsSlave)
            {
                return;
            }
            if (LotUtility.IsPrivateFrom(bed, sleeper) || RentUtility.DebtorCantAfford(sleeper, bed))
            {
                __result = false;
            }
        }
    }

    // The bed's "assign owner" dialog: a soldiers' bed can only be given to soldiers.
    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.CanAssignTo))]
    public static class Patch_CompAssignableToPawn_Bed_CanAssignTo
    {
        public static void Postfix(CompAssignableToPawn_Bed __instance, Pawn pawn, ref AcceptanceReport __result)
        {
            if (__result.Accepted && __instance.parent is Building_Bed bed && RentUtility.SoldiersOnlyBlocks(pawn, bed))
            {
                __result = "CW_SoldiersBedOnly".Translate();
            }
        }
    }
}
