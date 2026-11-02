using CouchGuys.Gameplay.Vehicles;
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
        private const string TruckPath =
            "Assets/_Project/Settings/Vehicles/Truck.asset";

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
        public void Truck_PreservesStarterClimbCapability()
        {
            RegionDefinition region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
            TruckSettings light = AssetDatabase.LoadAssetAtPath<TruckSettings>(TruckPath);
            Assert.That(region, Is.Not.Null);
            Assert.That(light, Is.Not.Null);
            Assert.That(light.DisplayName, Is.EqualTo("Truck"));
            Assert.That(AssetDatabase.FindAssets("t:TruckSettings", new[] { "Assets/_Project/Settings/Vehicles" }),
                Has.Length.EqualTo(1), "Only the retained truck configuration should remain.");

            float gravityAlongSlope = Physics.gravity.magnitude * Mathf.Sin(
                region.LandmarkRoads.MaximumSlope * Mathf.Deg2Rad);
            float lightClimbAcceleration = light.Acceleration * light.UphillPowerMultiplier;

            Assert.That(lightClimbAcceleration, Is.LessThan(gravityAlongSlope),
                "The starter truck should naturally stall on the summit grade.");
        }
    }
}
