using System;
using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class InteractionPromptTests
    {
        private readonly List<GameObject> m_objects = new();
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [TearDown]
        public void TearDown()
        {
            foreach (var instance in m_objects)
                if (instance != null) Object.DestroyImmediate(instance);
            m_objects.Clear();
        }

        [TestCase(typeof(CouchCarryPoint), "ActivePoints")]
        [TestCase(typeof(SledGrabPoint), "ActivePoints")]
        [TestCase(typeof(WeaponPickup), "ActivePickups")]
        [TestCase(typeof(DeliveryNPC), "ActiveNpcs")]
        [TestCase(typeof(DepotController), "ActiveDepots")]
        [TestCase(typeof(SledController), "ActiveSleds")]
        public void TargetsTrackDisableReuseAndDestruction(Type type, string registryName)
        {
            var instance = Create("Interaction registry test");
            var target = (Behaviour)instance.AddComponent(type);
            object registry = type.GetField(registryName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
            var contains = registry.GetType().GetMethod("Contains");
            bool Registered() => (bool)contains.Invoke(registry, new object[] { target });

            Assert.That(Registered(), Is.True);
            target.enabled = false;
            Assert.That(Registered(), Is.False);
            target.enabled = true;
            Assert.That(Registered(), Is.True);
            instance.SetActive(false);
            Assert.That(Registered(), Is.False);
            instance.SetActive(true);
            Assert.That(Registered(), Is.True);
            Object.DestroyImmediate(instance);
            Assert.That(Registered(), Is.False);
        }

        [Test]
        public void PromptRefreshIsThrottledButForcedRefreshUsesCurrentTargets()
        {
            // Keep the player inactive so this test doesn't start input or networking.
            var playerObject = Create("Prompt test player");
            playerObject.SetActive(false);
            playerObject.transform.position = new Vector3(100000f, 0f, 100000f);
            var carrier = playerObject.AddComponent<PlayerCouchCarrier>();
            var npcObject = Create("Prompt test dispatcher");
            npcObject.transform.position = playerObject.transform.position;
            var npc = npcObject.AddComponent<DeliveryNPC>();
            var refresh = typeof(PlayerCouchCarrier).GetMethod("UpdateSledInteractionPrompt", Private);
            var nextRefresh = typeof(PlayerCouchCarrier).GetField("m_nextInteractionPromptRefresh", Private);

            refresh.Invoke(carrier, new object[] { true });
            string nearbyPrompt = carrier.SledInteractionPrompt;
            Assert.That(nearbyPrompt, Does.Contain(npc.DisplayName));

            npcObject.transform.position += Vector3.right * 20f;
            nextRefresh.SetValue(carrier, Time.unscaledTime + 60f);
            refresh.Invoke(carrier, new object[] { false });
            Assert.That(carrier.SledInteractionPrompt, Is.EqualTo(nearbyPrompt));
            refresh.Invoke(carrier, new object[] { true });
            Assert.That(carrier.SledInteractionPrompt, Is.Empty);

            npcObject.transform.position = playerObject.transform.position;
            nextRefresh.SetValue(carrier, float.NegativeInfinity);
            refresh.Invoke(carrier, new object[] { false });
            Assert.That(carrier.SledInteractionPrompt, Is.EqualTo(nearbyPrompt));
            npcObject.SetActive(false);
            refresh.Invoke(carrier, new object[] { true });
            Assert.That(carrier.SledInteractionPrompt, Is.Empty);
        }

        private GameObject Create(string name)
        {
            var instance = new GameObject(name);
            m_objects.Add(instance);
            return instance;
        }
    }
}
