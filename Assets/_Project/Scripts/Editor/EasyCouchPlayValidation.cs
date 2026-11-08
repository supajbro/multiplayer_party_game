using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit host/independent late-client integration probe. No saved scenes are changed.</summary>
    [InitializeOnLoad]
    public static class EasyCouchPlayValidation
    {
        private const string Request = "Temp/EasyCouches.Play.request", Pending = "EasyCouches.Play.Pending";
        private const string Report = "Tools/Blender/Exports/EasyCouches/network_validation.txt";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static IEnumerator s_probe;
        private static NetworkManager s_host, s_remote;
        static EasyCouchPlayValidation()
        {
            EditorApplication.update += CheckRequest;
            EditorApplication.playModeStateChanged += StateChanged;
        }
        private static void CheckRequest()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request); Run();
        }
        [MenuItem("Couch Guys/Couches/Test Easy Couch Networking")]
        public static void Run()
        {
            File.WriteAllText(Report, "START: Easy couch host/independent client validation\n");
            SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); s_probe = Probe(); EditorApplication.update += Tick; }
            if (state == PlayModeStateChange.ExitingPlayMode) { EditorApplication.update -= Tick; s_probe = null; }
        }
        private static void Tick()
        {
            try
            {
                if (s_remote != null && s_remote.IsClientStarted)
                    foreach (var replica in s_remote.ClientManager.Objects.Spawned.Values)
                        foreach (var collider in replica.GetComponentsInChildren<Collider>()) collider.enabled = false;
                if (s_probe != null && !s_probe.MoveNext()) Finish();
            }
            catch (Exception e) { File.AppendAllText(Report, "FAIL: " + e + "\n"); Debug.LogException(e); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick; s_probe = null;
            if (s_remote != null) s_remote.ClientManager.StopConnection();
            if (s_host != null) { s_host.ServerManager.StopConnection(false); s_host.ClientManager.StopConnection(); }
            EditorApplication.ExitPlaymode();
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); File.AppendAllText(Report, "PASS: " + message + "\n"); }
        private static GameObject Visual(CouchCarryController couch) => (GameObject)typeof(CouchCarryController).GetField("m_modelInstance", Private).GetValue(couch);
        private static IEnumerator Probe()
        {
            Application.runInBackground = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var manager in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(manager.gameObject);
            var origin = new Vector3(22000, 0, 22000);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = origin + Vector3.down * .5f;
            floor.transform.localScale = new Vector3(100, 1, 100);
            var boot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"));
            s_host = boot.GetComponentInChildren<NetworkManager>();
            Object.DestroyImmediate(s_host.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>());
            yield return null;
            s_host.TransportManager.Transport.SetMaximumClients(4); s_host.TransportManager.Transport.SetPort(7793);
            s_host.ServerManager.StartConnection(); s_host.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 20;
            while ((!s_host.IsClientStarted || s_host.ServerManager.Clients.Count == 0) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_host.IsClientStarted && s_host.ServerManager.Clients.Count > 0, "FishNet loopback host started");
            var owner = s_host.ServerManager.Clients.Values.First(); s_host.SceneManager.AddConnectionToScene(owner, SceneManager.GetActiveScene());
            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Couch.prefab");
            var couches = new CouchCarryController[10];
            var priorRandom = UnityEngine.Random.state; UnityEngine.Random.InitState(84123);
            for (int i = 0; i < 10; i++)
            {
                var obj = Object.Instantiate(prefab, origin + Vector3.right * (i * 4), Quaternion.identity);
                s_host.ServerManager.Spawn(obj); var couch = obj.GetComponent<CouchCarryController>(); couches[i] = couch;
                couch.AssignDeliveryServer(500 + i); couch.SetDepotTransportActiveServer(true);
                int attempts = 0;
                do { couch.SelectRandomModelVariantServer(true); attempts++; yield return null; }
                while (couch.ModelVariantIndex != 3 + i && attempts < 200);
                Check(couch.ModelVariantIndex == 3 + i && Visual(couch).name == $"EasyCouch_{i + 1:00}", $"Server random selection reaches couch {i + 1:00}");
                var bounds = Visual(couch).GetComponent<CouchVisualBounds>().CollisionBounds;
                Check(Vector3.Distance(couch.GetComponent<BoxCollider>().size, bounds.size) < .001f && couch.DeliveryId == 500 + i,
                    $"Couch {i + 1:00} applies its saved collision envelope and retains delivery identity");
            }
            UnityEngine.Random.state = priorRandom;
            Check(couches.Select(c => c.ModelVariantIndex).Distinct().Count() == 10, "Ten simultaneous deliveries retain ten independent variants");
            var remoteRoot = new GameObject("Easy couch independent loopback client"); remoteRoot.SetActive(false);
            s_remote = remoteRoot.AddComponent<NetworkManager>();
            typeof(NetworkManager).GetField("_persistence", Private).SetValue(s_remote, NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad", Private).SetValue(s_remote, false);
            s_remote.SpawnablePrefabs = s_host.SpawnablePrefabs; remoteRoot.SetActive(true); yield return null;
            s_remote.TransportManager.Transport.SetClientAddress("127.0.0.1"); s_remote.TransportManager.Transport.SetPort(7793);
            s_remote.ClientManager.StartConnection(); deadline = Time.realtimeSinceStartup + 20;
            while ((!s_remote.IsClientStarted || s_host.ServerManager.Clients.Count < 2) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_remote.IsClientOnlyStarted && s_host.ServerManager.Clients.Count == 2, "Independent late client connected");
            var remoteOwner = s_host.ServerManager.Clients.Values.First(c => c.ClientId != owner.ClientId);
            s_host.SceneManager.AddConnectionToScene(remoteOwner, SceneManager.GetActiveScene());
            deadline = Time.realtimeSinceStartup + 15;
            while (couches.Any(c => !s_remote.ClientManager.Objects.Spawned.ContainsKey(c.ObjectId)) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            foreach (var couch in couches)
            {
                Check(s_remote.ClientManager.Objects.Spawned.TryGetValue(couch.ObjectId, out var obj), "Late client received delivery " + couch.DeliveryId);
                var replica = obj.GetComponent<CouchCarryController>();
                Check(replica.ModelVariantIndex == couch.ModelVariantIndex && replica.DeliveryId == couch.DeliveryId &&
                    Visual(replica) != null && Visual(replica).name == Visual(couch).name, "Late client reconstructs visual and identity " + couch.DeliveryId);
                Check(Vector3.Distance(replica.GetComponent<BoxCollider>().size, couch.GetComponent<BoxCollider>().size) < .001f,
                    "Late client uses identical collision bounds " + couch.DeliveryId);
            }
            var first = couches[0]; first.SelectRandomModelVariantServer(false);
            float until = Time.time + .5f; while (Time.time < until) yield return null;
            var firstReplica = s_remote.ClientManager.Objects.Spawned[first.ObjectId].GetComponent<CouchCarryController>();
            Check(first.ModelVariantIndex < 3 && firstReplica.ModelVariantIndex == first.ModelVariantIndex &&
                first.GetComponent<BoxCollider>().size == new Vector3(2.1f,.9f,.9f), "Legacy selection keeps original models and restores original collider on both peers");
            first.SelectRandomModelVariantServer(true);
            until = Time.time + .5f; while (Time.time < until) yield return null;
            Check(firstReplica.ModelVariantIndex == first.ModelVariantIndex && Visual(firstReplica).name == Visual(first).name,
                "Connected client receives subsequent Easy visual changes");
            // Exercise all four existing player grab contracts on each saved visual.
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab"), origin, Quaternion.identity);
            s_host.ServerManager.Spawn(player, owner);
            player.GetComponent<ThirdPersonPlayerController>().enabled = false;
            player.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled = false;
            until = Time.time + 1f; while (Time.time < until) yield return null;
            var carrier = player.GetComponent<PlayerCouchCarrier>();
            var grab = typeof(CouchCarryController).GetMethod("TryGrabPointServer", Private, null, new[] { typeof(PlayerCouchCarrier), typeof(int) }, null);
            var release = typeof(CouchCarryController).GetMethod("ReleaseAllOccupantsServer", Private);
            foreach (var couch in couches)
            {
                couch.SetDepotTransportActiveServer(false);
                for (int point = 0; point < 4; point++)
                {
                    Check((bool)grab.Invoke(couch, new object[] { carrier, point }), $"{Visual(couch).name} accepts grab point {point}");
                    release.Invoke(couch, null);
                    Check(!carrier.IsCarrying && couch.ActiveCarrierCount == 0, "Drop releases player and couch occupancy");
                }
                couch.SetDepotTransportActiveServer(true);
            }
            File.AppendAllText(Report, "COMPLETE: ten simultaneous variants, late join, live replication, collision bounds, original models, all forty grab/drop contracts.\n");
        }
    }
}
