using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Player;
using CouchGuys.UI;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit integration probe in transient Play-mode objects. Never edits gameplay scenes.
    [InitializeOnLoad]
    public static class WeaponTerminalPlayValidation
    {
        private const string Pending = "WeaponTerminal.Test", Report = "Temp/WeaponTerminalPlayValidation.txt";
        private static IEnumerator s_probe;
        private static NetworkManager s_network;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static WeaponTerminalPlayValidation() { EditorApplication.playModeStateChanged += OnState; }
        [MenuItem("Couch Guys/Weapon Terminal/Test Host Purchases And Camera")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode before running the probe.");
            File.WriteAllText(Report, "Starting weapon terminal integration probe\n");
            SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
        }
        private static void OnState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); s_probe = Probe(); EditorApplication.update += Tick; }
            if (state == PlayModeStateChange.ExitingPlayMode) { EditorApplication.update -= Tick; s_probe = null; }
        }
        private static void Tick()
        {
            try { if (s_probe != null && !s_probe.MoveNext()) Finish(); }
            catch (Exception e) { File.AppendAllText(Report, "FAIL: " + e + "\n"); Debug.LogException(e); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick; s_probe = null;
            DispatcherMenuController.Active?.CloseLocal(false, true);
            if (s_network != null)
            { s_network.ClientManager.StopConnection(); s_network.ServerManager.StopConnection(false); }
            EditorApplication.ExitPlaymode();
        }
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            File.AppendAllText(Report, "PASS: " + message + "\n");
        }
        private static IEnumerator Probe()
        {
            var origin = new Vector3(21000, 0, 21000);
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var bootstrap = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("64e03b333ee845f5bff4d89fa9a228c4")));
            s_network = bootstrap.GetComponentInChildren<NetworkManager>();
            var spawner = s_network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>();
            if (spawner != null) Object.DestroyImmediate(spawner);
            yield return null;
            s_network.ServerManager.StartConnection(); s_network.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 20f;
            while ((!s_network.IsClientStarted || s_network.ServerManager.Clients.Count == 0) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_network.IsClientStarted && s_network.ServerManager.Clients.Count > 0, "FishNet host connected");
            var connection = s_network.ServerManager.Clients.Values.First();
            s_network.SceneManager.AddConnectionToScene(connection, SceneManager.GetActiveScene());
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = origin + Vector3.down * .5f; floor.transform.localScale = new Vector3(100, 1, 100);
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab"), origin + Vector3.up * .05f, Quaternion.identity);
            s_network.ServerManager.Spawn(player, connection);
            deadline = Time.realtimeSinceStartup + 10f;
            while ((!player.IsClientInitialized || !player.IsOwner) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(player.IsOwner && player.IsClientInitialized, "Owned host player initialized");
            player.GetComponent<ThirdPersonPlayerController>().enabled = false;
            player.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled = false;
            var managerRoot = new GameObject("TerminalProbeManager"); managerRoot.SetActive(false);
            var manager = managerRoot.AddComponent<DeliveryManager>();
            managerRoot.GetComponent<CouchGuys.ProceduralGeneration.NeighbourhoodGenerator>().enabled = false;
            manager.enabled = false;
            var networkData = new SerializedObject(managerRoot.GetComponent<NetworkObject>());
            networkData.FindProperty("SceneId").ulongValue = 47; networkData.ApplyModifiedPropertiesWithoutUndo();
            var managerData = new SerializedObject(manager);
            managerData.FindProperty("_networkObjectCache").objectReferenceValue = managerRoot.GetComponent<NetworkObject>();
            managerData.FindProperty("_addedNetworkObject").objectReferenceValue = managerRoot.GetComponent<NetworkObject>();
            managerData.FindProperty("m_shopCatalogue").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
            managerData.ApplyModifiedPropertiesWithoutUndo(); managerRoot.SetActive(true);
            s_network.ServerManager.Spawn(managerRoot.GetComponent<NetworkObject>());
            var terminal = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(WeaponTerminalAuthoring.PrefabPath), origin, Quaternion.identity).GetComponent<WeaponShopTerminal>();
            var carrier = player.GetComponent<PlayerCouchCarrier>();
            var playerVisuals = player.GetComponentsInChildren<Renderer>(true);
            var previousVisibility = playerVisuals.Select(r => r.forceRenderingOff).ToArray();
            manager.AddTestCurrencyServer(50000);
            var npc = new GameObject("ProbeDispatcher").AddComponent<DeliveryNPC>(); npc.transform.position = origin; npc.SetDeliveryManager(manager);
            var weapon = manager.ShopCatalogue.Items.First(i => i != null && i.Category == ShopItemCategory.Weapon);
            Check(manager.TryBeginDialogueServer(npc, carrier), "Dispatcher session opens");
            Check(!manager.TryPurchaseShopItemServer(carrier, weapon.ItemId, 1), "Dispatcher rejects weapon purchases on the server");
            manager.ReleaseDialogueLockServer(carrier);
            Check(manager.TryBeginWeaponTerminalServer(terminal, carrier), "Terminal authorizes a nearby player");
            deadline = Time.realtimeSinceStartup + 5f;
            while (DispatcherMenuController.Active != terminal.Menu && Time.realtimeSinceStartup < deadline) yield return null;
            Check(DispatcherMenuController.Active == terminal.Menu, "Terminal opens the world-space menu for its owning client");
            deadline = Time.realtimeSinceStartup + .8f;
            while (Time.realtimeSinceStartup < deadline) yield return null;
            var ui = (DispatcherMenuUI)typeof(DispatcherMenuController).GetField("m_ui", Private).GetValue(terminal.Menu);
            Check(ui != null && ui.SelectedCategory == 1, "Terminal opens directly to the weapon purchasing panel");
            var camera = player.GetComponent<CouchGuys.Networking.NetworkPlayerOwnership>().PlayerCamera;
            var focus = (Transform)typeof(DispatcherMenuController).GetField("m_cameraFocusPoint", Private).GetValue(terminal.Menu);
            Check(Vector3.Distance(camera.transform.position, focus.position) < .03f, "Camera smoothly reaches terminal screen focus");
            Check(playerVisuals.All(r => r.forceRenderingOff), "Local player visuals are hidden during terminal use");
            var originalFocus = focus.position;
            focus.position = playerVisuals.First(r => r is SkinnedMeshRenderer).bounds.center;
            focus.rotation = Quaternion.Euler(10, 180, 0);
            deadline = Time.realtimeSinceStartup + .1f;
            while (Time.realtimeSinceStartup < deadline) yield return null;
            Check(Vector3.Distance(camera.transform.position, focus.position) < .03f, "Edited focus inside player bounds is honored without lifting the camera");
            focus.position = originalFocus;
            int funds = manager.CurrentCurrency;
            bool canPurchase = weapon.PurchaseBehaviour.CanPurchase(manager, null, carrier, out string reason);
            File.AppendAllText(Report, $"Purchase context: funds={funds}, inRange={terminal.IsInRange(player.transform.position, .35f)}, canPurchase={canPurchase}, reason={reason}\n");
            Check(manager.TryPurchaseShopItemServer(carrier, weapon.ItemId, 1), "Weapon purchase succeeds through existing authoritative fulfilment");
            Check(manager.CurrentCurrency == funds - weapon.Price, "Purchase charges the current catalogue price exactly once");
            Check(!manager.TryPurchaseShopItemServer(carrier, weapon.ItemId, 1) && manager.CurrentCurrency == funds - weapon.Price, "Duplicate purchase request cannot charge again");
            var other = manager.ShopCatalogue.Items.First(i => i != null && i.Category != ShopItemCategory.Weapon);
            Check(!manager.TryPurchaseShopItemServer(carrier, other.ItemId, 2), "Terminal rejects non-weapon purchases");
            Check(!manager.TryPurchaseDeliveryServer(carrier, 0), "Terminal cannot start dispatcher deliveries");
            terminal.Menu.CloseLocal(true, true);
            yield return null;
            Check(!DispatcherMenuController.IsLocalInteractionActive, "Closing restores local camera/input session");
            Check(playerVisuals.Select((r, i) => r.forceRenderingOff == previousVisibility[i]).All(restored => restored), "Closing restores the player's original rendering state");
            player.transform.position = origin + Vector3.right * 20f;
            Check(!manager.TryBeginWeaponTerminalServer(terminal, carrier), "Server rejects opening terminal outside interaction range");
            File.AppendAllText(Report, "COMPLETE\n");
        }
    }
}
