using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Vehicles;
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
        private const string Pending = "WeaponTerminal.Test";
        private static string Report => SessionState.GetBool("ShopTest.Vehicle", false)
            ? "Temp/VehicleShopPlayValidation.txt" : "Temp/WeaponTerminalPlayValidation.txt";
        private static IEnumerator s_probe;
        private static NetworkManager s_network;
        private static NetworkManager s_remote;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static WeaponTerminalPlayValidation() { EditorApplication.playModeStateChanged += OnState; }
        [MenuItem("Couch Guys/Weapon Terminal/Test Host Purchases And Camera")]
        public static void Run()
        {
            RunForCategory(false);
        }

        [MenuItem("Couch Guys/Vehicle Shop/Test Host And Remote Purchases")]
        public static void RunVehicles() => RunForCategory(true);

        private static void RunForCategory(bool vehicles)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode before running the probe.");
            SessionState.SetBool("ShopTest.Vehicle", vehicles);
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
            if (s_remote != null) s_remote.ClientManager.StopConnection();
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
            bool vehicles = SessionState.GetBool("ShopTest.Vehicle", false);
            var terminalPath = vehicles ? VehicleShopAuthoring.TerminalPath : WeaponTerminalAuthoring.PrefabPath;
            var terminal = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(terminalPath), origin, Quaternion.identity).GetComponent<WeaponShopTerminal>();
            var carrier = player.GetComponent<PlayerCouchCarrier>();
            var playerVisuals = player.GetComponentsInChildren<Renderer>(true);
            var previousVisibility = playerVisuals.Select(r => r.forceRenderingOff).ToArray();
            if (vehicles)
            {
                var vehicleProbe = ProbeVehicles(manager, terminal, carrier, player, origin);
                while (vehicleProbe.MoveNext()) yield return vehicleProbe.Current;
                yield break;
            }
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
            var other = manager.ShopCatalogue.Items.First(i => i != null && !DeliveryManager.IsShopItemAllowed(i, true));
            Check(!manager.TryPurchaseShopItemServer(carrier, other.ItemId, 2), "Terminal rejects purchases outside weapons and healing items");
            Check(!manager.TryPurchaseDeliveryServer(carrier, 0), "Terminal cannot start dispatcher deliveries");
            terminal.Menu.CloseLocal(true, true);
            yield return null;
            Check(!DispatcherMenuController.IsLocalInteractionActive, "Closing restores local camera/input session");
            Check(playerVisuals.Select((r, i) => r.forceRenderingOff == previousVisibility[i]).All(restored => restored), "Closing restores the player's original rendering state");
            player.transform.position = origin + Vector3.right * 20f;
            Check(!manager.TryBeginWeaponTerminalServer(terminal, carrier), "Server rejects opening terminal outside interaction range");
            File.AppendAllText(Report, "COMPLETE\n");
        }

        private static bool IsVehicle(NetworkObject instance) => instance.GetComponent<TruckController>() != null ||
            instance.GetComponent<MagneticHoverboardController>() != null || instance.GetComponent<CouchGuys.Gameplay.Sled.SledController>() != null;

        private static IEnumerator ProbeVehicles(DeliveryManager manager, WeaponShopTerminal terminal,
            PlayerCouchCarrier carrier, NetworkObject player, Vector3 origin)
        {
            var items = manager.ShopCatalogue.Items.Where(item => item != null && item.Category == ShopItemCategory.Vehicle && item.PurchaseBehaviour != null).ToArray();
            Check(manager.TryBeginWeaponTerminalServer(terminal, carrier), "Vehicle terminal authorizes the host player");
            float deadline = Time.realtimeSinceStartup + 5f;
            while (DispatcherMenuController.Active != terminal.Menu && Time.realtimeSinceStartup < deadline) yield return null;
            Check(DispatcherMenuController.Active == terminal.Menu, "Vehicle terminal opens through existing client UI");
            int funds = manager.CurrentCurrency;
            Check(funds < items.Min(item => item.Price), "Test starts with insufficient team currency");
            Check(!manager.TryPurchaseShopItemServer(carrier, items[0].ItemId, 1) && manager.CurrentCurrency == funds,
                "Unaffordable vehicle cannot spawn or consume currency");
            manager.AddTestCurrencyServer(items.Sum(item => item.Price) * 4);
            var weapon = manager.ShopCatalogue.Items.First(item => item != null && item.Category == ShopItemCategory.Weapon);
            Check(!manager.TryPurchaseShopItemServer(carrier, weapon.ItemId, 2), "Vehicle terminal rejects weapon purchases");
            uint request = 3;
            foreach (var item in items)
            {
                funds = manager.CurrentCurrency;
                int before = s_network.ServerManager.Objects.Spawned.Values.Count(IsVehicle);
                Physics.SyncTransforms();
                Check(manager.TryPurchaseShopItemServer(carrier, item.ItemId, request), "Existing vehicle fulfils purchase: " + item.DisplayName);
                Check(manager.CurrentCurrency == funds - item.Price, "Charges catalogue price: " + item.DisplayName);
                Check(s_network.ServerManager.Objects.Spawned.Values.Count(IsVehicle) == before + 1, "Exactly one server vehicle spawned: " + item.DisplayName);
                Check(!manager.TryPurchaseShopItemServer(carrier, item.ItemId, request++) && manager.CurrentCurrency == funds - item.Price,
                    "Duplicate request is harmless: " + item.DisplayName);
                foreach (var spawned in s_network.ServerManager.Objects.Spawned.Values.Where(IsVehicle).ToArray())
                    s_network.ServerManager.Despawn(spawned);
                yield return null;
            }
            terminal.Menu.CloseLocal(true, true);
            yield return null;

            // A client-only connection exercises owner RPCs and vehicle prefab replication.
            var remoteRoot = new GameObject("Vehicle shop remote client"); remoteRoot.SetActive(false);
            s_remote = remoteRoot.AddComponent<NetworkManager>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            typeof(NetworkManager).GetField("_persistence", flags).SetValue(s_remote, NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad", flags).SetValue(s_remote, false);
            s_remote.SpawnablePrefabs = s_network.SpawnablePrefabs;
            s_network.TransportManager.Transport.SetMaximumClients(4);
            remoteRoot.SetActive(true); yield return null;
            s_remote.TransportManager.Transport.SetClientAddress("127.0.0.1");
            s_remote.TransportManager.Transport.SetPort(s_network.TransportManager.Transport.GetPort());
            s_remote.ClientManager.StartConnection();
            deadline = Time.realtimeSinceStartup + 15f;
            while ((!s_remote.IsClientOnlyStarted || s_network.ServerManager.Clients.Count < 2) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_remote.IsClientOnlyStarted && s_network.ServerManager.Clients.Count == 2, "Independent client connected");
            var owner = s_network.ServerManager.Clients.Values.First(connection => connection.ClientId != player.Owner.ClientId);
            s_network.SceneManager.AddConnectionToScene(owner, player.gameObject.scene);
            var remotePlayer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab"), origin + new Vector3(0, .05f, 1f), Quaternion.identity);
            s_network.ServerManager.Spawn(remotePlayer, owner);
            remotePlayer.GetComponent<ThirdPersonPlayerController>().enabled = false;
            deadline = Time.realtimeSinceStartup + 8f;
            while (!s_remote.ClientManager.Objects.Spawned.ContainsKey(remotePlayer.ObjectId) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_remote.ClientManager.Objects.Spawned.ContainsKey(remotePlayer.ObjectId), "Remote player replica exists");
            var replica = s_remote.ClientManager.Objects.Spawned[remotePlayer.ObjectId];
            replica.GetComponent<ThirdPersonPlayerController>().enabled = false;
            replica.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled = false;
            var remoteCarrier = replica.GetComponent<PlayerCouchCarrier>();
            typeof(PlayerCouchCarrier).GetMethod("RequestWeaponTerminalServerRpc", flags).Invoke(remoteCarrier, new object[] { terminal.transform.position });
            deadline = Time.realtimeSinceStartup + 5f;
            while (terminal.Menu.Owner != remoteCarrier && Time.realtimeSinceStartup < deadline) yield return null;
            Check(terminal.Menu.Owner == remoteCarrier, "Remote owner's terminal interaction RPC opens the menu");
            request = 1;
            foreach (var item in items)
            {
                funds = manager.CurrentCurrency;
                typeof(PlayerCouchCarrier).GetMethod("RequestShopPurchaseServerRpc", flags).Invoke(remoteCarrier, new object[] { item.ItemId, request++ });
                deadline = Time.realtimeSinceStartup + 5f;
                while (manager.CurrentCurrency == funds && Time.realtimeSinceStartup < deadline) yield return null;
                Check(manager.CurrentCurrency == funds - item.Price, "Remote request charged server currency: " + item.DisplayName);
                var spawned = s_network.ServerManager.Objects.Spawned.Values.Single(IsVehicle);
                deadline = Time.realtimeSinceStartup + 5f;
                while (!s_remote.ClientManager.Objects.Spawned.ContainsKey(spawned.ObjectId) && Time.realtimeSinceStartup < deadline) yield return null;
                Check(s_remote.ClientManager.Objects.Spawned.ContainsKey(spawned.ObjectId), "Purchased vehicle replicated to remote client: " + item.DisplayName);
                s_network.ServerManager.Despawn(spawned);
                yield return null;
            }
            terminal.Menu.CloseLocal(true, true);
            yield return null;
            File.AppendAllText(Report, "COMPLETE\n");
        }
    }
}
