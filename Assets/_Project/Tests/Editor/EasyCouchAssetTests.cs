using System.Linq;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class EasyCouchAssetTests
    {
        [Test]
        public void EasyVisualsHaveValidMaterialsLodsAndCollisionProfiles()
        {
            CouchGuys.Editor.EasyCouchAuthoring.Validate();
        }

        [Test]
        public void SharedGameplayAndDifficultyPoolsRetainTheirConfiguredReferences()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Couch.prefab");
            var data = new SerializedObject(prefab.GetComponent<CouchCarryController>());
            Assert.That(data.FindProperty("m_settings").objectReferenceValue, Is.Not.Null);
            Assert.That(data.FindProperty("m_modelPrefabs").arraySize, Is.EqualTo(3));
            Assert.That(data.FindProperty("m_carryPoints").arraySize, Is.EqualTo(4));
            var tier = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/EasyDelivery.asset");
            Assert.That(data.FindProperty("m_easyDeliveryVisuals").objectReferenceValue, Is.SameAs(tier));
            Assert.That(tier.CouchVisualCount, Is.EqualTo(10));
            Assert.That(Enumerable.Range(0,10).Select(tier.GetCouchVisual).Distinct().Count(), Is.EqualTo(10));
            foreach (string name in new[] { "Medium", "Hard" })
            {
                var other = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{name}Delivery.asset");
                Assert.That(other.CouchVisualCount, Is.Zero);
            }
        }
    }
}
