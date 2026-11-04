#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    [InitializeOnLoad]
    public static class GrapplerThiefPlayProbe
    {
        private const string Pending = "GrapplerThiefProbe.Pending";
        private const string Result = "Temp/GrapplerThiefPlayResults.txt";
        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static IEnumerator s_routine;
        private static NetworkManager s_network;
        static GrapplerThiefPlayProbe() { EditorApplication.playModeStateChanged += StateChanged; }

        [MenuItem("Couch Guys/Grappler Thief/Test Local Host")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside an existing play session.");
            File.WriteAllText(Result, "START: local FishNet host grapple test\n");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                s_routine = Probe();
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.ExitingPlayMode) { EditorApplication.update -= Tick; s_routine = null; }
        }
        private static void Tick()
        {
            try { if (s_routine != null && !s_routine.MoveNext()) Finish(); }
            catch (Exception e) { File.AppendAllText(Result, "FAIL: " + e + "\n"); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick;
            s_routine = null;
            if (s_network != null) { s_network.ClientManager.StopConnection(); s_network.ServerManager.StopConnection(true); }
            EditorApplication.ExitPlaymode();
        }
        private static void SetSync<T>(object target, string name, T value) =>
            ((SyncVar<T>)target.GetType().GetField(name, Fields).GetValue(target)).Value = value;

        private static IEnumerator Probe()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var bootstrap = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath("64e03b333ee845f5bff4d89fa9a228c4")));
            s_network = bootstrap.GetComponentInChildren<NetworkManager>();
            var spawner = s_network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>();
            if (spawner != null) Object.DestroyImmediate(spawner);
            yield return null;
            s_network.ServerManager.StartConnection();
            s_network.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 12f;
            while (s_network.ServerManager.Clients.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            if (s_network.ServerManager.Clients.Count == 0) throw new Exception("Local host client did not connect.");
            var owner = s_network.ServerManager.Clients.Values.First();
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = Vector3.down * 0.5f;
            floor.transform.localScale = new Vector3(100f, 1f, 100f);
            floor.AddComponent<NavMeshSurface>().BuildNavMesh();
            var deliveryRoot = new GameObject("Grapple test delivery");
            deliveryRoot.SetActive(false);
            var delivery = deliveryRoot.AddComponent<DeliveryManager>();
            var networkData = new SerializedObject(deliveryRoot.GetComponent<NetworkObject>());
            networkData.FindProperty("SceneId").ulongValue = 42;
            networkData.ApplyModifiedPropertiesWithoutUndo();
            var deliveryData = new SerializedObject(delivery);
            deliveryData.FindProperty("_networkObjectCache").objectReferenceValue = deliveryRoot.GetComponent<NetworkObject>();
            deliveryData.FindProperty("_addedNetworkObject").objectReferenceValue = deliveryRoot.GetComponent<NetworkObject>();
            deliveryData.ApplyModifiedPropertiesWithoutUndo();
            delivery.enabled = false;
            deliveryRoot.GetComponent<CouchGuys.ProceduralGeneration.NeighbourhoodGenerator>().enabled = false;
            deliveryRoot.SetActive(true);
            var couch = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Couch.prefab"),
                Vector3.up * 0.5f, Quaternion.identity);
            s_network.ServerManager.Spawn(couch);
            couch.GetComponent<CouchGuys.Gameplay.Couch.CouchCarryController>().AssignDeliveryServer(1);
            var deliveries = (SyncList<DeliveryInstance>)typeof(DeliveryManager).GetField("m_deliveries", Fields).GetValue(delivery);
            deliveries.Add(new DeliveryInstance { Id = 1, CouchObjectId = couch.ObjectId,
                DestinationIndex = 0, TierIndex = 1, Status = DeliveryState.Transport });
            var source = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab");
            var player = Object.Instantiate(source, Vector3.forward * 2f + Vector3.up * 0.05f, Quaternion.identity);
            s_network.ServerManager.Spawn(player, owner);
            var other = Object.Instantiate(source, new Vector3(4f, 0.05f, 10f), Quaternion.identity);
            s_network.ServerManager.Spawn(other, owner);
            var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Enemies/GrapplerThief.prefab"),
                Vector3.forward * 11f, Quaternion.Euler(0f, 180f, 0f));
            s_network.ServerManager.Spawn(enemy);
            var member = enemy.GetComponent<ThiefSpawnMember>() ?? enemy.gameObject.AddComponent<ThiefSpawnMember>();
            member.AssignDelivery(1);
            var grapple = enemy.GetComponent<GrapplerThief>();
            var health = player.GetComponent<PlayerHealth>();
            var motor = player.GetComponent<ThirdPersonPlayerController>();
            deadline = Time.realtimeSinceStartup + 12f;
            bool launched = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                float up = (float)typeof(ThirdPersonPlayerController).GetField("m_verticalVelocity", Fields).GetValue(motor);
                if (up > 3.5f)
                {
                    Vector3 velocity = (Vector3)typeof(ThirdPersonPlayerController).GetField("m_externalVelocity", Fields).GetValue(motor);
                    Vector3 away = Vector3.ProjectOnPlane(player.transform.position - couch.transform.position, Vector3.up).normalized;
                    if (Vector3.Dot(velocity, away) < 10f || velocity.magnitude > 13f)
                        throw new Exception("Throw direction or single-application velocity is incorrect: " + velocity);
                    File.AppendAllText(Result, $"PASS: host throw velocity={velocity}, upward={up:F2}; closest-to-couch target, one owner application.\n");
                    launched = true;
                    break;
                }
                if (grapple.Target != null && grapple.Target != health) throw new Exception("Selected player nearest enemy rather than couch.");
                yield return null;
            }
            if (!launched) throw new Exception("No successful grapple/throw within 12 seconds.");
            if ((bool)typeof(PlayerHealth).GetProperty("CanBeEnemyGrappled", Fields).GetValue(health))
                throw new Exception("Player lacks shared recovery protection.");
            float released = Time.time;
            while (Time.time < released + 3.5f) yield return null;
            if (!motor.enabled || !health.IsAlive || health.IsKnockedDown || player.GetComponent<CharacterController>().bounds.min.y < -0.1f)
                throw new Exception($"Player recovery: motor={motor.enabled}, alive={health.IsAlive}, knocked={health.IsKnockedDown}, position={player.transform.position}");
            File.AppendAllText(Result, "PASS: controls retained, shared recovery protection active, player remained above terrain.\n");
            enemy.GetComponent<ThiefHealth>().ApplyDamageServer(200f, Vector3.right, 1f);
            yield return null;
            if (enemy.GetComponent<ThiefHealth>().IsAlive) throw new Exception("Enemy did not die normally.");
            File.AppendAllText(Result, "PASS: network-spawned Grappler Thief takes damage and dies normally.\n");
        }
    }
}
#endif
