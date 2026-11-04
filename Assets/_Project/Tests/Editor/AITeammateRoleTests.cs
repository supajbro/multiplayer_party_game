using System.Linq;
using CouchGuys.Player;
using FishNet.Managing.Object;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Tests.Editor
{
    public sealed class AITeammateRoleTests
    {
        [TestCase(AITeammateRole.HumanSupport)]
        [TestCase(AITeammateRole.Combat)]
        [TestCase(AITeammateRole.Carrier)]
        [TestCase(AITeammateRole.Support)]
        public void SavedRolePrefabHasCorrectConfigurationAndNetworkRegistration(AITeammateRole role)
        {
            var settings = AssetDatabase.LoadAssetAtPath<AITeammateRoleSettings>(
                $"Assets/_Project/Settings/AI/AI_{role}.asset");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/AI/AI_{role}.prefab");
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.Role, Is.EqualTo(role));
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<DebugCouchBotController>().RoleSettings, Is.SameAs(settings));
            Assert.That(settings.CouchNavigationSettings, Is.Not.Null);
            var registry = new SerializedObject(AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(
                "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset"));
            var entries = registry.FindProperty("_prefabs");
            Assert.That(Enumerable.Range(0, entries.arraySize).Count(i =>
                entries.GetArrayElementAtIndex(i).objectReferenceValue == prefab.GetComponent<NetworkObject>()), Is.EqualTo(1));
        }

        [Test]
        public void DepotEntranceHasSavedBidirectionalNavigationLink()
        {
            var depot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/World/depot_02.prefab");
            var entrance = depot.transform.Find("Teammate Entrance Navigation Link");
            Assert.That(entrance, Is.Not.Null);
            var link = entrance.GetComponent<Unity.AI.Navigation.NavMeshLink>();
            Assert.That(link.bidirectional, Is.True);
            Assert.That(link.width, Is.GreaterThanOrEqualTo(4f));
        }

        [Test]
        public void HumanPlayerNavigationRemainsDisabledAndSupportHasHighestRevivePriority()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            Assert.That(player.GetComponent<NavMeshAgent>().enabled, Is.False);
            var support = AssetDatabase.LoadAssetAtPath<AITeammateRoleSettings>("Assets/_Project/Settings/AI/AI_Support.asset");
            var human = AssetDatabase.LoadAssetAtPath<AITeammateRoleSettings>("Assets/_Project/Settings/AI/AI_HumanSupport.asset");
            Assert.That(support.RevivalPriority, Is.GreaterThan(human.RevivalPriority));
            Assert.That(human.RevivalPriority, Is.GreaterThan(0));
        }
    }
}
