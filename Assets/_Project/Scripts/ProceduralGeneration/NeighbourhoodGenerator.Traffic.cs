using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        private readonly List<TrafficRoadPortal> m_trafficRoadPortals = new List<TrafficRoadPortal>(2);
        private readonly List<GridCoordinate> m_trafficGatewayRoads = new List<GridCoordinate>(8);
        private readonly List<GridCoordinate> m_trafficPortalCoordinates = new List<GridCoordinate>(2);

        public IReadOnlyList<TrafficRoadPortal> TrafficRoadPortals => m_trafficRoadPortals;

        public bool IsTrafficGatewayRoad(GridCoordinate coordinate) =>
            m_trafficGatewayRoads.Contains(coordinate);

        public bool IsTunnelApproachRoad(GridCoordinate coordinate)
        {
            if (IsTrafficGatewayRoad(coordinate))
            {
                return true;
            }

            if (!HasSummitExit)
            {
                return false;
            }

            Vector2Int local = GridToLocal(coordinate);
            int summitLocalY = m_gridHeight * (m_blockHeight + 1) +
                               m_landmarkRoads.UphillLengthTiles;
            int clearanceTiles = Mathf.Max(1, Mathf.CeilToInt(
                (m_borderSettings.TunnelDepth * 0.5f +
                 m_borderSettings.TunnelClearance) / m_roadTileSize));
            return local.x == 0 && local.y >= summitLocalY - clearanceTiles;
        }

        private void CreateTrafficBoundaryExtensions()
        {
            m_trafficRoadPortals.Clear();
            m_trafficGatewayRoads.Clear();
            m_trafficPortalCoordinates.Clear();

            int maximumLocalY = m_gridHeight * (m_blockHeight + 1);
            GridCoordinate forward = DirectionOffset(m_startDirection);
            List<GridCoordinate> candidates = new List<GridCoordinate>();
            for (int index = 0; index < m_roadOrder.Count; index++)
            {
                GridCoordinate coordinate = m_roadOrder[index];
                Vector2Int local = GridToLocal(coordinate);
                // A boundary road running sideways along the edge is not an exit. Requiring
                // an inward neighbour limits tunnels to genuine roads that approach the edge.
                // The lateral clearance also reserves the full summit road/embankment envelope;
                // merely excluding x == 0 allowed the adjacent tile's tunnel to clip it.
                if (local.y == maximumLocalY &&
                    local.x != 0 &&
                    m_roadCells.Contains(coordinate - forward) &&
                    HasStandardTunnelClearanceFromSummit(local.x))
                {
                    candidates.Add(coordinate);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            candidates.Sort((first, second) =>
                Mathf.Abs(GridToLocal(first).x).CompareTo(Mathf.Abs(GridToLocal(second).x)));
            GridCoordinate primary = candidates[0];
            GridCoordinate secondary = primary;
            int greatestSeparation = 0;
            for (int index = 1; index < candidates.Count; index++)
            {
                int separation = Mathf.Abs(GridToLocal(candidates[index]).x - GridToLocal(primary).x);
                if (separation > greatestSeparation)
                {
                    greatestSeparation = separation;
                    secondary = candidates[index];
                }
            }

            ExtendTrafficGateway(primary, maximumLocalY);
            float requiredPortalSpacing = GetRequiredStandardTunnelSpacing();
            if (secondary != primary &&
                greatestSeparation * m_roadTileSize >= requiredPortalSpacing)
            {
                ExtendTrafficGateway(secondary, maximumLocalY);
            }
        }

        private bool HasStandardTunnelClearanceFromSummit(int localX)
        {
            if (!HasSummitExit)
            {
                return true;
            }

            return Mathf.Abs(localX * m_roadTileSize) >= GetSummitTunnelExclusionHalfWidth();
        }

        private float GetSummitTunnelExclusionHalfWidth()
        {
            float summitCorridorHalfWidth = m_terrainSettings.RoadHalfWidth +
                                            Mathf.Max(
                                                m_terrainSettings.RoadShoulderWidth,
                                                m_landmarkRoads.EmbankmentBlendWidth);
            return summitCorridorHalfWidth + GetTunnelOuterHalfWidth() +
                   Mathf.Max(1f, m_borderSettings.ColliderThickness);
        }

        private float GetRequiredStandardTunnelSpacing() =>
            GetTunnelOuterHalfWidth() * 2f +
            Mathf.Max(1f, m_borderSettings.ColliderThickness);

        private float GetTunnelOuterHalfWidth()
        {
            float pillarWidth = Mathf.Max(1.2f, m_borderSettings.TunnelWidth * 0.16f);
            return m_borderSettings.TunnelWidth * 0.5f + pillarWidth;
        }

        private void ExtendTrafficGateway(GridCoordinate boundaryRoad, int maximumLocalY)
        {
            float borderDistance = m_terrainSettings.MapBorder +
                                   (m_borderSettings.Enabled ? m_borderSettings.BorderWidth : 0f);
            int extensionCount = Mathf.Max(1, Mathf.CeilToInt(
                (borderDistance + m_roadTileSize * 0.6f) / m_roadTileSize));
            GridCoordinate forward = DirectionOffset(m_startDirection);
            m_trafficGatewayRoads.Add(boundaryRoad);
            GridCoordinate endpoint = boundaryRoad;
            int localX = GridToLocal(boundaryRoad).x;
            for (int step = 1; step <= extensionCount; step++)
            {
                endpoint += forward;
                AddRoad(endpoint, CalculateProgressiveElevation(localX, maximumLocalY + step));
                m_trafficGatewayRoads.Add(endpoint);
            }

            m_trafficPortalCoordinates.Add(endpoint);
        }

        private void FinaliseTrafficBoundaryConnections()
        {
            m_trafficRoadPortals.Clear();
            GridCoordinate outwardOffset = DirectionOffset(m_startDirection);
            Vector3 outward = new Vector3(outwardOffset.X, 0f, outwardOffset.Y).normalized;
            for (int index = 0; index < m_trafficPortalCoordinates.Count; index++)
            {
                GridCoordinate coordinate = m_trafficPortalCoordinates[index];
                if (!m_logicalRoads.TryGetValue(coordinate, out RoadConnections connections))
                {
                    continue;
                }

                m_logicalRoads[coordinate] = connections | DirectionConnection(m_startDirection);
                m_trafficRoadPortals.Add(new TrafficRoadPortal(
                    coordinate,
                    m_startDirection,
                    GridToWorld(coordinate),
                    outward));
            }
        }

        private void ClearTrafficBoundaryData()
        {
            m_trafficRoadPortals.Clear();
            m_trafficGatewayRoads.Clear();
            m_trafficPortalCoordinates.Clear();
        }
    }
}
