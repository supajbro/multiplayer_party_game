using System;
using System.Collections;
using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class GetawayTruckCarImpactPlayProbe
{
    private const string Pending = "GetawayCarImpact.Pending";
    private static IEnumerator s_test;
    private static NetworkManager s_network;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static GetawayTruckCarImpactPlayProbe()
    {
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += Tick;
    }

    [MenuItem("Couch Guys/Tests/Validate Getaway Car Impacts")]
    public static void Run()
    {
        SessionState.SetBool(Pending, true);
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (s_test == null)
            {
                SessionState.SetBool(Pending, false);
                s_test = Check();
            }
            if (s_test.MoveNext()) return;
            Debug.Log("PASS GETAWAY CAR IMPACTS: actual traffic and rogue collisions destroyed and launched both cars, dealt one configured damage per car, and ignored repeated wreck contacts.");
            Finish(0);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick; s_test = null;
        if (s_network != null)
        { s_network.ClientManager.StopConnection(); s_network.ServerManager.StopConnection(true); }
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else EditorApplication.ExitPlaymode();
    }

    private static IEnumerator Check()
    {
        var configuration = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
        var sourceCar = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Enemies/RogueCar.prefab");
        var context = new GameObject("Getaway car collision local host"); context.SetActive(false);
        s_network = context.AddComponent<NetworkManager>();
        var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
        prefabs.AddObject(configuration.TruckPrefab); prefabs.AddObject(sourceCar);
        s_network.SpawnablePrefabs = prefabs; context.SetActive(true);
        s_network.TransportManager.Transport.SetPort(22371);
        s_network.ServerManager.StartConnection(); s_network.ClientManager.StartConnection();
        float deadline = Time.realtimeSinceStartup + 12f;
        while (s_network.ServerManager.Clients.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        if (s_network.ServerManager.Clients.Count == 0) throw new Exception("Local collision test host did not connect.");
        Vector3 origin = new Vector3(45000, 0, 45000);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Ground car collision test";
        floor.transform.position = origin - Vector3.up * .5f; floor.transform.localScale = new Vector3(150, 1, 150);
        var truckObject = Object.Instantiate(configuration.TruckPrefab, origin, Quaternion.identity);
        s_network.ServerManager.Spawn(truckObject);
        truckObject.GetComponent<Rigidbody>().isKinematic = true;
        var truck = truckObject.GetComponent<TruckController>();
        var getaway = truckObject.GetComponent<GetawayTruckController>();
        float initialHealth = truck.Health;
        for (int test = 0; test < 2; test++)
        {
            Vector3 direction = test == 0 ? Vector3.forward : Vector3.right;
            var carObject = Object.Instantiate(sourceCar, origin + direction * 10f + Vector3.up * .1f, Quaternion.identity);
            var rogue = carObject.GetComponent<RogueCar>();
            if (test == 0) Object.DestroyImmediate(rogue);
            else rogue.enabled = false; // Keep its collision callback, without AI steering the setup.
            s_network.ServerManager.Spawn(carObject);
            var car = carObject.GetComponent<TrafficVehicleController>();
            var body = carObject.GetComponent<Rigidbody>();
            body.linearVelocity = -direction * 12f;
            deadline = Time.realtimeSinceStartup + 4f;
            while (!car.IsDestroyed && Time.realtimeSinceStartup < deadline) yield return null;
            if (!car.IsDestroyed || body.isKinematic || Vector3.Dot(body.linearVelocity, direction) < 10f ||
                body.linearVelocity.y < 3f || body.angularVelocity.magnitude < 1f)
                throw new Exception($"Car {test} was not destroyed and thrown back: destroyed={car.IsDestroyed}, velocity={body.linearVelocity}, angular={body.angularVelocity}.");
            float expected = initialHealth - (test + 1) * configuration.CarImpactDamage;
            if (Mathf.Abs(truck.Health - expected) > .01f) throw new Exception($"Truck received excess damage: {truck.Health}, expected {expected}.");
            typeof(GetawayTruckController).GetMethod("RepelCarServer", Hidden).Invoke(getaway, new object[] { car, 20f });
            if (Mathf.Abs(truck.Health - expected) > .01f) throw new Exception("Repeated wreck contact damaged the truck again.");
            s_network.ServerManager.Despawn(carObject);
        }
        s_network.ServerManager.Despawn(truckObject);
        Object.Destroy(floor);
    }
}
