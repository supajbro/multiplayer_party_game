using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    [InitializeOnLoad]
    public static class ThiefDepartureValidation
    {
        private const string Pending = "ThiefDeparture.Probe";
        private const string Report = "Tools/ThiefDepartureValidation.txt";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator s_probe;
        private static IEnumerator s_wait;
        private static NetworkManager s_host;
        private static NetworkManager s_remote;
        private static NavMeshDataInstance s_navMesh;
        private static bool s_failed;

        static ThiefDepartureValidation() => EditorApplication.playModeStateChanged += OnState;

        public static void RunBatch()
        {
            try
            {
                Directory.CreateDirectory("Temp");
                ThiefDepartureAuthoring.Apply();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, "Temp/AITest.unity");
                EditorApplication.delayCall += Run;
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        [MenuItem("Couch Guys/Animation/Test Thief Departure")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before starting the departure probe.");
            Directory.CreateDirectory("Tools");
            File.WriteAllText(Report, "Starting thief departure integration checks\n");
            SessionState.SetBool(Pending + ".Finished", false);
            foreach (string name in new[] { "Thief", "Thief_Pistol", "Thief_AssaultRifle", "Thief_Shotgun", "Thief_RocketLauncher", "GrapplerThief" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Enemies/{name}.prefab");
                var animator = prefab.GetComponentInChildren<Animator>(true);
                Check(animator != null && animator.runtimeAnimatorController.animationClips.Any(c =>
                    c.name.EndsWith("Thief_Frustration", StringComparison.Ordinal)), name + " has the saved frustration clip");
                Check(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.sharedMesh != null &&
                    r.bones.Length > 0 && r.bones.All(b => b != null)), name + " retains valid skinned bone references");
            }
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                s_failed = false;
                s_probe = Probe();
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode && Application.isBatchMode &&
                SessionState.GetBool(Pending + ".Finished", false))
                EditorApplication.Exit(s_failed ? 1 : 0);
        }

        private static void Tick()
        {
            try
            {
                if (s_wait != null)
                {
                    if (s_wait.MoveNext()) return;
                    s_wait = null;
                }
                if (s_probe == null) return;
                if (!s_probe.MoveNext()) Finish();
                else s_wait = s_probe.Current as IEnumerator;
            }
            catch (Exception exception)
            {
                s_failed = true;
                File.AppendAllText(Report, "FAIL " + exception + "\n");
                Debug.LogException(exception);
                Finish();
            }
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            s_probe = null;
            s_wait = null;
            if (s_remote != null) s_remote.ClientManager.StopConnection();
            if (s_host != null)
            {
                s_host.ClientManager.StopConnection();
                s_host.ServerManager.StopConnection(false);
            }
            if (s_navMesh.valid) s_navMesh.Remove();
            SessionState.SetBool(Pending + ".Finished", true);
            EditorApplication.ExitPlaymode();
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            File.AppendAllText(Report, "PASS " + message + "\n");
            Debug.Log("Thief departure check passed: " + message);
        }

        private static IEnumerator Wait(float duration)
        {
            float end = Time.time + duration;
            while (Time.time < end) yield return null;
        }

        private static ThiefNavigator SpawnThief(Vector3 position, int deliveryId, EnemySpawnManager manager)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab");
            var thief = Object.Instantiate(prefab, position, Quaternion.identity);
            var member = thief.gameObject.AddComponent<ThiefSpawnMember>();
            member.AssignDelivery(deliveryId);
            member.Register(manager, thief);
            s_host.ServerManager.Spawn(thief);
            thief.GetComponent<EnemyWeapon>().enabled = false;
            var active = (List<NetworkObject>)typeof(EnemySpawnManager).GetField("m_activeThieves", Private).GetValue(manager);
            active.Add(thief);
            return thief.GetComponent<ThiefNavigator>();
        }

        private static IEnumerator Probe()
        {
            Application.runInBackground = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            Vector3 origin = new(23000, 0, 23000);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = origin + Vector3.down * .5f;
            floor.transform.localScale = new Vector3(240, 1, 240);
            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, transform = floor.transform.localToWorldMatrix,
                size = Vector3.one, area = 0
            };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0),
                new List<NavMeshBuildSource> { source }, new Bounds(origin, new Vector3(240, 10, 240)),
                Vector3.zero, Quaternion.identity);
            s_navMesh = NavMesh.AddNavMeshData(data);
            Check(NavMesh.SamplePosition(origin, out _, 2, NavMesh.AllAreas), "Probe navigation surface is ready");
            var boot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"));
            s_host = boot.GetComponentInChildren<NetworkManager>();
            Object.DestroyImmediate(s_host.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>());
            yield return null;
            s_host.TransportManager.Transport.SetMaximumClients(4);
            s_host.TransportManager.Transport.SetPort(7794);
            s_host.ServerManager.StartConnection();
            s_host.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 20f;
            while ((!s_host.IsClientStarted || s_host.ServerManager.Clients.Count == 0) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_host.IsClientStarted && s_host.ServerManager.Clients.Count > 0, "FishNet loopback host connected");
            var owner = s_host.ServerManager.Clients.Values.First();
            s_host.SceneManager.AddConnectionToScene(owner, SceneManager.GetActiveScene());
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab"), origin, Quaternion.identity);
            s_host.ServerManager.Spawn(player, owner);
            player.GetComponent<ThirdPersonPlayerController>().enabled = false;
            player.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled = false;
            yield return Wait(.5f);
            var camera = new GameObject("Departure probe camera").AddComponent<Camera>();
            foreach (Camera other in Camera.allCameras) if (other != camera) other.enabled = false;
            camera.transform.position = origin + new Vector3(0, 3, -8);
            camera.farClipPlane = 500;
            var manager = new GameObject("Departure probe spawn manager").AddComponent<EnemySpawnManager>();
            manager.enabled = false;
            var active = (List<NetworkObject>)typeof(EnemySpawnManager).GetField("m_activeThieves", Private).GetValue(manager);
            var idle = SpawnThief(origin + Vector3.forward * 10, 101, manager);
            var otherDelivery = SpawnThief(origin + Vector3.right * 10, 202, manager);
            otherDelivery.enabled = false;
            otherDelivery.GetComponent<EnemyWeapon>().enabled = false;
            manager.RemoveDeliveryEnemiesServer(101, true);
            Check(idle.IsSpawned && idle.IsFrustrated && !idle.CanAttack, "Idle thief reacts without immediate despawn or combat");
            Check(!otherDelivery.IsDeparting && active.Count == 2, "Other deliveries and active population accounting are preserved");
            manager.RemoveDeliveryEnemiesServer(101, true);
            yield return Wait(.25f);
            Check(idle.GetComponent<ThiefAnimationDriver>().Animator.GetCurrentAnimatorStateInfo(0).IsName("Frustration"),
                "Host animator plays the embedded frustration clip");
            yield return Wait(1f);
            Check(idle.IsSpawned && !idle.IsFrustrated && !idle.CanAttack, "Reaction transitions to noncombat retreat");
            Vector3 start = idle.transform.position;
            float until = Time.time + 8f;
            while (Time.time < until)
            {
                camera.transform.LookAt(idle.transform.position + Vector3.up);
                yield return null;
            }
            Check(idle.IsSpawned && Vector3.Distance(start, idle.transform.position) > 8f,
                "Thief follows a retreat path and remains spawned while visible");
            Check(Vector3.Distance(origin, idle.transform.position) > 35f, "Retreat exceeds the configured player distance");
            camera.transform.rotation = Quaternion.Euler(0, 180, 0);
            yield return Wait(2f);
            Check(idle == null || !idle.IsSpawned, "Hidden distant thief despawns through the server");

            var chase = SpawnThief(origin + Vector3.forward * 20, 303, manager);
            chase.enabled = false;
            chase.SetTargetServer(player.GetComponent<PlayerHealth>(), 1f);
            var agent = chase.GetComponent<NavMeshAgent>();
            agent.stoppingDistance = .25f;
            agent.isStopped = false;
            agent.SetDestination(origin);
            yield return Wait(.25f);
            chase.BeginDepartureServer();
            chase.enabled = true;
            Check(chase.IsDeparting && !chase.IsFrustrated && chase.CanAttack, "Pursuing thief continues its current chase");
            yield return Wait(.3f);
            Check(!chase.IsFrustrated, "Delivery completion does not interrupt the chase with an immediate gesture");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = origin + Vector3.forward * 5 + Vector3.up * 2;
            wall.transform.localScale = new Vector3(20, 4, 1);
            yield return Wait(1.5f);
            Check(chase.IsFrustrated && !chase.CanAttack, "Losing sight ends the chase and triggers frustration");
            Object.Destroy(wall);
            yield return Wait(1.2f);
            Check(!chase.IsFrustrated && !chase.CanAttack, "Former pursuer retreats after its gesture");
            camera.transform.LookAt(chase.transform.position + Vector3.up);
            manager.RemoveDeliveryEnemiesServer(202);
            Check(otherDelivery == null || !otherDelivery.IsSpawned, "Immediate cleanup remains available for failed deliveries and resets");

            var timeout = SpawnThief(origin + new Vector3(-20, 0, 20), 405, manager);
            timeout.enabled = false;
            timeout.SetTargetServer(player.GetComponent<PlayerHealth>(), 1f);
            timeout.GetComponent<NavMeshAgent>().isStopped = false;
            timeout.GetComponent<NavMeshAgent>().SetDestination(origin);
            yield return Wait(.25f);
            timeout.BeginDepartureServer();
            timeout.enabled = true;
            Check(!timeout.IsFrustrated && timeout.CanAttack, "Visible target permits the bounded chase");
            yield return Wait(5.2f);
            Check(timeout.IsFrustrated && !timeout.CanAttack, "Chase timeout triggers frustration even with continuous sight");

            var remoteRoot = new GameObject("Independent departure client");
            remoteRoot.SetActive(false);
            s_remote = remoteRoot.AddComponent<NetworkManager>();
            typeof(NetworkManager).GetField("_persistence", Private).SetValue(s_remote, NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad", Private).SetValue(s_remote, false);
            s_remote.SpawnablePrefabs = s_host.SpawnablePrefabs;
            remoteRoot.SetActive(true);
            yield return null;
            s_remote.TransportManager.Transport.SetClientAddress("127.0.0.1");
            s_remote.TransportManager.Transport.SetPort(7794);
            s_remote.ClientManager.StartConnection();
            deadline = Time.realtimeSinceStartup + 20f;
            while ((!s_remote.IsClientStarted || s_host.ServerManager.Clients.Count < 2) &&
                Time.realtimeSinceStartup < deadline)
            {
                camera.transform.LookAt(chase.transform.position + Vector3.up);
                yield return null;
            }
            Check(s_remote.IsClientOnlyStarted && s_host.ServerManager.Clients.Count == 2, "Independent client connected");
            var remoteOwner = s_host.ServerManager.Clients.Values.First(c => c.ClientId != owner.ClientId);
            s_host.SceneManager.AddConnectionToScene(remoteOwner, SceneManager.GetActiveScene());
            deadline = Time.realtimeSinceStartup + 10f;
            while (!s_remote.ClientManager.Objects.Spawned.ContainsKey(chase.ObjectId) &&
                Time.realtimeSinceStartup < deadline)
            {
                camera.transform.LookAt(chase.transform.position + Vector3.up);
                yield return null;
            }
            var lateReplica = s_remote.ClientManager.Objects.Spawned[chase.ObjectId].GetComponent<ThiefNavigator>();
            Check(lateReplica.IsDeparting && !lateReplica.CanAttack, "Late client receives the existing retreat state");
            var reaction = SpawnThief(origin + Vector3.right * 20, 404, manager);
            reaction.BeginDepartureServer();
            yield return Wait(.4f);
            var replica = s_remote.ClientManager.Objects.Spawned[reaction.ObjectId].GetComponent<ThiefNavigator>();
            Check(replica.IsFrustrated && replica.GetComponent<ThiefAnimationDriver>().Animator
                .GetCurrentAnimatorStateInfo(0).IsName("Frustration"), "Independent client receives and plays the frustration reaction");
            yield return Wait(1f);
            Check(!replica.IsFrustrated && !replica.GetComponent<ThiefAnimationDriver>().Animator
                .GetCurrentAnimatorStateInfo(0).IsName("Frustration"), "Independent client returns to locomotion when the reaction ends");
        }
    }
}
