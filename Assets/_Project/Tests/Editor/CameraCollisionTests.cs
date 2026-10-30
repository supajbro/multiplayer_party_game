using System.Collections.Generic;
using System.Reflection;
using CouchGuys.CameraSystem;
using NUnit.Framework;
using UnityEngine;

namespace CouchGuys.Tests.Editor
{
    public sealed class CameraCollisionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Vector3 Origin = new(15000f, 1000f, 15000f);
        private readonly List<GameObject> m_objects = new();
        private ThirdPersonCameraController m_camera;

        [SetUp]
        public void SetUp()
        {
            GameObject rig = Create("Camera collision test", Origin);
            rig.SetActive(false);
            m_camera = rig.AddComponent<ThirdPersonCameraController>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject instance in m_objects) Object.DestroyImmediate(instance);
            m_objects.Clear();
        }

        [TestCase(-35f)]
        [TestCase(12f)]
        [TestCase(65f)]
        public void Orbit_StartingInsideCouch_KeepsCameraSphereOutside(float pitch)
        {
            BoxCollider couch = CreateBox(Origin, new Vector3(3f, 2f, 3f));
            typeof(ThirdPersonCameraController).GetField("m_cameraDistance", Private).SetValue(m_camera, 1f);
            Physics.SyncTransforms();

            Vector3 result = Invoke("CalculateCollisionPosition", Origin, Quaternion.Euler(pitch, 0f, 0f));

            Assert.That(Vector3.Distance(result, couch.ClosestPoint(result)), Is.GreaterThanOrEqualTo(0.199f));
        }

        [Test]
        public void SmoothedPosition_InsideFurniture_IsClampedBeforeSurface()
        {
            BoxCollider couch = CreateBox(Origin + Vector3.back * 2f, Vector3.one);
            Physics.SyncTransforms();

            Vector3 result = Invoke("ResolveCollisionPosition", Origin, couch.transform.position);

            Assert.That(result.z - Origin.z, Is.InRange(-1.21f, -1.19f));
            Assert.That(Vector3.Distance(result, couch.ClosestPoint(result)), Is.GreaterThan(0.29f));
        }

        [Test]
        public void Pivot_IgnoresPlayerAndTriggers_ButClearsFurniture()
        {
            BoxCollider player = CreateBox(Origin, Vector3.one * 10f);
            typeof(ThirdPersonCameraController).GetField("m_playerRoot", Private).SetValue(m_camera, player.transform);
            CreateBox(Origin, Vector3.one * 20f).isTrigger = true;
            Physics.SyncTransforms();
            Assert.That(Invoke("GetClearCollisionPivot", Origin), Is.EqualTo(Origin));

            BoxCollider couch = CreateBox(Origin, Vector3.one * 2f);
            Physics.SyncTransforms();
            Vector3 pivot = Invoke("GetClearCollisionPivot", Origin);
            Assert.That(pivot.y, Is.GreaterThan(couch.bounds.max.y + 0.2f));
        }

        [Test]
        public void ClearBoom_PreservesConfiguredDistance()
        {
            Physics.SyncTransforms();
            Vector3 result = Invoke("CalculateCollisionPosition", Origin, Quaternion.identity);
            Assert.That(Vector3.Distance(result, Origin + new Vector3(0.65f, 0f, -4.5f)), Is.LessThan(0.002f));
        }

        private Vector3 Invoke(string method, params object[] arguments)
        {
            return (Vector3)typeof(ThirdPersonCameraController).GetMethod(method, Private).Invoke(m_camera, arguments);
        }

        private GameObject Create(string name, Vector3 position)
        {
            GameObject instance = new(name);
            instance.transform.position = position;
            m_objects.Add(instance);
            return instance;
        }

        private BoxCollider CreateBox(Vector3 position, Vector3 size)
        {
            BoxCollider collider = Create("Camera obstacle", position).AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }
    }
}
