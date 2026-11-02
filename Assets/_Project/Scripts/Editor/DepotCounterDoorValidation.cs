using System;
using System.IO;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    public static class DepotCounterDoorValidation
    {
        [MenuItem("Couch Guys/Dispatcher/Validate Counter and Automatic Garage")]
        public static void Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var report = new System.Collections.Generic.List<string>();
            TruckController truck = null;
            try
            {
                foreach (string name in new[] { "depot_02", "CouchGuysDepotV2" })
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/_Project/Prefabs/World/" + name + ".prefab"), scene);
                    root.transform.position = new Vector3(10000f, 0f, 10000f);
                    var depot = root.GetComponent<DepotController>();
                    var counter = Get<Bounds>(depot, "m_counterInteractionArea");
                    Vector3 customer = root.transform.TransformPoint(counter.center);
                    customer.y = depot.DeliveryNpcMarker.position.y;
                    Check(depot.CanUseDispatcherCounter(depot.DeliveryNpcMarker.position, customer), "Counter inaccessible.");
                    Check(!depot.CanUseDispatcherCounter(depot.DeliveryNpcMarker.position + Vector3.right * 10f, customer), "Counter active while NPC is away.");
                    Check(!depot.CanUseDispatcherCounter(depot.DeliveryNpcMarker.position, customer + Vector3.forward * 10f), "Distant customer accepted.");
                    var npc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/_Project/Prefabs/DeliveryNPC.prefab"), scene);
                    npc.transform.SetParent(depot.DeliveryNpcMarker, false);
                    Check(npc.GetComponent<DeliveryNPC>().InteractionDistanceSquared(customer) == 0f, "NPC interaction does not use counter area.");
                    // Reproduce a player standing just outside the front of the box, rather
                    // than testing only its centre (which hid the original reach failure).
                    float side = Mathf.Sign(counter.center.z - root.transform.InverseTransformPoint(depot.DeliveryNpcMarker.position).z);
                    Vector3 approachLocal = counter.center;
                    approachLocal.y = root.transform.InverseTransformPoint(depot.DeliveryNpcMarker.position).y;
                    approachLocal.z += side * (counter.extents.z + .75f);
                    Vector3 approach = root.transform.TransformPoint(approachLocal);
                    float reach = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/_Project/Prefabs/Player.prefab").GetComponent<CouchGuys.Player.PlayerCouchCarrier>())
                        .FindProperty("m_interactionRange").floatValue;
                    Check(!counter.Contains(approachLocal), "Regression point must lie outside the counter box.");
                    Check(npc.GetComponent<DeliveryNPC>().InteractionDistanceSquared(approach) < reach * reach,
                        "Standing in front of the counter is outside the player's actual interaction reach.");
                    Check(npc.GetComponent<DeliveryNPC>().InteractionDistanceSquared(approach) <
                        (npc.transform.position - approach).sqrMagnitude, "Counter did not extend interaction across the desk.");

                    var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/_Project/Prefabs/Player.prefab"), scene);
                    var rig = Get<DepotDoorRig>(depot, "m_vehicleGarageDoor");
                    Check(rig.SafetyVolume != null, "Door safety volume missing.");
                    Vector3 door = rig.SafetyVolume.transform.TransformPoint(rig.SafetyVolume.center);
                    door.y = depot.DeliveryNpcMarker.position.y;
                    player.transform.position = door + Vector3.right * 100f;
                    Reset(depot); Physics.SyncTransforms();
                    Check(!depot.ShouldOpenAutomaticGarage(), "Empty garage did not close.");
                    player.transform.position = door + root.transform.forward;
                    Set(depot, "m_nextGarageCheck", -1f); Physics.SyncTransforms();
                    Check(depot.ShouldOpenAutomaticGarage(), "Nearby player did not open garage.");
                    player.transform.position = door + Vector3.right * 100f;
                    Set(depot, "m_nextGarageCheck", -1f); Physics.SyncTransforms();
                    Check(depot.ShouldOpenAutomaticGarage(), "Closing delay was not retained.");
                    Reset(depot);
                    Check(!depot.ShouldOpenAutomaticGarage(), "Garage did not close after delay.");

                    var vehicle = new GameObject("GaragePresenceTestTruck");
                    SceneManager.MoveGameObjectToScene(vehicle, scene);
                    truck = vehicle.AddComponent<TruckController>();
                    TruckController.ActiveTrucks.Add(truck);
                    vehicle.AddComponent<BoxCollider>().size = Vector3.one;
                    vehicle.transform.position = root.transform.TransformPoint(Get<Bounds>(depot, "m_truckInterior").center);
                    Reset(depot); Physics.SyncTransforms();
                    Check(depot.ShouldOpenAutomaticGarage(), "Truck inside did not hold door open.");
                    vehicle.transform.position = door + Vector3.right * 100f;
                    Reset(depot); Physics.SyncTransforms();
                    Check(!depot.ShouldOpenAutomaticGarage(), "Truck outside kept garage open.");
                    vehicle.transform.position = rig.SafetyVolume.transform.TransformPoint(rig.SafetyVolume.center);
                    Reset(depot); Physics.SyncTransforms();
                    Check(depot.IsDoorwayBlocked(DepotDoorKind.VehicleGarage) && depot.ShouldOpenAutomaticGarage(), "Doorway obstruction safety failed.");
                    TruckController.ActiveTrucks.Remove(truck); truck = null;
                    Object.DestroyImmediate(vehicle); Object.DestroyImmediate(player);
                    Object.DestroyImmediate(npc); Object.DestroyImmediate(root);
                    report.Add("PASS " + name + ": counter interaction at home; rejected away/distant interaction; empty auto-close; nearby player auto-open; closing delay; indoor truck hold-open; outdoor truck ignored; doorway safety.");
                }
                report.Add("Existing DeliveryManager door SyncVars retained. Multi-client gameplay/UI playtest outstanding.");
                File.WriteAllLines("Tools/DepotCounterDoorValidation.txt", report);
                Debug.Log(string.Join("\n", report));
            }
            finally
            {
                if (truck != null) TruckController.ActiveTrucks.Remove(truck);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Reset(DepotController depot)
        {
            Set(depot, "m_garageLastOccupied", float.NegativeInfinity);
            Set(depot, "m_nextGarageCheck", -1f);
        }
        private static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
