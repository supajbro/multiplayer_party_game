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
    }
}
