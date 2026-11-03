using System.Linq;
using System.Reflection;
using CouchGuys.Editor;
using CouchGuys.Gameplay.Weapons;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class RocketLauncherTests
    {
        [TestCase(0,80)] [TestCase(2.5f,40)] [TestCase(5,0)] [TestCase(7,0)]
        public void SplashFallsOffAndStopsAtRadius(float distance,float expected) =>
            Assert.That(RocketProjectile.DamageAtDistance(80,5,distance),Is.EqualTo(expected).Within(.001f));

        [Test]
        public void SavedPlayerUsesConfiguredRocketAndExistingAmmoArchitecture()
        {
            var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var so=new SerializedObject(player.GetComponent<PlayerWeaponController>());
            var w=so.FindProperty("m_weapons").GetArrayElementAtIndex(4);
            foreach(var pair in new[]{("m_damagePerPellet",60f),("m_explosionDamage",80f),("m_explosionRadius",5f),
                ("m_bulletSpeed",25f),("m_bulletLifetime",6f),("m_reloadDuration",2.5f),("m_fireInterval",1.5f)})
                Assert.That(w.FindPropertyRelative(pair.Item1).floatValue,Is.EqualTo(pair.Item2),pair.Item1);
            Assert.That(w.FindPropertyRelative("m_magazineCapacity").intValue,Is.EqualTo(1));
            Assert.That(w.FindPropertyRelative("m_startingReserve").intValue,Is.EqualTo(5));
            Assert.That(w.FindPropertyRelative("m_reticleSettings").objectReferenceValue,Is.Not.Null);
            Assert.That(w.FindPropertyRelative("m_bulletPrefab").objectReferenceValue,Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Projectile)));
        }

        [Test]
        public void SavedPrefabsAreServerOwnedAndEffectsAreLocalAndBounded()
        {
            var spawnables=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            foreach(string path in new[]{RocketLauncherAuthoring.Projectile,RocketLauncherAuthoring.Pickup})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(spawnables.Prefabs.Contains(prefab.GetComponent<NetworkObject>()),Is.True);
                Assert.That(new SerializedObject(prefab.GetComponent<NetworkTransform>()).FindProperty("_clientAuthoritative").boolValue,Is.False);
            }
            var explosion=AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Explosion);
            Assert.That(explosion.GetComponentsInChildren<NetworkObject>(true),Is.Empty);
            var particles=explosion.GetComponentsInChildren<ParticleSystem>();
            Assert.That(particles.Length,Is.EqualTo(9));
            Assert.That(particles.Sum(p=>p.main.maxParticles),Is.LessThan(100));
            foreach(var ps in particles)
            {
                Assert.That(ps.main.loop,Is.False);
                Assert.That(ps.main.startDelay.constantMax+ps.main.startLifetime.constantMax,Is.LessThanOrEqualTo(1));
                Assert.That(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.isSupported,Is.True);
                Assert.That(ShaderUtil.ShaderHasError(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader),Is.False);
            }
        }

        [Test]
        public void SweepFindsThinWallBetweenFramesAndClientSimulationDoesNothing()
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Projectile);
            var rocket=Object.Instantiate(source,new Vector3(100,10,100),Quaternion.identity).GetComponent<RocketProjectile>();
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position=rocket.transform.position+Vector3.forward*.6f;
                wall.transform.localScale=new Vector3(2,2,.01f);
                Physics.SyncTransforms();
                rocket.InitializeServer(null,null,WeaponDefinition.CreateDefaults()[4],~0);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                object[] args={rocket.transform.position,Vector3.forward*1.2f,default(RaycastHit)};
                Assert.That((bool)typeof(RocketProjectile).GetMethod("Sweep",flags).Invoke(rocket,args),Is.True);
                Assert.That(((RaycastHit)args[2]).collider,Is.EqualTo(wall.GetComponent<Collider>()));
                Vector3 position=rocket.transform.position;
                typeof(RocketProjectile).GetMethod("FixedUpdate",flags).Invoke(rocket,null);
                Assert.That(rocket.transform.position,Is.EqualTo(position),"Uninitialized clients must not simulate damage or movement.");
            }
            finally{Object.DestroyImmediate(rocket.gameObject);Object.DestroyImmediate(wall);}
        }
    }
}
