using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Gameplay.Shop;
using UnityEditor;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    public static class VehicleAssetValidation
    {
        public static void Run()
        {
            VehicleAssetUpgrade.EnsureAssets();
            foreach (string name in new[] { "LightTruck", "GoodTruck", "BestTruck" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Vehicles/{name}.prefab");
                TruckController truck = prefab.GetComponent<TruckController>();
                for (int i = 0; i < 4; i++)
                {
                    Transform door = truck.GetDoorPoint(i), seat = truck.GetSeatAnchor(i);
                    Check(door != null && seat != null && seat.name.StartsWith("SeatAnchor_"), "Door/original seat missing");
                    Check(truck.FindEntrySeat(door.position) == i, "Door entry selects wrong seat");
                    string[] suffix = seat.name.Replace("SeatAnchor_", "").Split('_');
                    Renderer surface = prefab.GetComponentsInChildren<Renderer>(true).First(r => r.name == $"Cab_{suffix[1]}_{suffix[0]}_DoorSurface");
                    Check(Mathf.Abs(door.position.z - surface.bounds.center.z) < .02f, "Door alignment");
                    Check(Mathf.Abs(door.position.x - surface.bounds.center.x) >= .5f, "Door outward clearance");
                    Debug.Log($"VEHICLE_AUDIT {name} seat={i} marker={door.position} anchor={seat.position}");
                }
                Check(truck.GetSeatAnchor(0).name == "SeatAnchor_Front_Right", "Driver must use front right");
                Check(truck.FindEntrySeat(prefab.transform.position) == -1, "Centre entry remains enabled");
                SerializedProperty slats = new SerializedObject(truck).FindProperty("m_shutterSlats");
                Check(slats.arraySize == 15, "Shutter slats missing");
                Transform first = slats.GetArrayElementAtIndex(0).objectReferenceValue as Transform;
                Check(Vector3.Distance(first.position, new Vector3(0f, 1.3125f, -3.87f)) < .02f, "Shutter import axes incorrect");
                ValidateShutterAndExit(prefab);
            }
            GameObject car = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Traffic/TrafficCar.prefab");
            TrafficVehicleController controller = car.GetComponent<TrafficVehicleController>();
            Check(controller.VisualVariantCount == 1, "Traffic must use a single model");
            LODGroup group = car.GetComponentInChildren<LODGroup>(true);
            Check(car.GetComponentsInChildren<LODGroup>(true).Length == 1, "Duplicate imported traffic LOD groups");
            Check(group != null && group.GetLODs().Length == 3, "Traffic LODs missing");
            long previous = long.MaxValue;
            foreach (LOD lod in group.GetLODs())
            {
                long tris = lod.renderers.Sum(r => (long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                Check(tris < previous, "LODs do not reduce geometry"); previous = tris;
                Debug.Log($"VEHICLE_AUDIT LOD transition={lod.screenRelativeTransitionHeight} triangles={tris}");
            }
            SerializedObject wheels = new(car.GetComponentInChildren<TrafficWheelAnimator>(true));
            Check(wheels.FindProperty("m_frontWheels").arraySize == 6 && wheels.FindProperty("m_rearWheels").arraySize == 6, "Wheels missing across LODs");
            Material glass = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Resources/Traffic/Hatchback_Glass.mat");
            Check(glass != null && glass.GetFloat("_Surface") == 1f && glass.GetColor("_BaseColor").a < .5f, "Glass is opaque");
            ShopCatalogue shop = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
            Check(shop.Items.Count(i => i != null && i.PurchaseBehaviour is VehiclePurchaseBehaviour) == 1, "Extra trucks remain purchasable");
            Check(shop.Find("van_light") == shop.Find("truck_light") && shop.Find("truck_light").Price == 250, "Legacy purchase ID/price compatibility");
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/vehicle-validation-passed.txt", "Vehicle prefab, door alignment, original seat anchors, shutter axes, traffic LODs/wheels/glass and shop checks passed.");
            Debug.Log("VEHICLE_VALIDATION_PASSED");
        }

        private static void Check(bool success, string message)
        {
            if (!success) throw new Exception(message);
        }

        private static void ValidateShutterAndExit(GameObject prefab)
        {
            Vector3 origin = new(10000f, 0f, 10000f);
            GameObject instance = Object.Instantiate(prefab, origin, Quaternion.identity);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject player = new("Exit validation player");
            GameObject blocker = null;
            try
            {
                TruckController truck = instance.GetComponent<TruckController>();
                typeof(TruckController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(truck, null);
                SerializedObject so = new(truck);
                Transform first = so.FindProperty("m_shutterSlats").GetArrayElementAtIndex(0).objectReferenceValue as Transform;
                Vector3 closed = first.position;
                MethodInfo shutter = typeof(TruckController).GetMethod("UpdateShutter", BindingFlags.Instance | BindingFlags.NonPublic);
                shutter.Invoke(truck, new object[] { 1f });
                Check(first.position.y > origin.y + 3.7f && first.position.z > closed.z + .25f, "Shutter did not roll around the roof track");
                shutter.Invoke(truck, new object[] { 0f });
                Check(Vector3.Distance(first.position, closed) < .005f, "Shutter did not restore its closed position");
                ground.transform.position = origin + Vector3.down * .5f;
                ground.transform.localScale = new Vector3(30f, 1f, 30f);
                player.transform.position = origin + Vector3.up;
                NetworkObject no = player.AddComponent<NetworkObject>();
                CharacterController capsule = player.AddComponent<CharacterController>();
                capsule.height = 1.8f; capsule.radius = .35f; capsule.center = Vector3.up * .9f;
                FieldInfo driverField = typeof(TruckController).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .First(f => typeof(SyncVar<NetworkObject>).IsAssignableFrom(f.FieldType) && f.Name.Contains("m_driver"));
                var driver = (SyncVar<NetworkObject>)driverField.GetValue(truck);
                driver.SetInitialValues(no);
                Physics.SyncTransforms();
                Check(truck.TryGetExitPosition(no, out Vector3 exit, out Quaternion rotation), "Grounded driver exit failed");
                Check(Mathf.Abs(exit.y - .04f) < .02f && exit.x > origin.x + 1.6f, "Exit is not ground aligned/outside its door");
                Check(Vector3.Dot(rotation * Vector3.forward, Vector3.right) > .99f, "Exit does not face away from truck");
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.position = truck.GetDoorPoint(0).position + Vector3.right * .6f + Vector3.up;
                blocker.transform.localScale = new Vector3(3f, 2f, 2f);
                Physics.SyncTransforms();
                Check(!truck.TryGetExitPosition(no, out _, out _), "Blocked door allowed an unsafe exit");
                Check(truck.FindEntrySeat(truck.GetDoorPoint(0).position) == -1, "Occupied driver door redirects to another seat");
            }
            finally
            {
                Object.DestroyImmediate(instance); Object.DestroyImmediate(ground); Object.DestroyImmediate(player);
                if (blocker != null) Object.DestroyImmediate(blocker);
            }
        }
    }
}
