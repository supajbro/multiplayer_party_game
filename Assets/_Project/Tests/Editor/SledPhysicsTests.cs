using System.Reflection;
using System.Linq;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

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
        public void LaunchedSledFallsPastCharacterSupportAndRestoresCollisionsAfterSeparation()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var sled = Object.Instantiate(Prefab("Vehicles/Sled"));
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var character = new GameObject("Character under launched sled");
                SceneManager.MoveGameObjectToScene(sled, scene);
                SceneManager.MoveGameObjectToScene(floor, scene);
                SceneManager.MoveGameObjectToScene(character, scene);
                floor.transform.position = Vector3.down * .5f;
                floor.transform.localScale = new Vector3(30f, 1f, 30f);
                var capsule = character.AddComponent<CapsuleCollider>();
                capsule.center = Vector3.up * .9f; capsule.height = 1.8f; capsule.radius = .3f;
                sled.transform.position = Vector3.up * 2.4f;
                var carry = sled.GetComponent<CouchCarryController>();
                Call(carry, "Awake");
                var body = sled.GetComponent<Rigidbody>();
                body.isKinematic = false;
                Physics.SyncTransforms();
                for (int i = 0; i < 90; i++) scene.GetPhysicsScene().Simulate(.02f);
                Assert.That(body.position.y, Is.GreaterThan(1.6f), "The fixture reproduces a sled resting on a character's head.");
                Call(carry, "IgnoreSledCharacterCollider", capsule);
                for (int i = 0; i < 90; i++) scene.GetPhysicsScene().Simulate(.02f);
                Assert.That(body.position.y, Is.LessThan(.1f), "The sled must land on the floor instead of remaining on the character.");
                var collider = sled.GetComponentInChildren<BoxCollider>();
                Assert.That(Physics.GetIgnoreCollision(collider, capsule), Is.True);
                character.transform.position = Vector3.right * 20f;
                Physics.SyncTransforms();
                Call(carry, "RestoreSledCharacterContacts");
                Assert.That(Physics.GetIgnoreCollision(collider, capsule), Is.False);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCase(0f)]
        [TestCase(43f)]
        [TestCase(90f)]
        public void BuriedSledRecoversWhileUnattendedAndCanMoveAgain(float yaw)
        {
            var simulation = EditorSceneManager.NewPreviewScene();
            var sled = Object.Instantiate(Prefab("Vehicles/Sled"));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Vector3 origin = new(48000f, 0f, 48000f);
                floor.name = "Ground sled recovery regression";
                floor.transform.position = origin - Vector3.up * .5f;
                floor.transform.localScale = new Vector3(30f, 1f, 30f);
                sled.transform.SetPositionAndRotation(origin - Vector3.up * .65f, Quaternion.Euler(0f, yaw, 0f));
                var carry = sled.GetComponent<CouchCarryController>();
                Call(carry, "Awake");
                var body = sled.GetComponent<Rigidbody>();
                body.isKinematic = false;
                Physics.SyncTransforms();
                object[] args = { Vector3.zero, Vector3.zero };
                Assert.That((bool)Call(carry, "TryGetSledGroundCorrection", args), Is.True,
                    "An embedded sled must recover without relying on a player or thief's lift force.");
                var correction = (Vector3)args[1];
                Assert.That(correction.y, Is.InRange(.6f, .8f));
                Call(carry, "RecoverSledGround", Vector3.zero);
                Physics.SyncTransforms();
                Assert.That(body.position.y, Is.GreaterThan(origin.y - .05f));
                Assert.That((bool)Call(carry, "TryGetSledGroundCorrection", new object[] { Vector3.zero, Vector3.zero }), Is.False,
                    "Recovery must stop once the sled clears the surface.");
                Assert.That(body.isKinematic, Is.False);
                Assert.That(body.constraints & RigidbodyConstraints.FreezePosition, Is.EqualTo(RigidbodyConstraints.None));
                SceneManager.MoveGameObjectToScene(sled, simulation);
                SceneManager.MoveGameObjectToScene(floor, simulation);
                Physics.SyncTransforms();
                Vector3 start = body.position;
                body.linearVelocity = Vector3.right * 2f;
                for (int i = 0; i < 30; i++) simulation.GetPhysicsScene().Simulate(.02f);
                Assert.That(body.position.x - start.x, Is.GreaterThan(.2f),
                    "The recovered body must actually move under physics, not remain pinned to the floor.");
            }
            finally { Object.DestroyImmediate(sled); Object.DestroyImmediate(floor); EditorSceneManager.ClosePreviewScene(simulation); }
        }

        [Test]
        public void SledDetectsCornerCurbButCannotLiftThroughCeiling()
        {
            var sled = Object.Instantiate(Prefab("Vehicles/Sled"));
            var curb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Vector3 origin = new(48000f, 0f, 48000f);
                sled.transform.position = origin;
                var carry = sled.GetComponent<CouchCarryController>();
                Call(carry, "Awake");
                // Only one corner overlaps the curb; the old central ray misses it.
                curb.name = "Sidewalk corner regression";
                curb.transform.position = origin + new Vector3(2.3f, .12f, 2f);
                curb.transform.localScale = new Vector3(.8f, .24f, .8f);
                ceiling.name = "Solid ceiling regression";
                ceiling.transform.position = origin + Vector3.up * 20f;
                ceiling.transform.localScale = new Vector3(10f, .2f, 10f);
                Physics.SyncTransforms();
                object[] args = { Vector3.forward, Vector3.zero };
                Assert.That((bool)Call(carry, "TryGetSledGroundCorrection", args), Is.True);
                Assert.That(((Vector3)args[1]).y, Is.InRange(.2f, .3f));
                ceiling.transform.position = origin + Vector3.up * 1.1f;
                Physics.SyncTransforms();
                Assert.That((bool)Call(carry, "TryGetSledGroundCorrection", new object[] { Vector3.forward, Vector3.zero }), Is.False,
                    "An overhead solid must block the entire recovery sweep.");
            }
            finally { Object.DestroyImmediate(sled); Object.DestroyImmediate(curb); Object.DestroyImmediate(ceiling); }
        }

        [Test]
        public void TerrainRecoveryDoesNotLiftOrdinaryCouches()
        {
            var couch = Object.Instantiate(Prefab("Couch"));
            try
            {
                var carry = couch.GetComponent<CouchCarryController>();
                Call(carry, "Awake");
                Assert.That((bool)Call(carry, "TryGetSledGroundCorrection", new object[] { Vector3.forward, Vector3.zero }), Is.False);
            }
            finally { Object.DestroyImmediate(couch); }
        }

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
        public void SledUsesFourPointsWithSlowerSpeedAndMinimalStaminaDrain()
        {
            var sledPrefab = Prefab("Vehicles/Sled");
            var sled = sledPrefab.GetComponent<CouchCarryController>();
            var couch = Prefab("Couch").GetComponent<CouchCarryController>();
            Assert.That(sledPrefab.GetComponents<CouchCarryController>().Length, Is.EqualTo(1));
            Assert.That(sledPrefab.GetComponentsInChildren<CouchCarryPoint>().Length, Is.EqualTo(4));
            Assert.That(sled.IsSled, Is.True);
            Assert.That(couch.IsSled, Is.False);
            Assert.That(sled.StaminaDrainMultiplier, Is.EqualTo(.05f));
            Assert.That(couch.StaminaDrainMultiplier, Is.EqualTo(1f));
            var network = new SerializedObject(sledPrefab.GetComponent<NetworkObject>());
            var behaviours = network.FindProperty("NetworkBehaviours");
            bool registered = false;
            for (int i = 0; i < behaviours.arraySize; i++) registered |= behaviours.GetArrayElementAtIndex(i).objectReferenceValue == sled;
            Assert.That(registered, Is.True);
            for (int count = 1; count <= 4; count++)
            {
                Assert.That((float)Call(sled, "GetConfiguredMoveSpeed", count), Is.EqualTo(.85f * (float)Call(couch, "GetConfiguredMoveSpeed", count)));
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
        public void ReplicatedDockPosesPreserveIdentityAndCanBeRemovedIndependently()
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
                Assert.That(secondPose.y, Is.EqualTo(firstPose.y).Within(.001f));
                Assert.That(Vector2.Distance(new Vector2(firstPose.x, firstPose.z), new Vector2(secondPose.x, secondPose.z)), Is.GreaterThan(1f));
                Assert.That(sled.MountedCouchCount, Is.EqualTo(2));
                Assert.That(first.IsPickupAvailable || second.IsPickupAvailable, Is.False, "Cargo cannot steal the handle interaction.");
                Assert.That(first.GetComponentInChildren<Collider>().enabled, Is.True);
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

        [Test]
        public void MixedCargoSharesCapacityAndWeaponReplicaDetachesCleanly()
        {
            var sledObject = Object.Instantiate(Prefab("Vehicles/Sled"));
            var firstObject = Object.Instantiate(Prefab("Couch"));
            var secondObject = Object.Instantiate(Prefab("Couch"));
            var thirdObject = Object.Instantiate(Prefab("Couch"));
            var weaponObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/WeaponPickups/Pistol.prefab"));
            try
            {
                var sled = sledObject.GetComponent<SledController>();
                var weapon = weaponObject.GetComponent<WeaponPickup>();
                Call(weapon, "Awake");
                var first = firstObject.GetComponent<CouchCarryController>();
                var second = secondObject.GetComponent<CouchCarryController>();
                Call(first, "Awake"); Call(second, "Awake");
                Assert.That(sled.MaximumCargoItems, Is.EqualTo(4));
                MountReplica(first, sled, (Vector3)Call(sled, "GetMountPosition", first));
                MountReplica(second, sled, (Vector3)Call(sled, "GetMountPosition", second));
                var attachment = new SledPickupAttachment { Sled = sled.NetworkObject, Slot = 1 };
                Value(weapon, "m_sledAttachment", attachment);
                Call(weapon, "ApplySledAttachment", false);
                Assert.That(sled.CargoCount, Is.EqualTo(3));
                Assert.That(sled.HasCargoSpace, Is.True);
                var third = thirdObject.GetComponent<CouchCarryController>();
                Call(third, "Awake");
                MountReplica(third, sled, (Vector3)Call(sled, "GetMountPosition", third));
                Assert.That(sled.CargoCount, Is.EqualTo(4));
                Assert.That(sled.HasCargoSpace, Is.False);
                Assert.That(weapon.transform.parent.name, Is.EqualTo("WeaponRack2"));
                Assert.That(weapon.GetComponent<Rigidbody>().isKinematic, Is.True);
                Assert.That(weapon.GetComponentInChildren<Collider>().enabled, Is.True);
                sled.transform.SetPositionAndRotation(new Vector3(5, 2, 8), Quaternion.Euler(0, 70, 0));
                Assert.That(Vector3.Distance(weapon.transform.position, weapon.transform.parent.position), Is.LessThan(.001f));
                Vector3 release = weapon.transform.position;
                Value(weapon, "m_sledAttachment", new SledPickupAttachment {
                    Slot = -1, ReleasePosition = release, ReleaseRotation = Quaternion.identity });
                Call(weapon, "ApplySledAttachment", true);
                Assert.That(weapon.transform.parent, Is.Null);
                Assert.That(Vector3.Distance(weapon.transform.position, release), Is.LessThan(.001f));
                Assert.That(sled.CargoCount, Is.EqualTo(3));
                Assert.That(sled.HasCargoSpace, Is.True);
                Assert.That(sled.MountedCouchCount, Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(weaponObject); Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject); Object.DestroyImmediate(sledObject);
                Object.DestroyImmediate(thirdObject);
            }
        }

        [TestCase(0f)]
        [TestCase(12f)]
        public void UnloadPlacementIgnoresPlayerAndStackButRejectsRealObstacles(float tilt)
        {
            var sledObject = Object.Instantiate(Prefab("Vehicles/Sled"));
            var firstObject = Object.Instantiate(Prefab("Couch"));
            var topObject = Object.Instantiate(Prefab("Couch"));
            var player = Object.Instantiate(Prefab("Player"));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var sled = sledObject.GetComponent<SledController>();
                var first = firstObject.GetComponent<CouchCarryController>();
                var top = topObject.GetComponent<CouchCarryController>();
                // Menu validation can run with a populated scene open. Keep the
                // placement fixture clear of that scene's ground and props.
                sled.transform.position = Vector3.up * 1000f;
                Call(first, "Awake"); Call(top, "Awake");
                Value(first, "m_deliveryId", 41); Value(top, "m_deliveryId", 42);
                MountReplica(first, sled, (Vector3)Call(sled, "GetMountPosition", first));
                MountReplica(top, sled, (Vector3)Call(sled, "GetMountPosition", top));
                sled.transform.rotation = Quaternion.Euler(tilt, 35f, 0f);
                floor.transform.position = sled.transform.position + Vector3.down * .5f;
                floor.transform.localScale = new Vector3(30f, 1f, 30f);
                Vector3 destination = sled.transform.TransformPoint(new SerializedObject(sled).FindProperty("m_unloadOffset").vector3Value);
                player.transform.position = destination;
                obstacle.SetActive(false);
                Physics.SyncTransforms();
                object[] args = { top, Vector3.zero, Quaternion.identity, null };
                var method = typeof(SledController).GetMethod("TryGetUnloadPose", Private);
                Assert.That((bool)method.Invoke(sled, args), Is.True, "The player must not block unloading.");
                Assert.That(Vector3.Dot((Quaternion)args[2] * Vector3.up, Vector3.up), Is.EqualTo(1f).Within(.001f));
                var bounds = (Bounds)typeof(CouchCarryController).GetField("m_cargoBounds", Private).GetValue(top);
                obstacle.transform.position = (Vector3)args[1] + (Quaternion)args[2] *
                    Vector3.Scale(bounds.center, top.transform.lossyScale);
                obstacle.transform.localScale = new Vector3(1f, 12f, 1f);
                obstacle.SetActive(true);
                Physics.SyncTransforms();
                Assert.That((bool)method.Invoke(sled, args), Is.False, "A solid obstacle must block unloading.");
                Assert.That(sled.MountedCouchCount, Is.EqualTo(2));
                Assert.That(top.transform.parent, Is.SameAs(sled.CouchAnchor));
                Assert.That(top.DeliveryId, Is.EqualTo(42));
                obstacle.SetActive(false);
                floor.SetActive(false);
                Physics.SyncTransforms();
                Assert.That((bool)method.Invoke(sled, args), Is.False, "Missing supporting ground must block unloading.");
            }
            finally
            {
                Object.DestroyImmediate(player); Object.DestroyImmediate(obstacle); Object.DestroyImmediate(floor);
                Object.DestroyImmediate(topObject); Object.DestroyImmediate(firstObject); Object.DestroyImmediate(sledObject);
            }
        }

        [Test]
        public void CouchLoadingAcceptsOffCentrePlacementButRejectsGroundAndDistantItems()
        {
            var sledObject = Object.Instantiate(Prefab("Vehicles/Sled"));
            var couchObject = Object.Instantiate(Prefab("Couch"));
            try
            {
                var sled = sledObject.GetComponent<SledController>();
                var couch = couchObject.GetComponent<CouchCarryController>();
                Call(couch, "Awake");
                Vector3 pose = (Vector3)Call(sled, "GetMountPosition", couch);
                couch.transform.position = sled.CouchAnchor.TransformPoint(pose + new Vector3(.8f, .15f, .5f));
                Assert.That((bool)Call(sled, "IsWithinLoadingSurface", couch), Is.True,
                    "Ordinary placement should not require the previous 45cm/35cm alignment.");
                couch.transform.position += Vector3.right * 4;
                Assert.That((bool)Call(sled, "IsWithinLoadingSurface", couch), Is.False);
                couch.transform.position = sled.CouchAnchor.TransformPoint(pose + Vector3.down);
                Assert.That((bool)Call(sled, "IsWithinLoadingSurface", couch), Is.False);
                couch.transform.position = sled.CouchAnchor.TransformPoint(pose);
                couch.transform.rotation = Quaternion.Euler(180, 0, 0);
                Assert.That((bool)Call(sled, "IsWithinLoadingSurface", couch), Is.False);
            }
            finally { Object.DestroyImmediate(couchObject); Object.DestroyImmediate(sledObject); }
        }

        [Test]
        public void ThreeWeaponRacksRemainDistinctAndPreservePickupAmmo()
        {
            var sledObject = Object.Instantiate(Prefab("Vehicles/Sled"));
            var weapons = new GameObject[3];
            try
            {
                var sled = sledObject.GetComponent<SledController>();
                for (int i = 0; i < weapons.Length; i++)
                {
                    weapons[i] = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/_Project/Resources/WeaponPickups/Pistol.prefab"));
                    var weapon = weapons[i].GetComponent<WeaponPickup>();
                    Call(weapon, "Awake");
                    typeof(WeaponPickup).GetField("m_magazine", Private).SetValue(weapon, 7);
                    typeof(WeaponPickup).GetField("m_reserve", Private).SetValue(weapon, 19);
                    Value(weapon, "m_sledAttachment", new SledPickupAttachment { Sled = sled.NetworkObject, Slot = i });
                    Call(weapon, "ApplySledAttachment", false);
                    Assert.That(weapon.transform.parent.name, Is.EqualTo("WeaponRack" + (i + 1)));
                    Assert.That(typeof(WeaponPickup).GetField("m_magazine", Private).GetValue(weapon), Is.EqualTo(7));
                    Assert.That(typeof(WeaponPickup).GetField("m_reserve", Private).GetValue(weapon), Is.EqualTo(19));
                }
                Assert.That(sled.CargoCount, Is.EqualTo(3));
                Assert.That(sled.HasCargoSpace, Is.True, "Three weapons leave one of the four shared cargo slots free.");
                // Edit-mode instances have not entered a player lifecycle. Invoke
                // the destruction callback explicitly, as we do Awake above.
                Call(weapons[1].GetComponent<WeaponPickup>(), "OnDestroy");
                Object.DestroyImmediate(weapons[1]);
                Assert.That(sled.CargoCount, Is.EqualTo(2));
                Assert.That(sled.HasCargoSpace, Is.True);
            }
            finally
            {
                foreach (var weapon in weapons) if (weapon != null) Object.DestroyImmediate(weapon);
                Object.DestroyImmediate(sledObject);
            }
        }

        [MenuItem("Couch Guys/Vehicles/Validate Sled Cargo")]
        public static void RunCargoValidation()
        {
            var tests = new SledPhysicsTests();
            tests.SledUsesFourPointsWithSlowerSpeedAndMinimalStaminaDrain();
            tests.SledMultipliersAreIndependentAndRegularCouchesKeepDefaults();
            tests.RaisedSledHandlesAreReachableWithOffsetPlayerCapsule();
            tests.ReplicatedDockPosesPreserveIdentityAndCanBeRemovedIndependently();
            tests.FourDockOccupancySurvivesRemovalAndReplicatedArrivalOrder();
            tests.MixedCargoSharesCapacityAndWeaponReplicaDetachesCleanly();
            tests.CouchLoadingAcceptsOffCentrePlacementButRejectsGroundAndDistantItems();
            tests.ThreeWeaponRacksRemainDistinctAndPreservePickupAmmo();
            tests.UnloadPlacementIgnoresPlayerAndStackButRejectsRealObstacles(0f);
            tests.UnloadPlacementIgnoresPlayerAndStackButRejectsRealObstacles(12f);
            Debug.Log("PASS: ten sled cargo validation checks.");
        }

        [Test]
        public void FourDockOccupancySurvivesRemovalAndReplicatedArrivalOrder()
        {
            var sledObject = Object.Instantiate(Prefab("Vehicles/Sled"));
            var replicaObject = Object.Instantiate(Prefab("Vehicles/Sled"));
            var couches = new GameObject[4];
            var replicas = new GameObject[4];
            try
            {
                var sled = sledObject.GetComponent<SledController>();
                var replica = replicaObject.GetComponent<SledController>();
                Vector3[] poses = new Vector3[4];
                for (int i=0;i<4;i++)
                {
                    couches[i]=Object.Instantiate(Prefab("Couch"));
                    var couch=couches[i].GetComponent<CouchCarryController>();
                    Call(couch,"Awake");
                    poses[i]=(Vector3)Call(sled,"GetMountPosition",couch);
                    MountReplica(couch,sled,poses[i]);
                    Assert.That(sled.MountedCouchCount,Is.EqualTo(i+1));
                    var cargoBounds=(Bounds)typeof(CouchCarryController).GetField("m_cargoBounds",Private).GetValue(couch);
                    var bottom= sled.CouchAnchor.InverseTransformPoint(couch.transform.TransformPoint(
                        new Vector3(cargoBounds.center.x,cargoBounds.min.y,cargoBounds.center.z)));
                    Assert.That(bottom.y,Is.EqualTo(0f).Within(.001f));
                    Assert.That(poses.Take(i).Any(p=>(p-poses[i]).sqrMagnitude<.01f),Is.False);
                }
                Assert.That(sled.HasCargoSpace,Is.False);
                Assert.That((int)Call(sled,"FindAvailableDock",couches[0].GetComponent<CouchCarryController>(),false),Is.EqualTo(-1));
                for(int i=3;i>=0;i--)
                {
                    replicas[i]=Object.Instantiate(Prefab("Couch"));
                    var couch=replicas[i].GetComponent<CouchCarryController>();Call(couch,"Awake");
                    MountReplica(couch,replica,poses[i]);
                }
                for(int i=0;i<4;i++)Assert.That(replica.IsCouchDockOccupied(i),Is.True);
                var removed=couches[1].GetComponent<CouchCarryController>();
                Value(removed,"m_mountedSled",(NetworkObject)null);Call(removed,"ApplyLoadedStateImmediately");
                Assert.That(sled.HasCargoSpace,Is.True);
                for(int i=0;i<4;i++)
                    if(i!=1)Assert.That(Vector3.Distance(couches[i].transform.localPosition,poses[i]),Is.LessThan(.001f));
                Assert.That(Vector3.Distance((Vector3)Call(sled,"GetMountPosition",removed),poses[1]),Is.LessThan(.001f));
                removed.transform.localScale *= 2f;
                Assert.That((int)Call(sled,"FindAvailableDock",removed,false),Is.EqualTo(-1),"An oversized couch must not clip into dock clamps.");
                removed.transform.localScale *= .5f;
                MountReplica(removed,sled,poses[1]);
                Assert.That(sled.HasCargoSpace,Is.False);
            }
            finally
            {
                foreach(var o in couches)if(o!=null)Object.DestroyImmediate(o);
                foreach(var o in replicas)if(o!=null)Object.DestroyImmediate(o);
                Object.DestroyImmediate(sledObject);Object.DestroyImmediate(replicaObject);
            }
        }

        [Test]
        public void AutomaticLoadingNearOccupiedDockUsesAnotherFreeDock()
        {
            var sledObject = Object.Instantiate(Prefab("Vehicles/Sled"));
            var occupiedObject = Object.Instantiate(Prefab("Couch"));
            var incomingObject = Object.Instantiate(Prefab("Couch"));
            try
            {
                var sled = sledObject.GetComponent<SledController>();
                var occupied = occupiedObject.GetComponent<CouchCarryController>();
                var incoming = incomingObject.GetComponent<CouchCarryController>();
                Call(occupied, "Awake");
                Call(incoming, "Awake");
                // Separate the free docks so only the occupied approach is in range.
                for (int i = 1; i < sled.CouchDockCount; i++)
                    sled.GetCouchDock(i).position += Vector3.right * (20f * i);
                occupied.transform.position = sled.GetCouchDock(0).position;
                MountReplica(occupied, sled, (Vector3)Call(sled, "GetMountPosition", occupied));
                Assert.That(sled.IsCouchDockOccupied(0), Is.True);
                var bounds = (Bounds)typeof(CouchCarryController).GetField("m_cargoBounds", Private).GetValue(incoming);
                incoming.transform.position = sled.GetCouchDock(0).position - bounds.center;

                Assert.That((bool)Call(sled, "IsWithinAutoLoadingRange", incoming), Is.True);
                int destination = (int)Call(sled, "FindAvailableDock", incoming, false);
                Assert.That(destination, Is.GreaterThan(0));
                Assert.That(Vector3.Distance(incoming.transform.position, sled.GetCouchDock(destination).position), Is.GreaterThan(10f));
                // Apply the existing replicated mount state and verify the chosen dock.
                MountReplica(incoming, sled, (Vector3)Call(sled, "GetMountPosition", incoming));
                Assert.That(sled.IsCouchDockOccupied(destination), Is.True);
                Assert.That(sled.MountedCouchCount, Is.EqualTo(2));

                incoming.transform.SetParent(null, true);
                incoming.transform.position = Vector3.one * 1000f;
                Assert.That((bool)Call(sled, "IsWithinAutoLoadingRange", incoming), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(incomingObject);
                Object.DestroyImmediate(occupiedObject);
                Object.DestroyImmediate(sledObject);
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
