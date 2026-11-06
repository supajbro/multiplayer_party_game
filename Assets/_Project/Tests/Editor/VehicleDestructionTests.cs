using System;
using System.Reflection;
using CouchGuys.Gameplay.Vehicles;
using FishNet.Component.Transforming;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class VehicleDestructionTests
    {
        [TestCase("Assets/_Project/Prefabs/Vehicles/Truck.prefab")]
        [TestCase("Assets/_Project/Prefabs/Vehicles/ThiefGetawayTruck.prefab")]
        [TestCase("Assets/_Project/Prefabs/Vehicles/MagneticHoverboard.prefab")]
        [TestCase("Assets/_Project/Resources/Traffic/TrafficCar.prefab")]
        [TestCase("Assets/_Project/Prefabs/Enemies/RogueCar.prefab")]
        public void WreckUsesExistingEffectAndServerTransform(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var destruction = prefab.GetComponent<VehicleDestruction>();
            Assert.That(destruction, Is.Not.Null, path);
            var serialized = new SerializedObject(destruction);
            var explosion = serialized.FindProperty("m_explosion").objectReferenceValue;
            Assert.That(AssetDatabase.GetAssetPath(explosion),
                Is.EqualTo("Assets/_Project/Prefabs/Weapons/RocketLauncher/RocketExplosion.prefab"));
            var network = new SerializedObject(prefab.GetComponent<NetworkObject>());
            var behaviours = network.FindProperty("NetworkBehaviours");
            int occurrences = 0;
            for (int i = 0; i < behaviours.arraySize; i++)
                if (behaviours.GetArrayElementAtIndex(i).objectReferenceValue == destruction) occurrences++;
            Assert.That(occurrences, Is.EqualTo(1), "The shared RPC component must be registered exactly once.");
            var transform = new SerializedObject(prefab.GetComponent<NetworkTransform>());
            Assert.That(transform.FindProperty("_clientAuthoritative").boolValue, Is.False);
            Assert.That(transform.FindProperty("_synchronizePosition").boolValue, Is.True);
            Assert.That(transform.FindProperty("_synchronizeRotation").boolValue, Is.True);
        }

        [TestCase(160f)]
        [TestCase(2400f)]
        public void KinematicWreckLaunchesTumblesAndFallsOntoGround(float mass)
        {
            Scene scene = SceneManager.CreateScene("Wreck physics " + Guid.NewGuid(),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            try
            {
                var floor = new GameObject("Ground");
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.position = Vector3.down * .5f;
                floor.AddComponent<BoxCollider>().size = new Vector3(200f, 1f, 200f);
                var wreck = new GameObject("Wreck");
                SceneManager.MoveGameObjectToScene(wreck, scene);
                wreck.transform.position = Vector3.up;
                wreck.AddComponent<BoxCollider>();
                var body = wreck.AddComponent<Rigidbody>();
                body.mass = mass;
                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                var settings = new VehicleDestructionSettings(16f, 4f, 8f);
                typeof(VehicleDestruction).GetMethod("Launch", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { body, settings, Vector3.right * 4f, new Vector3(.8f, 0f, .6f) });
                PhysicsScene physics = scene.GetPhysicsScene();
                physics.Simulate(.02f);
                Assert.That(body.isKinematic, Is.False);
                Assert.That(body.useGravity && body.detectCollisions, Is.True);
                Assert.That(body.constraints, Is.EqualTo(RigidbodyConstraints.None));
                Assert.That(body.linearVelocity.y, Is.GreaterThan(15f));
                Assert.That(body.linearVelocity.x, Is.GreaterThan(3.8f));
                Assert.That(body.angularVelocity.magnitude, Is.GreaterThan(7f));
                for (int i = 0; i < 49; i++) physics.Simulate(.02f);
                Assert.That(body.position.y, Is.GreaterThan(8f));
                Assert.That(Quaternion.Angle(body.rotation, Quaternion.identity), Is.GreaterThan(10f));
                for (int i = 0; i < 500; i++) physics.Simulate(.02f);
                Assert.That(body.position.y, Is.InRange(.45f, 1f), "The wreck must fall and collide with the ground.");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
