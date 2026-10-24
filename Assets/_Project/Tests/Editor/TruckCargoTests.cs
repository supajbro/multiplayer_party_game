using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Vehicles;
using NUnit.Framework;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class TruckCargoTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> m_objects = new();
        private static readonly Vector3 Origin = new(10000f, 1000f, 10000f);
        private TruckController m_truck;
        private CouchCarryController m_couch;
        private BoxCollider m_body;
        private static readonly Bounds CouchBounds = new(Vector3.up * .45f, new Vector3(2.1f, .9f, .9f));

        [SetUp]
        public void SetUp()
        {
            GameObject truck = Create("Cargo test truck", Origin);
            m_truck = truck.AddComponent<TruckController>();
            GameObject rear = Create("Cargo test rear", Origin + Vector3.back * 4f);
            rear.transform.SetParent(truck.transform, true);
            Set(m_truck, "m_cargoLoadingPoint", rear.transform);
            GameObject couch = Create("Cargo test couch", Origin + Vector3.up * 2f);
            m_body = couch.AddComponent<BoxCollider>();
            m_body.center = CouchBounds.center;
            m_body.size = CouchBounds.size;
            m_couch = couch.AddComponent<CouchCarryController>();
            Set(m_couch, "m_colliders", new Collider[] { m_body });
            Set(m_couch, "m_cargoBounds", CouchBounds);
            Set(m_couch, "m_rigidbody", couch.GetComponent<Rigidbody>());
            couch.GetComponent<Rigidbody>().isKinematic = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject obj in m_objects)
                if (obj != null) Object.DestroyImmediate(obj);
            m_objects.Clear();
        }

        [TestCase(0f)]
        [TestCase(20f)]
        public void RepeatedPlacementHandlesRotatedDisabledCouchesAndSlopes(float slope)
        {
            GameObject floor = Box("Cargo test floor", Origin - Vector3.up * .5f, new Vector3(100f, 1f, 100f));
            floor.transform.rotation = Quaternion.Euler(slope, 0f, 0f);
            m_truck.transform.rotation = floor.transform.rotation;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                m_couch.transform.rotation = Quaternion.Euler(attempt * 31f, attempt * 73f, attempt * 17f);
                m_body.enabled = false;
                Pose pose = FindPose();
                Assert.That(Vector3.Angle(pose.rotation * Vector3.up, floor.transform.up), Is.LessThan(.1f));
                AssertClear(pose);
            }
        }

        [Test]
        public void ObstructedRearSearchesNearbyAndSaturatedQueriesStayBlocked()
        {
            Box("Cargo test floor", Origin - Vector3.up * .5f, new Vector3(100f, 1f, 100f));
            Pose preferred = FindPose();
            for (int i = 0; i < 40; i++)
                Box("Cargo test obstruction", preferred.position + Vector3.up * .5f, Vector3.one);
            Pose alternative = FindPose();
            Assert.That(Vector3.Distance(preferred.position, alternative.position), Is.GreaterThan(1f));
            AssertClear(alternative);
        }

        [Test]
        public void FullyBlockedAreaStillUnloadsAndRestoresObstacleCollisionAfterSeparation()
        {
            Box("Cargo test floor", Origin - Vector3.up * .5f, new Vector3(100f, 1f, 100f));
            // Tall narrow columns obstruct all search positions without supplying a walkable roof in probe range.
            for (int x = -9; x <= 9; x++)
                for (int z = -12; z <= 6; z++)
                    Box("Cargo test column", Origin + new Vector3(x * 2f, 10f, z * 2f), new Vector3(1.9f, 20f, 1.9f));
            Pose fallback = FindPose();
            m_couch.transform.SetPositionAndRotation(fallback.position, fallback.rotation);
            Invoke(m_couch, "PrepareUnloadCollisionRecovery");
            var ignored = (List<Collider>)typeof(CouchCarryController).GetField("m_cargoIgnoredColliders", Private).GetValue(m_couch);
            Assert.That(ignored, Is.Not.Empty);
            Assert.That(ignored.Exists(hit => hit.name == "Cargo test floor"), Is.False);
            Collider obstacle = ignored[0];
            Invoke(m_couch, "SetCargoCollisionIgnored", obstacle, true);
            Assert.That(Physics.GetIgnoreCollision(m_body, obstacle), Is.True);
            m_couch.transform.position += Vector3.right * 100f;
            Physics.SyncTransforms();
            Invoke(m_couch, "UpdateCargoCollisionRecovery");
            Assert.That(ignored, Is.Empty);
            Assert.That(Physics.GetIgnoreCollision(m_body, obstacle), Is.False);
        }

        private Pose FindPose()
        {
            object[] args = { m_couch, 0, Vector3.zero, Quaternion.identity };
            typeof(TruckController).GetMethod("GetCouchUnloadPose", Private).Invoke(m_truck, args);
            return new Pose((Vector3)args[2], (Quaternion)args[3]);
        }

        private void AssertClear(Pose pose)
        {
            foreach (Collider hit in Physics.OverlapBox(pose.position + pose.rotation * CouchBounds.center,
                CouchBounds.extents + Vector3.one * .02f, pose.rotation, ~0, QueryTriggerInteraction.Ignore))
                Assert.That(hit.transform.IsChildOf(m_couch.transform), Is.True, hit.name);
        }

        private GameObject Create(string name, Vector3 position)
        {
            GameObject obj = new(name);
            obj.transform.position = position;
            m_objects.Add(obj);
            return obj;
        }

        private GameObject Box(string name, Vector3 position, Vector3 size)
        {
            GameObject obj = Create(name, position);
            obj.AddComponent<BoxCollider>().size = size;
            return obj;
        }

        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    }
}
