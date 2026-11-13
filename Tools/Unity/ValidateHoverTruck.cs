using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Vehicles;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ValidateHoverTruck
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Count(SerializedObject serialized, string name, int count)
    {
        var array = serialized.FindProperty(name);
        Require(array.arraySize == count, name + " count");
        var objects = Enumerable.Range(0, count).Select(i => array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
        Require(objects.All(o => o != null) && objects.Distinct().Count() == count, name + " references");
    }

    public static void VerifySceneAndExit()
    {
        try
        {
            Run();
            const string truckPath = "Assets/_Project/Prefabs/Vehicles/Truck.prefab";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/SampleScene.unity");
            var sceneTrucks = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TruckController>(true)).ToArray();
            Require(sceneTrucks.All(value => value.IsHoverTruck), "Every authored scene truck uses hover configuration");
            bool deliveryManager = scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<CouchGuys.Gameplay.Delivery.DeliveryManager>(true) != null);
            Require(deliveryManager || sceneTrucks.Length > 0, "Gameplay truck or delivery manager entry point");
            Require(AssetDatabase.GetDependencies("Assets/_Project/Resources/Shop/DeliveryManagerShop.asset", true).Contains(truckPath), "Gameplay shop resolves the hover truck");
            var purchase = AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Settings/Shop/truck_purchase.asset");
            var vehicle = new SerializedObject(purchase).FindProperty("m_vehiclePrefab").objectReferenceValue;
            Require(AssetDatabase.GetAssetPath(vehicle) == truckPath, "Purchase references the updated truck");
            var prefabs = AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.PrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            bool registered = Enumerable.Range(0, prefabs.GetObjectCount()).Any(index =>
                AssetDatabase.GetAssetPath(prefabs.GetObject(true, index)) == truckPath);
            Require(registered, "Truck remains registered for network spawning");
            File.AppendAllText("HoverTruckValidation.txt", "PASS gameplay delivery manager, shop catalogue, purchase and network spawn references resolve to the redesigned truck. Scene truck instances " + sceneTrucks.Length + ".\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/Truck.prefab");
        Require(prefab != null, "Saved truck prefab");
        var truck = prefab.GetComponent<TruckController>();
        var serialized = new SerializedObject(truck);
        Require(truck.IsHoverTruck, "Hover settings");
        Require(truck.Settings.CargoCapacity == 4, "Four couches");
        Require(truck.GetComponent<VehicleDestruction>() != null, "Existing destruction");
        foreach (var pair in new[] { ("m_seatAnchors", 4), ("m_exitPoints", 4), ("m_cargoPoints", 4),
            ("m_turretYaw", 3), ("m_turretPitch", 3), ("m_turretMuzzles", 3),
            ("m_hoverProbes", 6), ("m_hoverGlows", 6), ("m_dockClamps", 8) }) Count(serialized, pair.Item1, pair.Item2);
        Vector3 driver = truck.GetSeatAnchor(0).position;
        Vector3 front = truck.GetSeatAnchor(1).position;
        Vector3 rearLeft = truck.GetSeatAnchor(2).position;
        Vector3 rearRight = truck.GetSeatAnchor(3).position;
        Require(driver.x < front.x && driver.z > rearLeft.z && Mathf.Abs(driver.z - front.z) < .01f &&
            Mathf.Abs(rearLeft.z - rearRight.z) < .01f, "2 by 2 seat layout");
        Require(truck.GetCargoPoint(0).position.x < truck.GetCargoPoint(1).position.x &&
            truck.GetCargoPoint(0).position.z > truck.GetCargoPoint(2).position.z, "2 by 2 dock layout");
        Require(truck.GetCargoPoint(0).position.z < rearLeft.z - 1f, "Cargo behind passengers");
        for (int index = 0; index < 4; index++)
            Require(truck.FindEntrySeat(truck.GetDoorPoint(index).position) == index, "Individual seat entry " + index);
        var network = new SerializedObject(prefab.GetComponent<NetworkTransform>());
        Require(!network.FindProperty("_clientAuthoritative").boolValue, "Server transform authority");
        var behaviours = new SerializedObject(prefab.GetComponent<NetworkObject>()).FindProperty("NetworkBehaviours");
        Require(Enumerable.Range(0, behaviours.arraySize).Count(i => behaviours.GetArrayElementAtIndex(i).objectReferenceValue == truck) == 1,
            "Existing network behaviour registration");
        var turret = (TruckTurretSettings)serialized.FindProperty("m_turretSettings").objectReferenceValue;
        Require(turret.ProjectilePrefab != null && turret.ImpactPrefab != null && turret.YawLimit == 180f, "Turret assets and rear arcs");
        Require(turret.ProjectilePrefab.GetComponentsInChildren<Collider>().Length == 0, "Presentation bolt has no physics authority");
        var finite = typeof(TruckController).GetMethod("IsFiniteDirection", BindingFlags.Static | BindingFlags.NonPublic);
        Require(!(bool)finite.Invoke(null, new object[] { new Vector3(float.NaN, 0f, 1f) }), "Reject nonfinite aim");
        Require(!(bool)finite.Invoke(null, new object[] { Vector3.zero }), "Reject zero aim");
        Require((bool)finite.Invoke(null, new object[] { Vector3.back }), "Allow backward aim");
        ValidateSuspension(prefab);
        string report = "PASS saved prefab, four independent seat entries, 2x2 seats, 2x2 docks, three turret pivots, " +
            "six hover probes, eight clamps, server transform authority, existing network behaviour IDs, " +
            "finite aim validation, backward aiming, projectile/impact references, flat-ground suspension settle.\n" +
            "Separate remote-client ownership and automatic recipient delivery require end-to-end testing.\n";
        File.WriteAllText("HoverTruckValidation.txt", report);
        Debug.Log(report);
    }

    static void ValidateSuspension(GameObject prefab)
    {
        var ground = new GameObject("Hover validation ground");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        SimulationMode previous = Physics.simulationMode;
        try
        {
            Vector3 origin = new Vector3(10000f, 100f, 10000f);
            ground.transform.position = origin + Vector3.down * .5f;
            ground.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);
            instance.transform.position = origin + Vector3.up * 1.4f;
            var body = instance.GetComponent<Rigidbody>();
            body.isKinematic = false;
            var truck = instance.GetComponent<TruckController>();
            var tick = typeof(TruckController).GetMethod("TickHoverServer", Private);
            Physics.simulationMode = SimulationMode.Script;
            for (int step = 0; step < 300; step++)
            {
                Physics.SyncTransforms();
                tick.Invoke(truck, new object[] { .02f });
                Physics.Simulate(.02f);
            }
            float height = body.position.y - origin.y;
            Require(Mathf.Abs(height - .55f) < .12f && Mathf.Abs(body.linearVelocity.y) < .1f, "Suspension settle height " + height);
        }
        finally
        {
            Physics.simulationMode = previous;
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(ground);
        }
    }
}
