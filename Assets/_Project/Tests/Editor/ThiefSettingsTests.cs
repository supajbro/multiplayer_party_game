using System;
using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Tests.Editor
{
    public sealed class ThiefSettingsTests
    {
        [TestCase(EnemyWeaponType.Pistol)]
        [TestCase(EnemyWeaponType.AssaultRifle)]
        [TestCase(EnemyWeaponType.Shotgun)]
        public void ThiefComponentsShareTheirArchetypeSettings(EnemyWeaponType type)
        {
            var settings = AssetDatabase.LoadAssetAtPath<ThiefSettings>(
                $"Assets/_Project/Settings/Thieves/{type}ThiefSettings.asset");
            Assert.That(settings, Is.Not.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/_Project/Prefabs/Enemies/Thief_{type}.prefab");
            Assert.That(prefab, Is.Not.Null);
            Type[] consumers = { typeof(ThiefNavigator), typeof(EnemyWeapon), typeof(ThiefHealth),
                typeof(ThiefAudioEmitter), typeof(ThiefAnimationDriver) };
            foreach (Type consumer in consumers)
            {
                Component component = prefab.GetComponent(consumer);
                Assert.That(component, Is.Not.Null, consumer.Name);
                Assert.That(new SerializedObject(component).FindProperty("m_settings").objectReferenceValue,
                    Is.SameAs(settings), consumer.Name);
            }
            Assert.That(prefab.GetComponent<EnemyWeapon>().WeaponType, Is.EqualTo(type));
            Assert.That(settings.Audio.FootstepLoop, Is.Not.Null);
            Assert.That(settings.Audio.AlertClip, Is.Not.Null);
            Assert.That(settings.Alerts.AlertResetDistance, Is.GreaterThan(settings.Alerts.AlertDistance));
        }

        [Test]
        public void NavigatorAppliesAssetMovementInsteadOfAgentPrefabDefaults()
        {
            var settings = ScriptableObject.CreateInstance<ThiefSettings>();
            var root = new GameObject("Thief settings test");
            root.SetActive(false);
            try
            {
                NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
                agent.speed = 1f;
                ThiefNavigator navigator = root.AddComponent<ThiefNavigator>();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ThiefNavigator).GetField("m_settings", flags).SetValue(navigator, settings);
                settings.Movement.Speed = 6.5f;
                settings.Movement.Acceleration = 22f;
                settings.Movement.AngularSpeed = 420f;
                settings.Movement.StoppingDistance = 2.5f;
                settings.Movement.AutoBraking = false;

                typeof(ThiefNavigator).GetMethod("Awake", flags).Invoke(navigator, null);

                Assert.That(agent.speed, Is.EqualTo(6.5f));
                Assert.That(agent.acceleration, Is.EqualTo(22f));
                Assert.That(agent.angularSpeed, Is.EqualTo(420f));
                Assert.That(agent.stoppingDistance, Is.EqualTo(2.5f));
                Assert.That(agent.autoBraking, Is.False);
                Assert.That(typeof(ThiefNavigator).GetField("m_normalNavigationSpeed", flags)
                    .GetValue(navigator), Is.EqualTo(6.5f), "Couch carrying must restore the configured speed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }
    }
}
