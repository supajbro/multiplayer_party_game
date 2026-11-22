using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Tests.Editor
{
    public sealed class TrafficPlayerVehicleContactTests
    {
        private GameObject m_car, m_vehicle, m_control;
        private static readonly MethodInfo Contact = typeof(TrafficVehicleController).GetMethod(
            "IgnoreAndCheckContact", BindingFlags.Static | BindingFlags.NonPublic);

        [TearDown]
        public void Cleanup()
        {
            if (m_car != null) Object.DestroyImmediate(m_car);
            if (m_vehicle != null) Object.DestroyImmediate(m_vehicle);
            if (m_control != null) Object.DestroyImmediate(m_control);
        }

        private bool Check(Vector3 velocity, float dt = .02f) => (bool)Contact.Invoke(null, new object[] {
            m_car.GetComponent<Rigidbody>(), m_vehicle.GetComponent<Rigidbody>(),
            new List<Collider>(m_car.GetComponentsInChildren<Collider>()),
            new List<Collider>(m_vehicle.GetComponentsInChildren<Collider>()), dt, velocity });

        [TestCase("Truck")]
        [TestCase("Flamecouch")]
        [TestCase("MagneticHoverboard")]
        public void ActualVehicleCollidersIgnoreOverlappingTrafficWithoutReceivingImpulse(string name)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                m_car = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Project/Prefabs/Vehicles/TrafficCar.prefab"));
                m_vehicle = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/_Project/Prefabs/Vehicles/{name}.prefab"));
                SceneManager.MoveGameObjectToScene(m_car, scene);
                SceneManager.MoveGameObjectToScene(m_vehicle, scene);
                var body = m_vehicle.GetComponent<Rigidbody>();
                var carBody = m_car.GetComponent<Rigidbody>();
                body.isKinematic = false;
                body.useGravity = carBody.useGravity = false;
                body.linearVelocity = new Vector3(0, 0, 12);
                body.angularVelocity = Vector3.zero;
                carBody.linearVelocity = new Vector3(0, 0, -13);
                m_control = new GameObject("Unobstructed vehicle reference");
                SceneManager.MoveGameObjectToScene(m_control, scene);
                var control = m_control.AddComponent<Rigidbody>();
                control.useGravity = false;
                control.linearDamping = body.linearDamping;
                control.linearVelocity = body.linearVelocity;
                Assert.That(Check(carBody.linearVelocity), Is.True, "Initial overlap must trigger destruction, even without a collision callback.");
                foreach (var a in m_car.GetComponentsInChildren<Collider>())
                    foreach (var b in m_vehicle.GetComponentsInChildren<Collider>())
                        if (a.enabled && b.enabled && !a.isTrigger && !b.isTrigger && b.attachedRigidbody == body)
                            Assert.That(Physics.GetIgnoreCollision(a, b), Is.True);
                Assert.That(scene.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene));
                scene.GetPhysicsScene().Simulate(.02f);
                Assert.That(body.angularVelocity.magnitude, Is.LessThan(.001f));
                Assert.That(Vector3.Distance(body.linearVelocity, control.linearVelocity), Is.LessThan(.001f),
                    "Traffic contact must not slow, accelerate or deflect the player vehicle.");
            }
            finally
            {
                Cleanup();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RelativeSweepDetectsHighSpeedContactBeforeBodiesOverlap(bool movingPlayer)
        {
            m_car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var car = m_car.AddComponent<Rigidbody>();
            var vehicle = m_vehicle.AddComponent<Rigidbody>();
            m_car.transform.position = new Vector3(48000, 10, 48000);
            m_vehicle.transform.position = m_car.transform.position + Vector3.forward * 2;
            car.useGravity = vehicle.useGravity = false;
            vehicle.linearVelocity = movingPlayer ? Vector3.back * 100 : Vector3.zero;
            Physics.SyncTransforms();
            Assert.That(Check(movingPlayer ? Vector3.zero : Vector3.forward * 100), Is.True);
        }

        [Test]
        public void SeparatedVehiclesDoNotTriggerContact()
        {
            m_car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_car.AddComponent<Rigidbody>(); m_vehicle.AddComponent<Rigidbody>();
            m_vehicle.transform.position = Vector3.right * 100;
            Physics.SyncTransforms();
            Assert.That(Check(Vector3.zero), Is.False);
        }

        [TestCase("Assets/_Project/Prefabs/Vehicles/TrafficCar.prefab", true)]
        [TestCase("Assets/_Project/Prefabs/Enemies/RogueCar.prefab", false)]
        public void OnlyOrdinaryTrafficUsesHarmlessContact(string path, bool expected)
        {
            var car = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<TrafficVehicleController>();
            Assert.That(typeof(TrafficVehicleController).GetProperty("IsOrdinaryTraffic",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(car), Is.EqualTo(expected));
        }
    }
}
