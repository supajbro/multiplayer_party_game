using System;
using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Gameplay.Vehicles;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class MountedCouchTheftTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<Object> m_objects = new();
        private T Create<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            go.SetActive(false);
            m_objects.Add(go);
            return go.GetComponent<T>() ?? go.AddComponent<T>();
        }
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Hidden).SetValue(owner, value);
        private static SyncVar<NetworkObject> Sync(object owner, string field) => (SyncVar<NetworkObject>)owner.GetType().GetField(field, Hidden).GetValue(owner);
        private static object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Hidden).Invoke(owner, args);
        [TearDown] public void Cleanup()
        {
            for (int i = m_objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(m_objects[i]);
            m_objects.Clear();
        }

        [Test]
        public void MountedReachExpandsAndLooseCouchImmediatelyRestoresOriginalRange()
        {
            var settings = ScriptableObject.CreateInstance<GetawayTruckSettings>(); m_objects.Add(settings);
            var couch = Create<CouchCarryController>();
            var sled = Create<SledController>();
            var board = Create<MagneticHoverboardController>();
            Assert.That(Call(settings, "GetLoadingDistance", couch), Is.EqualTo(settings.LoadingDistance));
            Sync(couch, "m_mountedSled").Value = sled.GetComponent<NetworkObject>();
            Assert.That(Call(settings, "GetLoadingDistance", couch), Is.EqualTo(settings.LoadingDistance + settings.SledLoadingDistanceBonus));
            Sync(couch, "m_mountedSled").Value = null;
            Sync(couch, "m_mountedHoverboard").Value = board.GetComponent<NetworkObject>();
            Assert.That(Call(settings, "GetLoadingDistance", couch), Is.EqualTo(settings.LoadingDistance + settings.HoverboardLoadingDistanceBonus));
            Sync(couch, "m_mountedHoverboard").Value = null;
            Assert.That(Call(settings, "GetLoadingDistance", couch), Is.EqualTo(settings.LoadingDistance));
        }

        [Test]
        public void SledHandoffAcceptsTheGapThatRejectsAnOrdinaryCouch()
        {
            var settings = ScriptableObject.CreateInstance<GetawayTruckSettings>(); m_objects.Add(settings);
            var controller = Create<GetawayTruckController>();
            var truck = controller.GetComponent<TruckController>();
            var entrance = Create<Transform>(); entrance.position = new Vector3(30000f, 1.6f, 30000f);
            Set(truck, "m_cargoLoadingPoint", entrance);
            Set(controller, "m_truck", truck); Set(controller, "m_settings", settings);
            var plannerType = typeof(TruckController).Assembly.GetType("CouchGuys.Gameplay.Vehicles.CouchClearancePath");
            var planner = Activator.CreateInstance(plannerType, true);
            plannerType.GetProperty("LoadingTruck", Hidden).SetValue(planner, truck.transform);
            Set(controller, "m_carryPlanner", planner);
            var couch = Create<CouchCarryController>();
            Set(couch, "m_cargoBounds", new Bounds(Vector3.up * .45f, new Vector3(2.1f, .9f, .9f)));
            couch.transform.position = entrance.position + Vector3.back * 5f;
            Set(controller, "m_couch", couch);
            Assert.That(Call(controller, "CouchAtLoadingEntrance", couch, couch.transform.position), Is.False);
            var sled = Create<SledController>();
            Sync(couch, "m_mountedSled").Value = sled.GetComponent<NetworkObject>();
            Assert.That(Call(controller, "CouchAtLoadingEntrance", couch, couch.transform.position), Is.True);
            Sync(couch, "m_mountedSled").Value = null;
            Assert.That(Call(controller, "CouchAtLoadingEntrance", couch, couch.transform.position), Is.False);
        }

        [Test]
        public void OnlyThiefDriversAuthorizeHoverboardCargoTheft()
        {
            var board = Create<MagneticHoverboardController>();
            Assert.That(board.HasThiefDriver, Is.False);
            var player = Create<NetworkObject>();
            Sync(board, "m_driver").Value = player;
            Assert.That(board.HasThiefDriver, Is.False);
            var thief = Create<ThiefNavigator>();
            Sync(board, "m_driver").Value = thief.GetComponent<NetworkObject>();
            Assert.That(board.HasThiefDriver, Is.True);
            Sync(board, "m_driver").Value = null;
            Assert.That(board.HasThiefDriver, Is.False);
        }

        [Test]
        public void RidingThiefCannotShootOrGrabCouchesAndLateJoinPoseRestores()
        {
            var board = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Vehicles/MagneticHoverboard.prefab"));
            m_objects.Add(board);
            var thief = Create<ThiefNavigator>();
            var ride = board.GetComponent<MagneticHoverboardController>();
            Set(thief, "m_agent", thief.GetComponent<UnityEngine.AI.NavMeshAgent>());
            Sync(thief, "m_hoverboard").Value = ride.GetComponent<NetworkObject>();
            Assert.That(thief.IsRidingHoverboard, Is.True);
            Assert.That(thief.CanAttack, Is.False);
            Call(thief, "UpdateHoverboardPose");
            Assert.That(Vector3.Distance(thief.transform.position, ride.DriverAnchor.position), Is.LessThan(.001f));
            Sync(thief, "m_hoverboard").Value = null;
            Assert.That(thief.IsRidingHoverboard, Is.False);
        }

        [Test]
        public void HoverboardSteeringBrakesWithoutPathAndBoundsForwardAndReverseInput()
        {
            var board = Create<MagneticHoverboardController>();
            var settings = ScriptableObject.CreateInstance<MagneticHoverboardSettings>(); m_objects.Add(settings);
            Set(board, "m_settings", settings);
            var method = typeof(ThiefNavigator).GetMethod("CalculateHoverboardInput", BindingFlags.Static | BindingFlags.NonPublic);
            Vector2 Input(Vector3 direction) => (Vector2)method.Invoke(null, new object[] { board, direction });
            Assert.That(Input(Vector3.zero), Is.EqualTo(Vector2.zero));
            Assert.That(Input(Vector3.forward).y, Is.GreaterThan(0f));
            Assert.That(Input(Vector3.back).y, Is.LessThan(0f));
            foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                var input = Input(direction);
                Assert.That(Mathf.Abs(input.x), Is.LessThanOrEqualTo(1f));
                Assert.That(Mathf.Abs(input.y), Is.LessThanOrEqualTo(1f));
            }
        }
    }
}
