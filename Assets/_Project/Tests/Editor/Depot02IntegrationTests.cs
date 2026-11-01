using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using CouchGuys.Gameplay.Depot;
using CouchGuys.ProceduralGeneration;
using CouchGuys.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace CouchGuys.Tests.Editor
{
    public sealed class Depot02IntegrationTests
    {
        static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
        static GameObject Create()=>Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CouchGuys.Editor.Depot02PrefabBuilder.PrefabPath));
        [TestCase(22884574,0f)]
        [TestCase(22884574,90f)]
        [TestCase(22884574,180f)]
        [TestCase(22884574,270f)]
        [TestCase(12345,0f)]
        [TestCase(87654321,0f)]
        public void DepotExitConnectsToEntireGeneratedRoadGraph(int seed,float yaw)
        {
            var region=AssetDatabase.LoadAssetAtPath<RegionDefinition>("Assets/_Project/Settings/Regions/Region_Suburbs.asset");
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var host=new GameObject("Depot02_RoadGraphTest");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);
            host.transform.SetPositionAndRotation(new Vector3(31,4,-82),Quaternion.Euler(0,yaw,0));
            try
            {
                var generator=host.AddComponent<NeighbourhoodGenerator>();
                var type=typeof(NeighbourhoodGenerator);
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                void Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(generator,args);
                Call("ApplyRegionConfiguration",region);Call("CreateGeneratedHierarchy");
                var area=(StartingArea)type.GetMethod("CreateStartingArea",flags).Invoke(generator,null);
                type.GetProperty("GeneratedStartingArea").SetValue(generator,area);
                type.GetProperty("CurrentSeed").SetValue(generator,seed);
                type.GetField("m_gridOrigin",flags).SetValue(generator,area.RoadConnection.position);
                var direction=type.GetMethod("ClosestDirection",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{area.RoadConnection.forward});
                type.GetField("m_startDirection",flags).SetValue(generator,direction);
                Call("ReserveStartingArea",area);
                int minimumX=-(region.Grid.GridWidth/2)*(region.Grid.BlockWidth+1);
                type.GetField("m_minimumLocalX",flags).SetValue(generator,minimumX);
                Call("AssignBlockElevations",new System.Random(seed));
                int layoutSeed=(int)type.GetMethod("DeriveSeed",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{seed,0x1874A2B1});
                Call("GenerateRoadLayout",new System.Random(layoutSeed));Call("CreateTrafficBoundaryExtensions");
                Call("CalculateRoadConnections");Call("ResolveRoadElevations");Call("FinaliseTrafficBoundaryConnections");
                var roads=(HashSet<GridCoordinate>)type.GetField("m_roadCells",flags).GetValue(generator);
                var seen=new HashSet<GridCoordinate>{GridCoordinate.Zero};var queue=new Queue<GridCoordinate>();queue.Enqueue(GridCoordinate.Zero);
                var offsets=new[]{new GridCoordinate(0,1),new GridCoordinate(1,0),new GridCoordinate(0,-1),new GridCoordinate(-1,0)};
                var connections=new[]{RoadConnections.North,RoadConnections.East,RoadConnections.South,RoadConnections.West};
                while(queue.Count>0)
                {
                    var current=queue.Dequeue();Assert.That(generator.TryGetRoadConnections(current,out var exits),Is.True);
                    for(int i=0;i<4;i++)
                    {
                        var next=current+offsets[i];
                        if((exits&connections[i])!=0&&roads.Contains(next)&&seen.Add(next))queue.Enqueue(next);
                    }
                }
                Assert.That(seen.Count,Is.EqualTo(roads.Count),$"Seed {seed}, heading {yaw}: {seen.Count}/{roads.Count} roads reachable from depot");
                File.AppendAllText("Temp/depot02-road-tests.txt",$"PASS seed {seed}, heading {yaw}: {seen.Count}/{roads.Count} roads reachable\n");
            }
            finally {Object.DestroyImmediate(host);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
        [Test]
        public void RuntimeRegionSelectionConnectsDepot02ToGeneratorAndDispatcher()
        {
            var region=AssetDatabase.LoadAssetAtPath<RegionDefinition>("Assets/_Project/Settings/Regions/Region_Suburbs.asset");
            Assert.That(AssetDatabase.GetAssetPath(region.StartingAreaPrefab),Is.EqualTo(CouchGuys.Editor.Depot02PrefabBuilder.PrefabPath));
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var host=new GameObject("Depot02_RuntimeSelectionTest");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);
            host.transform.SetPositionAndRotation(new Vector3(83,3,-27),Quaternion.Euler(0,67,0));
            try
            {
                var generator=host.AddComponent<NeighbourhoodGenerator>();
                var manager=host.AddComponent<CouchGuys.Gameplay.Delivery.DeliveryManager>();
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var type=typeof(NeighbourhoodGenerator);
                type.GetMethod("ApplyRegionConfiguration",flags).Invoke(generator,new object[]{region});
                type.GetMethod("CreateGeneratedHierarchy",flags).Invoke(generator,null);
                var area=(StartingArea)type.GetMethod("CreateStartingArea",flags).Invoke(generator,null);
                type.GetProperty("GeneratedStartingArea").SetValue(generator,area);
                Assert.That(area.Depot,Is.Not.Null);
                Assert.That(area.Depot.HasConveyor,Is.True);
                Assert.That(area.Depot.ConveyorPath.Count,Is.GreaterThan(20));
                Assert.That(area.PlayerSpawnPoints.All(p=>p!=null&&p.IsChildOf(area.transform)),Is.True);
                Assert.That(area.GetComponentsInChildren<Transform>().Any(t=>t.name=="D02_RollerSlat_00"),Is.True);
                Assert.That(Vector3.Dot(area.Depot.VehicleArrival.forward,area.RoadConnection.forward),Is.GreaterThan(.999f));
                type.GetMethod("CreateDeliveryNpc",flags).Invoke(generator,null);
                var npc=area.GetComponentInChildren<CouchGuys.Gameplay.Delivery.DeliveryNPC>();
                Assert.That(npc,Is.Not.Null);
                Assert.That(npc.transform.parent,Is.EqualTo(area.DeliveryNpcSpawnPoint));
                Assert.That(npc.CouchSpawnPosition,Is.EqualTo(area.Depot.CouchHiddenSpawn.position));
                Assert.That(npc.VehicleSpawnPosition,Is.EqualTo(area.Depot.VehicleArrival.position));
                var managerType=typeof(CouchGuys.Gameplay.Delivery.DeliveryManager);
                managerType.GetField("m_generator",flags).SetValue(manager,generator);
                managerType.GetMethod("ResolveDepot",flags).Invoke(manager,null);
                Assert.That(managerType.GetField("m_depot",flags).GetValue(manager),Is.SameAs(area.Depot));
                Assert.That(area.Depot.VehicleSpawnSurface,Is.Not.Null);
                Physics.SyncTransforms();
                object[] spawnArgs={npc,null,null};
                bool validSpawn=(bool)managerType.GetMethod("TryFindVehicleSpawnPose",flags).Invoke(manager,spawnArgs);
                Assert.That(validSpawn,Is.True,"Actual vehicle shop must find a valid indoor spawn");
                Vector3 vehiclePosition=(Vector3)spawnArgs[1];
                Assert.That(vehiclePosition.y,Is.EqualTo(area.transform.position.y+.12f).Within(.02f),"Truck must ground on the floor, not the roof");
                Assert.That(Vector3.Dot(((Quaternion)spawnArgs[2])*Vector3.forward,area.RoadConnection.forward),Is.GreaterThan(.999f));                var npcData=new SerializedObject(npc);
                Assert.That(npcData.FindProperty("m_deliveryManager").objectReferenceValue,Is.SameAs(manager));
                Assert.That(area.GetComponentsInChildren<MeshFilter>().All(f=>!AssetDatabase.GetAssetPath(f.sharedMesh).Contains("/Models/depot/")),Is.True);
            }
            finally {Object.DestroyImmediate(host);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
        }
        [Test]
        public void LayoutAndGameplayReferencesMatchNewPlan()
        {
            var root=Create();
            try
            {
                var depot=root.GetComponent<DepotController>();var area=root.GetComponent<StartingArea>();
                Assert.That(depot.HasConveyor,Is.True);Assert.That(depot.ConveyorPath.Count,Is.GreaterThan(20));
                Assert.That(area.PlayerSpawnPoints.Length,Is.EqualTo(4));Assert.That(area.Depot,Is.EqualTo(depot));
                Assert.That(depot.DeliveryNpcMarker,Is.Not.Null);
                Assert.That(depot.VehicleArrival.localPosition.x,Is.EqualTo(7.5f).Within(.01));
                Assert.That(Vector3.Dot(depot.VehicleArrival.forward,root.transform.forward),Is.GreaterThan(.999f));
                Assert.That(depot.ConveyorPath.All(t=>t.localPosition.x<-9),Is.True,"Conveyor belongs to left perimeter");
                Assert.That(root.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("D02_RollerSlat_")),Is.EqualTo(16));
                Assert.That(root.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("D02_GlassPanel_")),Is.EqualTo(2));
                Assert.That(root.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh!=null),Is.True);
                Assert.That(root.GetComponentsInChildren<MeshFilter>().All(f=>!AssetDatabase.GetAssetPath(f.sharedMesh).Contains("/Models/depot/")),Is.True,"No old depot geometry may be reused");
                Assert.That(root.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).All(m=>m!=null),Is.True);
                Assert.That(root.GetComponentsInChildren<LODGroup>().Length,Is.GreaterThanOrEqualTo(5));
                Physics.SyncTransforms();
                // No shell gaps on either side of the player opening.
                foreach(float x in new[]{-7f,-2f})
                    Assert.That(Physics.Raycast(new Vector3(x,1.7f,8),Vector3.forward,4,~0,QueryTriggerInteraction.Ignore),Is.True);
                // An unobstructed lane fits the largest truck (3.1m x 4m x 7.3m).
                var hits=Physics.OverlapBox(new Vector3(7.5f,2.12f,-3.5f),new Vector3(1.55f,2.02f,3.7f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
                Assert.That(hits.Where(c=>c.transform.IsChildOf(root.transform)),Is.Empty,"Truck spawn intersects depot");
            }
            finally {root.GetComponent<DepotController>().ReleaseDoorAnimations();Object.DestroyImmediate(root);}
        }
        [Test]
        public void BothDoorsAnimateWithCollisionAfterWorldRotation()
        {
            var root=Create();
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(70,2,-45),Quaternion.Euler(0,71,0));
                var depot=root.GetComponent<DepotController>();
                var glass=new[]{Find(root,"D02_GlassPanel_0"),Find(root,"D02_GlassPanel_1")};
                var roller=Enumerable.Range(0,16).Select(i=>Find(root,$"D02_RollerSlat_{i:00}")).ToArray();
                var parts=glass.Concat(roller).ToArray();var closed=parts.Select(t=>t.position).ToArray();
                depot.ApplyDoorStates(false,false,true);Physics.SyncTransforms();
                var ray=new Ray(root.transform.TransformPoint(new Vector3(-4.1f,1.7f,8)),root.transform.forward);
                Assert.That(glass.Any(t=>t.GetComponent<BoxCollider>().Raycast(ray,out _,4)),Is.True);
                depot.ApplyDoorStates(true,true,true);Physics.SyncTransforms();
                Assert.That(glass.Any(t=>t.GetComponent<BoxCollider>().Raycast(ray,out _,4)),Is.False);
                var playerHits=Physics.OverlapBox(root.transform.TransformPoint(new Vector3(-4.5f,1.2f,10)),new Vector3(1.4f,1.1f,.4f),root.transform.rotation,~0,QueryTriggerInteraction.Ignore);
                Assert.That(playerHits.Where(c=>c.transform.IsChildOf(root.transform)),Is.Empty,"2.8m clear glass passage");
                var truckHits=Physics.OverlapBox(root.transform.TransformPoint(new Vector3(7.5f,2.2f,10)),new Vector3(1.55f,2.05f,.4f),root.transform.rotation,~0,QueryTriggerInteraction.Ignore);
                Assert.That(truckHits.Where(c=>c.transform.IsChildOf(root.transform)),Is.Empty,"Open roller truck clearance");
                for(int i=0;i<parts.Length;i++)Assert.That(Vector3.Distance(closed[i],parts[i].position),Is.GreaterThan(.4f));
                Assert.That(parts.All(t=>t.GetComponent<BoxCollider>().enabled),Is.True,"Collision follows panels throughout travel");
                depot.ApplyDoorStates(false,true,true);
                Assert.That(roller.All(t=>t.position.y>root.transform.position.y+5.5f),Is.True,"Other door update must preserve roller pose");
                depot.ApplyDoorStates(false,false,true);Physics.SyncTransforms();
                for(int i=0;i<parts.Length;i++)Assert.That(Vector3.Distance(closed[i],parts[i].position),Is.LessThan(.003f));
            }
            finally {root.GetComponent<DepotController>().ReleaseDoorAnimations();Object.DestroyImmediate(root);}
        }
        [Test]
        public void AutomaticDoorDetectsPlayersAndClosesAfterDelay()
        {
            var root=Create();var player=new GameObject("D02_PlayerTest");
            try
            {
                player.AddComponent<PlayerCouchCarrier>();if(player.GetComponent<Collider>()==null)player.AddComponent<CapsuleCollider>();
                var depot=root.GetComponent<DepotController>();player.transform.position=new Vector3(-4.5f,.2f,8.8f);Physics.SyncTransforms();
                Assert.That(depot.ShouldOpenAutomaticEntrance(),Is.True);
                player.transform.position=Vector3.one*150;Physics.SyncTransforms();Assert.That(depot.ShouldOpenAutomaticEntrance(),Is.True);
                typeof(DepotController).GetField("m_entranceLastOccupied",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(depot,Time.time-10);
                Assert.That(depot.ShouldOpenAutomaticEntrance(),Is.False);
            }
            finally {Object.DestroyImmediate(player);root.GetComponent<DepotController>().ReleaseDoorAnimations();Object.DestroyImmediate(root);}
        }
        [Test]
        public void ConveyorCurvesSupportTheRouteAndAimAtTheMarkedDropZone()
        {
            var root=Create();
            try
            {
                var depot=root.GetComponent<DepotController>();
                var belt=root.GetComponentsInChildren<Collider>().Where(c=>c.name.StartsWith("D02_Collider_Conveyor")).ToArray();
                Assert.That(belt.Count(c=>c is MeshCollider),Is.EqualTo(2),"Curved sections need curved collision, not enclosing boxes");
                Transform previous=depot.CouchHiddenSpawn;
                foreach(var point in depot.ConveyorPath)
                {
                    Assert.That(Quaternion.Angle(previous.rotation,point.rotation),Is.LessThan(7f),"No abrupt 90-degree couch turns");
                    var ray=new Ray(point.position+Vector3.up,Vector3.down);
                    Assert.That(belt.Any(c=>c.Raycast(ray,out _,1.5f)),Is.True,point.name+" is not supported by the conveyor geometry");
                    previous=point;
                }
                var last=depot.ConveyorPath[depot.ConveyorPath.Count-1];
                Vector3 toward=depot.CouchDropOffPoint.position-last.position;toward.y=0;
                Assert.That(Vector3.Dot(toward.normalized,depot.ConveyorExitVelocity.normalized),Is.GreaterThan(.99f));
                Assert.That(depot.ConveyorExitVelocity.magnitude,Is.GreaterThan(1f));
                Assert.That(toward.magnitude,Is.GreaterThan(1f),"Pickup location must be off the belt");
            }
            finally {root.GetComponent<DepotController>().ReleaseDoorAnimations();Object.DestroyImmediate(root);}
        }
        [MenuItem("Couch Guys/Depot 02/Run Checks %#&v")]
        public static void Run()
        {
            try
            {
                var tests=new Depot02IntegrationTests();
                File.WriteAllText("Temp/depot02-road-tests.txt","");
                foreach(float yaw in new[]{0f,90f,180f,270f})tests.DepotExitConnectsToEntireGeneratedRoadGraph(22884574,yaw);
                tests.DepotExitConnectsToEntireGeneratedRoadGraph(12345,0);tests.DepotExitConnectsToEntireGeneratedRoadGraph(87654321,0);
                tests.ConveyorCurvesSupportTheRouteAndAimAtTheMarkedDropZone();tests.RuntimeRegionSelectionConnectsDepot02ToGeneratorAndDispatcher();tests.LayoutAndGameplayReferencesMatchNewPlan();tests.BothDoorsAnimateWithCollisionAfterWorldRotation();tests.AutomaticDoorDetectsPlayersAndClosesAfterDelay();
                File.WriteAllText("Temp/depot02-tests.txt","PASS: runtime region selection, generated depot_02, dispatcher/DeliveryManager and spawn/conveyor connections, new reference layout, one roller/one glass entrance, connected gameplay references, truck spawn and doorway clearance, rotated door motion/collision, automatic detection and close delay.");Debug.Log("Depot 02 integration checks passed");
            }
            catch(Exception e){File.WriteAllText("Temp/depot02-tests.txt",e.ToString());throw;}
        }
    }
}
