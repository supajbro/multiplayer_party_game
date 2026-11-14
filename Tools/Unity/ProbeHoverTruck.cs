using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit host-side integration probe in an isolated AITest scene.</summary>
[InitializeOnLoad]
public static class ProbeHoverTruck
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Pending = "ProbeHoverTruck.Pending";
    static int stage;
    static double deadline;
    static NetworkManager manager;
    static TruckController truck;
    static TruckController target;
    static PlayerTruckInteractor[] players;
    static CouchCarryController[] couches;
    static float targetHealth;
    static float ownHealth;
    static Vector3 movementStart;
    static object Invoke(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private).Invoke(value, args);
    static object Field(object value, string field) => value.GetType().GetField(field, Private | BindingFlags.Public).GetValue(value);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static ProbeHoverTruck()
    {
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += Tick;
    }
    public static void Run()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/AITest.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static T Spawn<T>(string path, Vector3 position) where T : Component
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var instance = Object.Instantiate(prefab, position, Quaternion.identity);
        manager.ServerManager.Spawn(instance);
        return instance.GetComponent<T>();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || !Application.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            double now = Time.timeAsDouble;
            if (stage == 0)
            {
                var floor = new GameObject("Ground_HoverProbe");
                floor.AddComponent<AudioListener>();
                floor.transform.position = Vector3.down * .5f;
                floor.AddComponent<BoxCollider>().size = new Vector3(200f, 1f, 200f);
                var root = new GameObject("Hover probe network manager");
                root.SetActive(false);
                manager = root.AddComponent<NetworkManager>();
                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                foreach (string path in new[] { "Assets/_Project/Prefabs/Vehicles/Truck.prefab", "Assets/_Project/Prefabs/Player.prefab", "Assets/_Project/Prefabs/Couch.prefab" })
                    prefabs.AddObject(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkObject>());
                manager.SpawnablePrefabs = prefabs;
                root.SetActive(true);
                Require(manager.ServerManager.StartConnection(27772), "Start isolated server");
                deadline = now + 20;
                stage = 1;
            }
            else if (stage == 1)
            {
                Require(now < deadline, "Server start timeout");
                if (!manager.IsServerStarted) return;
                manager.ClientManager.StartConnection();
                stage = 2;
            }
            else if (stage == 2)
            {
                Require(now < deadline, "Host client timeout");
                if (!manager.IsClientStarted) return;
                truck = Spawn<TruckController>("Assets/_Project/Prefabs/Vehicles/Truck.prefab", Vector3.up * .55f);
                target = Spawn<TruckController>("Assets/_Project/Prefabs/Vehicles/Truck.prefab", new Vector3(13f, .55f, 2.8f));
                players = Enumerable.Range(0, 4).Select(i => Spawn<PlayerTruckInteractor>("Assets/_Project/Prefabs/Player.prefab", new Vector3(-10f - i * 2f, .1f, 10f))).ToArray();
                couches = Enumerable.Range(0, 4).Select(i => Spawn<CouchCarryController>("Assets/_Project/Prefabs/Couch.prefab", new Vector3(0f, 1f, -10f - i * 3f))).ToArray();
                for (int index = 0; index < 4; index++)
                {
                    couches[index].AssignDeliveryServer(700 + index);
                    Transform dock = truck.GetCargoPoint(index);
                    players[0].transform.position = dock.position + Vector3.right * (index % 2 == 0 ? -1.7f : 1.7f);
                    couches[index].transform.position = players[0].transform.position;
                    Require((bool)Invoke(truck, "TryLoadCouchServer", players[0], couches[index]), "Load dock " + index);
                }
                deadline = now + 1.5;
                stage = 3;
            }
            else if (stage == 3 && now >= deadline)
            {
                Require(truck.LoadedCouchCount == 4, "Four occupied docks");
                for (int index = 0; index < 4; index++)
                {
                    Require(couches[index].IsLoaded && couches[index].LoadedTruckObject == truck.NetworkObject && couches[index].DeliveryId == 700 + index, "Cargo identity " + index);
                    Require(Vector3.Distance(couches[index].transform.position, truck.GetCargoPoint(index).position) < .05f,
                        "Cargo attachment " + index + " at " + couches[index].transform.position + " expected " + truck.GetCargoPoint(index).position +
                        " parent " + couches[index].transform.parent + " transition " + Field(couches[index], "m_cargoTransition"));
                    object[] deliveryArgs = { null };
                    bool deliveryVehicle = (bool)couches[index].GetType().GetMethod("TryGetDeliveryVehicle", Private).Invoke(couches[index], deliveryArgs);
                    Require(deliveryVehicle && (Transform)deliveryArgs[0] == truck.transform, "Delivery vehicle contract " + index);
                }
                players[0].transform.position = truck.GetCargoPoint(1).position + Vector3.right * 1.7f;
                Require((bool)Invoke(truck, "TryUnloadFirstCouchServer", players[0]), "Individual unload");
                Require(!couches[1].IsLoaded && truck.LoadedCouchCount == 3, "Selected couch unloaded");
                for (int index = 0; index < 4; index++)
                {
                    players[index].transform.position = truck.GetDoorPoint(index).position;
                    Require(truck.TryEnterServer(players[index], out int seat) && seat == index, "Seat occupancy " + index);
                    ((SyncVar<int>)Field(players[index], "m_currentSeatIndex")).Value = index;
                    ((SyncVar<NetworkObject>)Field(players[index], "m_currentTruckObject")).Value = truck.NetworkObject;
                }
                Require(truck.OccupantCount == 4 && !truck.HasAvailableSeat, "Four simultaneous occupants");
                truck.SetInputServer(players[1], Vector2.one);
                Require((Vector2)Field(truck, "m_driveInput") == Vector2.zero, "Passenger driving rejected");
                Invoke(truck, "SetTurretInputServer", players[0], Vector3.back, true);
                Require(!((bool[])Field(truck, "m_turretFire")).Any(f => f), "Driver turret input rejected");
                targetHealth = target.Health;
                ownHealth = truck.Health;
                deadline = now + 2;
                stage = 4;
            }
            else if (stage == 4)
            {
                var muzzle = ((Transform[])Field(truck, "m_turretMuzzles"))[0];
                Vector3 targetDirection = (target.transform.position + Vector3.up * 1.1f - muzzle.position).normalized;
                Invoke(truck, "SetTurretInputServer", players[1], targetDirection, true);
                Invoke(truck, "SetTurretInputServer", players[2], Vector3.back, true);
                Invoke(truck, "SetTurretInputServer", players[3], Vector3.left, true);
                if (now < deadline) return;
                Require(target.Health < targetHealth, "Projectile damage on another truck");
                Require(Mathf.Approximately(truck.Health, ownHealth), "Own truck excluded from shots");
                for (int index = 1; index < 4; index++)
                    Require(Field(players[index].GetComponent<MagneticCarryAura>(), "m_effect") != null, "Passenger magnetic turret aura " + index);
                Require(Field(players[0].GetComponent<MagneticCarryAura>(), "m_effect") == null, "Driver has no turret aura");
                var aims = new[] { "m_turretAim0", "m_turretAim1", "m_turretAim2" }.Select(name => ((SyncVar<Vector2>)Field(truck, name)).Value).ToArray();
                Require(Mathf.Abs(Mathf.DeltaAngle(aims[0].x, 90f)) < 3f && Mathf.Abs(Mathf.DeltaAngle(aims[1].x, 180f)) < 1f && Mathf.Abs(Mathf.DeltaAngle(aims[2].x, -90f)) < 1f, "Independent turret poses including backward aim");
                for (int index = 1; index < 4; index++) Invoke(truck, "SetTurretInputServer", players[index], Vector3.forward, false);
                movementStart = truck.transform.position;
                deadline = now + .8;
                stage = 5;
            }
            else if (stage == 5)
            {
                truck.SetInputServer(players[0], new Vector2(.2f, 1f));
                if (now < deadline) return;
                Require(Vector3.Distance(movementStart, truck.transform.position) > .4f, "Driver movement");
                for (int index = 0; index < 4; index++)
                    if (index != 1) Require(Vector3.Distance(couches[index].transform.position, truck.GetCargoPoint(index).position) < .1f, "Moving cargo attachment " + index);
                truck.Settings.Destruction.DestroyedVehicleLifetime = .4f;
                truck.ApplyBulletDamageServer(truck.Health * 2f);
                Require(truck.IsDestroyed && truck.OccupantCount == 0 && players.All(p => p.CurrentTruck == null), "Destroyed truck releases all passengers");
                Require(couches.All(c => !c.IsLoaded && c.DeliveryId >= 700), "Destruction releases cargo and preserves delivery IDs");
                deadline = now + 1.5;
                stage = 6;
            }
            else if (stage == 6 && now >= deadline)
            {
                Require(truck == null || !truck.IsSpawned, "Destroyed truck despawns");
                Require(players.All(player => Field(player.GetComponent<MagneticCarryAura>(), "m_effect") == null), "Turret auras release after dismount");
                Finish(0, "PASS host server/client startup, four networked occupants, passenger driving rejection, driver turret rejection, " +
                    "three independent turret poses including rear aiming, swept projectile damage to another vehicle, own-truck exclusion, " +
                    "four couch attachments with delivery IDs, delivery vehicle lookup, selected couch unloading, driver movement, moving cargo alignment, passenger turret auras, aura cleanup on dismount, destruction dismount/cargo recovery and despawn.\n" +
                    "Separate remote client ownership and automatic recipient delivery remain unverified.\n");
            }
        }
        catch (Exception error) { Finish(1, "FAIL " + error + "\n"); }
    }

    static void Finish(int exitCode, string message)
    {
        SessionState.SetBool(Pending, false);
        EditorApplication.update -= Tick;
        File.WriteAllText("HoverTruckNetworkValidation.txt", message);
        Debug.Log(message);
        manager?.ServerManager.StopConnection(true);
        EditorApplication.Exit(exitCode);
    }
}
