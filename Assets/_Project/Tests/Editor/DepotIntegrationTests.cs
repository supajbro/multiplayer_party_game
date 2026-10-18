using System.Linq;
using CouchGuys.Gameplay.Depot;
using CouchGuys.ProceduralGeneration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class DepotIntegrationTests
    {
        private const string ModelPath = "Assets/_Project/Models/depot/depot.fbx";
        private const string RegionPath = "Assets/_Project/Settings/Regions/Region_Suburbs.asset";

        [Test]
        public void ImportedDepotContainsRequiredGameplayMarkersAndAnimation()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(model, Is.Not.Null);
            string[] markers =
            {
                "CGD_MARK_Player_InitialSpawn", "CGD_MARK_DeliveryMan_Frog",
                "CGD_MARK_Truck_RearOffstageArrival", "CGD_MARK_Couch_OffstageHiddenSpawn",
                "CGD_MARK_Couch_VisiblePickup", "CGD_MARK_ExitDoor_SuburbsOutside",
                "CGD_MARK_ExitDoor_SafetyVolume", "CGD_MARK_TruckDoor_SafetyVolume",
                "CGD_INTERACT_ExitDoor_ExteriorButton", "CGD_INTERACT_ExitDoor_InteriorButton"
            };
            string[] names = model.GetComponentsInChildren<Transform>(true)
                .Select(value => value.name).ToArray();
            foreach (string marker in markers) Assert.That(names, Does.Contain(marker), marker);
            for (int index = 0; index < 5; index++)
                Assert.That(names, Does.Contain($"CGD_MARK_Couch_ConveyorPoint_{index}"));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Any(value => !value.name.StartsWith("__preview__") && value.length > 0f), Is.True);
        }

        [Test]
        public void RuntimeDepotSetupCreatesSeparatedSpawnsAndConveyor()
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            GameObject instance = Object.Instantiate(asset);
            try
            {
                Assert.That(DepotController.TryConfigureImportedModel(instance,
                    out StartingArea startingArea), Is.True);
                Assert.That(startingArea.PlayerSpawnPoints.Length, Is.EqualTo(4));
                Assert.That(startingArea.PlayerSpawnPoints.Distinct().Count(), Is.EqualTo(4));
                Assert.That(startingArea.PlayerSpawnPoints.SelectMany((left, leftIndex) =>
                    startingArea.PlayerSpawnPoints.Skip(leftIndex + 1)
                        .Select(right => Vector3.Distance(left.position, right.position)))
                    .All(distance => distance > 1f), Is.True);
                Assert.That(startingArea.Depot, Is.Not.Null);
                Assert.That(startingArea.Depot.HasConveyor, Is.True);
                Assert.That(startingArea.Depot.ConveyorPath.Count, Is.EqualTo(5));
                Assert.That(startingArea.GridFootprint, Is.EqualTo(new Vector2Int(5, 5)));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SuburbsReferencesDepotStartingArea()
        {
            RegionDefinition region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
            Assert.That(region, Is.Not.Null);
            Assert.That(region.StartingAreaPrefab, Is.Not.Null);
            Assert.That(region.StartingAreaPrefab.GetComponentsInChildren<Transform>(true)
                .Any(value => value.name == "CGD_MARK_Player_InitialSpawn"), Is.True);
        }
    }
}
