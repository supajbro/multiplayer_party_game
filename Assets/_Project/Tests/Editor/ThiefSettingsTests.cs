using System;
using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Tests.Editor
{
    public sealed class ThiefSettingsTests
    {
        [TestCase(0f, true, false)]
        [TestCase(100f, true, true)]
        [TestCase(100f, false, false)]
        public void RetaliationRollTargetsOnlyLivingAttackers(float chance, bool alive, bool retaliates)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = new GameObject("Retaliation test");
            root.SetActive(false);
            try
            {
                var thief = new GameObject("Thief");
                thief.transform.SetParent(root.transform);
                var navigator = thief.AddComponent<ThiefNavigator>();
                thief.GetComponent<NavMeshAgent>().enabled = false;
                var attackerObject = new GameObject("Attacker");
                attackerObject.transform.SetParent(root.transform);
                var attacker = attackerObject.AddComponent<PlayerHealth>();
                ((SyncVar<float>)typeof(PlayerHealth).GetField("m_currentHealth", flags)
                    .GetValue(attacker)).Value = alive ? 100f : 0f;
                typeof(ThiefNavigator).GetField("m_retaliationChance", flags).SetValue(navigator, chance);

                typeof(ThiefNavigator).GetMethod("ReactToAttackServer", flags)
                    .Invoke(navigator, new object[] { attacker });

                Assert.That(typeof(ThiefNavigator).GetField("m_retaliationTarget", flags).GetValue(navigator),
                    Is.EqualTo(retaliates ? attacker : null));
                if (retaliates)
                {
                    Assert.That(typeof(ThiefNavigator).GetField("m_target", flags).GetValue(navigator),
                        Is.SameAs(attacker));
                    Assert.That((float)typeof(ThiefNavigator).GetField("m_retaliationUntil", flags)
                        .GetValue(navigator), Is.EqualTo(Time.time + 5f).Within(0.01f));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(1, true)]
        [TestCase(0, false)]
        public void UnavailableCouchCombatPreservesWeaponReactionDelay(int memberIndex, bool fighting)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var root = new GameObject("Thief combat transition test");
            root.SetActive(false);
            try
            {
                var thief = new GameObject("Thief");
                thief.transform.SetParent(root.transform);
                var weapon = thief.AddComponent<EnemyWeapon>();
                var navigator = thief.AddComponent<ThiefNavigator>();
                thief.GetComponent<NavMeshAgent>().enabled = false;
                var playerObject = new GameObject("Player");
                playerObject.transform.SetParent(root.transform);
                var player = playerObject.AddComponent<PlayerHealth>();
                ((SyncVar<float>)typeof(PlayerHealth).GetField("m_currentHealth", flags)
                    .GetValue(player)).Value = 100f;
                var couchObject = new GameObject("Couch without available grips");
                couchObject.transform.SetParent(root.transform);
                var couch = couchObject.AddComponent<CouchCarryController>();

                // Dynamically assembled edit-mode objects need the references normally
                // saved in network prefabs before invoking their gameplay methods.
                foreach (var behaviour in root.GetComponentsInChildren<FishNet.Object.NetworkBehaviour>(true))
                {
                    var network = behaviour.GetComponent<FishNet.Object.NetworkObject>();
                    if (network == null) network = behaviour.gameObject.AddComponent<FishNet.Object.NetworkObject>();
                    var data = new SerializedObject(behaviour);
                    data.FindProperty("_networkObjectCache").objectReferenceValue = network;
                    data.FindProperty("_addedNetworkObject").objectReferenceValue = network;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }

                typeof(ThiefNavigator).GetField("m_weapon", flags).SetValue(navigator, weapon);
                typeof(ThiefNavigator).GetField("m_memberIndex", flags).SetValue(navigator, memberIndex);
                typeof(ThiefNavigator).GetField("m_closestPlayer", flags).SetValue(navigator, player);
                typeof(ThiefNavigator).GetField("m_nextPlayerSearchTime", flags)
                    .SetValue(navigator, float.PositiveInfinity);
                typeof(EnemyWeapon).GetField("m_target", flags).SetValue(weapon, player);
                // An elapsed reaction timer must stay elapsed on subsequent combat updates.
                typeof(EnemyWeapon).GetField("m_reactionEndsAt", flags).SetValue(weapon, -1f);
                MethodInfo update = typeof(ThiefNavigator).GetMethod("UpdateStealObjective", flags);
                for (int frame = 0; frame < 3; frame++)
                    update.Invoke(navigator, new object[] { couch });

                Assert.That(typeof(EnemyWeapon).GetField("m_target", flags).GetValue(weapon),
                    Is.EqualTo(fighting ? player : null));
                if (fighting)
                    Assert.That(typeof(EnemyWeapon).GetField("m_reactionEndsAt", flags).GetValue(weapon),
                        Is.EqualTo(-1f), "Combat must not restart the firing reaction delay each frame.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

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
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
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
