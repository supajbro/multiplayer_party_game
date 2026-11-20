using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Managing;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit isolated host probe. Never runs on import or normal Play mode.</summary>
    [InitializeOnLoad]
    public static class DepotSafeZonePlayProbe
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Pending = "DepotSafeZoneProbe.Pending";
        private const string Result = "Logs/DepotSafeZone-play.result";
        private static IEnumerator s_probe;
        private static NetworkManager s_network;
        static DepotSafeZonePlayProbe() => EditorApplication.playModeStateChanged += StateChanged;

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run this probe in an isolated batch project.");
            Directory.CreateDirectory("Logs"); File.WriteAllText(Result, "START\n");
            DepotSafeZoneAuthoring.RunBatch();
            EditorSettings.enterPlayModeOptionsEnabled = false;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            const string sceneFolder = "Assets/_Project/Tests/Generated/DepotSafeZoneProbe";
            Directory.CreateDirectory(sceneFolder);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, sceneFolder + "/AITest.unity");
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false); s_probe = Probe(); EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            try { if (!s_probe.MoveNext()) Finish(0); }
            catch (Exception e) { File.AppendAllText(Result, "FAIL: " + e + "\n"); Finish(1); }
        }
        private static void Finish(int code)
        {
            EditorApplication.update -= Tick; s_probe = null;
            if (s_network != null) { s_network.ClientManager.StopConnection(); s_network.ServerManager.StopConnection(true); }
            EditorApplication.Exit(code);
        }
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            File.AppendAllText(Result, "PASS: " + message + "\n");
        }
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
        private static T Spawn<T>(string path, Vector3 position, bool owned = false) where T : Component
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), position, Quaternion.identity);
            s_network.ServerManager.Spawn(root, owned ? s_network.ServerManager.Clients.Values.First() : null);
            if (root.TryGetComponent(out ThirdPersonPlayerController motor)) motor.enabled = false;
            if (root.TryGetComponent(out CouchGuys.Input.PlayerInputReader input)) input.enabled = false;
            return root.GetComponent<T>();
        }
        private static IEnumerator Probe()
        {
            var bootstrap = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"));
            s_network = bootstrap.GetComponent<NetworkManager>();
            var spawner = s_network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>();
            if (spawner != null) Object.DestroyImmediate(spawner);
            yield return null;
            s_network.ServerManager.StartConnection(); s_network.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 15;
            while (s_network.ServerManager.Clients.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_network.ServerManager.Clients.Count > 0, "FishNet host connects");
            var zoneObject = new GameObject("Probe boundary", typeof(NavMeshModifierVolume), typeof(DepotSafeZone));
            var volume = zoneObject.GetComponent<NavMeshModifierVolume>();
            volume.center = new Vector3(0, 4, 0); volume.size = new Vector3(30, 12, 30); volume.area = DepotSafeZone.NavigationArea;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = Vector3.down * .5f; floor.transform.localScale = new Vector3(100, 1, 100);
            floor.AddComponent<NavMeshSurface>().BuildNavMesh();
            const string playerPath = "Assets/_Project/Prefabs/Player.prefab";
            var player = Spawn<PlayerHealth>(playerPath, new Vector3(14, .05f, 0), true);
            yield return null;
            float hp = player.CurrentHealth;
            Check(!player.IsEnemyTargetable, "Player inside cannot be targeted");
            player.ApplyDamageServer(20, Vector3.right, 10);
            player.ApplyExplosionDamageServer(20, Vector3.right, 10, 10);
            player.ApplyBatImpactServer(20, Vector3.right, 10, 10);
            Check(player.CurrentHealth == hp && !player.IsKnockedDown, "Incoming bullets, blast damage and bat impacts are blocked");
            Check(EnemyWeapon.FindClosestCouchEnemy(player.transform.position) == null && GrapplerThief.FindTarget(player.transform.position) == null,
                "Rocket and grapple targeting exclude protected players");
            var inventory = player.GetComponent<PlayerWeaponController>();
            Check(inventory.CanHandleWeaponServer && inventory.AcquireWeaponServer(PlayerWeaponType.Pistol), "Shopping/inventory remains usable inside");
            typeof(PlayerWeaponController).GetField("m_switchCompleteAt", Flags).SetValue(inventory, 0f);
            Check(!(bool)Call(inventory, "TryFireServer", player.AimPoint, Vector3.right), "Server rejects firing from inside");
            var thief = Spawn<ThiefNavigator>("Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab", new Vector3(18, .05f, 0));
            thief.GetComponent<EnemyWeapon>().enabled = false;
            yield return null;
            Check((thief.GetComponent<UnityEngine.AI.NavMeshAgent>().areaMask & (1 << DepotSafeZone.NavigationArea)) == 0, "Spawned thief excludes safe navigation area");
            thief.SetTargetServer(player, 1);
            Check(thief.GetComponent<EnemyWeapon>().Target == null, "Thief weapon does not acquire protected player");
            Check(!(bool)Call(inventory, "IsValidGrappleTarget", thief.NetworkObject), "Player cannot grapple thieves through boundary");
            var couch = Spawn<CouchCarryController>("Assets/_Project/Prefabs/Couch.prefab", new Vector3(14, .1f, 2));
            yield return null;
            var grab = typeof(CouchCarryController).GetMethod("TryGrabPointServer", Flags, null, new[] { typeof(ICouchCarrierServer), typeof(int) }, null);
            Check(!(bool)grab.Invoke(couch, new object[] { thief, 0 }), "Thief cannot grab couch inside boundary");
            var protectedPosition = player.transform.position;
            player.transform.position = new Vector3(20, .05f, 4);
            yield return null;
            Check(player.IsEnemyTargetable, "Targetability resumes outside");
            player.ApplyDamageServer(1, Vector3.zero, 0);
            Check(player.CurrentHealth < hp, "Damage resumes outside");
            Check((bool)Call(inventory, "TryFireServer", player.AimPoint, Vector3.right), "Server allows ordinary firing outside");
            thief.SetTargetServer(player, 1);
            player.transform.position = protectedPosition;
            yield return null; yield return null;
            Check(thief.IsDeparting && !thief.CanAttack, "Thief gives up pursuit when remaining objectives are protected");
            Check(!DepotSafeZone.Contains(thief.transform.position), "Thief remains outside boundary");
            File.AppendAllText(Result, "COMPLETE\n");
        }
    }
}
