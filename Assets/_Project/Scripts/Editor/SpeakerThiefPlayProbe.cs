using System;
using System.Collections;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using FishNet.Managing;
using FishNet.Object;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit loopback host + independent client check. Never runs during normal play.</summary>
    [InitializeOnLoad]
    public static class SpeakerThiefPlayProbe
    {
        private const string Pending = "SpeakerThiefExisting.Probe";
        private const string Result = "Temp/SpeakerThiefPlay.result";
        private static NetworkManager s_host, s_remote;
        private static IEnumerator s_probe;
        static SpeakerThiefPlayProbe() => EditorApplication.playModeStateChanged += StateChanged;
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit the current play session.");
            File.WriteAllText(Result, "START: existing Character01 variant, host and separate late-joining client\n");
            SessionState.SetBool(Pending, true);EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false);s_probe = Probe();EditorApplication.update += Tick; }
            if (state == PlayModeStateChange.ExitingPlayMode) { EditorApplication.update -= Tick;s_probe = null; }
        }
        private static void Tick()
        {
            try { if (s_probe != null && !s_probe.MoveNext()) Finish(); }
            catch (Exception e) { File.AppendAllText(Result, "FAIL: " + e + "\n");Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick;s_probe = null;
            if (s_remote != null) s_remote.ClientManager.StopConnection();
            if (s_host != null) { s_host.ClientManager.StopConnection();s_host.ServerManager.StopConnection(true); }
            EditorApplication.ExitPlaymode();
        }
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string text) => File.AppendAllText(Result, "PASS: " + text + "\n");
        private static NetworkManager Manager()
        {
            var staging = new GameObject("Inactive test bootstrap staging");staging.SetActive(false);
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"), staging.transform);
            var manager = go.GetComponentInChildren<NetworkManager>(true);var so = new SerializedObject(manager);
            so.FindProperty("_persistence").enumValueIndex = (int)NetworkManager.PersistenceType.AllowMultiple;so.ApplyModifiedPropertiesWithoutUndo();
            var spawner = go.GetComponentInChildren<CouchGuys.Networking.NeighbourhoodPlayerSpawner>(true);
            if (spawner != null) Object.DestroyImmediate(spawner);
            go.transform.SetParent(null, false);Object.Destroy(staging);return manager;
        }
        private static IEnumerator Probe()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (GameObject root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position = Vector3.down * .5f;
            floor.transform.localScale = new Vector3(80, 1, 80);floor.AddComponent<NavMeshSurface>().BuildNavMesh();
            s_host = Manager();yield return null;
            s_host.TransportManager.Transport.SetMaximumClients(3); // Probe copy only: the solo bootstrap normally permits one.
            s_host.ServerManager.StartConnection();s_host.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 12;
            while (s_host.ServerManager.Clients.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Require(s_host.ServerManager.Clients.Count == 1, "Host connection failed.");
            var leader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Thief.prefab"), new Vector3(0, .05f, 5), Quaternion.identity).GetComponent<NetworkObject>();
            leader.GetComponent<ThiefNavigator>().enabled = false;
            var speaker = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpeakerThiefAuthoring.PrefabPath), new Vector3(0, .05f, -4), Quaternion.identity).GetComponent<NetworkObject>();
            var group = new[] { leader, speaker };
            var member = leader.GetComponent<ThiefSpawnMember>() ?? leader.gameObject.AddComponent<ThiefSpawnMember>();member.AssignGroup(group);member.AssignDelivery(941);
            speaker.GetComponent<ThiefSpawnMember>().AssignGroup(group);speaker.GetComponent<ThiefSpawnMember>().AssignDelivery(941);
            s_host.ServerManager.Spawn(leader);s_host.ServerManager.Spawn(speaker);
            int id = speaker.ObjectId;
            Require(speaker.GetComponent<EnemyWeapon>() == null && !speaker.GetComponent<ThiefNavigator>().CanAttack, "Speaker can attack.");
            s_remote = Manager();yield return null;s_remote.ClientManager.StartConnection();
            NetworkObject replica = null;deadline = Time.realtimeSinceStartup + 12;
            while ((!s_remote.ClientManager.Objects.Spawned.TryGetValue(id, out replica) || replica == null) && Time.realtimeSinceStartup < deadline) yield return null;
            Require(replica != null && !replica.IsServerInitialized, "Independent client did not receive the speaker variant.");
            Require(!replica.GetComponent<NavMeshAgent>().enabled, "Client navigation must be disabled.");
            var hostMount = speaker.GetComponentsInChildren<Transform>(true).First(t => t.name == "ShoulderSpeaker");
            var clientMount = replica.GetComponentsInChildren<Transform>(true).First(t => t.name == "ShoulderSpeaker");
            Require(Vector3.Distance(hostMount.localPosition, clientMount.localPosition) < .001f && Quaternion.Angle(hostMount.localRotation, clientMount.localRotation) < .01f, "Shoulder attachments differ.");
            Require(clientMount.GetComponentsInChildren<AudioSource>(true).Length == 0, "Speaker unexpectedly contains audio.");
            Pass("registered prefab received by host and independent late client; identical shoulder attachment, silent prop, server-only navigation");
            var agent = leader.GetComponent<NavMeshAgent>();agent.isStopped = false;agent.SetDestination(new Vector3(0, 0, 13));
            float until = Time.time + 5;while (Time.time < until) yield return null;
            Require(speaker.transform.position.z > 0 && Vector3.Distance(speaker.transform.position, leader.transform.position) < 9, "Group following failed.");
            Require(Vector3.Distance(speaker.transform.position, replica.transform.position) < 2, "Client transform did not track the server.");
            Pass("group following/catch-up and replicated navigation; no weapon or attack capability");
            speaker.GetComponent<ThiefHealth>().ApplyDamageServer(1000, Vector3.right, 1);
            deadline = Time.realtimeSinceStartup + 2;
            while (replica != null && replica.GetComponent<ThiefHealth>().IsAlive && Time.realtimeSinceStartup < deadline) yield return null;
            Require(replica != null && !replica.GetComponent<ThiefHealth>().IsAlive && replica.GetComponentInChildren<PedestrianRagdoll>(true).IsFallen, "Shared death/ragdoll did not replicate.");
            Pass("shared health, damage, death and ragdoll replicate to the independent client");
            deadline = Time.realtimeSinceStartup + 7;
            while (s_remote.ClientManager.Objects.Spawned.ContainsKey(id) && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!s_remote.ClientManager.Objects.Spawned.ContainsKey(id), "Death despawn did not reach the client.");
            Pass("shared death despawn removes the variant and its attached speaker on the client");
            File.Copy(Result, "Tools/SpeakerThiefMultiplayerValidation.txt", true);
        }
    }
}
