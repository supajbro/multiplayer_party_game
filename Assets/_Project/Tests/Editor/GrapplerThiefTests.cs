using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class GrapplerThiefTests
    {
        private const string Prefab = "Assets/_Project/Prefabs/Enemies/GrapplerThief.prefab";
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(-12f)]
        [TestCase(0f)]
        [TestCase(12f)]
        public void ThrowTravelsAwayFromCouchWithConfiguredVelocity(float variation)
        {
            var settings = ScriptableObject.CreateInstance<GrapplerThiefSettings>();
            try
            {
                Vector3 velocity = settings.CalculateThrowVelocity(Vector3.right * 2f, Vector3.zero,
                    Vector3.left, variation);
                Assert.That(Vector3.Dot(velocity, Vector3.right), Is.GreaterThan(11f));
                Assert.That(Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude, Is.EqualTo(12f).Within(0.001f));
                Assert.That(velocity.y, Is.EqualTo(5f));
                Assert.That(settings.CalculateThrowVelocity(Vector3.zero, Vector3.zero, Vector3.zero, 0f),
                    Is.EqualTo(new Vector3(0f, 5f, 12f)));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void PrefabReusesThiefSystemsAndSavedGunAndRope()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<EnemyWeapon>(), Is.Null);
            Assert.That(prefab.GetComponent<ThiefNavigator>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ThiefHealth>(), Is.Not.Null);
            Assert.That(prefab.GetComponentInChildren<PedestrianRagdoll>(true), Is.Not.Null);
            var serialized = new SerializedObject(prefab.GetComponent<GrapplerThief>());
            foreach (string field in new[] { "m_settings", "m_gun", "m_muzzle", "m_rope" })
                Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            var gun = (Transform)serialized.FindProperty("m_gun").objectReferenceValue;
            for (Transform parent = gun; parent != null; parent = parent.parent)
                Assert.That(parent.gameObject.activeSelf, Is.True, parent.name);
            Assert.That(gun.parent, Is.Not.EqualTo(prefab.transform), "Gun must follow an animated hand.");
            var rope = (LineRenderer)serialized.FindProperty("m_rope").objectReferenceValue;
            Assert.That(rope.sharedMaterial, Is.Not.Null);
            var network = prefab.GetComponent<FishNet.Object.NetworkObject>();
            var behaviours = new SerializedObject(network).FindProperty("NetworkBehaviours");
            Assert.That(behaviours.arraySize, Is.EqualTo(prefab.GetComponents<FishNet.Object.NetworkBehaviour>().Length));
            for (int i = 0; i < behaviours.arraySize; i++)
            {
                var behaviour = behaviours.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.That(behaviour, Is.Not.Null);
                var data = new SerializedObject(behaviour);
                Assert.That(data.FindProperty("_networkObjectCache").objectReferenceValue, Is.EqualTo(network));
                Assert.That(data.FindProperty("_componentIndexCache").intValue, Is.EqualTo(i));
            }
            Assert.That(network.AssetPathHash, Is.Not.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab").GetComponent<FishNet.Object.NetworkObject>().AssetPathHash));
            foreach (string path in new[] { "Assets/DefaultPrefabObjects.asset", "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset" })
            {
                var collection = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(path)).FindProperty("_prefabs");
                bool registered = false;
                for (int i = 0; i < collection.arraySize; i++)
                    if (collection.GetArrayElementAtIndex(i).objectReferenceValue == prefab.GetComponent<FishNet.Object.NetworkObject>()) registered = true;
                Assert.That(registered, Is.True, path);
            }
        }

        [TestCase("Easy", 0f, 0)]
        [TestCase("Medium", 0.15f, 1)]
        [TestCase("Hard", 0.25f, 2)]
        public void DeliveryReplacementsAreConfigured(string name, float chance, int cap)
        {
            var tier = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/" + name + "Delivery.asset");
            Assert.That(tier.GrapplerThiefPrefab, Is.Not.Null);
            Assert.That(tier.GrapplerThiefChance, Is.EqualTo(chance));
            Assert.That(tier.MaxActiveGrapplerThieves, Is.EqualTo(cap));
            int replacements = 0;
            for (int roll = 0; roll < 1000; roll++)
                if (tier.ShouldReplaceWithGrappler(0, (roll + 0.5f) / 1000f)) replacements++;
            Assert.That(replacements, Is.EqualTo(Mathf.RoundToInt(chance * 1000f)));
            Assert.That(tier.ShouldReplaceWithGrappler(cap, 0f), Is.False);
            if (name != "Easy") return;
            var copy = Object.Instantiate(tier);
            try
            {
                var serialized = new SerializedObject(copy);
                serialized.FindProperty("m_grapplerThiefChance").floatValue = 1f;
                serialized.FindProperty("m_maxActiveGrapplerThieves").intValue = 10;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.GrapplerThiefChance, Is.Zero);
                Assert.That(copy.MaxActiveGrapplerThieves, Is.Zero);
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test]
        public void TargetIsClosestToCouchAndIgnoresDownedAndInactivePlayers()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            GameObject nearCouch = Object.Instantiate(source), nearEnemy = Object.Instantiate(source);
            GameObject thief = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab"));
            GameObject rogue = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Enemies/RogueCar.prefab"));
            try
            {
                var a = nearCouch.GetComponent<PlayerHealth>();
                var b = nearEnemy.GetComponent<PlayerHealth>();
                foreach (PlayerHealth player in new[] { a, b })
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    typeof(PlayerHealth).GetMethod("Awake", flags).Invoke(player, null);
                    var network = player.GetComponent<FishNet.Object.NetworkObject>();
                    typeof(FishNet.Object.NetworkObject).GetProperty("ObjectId").SetValue(network, player == a ? 110 : 111);
                    typeof(FishNet.Object.NetworkObject).GetProperty("IsDeinitializing", flags).SetValue(network, false);
                    typeof(FishNet.Object.NetworkObject).GetProperty("Owner").SetValue(network, new FishNet.Connection.NetworkConnection());
                    ((FishNet.Object.Synchronizing.SyncVar<float>)typeof(PlayerHealth).GetField("m_currentHealth", flags)
                        .GetValue(player)).Value = 100f;
                    ((FishNet.Object.Synchronizing.SyncVar<int>)typeof(PlayerHealth).GetField("m_aiTeammateIndex", flags)
                        .GetValue(player)).Value = 0;
                }
                nearCouch.transform.position = Vector3.right;
                nearEnemy.transform.position = Vector3.right * 20f;
                thief.transform.position = rogue.transform.position = Vector3.zero;
                void AssertTargets(PlayerHealth expected)
                {
                    Assert.That(GrapplerThief.FindTarget(Vector3.zero), Is.EqualTo(expected), "Grappler");
                    Assert.That(EnemyWeapon.FindClosestCouchEnemy(Vector3.zero), Is.EqualTo(expected), "Rocket");
                    Assert.That(typeof(ThiefNavigator).GetMethod("GetClosestLivingTarget", Private)
                        .Invoke(thief.GetComponent<ThiefNavigator>(), null), Is.EqualTo(expected), "Cached thief target");
                    typeof(RogueCar).GetMethod("RefreshTarget", Private).Invoke(rogue.GetComponent<RogueCar>(), null);
                    Assert.That(typeof(RogueCar).GetField("m_target", Private).GetValue(rogue.GetComponent<RogueCar>()),
                        Is.EqualTo(expected), "Rogue car");
                }
                AssertTargets(a);
                nearEnemy.transform.position = Vector3.right * 0.5f;
                // Expire only the distance cache; invalidation below must bypass its timer.
                typeof(ThiefNavigator).GetField("m_nextPlayerSearchTime", Private).SetValue(thief.GetComponent<ThiefNavigator>(), 0f);
                AssertTargets(b);
                const BindingFlags both = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var knockedDown = (FishNet.Object.Synchronizing.SyncVar<bool>)typeof(PlayerHealth).GetField("m_knockedDown", both).GetValue(b);
                var health = (FishNet.Object.Synchronizing.SyncVar<float>)typeof(PlayerHealth).GetField("m_currentHealth", both).GetValue(b);
                knockedDown.Value = true;
                AssertTargets(a);
                health.Value = 0f;
                AssertTargets(a);
                knockedDown.Value = false;
                AssertTargets(a); // Dead players remain excluded without the knockdown flag.
                nearCouch.SetActive(false);
                AssertTargets(null);
                health.Value = 100f;
                AssertTargets(b); // Recovery makes the player available to normal acquisition again.
            }
            finally
            {
                Object.DestroyImmediate(nearCouch); Object.DestroyImmediate(nearEnemy);
                Object.DestroyImmediate(thief); Object.DestroyImmediate(rogue);
            }
        }

        [Test]
        public void CoverAndNonPlayersBlockGrapple()
        {
            GameObject enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            GameObject player = new GameObject("Grapple test player");
            GameObject cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                player.AddComponent<FishNet.Object.NetworkObject>();
                var health = player.AddComponent<PlayerHealth>();
                var collider = player.AddComponent<CapsuleCollider>();
                collider.center = Vector3.up * 0.9f;
                collider.height = 1.8f;
                player.transform.position = Vector3.forward * 8f;
                cover.transform.position = new Vector3(0f, 1f, 4f);
                cover.transform.localScale = new Vector3(5f, 3f, 1f);
                Physics.SyncTransforms();
                var grapple = enemy.GetComponent<GrapplerThief>();
                Assert.That(grapple.HasLineOfSight(health), Is.False);
                cover.SetActive(false);
                Physics.SyncTransforms();
                Assert.That(grapple.HasLineOfSight(health), Is.True);
                player.transform.position = Vector3.forward * 20f;
                Physics.SyncTransforms();
                Assert.That(grapple.HasLineOfSight(health), Is.False);
            }
            finally { Object.DestroyImmediate(enemy); Object.DestroyImmediate(player); Object.DestroyImmediate(cover); }
        }

        [Test]
        public void EnemyLaunchReplacesOpposingMomentumWithoutChangingPlayerFlick()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
            try
            {
                var motor = player.GetComponent<ThirdPersonPlayerController>();
                typeof(ThirdPersonPlayerController).GetField("m_externalVelocity", Private).SetValue(motor, Vector3.left * 30f);
                typeof(ThirdPersonPlayerController).GetMethod("ApplyEnemyGrappleThrow", Private)
                    .Invoke(motor, new object[] { new Vector3(12f, 5f, 0f), 3f, 1.5f });
                Assert.That((Vector3)typeof(ThirdPersonPlayerController).GetField("m_externalVelocity", Private).GetValue(motor),
                    Is.EqualTo(Vector3.right * 12f));
                Assert.That((float)typeof(ThirdPersonPlayerController).GetField("m_verticalVelocity", Private).GetValue(motor), Is.EqualTo(5f));
            }
            finally { Object.DestroyImmediate(player); }
        }
    }
}
