using System.Collections.Generic;
using CouchGuys.ProceduralGeneration;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    internal static class GetawayRouteFollower
    {
        internal static bool ReachedWaypoint(IReadOnlyList<Vector3> route, int index, Vector3 position,
            float radius, float passDistance)
        {
            Vector3 offset = Vector3.ProjectOnPlane(position - route[index], Vector3.up);
            if (offset.sqrMagnitude <= radius * radius) return true;
            if (index == route.Count - 1) return false; // Stop at the actual parking/tunnel endpoint.
            Vector3 travel = Vector3.ProjectOnPlane(index > 0
                ? route[index] - route[index - 1] : route[1] - route[0], Vector3.up).normalized;
            float along = Vector3.Dot(offset, travel);
            Vector3 lateral = offset - travel * along;
            return along >= 0f && lateral.sqrMagnitude <= passDistance * passDistance;
        }

        internal static Vector3 SteeringTarget(IReadOnlyList<Vector3> route, int index, Vector3 position,
            float lookAhead)
        {
            Vector3 target = route[index];
            float remaining = lookAhead - Vector3.ProjectOnPlane(target - position, Vector3.up).magnitude;
            while (remaining > 0f && index + 1 < route.Count)
            {
                Vector3 next = route[++index];
                float distance = Vector3.ProjectOnPlane(next - target, Vector3.up).magnitude;
                if (distance > remaining) return Vector3.Lerp(target, next, remaining / distance);
                remaining -= distance;
                target = next;
            }
            return target;
        }
    }

    // Cached generated road connectivity; no navigation over houses or arbitrary straight-line shortcuts.
    internal sealed class GetawayRoadRouter
    {
        private readonly Dictionary<GridCoordinate, GeneratedRoad> m_roads = new();
        private readonly List<TrafficRoadPortal> m_portals = new();
        private readonly Dictionary<(GridCoordinate, GridCoordinate), bool> m_clearance = new();
        private Transform m_vehicle;
        private Vector3 m_clearanceHalfExtents;
        private float m_clearanceHeight;

        internal void ConfigureVehicleClearance(Transform vehicle, Vector3 halfExtents, float height)
        {
            m_vehicle = vehicle;
            m_clearanceHalfExtents = halfExtents;
            m_clearanceHeight = height;
            RefreshClearance();
        }

        internal GetawayRoadRouter ForVehicle(Transform vehicle, Vector3 halfExtents, float height)
        {
            // Departing trucks can overlap the next theft attempt. Their collision filters
            // and query caches must not replace the active truck's configuration.
            var router = new GetawayRoadRouter();
            foreach (var road in m_roads) router.m_roads.Add(road.Key, road.Value);
            router.m_portals.AddRange(m_portals);
            router.ConfigureVehicleClearance(vehicle, halfExtents, height);
            return router;
        }

        internal void RefreshClearance() => m_clearance.Clear();

        internal bool SegmentClear(Vector3 start, Vector3 end)
        {
            if (m_vehicle == null) return true;
            Vector3 direction = end - start;
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            Quaternion rotation = horizontal.sqrMagnitude > .001f ? Quaternion.LookRotation(horizontal) : m_vehicle.rotation;
            Vector3 centre = start + Vector3.up * m_clearanceHeight;
            bool Obstruction(Collider hit) => !hit.transform.IsChildOf(m_vehicle) &&
                !TruckController.IsGroundSurface(hit) &&
                hit.GetComponentInParent<CouchGuys.Player.PlayerHealth>() == null &&
                hit.GetComponentInParent<CouchGuys.Gameplay.Enemies.ThiefHealth>() == null &&
                hit.GetComponentInParent<CouchGuys.Gameplay.Couch.CouchCarryController>() == null &&
                (hit.attachedRigidbody == null || hit.attachedRigidbody.isKinematic);
            foreach (Collider hit in Physics.OverlapBox(centre, m_clearanceHalfExtents, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (Obstruction(hit)) return false;
            foreach (RaycastHit hit in Physics.BoxCastAll(centre, m_clearanceHalfExtents, direction.normalized,
                rotation, direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (Obstruction(hit.collider)) return false;
            return true;
        }

        private bool EdgeClear(GridCoordinate start, GridCoordinate end)
        {
            var key = (start, end);
            if (!m_clearance.TryGetValue(key, out bool clear))
            {
                clear = SegmentClear(m_roads[start].transform.position, m_roads[end].transform.position);
                m_clearance[key] = clear;
                m_clearance[(end, start)] = clear;
            }
            return clear;
        }
        private static readonly GridCoordinate[] Offsets =
        {
            new GridCoordinate(0, 1), new GridCoordinate(1, 0),
            new GridCoordinate(0, -1), new GridCoordinate(-1, 0)
        };
        internal IEnumerable<GeneratedRoad> Roads => m_roads.Values;
        internal IReadOnlyList<TrafficRoadPortal> Portals => m_portals;

        internal void Build(NeighbourhoodGenerator generator)
        {
            m_roads.Clear();
            m_portals.Clear();
            foreach (GeneratedRoad road in generator.GeneratedRoads)
                if (road != null) m_roads[road.Coordinate] = road;
            foreach (TrafficRoadPortal portal in generator.TrafficRoadPortals) m_portals.Add(portal);
        }

        internal GeneratedRoad Nearest(Vector3 position)
        {
            GeneratedRoad best = null;
            float distance = float.PositiveInfinity;
            foreach (GeneratedRoad road in m_roads.Values)
            {
                float candidate = (road.transform.position - position).sqrMagnitude;
                if (candidate < distance) { best = road; distance = candidate; }
            }
            return best;
        }

        internal bool TryRoute(Vector3 start, GridCoordinate end, out List<Vector3> route)
        {
            route = null;
            GeneratedRoad source = Nearest(start);
            if (source == null || !m_roads.ContainsKey(end)) return false;
            var queue = new Queue<GridCoordinate>();
            var previous = new Dictionary<GridCoordinate, GridCoordinate>();
            var visited = new HashSet<GridCoordinate> { source.Coordinate };
            queue.Enqueue(source.Coordinate);
            while (queue.Count > 0)
            {
                GridCoordinate current = queue.Dequeue();
                if (current == end)
                {
                    route = new List<Vector3>();
                    while (true)
                    {
                        route.Add(m_roads[current].transform.position);
                        if (current == source.Coordinate) break;
                        current = previous[current];
                    }
                    route.Reverse();
                    return true;
                }
                for (int i = 0; i < Offsets.Length; i++)
                {
                    GridCoordinate neighbour = current + Offsets[i];
                    if ((m_roads[current].Connections & (RoadConnections)(1 << i)) == 0 ||
                        !m_roads.TryGetValue(neighbour, out GeneratedRoad next) ||
                        (next.Connections & (RoadConnections)(1 << ((i + 2) % 4))) == 0 ||
                        !EdgeClear(current, neighbour) || !visited.Add(neighbour)) continue;
                    previous[neighbour] = current;
                    queue.Enqueue(neighbour);
                }
            }
            return false;
        }

        internal static float Length(Vector3 start, List<Vector3> route)
        {
            float length = 0f;
            foreach (Vector3 point in route) { length += Vector3.Distance(start, point); start = point; }
            return length;
        }

        internal bool TryForwardRoute(Vector3 start, Vector3 forward, GridCoordinate end, out List<Vector3> route)
        {
            route = null;
            GeneratedRoad source = Nearest(start);
            if (source == null || !m_roads.ContainsKey(end)) return false;
            int heading = 0;
            float bestDot = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                float dot = Vector3.Dot(forward, new Vector3(Offsets[i].X, 0, Offsets[i].Y));
                if (dot > bestDot) { heading = i; bestDot = dot; }
            }
            var initial = (Coordinate: source.Coordinate, Heading: heading);
            var queue = new Queue<(GridCoordinate Coordinate, int Heading)>();
            var previous = new Dictionary<(GridCoordinate Coordinate, int Heading), (GridCoordinate Coordinate, int Heading)>();
            var visited = new HashSet<(GridCoordinate Coordinate, int Heading)> { initial };
            queue.Enqueue(initial);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Coordinate == end)
                {
                    route = new List<Vector3>();
                    while (true)
                    {
                        route.Add(m_roads[current.Coordinate].transform.position);
                        if (current == initial) break;
                        current = previous[current];
                    }
                    route.Reverse();
                    return true;
                }
                for (int i = 0; i < 4; i++)
                {
                    if (i == (current.Heading + 2) % 4) continue;
                    if (current == initial && Vector3.Dot(forward,
                        new Vector3(Offsets[i].X, 0f, Offsets[i].Y)) < -.1f) continue;
                    GridCoordinate coordinate = current.Coordinate + Offsets[i];
                    if ((m_roads[current.Coordinate].Connections & (RoadConnections)(1 << i)) == 0 ||
                        !m_roads.TryGetValue(coordinate, out var road) ||
                        (road.Connections & (RoadConnections)(1 << ((i + 2) % 4))) == 0 ||
                        !EdgeClear(current.Coordinate, coordinate)) continue;
                    var next = (Coordinate: coordinate, Heading: i);
                    if (!visited.Add(next)) continue;
                    previous[next] = current;
                    queue.Enqueue(next);
                }
            }
            return false; // The motor can make a bounded three-point recovery at a dead end.
        }
    }
}
