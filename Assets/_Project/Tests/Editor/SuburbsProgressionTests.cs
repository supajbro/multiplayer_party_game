using CouchGuys.Gameplay.Vehicles;
using System.Reflection;
using CouchGuys.ProceduralGeneration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class SuburbsProgressionTests
    {
        private const string RegionPath =
            "Assets/_Project/Settings/Regions/Region_Suburbs.asset";

        [Test]
        public void Suburbs_EnablesHillyBoundaryAndSummitRoad()
        {
            RegionDefinition region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
            Assert.That(region, Is.Not.Null);
            Assert.That(region.Border.Enabled, Is.True);
            Assert.That(region.Border.HillHeight, Is.GreaterThan(0f));
            Assert.That(region.Border.HillDepth, Is.GreaterThan(region.Terrain.RoadShoulderWidth));
            Assert.That(region.Border.TreeDensity, Is.GreaterThan(0f));
            Assert.That(region.LandmarkRoads.Enabled, Is.True);
            Assert.That(region.LandmarkRoads.UphillLengthTiles, Is.GreaterThanOrEqualTo(12));
            Assert.That(region.LandmarkRoads.SummitHeight, Is.GreaterThan(40f));
            Assert.That(region.LandmarkRoads.NextRegionId, Is.Not.Empty);

            float pillarWidth = Mathf.Max(1.2f, region.Border.TunnelWidth * 0.16f);
            float tunnelOuterHalfWidth = region.Border.TunnelWidth * 0.5f + pillarWidth;
            float summitCorridorHalfWidth = region.Terrain.RoadHalfWidth + Mathf.Max(
                region.Terrain.RoadShoulderWidth,
                region.LandmarkRoads.EmbankmentBlendWidth);
            float minimumCentreOffset = summitCorridorHalfWidth + tunnelOuterHalfWidth +
                                        Mathf.Max(1f, region.Border.ColliderThickness);
            int firstValidParallelExit = region.Grid.BlockWidth + 1;
            Assert.That(firstValidParallelExit * region.Grid.RoadTileSize,
                Is.GreaterThanOrEqualTo(minimumCentreOffset),
                "The first genuine parallel boundary road must clear the summit corridor.");
        }

        [Test]
        public void SummitExitRequiresTheFlamecouch()
        {
            var truck = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/Truck.prefab");
            var flame = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/Flamecouch.prefab");
            Assert.That(UphillExitRestriction.AllowsExitVehicle(truck.GetComponent<TruckController>()), Is.False);
            Assert.That(UphillExitRestriction.AllowsExitVehicle(flame.GetComponent<TruckController>()), Is.True);
        }

        [TestCase(CardinalDirection.North)]
        [TestCase(CardinalDirection.East)]
        [TestCase(CardinalDirection.South)]
        [TestCase(CardinalDirection.West)]
        public void TrafficTunnelHasClearTerrainAndRoadThroughItsShell(CardinalDirection direction)
        {
            var host = new GameObject("Traffic tunnel terrain test");
            try
            {
                var generator = host.AddComponent<NeighbourhoodGenerator>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(NeighbourhoodGenerator);
                var region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
                type.GetMethod("ApplyRegionConfiguration", flags).Invoke(generator, new object[] { region });
                type.GetField("m_startDirection", flags).SetValue(generator, direction);
                type.GetField("m_gridOrigin", flags).SetValue(generator, new Vector3(37f, 12f, -59f));
                type.GetMethod("GenerateGuaranteedCentralRoad", flags).Invoke(generator, null);
                type.GetMethod("GenerateUphillExitRoad", flags).Invoke(generator, null);
                int boundary = region.Grid.GridHeight * (region.Grid.BlockHeight + 1);
                var coordinate = (GridCoordinate)type.GetMethod("LocalToGrid", flags)
                    .Invoke(generator, new object[] { region.Grid.BlockWidth + 1, boundary });
                type.GetMethod("ExtendTrafficGateway", flags).Invoke(generator, new object[] { coordinate, boundary });
                type.GetMethod("CalculateRoadConnections", flags).Invoke(generator, null);
                type.GetMethod("FinaliseTrafficBoundaryConnections", flags).Invoke(generator, null);
                Assert.That(generator.TrafficRoadPortals.Count, Is.EqualTo(1));
                TrafficRoadPortal portal = generator.TrafficRoadPortals[0];
                Vector3 side = Vector3.Cross(Vector3.up, portal.WorldOutward);
                foreach (float along in new[] { 0f, region.Border.TunnelDepth * 0.5f })
                {
                    Vector3 centre = portal.WorldPosition + portal.WorldOutward * along;
                    foreach (float across in new[] { -region.Border.TunnelWidth * 0.49f, 0f, region.Border.TunnelWidth * 0.49f })
                    {
                        Assert.That(generator.SampleTerrainHeight(centre + side * across),
                            Is.LessThanOrEqualTo(portal.WorldPosition.y + region.Terrain.RoadSurfaceOffset + 0.001f));
                    }
                    GridCoordinate road = generator.WorldToGrid(centre);
                    Assert.That(generator.TryGetRoadHeight(road, out _), Is.True);
                }
                var clear = type.GetMethod("IsHillTreePositionClear", flags);
                Assert.That((bool)clear.Invoke(generator, new object[] { portal.WorldPosition }), Is.False);
                Vector3 upperHill = generator.SummitExitPortal.WorldPosition + side * 60f;
                Assert.That((bool)clear.Invoke(generator, new object[] { upperHill }), Is.True);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [TestCase(CardinalDirection.North)]
        [TestCase(CardinalDirection.East)]
        [TestCase(CardinalDirection.South)]
        [TestCase(CardinalDirection.West)]
        public void RearTerrainFollowsTheClimbAcrossTheFullWidth(CardinalDirection direction)
        {
            var host = new GameObject("Rear highlands test");
            try
            {
                var generator = host.AddComponent<NeighbourhoodGenerator>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(NeighbourhoodGenerator);
                var region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
                type.GetMethod("ApplyRegionConfiguration", flags).Invoke(generator, new object[] { region });
                type.GetField("m_startDirection", flags).SetValue(generator, direction);
                type.GetField("m_gridOrigin", flags).SetValue(generator, new Vector3(37f, 12f, -59f));
                foreach (string method in new[] { "GenerateGuaranteedCentralRoad", "GenerateUphillExitRoad", "CalculateRoadConnections" })
                {
                    type.GetMethod(method, flags).Invoke(generator, null);
                }

                var toWorld = type.GetMethod("LocalCoordinatesToWorld", flags);
                float boundaryY = region.Grid.GridHeight * (region.Grid.BlockHeight + 1);
                float previousHeight = float.NegativeInfinity;
                foreach (float step in new[] { 6f, 12f, 18f, 24f, 30f })
                {
                    float roadY = boundaryY + Mathf.Min(step, region.LandmarkRoads.UphillLengthTiles);
                    var centre = (Vector3)toWorld.Invoke(generator, new object[] { 0f, roadY, 0f });
                    float expected = generator.SampleTerrainHeight(centre);
                    Assert.That(expected, Is.GreaterThanOrEqualTo(previousHeight - 0.001f));
                    previousHeight = expected;
                    foreach (float x in new[] { -28f, -14f, 14f, 28f })
                    {
                        var point = (Vector3)toWorld.Invoke(generator, new object[] { x, boundaryY + step, 0f });
                        Assert.That(generator.SampleTerrainHeight(point), Is.EqualTo(expected).Within(0.001f),
                            $"Rear terrain at local ({x}, {boundaryY + step}) must match the exit climb.");
                    }
                }

                var highlands = type.GetMethod("ApplySummitHighlands", flags);
                Assert.That((float)highlands.Invoke(generator, new object[] { 17f, boundaryY }), Is.EqualTo(17f));
                Assert.That((float)highlands.Invoke(generator, new object[] { 17f, boundaryY - 1f }), Is.EqualTo(17f));
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
