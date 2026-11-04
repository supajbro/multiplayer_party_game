using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class SledPhysicsTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/" + name + ".prefab");
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        private static void Value<T>(object target, string field, T value) =>
            ((SyncVar<T>)target.GetType().GetField(field, Private).GetValue(target)).Value = value;

        [Test]
        public void RaisedSledHandlesAreReachableWithOffsetPlayerCapsule()
        {
            GameObject player = Object.Instantiate(Prefab("Player"));
            try
            {
                PlayerCouchCarrier carrier = player.GetComponent<PlayerCouchCarrier>();
                var movement = player.GetComponent<ThirdPersonPlayerController>();
                movement.SetExternalSpeedMultiplier(1.3f);
                Assert.That((float)typeof(ThirdPersonPlayerController).GetField("m_externalSpeedMultiplier", Private).GetValue(movement), Is.EqualTo(1.3f));
                typeof(PlayerCouchCarrier).GetField("m_carrierCollider", Private).SetValue(carrier, player.GetComponent<CharacterController>());
                var sled = Prefab("Vehicles/Sled").GetComponent<CouchCarryController>();
                float range = new SerializedObject(carrier).FindProperty("m_interactionRange").floatValue;
                for (int index = 0; index < 4; index++)
                {
                    Vector3 grip = sled.GetPoint(index).transform.position;
                    player.transform.position = new Vector3(grip.x + Mathf.Sign(grip.x) * .73f, -.66f, grip.z + .08f);
                    Assert.That(Vector3.Distance(player.transform.position, grip), Is.GreaterThan(range));
                    Vector3 origin = (Vector3)Call(carrier, "GetCarryInteractionOrigin", sled);
                    Assert.That(Vector3.Distance(origin, grip), Is.LessThan(range));
                    Assert.That(Vector3.Distance(origin, grip), Is.LessThan(sled.MaxCarryDistance));
                }
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void SledUsesTheSameControllerAndFourPointsWithDoubleCouchSpeeds()
        {
            var sledPrefab = Prefab("Vehicles/Sled");
            var sled = sledPrefab.GetComponent<CouchCarryController>();
            var couch = Prefab("Couch").GetComponent<CouchCarryController>();
            Assert.That(sledPrefab.GetComponents<CouchCarryController>().Length, Is.EqualTo(1));
            Assert.That(sledPrefab.GetComponentsInChildren<CouchCarryPoint>().Length, Is.EqualTo(4));
            Assert.That(sled.IsSled, Is.True);
            Assert.That(couch.IsSled, Is.False);
            Assert.That(sled.StaminaDrainMultiplier, Is.EqualTo(.35f));
            Assert.That(couch.StaminaDrainMultiplier, Is.EqualTo(1f));
            var network = new SerializedObject(sledPrefab.GetComponent<NetworkObject>());
            var behaviours = network.FindProperty("NetworkBehaviours");
            bool registered = false;
            for (int i = 0; i < behaviours.arraySize; i++) registered |= behaviours.GetArrayElementAtIndex(i).objectReferenceValue == sled;
            Assert.That(registered, Is.True);
            for (int count = 1; count <= 4; count++)
            {
                Assert.That((float)Call(sled, "GetConfiguredMoveSpeed", count), Is.EqualTo(2f * (float)Call(couch, "GetConfiguredMoveSpeed", count)));
                Assert.That(sled.GetPoint(count - 1).transform, Is.SameAs(sledPrefab.GetComponent<SledController>().GetPoint(count - 1).transform));
            }
        }

        [Test]
        public void SledMultipliersAreIndependentAndRegularCouchesKeepDefaults()
        {
            var settings = ScriptableObject.CreateInstance<SledSettings>();
            try
            {
                Assert.That(settings.MovementSpeedMultiplier, Is.EqualTo(2f));
                typeof(SledSettings).GetField("m_staminaDrainMultiplier", Private).SetValue(settings, .12f);
                Assert.That(settings.StaminaDrainMultiplier, Is.EqualTo(.12f));
                Assert.That(settings.MovementSpeedMultiplier, Is.EqualTo(2f));
                Assert.That(Prefab("Couch").GetComponent<CouchCarryController>().MovementSpeedMultiplier, Is.EqualTo(1f));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void ReplicatedStackPosesPreserveIdentityAndCanBeRemovedIndependently()
        {
            GameObject sledObject = Object.Instantiate(Prefab("Vehicles/Sled"));
            GameObject firstObject = Object.Instantiate(Prefab("Couch"));
            GameObject secondObject = Object.Instantiate(Prefab("Couch"));
            try
            {
                var sled = sledObject.GetComponent<SledController>();
                var first = firstObject.GetComponent<CouchCarryController>();
                var second = secondObject.GetComponent<CouchCarryController>();
                Call(sled.CarryController, "Awake");
                Call(first, "Awake"); Call(second, "Awake");
                Value(first, "m_deliveryId", 41); Value(second, "m_deliveryId", 42);
                Vector3 firstPose = (Vector3)Call(sled, "GetMountPosition", first);
                MountReplica(first, sled, firstPose);
                Vector3 secondPose = (Vector3)Call(sled, "GetMountPosition", second);
                MountReplica(second, sled, secondPose);
                var bounds = (Bounds)typeof(CouchCarryController).GetField("m_cargoBounds", Private).GetValue(first);
                Assert.That(secondPose.y - firstPose.y, Is.EqualTo(bounds.size.y * first.transform.localScale.y).Within(.001f));
                Assert.That(sled.MountedCouchCount, Is.EqualTo(2));
                Assert.That(first.IsPickupAvailable || second.IsPickupAvailable, Is.False, "Cargo cannot steal the handle interaction.");
                Assert.That(first.GetComponentInChildren<Collider>().enabled, Is.True, "Cargo must support another couch being placed on it.");
                sled.transform.SetPositionAndRotation(new Vector3(3, 0, 4), Quaternion.Euler(0, 70, 0));
                Assert.That(Vector3.Distance(second.transform.position, sled.CouchAnchor.TransformPoint(secondPose)), Is.LessThan(.001f));
                Value(first, "m_mountedSled", (NetworkObject)null);
                Call(first, "ApplyLoadedStateImmediately");
                Assert.That(first.transform.parent, Is.Null);
                Assert.That(first.IsPickupAvailable, Is.True);
                Assert.That(second.transform.parent, Is.SameAs(sled.CouchAnchor));
                Assert.That(sled.MountedCouchCount, Is.EqualTo(1));
                Assert.That(first.DeliveryId, Is.EqualTo(41)); Assert.That(second.DeliveryId, Is.EqualTo(42));
            }
            finally
            {
                Object.DestroyImmediate(firstObject); Object.DestroyImmediate(secondObject); Object.DestroyImmediate(sledObject);
            }
        }

        private static void MountReplica(CouchCarryController couch, SledController sled, Vector3 pose)
        {
            Value(couch, "m_sledMountPosition", pose);
            Value(couch, "m_mountedSled", sled.NetworkObject);
            Call(couch, "ApplyLoadedStateImmediately");
        }
    }
}
