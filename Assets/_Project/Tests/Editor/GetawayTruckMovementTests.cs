using System;
using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Vehicles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class GetawayTruckMovementTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void SharedCarryPathApproachesTruckAroundAnObstacle()
        {
            Vector3 origin = new Vector3(24000, 0, 24000);
            var configuration = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            var truck = Object.Instantiate(configuration.TruckPrefab.gameObject, origin + Vector3.forward * 25, Quaternion.identity);
            var couchObject = new GameObject("Getaway shared path test couch");
            var couch = couchObject.AddComponent<CouchCarryController>();
            couchObject.transform.position = origin;
            NavMeshData data = null;
            NavMeshDataInstance mesh = default;
            try
            {
                var settings = NavMesh.GetSettingsByIndex(0);
                var sources = new List<NavMeshBuildSource>
                {
                    new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 0,
                        transform = Matrix4x4.TRS(origin - Vector3.up * .1f, Quaternion.identity, Vector3.one),
                        size = new Vector3(80, .2f, 80) },
                    new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 1,
                        transform = Matrix4x4.TRS(origin + new Vector3(0, 2, 12), Quaternion.identity, Vector3.one),
                        size = new Vector3(10, 4, 8) }
                };
                data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                    new Bounds(origin, new Vector3(100, 20, 100)), Vector3.zero, Quaternion.identity);
                Assert.That(data, Is.Not.Null);
                mesh = NavMesh.AddNavMeshData(data);
                var controller = truck.GetComponent<GetawayTruckController>();
                typeof(GetawayTruckController).GetField("m_truck", Hidden).SetValue(controller, truck.GetComponent<TruckController>());
                MethodInfo intent = typeof(GetawayTruckController).GetMethod("GetCarryMovementIntent", Hidden);
                Vector3 first = (Vector3)intent.Invoke(controller, new object[] { couch });
                Vector3 second = (Vector3)intent.Invoke(controller, new object[] { couch });
                Assert.That(first.magnitude, Is.GreaterThan(.9f), "Carriers must move before parking completes.");
                Assert.That(Mathf.Abs(first.x), Is.GreaterThan(.1f), "The shared path must lead around the obstacle.");
                Assert.That(Vector3.Dot(first, second), Is.GreaterThan(.99f), "Both grips must pull the same way.");
                truck.transform.position = origin + new Vector3(30, 0, 0);
                typeof(GetawayTruckController).GetField("m_nextCarryPath", Hidden).SetValue(controller, 0f);
                Vector3 updated = (Vector3)intent.Invoke(controller, new object[] { couch });
                Assert.That(updated.x, Is.GreaterThan(.9f), "The destination must follow the live truck.");
                truck.transform.position = origin + Vector3.right * 60;
                typeof(GetawayTruckController).GetField("m_nextCarryPath", Hidden).SetValue(controller, 0f);
                Vector3 nearest = (Vector3)intent.Invoke(controller, new object[] { couch });
                Assert.That(nearest.x, Is.GreaterThan(.9f), "Move as close as the NavMesh permits when the truck is outside it.");
            }
            finally
            {
                Object.DestroyImmediate(truck);
                Object.DestroyImmediate(couchObject);
                if (mesh.valid) mesh.Remove();
                if (data != null) Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void RogueMotorDrivesActualTruckThroughTurnsWithoutTipping()
        {
            if (!Application.isPlaying) Assert.Ignore("Run Couch Guys/Tests/Validate Getaway Truck for the Play-mode physics check.");
            var configuration = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            var scene = SceneManager.CreateScene("Getaway motor isolated test", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physics = scene.GetPhysicsScene();
            GameObject truck = null;
            GameObject ground = null;
            GameObject networkContext = null;
            FishNet.Managing.Object.SinglePrefabObjects prefabs = null;
            PhysicsMaterial contact = null;
            var trace = new System.Text.StringBuilder();
            try
            {
                Vector3 origin = new Vector3(26000, 0, 26000);
                ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Ground truck flat regression";
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = origin - Vector3.up * .5f;
                ground.transform.localScale = new Vector3(200, 1, 200);
                if (Object.FindFirstObjectByType<FishNet.Managing.NetworkManager>() == null)
                {
                    networkContext = new GameObject("Getaway physics test network context");
                    networkContext.SetActive(false);
                    SceneManager.MoveGameObjectToScene(networkContext, scene);
                    var networkManager = networkContext.AddComponent<FishNet.Managing.NetworkManager>();
                    prefabs = ScriptableObject.CreateInstance<FishNet.Managing.Object.SinglePrefabObjects>();
                    prefabs.AddObject(configuration.TruckPrefab, initializeAdded: false);
                    networkManager.SpawnablePrefabs = prefabs;
                    networkContext.SetActive(true);
                }
                truck = Object.Instantiate(configuration.TruckPrefab.gameObject, origin + Vector3.up, Quaternion.identity);
                SceneManager.MoveGameObjectToScene(truck, scene);
                Rigidbody body = truck.GetComponent<Rigidbody>();
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.None; // This test advances physics without rendering frames.
                contact = new PhysicsMaterial("Getaway motor test contact")
                { dynamicFriction = .03f, staticFriction = 0, frictionCombine = PhysicsMaterialCombine.Minimum };
                foreach (Collider collider in truck.GetComponentsInChildren<Collider>()) collider.sharedMaterial = contact;
                Type motorType = typeof(GetawayTruckController).Assembly.GetType("CouchGuys.Gameplay.Vehicles.RogueVehicleMotor", true);
                object motor = Activator.CreateInstance(motorType, Hidden, null, new object[] { body }, null);
                motorType.GetMethod("ConfigureRoadContact", Hidden).Invoke(motor,
                    new object[] { configuration.GroundAlignmentStrength, configuration.GroundAlignmentDamping });
                MethodInfo drive = motorType.GetMethod("Drive", Hidden);
                MethodInfo stop = motorType.GetMethod("Stop", Hidden);
                Vector3[] goals = { origin + Vector3.forward * 30, origin + new Vector3(30, 0, 30),
                    origin + new Vector3(30, 0, 60), Vector3.zero };
                int goal = 0;
                float reverseTime = 0f;
                float maximumReverseTime = 0f;
                Physics.SyncTransforms();
                for (int step = 0; step < 2500 && goal < goals.Length; step++)
                {
                    bool escape = goal == 3;
                    float steering = (float)drive.Invoke(motor, new object[] { configuration.DrivingSettings, goals[goal],
                        escape ? configuration.EscapeSpeed : configuration.ApproachSpeed, 1f, .02f, escape });
                    physics.Simulate(.02f);
                    if (escape)
                    {
                        reverseTime = Vector3.Dot(body.linearVelocity, body.rotation * Vector3.forward) < -.3f ? reverseTime + .02f : 0f;
                        maximumReverseTime = Mathf.Max(maximumReverseTime, reverseTime);
                    }
                    if (step % 50 == 0) trace.AppendLine($"t={step * .02f:F1} goal={goal} p={body.position-origin} yaw={body.rotation.eulerAngles.y:F1} v={body.linearVelocity} angular={body.angularVelocity.y:F2} steer={steering:F2} inertia={body.inertiaTensor} reverse={motorType.GetField("m_targetReverse", Hidden).GetValue(motor)}");
                    Assert.That(Vector3.Dot(body.rotation * Vector3.up, Vector3.up), Is.GreaterThan(.99f),
                        $"Truck tipped during a turn: rotation={body.rotation.eulerAngles}, constraints={body.constraints}, inertia frame={body.inertiaTensorRotation.eulerAngles}.");
                    Assert.That(body.position.y - origin.y, Is.LessThan(3f), "Truck was launched off the ground.");
                    if (Vector3.ProjectOnPlane(goals[goal] - body.position, Vector3.up).magnitude < 3f)
                    {
                        if (++goal == 3)
                        {
                            goals[3] = body.position - body.rotation * Vector3.forward * 10f;
                            motorType.GetField("m_targetReverse", Hidden).SetValue(motor, true);
                        }
                    }
                }
                Assert.That(goal, Is.EqualTo(goals.Length), $"Truck failed to follow goals; stopped at {body.position - origin}.");
                Assert.That(maximumReverseTime, Is.LessThan(1.7f), "Escape must turn and drive forward instead of reversing along its route.");
                for (int step = 0; step < 100; step++)
                { stop.Invoke(motor, new object[] { configuration.DrivingSettings, false }); physics.Simulate(.02f); }
                Assert.That(Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude, Is.LessThan(.2f));
                body.isKinematic = true;
                Vector3 parked = body.position;
                stop.Invoke(motor, new object[] { configuration.DrivingSettings, true });
                for (int step = 0; step < 50; step++) physics.Simulate(.02f);
                Assert.That(Vector3.Distance(parked, body.position), Is.LessThan(.01f), "A secured parking brake must hold its position.");
                drive.Invoke(motor, new object[] { configuration.DrivingSettings, body.position + body.rotation * Vector3.forward * 10,
                    configuration.EscapeSpeed, 1f, .02f, true });
                Assert.That(body.isKinematic, Is.False, "Escaping must release the parking brake.");
            }
            finally
            {
                System.IO.File.WriteAllText("Temp/GetawayMotorTrace.txt", trace.ToString());
                if (truck != null) Object.DestroyImmediate(truck);
                if (ground != null) Object.DestroyImmediate(ground);
                if (contact != null) Object.DestroyImmediate(contact);
                if (networkContext != null) Object.DestroyImmediate(networkContext);
                if (prefabs != null) Object.DestroyImmediate(prefabs);
                if (Application.isPlaying) SceneManager.UnloadSceneAsync(scene);
                else EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
