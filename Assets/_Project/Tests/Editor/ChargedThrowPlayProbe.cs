using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Healing;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Input;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    /// <summary>Explicit Play mode test with a dedicated UDP server and two separate clients.</summary>
    [InitializeOnLoad]
    public static class ChargedThrowPlayProbe
    {
        private const string Report = "Temp/ChargedThrowValidation.txt";
        private const string Request = "Temp/RunChargedThrowProbe.txt";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<NetworkManager> Managers = new();
        private static readonly List<InputDevice> Disabled = new();
        private static IEnumerator s_probe;
        private static float s_resume, s_deadline;
        private static Keyboard s_keyboard;

        static ChargedThrowPlayProbe()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Throw.Validate", false)) return;
                SessionState.SetBool("Throw.Validate", false);
                s_resume = 0;
                s_deadline = Time.realtimeSinceStartup + 50f;
                s_probe = Probe();
            };
        }

        [MenuItem("Couch Guys/Weapons/Test Charged Throw")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before running the throw probe.");
            File.WriteAllText(Report, "START: charged throw validation\n");
            SessionState.SetBool("Throw.Validate", true);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (File.Exists(Request) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                File.Delete(Request); Run(); return;
            }
            if (s_probe == null || !EditorApplication.isPlaying) return;
            try
            {
                Check(Time.realtimeSinceStartup < s_deadline, "Probe timed out.");
                if (Time.time < s_resume) return;
                if (s_probe.MoveNext())
                {
                    if (s_probe.Current is float resume) s_resume = resume;
                    return;
                }
                Log("PASS: all charged throw checks.");
            }
            catch (Exception e) { Log("FAIL: " + e); }
            s_probe = null;
            if (s_keyboard != null) InputSystem.RemoveDevice(s_keyboard);
            s_keyboard = null;
            foreach (var device in Disabled) if (device.added) InputSystem.EnableDevice(device);
            Disabled.Clear();
            foreach (var manager in Managers)
                if (manager != null) { manager.ClientManager.StopConnection(); manager.ServerManager.StopConnection(true); }
            Managers.Clear();
            EditorApplication.ExitPlaymode();
        }

        private static void Log(string value) { File.AppendAllText(Report, value + "\n"); Debug.Log(value); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Fields).Invoke(target, args);
        private static void KeyState(params Key[] keys) => InputSystem.QueueStateEvent(s_keyboard, new KeyboardState(keys));

        private static NetworkManager Manager(string name)
        {
            var parent = new GameObject(name); parent.SetActive(false);
            var obj = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"), parent.transform);
            Object.DestroyImmediate(obj.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>());
            Object.DestroyImmediate(obj.GetComponent<CouchGuys.Networking.GameplaySessionController>());
            var manager = obj.GetComponent<NetworkManager>();
            var settings = new SerializedObject(manager);
            settings.FindProperty("_persistence").intValue = (int)NetworkManager.PersistenceType.AllowMultiple;
            settings.FindProperty("_dontDestroyOnLoad").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var transport = new SerializedObject(obj.GetComponent<FishNet.Transporting.Tugboat.Tugboat>());
            transport.FindProperty("_maximumClients").intValue = 4;
            transport.FindProperty("_port").intValue = 17864;
            transport.ApplyModifiedPropertiesWithoutUndo();
            parent.SetActive(true); Managers.Add(manager); return manager;
        }

        private static WeaponPickup Pickup(NetworkManager server) => server.ServerManager.Objects.Spawned.Values
            .Select(o => o.GetComponent<WeaponPickup>()).Single(p => p != null);

        private static IEnumerator Probe()
        {
            var server = Manager("Throw server"); server.ServerManager.StartConnection();
            var client = Manager("Throw owner"); client.ClientManager.StartConnection();
            while (!client.IsClientStarted || !client.ClientManager.Connection.IsValid || server.ServerManager.Clients.Count < 1) yield return null;
            var connection = server.ServerManager.Clients.Values.First();
            var observer = Manager("Throw observer"); observer.ClientManager.StartConnection();
            while (!observer.IsClientStarted || server.ServerManager.Clients.Count < 2) yield return null;
            var obj = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"), new Vector3(200, 100, 200), Quaternion.identity);
            obj.GetComponent<ThirdPersonPlayerController>().enabled = false;
            server.ServerManager.Spawn(obj, connection);
            int id = obj.GetComponent<NetworkObject>().ObjectId;
            while (!client.ClientManager.Objects.Spawned.ContainsKey(id) || !observer.ClientManager.Objects.Spawned.ContainsKey(id)) yield return null;
            var authoritative = obj.GetComponent<PlayerWeaponController>();
            var owner = client.ClientManager.Objects.Spawned[id].GetComponent<PlayerWeaponController>();
            owner.GetComponent<ThirdPersonPlayerController>().enabled = false;
            observer.ClientManager.Objects.Spawned[id].GetComponent<ThirdPersonPlayerController>().enabled = false;
            // These clients share the test process's physics scene with the dedicated server.
            foreach (var collider in owner.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var collider in observer.ClientManager.Objects.Spawned[id].GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var device in InputSystem.devices.ToArray())
                if (device is Keyboard && device.enabled) { Disabled.Add(device); InputSystem.DisableDevice(device); }
            s_keyboard = InputSystem.AddDevice<Keyboard>();
            Check(authoritative.AcquireWeaponServer(PlayerWeaponType.Pistol, 3, 9), "Could not acquire pistol.");
            yield return Time.time + .4f;

            KeyState(Key.Q); yield return Time.time + .12f;
            Check(authoritative.HasEquippedWeapon, "Press dropped before release.");
            KeyState(); yield return Time.time + .2f;
            Check(!authoritative.HasEquippedWeapon, "Tap did not drop.");
            var pickup = Pickup(server);
            Check(pickup.GetComponent<Rigidbody>().linearVelocity.z < 3f, "Tap used throw velocity.");
            pickup.transform.position = obj.transform.position + Vector3.up;
            Check(pickup.TryPickupServer(authoritative), "Cannot reacquire dropped weapon.");
            Check(authoritative.CurrentMagazine == 3 && authoritative.CurrentReserve == 9, "Drop lost ammo.");
            yield return Time.time + .3f;
            Log("PASS: real Q tap drops on release and preserves ammo.");

            KeyState(Key.Q); yield return Time.time + .95f;
            Check(authoritative.HasEquippedWeapon, "Full charge automatically released.");
            Check(owner.GetItemReleasePrompt(out float progress, out float duration) != null && progress >= duration && duration > 0f,
                "HUD prompt did not reach full charge.");
            KeyState(); yield return Time.time + .15f;
            Check(!authoritative.HasEquippedWeapon, "Charged release did not throw.");
            pickup = Pickup(server);
            Check(pickup.GetComponent<Rigidbody>().linearVelocity.z > 10f, "Missing server throw velocity.");
            int pickupId = pickup.ObjectId;
            yield return Time.time + .3f;
            Check(observer.ClientManager.Objects.Spawned.ContainsKey(pickupId), "Observer did not receive pickup.");
            var remote = observer.ClientManager.Objects.Spawned[pickupId];
            Check(remote.transform.position.z > obj.transform.position.z + 1f, "Observer did not see thrown motion.");
            Check(remote.GetComponent<Rigidbody>().isKinematic, "Observer is simulating server-owned physics.");
            pickup.transform.position = obj.transform.position + Vector3.up;
            Check(pickup.TryPickupServer(authoritative), "Cannot pick up thrown weapon.");
            Check(authoritative.CurrentMagazine == 3 && authoritative.CurrentReserve == 9, "Throw lost ammo.");
            yield return Time.time + .3f;
            Log("PASS: full Q circle waits for release; UDP server/owner/observer physics and ammo transfer.");

            KeyState(Key.Q); yield return Time.time + .3f;
            owner.GetComponent<PlayerInputReader>().SetGameplaySuppressed(true);
            KeyState(); yield return Time.time + .3f;
            owner.GetComponent<PlayerInputReader>().SetGameplaySuppressed(false);
            Check(authoritative.HasEquippedWeapon, "Suppression released an item.");
            Call(owner, "FinishItemReleaseServerRpc", true); yield return Time.time + .2f;
            Check(authoritative.HasEquippedWeapon, "Cancelled charge was reused.");
            KeyState(Key.Q); yield return Time.time + .2f;
            Call(owner, "RequestHotbarSelectionServerRpc", 1); yield return Time.time + .2f;
            KeyState(); yield return Time.time + .2f;
            Check(authoritative.GetInventoryWeaponIndex(0) == (int)PlayerWeaponType.Pistol, "Slot switch dropped previous item.");
            Log("PASS: suppression, replayed release and hotbar switch cancel charge.");

            var healing = HealingItemCatalogue.Load().Items.First(i => i != null);
            Check(authoritative.AcquireHealingItemServer(healing) && authoritative.AcquireHealingItemServer(healing), "Could not acquire healing stack.");
            yield return Time.time + .3f;
            int slot = authoritative.SelectedHotbarSlot;
            KeyState(Key.Q); yield return Time.time + .95f;
            KeyState(); yield return Time.time + .2f;
            Check(authoritative.GetInventoryHealingQuantity(slot) == 1, "Throw did not remove exactly one healing item.");
            pickup = Pickup(server);
            Check(pickup.HealingSettings == healing && pickup.GetComponent<Rigidbody>().linearVelocity.z > 10f, "Healing item did not throw.");
            Log("PASS: healing stack throws one server-owned item.");
        }
    }
}
