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
        private const string PrefabPath = "Assets/_Project/Prefabs/World/CouchGuysDepotV2.prefab";

        [Test]
        public void DepotSpawnRearWallAndLodsRemainValidAfterWorldPlacement()
        {
            GameObject instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                instance.transform.SetPositionAndRotation(new Vector3(73, 2, -41), Quaternion.Euler(0, 93, 0));
                DepotController depot = instance.GetComponent<DepotController>();
                Transform spawn = instance.transform.Find("TruckSpawnPoint");
                Transform road = instance.GetComponentsInChildren<Transform>().Single(t => t.name == "RoadConnection");
                Assert.That(depot.VehicleArrival, Is.EqualTo(spawn));
                Assert.That(Vector3.Dot(spawn.forward, road.forward), Is.GreaterThan(.999f));
                Assert.That(instance.transform.InverseTransformPoint(spawn.position).z, Is.InRange(-8f, 7f));
                BoxCollider rear = instance.GetComponentsInChildren<BoxCollider>().Single(c => c.name.Contains("RearWall_Closure"));
                Physics.SyncTransforms();
                Vector3 centre = rear.transform.TransformPoint(rear.center);
                Assert.That(rear.Raycast(new Ray(centre - instance.transform.forward * 3f,
                    instance.transform.forward), out _, 6f), Is.True);
                Assert.That(Physics.Raycast(instance.transform.TransformPoint(new Vector3(7.2f, 5.44f, -14f)),
                    instance.transform.forward, 5f, ~0, QueryTriggerInteraction.Ignore), Is.True,
                    "The rear closure must meet the existing header without a gap.");
                RaycastHit ground = Physics.RaycastAll(spawn.position + Vector3.up * 20f,
                    Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).First();
                Assert.That(ground.collider.name, Does.Contain("WalkableFloor"));
                Vector3 vehicleCentre = ground.point + Vector3.up * 1.52f;
                Assert.That(Physics.OverlapBox(vehicleCentre, new Vector3(1.7f, 1.32f, 3.5f),
                    spawn.rotation, ~0, QueryTriggerInteraction.Ignore), Is.Empty,
                    "Purchased vehicle must fit inside the dispatch lane.");
                LODGroup group = instance.GetComponentInChildren<LODGroup>();
                LOD[] lods = group.GetLODs();
                Assert.That(lods.Length, Is.EqualTo(3));
                Assert.That(group.fadeMode, Is.EqualTo(LODFadeMode.None));
                int[] triangles = lods.Select(l => l.renderers.Sum(r =>
                    r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3)).ToArray();
                Assert.That(triangles[1], Is.LessThan(triangles[0]));
                Assert.That(triangles[2], Is.LessThan(triangles[1]));
                Renderer[] renderers = lods.SelectMany(l => l.renderers).ToArray();
                Assert.That(renderers.Distinct().Count(), Is.EqualTo(renderers.Length));
                Assert.That(renderers.All(r => !r.name.Contains("_Slat_")), Is.True);
                MeshRenderer[] functionalRenderers = instance.GetComponentsInChildren<MeshRenderer>()
                    .Where(r => !renderers.Contains(r)).ToArray();
                Assert.That(functionalRenderers.Length, Is.EqualTo(24));
                Assert.That(functionalRenderers.All(r => r.name.Contains("_Slat_")), Is.True);
                Assert.That(instance.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh != null), Is.True);
                Assert.That(depot.HasConveyor, Is.True);
            }
            finally
            {
                instance.GetComponent<DepotController>().ReleaseDoorAnimations();
                Object.DestroyImmediate(instance);
            }
        }

        [TestCase("ExitRollerDoor", true)]
        [TestCase("TruckGarageDoor", false)]
        public void AuthoredDoorMovesEverySlatIndependentlyAndRestoresCollision(string prefix, bool exit)
        {
            GameObject instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                DepotController depot = instance.GetComponent<DepotController>();
                Transform[] slats = Enumerable.Range(0, 12).Select(i => instance
                    .GetComponentsInChildren<Transform>().Single(t => t.name == $"{prefix}_Slat_{i:00}")).ToArray();
                SerializedObject data = new SerializedObject(depot);
                BoxCollider blocker = (BoxCollider)data.FindProperty(exit ? "m_exitDoor" : "m_vehicleGarageDoor")
                    .FindPropertyRelative("m_blockingCollider").objectReferenceValue;
                depot.ApplyDoorStates(false, false, true);
                Vector3[] closed = slats.Select(t => t.localPosition).ToArray();
                depot.ApplyDoorStates(exit, !exit, true);
                for (int i = 0; i < slats.Length; i++)
                    Assert.That(Vector3.Distance(slats[i].localPosition, closed[i]), Is.GreaterThan(.5f), slats[i].name);
                Assert.That(blocker.enabled, Is.False);
                // Updating the other graph must not reset this door.
                Vector3[] open = slats.Select(t => t.localPosition).ToArray();
                depot.ApplyDoorStates(true, true, true);
                for (int i = 0; i < slats.Length; i++)
                    Assert.That(Vector3.Distance(slats[i].localPosition, open[i]), Is.LessThan(.001f));
                depot.ApplyDoorStates(false, false, true);
                Assert.That(blocker.enabled, Is.True);
                for (int i = 0; i < slats.Length; i++)
                    Assert.That(Vector3.Distance(slats[i].localPosition, closed[i]), Is.LessThan(.001f));
            }
            finally
            {
                instance.GetComponent<DepotController>().ReleaseDoorAnimations();
                Object.DestroyImmediate(instance);
            }
        }

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
