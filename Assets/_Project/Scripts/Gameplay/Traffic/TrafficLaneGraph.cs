using System.Collections.Generic;
using CouchGuys.ProceduralGeneration;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    public sealed class TrafficRoute
    {
        public readonly Vector3[] Waypoints;
        public readonly GridCoordinate[] Coordinates;

        public TrafficRoute(Vector3[] waypoints, GridCoordinate[] coordinates = null)
        {
            Waypoints = waypoints;
            Coordinates = coordinates;
        }
    }

    /// <summary>Cached road connectivity and baseline routes, with individual randomized trips.</summary>
    public sealed class TrafficLaneGraph
    {
        private readonly List<TrafficRoute> m_routes = new List<TrafficRoute>(4);
        private readonly Dictionary<GridCoordinate, GeneratedRoad> m_roads =
            new Dictionary<GridCoordinate, GeneratedRoad>();
        private readonly List<GridCoordinate> m_portals = new List<GridCoordinate>();

        public IReadOnlyList<TrafficRoute> Routes => m_routes;
        public string BuildFailureReason { get; private set; }

        public void Build(NeighbourhoodGenerator generator, TrafficSettings settings)
        {
            Clear();
            if (generator == null || settings == null)
            {
                BuildFailureReason = "Missing neighbourhood generator or traffic settings.";
                return;
            }
            if (generator.TrafficRoadPortals.Count < 2)
            {
                BuildFailureReason = "Traffic requires at least two tunnel portals.";
                return;
            }

            for (int index = 0; index < generator.GeneratedRoads.Count; index++)
            {
                GeneratedRoad road = generator.GeneratedRoads[index];
                // The summit corridor is reserved for the flying couch. Excluding it
                // here also keeps its waypoints out of traffic and rogue-car spawns.
                if (road != null && road.Role != GeneratedRoadRole.UphillExit)
                {
                    m_roads[road.Coordinate] = road;
                }
            }

            List<GridCoordinate> portals = new List<GridCoordinate>(generator.TrafficRoadPortals.Count);
            for (int index = 0; index < generator.TrafficRoadPortals.Count; index++)
            {
                GridCoordinate coordinate = generator.TrafficRoadPortals[index].Coordinate;
                if (!m_roads.ContainsKey(coordinate))
                {
                    BuildFailureReason = $"Tunnel portal {coordinate} has no generated road.";
                    return;
                }
                if (!portals.Contains(coordinate)) portals.Add(coordinate);
            }
            if (portals.Count < 2)
            {
                BuildFailureReason = "Traffic requires two distinct tunnel portals.";
                return;
            }
            m_portals.AddRange(portals);

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

            if (m_routes.Count == 0)
            {
                BuildFailureReason = "No connected driving route exists between the tunnel portals, even allowing junction turns.";
                return;
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
                    if (!TryFindStrictPath(start, target, out List<GridCoordinate> outbound))
                        continue;
                    CardinalDirection? incoming = outbound.Count > 1
                        ? Direction(outbound[outbound.Count - 1] - outbound[outbound.Count - 2])
                        : null;
                    if (!TryFindStrictPath(target, end, out List<GridCoordinate> inbound, incoming))
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
            m_portals.Clear();
            BuildFailureReason = null;
        }

        public bool TryCreateRandomRoute(TrafficRoute entryRoute, TrafficSettings settings,
            System.Random random, out TrafficRoute route)
        {
            route = null;
            if (entryRoute?.Coordinates == null || entryRoute.Coordinates.Length < 2 ||
                settings == null || random == null || m_portals.Count < 2) return false;

            var coordinates = new List<GridCoordinate> { entryRoute.Coordinates[0], entryRoute.Coordinates[1] };
            var visits = new Dictionary<(GridCoordinate From, GridCoordinate To), int>();
            var candidates = new List<GridCoordinate>(3);
            int minimum = System.Math.Max(2, settings.MinimumRandomRouteTiles);
            int maximum = System.Math.Max(minimum, settings.MaximumRandomRouteTiles);
            int length = random.Next(minimum, maximum + 1);
            for (int step = 1; step < length; step++)
            {
                GridCoordinate current = coordinates[coordinates.Count - 1];
                GridCoordinate previous = coordinates[coordinates.Count - 2];
                candidates.Clear();
                int fewestVisits = int.MaxValue;
                for (int direction = 0; direction < 4; direction++)
                {
                    if (!CanTravel(current, (CardinalDirection)direction)) continue;
                    GridCoordinate next = current + Offset((CardinalDirection)direction);
                    if (next == previous) continue;
                    visits.TryGetValue((current, next), out int count);
                    if (count < fewestVisits) { candidates.Clear(); fewestVisits = count; }
                    if (count == fewestVisits) candidates.Add(next);
                }
                // At junctions, straight and turns are equally eligible. Prefer
                // unexplored edges so cars spread out instead of circling one block.
                GridCoordinate chosen = candidates.Count > 0
                    ? candidates[random.Next(candidates.Count)]
                    : previous;
                visits.TryGetValue((current, chosen), out int used);
                visits[(current, chosen)] = used + 1;
                coordinates.Add(chosen);
            }

            GridCoordinate last = coordinates[coordinates.Count - 1];
            CardinalDirection heading = Direction(last - coordinates[coordinates.Count - 2]);
            int firstExit = random.Next(m_portals.Count);
            for (int index = 0; index < m_portals.Count; index++)
            {
                GridCoordinate exit = m_portals[(firstExit + index) % m_portals.Count];
                if (exit == coordinates[0]) continue;
                if (!TryFindTurningPath(last, exit, out var finish, heading)) continue;
                for (int point = 1; point < finish.Count; point++) coordinates.Add(finish[point]);
                route = CreateRoute(coordinates, settings);
                return true;
            }
            return false;
        }

        private bool TryFindPath(
            GridCoordinate start,
            GridCoordinate end,
            out List<GridCoordinate> coordinates,
            CardinalDirection? incoming = null)
        {
            return TryFindStrictPath(start, end, out coordinates, incoming) ||
                   TryFindTurningPath(start, end, out coordinates, incoming);
        }

        // Fallback only for portal trips. Keep the existing cheap straight-first
        // searches for optional coverage loops so generation cost stays bounded.
        private bool TryFindTurningPath(
            GridCoordinate start,
            GridCoordinate end,
            out List<GridCoordinate> coordinates,
            CardinalDirection? incoming = null)
        {
            coordinates = null;
            var initial = (Coordinate: start, Heading: incoming.HasValue ? (int)incoming.Value : -1);
            var costs = new Dictionary<(GridCoordinate Coordinate, int Heading), (int Turns, int Steps)>();
            var previous = new Dictionary<(GridCoordinate Coordinate, int Heading),
                (GridCoordinate Coordinate, int Heading)>();
            var frontier = new SortedSet<(int Turns, int Steps, int X, int Y, int Heading)>();
            costs[initial] = (0, 0);
            frontier.Add((0, 0, start.X, start.Y, initial.Heading));
            while (frontier.Count > 0)
            {
                var entry = frontier.Min;
                frontier.Remove(entry);
                var current = (Coordinate: new GridCoordinate(entry.X, entry.Y), Heading: entry.Heading);
                if (costs[current] != (entry.Turns, entry.Steps)) continue;
                if (current.Coordinate == end)
                {
                    coordinates = new List<GridCoordinate> { current.Coordinate };
                    while (current != initial)
                    {
                        current = previous[current];
                        coordinates.Add(current.Coordinate);
                    }
                    coordinates.Reverse();
                    return true;
                }

                int reverse = current.Heading >= 0 ? (current.Heading + 2) % 4 : -1;
                bool deadEnd = true;
                for (int direction = 0; direction < 4; direction++)
                    if (direction != reverse && CanTravel(current.Coordinate, (CardinalDirection)direction))
                        deadEnd = false;
                for (int direction = 0; direction < 4; direction++)
                {
                    if ((direction == reverse && !deadEnd) ||
                        !CanTravel(current.Coordinate, (CardinalDirection)direction)) continue;
                    var next = (Coordinate: current.Coordinate + Offset((CardinalDirection)direction), Heading: direction);
                    int turns = entry.Turns + (current.Heading >= 0 && direction != current.Heading ? 1 : 0);
                    int steps = entry.Steps + 1;
                    if (costs.TryGetValue(next, out var cost) &&
                        (cost.Turns < turns || (cost.Turns == turns && cost.Steps <= steps))) continue;
                    costs[next] = (turns, steps);
                    previous[next] = current;
                    frontier.Add((turns, steps, next.Coordinate.X, next.Coordinate.Y, direction));
                }
            }
            return false;
        }

        private bool TryFindStrictPath(
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

            m_routes.Add(CreateRoute(coordinates, settings));
        }

        private TrafficRoute CreateRoute(List<GridCoordinate> coordinates, TrafficSettings settings)
        {
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

            return new TrafficRoute(waypoints, coordinates.ToArray());
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
