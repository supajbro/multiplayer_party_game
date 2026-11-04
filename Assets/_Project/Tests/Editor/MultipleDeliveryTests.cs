using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.ProceduralGeneration;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class MultipleDeliveryTests
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private GameObject m_host;
        private DeliveryManager m_manager;

        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);

        [SetUp]
        public void SetUp()
        {
            m_host = new GameObject("MultipleDeliveryTest");
            m_host.SetActive(false);
            m_manager = m_host.AddComponent<DeliveryManager>();
            Set(m_manager, "m_generator", m_host.GetComponent<NeighbourhoodGenerator>());
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_host);

        [Test]
        public void ExhaustedDifficultyCannotReuseReservedHousesOrBlockOtherDifficulties()
        {
            Set(m_manager, "m_candidatesReady", true);
            var candidates = Get<List<int>[]>(m_manager, "m_tierCandidates");
            candidates[0].AddRange(new[] { 0, 1 });
            candidates[1].Add(2);
            candidates[2].Add(3);
            var reservations = Get<HashSet<int>>(m_manager, "m_usedDestinationIndices");
            reservations.Add(0);
            Assert.That(Call(m_manager, "SelectDestinationIndex", 0), Is.EqualTo(1));
            reservations.Add(1);
            Assert.That(Call(m_manager, "SelectDestinationIndex", 0), Is.EqualTo(-1));
            Assert.That(Call(m_manager, "SelectDestinationIndex", 1), Is.EqualTo(2));
            Assert.That(Call(m_manager, "SelectDestinationIndex", 2), Is.EqualTo(3));
            reservations.Remove(0);
            Assert.That(Call(m_manager, "SelectDestinationIndex", 0), Is.EqualTo(0));
        }

        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        public void SnapshotReconstructsFiveDistinctOrderedTimersWithoutDuplicates(int width, int height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<PlayerWeaponHudView>("Assets/_Project/Prefabs/UI/PlayerWeaponHud.prefab");
            Assert.That(prefab, Is.Not.Null);
            var view = Object.Instantiate(prefab);
            try
            {
                view.gameObject.SetActive(false);
                view.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                ((RectTransform)view.transform).sizeDelta = new Vector2(width, height);
                var hud = m_host.AddComponent<PlayerWeaponHud>();
                Set(hud, "m_view", view);
                Set(hud, "m_delivery", m_manager);
                var deliveries = Get<SyncList<DeliveryInstance>>(m_manager, "m_deliveries");
                int[] tiers = { 2, 0, 1, 0, 2 };
                for (int i = 0; i < tiers.Length; i++)
                    deliveries.Add(new DeliveryInstance { Id = i + 1, TierIndex = tiers[i], CouchObjectId = -1 });
                Call(hud, "SyncDeliveryTimers");
                Call(hud, "LayoutDeliveryTimers");
                var timers = Get<Dictionary<int, DeliveryTimerHudView>>(hud, "m_deliveryTimers");
                var original = timers.ToDictionary(pair => pair.Key, pair => pair.Value);
                Assert.That(timers.Count, Is.EqualTo(5));
                Assert.That(view.m_deliveryTimerTemplate.gameObject.activeSelf, Is.False);
                Assert.That(view.GetComponentsInChildren<DeliveryTimerHudView>(true).Length, Is.EqualTo(6));
                int[] order = { 2, 4, 3, 1, 5 };
                RectTransform previous = null;
                foreach (int id in order)
                {
                    var rect = (RectTransform)timers[id].transform;
                    Assert.That(timers[id].DeliveryId, Is.EqualTo(id));
                    if (previous == null)
                        Assert.That(rect.anchoredPosition, Is.EqualTo(((RectTransform)view.m_deliveryTimerTemplate.transform).anchoredPosition));
                    else
                        Assert.That(rect.anchoredPosition.y + rect.rect.height * rect.localScale.y,
                            Is.LessThanOrEqualTo(previous.anchoredPosition.y));
                    Assert.That(height + rect.anchoredPosition.y - rect.rect.height * rect.localScale.y,
                        Is.GreaterThanOrEqualTo(view.m_deliveryTimerBottomMargin));
                    previous = rect;
                }
                Call(hud, "SyncDeliveryTimers");
                Assert.That(view.GetComponentsInChildren<DeliveryTimerHudView>(true).Length, Is.EqualTo(6));
                foreach (var entry in original) Assert.That(timers[entry.Key], Is.SameAs(entry.Value));
            }
            finally { Object.DestroyImmediate(view.gameObject); }
        }
    }
}
