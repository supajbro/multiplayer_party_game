using System.Collections.Generic;
using CouchGuys.ProceduralGeneration;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    public sealed class TrafficRoute
    {
        public readonly Vector3[] Waypoints;

        public TrafficRoute(Vector3[] waypoints)
        {
            Waypoints = waypoints;
        }
    }

    /// <summary>Immutable routes precomputed once from a generated neighbourhood.</summary>
    public sealed class TrafficLaneGraph
    {
        private readonly List<TrafficRoute> m_routes = new List<TrafficRoute>(4);
        private readonly Dictionary<GridCoordinate, GeneratedRoad> m_roads =
            new Dictionary<GridCoordinate, GeneratedRoad>();

        public IReadOnlyList<TrafficRoute> Routes => m_routes;

        public void Build(NeighbourhoodGenerator generator, TrafficSettings settings)
        {
            m_routes.Clear();
            m_roads.Clear();
            if (generator == null || settings == null || generator.TrafficRoadPortals.Count < 2)
            {
                return;
            }

            for (int index = 0; index < generator.GeneratedRoads.Count; index++)
            {
                GeneratedRoad road = generator.GeneratedRoads[index];
                if (road != null)
                {
                    m_roads[road.Coordinate] = road;
                }
            }

            List<GridCoordinate> portals = new List<GridCoordinate>(generator.TrafficRoadPortals.Count);
            for (int index = 0; index < generator.TrafficRoadPortals.Count; index++)
                portals.Add(generator.TrafficRoadPortals[index].Coordinate);

            HashSet<string> routeKeys = new HashSet<string>();
            for (int start = 0; start < portals.Count; start++)
            {
                for (int end = 0; end < portals.Count; end++)
                {
                    if (start == end) continue;

                    if (TryFindPath(portals[start], portals[end], out List<GridCoordinate> path))
                        AddRoute(path, settings, routeKeys);
                }
            }

            // Add trips through side streets and loops where the forward-first
            // driving rules permit them. Preserve the arrival heading at the join.
            List<GridCoordinate> coverageTargets = new List<GridCoordinate>(m_roads.Keys);
            coverageTargets.Sort((left, right) =>
            {
                int yComparison = left.Y.CompareTo(right.Y);
                return yComparison != 0 ? yComparison : left.X.CompareTo(right.X);
            });
            for (int targetIndex = 0; targetIndex < coverageTargets.Count; targetIndex++)
            {
                GridCoordinate target = coverageTargets[targetIndex];
                for (int startIndex = 0; startIndex < portals.Count; startIndex++)
                {
                    int endOffset = 1 + targetIndex % (portals.Count - 1);
                    GridCoordinate start = portals[startIndex];
                    GridCoordinate end = portals[(startIndex + endOffset) % portals.Count];
                    if (!TryFindPath(start, target, out List<GridCoordinate> outbound))
                        continue;
                    CardinalDirection? incoming = outbound.Count > 1
                        ? Direction(outbound[outbound.Count - 1] - outbound[outbound.Count - 2])
                        : null;
                    if (!TryFindPath(target, end, out List<GridCoordinate> inbound, incoming))
                        continue;

                    List<GridCoordinate> combined = new List<GridCoordinate>(
                        outbound.Count + inbound.Count - 1);
                    combined.AddRange(outbound);
                    for (int index = 1; index < inbound.Count; index++)
                        combined.Add(inbound[index]);
                    AddRoute(combined, settings, routeKeys);
                }
            }
        }

        public void Clear()
        {
            m_routes.Clear();
            m_roads.Clear();
        }

        private bool TryFindPath(
            GridCoordinate start,
            GridCoordinate end,
            out List<GridCoordinate> coordinates,
            CardinalDirection? incoming = null)
        {
            coordinates = null;
            // Heading is part of the search state: reaching a junction from another
            // road can permit a different exit. All searches still run only at build time.
            var initial = (Coordinate: start, Heading: incoming.HasValue ? (int)incoming.Value : -1);
            var frontier = new Queue<(GridCoordinate Coordinate, int Heading)>();
            var previous = new Dictionary<(GridCoordinate Coordinate, int Heading),
                (GridCoordinate Coordinate, int Heading)>();
            var visited = new HashSet<(GridCoordinate Coordinate, int Heading)> { initial };
            var destination = initial;
            bool found = false;
            frontier.Enqueue(initial);
            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (current.Coordinate == end)
                {
                    destination = current;
                    found = true;
                    break;
                }

                if (!m_roads.ContainsKey(current.Coordinate))
                {
                    continue;
                }

                bool straight = current.Heading >= 0 &&
                    CanTravel(current.Coordinate, (CardinalDirection)current.Heading);
                int reverse = current.Heading >= 0 ? (current.Heading + 2) % 4 : -1;
                bool hasTurn = current.Heading >= 0 &&
                    (CanTravel(current.Coordinate, (CardinalDirection)((current.Heading + 1) % 4)) ||
                     CanTravel(current.Coordinate, (CardinalDirection)((current.Heading + 3) % 4)));
                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    CardinalDirection direction = (CardinalDirection)directionIndex;
                    // Never trade a valid straight road for a shorter trip, and only
                    // reverse when there is no connected forward or side road.
                    if ((straight && directionIndex != current.Heading) ||
                        (directionIndex == reverse && (straight || hasTurn)) ||
                        !CanTravel(current.Coordinate, direction))
                    {
                        continue;
                    }

                    var neighbour = (Coordinate: current.Coordinate + Offset(direction), Heading: directionIndex);
                    if (!visited.Add(neighbour))
                    {
                        continue;
                    }

                    previous[neighbour] = current;
                    frontier.Enqueue(neighbour);
                }
            }

            if (!found)
            {
                return false;
            }

            coordinates = new List<GridCoordinate>();
            var cursor = destination;
            coordinates.Add(cursor.Coordinate);
            while (cursor != initial)
            {
                cursor = previous[cursor];
                coordinates.Add(cursor.Coordinate);
            }
            coordinates.Reverse();

            return coordinates.Count > 0;
        }

        private bool CanTravel(GridCoordinate coordinate, CardinalDirection direction)
        {
            return m_roads.TryGetValue(coordinate, out GeneratedRoad road) &&
                   (road.Connections & Connection(direction)) != 0 &&
                   m_roads.TryGetValue(coordinate + Offset(direction), out GeneratedRoad neighbour) &&
                   (neighbour.Connections & Connection((CardinalDirection)(((int)direction + 2) % 4))) != 0;
        }

        private static CardinalDirection Direction(GridCoordinate offset) => offset.X > 0
            ? CardinalDirection.East
            : offset.X < 0 ? CardinalDirection.West
            : offset.Y > 0 ? CardinalDirection.North : CardinalDirection.South;

        private void AddRoute(
            List<GridCoordinate> coordinates,
            TrafficSettings settings,
            HashSet<string> routeKeys)
        {
            if (coordinates == null || coordinates.Count < 2) return;

            string key = string.Join(";", coordinates);
            if (!routeKeys.Add(key)) return;

            float laneOffset = Mathf.Max(0.8f,
                Vector3.Distance(m_roads[coordinates[0]].transform.position,
                    m_roads[coordinates[1]].transform.position) * settings.LaneOffsetFraction);
            float side = settings.DrivingSide == TrafficDrivingSide.AustralianLeft ? -1f : 1f;
            Vector3[] waypoints = new Vector3[coordinates.Count];
            for (int index = 0; index < coordinates.Count; index++)
            {
                Vector3 centre = m_roads[coordinates[index]].transform.position;
                Vector3 incoming = index > 0
                    ? centre - m_roads[coordinates[index - 1]].transform.position
                    : m_roads[coordinates[1]].transform.position - centre;
                Vector3 outgoing = index + 1 < coordinates.Count
                    ? m_roads[coordinates[index + 1]].transform.position - centre
                    : incoming;
                incoming = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
                outgoing = Vector3.ProjectOnPlane(outgoing, Vector3.up).normalized;
                Vector3 travel = (incoming + outgoing).normalized;
                if (travel.sqrMagnitude < 0.1f) travel = outgoing;
                Vector3 right = Vector3.Cross(Vector3.up, travel).normalized;
                waypoints[index] = centre + right * (side * laneOffset) + Vector3.up * 0.12f;
            }

            m_routes.Add(new TrafficRoute(waypoints));
        }

        private static GridCoordinate Offset(CardinalDirection direction) => direction switch
        {
            CardinalDirection.North => new GridCoordinate(0, 1),
            CardinalDirection.East => new GridCoordinate(1, 0),
            CardinalDirection.South => new GridCoordinate(0, -1),
            _ => new GridCoordinate(-1, 0)
        };

        private static RoadConnections Connection(CardinalDirection direction) => direction switch
        {
            CardinalDirection.North => RoadConnections.North,
            CardinalDirection.East => RoadConnections.East,
            CardinalDirection.South => RoadConnections.South,
            _ => RoadConnections.West
        };
    }
}
