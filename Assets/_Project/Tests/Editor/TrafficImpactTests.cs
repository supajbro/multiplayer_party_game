using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class TrafficImpactTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject m_object;

        [TearDown]
        public void TearDown()
        {
            if (m_object != null) Object.DestroyImmediate(m_object);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(225f)]
        public void TrafficImpulseReplacesOpposingPlayerVelocity(float heading)
        {
            m_object = new GameObject("Traffic impact player");
            m_object.SetActive(false);
            var controller = m_object.AddComponent<ThirdPersonPlayerController>();
            Vector3 direction = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            controller.ApplyExternalImpulse(-direction * 30f + Vector3.right * 5f);
            typeof(ThirdPersonPlayerController).GetField("m_currentSpeed", Private).SetValue(controller, 6f);

            controller.ApplyExternalImpulse(direction * 13f, true);

            Assert.That((ReadVelocity(controller) - direction * 13f).sqrMagnitude, Is.LessThan(0.0001f));
            Assert.That((float)typeof(ThirdPersonPlayerController).GetField("m_currentSpeed", Private)
                .GetValue(controller), Is.Zero);
            Assert.That((float)typeof(ThirdPersonPlayerController).GetField("m_vehicleImpactUntil", Private)
                .GetValue(controller), Is.GreaterThan(Time.time));
            Assert.That(controller.MovementLocked, Is.False, "Impact recovery must not latch the regular movement lock.");
        }

        [Test]
        public void OrdinaryPlayerImpulsesRemainAdditive()
        {
            m_object = new GameObject("Ordinary impact player");
            m_object.SetActive(false);
            var controller = m_object.AddComponent<ThirdPersonPlayerController>();
            controller.ApplyExternalImpulse(Vector3.forward * 3f);
            controller.ApplyExternalImpulse(Vector3.right * 4f);
            Assert.That(ReadVelocity(controller), Is.EqualTo(new Vector3(4f, 0f, 3f)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ThiefTrafficImpulseOverridesPreviousPushWithoutChangingOtherImpacts(bool directional)
        {
            m_object = new GameObject("Traffic impact thief");
            m_object.SetActive(false);
            var health = m_object.AddComponent<ThiefHealth>();
            ((SyncVar<float>)typeof(ThiefHealth).GetField("m_currentHealth", Private)
                .GetValue(health)).Value = 100f;
            var velocity = typeof(ThiefHealth).GetField("m_knockbackVelocity", Private);
            velocity.SetValue(health, Vector3.back * 20f);

            // Exercise the impact calculation without requiring a spawned network session.
            typeof(ThiefHealth).GetMethod("ApplyImpactServer", Private).Invoke(health,
                new object[] { 0f, Vector3.forward, 13f, 0f, true, directional });

            Assert.That((Vector3)velocity.GetValue(health),
                Is.EqualTo(Vector3.forward * (directional ? 13f : -7f)));
            Assert.That(health.IsBeingKnockedBack, Is.True);
        }

        private static Vector3 ReadVelocity(ThirdPersonPlayerController controller) =>
            (Vector3)typeof(ThirdPersonPlayerController).GetField("m_externalVelocity", Private).GetValue(controller);
    }
}
