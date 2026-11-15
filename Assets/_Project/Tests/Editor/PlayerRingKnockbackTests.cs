using System;
using System.IO;
using System.Reflection;
using CouchGuys.Player;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class PlayerRingKnockbackTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
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
                tests.SavedSmokeIsLoopingBoundedAndClearedOnDisable();
                File.WriteAllText("Temp/PlayerRingKnockback/results.txt",
                    "PASS: downed ring follows moved and rotated root; recovery stays at current body; saved smoke loops locally, has bounded particles and clears on disable.\n");
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
