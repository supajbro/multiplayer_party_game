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
        private const string LightVanPath =
            "Assets/_Project/Settings/Vehicles/LightVanSettings.asset";
        private const string GoodVanPath =
            "Assets/_Project/Settings/Vehicles/GoodVanSettings.asset";
        private const string BestVanPath =
            "Assets/_Project/Settings/Vehicles/BestVanSettings.asset";

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
        public void VehicleTiers_HaveGenuineProgressiveClimbCapability()
        {
            RegionDefinition region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
            VanSettings light = AssetDatabase.LoadAssetAtPath<VanSettings>(LightVanPath);
            VanSettings good = AssetDatabase.LoadAssetAtPath<VanSettings>(GoodVanPath);
            VanSettings best = AssetDatabase.LoadAssetAtPath<VanSettings>(BestVanPath);
            Assert.That(region, Is.Not.Null);
            Assert.That(light, Is.Not.Null);
            Assert.That(good, Is.Not.Null);
            Assert.That(best, Is.Not.Null);

            float gravityAlongSlope = Physics.gravity.magnitude * Mathf.Sin(
                region.LandmarkRoads.MaximumSlope * Mathf.Deg2Rad);
            float lightClimbAcceleration = light.Acceleration * light.UphillPowerMultiplier;
            float goodClimbAcceleration = good.Acceleration * good.UphillPowerMultiplier;
            float bestClimbAcceleration = best.Acceleration * best.UphillPowerMultiplier;

            Assert.That(lightClimbAcceleration, Is.LessThan(gravityAlongSlope),
                "The starter van should naturally stall on the summit grade.");
            Assert.That(goodClimbAcceleration, Is.LessThan(gravityAlongSlope),
                "The mid-tier van should improve on the starter but still stall on the summit grade.");
            Assert.That(bestClimbAcceleration, Is.GreaterThan(gravityAlongSlope),
                "The best van should be able to overcome gravity on the summit grade.");
            Assert.That(bestClimbAcceleration, Is.GreaterThan(goodClimbAcceleration));
            Assert.That(light.Traction, Is.LessThan(good.Traction));
            Assert.That(good.Traction, Is.LessThan(best.Traction));
        }
    }
}
