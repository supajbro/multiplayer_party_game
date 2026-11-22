using System.Reflection;
using CouchGuys.Gameplay.Vehicles;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Tests.Editor
{
    public sealed class TruckSteeringTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene m_scene;
        private GameObject m_vehicle;
        private TruckController m_truck;
        private Rigidbody m_body;
        private TruckSettings m_settings;

        private void CreateVehicle(string name = "Truck")
        {
            m_scene = EditorSceneManager.NewPreviewScene();
            m_vehicle = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/_Project/Prefabs/Vehicles/{name}.prefab"));
            SceneManager.MoveGameObjectToScene(m_vehicle, m_scene);
            m_truck = m_vehicle.GetComponent<TruckController>();
            m_body = m_vehicle.GetComponent<Rigidbody>();
            m_body.isKinematic = false;
            m_body.useGravity = false;
            m_settings = Object.Instantiate(m_truck.Settings);
            typeof(TruckController).GetField("m_settings", Hidden).SetValue(m_truck, m_settings);
        }

        [TearDown]
        public void Cleanup()
        {
            if (m_vehicle != null) Object.DestroyImmediate(m_vehicle);
            if (m_settings != null) Object.DestroyImmediate(m_settings);
            if (m_scene.IsValid()) EditorSceneManager.ClosePreviewScene(m_scene);
        }

        private void Input(float steering) => typeof(TruckController).GetField("m_driveInput", Hidden)
            .SetValue(m_truck, new Vector2(steering, 0f));

        private void Steer(float speed, float dt) => typeof(TruckController).GetMethod("SimulateSteering", Hidden)
            .Invoke(m_truck, new object[] { speed, dt });

        [TestCase(2.5f)]
        [TestCase(-2.5f)]
        [TestCase(12.5f)]
        public void TruckHasTighterTurnsThanPreviousSettingsAndHonoursItsRadiusLimit(float speed)
        {
            CreateVehicle();
            Input(1f);
            for (int i = 0; i < 150; i++) Steer(speed, .02f);
            float yaw = m_body.angularVelocity.y;
            float speed01 = Mathf.Clamp01(Mathf.Abs(speed) / (45f / 3.6f));
            float previousYaw = 100f * Mathf.Lerp(1f, .32f, speed01) * speed01 * Mathf.Deg2Rad;
            float radius = Mathf.Abs(speed / yaw);
            Assert.That(Mathf.Sign(yaw), Is.EqualTo(Mathf.Sign(speed)), "Reverse steering must reverse yaw.");
            Assert.That(Mathf.Abs(yaw), Is.GreaterThan(previousYaw * 1.5f));
            Assert.That(radius, Is.GreaterThanOrEqualTo(m_settings.MinimumTurningRadius - .01f));
            if (Mathf.Abs(speed) < 3f) Assert.That(radius, Is.EqualTo(4.5f).Within(.05f));
        }

        [Test]
        public void PartialSteeringProducesAWiderTurn()
        {
            CreateVehicle();
            Input(.5f);
            for (int i = 0; i < 150; i++) Steer(2.5f, .02f);
            Assert.That(2.5f / m_body.angularVelocity.y, Is.EqualTo(9f).Within(.05f));
        }

        [TestCase("Truck", .02f)]
        [TestCase("Flamecouch", .02f)]
        [TestCase("Truck", .01f)]
        [TestCase("Truck", .0333333f)]
        public void OffCentreImpactSpinSettlesWithoutDriverInput(string name, float dt)
        {
            CreateVehicle(name);
            // A glancing impact applies linear impulse away from the centre of
            // mass. Use actual prefab inertia and Unity's physics integration.
            m_body.AddForceAtPosition(Vector3.right * (m_body.mass * 8f),
                m_body.worldCenterOfMass + Vector3.forward * 3f, ForceMode.Impulse);
            PhysicsScene physics = m_scene.GetPhysicsScene();
            physics.Simulate(dt);
            Assert.That(Mathf.Abs(m_body.angularVelocity.y), Is.GreaterThan(.3f), "The setup must impart yaw spin.");
            var drive = typeof(TruckController).GetMethod("SimulateDriving", Hidden);
            for (int i = 0; i < Mathf.CeilToInt(2f / dt); i++)
            {
                drive.Invoke(m_truck, new object[] { dt });
                physics.Simulate(dt);
            }
            Assert.That(Mathf.Abs(m_body.angularVelocity.y), Is.LessThan(.001f), "Collision spin must recover even without a driver.");
        }

        [Test]
        public void StationaryYawRecoveryPreservesPitchAndRoll()
        {
            CreateVehicle();
            Input(1f);
            m_body.angularVelocity = new Vector3(.3f, 5f, -.4f);
            for (int i = 0; i < 100; i++) Steer(0f, .02f);
            Assert.That(Mathf.Abs(m_body.angularVelocity.y), Is.LessThan(.001f));
            Assert.That(m_body.angularVelocity.x, Is.EqualTo(.3f).Within(.0001f));
            Assert.That(m_body.angularVelocity.z, Is.EqualTo(-.4f).Within(.0001f));
        }

        [Test]
        public void ReleasingSteeringStopsAnExistingTurn()
        {
            CreateVehicle();
            Input(1f);
            for (int i = 0; i < 100; i++) Steer(8f, .02f);
            Assert.That(m_body.angularVelocity.y, Is.GreaterThan(.5f));
            Input(0f);
            for (int i = 0; i < 100; i++) Steer(8f, .02f);
            Assert.That(Mathf.Abs(m_body.angularVelocity.y), Is.LessThan(.001f));
        }
    }
}
