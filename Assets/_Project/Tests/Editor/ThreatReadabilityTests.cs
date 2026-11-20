using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Object;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class ThreatReadabilityTests
    {
        internal static readonly string[] Prefabs = { "GrapplerThief", "Thief_RocketLauncher", "RogueCar" };

        [Test]
        public void SavedThreatsHaveEditableVisualsAndSpatialVoices()
        {
            foreach (string name in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Enemies/{name}.prefab");
                Assert.That(prefab, Is.Not.Null, name);
                var cue = prefab.GetComponent<ThreatTelegraph>();
                Assert.That(cue, Is.Not.Null, name);
                var data = new SerializedObject(cue);
                foreach (string field in new[] { "m_preparationSignal", "m_directionMarker", "m_recoverySignal",
                    "m_arrivalSource", "m_attackSource", "m_arrivalClip", "m_preparationClip", "m_commitClip", "m_recoveryClip" })
                    Assert.That(data.FindProperty(field).objectReferenceValue, Is.Not.Null, name + ": " + field);
                foreach (string field in new[] { "m_arrivalSource", "m_attackSource" })
                {
                    var source = (AudioSource)data.FindProperty(field).objectReferenceValue;
                    Assert.That(source.spatialBlend, Is.EqualTo(1f), name);
                    Assert.That(source.playOnAwake, Is.False);
                    Assert.That(source.loop, Is.False);
                }
                foreach (string field in new[] { "m_preparationSignal", "m_directionMarker", "m_recoverySignal" })
                {
                    var visual = (Transform)data.FindProperty(field).objectReferenceValue;
                    Assert.That(visual.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0));
                    Assert.That(visual.GetComponentsInChildren<Collider>(true), Is.Empty, "Warnings must not block shots or movement.");
                    Assert.That(visual.gameObject.activeSelf, Is.False);
                }
                var network = new SerializedObject(prefab.GetComponent<NetworkObject>());
                var behaviours = network.FindProperty("NetworkBehaviours");
                Assert.That(Enumerable.Range(0, behaviours.arraySize).Any(i => behaviours.GetArrayElementAtIndex(i).objectReferenceValue == cue), Is.True);
            }
        }

        [Test]
        public void CommitmentLeavesTimeToDodgeAndRecoveryLeavesAnOpening()
        {
            var grapple = AssetDatabase.LoadAssetAtPath<GrapplerThiefSettings>("Assets/_Project/Settings/Thieves/GrapplerThiefSettings.asset");
            Assert.That(grapple.AimWindup, Is.GreaterThanOrEqualTo(1f));
            Assert.That(grapple.AimCommitLead, Is.InRange(.25f, grapple.AimWindup));
            Assert.That(grapple.RewindDuration, Is.GreaterThanOrEqualTo(1f));
            var rocket = AssetDatabase.LoadAssetAtPath<ThiefSettings>("Assets/_Project/Settings/Thieves/RocketLauncherThiefSettings.asset");
            Assert.That(rocket.Combat.RocketWindup, Is.GreaterThanOrEqualTo(1.25f));
            Assert.That(rocket.Combat.RocketAimCommitLead, Is.InRange(.3f, rocket.Combat.RocketWindup));
            foreach (string tier in new[] { "Medium", "Hard" })
            {
                var car = AssetDatabase.LoadAssetAtPath<RogueCarSettings>($"Assets/_Project/Settings/Delivery/RogueCarSettings_{tier}Delivery.asset");
                Assert.That(car.ChargeWindup, Is.GreaterThanOrEqualTo(1.5f));
                Assert.That(car.ChargeRecovery, Is.GreaterThanOrEqualTo(1.5f));
                Assert.That(car.ChargeTurnSpeed, Is.LessThan(car.TurnSpeed));
            }
            var projectile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Weapons/RocketLauncher/RocketProjectile.prefab");
            var flight = (AudioSource)new SerializedObject(projectile.GetComponent<RocketProjectile>())
                .FindProperty("m_flightAudio").objectReferenceValue;
            Assert.That(flight, Is.Not.Null);
            Assert.That(flight.clip, Is.Not.Null);
            Assert.That(flight.spatialBlend, Is.EqualTo(1f));
            Assert.That(flight.loop, Is.True);
            Assert.That(flight.playOnAwake, Is.False);
        }
    }

    [InitializeOnLoad]
    public static class ThreatReadabilityPlayProbe
    {
        private const string Pending = "ThreatReadability.PlayPending";
        private const string Result = "Tools/ThreatReadabilityValidation.txt";
        private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
        private static IEnumerator s_probe;
        private static NetworkManager s_host, s_remote;
        static ThreatReadabilityPlayProbe() => EditorApplication.playModeStateChanged += StateChanged;

        [MenuItem("Couch Guys/Tests/Threat Readability/Validate Assets")]
        public static void ValidateAssets()
        {
            var tests = new ThreatReadabilityTests();
            tests.SavedThreatsHaveEditableVisualsAndSpatialVoices();
            tests.CommitmentLeavesTimeToDodgeAndRecoveryLeavesAnOpening();
            File.WriteAllText(Result, "PASS: saved prefab references, warning meshes without colliders, spatial audio, registered network behaviours, wind-up/commit/recovery settings.\n");
        }

        [MenuItem("Couch Guys/Tests/Threat Readability/Run Multiplayer Probe")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit the current Play session first.");
            ValidateAssets();
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); s_probe = Probe(); EditorApplication.update += Tick; }
            if (state == PlayModeStateChange.ExitingPlayMode) { EditorApplication.update -= Tick; s_probe = null; }
        }
        private static void Tick()
        {
            try { if (s_probe != null && !s_probe.MoveNext()) Finish(); }
            catch (Exception error) { File.AppendAllText(Result, "FAIL: " + error + "\n"); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick; s_probe = null;
            if (s_remote != null) s_remote.ClientManager.StopConnection();
            if (s_host != null) { s_host.ClientManager.StopConnection(); s_host.ServerManager.StopConnection(true); }
            EditorApplication.ExitPlaymode();
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) => File.AppendAllText(Result, "PASS: " + message + "\n");
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Fields).SetValue(target, value);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Fields).Invoke(target, args);
        private static NetworkManager Manager()
        {
            var staging = new GameObject("Inactive test staging"); staging.SetActive(false);
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"), staging.transform);
            var manager = go.GetComponentInChildren<NetworkManager>(true);
            var data = new SerializedObject(manager);
            data.FindProperty("_persistence").enumValueIndex = (int)NetworkManager.PersistenceType.AllowMultiple;
            data.ApplyModifiedPropertiesWithoutUndo();
            var spawner = go.GetComponentInChildren<CouchGuys.Networking.NeighbourhoodPlayerSpawner>(true);
            if (spawner != null) Object.DestroyImmediate(spawner);
            go.transform.SetParent(null, false); Object.Destroy(staging);
            return manager;
        }

        private static IEnumerator Probe()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = Vector3.down * .5f; floor.transform.localScale = new Vector3(200, 1, 200);
            floor.AddComponent<NavMeshSurface>().BuildNavMesh();
            s_host = Manager(); yield return null;
            s_host.TransportManager.Transport.SetMaximumClients(3);
            s_host.ServerManager.StartConnection(); s_host.ClientManager.StartConnection();
            float deadline = Time.realtimeSinceStartup + 12f;
            while (s_host.ServerManager.Clients.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Check(s_host.ServerManager.Clients.Count == 1, "Host failed to connect.");
            var enemies = ThreatReadabilityTests.Prefabs.Select((name, i) =>
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Enemies/{name}.prefab"), new Vector3(i * 8, .05f, 0), Quaternion.identity)).ToArray();
            foreach (var enemy in enemies)
            {
                if (enemy.TryGetComponent(out ThiefNavigator nav)) nav.enabled = false;
                if (enemy.TryGetComponent(out GrapplerThief grapple)) grapple.enabled = false;
                if (enemy.TryGetComponent(out EnemyWeapon weapon)) weapon.enabled = false;
                if (enemy.TryGetComponent(out RogueCar car)) car.enabled = false;
                s_host.ServerManager.Spawn(enemy.GetComponent<NetworkObject>());
            }
            yield return null;
            // Health initialisation restores weapon availability on spawn. The fixture
            // drives attacks explicitly, so disable it after those lifecycle callbacks.
            foreach (var enemy in enemies)
                if (enemy.TryGetComponent(out EnemyWeapon weapon)) weapon.enabled = false;
            foreach (var enemy in enemies) enemy.GetComponent<ThreatTelegraph>().PrepareServer(null, enemy.transform.position + Vector3.forward * 10, 4f);
            s_remote = Manager(); yield return null; s_remote.ClientManager.StartConnection();
            deadline = Time.realtimeSinceStartup + 12f;
            while (enemies.Any(e => !s_remote.ClientManager.Objects.Spawned.ContainsKey(e.GetComponent<NetworkObject>().ObjectId)) && Time.realtimeSinceStartup < deadline) yield return null;
            foreach (var enemy in enemies)
            {
                int id = enemy.GetComponent<NetworkObject>().ObjectId;
                Check(s_remote.ClientManager.Objects.Spawned.TryGetValue(id, out var replica), "Late client missing " + enemy.name);
                var hostCue = enemy.GetComponent<ThreatTelegraph>();
                var clientCue = replica.GetComponent<ThreatTelegraph>();
                Check(clientCue.IsPreparing && Mathf.Abs(hostCue.Remaining - clientCue.Remaining) < .25f,
                    "Late client did not retain the server's warning deadline.");
                Check(replica.transform.Find("Preparation Signal").gameObject.activeSelf, "Client warning not visible.");
                hostCue.LockServer(enemy.transform.position + Vector3.forward * 10);
            }
            Pass("three saved threats spawn on host and independent late client; preparation visuals and server tick deadlines agree");
            CapturePreview();
            deadline = Time.realtimeSinceStartup + .4f;
            while (Time.realtimeSinceStartup < deadline) yield return null;
            foreach (var enemy in enemies)
            {
                var cue = enemy.GetComponent<ThreatTelegraph>();
                Check(cue.Phase == ThreatAttackPhase.Locked, "Lock not retained.");
                var remoteCue = s_remote.ClientManager.Objects.Spawned[enemy.GetComponent<NetworkObject>().ObjectId].GetComponent<ThreatTelegraph>();
                Check(remoteCue.Phase == ThreatAttackPhase.Locked && Vector3.Distance(cue.AimPoint, remoteCue.AimPoint) < .01f, "Locked bearing not replicated.");
                cue.RecoverServer(1f);
            }
            deadline = Time.realtimeSinceStartup + .3f;
            while (Time.realtimeSinceStartup < deadline) yield return null;
            foreach (var enemy in enemies)
            {
                var replica = s_remote.ClientManager.Objects.Spawned[enemy.GetComponent<NetworkObject>().ObjectId];
                Check(replica.GetComponent<ThreatTelegraph>().IsRecovering && replica.transform.Find("Recovery Signal").gameObject.activeSelf,
                    "Recovery opening not replicated.");
                enemy.GetComponent<ThreatTelegraph>().CancelServer();
            }
            deadline = Time.realtimeSinceStartup + .3f;
            while (Time.realtimeSinceStartup < deadline) yield return null;
            foreach (var enemy in enemies)
                Check(!s_remote.ClientManager.Objects.Spawned[enemy.GetComponent<NetworkObject>().ObjectId].transform.Find("Recovery Signal").gameObject.activeSelf,
                    "Cancelled cue remained visible.");
            Pass("locked aim, recovery windows and cancellation replicate without restarting the client's wind-up");

            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab"), new Vector3(0, .05f, 10), Quaternion.identity);
            s_host.ServerManager.Spawn(player, s_host.ServerManager.Clients.Values.First());
            player.GetComponent<ThirdPersonPlayerController>().enabled = false;
            var health = player.GetComponent<PlayerHealth>();
            var grappleAttack = enemies[0].GetComponent<GrapplerThief>();
            var grappleCue = enemies[0].GetComponent<ThreatTelegraph>();
            Set(grappleAttack, "m_target", health);
            grappleCue.PrepareServer(player, health.AimPoint, 1.1f);
            grappleCue.LockServer(health.AimPoint);
            Vector3 locked = grappleCue.AimPoint;
            player.transform.position += Vector3.right * 4;
            Physics.SyncTransforms();
            Check(grappleCue.AimPoint == locked, "Grappler aim followed a dodging target after lock.");
            Call(grappleAttack, "FireGrappleServer");
            Check(!grappleAttack.IsHolding, "Grapple hit a player who dodged away from the committed shot.");
            Pass("real grapple trace misses a sidestep after aim lock");
            grappleCue.CancelServer();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = (enemies[0].transform.position + health.AimPoint) * .5f;
            wall.transform.localScale = new Vector3(5, 3, 1);
            Physics.SyncTransforms();
            Check(!grappleAttack.HasLineOfSight(health), "Cover did not block grapple targeting.");
            grappleCue.PrepareServer(player, health.AimPoint, 1.1f);
            grappleCue.LockServer(health.AimPoint);
            Call(grappleAttack, "FireGrappleServer");
            Check(!grappleAttack.IsHolding, "Grapple attached through solid cover.");
            Object.DestroyImmediate(wall);
            grappleCue.PrepareServer(player, health.AimPoint, 1.1f);
            Call(grappleAttack, "StopForDepartureServer");
            Check(grappleCue.Phase == ThreatAttackPhase.Idle, "Leaving combat retained a queued grapple.");
            Pass("solid cover blocks grapple targeting and attachment; leaving combat cancels a queued attack");

            var rocketAttack = enemies[1].GetComponent<EnemyWeapon>();
            var rocketCue = enemies[1].GetComponent<ThreatTelegraph>();
            rocketAttack.enabled = false;
            Set(enemies[1].GetComponent<ThiefHealth>(), "m_weapon", null);
            Set(rocketAttack, "m_target", health);
            rocketCue.PrepareServer(player, health.AimPoint, 1.5f);
            rocketCue.LockServer(health.AimPoint);
            locked = rocketCue.AimPoint;
            Call(rocketAttack, "UpdateRocketAttackServer", Vector3.forward);
            Check(!Object.FindObjectsByType<RocketProjectile>(FindObjectsSortMode.None).Any(p => p.IsServerInitialized),
                "Rocket fired before the warning deadline.");
            player.transform.position += Vector3.right * 4;
            deadline = Time.realtimeSinceStartup + 8f;
            while (rocketCue.Remaining > 0f && Time.realtimeSinceStartup < deadline) yield return null;
            Check(rocketCue.Remaining <= 0f, "Server simulation did not advance through the rocket wind-up.");
            Check(rocketCue.Phase == ThreatAttackPhase.Locked, $"Fixture lost its committed rocket warning (weapon enabled={rocketAttack.enabled}).");
            Call(rocketAttack, "UpdateRocketAttackServer", Vector3.forward);
            var projectile = Object.FindObjectsByType<RocketProjectile>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsServerInitialized);
            Check(projectile != null && Vector3.Angle(projectile.transform.forward, locked - projectile.transform.position) < 1f,
                $"Rocket did not fire towards its committed point: phase={rocketCue.Phase}, remaining={rocketCue.Remaining}, projectile={projectile}, locked={locked}, position={(projectile != null ? projectile.transform.position : Vector3.zero)}, forward={(projectile != null ? projectile.transform.forward : Vector3.zero)}.");
            Check(rocketCue.Phase == ThreatAttackPhase.Committed, "Rocket did not enter committed phase.");
            var flight = (AudioSource)new SerializedObject(projectile).FindProperty("m_flightAudio").objectReferenceValue;
            Check(flight != null && flight.isPlaying, "Spawned rocket flight audio did not start on the host client.");
            Pass("real rocket fires at the locked position after its complete wind-up, despite subsequent target movement");

            var rogue = enemies[2].GetComponent<RogueCar>();
            var carCue = enemies[2].GetComponent<ThreatTelegraph>();
            var settings = AssetDatabase.LoadAssetAtPath<RogueCarSettings>("Assets/_Project/Settings/Delivery/RogueCarSettings_HardDelivery.asset");
            Call(rogue, "Initialise", settings, null);
            Set(rogue, "m_target", health);
            carCue.PrepareServer(player, rogue.transform.position + Vector3.forward * 10, .2f);
            Check(!rogue.CanDamagePlayers, "Approaching/preparing car can damage players before its warning completes.");
            carCue.LockServer(rogue.transform.position + Vector3.forward * 10);
            deadline = Time.realtimeSinceStartup + 1f;
            while (carCue.Remaining > 0f && Time.realtimeSinceStartup < deadline) yield return null;
            Call(rogue, "UpdateChargeServer");
            Check(carCue.Phase == ThreatAttackPhase.Committed && rogue.CanDamagePlayers, "Warned car failed to commit.");
            var destination = (Vector3)typeof(RogueCar).GetField("m_chargeDestination", Fields).GetValue(rogue);
            player.transform.position += Vector3.right * 10;
            Call(rogue, "UpdateChargeServer");
            Check((Vector3)typeof(RogueCar).GetField("m_chargeDestination", Fields).GetValue(rogue) == destination, "Car retargeted during its charge.");
            Pass("rogue damage is gated until commitment and its charge destination survives target movement");
            Pass("multiplayer threat readability probe complete");
        }

        private static void CapturePreview()
        {
            foreach (var replica in s_remote.ClientManager.Objects.Spawned.Values)
                foreach (var renderer in replica.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = true;
            var cameraObject = new GameObject("Threat preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(12, 7, -17);
            camera.transform.LookAt(new Vector3(8, 1, 0));
            camera.orthographic = true; camera.orthographicSize = 6.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .17f, .22f);
            var lightObject = new GameObject("Threat preview light");
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(40, -35, 0);
            var target = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes("Tools/ThreatReadabilityPreview.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null;
                Object.Destroy(image); Object.Destroy(target); Object.Destroy(cameraObject); Object.Destroy(lightObject);
                foreach (var replica in s_remote.ClientManager.Objects.Spawned.Values)
                    foreach (var renderer in replica.GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = false;
            }
        }
    }
}
