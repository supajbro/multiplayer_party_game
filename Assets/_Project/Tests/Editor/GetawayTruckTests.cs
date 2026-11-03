using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class GetawayTruckTests
    {
        private readonly List<GameObject> m_objects = new();
        private static readonly Type Router = typeof(GetawayTruckController).Assembly.GetType(
            "CouchGuys.Gameplay.Vehicles.GetawayRoadRouter", true);
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in m_objects) Object.DestroyImmediate(item);
            m_objects.Clear();
        }

        [Test]
        public void RoutesFollowConnectedRoadsAroundBuildings()
        {
            object router = Activator.CreateInstance(Router, true);
            Road(router, 0, 0, RoadConnections.East);
            Road(router, 1, 0, RoadConnections.West | RoadConnections.North);
            Road(router, 1, 1, RoadConnections.South | RoadConnections.West);
            Road(router, 0, 1, RoadConnections.East);
            object[] arguments = { Vector3.zero, new GridCoordinate(0, 1), null };
            Assert.That((bool)Router.GetMethod("TryRoute", Hidden).Invoke(router, arguments), Is.True);
            var route = (List<Vector3>)arguments[2];
            Assert.That(route.Count, Is.EqualTo(4), "The truck must follow roads around the disconnected block edge.");
            Assert.That(route[1], Is.EqualTo(new Vector3(20, 0, 0)));
        }

        [Test]
        public void DisconnectedAndNonReciprocalRoadsCannotBecomeEscapeRoutes()
        {
            object router = Activator.CreateInstance(Router, true);
            Road(router, 0, 0, RoadConnections.East);
            Road(router, 1, 0, RoadConnections.East); // No west connection back to the source.
            object[] arguments = { Vector3.zero, new GridCoordinate(1, 0), null };
            Assert.That((bool)Router.GetMethod("TryRoute", Hidden).Invoke(router, arguments), Is.False);
            Assert.That(arguments[2], Is.Null);
        }

        [Test]
        public void MissedRoadWaypointDoesNotSendTruckBackwards()
        {
            Type follower = Router.Assembly.GetType("CouchGuys.Gameplay.Vehicles.GetawayRouteFollower", true);
            var route = new List<Vector3> { Vector3.zero, new Vector3(20, 0, 0), new Vector3(20, 0, 20) };
            MethodInfo reached = follower.GetMethod("ReachedWaypoint", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)reached.Invoke(null, new object[] { route, 1, new Vector3(22, 0, 1), 1.5f, 4f }), Is.True);
            Assert.That((bool)reached.Invoke(null, new object[] { route, 1, new Vector3(22, 0, 10), 1.5f, 4f }), Is.False,
                "A vehicle far from the road must not skip its route.");
            Assert.That((bool)reached.Invoke(null, new object[] { route, 2, new Vector3(20, 0, 25), 1.5f, 4f }), Is.False,
                "The final tunnel/parking position still needs to be reached.");
            MethodInfo steering = follower.GetMethod("SteeringTarget", BindingFlags.Static | BindingFlags.NonPublic);
            Vector3 target = (Vector3)steering.Invoke(null, new object[] { route, 1, new Vector3(18, 0, 0), 4f });
            Assert.That(target.x, Is.EqualTo(20f));
            Assert.That(target.z, Is.GreaterThan(0f), "Steering should anticipate the corner before reaching its centre.");
        }

        [Test]
        public void CarriersFollowActualTruckBeforeItParks()
        {
            var settings = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            GameObject instance = Object.Instantiate(settings.TruckPrefab.gameObject);
            m_objects.Add(instance);
            var controller = instance.GetComponent<GetawayTruckController>();
            typeof(GetawayTruckController).GetField("m_truck", Hidden).SetValue(controller, instance.GetComponent<TruckController>());
            var state = (SyncVar<GetawayTruckState>)typeof(GetawayTruckController).GetField("m_state", Hidden | BindingFlags.Public).GetValue(controller);
            MethodInfo destination = typeof(GetawayTruckController).GetMethod("TryGetCarryDestination", Hidden);
            object[] arguments = { Vector3.zero };
            foreach (var phase in new[] { GetawayTruckState.Waiting, GetawayTruckState.Arriving, GetawayTruckState.Approaching })
            {
                state.Value = phase;
                Assert.That((bool)destination.Invoke(controller, arguments), Is.True, phase.ToString());
                Assert.That((Vector3)arguments[0], Is.EqualTo(instance.transform.position));
                instance.transform.position += Vector3.forward * 10f;
                Assert.That((bool)destination.Invoke(controller, arguments), Is.True);
                Assert.That((Vector3)arguments[0], Is.EqualTo(instance.transform.position), "Target follows the actual truck.");
            }
        }
        [Test]
        public void SavedGetawayPrefabConstructsWithFishNetRpcStateIntact()
        {
            var settings = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            Assert.That(settings, Is.Not.Null);
            // Instantiate the real asset: asset-reference checks alone do not run its constructor.
            GameObject instance = Object.Instantiate(settings.TruckPrefab.gameObject);
            m_objects.Add(instance);
            var controller = instance.GetComponent<GetawayTruckController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(typeof(NetworkBehaviour).GetField("_observersRpcDelegates", Hidden).GetValue(controller),
                Is.Not.Null, "The component constructor must finish before FishNet registers its RPCs.");
        }

        [Test]
        public void SavedGetawayAssetsHaveCargoNetworkingAndBalanceSettings()
        {
            var settings = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.TruckPrefab, Is.Not.Null);
            string path = AssetDatabase.GetAssetPath(settings.TruckPrefab);
            Assert.That(path, Does.StartWith("Assets/_Project/Prefabs/Vehicles/"));
            var truck = settings.TruckPrefab.GetComponent<TruckController>();
            Assert.That(truck.GetComponent<GetawayTruckController>(), Is.Not.Null);
            Assert.That(truck.GetCargoPoint(0), Is.Not.Null);
            Assert.That(truck.CargoLoadingPoint, Is.Not.EqualTo(truck.transform));
            Assert.That(settings.Health, Is.GreaterThan(0));
            Assert.That(settings.DrivingSettings, Is.Not.Null);
            Assert.That(settings.DrivingSettings.MaxSpeed, Is.GreaterThanOrEqualTo(settings.EscapeSpeed));
            var obstacle = truck.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            Assert.That(obstacle, Is.Not.Null);
            Assert.That(obstacle.carving && obstacle.carveOnlyStationary, Is.True,
                "Carriers must navigate around the parked chassis to reach the rear.");
            var constraints = truck.GetComponent<Rigidbody>().constraints;
            Assert.That(constraints.HasFlag(RigidbodyConstraints.FreezeRotationX) &&
                constraints.HasFlag(RigidbodyConstraints.FreezeRotationZ), Is.True);
            Assert.That(settings.EscapeSpeed, Is.GreaterThan(settings.ApproachSpeed));
            var networkTransform = new SerializedObject(truck.GetComponent<FishNet.Component.Transforming.NetworkTransform>());
            Assert.That(networkTransform.FindProperty("_clientAuthoritative").boolValue, Is.False);
            var controller = new SerializedObject(truck.GetComponent<GetawayTruckController>());
            Assert.That(controller.FindProperty("m_settings").objectReferenceValue, Is.EqualTo(settings));
            Assert.That(controller.FindProperty("m_explosion").objectReferenceValue, Is.Not.Null);
            var network = new SerializedObject(settings.TruckPrefab);
            Assert.That(network.FindProperty("NetworkBehaviours").arraySize, Is.EqualTo(3));
            string guid = AssetDatabase.AssetPathToGUID(path);
            Assert.That(File.ReadAllText("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset"), Does.Contain(guid));
        }

        private void Road(object router, int x, int y, RoadConnections connections)
        {
            var item = new GameObject("Getaway road test");
            m_objects.Add(item);
            item.transform.position = new Vector3(x * 20, 0, y * 20);
            var road = item.AddComponent<GeneratedRoad>();
            road.Initialise(new GridCoordinate(x, y), connections);
            ((IDictionary)Router.GetField("m_roads", Hidden).GetValue(router)).Add(road.Coordinate, road);
        }

        [MenuItem("Couch Guys/Tests/Validate Getaway Truck %#&g")]
        public static void RunFocusedChecks()
        {
            var tests = new GetawayTruckTests();
            var cargo = new TruckCargoTests();
            try
            {
                tests.RoutesFollowConnectedRoadsAroundBuildings();
                tests.TearDown();
                tests.DisconnectedAndNonReciprocalRoadsCannotBecomeEscapeRoutes();
                tests.SavedGetawayAssetsHaveCargoNetworkingAndBalanceSettings();
                tests.SavedGetawayPrefabConstructsWithFishNetRpcStateIntact();
                tests.MissedRoadWaypointDoesNotSendTruckBackwards();
                tests.CarriersFollowActualTruckBeforeItParks();
                var regressions = new GetawayTruckRegressionTests();
                regressions.ManagerConfigurationAndReplicatedMaximumHealthOverridePrefabDefaults();
                regressions.JockeyingAndReplanningDoNotRenewRouteProgress();
                regressions.EscapeRouteUsesForwardLoopInsteadOfImmediateReverse();
                regressions.RoadBarrierBlocksDirectLinkAndRoutingUsesClearDetour();
                regressions.WideCouchDetoursAroundPedestrianGapAndRejectsDisconnectedRear();
                new GetawayTruckMovementTests().SharedCarryPathApproachesTruckAroundAnObstacle();
                cargo.SetUp();
                BindOfflineCargoNetworkObjects(cargo);
                cargo.SecuredCargoFollowsTruckWithoutPhysicsOrNetworkPoseWriters(true);
                cargo.TearDown();
                cargo.SetUp();
                BindOfflineCargoNetworkObjects(cargo);
                cargo.ObstructedRearSearchesNearbyAndSaturatedQueriesStayBlocked();
                File.WriteAllText("Temp/GetawayValidation.txt", "PASS EDIT: saved networking/cargo assets, configuration overrides and replicated maximum health, monotonic route progress, heading-compatible escape routing, group clearance through a narrow-gap detour, rejection of disconnected rear access, safe partial approach paths, secured cargo and recovery.\nSTART PLAY: truck physics, turns, and forward escape toward a close rearward target.\n");
                CouchGuys.Editor.GetawayTruckPhysicsProbe.Run();
            }
            catch (Exception error)
            {
                File.WriteAllText("Temp/GetawayValidation.txt", "FAIL: " + error);
                Debug.LogException(error);
            }
            finally { tests.TearDown(); cargo.TearDown(); }
        }

        private static void BindOfflineCargoNetworkObjects(TruckCargoTests fixture)
        {
            // AddComponent in Edit mode does not run FishNet's Awake cache binding.
            // Bind only the existing objects; this does not simulate server startup.
            foreach (string name in new[] { "m_truck", "m_couch" })
            {
                var component = (NetworkBehaviour)typeof(TruckCargoTests).GetField(name, Hidden).GetValue(fixture);
                typeof(NetworkBehaviour).GetField("_networkObjectCache", Hidden).SetValue(component,
                    component.GetComponent<NetworkObject>());
            }
        }
    }
}
