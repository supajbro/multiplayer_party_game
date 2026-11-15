using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Vehicles;
using FishNet.Managing.Object;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class FlamecouchVehicleTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/Flamecouch.prefab";

        [Test]
        public void CataloguePurchasesTheRegisteredFlamecouchForOneHundredThousand()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(
                "Assets/_Project/Resources/Shop/DeliveryManagerShop.asset");
            var item = catalogue.Find("flamecouch");
            Assert.That(item, Is.Not.Null);
            Assert.That(item.Price, Is.EqualTo(100000));
            Assert.That(item.Category, Is.EqualTo(ShopItemCategory.Vehicle));
            Assert.That(item.Icon, Is.Not.Null);
            Assert.That(catalogue.Items.Count(value => value == item), Is.EqualTo(1));
            Assert.That(item.PurchaseBehaviour, Is.TypeOf<VehiclePurchaseBehaviour>());
            var purchase = (VehiclePurchaseBehaviour)item.PurchaseBehaviour;
            Assert.That(AssetDatabase.GetAssetPath(purchase.VehiclePrefab), Is.EqualTo(PrefabPath));
            Assert.That(purchase.VehiclePrefab.GetComponent<TruckController>(), Is.Not.Null);
            StringAssert.DoesNotContain("CouchGuys.Gameplay.Vehicles.VanController", File.ReadAllText(PrefabPath));
            var registry = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(
                "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            Assert.That(registry.Prefabs.Count(value => value == purchase.VehiclePrefab), Is.EqualTo(1));
            var clearance = new SerializedObject(purchase).FindProperty("m_spawnClearanceSize").vector3Value;
            Assert.That(clearance.x, Is.GreaterThanOrEqualTo(4.5f));
            Assert.That(clearance.y, Is.GreaterThanOrEqualTo(3.1f));
        }

        [Test]
        public void FourHoverSeatsHaveIndividualEntriesAndRegularPassengerCameras()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var vehicle = prefab.GetComponent<TruckController>();
            Assert.That(vehicle.IsHoverTruck, Is.True);
            Assert.That(vehicle.Settings.DisplayName, Is.EqualTo("Flamecouch"));
            Assert.That(prefab.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            var data = new SerializedObject(vehicle);
            Assert.That(data.FindProperty("m_hoverProbes").arraySize, Is.EqualTo(4));
            Assert.That(data.FindProperty("m_hoverGlows").arraySize, Is.EqualTo(4));
            Assert.That(data.FindProperty("m_cargoPoints").arraySize, Is.Zero);
            for (int seat = 0; seat < TruckController.MaximumSeats; seat++)
            {
                Assert.That(vehicle.GetSeatAnchor(seat), Is.Not.Null);
                Assert.That(vehicle.GetDoorPoint(seat), Is.Not.Null);
                Assert.That(vehicle.FindEntrySeat(vehicle.GetDoorPoint(seat).position), Is.EqualTo(seat));
                Assert.That(vehicle.HasTurretSeat(seat), Is.False);
            }
            Assert.That(vehicle.GetSeatAnchor(0).position.z, Is.GreaterThan(vehicle.GetSeatAnchor(2).position.z));
            var existingTruck = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Vehicles/Truck.prefab").GetComponent<TruckController>();
            Assert.That(existingTruck.HasTurretSeat(0), Is.False);
            for (int seat = 1; seat < TruckController.MaximumSeats; seat++)
                Assert.That(existingTruck.HasTurretSeat(seat), Is.True);
        }

        [Test]
        public void SavedDepotHasSpaceForTheFlamecouchPurchase()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/World/depot_02.prefab");
            var depotObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var managerObject = new GameObject("Flamecouch depot spawn test");
            try
            {
                var depot = depotObject.GetComponent<DepotController>();
                var manager = managerObject.AddComponent<DeliveryManager>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(DeliveryManager).GetField("m_depot", flags).SetValue(manager, depot);
                var validate = typeof(DeliveryManager).GetMethod("TryValidateVehicleSpawnPose", flags);
                var purchase = AssetDatabase.LoadAssetAtPath<VehiclePurchaseBehaviour>(
                    "Assets/_Project/Settings/Shop/flamecouch_purchase.asset");
                var clearance = new SerializedObject(purchase).FindProperty("m_spawnClearanceSize").vector3Value;
                Physics.SyncTransforms();
                bool found = depot.EnumerateVehicleSpawnMarkers().Any(marker =>
                {
                    object[] arguments = { marker.position, depot.VehicleArrival.rotation,
                        depotObject.transform, Vector3.zero, Quaternion.identity, clearance };
                    return (bool)validate.Invoke(manager, arguments);
                });
                Assert.That(found, Is.True, "The saved garage must accept the Flamecouch footprint");
            }
            finally
            {
                Object.DestroyImmediate(managerObject);
                Object.DestroyImmediate(depotObject);
            }
        }

        [Test]
        public void WiderVehicleClearanceRejectsAnObstacleOutsideTheDefaultFootprint()
        {
            var origin = new Vector3(10000f, 0f, 10000f);
            var managerObject = new GameObject("Flamecouch clearance test");
            var floor = new GameObject("Test floor");
            var obstacle = new GameObject("Test obstacle");
            try
            {
                managerObject.transform.position = origin;
                var manager = managerObject.AddComponent<DeliveryManager>();
                floor.transform.position = origin + Vector3.down * .5f;
                floor.AddComponent<BoxCollider>().size = new Vector3(30f, 1f, 30f);
                obstacle.transform.position = origin + new Vector3(2.15f, 1f, 0f);
                obstacle.AddComponent<BoxCollider>().size = new Vector3(.2f, 2f, 2f);
                Physics.SyncTransforms();
                var validate = typeof(DeliveryManager).GetMethod("TryValidateVehicleSpawnPose",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                object[] arguments = { origin, Quaternion.identity, managerObject.transform,
                    Vector3.zero, Quaternion.identity, Vector3.zero };
                Assert.That((bool)validate.Invoke(manager, arguments), Is.True);
                arguments[5] = new Vector3(4.7f, 3.2f, 7f);
                Assert.That((bool)validate.Invoke(manager, arguments), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(obstacle);
                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(managerObject);
            }
        }
    }
}
