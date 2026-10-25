using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class GrappleGunTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [TestCase(1f)]
        [TestCase(50f)]
        [TestCase(0.01f)]
        public void EquippedMuzzleRemainsAtGunBarrelUnderScaledRig(float rigScale)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
            try
            {
                // Reproduce a weapon socket beneath an imported, scaled skeleton.
                var socket = new GameObject("ScaledWeaponSocket").transform;
                socket.SetParent(player.transform, false);
                socket.localPosition = new Vector3(0f, 1f, 0f);
                socket.localScale = Vector3.one * rigScale;
                var pistol = new GameObject("TestPistol").transform;
                pistol.SetParent(socket, false);
                var controller = player.GetComponent<PlayerWeaponController>();
                typeof(PlayerWeaponController).GetField("m_weaponModels", PrivateInstance)
                    .SetValue(controller, new Transform[] { pistol, null, null, null });
                typeof(PlayerWeaponController).GetMethod("EnsureGrappleModel", PrivateInstance).Invoke(controller, null);

                Transform model = socket.Find("Weapon_GrappleGun");
                Assert.That(model, Is.Not.Null);
                Transform muzzle = model.Find("Muzzle");
                Vector3 barrelOffset = muzzle.position - model.position;
                Assert.That(barrelOffset.magnitude, Is.EqualTo(0.24f).Within(0.001f));
                Vector3 size = model.GetComponent<Renderer>().bounds.size;
                Assert.That(size.x, Is.EqualTo(0.22f).Within(0.001f));
                Assert.That(size.y, Is.EqualTo(0.2f).Within(0.001f));
                Assert.That(size.z, Is.EqualTo(0.4f).Within(0.001f));
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void PickupShopAndVisualAssetsAreReadyForNetworkSpawning()
        {
            GameObject pickup = Resources.Load<GameObject>("WeaponPickups/GrappleGun");
            Assert.That(pickup, Is.Not.Null);
            Assert.That(pickup.GetComponent<WeaponPickup>(), Is.Not.Null);
            Assert.That(pickup.GetComponent<Rigidbody>().isKinematic, Is.False);
            Assert.That(pickup.GetComponent<Collider>().isTrigger, Is.False);
            Assert.That(new SerializedObject(pickup.GetComponent<WeaponPickup>())
                .FindProperty("m_weaponType").intValue, Is.EqualTo((int)PlayerWeaponType.GrappleGun));
            Assert.That(new SerializedObject(pickup.GetComponent<NetworkTransform>())
                .FindProperty("_clientAuthoritative").boolValue, Is.False);
            var spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(
                "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            Assert.That(spawnables.Prefabs.Contains(pickup.GetComponent<NetworkObject>()), Is.True);
            GameObject model = Resources.Load<GameObject>("Weapons/GrappleGunModel");
            Assert.That(model.GetComponentInChildren<MeshFilter>().sharedMesh, Is.Not.Null);
            Assert.That(model.transform.Find("Muzzle"), Is.Not.Null);
            Assert.That(Resources.Load<Material>("Weapons/GrappleRope"), Is.Not.Null);
            ShopItemDefinition item = Resources.Load<ShopCatalogue>("Shop/DeliveryManagerShop").Find("weapon_grapplegun");
            Assert.That(item, Is.Not.Null);
            Assert.That(item.PurchaseBehaviour, Is.TypeOf<WeaponPurchaseBehaviour>());
            Assert.That(item.Price, Is.GreaterThan(0));
        }

        [Test]
        public void GrappleMigrationPreservesExistingWeaponTuning()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
            try
            {
                var controller = player.GetComponent<PlayerWeaponController>();
                WeaponDefinition[] original = WeaponDefinition.CreateDefaults().Take(3).ToArray();
                typeof(WeaponDefinition).GetField("m_fireInterval", PrivateInstance).SetValue(original[0], 0.77f);
                typeof(PlayerWeaponController).GetField("m_weapons", PrivateInstance).SetValue(controller, original);
                typeof(PlayerWeaponController).GetMethod("EnsureWeaponDefinitions", PrivateInstance).Invoke(controller, null);
                var migrated = (WeaponDefinition[])typeof(PlayerWeaponController).GetField("m_weapons", PrivateInstance).GetValue(controller);
                Assert.That(migrated.Length, Is.EqualTo(4));
                for (int i = 0; i < original.Length; i++) Assert.That(migrated[i], Is.SameAs(original[i]));
                Assert.That(migrated[0].FireInterval, Is.EqualTo(0.77f));
                Assert.That(migrated[3].Type, Is.EqualTo(PlayerWeaponType.GrappleGun));
                Assert.That(migrated[3].Range, Is.GreaterThan(0f));
                Assert.That(migrated[3].PullForce, Is.GreaterThan(0f));
                Assert.That(migrated[3].PullSpeed, Is.GreaterThan(0f));
            }
            finally { Object.DestroyImmediate(player); }
        }

        [Test]
        public void PullAcceleratesSmoothlyCapsPullSpeedAndPreservesOtherMomentum()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
            try
            {
                var motor = player.GetComponent<ThirdPersonPlayerController>();
                FieldInfo velocity = typeof(ThirdPersonPlayerController).GetField("m_externalVelocity", PrivateInstance);
                MethodInfo pull = typeof(ThirdPersonPlayerController).GetMethod("ApplyGrapplePull", PrivateInstance);
                velocity.SetValue(motor, Vector3.forward * 5f);
                object[] args = { Vector3.right, 45f, 12f, 0.02f };
                pull.Invoke(motor, args);
                Vector3 first = (Vector3)velocity.GetValue(motor);
                Assert.That(first.x, Is.EqualTo(0.9f).Within(0.0001f));
                Assert.That(first.z, Is.EqualTo(5f));
                for (int i = 0; i < 100; i++) pull.Invoke(motor, args);
                Vector3 capped = (Vector3)velocity.GetValue(motor);
                Assert.That(capped.x, Is.EqualTo(12f).Within(0.0001f));
                Assert.That(capped.z, Is.EqualTo(5f));
                velocity.SetValue(motor, Vector3.right * 20f);
                pull.Invoke(motor, args);
                Assert.That((Vector3)velocity.GetValue(motor), Is.EqualTo(Vector3.right * 20f));
            }
            finally { Object.DestroyImmediate(player); }
        }
    }
}
