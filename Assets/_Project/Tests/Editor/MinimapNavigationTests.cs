using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Tests.Editor
{
    public sealed class MinimapNavigationTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<GameObject> m_objects = new();
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);

        private T Create<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            go.SetActive(false);
            m_objects.Add(go);
            return go.AddComponent<T>();
        }

        private void SpawnForQuery(NetworkBehaviour behaviour, int id)
        {
            var obj = behaviour.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetProperty("ObjectId", Flags).SetValue(obj, id);
            typeof(NetworkObject).GetProperty("IsDeinitializing", Flags).SetValue(obj, false);
            typeof(NetworkBehaviour).GetField("_networkObjectCache", Flags).SetValue(behaviour, obj);
        }

        private NeighbourhoodMap Map()
        {
            var map = Create<NeighbourhoodMap>();
            Set(map, "m_carrier", map.gameObject.AddComponent<PlayerCouchCarrier>());
            Set(map, "m_truckInteractor", map.gameObject.AddComponent<PlayerTruckInteractor>());
            Set(map, "m_hoverboardInteractor", map.gameObject.AddComponent<PlayerHoverboardInteractor>());
            return map;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = m_objects.Count - 1; i >= 0; i--)
                if (m_objects[i] != null) Object.DestroyImmediate(m_objects[i]);
            m_objects.Clear();
        }

        [Test]
        public void PickupDropAndDespawnUseReplicatedCarryState()
        {
            var map = Map();
            var couch = Create<CouchCarryController>();
            SpawnForQuery(couch, 1);
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.False);
            var carried = Get<SyncVar<NetworkObject>>(Get<PlayerCouchCarrier>(map, "m_carrier"), "m_carriedCouch");
            carried.Value = couch.GetComponent<NetworkObject>();
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.True);
            carried.Value = null;
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.False);
            carried.Value = couch.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetProperty("IsDeinitializing", Flags).SetValue(carried.Value, true);
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.False);
        }

        [TestCase("truck")]
        [TestCase("sled")]
        [TestCase("hoverboard")]
        public void AttachmentRequiresLocalOccupancyAndUpdatesOnUnload(string transport)
        {
            var map = Map();
            var other = Map();
            var couch = Create<CouchCarryController>();
            SpawnForQuery(couch, 1);
            SyncVar<NetworkObject> attachment;
            if (transport == "truck")
            {
                var truck = Create<TruckController>();
                SpawnForQuery(truck, 2);
                attachment = Get<SyncVar<NetworkObject>>(couch, "m_loadedTruck");
                attachment.Value = truck.GetComponent<NetworkObject>();
                Get<SyncVar<NetworkObject>>(Get<PlayerTruckInteractor>(map, "m_truckInteractor"), "m_currentTruckObject").Value = attachment.Value;
            }
            else if (transport == "sled")
            {
                var sled = Create<SledController>();
                attachment = Get<SyncVar<NetworkObject>>(couch, "m_mountedSled");
                attachment.Value = sled.GetComponent<NetworkObject>();
                Get<SyncVar<NetworkObject>>(Get<PlayerCouchCarrier>(map, "m_carrier"), "m_carriedCouch").Value = attachment.Value;
            }
            else
            {
                var board = Create<MagneticHoverboardController>();
                SpawnForQuery(board, 2);
                attachment = Get<SyncVar<NetworkObject>>(couch, "m_mountedHoverboard");
                attachment.Value = board.GetComponent<NetworkObject>();
                Get<SyncVar<NetworkObject>>(Get<PlayerHoverboardInteractor>(map, "m_hoverboardInteractor"), "m_board").Value = attachment.Value;
            }
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.True);
            Assert.That(Call(other, "IsTransportedByLocalPlayer", couch), Is.False);
            attachment.Value = null;
            Assert.That(Call(map, "IsTransportedByLocalPlayer", couch), Is.False);
        }

        [Test]
        public void PrimaryDefaultsToEarliestRetainsChoiceCyclesAndRecoversAfterRemoval()
        {
            var first = Map();
            var second = Map();
            foreach (var map in new[] { first, second })
            {
                Get<List<int>>(map, "m_navigationIds").AddRange(new[] { 1, 2, 3 });
                Call(map, "SelectPrimaryDestination", 2, false);
                Assert.That(Get<int>(map, "m_primaryDeliveryId"), Is.EqualTo(2));
            }
            Call(first, "SelectPrimaryDestination", 1, true);
            Assert.That(Get<int>(first, "m_primaryDeliveryId"), Is.EqualTo(3));
            Assert.That(Get<int>(second, "m_primaryDeliveryId"), Is.EqualTo(2));
            Call(first, "SelectPrimaryDestination", 1, false);
            Assert.That(Get<int>(first, "m_primaryDeliveryId"), Is.EqualTo(3));
            Get<List<int>>(first, "m_navigationIds").Remove(3);
            Call(first, "SelectPrimaryDestination", 1, false);
            Assert.That(Get<int>(first, "m_primaryDeliveryId"), Is.EqualTo(1));
            Get<List<int>>(first, "m_navigationIds").Clear();
            Call(first, "SelectPrimaryDestination", -1, false);
            Assert.That(Get<int>(first, "m_primaryDeliveryId"), Is.EqualTo(-1));
        }

        [Test]
        public void SavedMarkerIsWiredAndPrimaryUsesOnlyExistingNavigationIndicator()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var template = Get<RectTransform>(player.GetComponent<NeighbourhoodMap>(), "m_deliveryMarkerPrefab");
            Assert.That(template, Is.Not.Null);
            Assert.That(template.GetComponentInChildren<Text>(true), Is.Not.Null);
            var marker = Object.Instantiate(template);
            m_objects.Add(marker.gameObject);
            var map = Map();
            Get<List<int>>(map, "m_navigationIds").AddRange(new[] { 10, 20 });
            var label = marker.GetComponentInChildren<Text>(true);
            Call(map, "StyleDeliveryMarker", marker, label, 20, true, false);
            Assert.That(label.text, Is.EqualTo("2"));
            Assert.That(marker.GetComponent<Image>().enabled, Is.True);
            float secondarySize = marker.sizeDelta.x;
            Call(map, "StyleDeliveryMarker", marker, label, 20, true, true);
            Assert.That(marker.sizeDelta.x, Is.GreaterThan(secondarySize));
            Assert.That(marker.GetComponent<Image>().enabled, Is.False);
        }

        [Test]
        public void CollinearDestinationsKeepDistinctNumbersInsideMinimap()
        {
            var map = Map();
            var positions = Get<List<Vector2>>(map, "m_destinationMarkerPositions");
            positions.Add(new Vector2(90f, 0f));
            var second = (Vector2)Call(map, "SeparateDestinationMarker", new Vector2(90f, 0f), 100f);
            var third = (Vector2)Call(map, "SeparateDestinationMarker", new Vector2(90f, 0f), 100f);
            Assert.That(second.magnitude, Is.LessThanOrEqualTo(100.01f));
            Assert.That(third.magnitude, Is.LessThanOrEqualTo(100.01f));
            Assert.That(Vector2.Distance(second, third), Is.GreaterThan(22f));
            Assert.That(Vector2.Distance(second, positions[0]), Is.GreaterThan(22f));
        }
    }
}
