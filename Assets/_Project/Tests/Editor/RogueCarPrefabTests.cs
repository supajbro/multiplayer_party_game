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
        public void RoguePrefabKeepsTrafficModelAndLods()
        {
            var rogue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/RogueCar.prefab");
            var traffic = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Traffic/TrafficCar.prefab");
            CollectionAssert.AreEqual(traffic.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh),
                rogue.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh));
            CollectionAssert.AreEqual(traffic.GetComponentsInChildren<LODGroup>(true).Select(l => l.lodCount),
                rogue.GetComponentsInChildren<LODGroup>(true).Select(l => l.lodCount));
        }
    }
}
