using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Delivery;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace CouchGuys.Tests.Editor
{
    // Runs only after the explicit menu command. Play mode restores the user's edit-mode scene.
    [InitializeOnLoad]
    public static class DepotConveyorPlayProbe
    {
        const string Pending="DepotConveyorPlayProbe.Pending";
        const string Result="Temp/depot-conveyor-play-probe.txt";
        static IEnumerator routine;
        static NetworkManager network;
        static DepotConveyorPlayProbe(){EditorApplication.playModeStateChanged+=StateChanged;}
        [MenuItem("Couch Guys/Depot 02/Test Conveyor On Local Host %#&F11")]
        public static void Run()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Run this check outside an existing play session.");
            File.WriteAllText(Result,"START: local FishNet host conveyor test\n");
            SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
        }
        static void StateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);routine=Probe();EditorApplication.update+=Tick;
            }
            if(state==PlayModeStateChange.ExitingPlayMode){EditorApplication.update-=Tick;routine=null;}
        }
        static void Tick()
        {
            try {if(routine!=null&&!routine.MoveNext())Finish();}
            catch(Exception e){File.AppendAllText(Result,"FAIL: "+e+"\n");Finish();}
        }
        static void Finish()
        {
            EditorApplication.update-=Tick;routine=null;
            if(network!=null){network.ClientManager.StopConnection();network.ServerManager.StopConnection(true);}
            EditorApplication.ExitPlaymode();
        }
        static IEnumerator Probe()
        {
            // Isolate the test from the menu, Steam transport, and current level.
            for(int i=0;i<SceneManager.sceneCount;i++)
                foreach(var root in SceneManager.GetSceneAt(i).GetRootGameObjects())root.SetActive(false);
            foreach(var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None))Object.DestroyImmediate(old.gameObject);
            var bootstrap=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("64e03b333ee845f5bff4d89fa9a228c4")));
            network=bootstrap.GetComponentInChildren<NetworkManager>();
            var spawner=network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>();if(spawner!=null)Object.DestroyImmediate(spawner);
            yield return null;
            if(!(network.ServerManager.Started||network.ServerManager.StartConnection())||!(network.ClientManager.Started||network.ClientManager.StartConnection()))throw new Exception("Local host could not start");
            float deadline=Time.realtimeSinceStartup+10;
            while(network.ServerManager.Clients.Count==0&&Time.realtimeSinceStartup<deadline)yield return null;
            if(network.ServerManager.Clients.Count==0)throw new Exception("Local client did not connect");
            var depot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CouchGuys.Editor.Depot02PrefabBuilder.PrefabPath)).GetComponent<DepotController>();
            var couch=Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Couch.prefab"),depot.CouchHiddenSpawn.position,depot.CouchHiddenSpawn.rotation);
            network.ServerManager.Spawn(couch);
            var controller=couch.GetComponent<CouchCarryController>();controller.SelectRandomModelVariantServer();controller.SetDepotTransportActiveServer(true);
            var body=controller.CouchRigidbody;
            File.AppendAllText(Result,$"Spawn: body={body.position:F3}, kinematic={body.isKinematic}, pickup={controller.IsPickupAvailable}\n");
            float start=Time.time;
            while(Time.time-start<depot.ArrivalDelay)yield return null;
            File.AppendAllText(Result,$"After arrival delay: body={body.position:F3}, kinematic={body.isKinematic}, host={controller.IsClientInitialized&&controller.IsServerInitialized}\n");
            if (!body.isKinematic)throw new Exception("Host initialization released conveyor physics before arrival");
            var method=typeof(DeliveryManager).GetMethod("MoveCouchAlongConveyor",BindingFlags.Static|BindingFlags.NonPublic);
            var motion=(IEnumerator)method.Invoke(null,new object[]{body,depot.ConveyorPath,depot.ConveyorSpeed});
            float fixedTime=Time.fixedTime;float lastLog=0;
            deadline=Time.realtimeSinceStartup+25;
            while(true)
            {
                while(Time.fixedTime<=fixedTime&&Time.realtimeSinceStartup<deadline)yield return null;
                if(Time.realtimeSinceStartup>=deadline)throw new Exception($"Conveyor stalled: body={body.position:F3}, transform={body.transform.position:F3}, kinematic={body.isKinematic}");
                fixedTime=Time.fixedTime;
                if (!body.isKinematic)throw new Exception("Conveyor lost kinematic ownership during transfer");
                if(!motion.MoveNext())break;
                if(Time.time-lastLog>1)
                {lastLog=Time.time;File.AppendAllText(Result,$"Motion: body={body.position:F3}, transform={body.transform.position:F3}, kinematic={body.isKinematic}\n");}
                yield return null;
            }
            controller.ReleaseDepotTransportServer(depot.ConveyorExitVelocity);
            var last=depot.ConveyorPath[depot.ConveyorPath.Count-1];
            if(Vector3.Distance(body.position,last.position)>.04f)throw new Exception("Couch did not reach pickup point");
            if(!controller.IsPickupAvailable||body.isKinematic)throw new Exception("Couch did not return to pickup physics");
            if(controller.GetComponentsInChildren<Renderer>().Count(r=>r.enabled)==0)throw new Exception("Couch visual was not spawned");
            float released=Time.time;
            while(Time.time-released<3f)yield return null;
            var target=depot.CouchDropOffPoint;
            if(target!=null)
            {
                Vector3 horizontal=body.position-target.position;horizontal.y=0;
                if(horizontal.magnitude>1.6f)throw new Exception($"Couch missed marked drop zone: {body.position:F3}, target={target.position:F3}");
                var own=controller.GetComponent<BoxCollider>();
                if(own.bounds.min.y>target.position.y+.15f)throw new Exception("Couch did not settle on the floor");
                foreach(var belt in depot.GetComponentsInChildren<Collider>().Where(c=>c.name.StartsWith("D02_Collider_Conveyor")))
                    if(Physics.ComputePenetration(own,own.transform.position,own.transform.rotation,belt,belt.transform.position,belt.transform.rotation,out _,out float depth)&&depth>.01f)
                        throw new Exception("Couch still overlaps belt after discharge: "+belt.name);
                File.AppendAllText(Result,$"PASS: couch physically discharged into marked zone at {body.position:F3}, target offset={horizontal.magnitude:F2}m, floor contact and no conveyor overlap\n");
            }
            File.AppendAllText(Result,$"PASS: network-spawned couch traversed saved curved route, visible and pickup enabled; elapsed={Time.time-start:F2}s\n");
        }
    }
}
