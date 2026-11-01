using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class DepotIntegrationTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/World/CouchGuysDepotV2.prefab";
        private GameObject Create() => Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        private static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
        private static void Dispose(GameObject root)
        {
            root.GetComponent<DepotController>().ReleaseDoorAnimations();
            Object.DestroyImmediate(root);
        }

        [Test]
        public void SavedDepotPreservesGameplayAndModularLods()
        {
            GameObject root = Create();
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(73,2,-41),Quaternion.Euler(0,93,0));
                var depot=root.GetComponent<DepotController>();
                Assert.That(depot.HasConveyor,Is.True);
                Assert.That(depot.ConveyorPath.Count,Is.EqualTo(5));
                Assert.That(depot.ConveyorPath.All(t=>t!=null),Is.True);
                Assert.That(depot.DeliveryNpcMarker,Is.Not.Null);
                Assert.That(depot.VehicleArrival,Is.EqualTo(Find(root,"TruckSpawnPoint")));
                Assert.That(Vector3.Dot(depot.VehicleArrival.forward,Find(root,"RoadConnection").forward),Is.GreaterThan(.999f));
                Assert.That(root.GetComponentsInChildren<Transform>().Any(t=>t.name.StartsWith("ExitRollerDoor_Slat")),Is.False);
                Assert.That(root.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh!=null),Is.True);
                Assert.That(root.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).All(m=>m!=null),Is.True);
                var lods=root.GetComponentInChildren<LODGroup>().GetLODs();
                Assert.That(lods.Length,Is.EqualTo(3));
                int[] triangles=lods.Select(l=>l.renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)).ToArray();
                Assert.That(triangles[1],Is.LessThan(triangles[0]));
                Assert.That(triangles[2],Is.LessThan(triangles[1]));
                Assert.That(lods.SelectMany(l=>l.renderers).All(r=>!r.name.Contains("GlassPanel")&&!r.name.Contains("_Slat_")),Is.True);
                Assert.That(Find(root,"Hub_ShelfTray").GetComponent<Renderer>(),Is.Not.Null,"Props must remain independently adjustable");
                var region=AssetDatabase.LoadAssetAtPath<RegionDefinition>("Assets/_Project/Settings/Regions/Region_Suburbs.asset");
                Assert.That(region.StartingAreaPrefab.GetComponent<StartingArea>().Depot,Is.Not.Null);
                Assert.That(DepotController.TryConfigureImportedModel(root,out _),Is.True);
            }
            finally { Dispose(root); }
        }

        [Test]
        public void DoorAnimationAndCollisionRemainCorrectAfterPlacement()
        {
            GameObject root=Create();
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(30,1,-20),Quaternion.Euler(0,67,0));
                var depot=root.GetComponent<DepotController>();
                var panels=new[]{Find(root,"Hub_GlassPanel_0"),Find(root,"Hub_GlassPanel_1")};
                var slats=Enumerable.Range(0,12).Select(i=>Find(root,$"TruckGarageDoor_Slat_{i:00}")).ToArray();
                var closed=panels.Concat(slats).Select(t=>t.position).ToArray();
                depot.ApplyDoorStates(false,false,true); Physics.SyncTransforms();
                var ray=new Ray(root.transform.TransformPoint(new Vector3(-6.55f,1.6f,8)),root.transform.forward);
                Assert.That(panels.Any(p=>p.GetComponent<BoxCollider>().Raycast(ray,out _,7)),Is.True);
                depot.ApplyDoorStates(true,true,true); Physics.SyncTransforms();
                Assert.That(panels.Any(p=>p.GetComponent<BoxCollider>().Raycast(ray,out _,7)),Is.False);
                foreach(var p in panels) Assert.That(p.GetComponent<BoxCollider>().enabled,Is.True);
                for(int i=0;i<14;i++)Assert.That(Vector3.Distance(closed[i],panels.Concat(slats).ElementAt(i).position),Is.GreaterThan(1.8f));
                // The largest truck's collision envelope must clear the complete open bay.
                var centre=root.transform.TransformPoint(new Vector3(7.2f,2.1f,11.4f));
                var blockers=Physics.OverlapBox(centre,new Vector3(1.55f,2.02f,.5f),root.transform.rotation,~0,QueryTriggerInteraction.Ignore);
                Assert.That(blockers.Where(c=>c.transform.IsChildOf(root.transform)),Is.Empty,"Open shutter clearance");
                depot.ApplyDoorStates(false,true,true);
                for(int i=0;i<12;i++)Assert.That(slats[i].position.y,Is.GreaterThan(root.transform.position.y+5.5f),"Glass update must not reset roller");
                depot.ApplyDoorStates(false,false,true);Physics.SyncTransforms();
                for(int i=0;i<14;i++)Assert.That(Vector3.Distance(closed[i],panels.Concat(slats).ElementAt(i).position),Is.LessThan(.002f));
            }
            finally { Dispose(root); }
        }

        [Test]
        public void AutomaticEntranceDetectsPlayersAndRetainsConfiguredDelay()
        {
            GameObject root=Create();
            GameObject player=new GameObject("DepotDetectionTestPlayer");
            try
            {
                player.AddComponent<PlayerCouchCarrier>();
                var collider=player.GetComponent<Collider>();
                if(collider==null)collider=player.AddComponent<CapsuleCollider>();
                var depot=root.GetComponent<DepotController>();
                Transform sensor=Find(root,"CGD_MARK_ExitDoor_SafetyVolume");
                player.transform.position=sensor.position+root.transform.forward*2;
                Physics.SyncTransforms();
                Assert.That(depot.ShouldOpenAutomaticEntrance(),Is.True);
                player.transform.position+=Vector3.right*100;Physics.SyncTransforms();
                Assert.That(depot.ShouldOpenAutomaticEntrance(),Is.True,"Closing delay must keep door open");
                typeof(DepotController).GetField("m_entranceLastOccupied",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(depot,Time.time-10);
                Assert.That(depot.ShouldOpenAutomaticEntrance(),Is.False);
            }
            finally { Object.DestroyImmediate(player); Dispose(root); }
        }

        [MenuItem("Couch Guys/Run Depot Checks %#&t")]
        public static void RunChecks()
        {
            var test=new DepotIntegrationTests();
            try
            {
                test.SavedDepotPreservesGameplayAndModularLods();
                test.DoorAnimationAndCollisionRemainCorrectAfterPlacement();
                test.AutomaticEntranceDetectsPlayersAndRetainsConfiguredDelay();
                File.WriteAllText("Temp/depot-tests.txt","PASS: gameplay references, modular LODs, transformed door motion/collision, truck clearance, automatic player detection and closing delay");
                Debug.Log("Depot integration tests passed.");
            }
            catch(Exception e) { File.WriteAllText("Temp/depot-tests.txt",e.ToString());throw; }
        }
    }
}
