using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit edit checks and real host/independent late-client presentation checks.</summary>
    [InitializeOnLoad]
    public static class CarryPresentationValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string Result = "Temp/CarryPresentation.Play.result", Pending = "CarryPresentation.Play.Pending";
        static NetworkManager host, remote;
        static IEnumerator probe;
        static TestRunnerApi runner;
        static CarryPresentationValidation()
        {
            EditorApplication.update += CheckRequest;
            EditorApplication.playModeStateChanged += StateChanged;
        }
        static void CheckRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists("Temp/CarryPresentation.Tests.request"))
            {
                File.Delete("Temp/CarryPresentation.Tests.request");
                RunTests();
            }
            if (File.Exists("Temp/CarryPresentation.Play.request"))
            {
                File.Delete("Temp/CarryPresentation.Play.request");
                RunNetworkProbe();
            }
        }
        [MenuItem("Couch Guys/Thieves/Test Carry Presentation")]
        public static void RunTests()
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
            runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode,
                testNames = new[] { "CouchGuys.Tests.Editor.CarryPresentationTests" } }));
        }
        sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor t) { }
            public void TestStarted(ITestAdaptor t) { }
            public void TestFinished(ITestResultAdaptor t) { }
            public void RunFinished(ITestResultAdaptor t)
            {
                TestRunnerApi.SaveResultToFile(t, "Temp/CarryPresentation.Tests.xml");
                File.WriteAllText("Temp/CarryPresentation.Tests.result", $"{t.PassCount} passed, {t.FailCount} failed");
                Object.DestroyImmediate(runner);
            }
        }
        [MenuItem("Couch Guys/Thieves/Test Carry Presentation Networking")]
        public static void RunNetworkProbe()
        {
            File.WriteAllText(Result, "START\n");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                probe = Probe();
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.ExitingPlayMode) { EditorApplication.update -= Tick; probe = null; }
        }
        static void Tick()
        {
            try
            {
                if (remote != null && remote.IsClientStarted)
                    foreach (var replica in remote.ClientManager.Objects.Spawned.Values)
                        foreach (var collider in replica.GetComponentsInChildren<Collider>()) collider.enabled = false;
                if (probe != null && !probe.MoveNext()) Finish();
            }
            catch (Exception e) { File.AppendAllText(Result, "FAIL: " + e + "\n"); Finish(); }
        }
        static void Finish()
        {
            EditorApplication.update -= Tick;
            probe = null;
            if (remote != null) remote.ClientManager.StopConnection();
            if (host != null) { host.ServerManager.StopConnection(false); host.ClientManager.StopConnection(); }
            EditorApplication.ExitPlaymode();
        }
        static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            File.AppendAllText(Result, "PASS: " + description + "\n");
        }
        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static void Sync<T>(object target, string name, T value) => Field<SyncVar<T>>(target, name).Value = value;
        static NetworkObject Spawn(string path, Vector3 position, NetworkConnection owner = null)
        {
            var obj = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/" + path + ".prefab"), position, Quaternion.identity);
            host.ServerManager.Spawn(obj, owner);
            return obj;
        }
        static void Presentation(NetworkObject obj, bool carrying, string label)
        {
            var aura = obj.GetComponent<MagneticCarryAura>();
            var effect = Field<MagneticCarryEffect>(aura, "m_effect");
            Check((effect != null) == carrying, label + " aura matches attachment");
            if (carrying)
            {
                Check(Field<Transform>(aura, "m_torso") != null && Field<Transform[]>(aura, "m_hands").All(h => h != null), label + " aura has torso and both hands");
                Check(effect.GetComponentsInChildren<LineRenderer>().Any(l => l.positionCount > 1 && l.GetPosition(0) != l.GetPosition(1)), label + " aura streams render");
            }
            var enemy = obj.GetComponent<EnemyWeapon>();
            if (enemy != null)
                Check(Field<Renderer[]>(enemy, "m_heldWeaponRenderers").All(r => r.forceRenderingOff == carrying), label + " held weapon visibility");
            var player = obj.GetComponent<PlayerWeaponController>();
            if (player != null)
                Check(Field<Transform[]>(player, "m_weaponModels").Where(m => m != null).All(m => !m.gameObject.activeSelf) == carrying, label + " player weapon visibility");
        }
        static IEnumerator Probe()
        {
            Application.runInBackground = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var network in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(network.gameObject);
            var boot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"));
            host = boot.GetComponentInChildren<NetworkManager>();
            Object.DestroyImmediate(host.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>());
            yield return null;
            host.TransportManager.Transport.SetMaximumClients(4);
            host.TransportManager.Transport.SetPort(7791);
            host.ServerManager.StartConnection(); host.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 20;
            while ((!host.IsClientStarted || host.ServerManager.Clients.Count == 0) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(host.IsClientStarted && host.ServerManager.Clients.Count > 0, "Host connected");
            var owner = host.ServerManager.Clients.Values.First();
            host.SceneManager.AddConnectionToScene(owner, SceneManager.GetActiveScene());
            Vector3 origin = new(20000, 5, 20000);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = origin + Vector3.down * .5f;
            floor.transform.localScale = new Vector3(50, 1, 50);
            Physics.SyncTransforms();
            var couch = Spawn("Couch", origin).GetComponent<CouchCarryController>();
            var sled = Spawn("Vehicles/Sled", origin + Vector3.right * 4).GetComponent<CouchCarryController>();
            var thief = Spawn("Enemies/Thief_Pistol", origin + Vector3.back);
            var navigator = thief.GetComponent<ThiefNavigator>();
            navigator.enabled = false; thief.GetComponent<ThiefHealth>().enabled = false;
            thief.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
            var player = Spawn("Player", origin + Vector3.left * 2, owner);
            player.GetComponent<ThirdPersonPlayerController>().enabled = false;
            player.GetComponent<PlayerHealth>().enabled = false;
            player.GetComponent<PlayerCouchCarrier>().enabled = false;
            foreach (var body in new[] { couch.CouchRigidbody, sled.CouchRigidbody }) { body.useGravity = false; body.isKinematic = true; }
            couch.enabled = false; sled.enabled = false;
            ((ICouchCarrierServer)navigator).SetCarriedCouchServer(couch, 0);
            ((ICouchCarrierServer)player.GetComponent<PlayerCouchCarrier>()).SetCarriedCouchServer(couch, 1);
            Sync(player.GetComponent<PlayerWeaponController>(), "m_equippedIndex", 0);
            float until = Time.realtimeSinceStartup + .6f; while (Time.realtimeSinceStartup < until) yield return null;
            Presentation(thief, true, "Host thief couch"); Presentation(player, true, "Host player couch");
            var remoteRoot = new GameObject("Carry presentation independent client"); remoteRoot.SetActive(false);
            remote = remoteRoot.AddComponent<NetworkManager>();
            typeof(NetworkManager).GetField("_persistence", Private).SetValue(remote, NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad", Private).SetValue(remote, false);
            remote.SpawnablePrefabs = host.SpawnablePrefabs; remoteRoot.SetActive(true); yield return null;
            remote.TransportManager.Transport.SetClientAddress("127.0.0.1"); remote.TransportManager.Transport.SetPort(7791); remote.ClientManager.StartConnection();
            deadline = Time.realtimeSinceStartup + 15;
            while ((!remote.IsClientStarted || host.ServerManager.Clients.Count < 2) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(remote.IsClientOnlyStarted && host.ServerManager.Clients.Count == 2, "Independent late client connected");
            host.SceneManager.AddConnectionToScene(host.ServerManager.Clients.Values.First(c => c.ClientId != owner.ClientId), player.gameObject.scene);
            deadline = Time.realtimeSinceStartup + 10;
            while ((!remote.ClientManager.Objects.Spawned.ContainsKey(thief.ObjectId) || !remote.ClientManager.Objects.Spawned.ContainsKey(player.ObjectId)) && Time.realtimeSinceStartup < deadline) yield return null;
            var remoteThief = remote.ClientManager.Objects.Spawned[thief.ObjectId];
            var remotePlayer = remote.ClientManager.Objects.Spawned[player.ObjectId];
            until = Time.realtimeSinceStartup + .8f; while (Time.realtimeSinceStartup < until) yield return null;
            Presentation(remoteThief, true, "Late client thief couch"); Presentation(remotePlayer, true, "Late client player couch");
            foreach (var target in new[] { sled, couch })
            {
                ((ICouchCarrierServer)navigator).SetCarriedCouchServer(target, 0);
                ((ICouchCarrierServer)player.GetComponent<PlayerCouchCarrier>()).SetCarriedCouchServer(target, 1);
                Sync(player.GetComponent<PlayerWeaponController>(), "m_equippedIndex", 2);
                until = Time.realtimeSinceStartup + .6f; while (Time.realtimeSinceStartup < until) yield return null;
                Presentation(thief, true, "Host grab " + target.name); Presentation(remoteThief, true, "Client grab " + target.name);
                Presentation(player, true, "Host weapon switch " + target.name); Presentation(remotePlayer, true, "Client weapon switch " + target.name);
                Check(remoteThief.GetComponent<ThiefNavigator>().CarriedCouch.NetworkObject.ObjectId == target.ObjectId, "Client follows selected carry target");
                ((ICouchCarrierServer)navigator).ClearCarriedCouchServer(target, 0);
                ((ICouchCarrierServer)player.GetComponent<PlayerCouchCarrier>()).ClearCarriedCouchServer(target, 1);
                until = Time.realtimeSinceStartup + .8f; while (Time.realtimeSinceStartup < until) yield return null;
                Presentation(thief, false, "Host release"); Presentation(remoteThief, false, "Client release");
                Presentation(player, false, "Host player release"); Presentation(remotePlayer, false, "Client player release");
            }
            File.AppendAllText(Result, "COMPLETE\n");
        }
    }
}
