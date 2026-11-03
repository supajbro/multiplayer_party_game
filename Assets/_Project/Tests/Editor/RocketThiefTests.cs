using System;
using System.IO;
using System.Reflection;
using CouchGuys.Editor;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using FishNet.Managing.Object;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class RocketThiefTests
    {
        [TestCase("Easy")][TestCase("Medium")][TestCase("Hard")]
        public void RocketSpawnsAreHardOnlyEvenWithMisconfiguredReferences(string name)
        {
            var source=AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{name}Delivery.asset");
            var copy=Object.Instantiate(source);
            try
            {
                var so=new SerializedObject(copy);
                so.FindProperty("m_rocketThiefPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(RocketThiefAuthoring.PrefabPath).GetComponent<NetworkObject>();
                so.FindProperty("m_rocketThiefChance").floatValue=1;
                so.FindProperty("m_maxActiveRocketThieves").intValue=2;so.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.ShouldReplaceWithRocketThief(0,0),Is.EqualTo(name=="Hard"));
                Assert.That(copy.ShouldReplaceWithRocketThief(2,0),Is.False,"Active cap must be enforced.");
                if(name!="Hard")Assert.That(copy.RocketThiefPrefab,Is.Null);
            }
            finally{Object.DestroyImmediate(copy);}
        }

        [Test]
        public void PrefabUsesExistingRocketDamageRagdollAndNetworkSystems()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(RocketThiefAuthoring.PrefabPath);
            Assert.That(prefab,Is.Not.Null);
            Assert.That(prefab.GetComponent<EnemyWeapon>().IsRocketLauncher,Is.True);
            Assert.That(prefab.GetComponent<ThiefHealth>(),Is.Not.Null);
            Assert.That(prefab.GetComponent<ThiefNavigator>(),Is.Not.Null);
            var data=new SerializedObject(prefab.GetComponent<EnemyWeapon>());
            Assert.That(data.FindProperty("m_muzzle").objectReferenceValue,Is.Not.Null);
            Assert.That(data.FindProperty("m_rocketWeapon").FindPropertyRelative("m_bulletPrefab").objectReferenceValue,
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Projectile)));
            var registered=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            Assert.That(registered.Prefabs,Does.Contain(prefab.GetComponent<NetworkObject>()));
            Assert.That((int)EnemyWeaponType.RocketLauncher,Is.EqualTo((int)PlayerWeaponType.RocketLauncher),"Drops/audio must use the existing launcher index.");
        }

        [MenuItem("Couch Guys/Thieves/Validate Rocket Launcher Variant %#&l")]
        public static void RunChecks()
        {
            RunValidation();
        }

        [Test]
        public void RocketIgnoresShooterButCanCollideWithOtherThieves()
        {
            var shooter=new GameObject("Shooter collision test");
            var other=new GameObject("Friendly fire collision test");
            GameObject projectile=null;
            try
            {
                var shooterCollider=shooter.AddComponent<CapsuleCollider>();
                var shooterHealth=shooter.AddComponent<ThiefHealth>();
                var otherCollider=other.AddComponent<CapsuleCollider>();
                var otherHealth=other.AddComponent<ThiefHealth>();
                projectile=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Projectile));
                var rocket=projectile.GetComponent<RocketProjectile>();
                rocket.InitializeEnemyServer(shooterHealth,WeaponDefinition.CreateDefaults()[4],~0);
                var collision=typeof(RocketProjectile).GetMethod("IsCollisionTarget",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.That((bool)collision.Invoke(rocket,new object[]{shooterCollider}),Is.False);
                Assert.That((bool)collision.Invoke(rocket,new object[]{otherCollider}),Is.True);
                Assert.That(RocketProjectile.FindDamageTarget(otherCollider),Is.EqualTo(otherHealth));
                Assert.That(RocketProjectile.FindDamageTarget(shooterCollider),Is.EqualTo(shooterHealth),"Self-collision exemption must not grant splash immunity.");
            }
            finally{if(projectile!=null)Object.DestroyImmediate(projectile);Object.DestroyImmediate(shooter);Object.DestroyImmediate(other);}
        }

        [Test]
        public void NormalSpawnPoolCannotBypassHardOnlyReplacementRule()
        {
            var root=new GameObject("Rocket spawn pool test");
            try
            {
                var spawner=root.AddComponent<EnemySpawnManager>();
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(EnemySpawnManager).GetField("m_deliveryManager",flags).SetValue(spawner,null);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(RocketThiefAuthoring.PrefabPath).GetComponent<NetworkObject>();
                typeof(EnemySpawnManager).GetField("m_enemyPrefabs",flags).SetValue(spawner,new[]{prefab});
                Assert.That(typeof(EnemySpawnManager).GetMethod("SelectEnemyPrefab",flags).Invoke(spawner,null),Is.Null);
            }
            finally{Object.DestroyImmediate(root);}
        }

        private static void RunValidation()
        {
            Directory.CreateDirectory("Temp/RocketThief");
            try
            {
                var tests=new RocketThiefTests();tests.RocketSpawnsAreHardOnlyEvenWithMisconfiguredReferences("Easy");
                tests.RocketSpawnsAreHardOnlyEvenWithMisconfiguredReferences("Medium");tests.RocketSpawnsAreHardOnlyEvenWithMisconfiguredReferences("Hard");
                tests.PrefabUsesExistingRocketDamageRagdollAndNetworkSystems();
                tests.RocketIgnoresShooterButCanCollideWithOtherThieves();
                tests.NormalSpawnPoolCannotBypassHardOnlyReplacementRule();
                File.WriteAllText("Temp/RocketThief/validation.txt","PASS: saved launcher thief and registered rocket prefab; Hard-only spawn gates, misconfiguration protection, active cap and normal-pool bypass prevention; shooter collision exclusion without splash immunity; other-thief collision/damage eligibility; existing weapon drops.\n");
                Debug.Log("Rocket thief asset and spawn checks PASS.");
            }
            catch(Exception error){File.WriteAllText("Temp/RocketThief/validation.txt","FAIL: "+error);throw;}
        }
    }
}
