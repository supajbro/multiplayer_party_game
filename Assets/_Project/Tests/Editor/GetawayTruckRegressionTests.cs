using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.ProceduralGeneration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class GetawayTruckRegressionTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static Type Runtime(string name) => typeof(GetawayTruckController).Assembly.GetType("CouchGuys.Gameplay.Vehicles." + name, true);

        [Test]
        public void ParkedTruckKeepsValidHandoffWithoutANewWalkingPath()
        {
            var settings = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            var instance = Object.Instantiate(settings.TruckPrefab.gameObject);
            var couchObject = new GameObject("Getaway loading arrival regression");
            var couch = couchObject.AddComponent<CouchCarryController>();
            var collider = couchObject.AddComponent<BoxCollider>();
            var bounds = new Bounds(Vector3.up * .45f, new Vector3(2.1f, .9f, .9f));
            collider.center = bounds.center; collider.size = bounds.size;
            typeof(CouchCarryController).GetField("m_cargoBounds", Hidden).SetValue(couch, bounds);
            var controller = instance.GetComponent<GetawayTruckController>();
            var truck = instance.GetComponent<TruckController>();
            Type type = typeof(GetawayTruckController);
            type.GetMethod("Awake", Hidden).Invoke(controller, null);
            type.GetField("m_couch", Hidden).SetValue(controller, couch);
            object state = type.GetField("m_state", Hidden).GetValue(controller);
            state.GetType().GetProperty("Value").SetValue(state, GetawayTruckState.Parked);
            var targetMethod = type.GetMethod("RearLoadingTarget", Hidden);
            var atEntrance = type.GetMethod("CouchAtLoadingEntrance", Hidden);
            var inRange = typeof(TruckController).GetMethod("IsCouchInLoadingRange", Hidden, null,
                new[] { typeof(CouchCarryController), typeof(float) }, null);
            try
            {
                instance.transform.position = new Vector3(37000, 0, 37000); // No NavMesh here.
                foreach (float slope in new[] { -12f, 0f, 12f })
                    foreach (float yaw in new[] { 0f, 45f, 90f, 135f })
                        foreach (float carryHeight in new[] { 0f, .4f, .8f })
                        {
                            instance.transform.rotation = Quaternion.Euler(slope, 0f, 0f);
                            couch.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                            Vector3 target = (Vector3)targetMethod.Invoke(controller, null);
                            Vector3 normal = instance.transform.up;
                            float roadY = -Vector3.Dot(normal, Vector3.ProjectOnPlane(target - instance.transform.position, Vector3.up)) / normal.y;
                            couch.transform.position = new Vector3(target.x, roadY + carryHeight, target.z);
                            Physics.SyncTransforms();
                            Assert.That((bool)inRange.Invoke(truck, new object[] { couch, settings.LoadingDistance }), Is.True,
                                $"Arrival must be inside actual loading range: slope={slope}, yaw={yaw}, height={carryHeight}.");
                            Assert.That((bool)atEntrance.Invoke(controller, new object[] { couch, couch.transform.position }), Is.True,
                                $"The destination truck must not block its own handoff: slope={slope}, yaw={yaw}, height={carryHeight}.");
                            type.GetMethod("RefreshCarryPath", Hidden).Invoke(controller, new object[] { couch, true });
                            Assert.That((bool)type.GetField("m_reachesRear", Hidden).GetValue(controller), Is.True,
                                "An arrived couch must not require another NavMesh path to keep its parked truck.");
                        }
                var wall = new GameObject("Blocked loading arrival");
                try
                {
                    instance.transform.rotation = Quaternion.identity;
                    couch.transform.rotation = Quaternion.identity;
                    Vector3 target = (Vector3)targetMethod.Invoke(controller, null);
                    couch.transform.position = new Vector3(target.x, 0f, target.z);
                    wall.transform.position = Vector3.Lerp(couch.transform.position, truck.CargoLoadingPoint.position, .5f) + Vector3.up;
                    wall.AddComponent<BoxCollider>().size = new Vector3(10f, 4f, .1f);
                    Physics.SyncTransforms();
                    Assert.That((bool)atEntrance.Invoke(controller, new object[] { couch, couch.transform.position }), Is.False,
                        "A genuinely obstructed loading entrance still cannot accept a couch.");
                }
                finally { Object.DestroyImmediate(wall); }
            }
            finally { Object.DestroyImmediate(instance); Object.DestroyImmediate(couchObject); }
        }

        [Test]
        public void ManagerConfigurationAndReplicatedMaximumHealthOverridePrefabDefaults()
        {
            var original = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            var settings = Object.Instantiate(original);
            var instance = Object.Instantiate(original.TruckPrefab.gameObject);
            try
            {
                settings.Health = 517f;
                settings.ApproachSpeed = 4f;
                settings.LoadingDistance = 4.25f;
                var controller = instance.GetComponent<GetawayTruckController>();
                Runtime("GetawayTruckController").GetMethod("ConfigureBeforeSpawn", Hidden).Invoke(controller, new object[] { settings });
                Assert.That(controller.MaximumHealth, Is.EqualTo(517f));
                Assert.That(Runtime("GetawayTruckController").GetField("m_settings", Hidden).GetValue(controller), Is.SameAs(settings));
                controller.OnStartServer();
                // A client retains its prefab asset reference but receives the server's maximum.
                Runtime("GetawayTruckController").GetField("m_settings", Hidden).SetValue(controller, original);
                Assert.That(controller.MaximumHealth, Is.EqualTo(517f));
            }
            finally { Object.DestroyImmediate(instance); Object.DestroyImmediate(settings); }
        }

        [Test]
        public void JockeyingAndReplanningDoNotRenewRouteProgress()
        {
            Type type = Runtime("GetawayRouteProgress");
            object tracker = Activator.CreateInstance(type, true);
            void Call(string name, params object[] args) => type.GetMethod(name, Hidden).Invoke(tracker, args);
            bool Expired(float time) => (bool)type.GetMethod("TimedOut", Hidden).Invoke(tracker, new object[] { time, 25f });
            Call("Reset", 0f);
            Call("Observe", 20f, 0f, 1f);
            Call("Observe", 16f, 1f, 1f);
            for (int time = 2; time <= 27; time++) Call("Observe", time % 2 == 0 ? 20f : 16f, (float)time, 1f);
            Assert.That(Expired(27f), Is.True, "Backing away and returning to the same point is not progress.");
            Call("Replan");
            Call("Observe", 14f, 28f, 1f);
            Assert.That(Expired(28f), Is.True, "Merely replacing a route cannot postpone recovery.");
            Call("Reset", 29f);
            Call("Observe", 20f, 29f, 1f);
            Call("Observe", 17f, 30f, 1f);
            Assert.That(Expired(31f), Is.False);
        }

        [Test]
        public void RoadBarrierBlocksDirectLinkAndRoutingUsesClearDetour()
        {
            Type type = Runtime("GetawayRoadRouter");
            object router = Activator.CreateInstance(type, true);
            var objects = new List<GameObject>();
            Vector3 origin = new Vector3(35000, 0, 35000);
            void Road(int x, int z)
            {
                var item = new GameObject("Barrier route test"); objects.Add(item);
                item.transform.position = origin + new Vector3(x * 20, 0, z * 20);
                var road = item.AddComponent<GeneratedRoad>();
                road.Initialise(new GridCoordinate(x, z), RoadConnections.North | RoadConnections.East | RoadConnections.South | RoadConnections.West);
                ((IDictionary)type.GetField("m_roads", Hidden).GetValue(router)).Add(road.Coordinate, road);
            }
            try
            {
                Road(0, 0); Road(1, 0); Road(0, 1); Road(1, 1);
                var vehicle = new GameObject("Clearance vehicle"); objects.Add(vehicle);
                vehicle.transform.position = origin;
                object template = router;
                router = type.GetMethod("ForVehicle", Hidden).Invoke(template,
                    new object[] { vehicle.transform, new Vector3(1.65f, 1.6f, 3.9f), 1.9f });
                var barrier = new GameObject("RoadClosed_test"); objects.Add(barrier);
                barrier.transform.position = origin + new Vector3(10, 1, 0);
                barrier.AddComponent<BoxCollider>().size = new Vector3(.8f, 2.2f, 12);
                Physics.SyncTransforms();
                object[] topologyOnly = { origin, new GridCoordinate(1, 0), null };
                type.GetMethod("TryRoute", Hidden).Invoke(template, topologyOnly);
                Assert.That(((List<Vector3>)topologyOnly[2]).Count, Is.EqualTo(2), "Vehicle filters must not mutate the shared road topology.");
                foreach (string method in new[] { "TryRoute", "TryForwardRoute" })
                {
                    object[] args = method == "TryRoute"
                        ? new object[] { origin, new GridCoordinate(1, 0), null }
                        : new object[] { origin, Vector3.forward, new GridCoordinate(1, 0), null };
                    Assert.That((bool)type.GetMethod(method, Hidden).Invoke(router, args), Is.True);
                    var path = (List<Vector3>)args[args.Length - 1];
                    Assert.That(path.Count, Is.EqualTo(4), "The barrier must remove the direct road link.");
                    Assert.That(path[1], Is.EqualTo(origin + Vector3.forward * 20));
                }
                barrier.transform.position = origin + new Vector3(0, 1, 10);
                Physics.SyncTransforms();
                type.GetMethod("RefreshClearance", Hidden).Invoke(router, null);
                object[] direct = { origin, new GridCoordinate(1, 0), null };
                Assert.That((bool)type.GetMethod("TryRoute", Hidden).Invoke(router, direct), Is.True);
                Assert.That(((List<Vector3>)direct[2]).Count, Is.EqualTo(2), "Replanning must refresh moved barriers.");
                // A narrow opening clear to a centre ray must still reject a truck-width sweep.
                barrier.transform.position = origin + new Vector3(10, 1, 2);
                barrier.GetComponent<BoxCollider>().size = new Vector3(.8f, 2.2f, 1);
                Physics.SyncTransforms();
                Assert.That((bool)type.GetMethod("SegmentClear", Hidden).Invoke(router,
                    new object[] { origin, origin + Vector3.right * 20 }), Is.False);
            }
            finally { foreach (var item in objects) Object.DestroyImmediate(item); }
        }

        [Test]
        public void EscapeRouteUsesForwardLoopInsteadOfImmediateReverse()
        {
            Type type = Runtime("GetawayRoadRouter");
            object router = Activator.CreateInstance(type, true);
            var objects = new List<GameObject>();
            void Road(int x, int z, RoadConnections connections)
            {
                var item = new GameObject("Escape heading road test"); objects.Add(item);
                item.transform.position = new Vector3(x * 10, 0, z * 10);
                var road = item.AddComponent<GeneratedRoad>(); road.Initialise(new GridCoordinate(x, z), connections);
                ((IDictionary)type.GetField("m_roads", Hidden).GetValue(router)).Add(road.Coordinate, road);
            }
            try
            {
                Road(0, 0, RoadConnections.North | RoadConnections.South | RoadConnections.East);
                Road(0, 1, RoadConnections.South | RoadConnections.East);
                Road(1, 1, RoadConnections.West | RoadConnections.South);
                Road(1, 0, RoadConnections.North | RoadConnections.West);
                Road(0, -1, RoadConnections.North);
                object[] args = { Vector3.zero, Vector3.forward, new GridCoordinate(0, -1), null };
                Assert.That((bool)type.GetMethod("TryForwardRoute", Hidden).Invoke(router, args), Is.True);
                var route = (List<Vector3>)args[3];
                Assert.That(route.Count, Is.GreaterThan(2));
                Assert.That(Vector3.Dot(route[1] - route[0], Vector3.forward), Is.GreaterThanOrEqualTo(0f));
                for (int i = 2; i < route.Count; i++)
                    Assert.That(Vector3.Dot((route[i] - route[i - 1]).normalized, (route[i - 1] - route[i - 2]).normalized), Is.GreaterThan(-.9f));
            }
            finally { foreach (var item in objects) Object.DestroyImmediate(item); }
        }

        [Test]
        public void WideCouchDetoursAroundPedestrianGapAndRejectsDisconnectedRear()
        {
            Vector3 origin = new Vector3(31000, 0, 31000);
            var settings = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            var couchObject = new GameObject("Couch clearance regression");
            couchObject.transform.position = origin;
            var couch = couchObject.AddComponent<CouchCarryController>();
            var objects = new List<GameObject>();
            NavMeshData data = null;
            NavMeshDataInstance mesh = default;
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 0,
                    transform = Matrix4x4.TRS(origin - Vector3.up * .1f, Quaternion.identity, Vector3.one), size = new Vector3(80, .2f, 80) }
            };
            void Wall(float x, float width)
            {
                var item = new GameObject("Couch clearance wall"); objects.Add(item);
                item.transform.position = origin + new Vector3(x, 2, 12);
                var collider = item.AddComponent<BoxCollider>(); collider.size = new Vector3(width, 4, 4);
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 1,
                    transform = item.transform.localToWorldMatrix, size = collider.size });
            }
            try
            {
                Wall(-10.375f, 19.25f); Wall(10.375f, 19.25f); // 1.5 m pedestrian gap; 2.1 m couch.
                data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), sources,
                    new Bounds(origin, new Vector3(100, 20, 100)), Vector3.zero, Quaternion.identity);
                mesh = NavMesh.AddNavMeshData(data);
                Physics.SyncTransforms();
                Type plannerType = Runtime("CouchClearancePath");
                object planner = Activator.CreateInstance(plannerType, true);
                object[] args = { couch, origin + Vector3.forward * 25, settings, 2f, false, null, false };
                Assert.That((bool)plannerType.GetMethod("TryBuild", Hidden).Invoke(planner, args), Is.True);
                Assert.That((bool)args[6], Is.True);
                bool around = false;
                foreach (Vector3 corner in (Vector3[])args[5]) around |= Mathf.Abs(corner.x - origin.x) > 20f;
                Assert.That(around, Is.True, "The group must go around the walls, rather than accepting their narrow gap.");
                mesh.Remove(); Object.DestroyImmediate(data); data = null;
                foreach (var item in objects) Object.DestroyImmediate(item); objects.Clear();
                sources.RemoveRange(1, sources.Count - 1);
                Wall(0, 80); // No route across the wall exists.
                data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), sources,
                    new Bounds(origin, new Vector3(100, 20, 100)), Vector3.zero, Quaternion.identity);
                mesh = NavMesh.AddNavMeshData(data); Physics.SyncTransforms();
                args = new object[] { couch, origin + Vector3.forward * 25, settings, 2f, false, null, false };
                Assert.That((bool)plannerType.GetMethod("TryBuild", Hidden).Invoke(planner, args), Is.False,
                    "A partial path must never approve an unreachable loading entrance.");
                args[4] = true;
                Assert.That((bool)plannerType.GetMethod("TryBuild", Hidden).Invoke(planner, args), Is.True,
                    "During approach, move to the closest point that has group clearance.");
                Assert.That((bool)args[6], Is.False);
                // Being close to the doorway is insufficient when a wall blocks the loading sweep.
                foreach (var item in objects) Object.DestroyImmediate(item); objects.Clear();
                var blockedDoor = new GameObject("Blocked cargo doorway"); objects.Add(blockedDoor);
                blockedDoor.transform.position = origin + new Vector3(0, 2, 27.3f);
                blockedDoor.AddComponent<BoxCollider>().size = new Vector3(10, 4, .2f);
                Physics.SyncTransforms();
                MethodInfo loading = plannerType.GetMethod("LoadingEntranceClear", Hidden);
                Assert.That((bool)loading.Invoke(planner, new object[] { couch, origin + Vector3.forward * 25,
                    origin + new Vector3(0, 1.32f, 27.8f) }), Is.False);
                Object.DestroyImmediate(blockedDoor); objects.Clear(); Physics.SyncTransforms();
                Assert.That((bool)loading.Invoke(planner, new object[] { couch, origin + Vector3.forward * 25,
                    origin + new Vector3(0, 1.32f, 27.8f) }), Is.True);
            }
            finally
            {
                if (mesh.valid) mesh.Remove(); if (data != null) Object.DestroyImmediate(data);
                foreach (var item in objects) Object.DestroyImmediate(item);
                Object.DestroyImmediate(couchObject);
            }
        }
    }
}
