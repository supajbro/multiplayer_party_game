using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.ProceduralGeneration;
using NUnit.Framework;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class TrafficRoutingTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject m_root;

        [TearDown]
        public void TearDown()
        {
            if (m_root != null) UnityEngine.Object.DestroyImmediate(m_root);
        }

        [Test]
        public void IndividualCarsExploreDifferentConnectedTripsAndFinishAtAnExit()
        {
            var cells = new List<(int X, int Y)> { (0, -1), (4, 5) };
            for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++) cells.Add((x, y));
            var graph = CreateGraph(cells.ToArray());
            var portals = (List<GridCoordinate>)typeof(TrafficLaneGraph)
                .GetField("m_portals", Private).GetValue(graph);
            portals.Add(new GridCoordinate(0, -1));
            portals.Add(new GridCoordinate(4, 5));
            var start = new[] { new GridCoordinate(0, -1), new GridCoordinate(0, 0) };
            var template = new TrafficRoute(new Vector3[2], start);
            var settings = ScriptableObject.CreateInstance<TrafficSettings>();
            try
            {
                var signatures = new HashSet<string>();
                for (int seed = 0; seed < 32; seed++)
                {
                    Assert.That(graph.TryCreateRandomRoute(template, settings, new System.Random(seed), out var route), Is.True);
                    var coordinates = route.Coordinates;
                    Assert.That(coordinates[0], Is.EqualTo(start[0]));
                    Assert.That(coordinates[1], Is.EqualTo(start[1]));
                    Assert.That(coordinates[coordinates.Length - 1], Is.EqualTo(portals[1]));
                    Assert.That(coordinates.Length > settings.MinimumRandomRouteTiles, Is.True);
                    signatures.Add(string.Join(";", coordinates));
                    for (int i = 1; i < coordinates.Length; i++)
                    {
                        GridCoordinate delta = coordinates[i] - coordinates[i - 1];
                        Assert.That(System.Math.Abs(delta.X) + System.Math.Abs(delta.Y), Is.EqualTo(1));
                        Assert.That(cells.Contains((coordinates[i].X, coordinates[i].Y)), Is.True);
                        if (i >= 2 && coordinates[i] == coordinates[i - 2])
                        {
                            GridCoordinate at = coordinates[i - 1];
                            int neighbours = 0;
                            foreach (var cell in cells)
                                if (System.Math.Abs(cell.X - at.X) + System.Math.Abs(cell.Y - at.Y) == 1) neighbours++;
                            Assert.That(neighbours, Is.EqualTo(1));
                        }
                    }
                }
                Assert.That(signatures.Count > 16, Is.True);
                Assert.That(template.Coordinates.Length, Is.EqualTo(2));
                Assert.That(graph.TryCreateRandomRoute(template, settings, new System.Random(7), out var first), Is.True);
                Assert.That(graph.TryCreateRandomRoute(template, settings, new System.Random(7), out var second), Is.True);
                Assert.That(first.Coordinates, Is.EqualTo(second.Coordinates));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        [Test]
        public void UphillExitIsExcludedFromRoutesAndSpawnWaypoints()
        {
            var graph = CreateGraph(new[] { (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0),
                (0, 1), (0, 2), (0, 3) });
            var roads = (Dictionary<GridCoordinate, GeneratedRoad>)typeof(TrafficLaneGraph)
                .GetField("m_roads", Private).GetValue(graph);
            for (int y = 1; y <= 3; y++)
            {
                var road = roads[new GridCoordinate(0, y)];
                // Include a flat summit tile: eligibility depends on road purpose,
                // rather than its current slope or elevation.
                road.Initialise(road.Coordinate, road.Connections, SuburbZone.Easy,
                    GeneratedRoadRole.UphillExit, 0f, 0f);
            }

            var generator = m_root.AddComponent<NeighbourhoodGenerator>();
            var generatedRoads = (List<GeneratedRoad>)typeof(NeighbourhoodGenerator)
                .GetField("m_generatedRoads", Private).GetValue(generator);
            generatedRoads.AddRange(roads.Values);
            var portals = (List<TrafficRoadPortal>)typeof(NeighbourhoodGenerator)
                .GetField("m_trafficRoadPortals", Private).GetValue(generator);
            portals.Add(new TrafficRoadPortal(new GridCoordinate(-2, 0), CardinalDirection.West,
                Vector3.left * 20f, Vector3.left));
            portals.Add(new TrafficRoadPortal(new GridCoordinate(2, 0), CardinalDirection.East,
                Vector3.right * 20f, Vector3.right));
            var settings = ScriptableObject.CreateInstance<TrafficSettings>();
            try
            {
                graph.Build(generator, settings);
                Assert.That(graph.BuildFailureReason, Is.Null);
                Assert.That(graph.Routes.Count, Is.GreaterThan(0));
                Assert.That(Find(graph, "TryFindPath", (-2, 0), (0, 3), out _), Is.False);
                foreach (var route in graph.Routes)
                {
                    AssertOnlyStreetWaypoints(route);
                    for (int seed = 0; seed < 32; seed++)
                    {
                        Assert.That(graph.TryCreateRandomRoute(route, settings,
                            new System.Random(seed), out var trip), Is.True);
                        AssertOnlyStreetWaypoints(trip);
                    }
                }
                Assert.That(Find(graph, "TryFindPath", (-2, 0), (2, 0), out var street), Is.True);
                Assert.That(street, Does.Contain(GridCoordinate.Zero),
                    "The junction at the foot of the hill stays open to traffic.");
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); }
        }

        private static void AssertOnlyStreetWaypoints(TrafficRoute route)
        {
            foreach (var coordinate in route.Coordinates)
                Assert.That(coordinate.Y, Is.Zero, "Routes must not enter the uphill exit.");
            // Both normal and rogue traffic choose spawn poses from these waypoints.
            foreach (var waypoint in route.Waypoints)
                Assert.That(Mathf.Abs(waypoint.z), Is.LessThan(10f),
                    "Spawn waypoints must stay below the first hill tile and summit.");
        }

        [Test]
        public void FallbackEscapesStraightFirstLoopWithoutReversingAtJunctions()
        {
            var graph = CreateGraph(new[] { (0, 0), (1, 0), (2, 0), (3, 0), (3, -1),
                (3, -2), (2, -2), (1, -2), (0, -2), (0, -1), (1, 1) });
            Assert.That(Find(graph, "TryFindStrictPath", (0, 0), (1, 1), out _), Is.False);
            Assert.That(Find(graph, "TryFindPath", (0, 0), (1, 1), out var path), Is.True);
            Assert.That(path, Is.EqualTo(new[] { new GridCoordinate(0, 0),
                new GridCoordinate(1, 0), new GridCoordinate(1, 1) }));
        }

        [Test]
        public void SuccessfulStraightFirstRouteIsPreserved()
        {
            var graph = CreateGraph(new[] { (0, 0), (1, 0), (2, 0), (2, 1), (1, 1) });
            Assert.That(Find(graph, "TryFindStrictPath", (0, 0), (1, 1), out var original), Is.True);
            Assert.That(Find(graph, "TryFindPath", (0, 0), (1, 1), out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(original));
        }

        [Test]
        public void TurningFallbackPrefersFewerTurnsOverAShorterZigzag()
        {
            var graph = CreateGraph(new[] { (0, 0), (1, 0), (1, 1), (2, 1), (2, 2), (3, 2),
                (0, -1), (0, -2), (1, -2), (2, -2), (3, -2), (3, -1), (3, 0), (3, 1) });
            Assert.That(Find(graph, "TryFindTurningPath", (0, 0), (3, 2), out var path), Is.True);
            Assert.That(path[1], Is.EqualTo(new GridCoordinate(0, -1)));
            Assert.That(path.Count, Is.EqualTo(10));
        }

        [Test]
        public void DisconnectedRoadsStillFail()
        {
            var graph = CreateGraph(new[] { (0, 0), (1, 0), (4, 0), (5, 0) });
            Assert.That(Find(graph, "TryFindPath", (0, 0), (4, 0), out _), Is.False);
        }

        [Test]
        public void IncomingHeadingOnlyAllowsReversalAtADeadEnd()
        {
            var graph = CreateGraph(new[] { (-1, 0), (0, 0), (1, 0) });
            Assert.That(Find(graph, "TryFindTurningPath", (0, 0), (-1, 0), out var path,
                CardinalDirection.East), Is.True);
            Assert.That(path, Is.EqualTo(new[] { new GridCoordinate(0, 0), new GridCoordinate(1, 0),
                new GridCoordinate(0, 0), new GridCoordinate(-1, 0) }));
        }

        private TrafficLaneGraph CreateGraph((int X, int Y)[] coordinates)
        {
            m_root = new GameObject("Traffic routing regression test");
            var graph = new TrafficLaneGraph();
            var roads = (Dictionary<GridCoordinate, GeneratedRoad>)typeof(TrafficLaneGraph)
                .GetField("m_roads", Private).GetValue(graph);
            foreach (var coordinate in coordinates)
            {
                var host = new GameObject("Road");
                host.transform.SetParent(m_root.transform);
                var road = host.AddComponent<GeneratedRoad>();
                var grid = new GridCoordinate(coordinate.X, coordinate.Y);
                host.transform.position = new Vector3(coordinate.X * 10f, 0f, coordinate.Y * 10f);
                // CanTravel also requires the neighbouring road, so absent cells
                // remain disconnected even with all connection bits set here.
                road.Initialise(grid, RoadConnections.North | RoadConnections.East |
                    RoadConnections.South | RoadConnections.West);
                roads.Add(grid, road);
            }
            return graph;
        }

        private static bool Find(TrafficLaneGraph graph, string method, (int X, int Y) start,
            (int X, int Y) end, out List<GridCoordinate> path, CardinalDirection? incoming = null)
        {
            object[] arguments = { new GridCoordinate(start.X, start.Y),
                new GridCoordinate(end.X, end.Y), null, incoming };
            bool found = (bool)typeof(TrafficLaneGraph).GetMethod(method, Private).Invoke(graph, arguments);
            path = (List<GridCoordinate>)arguments[2];
            return found;
        }
    }
}
