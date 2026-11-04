using System.Reflection;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class SledPhysicsTests
    {
        [Test]
        public void RaisedSledHandlesAreReachableWithOffsetPlayerCapsule()
        {
            GameObject player = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
            GameObject sled = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/Sled.prefab");
            try
            {
                PlayerCouchCarrier carrier = player.GetComponent<PlayerCouchCarrier>();
                typeof(PlayerCouchCarrier).GetField("m_carrierCollider", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(carrier, player.GetComponent<CharacterController>());
                PropertyInfo origin = typeof(PlayerCouchCarrier).GetProperty("SledGrabOrigin",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                float range = new SerializedObject(carrier).FindProperty("m_interactionRange").floatValue;
                SledController controller = sled.GetComponent<SledController>();
                for (int index = 0; index < SledController.MaximumGrabPoints; index++)
                {
                    Vector3 grip = controller.GetPoint(index).transform.position;
                    // Reproduces the reported position beside the cart: capsule supported
                    // on the floor, root below it, and a raised grip above the deck.
                    player.transform.position = new Vector3(grip.x + Mathf.Sign(grip.x) * .73f, -.66f, grip.z + .08f);
                    Assert.That(Vector3.Distance(player.transform.position, grip), Is.GreaterThan(range));
                    float reach = Vector3.Distance((Vector3)origin.GetValue(carrier), grip);
                    Assert.That(reach, Is.LessThan(range), $"Handle {index} must be selectable.");
                    Assert.That(reach, Is.LessThan(controller.MaxGrabDistance), $"Handle {index} must stay attached.");
                }
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void SettingsProvideIndependentSpeedForEveryCarrierCount()
        {
            SledSettings settings = ScriptableObject.CreateInstance<SledSettings>();
            try
            {
                Set(settings, "m_speedOnePlayer", 1.1f);
                Set(settings, "m_speedTwoPlayers", 2.2f);
                Set(settings, "m_speedThreePlayers", 3.3f);
                Set(settings, "m_speedFourPlayers", 4.4f);
                Assert.That(settings.GetSpeed(1), Is.EqualTo(1.1f));
                Assert.That(settings.GetSpeed(2), Is.EqualTo(2.2f));
                Assert.That(settings.GetSpeed(3), Is.EqualTo(3.3f));
                Assert.That(settings.GetSpeed(4), Is.EqualTo(4.4f));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        private static void Set(SledSettings settings, string fieldName, float value)
        {
            typeof(SledSettings).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(settings, value);
        }
    }
}
