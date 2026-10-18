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

        private void CreateTrafficBoundaryExtensions()
        {
            m_trafficRoadPortals.Clear();
            m_trafficGatewayRoads.Clear();
            m_trafficPortalCoordinates.Clear();

            int maximumLocalY = m_gridHeight * (m_blockHeight + 1);
            List<GridCoordinate> candidates = new List<GridCoordinate>();
            for (int index = 0; index < m_roadOrder.Count; index++)
            {
                GridCoordinate coordinate = m_roadOrder[index];
                Vector2Int local = GridToLocal(coordinate);
                if (local.y == maximumLocalY && local.x != 0)
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
            if (secondary != primary && greatestSeparation >= 2)
            {
                ExtendTrafficGateway(secondary, maximumLocalY);
            }
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
