using System.Linq;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Traffic;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class RogueCarPrefabTests
    {
        [MenuItem("Couch Guys/Tests/Run Rogue Car Prefab Tests %&F1")]
        public static void RunPrefabChecks()
        {
            var tests = new RogueCarPrefabTests();
            tests.HardDeliveryUsesDedicatedRegisteredRoguePrefab();
            tests.RoguePrefabUsesDedicatedModelAndThreeLods();
            Debug.Log("PASS ROGUE CAR PREFAB TESTS: delivery/network references, dedicated model, three LODs, materials and animated wheel references.");
        }

        [Test]
        public void HardDeliveryUsesDedicatedRegisteredRoguePrefab()
        {
            var hard = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/HardDelivery.asset");
            var traffic = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Traffic/TrafficCar.prefab");
            Assert.That(hard.RogueCarSettings, Is.Not.Null);
            NetworkObject rogue = hard.RogueCarPrefab;
            Assert.That(rogue, Is.Not.Null);
            Assert.That(rogue.gameObject, Is.Not.SameAs(traffic));
            Assert.That(rogue.GetComponents<RogueCar>().Length, Is.EqualTo(1));
            Assert.That(rogue.GetComponent<TrafficVehicleController>(), Is.Not.Null);
            Assert.That(new SerializedObject(rogue.GetComponent<NetworkTransform>())
                .FindProperty("_clientAuthoritative").boolValue, Is.False);
            Assert.That(rogue.AssetPathHash, Is.Not.Zero);
            Assert.That(rogue.AssetPathHash, Is.Not.EqualTo(traffic.GetComponent<NetworkObject>().AssetPathHash));
            foreach (string path in new[] { "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset", "Assets/DefaultPrefabObjects.asset" })
                Assert.That(AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path).Prefabs.Contains(rogue), Is.True, path);
            Assert.That(traffic.GetComponent<RogueCar>(), Is.Null);
            Assert.That(new SerializedObject(rogue.GetComponent<RogueCar>()).FindProperty("m_settings").objectReferenceValue,
                Is.SameAs(hard.RogueCarSettings));
        }

        [Test]
        public void RoguePrefabUsesDedicatedModelAndThreeLods()
        {
            var rogue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/RogueCar.prefab");
            var traffic = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Traffic/TrafficCar.prefab");
            var visual = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/RogueCarVisual.prefab");
            Assert.That(visual, Is.Not.Null);
            var controller = new SerializedObject(rogue.GetComponent<TrafficVehicleController>());
            var prefabs = controller.FindProperty("m_visualPrefabs");
            Assert.That(prefabs.arraySize, Is.EqualTo(1));
            Assert.That(prefabs.GetArrayElementAtIndex(0).objectReferenceValue, Is.SameAs(visual));
            var variants = controller.FindProperty("m_visualVariants");
            Assert.That(variants.arraySize, Is.EqualTo(1));
            Assert.That(variants.GetArrayElementAtIndex(0).objectReferenceValue, Is.Not.Null);
            var group = rogue.GetComponentInChildren<LODGroup>(true);
            Assert.That(group, Is.Not.Null);
            Assert.That(group.lodCount, Is.EqualTo(3));
            var trafficMeshes = traffic.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).ToArray();
            for (int lod = 0; lod < 3; lod++)
            {
                var renderers = group.GetLODs()[lod].renderers;
                Assert.That(renderers.Length, Is.GreaterThan(0));
                foreach (var renderer in renderers)
                {
                    Assert.That(renderer, Is.Not.Null);
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(AssetDatabase.GetAssetPath(mesh), Does.EndWith($"rogue_car_LOD{lod}.fbx"));
                    Assert.That(trafficMeshes.Contains(mesh), Is.False);
                    Assert.That(renderer.sharedMaterials.All(m => m != null && m.shader.name == "Universal Render Pipeline/Lit"), Is.True);
                }
            }
            var wheels = new SerializedObject(rogue.GetComponentInChildren<TrafficWheelAnimator>(true));
            foreach (string property in new[] { "m_frontWheels", "m_rearWheels" })
            {
                var list = wheels.FindProperty(property);
                Assert.That(list.arraySize, Is.EqualTo(2));
                for (int i = 0; i < list.arraySize; i++)
                {
                    var pivot = (Transform)list.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.That(pivot, Is.Not.Null);
                    Assert.That(pivot.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(3));
                }
            }
        }
    }
}
