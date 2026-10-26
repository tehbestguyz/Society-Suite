using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace GuardPosts
{
    public class Building_GuardSpot : Building
    {
        public int patrolOrder;

        private GuardSpotExtension Ext => def.GetModExtension<GuardSpotExtension>();

        public GuardSpotKind Kind => Ext.kind;

        public GuardColor SpotColor => Ext.color;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad && Kind == GuardSpotKind.Patrol)
            {
                // New waypoints go on the end of their route.
                List<Building_GuardSpot> route = GuardUtility.PatrolRoute(map, SpotColor);
                route.Remove(this);
                patrolOrder = route.Count == 0 ? 0 : route.Max(s => s.patrolOrder) + 1;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref patrolOrder, "patrolOrder");
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (GuardUtility.ShouldDrawSpot(this))
            {
                base.DrawAt(drawLoc, flip);
            }
        }

        public override void DrawGUIOverlay()
        {
            if (Kind != GuardSpotKind.Patrol || !GuardUtility.ShouldDrawSpot(this))
            {
                return;
            }
            int index = GuardUtility.PatrolRoute(Map, SpotColor).IndexOf(this);
            GenMapUI.DrawThingLabel(this, (index + 1).ToString(), GuardUtility.ColorOf(SpotColor));
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (Kind != GuardSpotKind.Patrol)
            {
                return;
            }
            List<Building_GuardSpot> route = GuardUtility.PatrolRoute(Map, SpotColor);
            if (route.Count < 2)
            {
                return;
            }
            float altitude = AltitudeLayer.MetaOverlays.AltitudeFor();
            SimpleColor lineColor = GuardUtility.SimpleColorOf(SpotColor);
            bool pingPong = GameComponent_Guards.Instance?.IsPingPong(SpotColor) ?? false;
            int segments = pingPong ? route.Count - 1 : route.Count;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = route[i].TrueCenter();
                Vector3 b = route[(i + 1) % route.Count].TrueCenter();
                a.y = b.y = altitude;
                GenDraw.DrawLineBetween(a, b, lineColor);
            }
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            string color = GuardUtility.ColorLabel(SpotColor);
            if (Kind == GuardSpotKind.Patrol)
            {
                List<Building_GuardSpot> route = GuardUtility.PatrolRoute(Map, SpotColor);
                bool pingPong = GameComponent_Guards.Instance?.IsPingPong(SpotColor) ?? false;
                sb.Append("GP_InspectPatrolStop".Translate(route.IndexOf(this) + 1, route.Count, color,
                    (pingPong ? "GP_RoutePingPong" : "GP_RouteLoop").Translate()));
            }
            else
            {
                Pawn holder = Map.mapPawns.FreeColonistsSpawned
                    .FirstOrDefault(p => p.CurJobDef == GuardDefOf.GP_GuardPost && p.CurJob.targetA.Thing == this);
                sb.Append(holder != null
                    ? "GP_InspectPostHeldBy".Translate(holder.LabelShort)
                    : "GP_InspectPostEmpty".Translate());
            }
            return sb.ToString().TrimEndNewlines();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            GameComponent_Guards comp = GameComponent_Guards.Instance;
            if (comp == null)
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                defaultLabel = "GP_ShowSpots".Translate(),
                defaultDesc = "GP_ShowSpotsDesc".Translate(),
                icon = GuardTextures.ToggleVisibility,
                isActive = () => comp.spotsVisible,
                toggleAction = () => comp.spotsVisible = !comp.spotsVisible
            };

            if (Kind != GuardSpotKind.Patrol)
            {
                yield break;
            }

            List<Building_GuardSpot> route = GuardUtility.PatrolRoute(Map, SpotColor);
            int index = route.IndexOf(this);

            Command_Action earlier = new Command_Action
            {
                defaultLabel = "GP_MoveEarlier".Translate(),
                defaultDesc = "GP_MoveEarlierDesc".Translate(),
                icon = TexButton.ReorderUp,
                action = () => SwapWithNeighbor(-1)
            };
            if (index <= 0)
            {
                earlier.Disable("GP_AlreadyFirst".Translate());
            }
            yield return earlier;

            Command_Action later = new Command_Action
            {
                defaultLabel = "GP_MoveLater".Translate(),
                defaultDesc = "GP_MoveLaterDesc".Translate(),
                icon = TexButton.ReorderDown,
                action = () => SwapWithNeighbor(1)
            };
            if (index < 0 || index >= route.Count - 1)
            {
                later.Disable("GP_AlreadyLast".Translate());
            }
            yield return later;

            bool pingPong = comp.IsPingPong(SpotColor);
            GuardColor color = SpotColor;
            yield return new Command_Action
            {
                defaultLabel = (pingPong ? "GP_RoutePingPong" : "GP_RouteLoop").Translate().CapitalizeFirst(),
                defaultDesc = "GP_RouteModeDesc".Translate(GuardUtility.ColorLabel(color)),
                icon = pingPong ? GuardTextures.RoutePingPong : GuardTextures.RouteLoop,
                defaultIconColor = GuardUtility.ColorOf(color),
                action = () => comp.SetPingPong(color, !pingPong)
            };
        }

        private void SwapWithNeighbor(int offset)
        {
            List<Building_GuardSpot> route = GuardUtility.PatrolRoute(Map, SpotColor);
            int index = route.IndexOf(this);
            int other = index + offset;
            if (index < 0 || other < 0 || other >= route.Count)
            {
                return;
            }
            // Renumber first so ties from old saves or odd placements can't make the swap a no-op.
            for (int i = 0; i < route.Count; i++)
            {
                route[i].patrolOrder = i;
            }
            route[index].patrolOrder = other;
            route[other].patrolOrder = index;
        }
    }
}
