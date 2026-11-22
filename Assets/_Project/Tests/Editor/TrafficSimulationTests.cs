using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Traffic;
using NUnit.Framework;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class TrafficSimulationTests
    {
        private TrafficSettings m_settings;
        private GameObject m_car;
        private GameObject m_manager;
        private CarSettings m_carSettings;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp() => m_settings = ScriptableObject.CreateInstance<TrafficSettings>();

        [TearDown]
        public void TearDown()
        {
            if (m_car != null) Object.DestroyImmediate(m_car);
            if (m_manager != null) Object.DestroyImmediate(m_manager);
            if (m_carSettings != null) Object.DestroyImmediate(m_carSettings);
            Object.DestroyImmediate(m_settings);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemotePlayerKeepsTrafficPhysicalRegardlessOfObserverOrder(bool reverse)
        {
            var players = new List<TrafficObserver>
            {
                new(Vector3.zero, 0f), new(Vector3.right * 1000f, 0f)
            };
            if (reverse) players.Reverse();
            Vector3 car = Vector3.right * 1020f;
            Assert.That(TrafficSimulationPolicy.NeedsFullPhysics(car, 13f, true, players, m_settings), Is.True);
            Assert.That(TrafficSimulationPolicy.CanDespawn(car, 13f, players, m_settings), Is.False);
        }

        [Test]
        public void SeparateDistancesPreventRepeatedSwitchingAtBoundary()
        {
            var players = new[] { new TrafficObserver(Vector3.zero, 0f) };
            var car = Vector3.right * 120f;
            Assert.That(TrafficSimulationPolicy.NeedsFullPhysics(car, 0f, true, players, m_settings), Is.False);
            Assert.That(TrafficSimulationPolicy.NeedsFullPhysics(car, 0f, false, players, m_settings), Is.True);
        }

        [Test]
        public void FastApproachActivatesPhysicsBeforeNominalDistance()
        {
            var players = new[] { new TrafficObserver(Vector3.zero, 40f) };
            var car = Vector3.right * 150f;
            Assert.That(TrafficSimulationPolicy.NeedsFullPhysics(car, 30f, true, players, m_settings), Is.True);
            players[0] = new TrafficObserver(Vector3.zero, 0f);
            Assert.That(TrafficSimulationPolicy.NeedsFullPhysics(car, 0f, true, players, m_settings), Is.False);
        }

        [Test]
        public void DespawnRequiresDistanceFromAllPlayersAndIncludesApproachBuffer()
        {
            var players = new List<TrafficObserver> { new(Vector3.zero, 0f), new(Vector3.right * 900f, 0f) };
            Assert.That(TrafficSimulationPolicy.CanDespawn(Vector3.right * 920f, 13f, players, m_settings), Is.False);
            players.RemoveAt(1); // Disconnected remote viewer no longer retains that car.
            Assert.That(TrafficSimulationPolicy.CanDespawn(Vector3.right * 920f, 13f, players, m_settings), Is.True);
            players[0] = new TrafficObserver(Vector3.zero, 40f);
            Assert.That(TrafficSimulationPolicy.CanDespawn(Vector3.right * 470f, 13f, players, m_settings), Is.False);
        }

        [Test]
        public void NoPlayersAllowsPopulationCleanupButDoesNotGuessASimulationTier()
        {
            var players = new TrafficObserver[0];
            Assert.That(TrafficSimulationPolicy.CanDespawn(Vector3.zero, 13f, players, m_settings), Is.True);
            Assert.That(TrafficSimulationPolicy.NeedsFullPhysics(Vector3.zero, 13f, true, players, m_settings), Is.True);
        }

        [Test]
        public void RouteCanStartInsidePlayerRangeWhenTunnelIsTooDistant()
        {
            m_manager = new GameObject("Traffic spawn test");
            var manager = m_manager.AddComponent<TrafficManager>();
            Set(manager, "m_settings", m_settings);
            var observers = (List<TrafficObserver>)typeof(TrafficManager).GetField("m_observers", Private).GetValue(manager);
            observers.Add(new TrafficObserver(Vector3.zero, 0f));
            var route = new TrafficRoute(new[]
            {
                Vector3.right * 1000f, Vector3.right * 800f, Vector3.right * 300f, Vector3.right * 200f
            });
            Assert.That((int)Invoke(manager, "FindSpawnWaypoint", route), Is.EqualTo(2));
        }

        [Test]
        public void DistantMovementFollowsSlopesAndTransfersVelocityToPhysics()
        {
            var car = CreateCar();
            var body = m_car.GetComponent<Rigidbody>();
            var route = new TrafficRoute(new[] { Vector3.zero, new Vector3(0f, 2f, 10f), new Vector3(10f, 2f, 10f) });
            Set(car, "m_route", route);
            Set(car, "m_waypointIndex", 1);
            body.position = Vector3.zero;
            Invoke(car, "SetDistantSimulation", true);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.detectCollisions, Is.True, "Long-range weapons can still hit the chassis.");
            Assert.That((bool)Invoke(car, "AdvanceDistantRoute", 1f), Is.True);
            Assert.That(body.position.y, Is.EqualTo(2f).Within(0.01f));
            Assert.That(body.position.x, Is.GreaterThan(0f), "Movement continues around the route corner.");
            Vector3 before = body.position;
            Invoke(car, "KeepFullPhysicsForInteraction");
            Assert.That(car.IsDistantSimulation, Is.False);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.position, Is.EqualTo(before), "Reactivation does not teleport the car.");
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
            Assert.That(body.linearVelocity.magnitude, Is.EqualTo(m_carSettings.MaximumDrivingSpeed).Within(0.01f));
        }

        [Test]
        public void RouteCompletionAndPoolResetDoNotRetainPreviousTripState()
        {
            var car = CreateCar();
            Set(car, "m_route", new TrafficRoute(new[] { Vector3.zero, Vector3.forward }));
            Set(car, "m_waypointIndex", 1);
            Invoke(car, "SetDistantSimulation", true);
            Assert.That((bool)Invoke(car, "AdvanceDistantRoute", 1f), Is.False);
            Invoke(car, "ResetSimulationServer");
            Assert.That(car.IsDistantSimulation, Is.False);
            var body = m_car.GetComponent<Rigidbody>();
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(typeof(TrafficVehicleController).GetField("m_route", Private).GetValue(car), Is.Null);
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
        }

        private TrafficVehicleController CreateCar()
        {
            m_car = new GameObject("Traffic simulation test");
            var body = m_car.AddComponent<Rigidbody>();
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var car = m_car.AddComponent<TrafficVehicleController>();
            m_carSettings = ScriptableObject.CreateInstance<CarSettings>();
            Set(car, "m_settings", m_carSettings);
            Set(car, "m_simulationSettings", m_settings);
            return car;
        }

        private static void Set(object instance, string field, object value) =>
            instance.GetType().GetField(field, Private).SetValue(instance, value);
        private static object Invoke(object instance, string method, params object[] args) =>
            instance.GetType().GetMethod(method, Private).Invoke(instance, args);
    }
}
