using System;
using System.IO;
using System.Reflection;
using CouchGuys.Player;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class PlayerRingKnockbackTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Flags).SetValue(target, value);

        private static void Sync<T>(PlayerHealth health, string field, T value) =>
            ((SyncVar<T>)typeof(PlayerHealth).GetField(field, Flags).GetValue(health)).Value = value;

        private static void Call(object target, string method) =>
            target.GetType().GetMethod(method, Flags).Invoke(target, null);

        [Test]
        public void DownedRingFollowsCurrentNetworkRoot()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
            try
            {
                var health = root.GetComponent<PlayerHealth>();
                var hover = root.GetComponent<RobotHoverVFX>();
                Call(hover, "Awake");
                Sync(health, "m_knockedDown", true);
                Call(hover, "AnimateHover");
                Vector3 offset = root.transform.InverseTransformPoint(hover.RingWorldPosition);

                root.transform.SetPositionAndRotation(new Vector3(12f, 4f, -8f), Quaternion.Euler(0f, 110f, 0f));
                // The falling body rotates independently of the movement root.
                root.transform.Find("Visual").localRotation = Quaternion.Euler(78f, 0f, 0f);
                Call(hover, "AnimateHover");
                Assert.That(Vector3.Distance(hover.RingWorldPosition, root.transform.TransformPoint(offset)),
                    Is.LessThan(0.001f));

                root.transform.position += new Vector3(-5f, -2f, 3f);
                Call(hover, "AnimateHover");
                Assert.That(Vector3.Distance(hover.RingWorldPosition, root.transform.TransformPoint(offset)),
                    Is.LessThan(0.001f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RecoveryKeepsCurrentBodyPosition()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
            try
            {
                var health = root.GetComponent<PlayerHealth>();
                Sync(health, "m_knockedDown", true);
                Sync(health, "m_returningToRing", true);
                Set(health, "m_localRingRecovery", true);
                Set(health, "m_ringRecoveryDuration", 100f);
                Vector3 current = new(12f, 4f, -8f);
                root.transform.position = current;
                Call(health, "LateUpdate");
                Assert.That(root.transform.position, Is.EqualTo(current));
                current += Vector3.right * 3f;
                root.transform.position = current;
                Call(health, "LateUpdate");
                Assert.That(root.transform.position, Is.EqualTo(current));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void HoverboardDeathRestoresOnFootStateRegardlessOfReplicationOrder()
        {
            foreach (bool deathArrivesFirst in new[] { true, false })
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
                var board = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Project/Prefabs/Vehicles/MagneticHoverboard.prefab"));
                try
                {
                    root.transform.position = new Vector3(20000f, 5f, 20000f);
                    board.transform.position = root.transform.position + Vector3.forward * 20f;
                    var health = root.GetComponent<PlayerHealth>();
                    var interactor = root.GetComponent<PlayerHoverboardInteractor>();
                    var capsule = root.GetComponent<CharacterController>();
                    var motor = root.GetComponent<ThirdPersonPlayerController>();
                    var vfx = root.GetComponent<RobotHoverVFX>();
                    var networkTransform = root.GetComponent<NetworkTransform>();
                    Call(motor, "Awake");
                    Call(vfx, "Awake");
                    Sync(health, "m_currentHealth", 100f);
                    var mount = (SyncVar<NetworkObject>)typeof(PlayerHoverboardInteractor)
                        .GetField("m_board", Flags).GetValue(interactor);
                    mount.Value = board.GetComponent<NetworkObject>();
                    Call(interactor, "ApplyState");
                    Assert.That(capsule.enabled, Is.False, "Mounted player must use the driver hitbox.");
                    Assert.That(motor.ExternalControlPaused, Is.True);

                    void Die()
                    {
                        Sync(health, "m_currentHealth", 0f);
                        Sync(health, "m_knockedDown", true);
                        typeof(PlayerHealth).GetMethod("ApplyKnockdownState", Flags)
                            .Invoke(health, new object[] { true });
                    }

                    if (deathArrivesFirst) Die();
                    mount.Value = null;
                    Call(interactor, "ApplyState");
                    // The exit RPC may run before or after the board SyncVar callback.
                    Call(interactor, "RestoreControls");
                    if (!deathArrivesFirst) Die();

                    Assert.That(capsule.enabled, Is.True, "Downed player still needs on-foot collision and knockback.");
                    Assert.That(vfx.enabled, Is.True, "The downed player's ground ring must resume.");
                    Assert.That(motor.ExternalControlPaused, Is.False);
                    Assert.That(motor.MovementLocked, Is.True, "Restoring the capsule must not unlock a downed player.");
                    var serialized = new SerializedObject(networkTransform);
                    Assert.That(serialized.FindProperty("_synchronizePosition").boolValue, Is.True);
                    Assert.That(serialized.FindProperty("_synchronizeRotation").boolValue, Is.True);
                    var hitbox = (CapsuleCollider)typeof(PlayerHoverboardInteractor)
                        .GetField("m_driverHitbox", Flags).GetValue(interactor);
                    Assert.That(hitbox.enabled, Is.False);

                    // Revival and depot respawn both restore health and clear knockdown.
                    Sync(health, "m_currentHealth", 50f);
                    Sync(health, "m_knockedDown", false);
                    typeof(PlayerHealth).GetMethod("ApplyKnockdownState", Flags)
                        .Invoke(health, new object[] { false });
                    Assert.That(capsule.enabled, Is.True);
                    Assert.That(motor.MovementLocked, Is.False);
                    Vector3 before = root.transform.position;
                    motor.Teleport(before + Vector3.right * 2f, root.transform.rotation);
                    capsule.Move(Vector3.right);
                    Assert.That(root.transform.position.x, Is.GreaterThan(before.x + 2.5f),
                        "Recovered player must be able to move into server-validated interaction range.");
                }
                finally { Object.DestroyImmediate(root); Object.DestroyImmediate(board); }
            }
        }

        [Test]
        public void SavedSmokeIsLoopingBoundedAndClearedOnDisable()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
            try
            {
                var health = root.GetComponent<PlayerHealth>();
                var smoke = (ParticleSystem)typeof(PlayerHealth).GetField("m_downedSmoke", Flags).GetValue(health);
                Assert.That(smoke, Is.Not.Null);
                Assert.That(smoke.transform.IsChildOf(root.transform), Is.True);
                Assert.That(smoke.main.loop, Is.True);
                Assert.That(smoke.main.playOnAwake, Is.False);
                Assert.That(smoke.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));
                Assert.That(smoke.main.maxParticles, Is.LessThanOrEqualTo(32));
                Assert.That(smoke.emission.rateOverTime.constant, Is.GreaterThan(0f));
                Assert.That(smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial, Is.Not.Null);
                smoke.Play();
                smoke.Emit(3);
                Call(health, "OnDisable");
                Assert.That(smoke.isPlaying, Is.False);
                Assert.That(smoke.particleCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Couch Guys/Player/Test Ring Knockback %#&k")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run the downed-player checks outside Play mode.");
            Directory.CreateDirectory("Temp/PlayerRingKnockback");
            try
            {
                var tests = new PlayerRingKnockbackTests();
                tests.DownedRingFollowsCurrentNetworkRoot();
                tests.RecoveryKeepsCurrentBodyPosition();
                tests.HoverboardDeathRestoresOnFootStateRegardlessOfReplicationOrder();
                tests.SavedSmokeIsLoopingBoundedAndClearedOnDisable();
                File.WriteAllText("Temp/PlayerRingKnockback/results.txt",
                    "PASS: downed ring follows moved and rotated root; recovery stays at current body; hoverboard death restores collision, movement and transform sync in either replication order; saved smoke loops locally, has bounded particles and clears on disable.\n");
                Debug.Log("Downed-player checks PASS.");
            }
            catch (Exception error)
            {
                File.WriteAllText("Temp/PlayerRingKnockback/results.txt", "FAIL: " + error);
                throw;
            }
        }
    }
}
