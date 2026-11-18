using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Input;
using CouchGuys.Player;
using CouchGuys.UI;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    [InitializeOnLoad]
    public sealed class PlayerEmoteTests
    {
        private const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string Report = "Temp/PlayerEmoteValidation.txt";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static IEnumerator s_probe;
        private static float s_deadline, s_resume;
        private static readonly System.Collections.Generic.List<NetworkManager> Managers = new();
        private static Keyboard s_keyboard;
        private static Mouse s_mouse;
        private static readonly System.Collections.Generic.List<InputDevice> DisabledDevices = new();
        static PlayerEmoteTests()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("Emotes.RunAfterStop", false))
                {
                    SessionState.SetBool("Emotes.RunAfterStop", false);
                    EditorApplication.delayCall += () => { try { Run(); } catch (Exception e) { Log("FAIL: " + e); } };
                }
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("Emotes.Validate", false)) return;
                SessionState.SetBool("Emotes.Validate", false);
                s_deadline = Time.realtimeSinceStartup + 100;
                s_resume = 0;
                s_probe = Probe();
            };
        }

        private static object Get(object target, string field) => target.GetType().GetField(field, Private | BindingFlags.Public).GetValue(target);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private | BindingFlags.Public).Invoke(target, args);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Log(string message) => File.AppendAllText(Report, message + "\n");

        [Test]
        public void SavedAssets_TargetOnlyExistingRigAndRegisterNetworkBehaviour()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var emotes = player.GetComponent<PlayerEmoteController>();
            Assert.That(emotes, Is.Not.Null);
            var definitions = (EmoteDefinition[])Get(emotes, "m_definitions");
            Assert.That(definitions.Length, Is.EqualTo(6));
            Assert.That(definitions.Select(d => d.Id).Distinct().Count(), Is.EqualTo(6));
            var animator = player.GetComponent<CouchGuyAnimationDriver>().Animator;
            foreach (var definition in definitions)
            {
                Assert.That(definition.Duration, Is.GreaterThan(1));
                Assert.That(definition.Clip.isLooping, Is.EqualTo(definition.Loop));
                foreach (var binding in AnimationUtility.GetCurveBindings(definition.Clip))
                {
                    Assert.That(binding.path, Does.StartWith("CouchGuy_Rig"));
                    Assert.That(binding.path, Does.Not.Contain("Weapon"));
                    Assert.That(binding.propertyName, Does.StartWith("m_LocalRotation"));
                    Assert.That(animator.transform.Find(binding.path), Is.Not.Null, binding.path);
                }
            }
            var network = new SerializedObject(player.GetComponent<NetworkObject>()).FindProperty("NetworkBehaviours");
            Assert.That(Enumerable.Range(0, network.arraySize).Any(i => network.GetArrayElementAtIndex(i).objectReferenceValue == emotes), Is.True);
            var wheel = (EmoteWheelView)Get(emotes, "m_wheelPrefab");
            Assert.That(AssetDatabase.GetAssetPath(wheel), Is.EqualTo("Assets/_Project/Prefabs/UI/EmoteWheel.prefab"));
            Assert.That(wheel.GetComponentsInChildren<EmoteWheelSector>(true).Length, Is.EqualTo(6));
            Assert.That(wheel.GetComponentsInChildren<EmoteWheelSector>(true).All(s => s.GetComponent<CanvasRenderer>() != null), Is.True);
            Assert.That(wheel.GetComponentsInChildren<TMPro.TMP_Text>(true).All(t => t.font != null), Is.True);
        }

        [Test]
        public void Wheel_MapsAllDirectionsAndCancelsAtCentreAndOutside()
        {
            Assert.That(EmoteWheelView.SelectSegment(Vector2.zero, 40, 100), Is.EqualTo(-1));
            Assert.That(EmoteWheelView.SelectSegment(Vector2.up * 101, 40, 100), Is.EqualTo(-1));
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                Assert.That(EmoteWheelView.SelectSegment(new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 70, 40, 100), Is.EqualTo(i));
            }
        }

        [Test]
        public void Eligibility_ReadsExistingHealthCarryVehicleAndActionState()
        {
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var emotes = player.GetComponent<PlayerEmoteController>(); Call(emotes, "Awake");
                bool Compatible() => (bool)typeof(PlayerEmoteController).GetProperty("Compatible", Private).GetValue(emotes);
                void State(Component component, string field, object value)
                {
                    object sync = Get(component, field);
                    sync.GetType().GetField("_value", Private).SetValue(sync, value);
                }
                var health = player.GetComponent<PlayerHealth>();
                State(health, "m_currentHealth", 100f);
                Assert.That(Compatible(), Is.True);
                State(health, "m_knockedDown", true); Assert.That(Compatible(), Is.False);
                State(health, "m_knockedDown", false);
                State(health, "m_currentHealth", 0f); Assert.That(Compatible(), Is.False);
                State(health, "m_currentHealth", 100f);
                var carrier = player.GetComponent<PlayerCouchCarrier>();
                State(carrier, "m_carriedCouch", player.GetComponent<NetworkObject>()); Assert.That(Compatible(), Is.False);
                State(carrier, "m_carriedCouch", null);
                var board = player.GetComponent<PlayerHoverboardInteractor>();
                State(board, "m_board", player.GetComponent<NetworkObject>()); Assert.That(Compatible(), Is.False);
                State(board, "m_board", null);
                var truck = AssetDatabase.FindAssets("Truck t:Prefab", new[] { "Assets/_Project/Prefabs" })
                    .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                    .First(p => p.GetComponent<CouchGuys.Gameplay.Vehicles.TruckController>() != null);
                var interactor = player.GetComponent<PlayerTruckInteractor>();
                State(interactor, "m_currentTruckObject", truck.GetComponent<NetworkObject>()); Assert.That(Compatible(), Is.False);
                State(interactor, "m_currentTruckObject", null);
                var healing = player.GetComponent<CouchGuys.Gameplay.Healing.HealingItem>();
                State(healing, "m_usingId", 1); Assert.That(Compatible(), Is.False);
                State(healing, "m_usingId", -1);
                var weapons = player.GetComponent<CouchGuys.Gameplay.Weapons.PlayerWeaponController>();
                weapons.GetType().GetField("m_batSwinging", Private).SetValue(weapons, true); Assert.That(Compatible(), Is.False);
                weapons.GetType().GetField("m_batSwinging", Private).SetValue(weapons, false);
                State(weapons, "m_grappleAttached", true); Assert.That(Compatible(), Is.False);
                State(weapons, "m_grappleAttached", false);
                Assert.That(Compatible(), Is.True, "Cleared state must allow emotes again.");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        [Test]
        public void AllSixClips_RestorePoseAndNeverMoveGameplayRoot()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
            try
            {
                var playback = player.GetComponent<PlayerEmotePlayback>(); Call(playback, "Awake");
                var bones = (Transform[])Get(playback, "m_bones");
                var positions = bones.Select(b => b.localPosition).ToArray();
                var rotations = bones.Select(b => b.localRotation).ToArray();
                Vector3 root = player.transform.position;
                foreach (var definition in (EmoteDefinition[])Get(player.GetComponent<PlayerEmoteController>(), "m_definitions"))
                {
                    for (int frame = 0; frame <= 60; frame++)
                    {
                        playback.Sample(definition, definition.Duration * frame / 60f);
                        Assert.That(player.transform.position, Is.EqualTo(root));
                        Assert.That(player.transform.rotation, Is.EqualTo(Quaternion.identity));
                        playback.RestorePose();
                        for (int b = 0; b < bones.Length; b++)
                        {
                            Assert.That(Vector3.Distance(bones[b].localPosition, positions[b]), Is.LessThan(.00001f));
                            Assert.That(Quaternion.Angle(bones[b].localRotation, rotations[b]), Is.LessThan(.05f));
                        }
                    }
                    if (definition.Loop)
                    {
                        definition.Clip.SampleAnimation(player.GetComponent<CouchGuyAnimationDriver>().Animator.gameObject, 0);
                        var first = bones.Select(b => b.localRotation).ToArray();
                        definition.Clip.SampleAnimation(player.GetComponent<CouchGuyAnimationDriver>().Animator.gameObject, definition.Duration);
                        for (int b = 0; b < bones.Length; b++) Assert.That(Quaternion.Angle(first[b], bones[b].localRotation), Is.LessThan(.1f));
                        for (int b = 0; b < bones.Length; b++) bones[b].SetLocalPositionAndRotation(positions[b], rotations[b]);
                    }
                }
            }
            finally { Object.DestroyImmediate(player); }
        }

        [MenuItem("Couch Guys/Player/Validate Emotes")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                SessionState.SetBool("Emotes.RunAfterStop", true);
                EditorApplication.ExitPlaymode();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Wait for Play mode transition.");
            File.WriteAllText(Report, "START: focused emote validation\n");
            var tests = new PlayerEmoteTests();
            tests.SavedAssets_TargetOnlyExistingRigAndRegisterNetworkBehaviour();
            Log("PASS: six clips/definitions, original rig bindings, saved wheel/font, network registration.");
            tests.Wheel_MapsAllDirectionsAndCancelsAtCentreAndOutside();
            Log("PASS: six radial directions, centre and outside cancellation.");
            tests.Eligibility_ReadsExistingHealthCarryVehicleAndActionState();
            Log("PASS: downed/dead, couch carry, truck seat, hoverboard, healing, attack and grapple eligibility guards.");
            tests.AllSixClips_RestorePoseAndNeverMoveGameplayRoot();
            Log("PASS: all six animations sampled across full duration, loop seams, exact pose restoration, unchanged gameplay root.");
            SessionState.SetBool("Emotes.Validate", true);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (s_probe == null)
            {
                if (!File.Exists("Temp/PlayerEmoteValidation.request") || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                File.Delete("Temp/PlayerEmoteValidation.request");
                try { Run(); } catch (Exception e) { Log("FAIL: " + e); }
                return;
            }
            bool done;
            try
            {
                Check(Time.realtimeSinceStartup < s_deadline, "Network validation timed out.");
                if (Time.time < s_resume) return;
                done = !s_probe.MoveNext();
                if (!done && s_probe.Current is float time) s_resume = time;
            }
            catch (Exception e) { Log("FAIL: " + e); done = true; }
            if (!done) return;
            s_probe = null;
            if (s_keyboard != null) { InputSystem.RemoveDevice(s_keyboard); s_keyboard = null; }
            if (s_mouse != null) { InputSystem.RemoveDevice(s_mouse); s_mouse = null; }
            foreach (var device in DisabledDevices) if (device.added) InputSystem.EnableDevice(device);
            DisabledDevices.Clear();
            foreach (var manager in Managers)
            {
                if (manager == null) continue;
                manager.ClientManager.StopConnection(); manager.ServerManager.StopConnection(true);
            }
            Managers.Clear();
            EditorApplication.ExitPlaymode();
        }

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
            var transport = obj.GetComponent<FishNet.Transporting.Tugboat.Tugboat>();
            var transportSettings = new SerializedObject(transport);
            transportSettings.FindProperty("_maximumClients").intValue = 4;
            transportSettings.FindProperty("_port").intValue = 17863;
            transportSettings.ApplyModifiedPropertiesWithoutUndo();
            parent.SetActive(true);
            Managers.Add(manager); return manager;
        }

        private static void Keyboard(params Key[] keys)
        {
            InputSystem.QueueStateEvent(s_keyboard, new KeyboardState(keys));
        }

        private static IEnumerator Probe()
        {
            var server = Manager("Emote server"); server.ServerManager.StartConnection();
            var client = Manager("Emote owner client"); client.ClientManager.StartConnection();
            while (!client.IsClientStarted || server.ServerManager.Clients.Count < 1 || !client.ClientManager.Connection.IsValid) yield return null;
            var obj = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), new Vector3(200, 3, 200), Quaternion.identity);
            obj.GetComponent<ThirdPersonPlayerController>().enabled = false;
            server.ServerManager.Spawn(obj, server.ServerManager.Clients.Values.First());
            int objectId = obj.GetComponent<NetworkObject>().ObjectId;
            while (!client.ClientManager.Objects.Spawned.ContainsKey(objectId)) yield return null;
            var owner = client.ClientManager.Objects.Spawned[objectId].GetComponent<PlayerEmoteController>();
            owner.GetComponent<ThirdPersonPlayerController>().enabled = false;
            // Direct-RPC checks are independent of physical keyboard input in the editor.
            owner.GetComponent<PlayerInputReader>().enabled = false;
            var authoritative = obj.GetComponent<PlayerEmoteController>();
            yield return Time.time + .35f;
            Call(owner, "RequestEmoteServerRpc", (byte)255);
            yield return Time.time + .15f;
            Check(!authoritative.IsEmoting, "Invalid ID accepted.");
            for (byte id = 1; id <= 6; id++)
            {
                Call(owner, "RequestEmoteServerRpc", id);
                yield return Time.time + .35f;
                Check(owner.IsEmoting && authoritative.IsEmoting, "ID " + id + " failed server-to-owner roundtrip. owner=" +
                    ((PlayerEmoteController.EmoteState)Get(owner, "m_observed")).Id + " server=" +
                    ((PlayerEmoteController.EmoteState)Get(authoritative, "m_observed")).Id + " compatible=" +
                    typeof(PlayerEmoteController).GetProperty("Compatible", Private).GetValue(authoritative));
                Check(((PlayerEmoteController.EmoteState)Get(owner, "m_observed")).Id == id, "Wrong emote ID.");
                Check(owner.GetComponent<RobotFaceExpressionController>().CurrentExpression == owner.FindDefinition(id).Expression,
                    "Existing face controller did not display emote expression: " + id);
                if (!owner.FindDefinition(id).Loop)
                {
                    yield return Time.time + owner.FindDefinition(id).Duration;
                    Check(!owner.IsEmoting && !authoritative.IsEmoting, "One shot did not stop: " + id);
                }
                else
                {
                    yield return Time.time + owner.FindDefinition(id).Duration;
                    Check(owner.IsEmoting, "Loop stopped automatically: " + id);
                    owner.RequestInterrupt();
                    Check(!owner.IsEmoting, "Local stop was delayed.");
                    yield return Time.time + .25f;
                    Check(!authoritative.IsEmoting, "Stop RPC did not reach server.");
                }
                Log("PASS: UDP owner/server playback and completion/stop for emote " + id + ".");
            }
            Call(owner, "RequestEmoteServerRpc", (byte)1);
            yield return Time.time + .4f;
            var lateClient = Manager("Emote late observer"); lateClient.ClientManager.StartConnection();
            while (!lateClient.ClientManager.Objects.Spawned.ContainsKey(objectId)) yield return null;
            var remote = lateClient.ClientManager.Objects.Spawned[objectId].GetComponent<PlayerEmoteController>();
            yield return Time.time + .2f;
            Check(remote != owner && remote != authoritative && remote.IsEmoting, "Late observer did not receive active emote.");
            var remoteState = (PlayerEmoteController.EmoteState)Get(remote, "m_observed");
            var ownerState = (PlayerEmoteController.EmoteState)Get(owner, "m_observed");
            Check(remoteState.Id == ownerState.Id && remoteState.Sequence == ownerState.Sequence && remoteState.StartTick == ownerState.StartTick,
                "Late join lost original phase or sequence.");
            Call(remote, "RequestEmoteServerRpc", (byte)5);
            yield return Time.time + .3f;
            Check(((PlayerEmoteController.EmoteState)Get(authoritative, "m_observed")).Id == 1, "Non-owner changed emote.");
            Log("PASS: distinct late observer receives original ID/sequence/start tick; non-owner RPC rejected.");
            authoritative.GetComponent<PlayerHealth>().ApplyDamageServer(1, Vector3.zero, 0);
            yield return Time.time + .3f;
            Check(!owner.IsEmoting && !remote.IsEmoting, "Damage did not stop both clients.");
            Log("PASS: server damage interrupts owner and remote observer.");
            Call(owner, "RequestEmoteServerRpc", (byte)1);
            yield return Time.time + .35f;
            obj.transform.position += Vector3.right;
            yield return Time.time + .3f;
            Check(!authoritative.IsEmoting && !remote.IsEmoting, "Server displacement did not interrupt.");
            Log("PASS: server movement interrupts replicated emote.");
            // Isolate synthetic device checks from editor pointer events; restore every device in cleanup.
            foreach (var device in InputSystem.devices)
                if ((device is Keyboard || device is Mouse) && device.enabled)
                { DisabledDevices.Add(device); InputSystem.DisableDevice(device); }
            s_keyboard = InputSystem.AddDevice<Keyboard>();
            owner.GetComponent<PlayerInputReader>().enabled = true;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            Keyboard(Key.B);
            yield return Time.time + .15f;
            Check(owner.WheelOpen, "Holding B did not open wheel. focus=" + Application.isFocused +
                " owner=" + owner.IsOwner + " input=" + owner.GetComponent<PlayerInputReader>().isActiveAndEnabled +
                " canOpen=" + owner.GetComponent<PlayerInputReader>().CanOpenEmoteWheel +
                " held=" + owner.GetComponent<PlayerInputReader>().EmoteHeld +
                " compatible=" + typeof(PlayerEmoteController).GetProperty("Compatible", Private).GetValue(owner) +
                " lock=" + Cursor.lockState);
            Check(Cursor.lockState == CursorLockMode.None, "Wheel did not unlock cursor.");
            Keyboard();
            yield return Time.time + .15f;
            Check(!owner.WheelOpen && Cursor.lockState == CursorLockMode.Locked, "Release did not close/restore cursor.");
            Check(!owner.IsEmoting, "Centre release started an emote.");
            s_mouse = InputSystem.AddDevice<Mouse>();
            Keyboard(Key.B);
            yield return Time.time + .15f;
            var view = (EmoteWheelView)Get(owner, "m_wheel");
            var wheelRect = (RectTransform)Get(view, "m_wheel");
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, wheelRect.TransformPoint(Vector3.up * 220));
            InputSystem.QueueStateEvent(s_mouse, new MouseState { position = point });
            yield return Time.time + .15f;
            Check(view.Selected == 0, "Mouse direction did not highlight top wedge.");
            Keyboard();
            yield return Time.time + .35f;
            Check(owner.IsEmoting && remote.IsEmoting, "Valid wheel release did not start replicated emote.");
            Keyboard(Key.W);
            yield return Time.time + .25f;
            Check(!owner.IsEmoting && !remote.IsEmoting, "Movement input did not stop playback on both clients.");
            Keyboard();
            yield return Time.time + .15f;
            Keyboard(Key.B);
            yield return Time.time + .15f;
            Keyboard(Key.W);
            yield return Time.time + .15f;
            Check(!owner.WheelOpen, "Movement did not cancel wheel.");
            Keyboard();
            Log("PASS: actual Input System B hold/release, centre cancel, mouse highlight, release-to-play, movement interrupt and cursor restoration.");
            yield return Time.time + .3f;
            Call(owner, "RequestEmoteServerRpc", (byte)1);
            yield return Time.time + .3f;
            Check(owner.IsEmoting, "Interaction probe did not start.");
            Keyboard(Key.E);
            yield return Time.time + .3f;
            Check(!owner.IsEmoting && !remote.IsEmoting, "Interact intent did not stop both clients.");
            Keyboard();
            yield return Time.time + .3f;
            Call(owner, "RequestEmoteServerRpc", (byte)1);
            yield return Time.time + .3f;
            Check(owner.IsEmoting, "Attack probe did not start.");
            InputSystem.QueueStateEvent(s_mouse, new MouseState { position = point, buttons = 1 });
            yield return Time.time + .3f;
            Check(!owner.IsEmoting && !remote.IsEmoting, "Attack intent did not stop both clients.");
            InputSystem.QueueStateEvent(s_mouse, new MouseState { position = point });
            yield return Time.time + 1f;
            Call(owner, "RequestEmoteServerRpc", (byte)1);
            yield return Time.time + .3f;
            Check(owner.IsEmoting, "Knockdown probe did not start.");
            authoritative.GetComponent<PlayerHealth>().ApplyDamageServer(30, Vector3.forward, 0);
            yield return Time.time + .3f;
            Check(!owner.IsEmoting && !remote.IsEmoting && authoritative.GetComponent<PlayerHealth>().IsKnockedDown,
                "Knockdown did not interrupt.");
            Call(owner, "RequestEmoteServerRpc", (byte)1);
            yield return Time.time + .3f;
            Check(!owner.IsEmoting && !remote.IsEmoting, "Downed player started an emote.");
            Log("PASS: interaction and attack input interrupt both clients; knockdown interrupts and server rejects a downed start.");
            Log("ALL FOCUSED CHECKS PASSED. Three independent FishNet managers used local UDP transport.");
        }
    }
}
