using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using CouchGuys.Tests.Editor;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Reload registers test callbacks only; authoring is always an explicit command.
    [InitializeOnLoad]
    public static class BaseballBatPlayProbe
    {
        private const string Pending="BaseballBatProbe.Pending", Batch="BaseballBatProbe.Batch";
        private static string Result=>Path.Combine(BaseballBatAuthoring.OutputFolder,"play-tests.txt");
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static IEnumerator s_routine;
        private static NetworkManager s_network;
        private static bool s_failed;
        static BaseballBatPlayProbe(){EditorApplication.playModeStateChanged+=StateChanged;}

        [MenuItem("Couch Guys/Weapons/Test Baseball Bat")]
        public static void Run()
        {
            Directory.CreateDirectory(BaseballBatAuthoring.OutputFolder);File.WriteAllText(Result,"START: baseball bat tests\n");
            try
            {
                var tests=new BaseballBatTests();tests.AssetsSupportPhysicalNetworkPickupAndShop();
                tests.SwingRetainsTwoHandGripAndDoesNotAnimateTheHoverRing();
                tests.ThinWallBlocksBatLineOfSightButOwnColliderDoesNot();
                Log("PASS: configured assets, network pickup, shop, two-hand animation, hover mask and thin-wall occlusion.");
            }
            catch(Exception e){Log("FAIL: "+e);if(Application.isBatchMode)EditorApplication.Exit(1);throw;}
            SessionState.SetBool(Pending,true);SessionState.SetBool(Batch,Application.isBatchMode);
            EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false))
            {SessionState.SetBool(Pending,false);s_routine=Probe();EditorApplication.update+=Tick;}
            if(state==PlayModeStateChange.ExitingPlayMode){EditorApplication.update-=Tick;s_routine=null;}
            if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Batch,false))
            {SessionState.SetBool(Batch,false);EditorApplication.Exit(SessionState.GetBool("BaseballBatProbe.Failed",false)?1:0);}
        }
        private static void Tick()
        {
            try{if(s_routine!=null && !s_routine.MoveNext())Finish();}
            catch(Exception e){s_failed=true;Log("FAIL: "+e);Finish();}
        }
        private static void Finish()
        {
            EditorApplication.update-=Tick;s_routine=null;
            SessionState.SetBool("BaseballBatProbe.Failed",s_failed);
            if(s_network!=null){s_network.ClientManager.StopConnection();s_network.ServerManager.StopConnection(true);}
            EditorApplication.ExitPlaymode();
        }
        private static void Log(string value){File.AppendAllText(Result,value+"\n");Debug.Log("Bat test: "+value);}
        private static void Check(bool value,string message){if(!value)throw new Exception(message);}
        private static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Flags).Invoke(target,args);
        private static void Field(object target,string field,object value)=>target.GetType().GetField(field,Flags).SetValue(target,value);
        private static T Spawn<T>(string path,Vector3 position,bool owned=false) where T:Component
        {
            var obj=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),position,Quaternion.identity);
            s_network.ServerManager.Spawn(obj,owned?s_network.ServerManager.Clients.Values.First():null);
            if(obj.TryGetComponent(out CouchGuys.Input.PlayerInputReader input))input.enabled=false;
            if(obj.TryGetComponent(out ThirdPersonPlayerController motor))motor.enabled=false;
            foreach(var listener in obj.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
            return obj.GetComponent<T>();
        }
        private static IEnumerator Probe()
        {
            for(int i=0;i<SceneManager.sceneCount;i++)foreach(var root in SceneManager.GetSceneAt(i).GetRootGameObjects())root.SetActive(false);
            foreach(var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None))Object.DestroyImmediate(old.gameObject);
            var bootstrap=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"));
            s_network=bootstrap.GetComponent<NetworkManager>();
            var spawner=s_network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>();if(spawner!=null)Object.DestroyImmediate(spawner);
            yield return null;s_network.ServerManager.StartConnection();s_network.ClientManager.StartConnection();
            float timeout=Time.realtimeSinceStartup+15;
            while(s_network.ServerManager.Clients.Count==0 && Time.realtimeSinceStartup<timeout)yield return null;
            Check(s_network.ServerManager.Clients.Count>0,"FishNet host did not connect.");
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(1000,1,1000);
            const string playerPath="Assets/_Project/Prefabs/Player.prefab";
            var shooter=Spawn<PlayerWeaponController>(playerPath,new Vector3(0,.05f,0),true);
            var victim=Spawn<PlayerHealth>(playerPath,new Vector3(0,.05f,1.2f),true);
            var blocked=Spawn<PlayerHealth>(playerPath,new Vector3(-1.1f,.05f,.9f));
            var outside=Spawn<PlayerHealth>(playerPath,new Vector3(0,.05f,3.5f));
            for(int i=0;i<3;i++)
            {var child=new GameObject("BatCompoundHurtbox");child.transform.SetParent(victim.transform,false);child.transform.localPosition=Vector3.up;child.AddComponent<BoxCollider>().size=Vector3.one*.65f;}
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(-.6f,1,.5f);
            wall.transform.localScale=new Vector3(1.1f,2,.04f);wall.transform.rotation=Quaternion.Euler(0,50,0);
            yield return null;Physics.SyncTransforms();
            Check(shooter.AcquireWeaponServer(PlayerWeaponType.BaseballBat),"Could not acquire bat.");
            float ownerDeadline=Time.realtimeSinceStartup+5;
            while((!shooter.IsClientInitialized || !shooter.IsOwner) && Time.realtimeSinceStartup<ownerDeadline)yield return null;
            Check(shooter.IsClientInitialized && shooter.IsOwner,"Test shooter ownership was not initialized.");
            // Exercise the actual debug-menu RPC rather than only validating its enum mapping.
            shooter.GetComponent<CouchGuys.Networking.NetworkPlayerOwnership>().DebugSpawnServerRpc(6);
            float debugUntil=Time.time+.3f;while(Time.time<debugUntil)yield return null;
            var debugPickup=Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None).FirstOrDefault(p=>p.DisplayName=="Baseball Bat");
            Check(debugPickup!=null,"Debug menu did not spawn a networked baseball bat.");
            s_network.ServerManager.Despawn(debugPickup.NetworkObject);
            Log("PASS: debug-menu RPC spawns the physical bat pickup.");
            var settings=(BaseballBatAssets)typeof(PlayerWeaponController).GetField("m_baseballBatAssets",Flags).GetValue(shooter);
            // Clone settings for this test; saved tuning is never changed by probes.
            settings=Object.Instantiate(settings);Field(settings,"m_playerKnockback",0f);Field(settings,"m_playerLift",0f);
            Field(shooter,"m_baseballBatAssets",settings);Field(shooter,"m_switchCompleteAt",0f);
            float initial=victim.CurrentHealth,behind=blocked.CurrentHealth,far=outside.CurrentHealth,self=shooter.GetComponent<PlayerHealth>().CurrentHealth;
            Check((bool)Call(shooter,"TryBeginBatSwingServer"),"First swing rejected.");
            Check(!(bool)Call(shooter,"TryBeginBatSwingServer"),"Overlapping swing accepted.");
            float started=Time.time;
            Field(shooter,"m_batStartedAt",Time.time-.15f);Call(shooter,"UpdateBatSwingServer");
            Check(victim.CurrentHealth==initial,"Hit before active swing frames.");
            Field(shooter,"m_batStartedAt",started);
            float until=started+.9f;while(Time.time<until)yield return null;
            Check(Mathf.Abs(initial-victim.CurrentHealth-5)<.01f,"Compound victim did not receive exactly one 5 HP hit.");
            Check(blocked.CurrentHealth==behind,"Bat hit through a thin wall.");
            Check(outside.CurrentHealth==far,"Bat hit outside melee range.");
            Check(shooter.GetComponent<PlayerHealth>().CurrentHealth==self,"Bat hit its attacker.");
            Check(!(bool)Call(shooter,"TryBeginBatSwingServer"),"Cooldown ended when animation ended instead of after two seconds.");
            Check(!(bool)Call(shooter,"TryFireServer",Vector3.up,Vector3.forward),"Bat bypassed melee through gun RPC.");
            Call(shooter,"BeginReloadServer");Check(!shooter.IsReloading,"Bat started reloading.");
            var deadlines=(float[])typeof(PlayerWeaponController).GetField("m_nextServerFireTime",Flags).GetValue(shooter);
            Check(Mathf.Abs(deadlines[5]-started-2)<.04f,"Authoritative cooldown is not two seconds.");
            until=started+2.05f;while(Time.time<until)yield return null;
            Check((bool)Call(shooter,"TryBeginBatSwingServer"),"Swing was still blocked after two seconds.");
            Log("PASS: active-frame-only hits, one 5 HP hit across compound colliders, no self/out-of-range/wall hits, no reload/hitscan bypass, overlap rejection and two-second server cooldown.");

            Field(shooter,"m_batSwinging",false);
            Field(settings,"m_playerKnockback",24f);Field(settings,"m_playerLift",9f);
            Call(shooter,"ApplyBatHitServer",victim,Vector3.forward,victim.transform.position+Vector3.up);
            until=Time.time+.2f;while(Time.time<until)yield return null;
            var motor=victim.GetComponent<ThirdPersonPlayerController>();
            var velocity=(Vector3)typeof(ThirdPersonPlayerController).GetField("m_externalVelocity",Flags).GetValue(motor);
            Check(velocity.z>10,"Owning player did not receive launch impulse through TargetRpc.");
            float lockedUntil=(float)typeof(ThirdPersonPlayerController).GetField("m_vehicleImpactUntil",Flags).GetValue(motor);
            Check(lockedUntil>Time.time,"Initial bat launch did not lock movement input.");
            Log("PASS: owning-player launch impulse delivered through the existing FishNet TargetRpc/movement motor.");

            var car=Spawn<TrafficVehicleController>("Assets/_Project/Resources/Traffic/TrafficCar.prefab",new Vector3(20,.05f,0));
            var truck=Spawn<TruckController>("Assets/_Project/Prefabs/Vehicles/Truck.prefab",new Vector3(30,.05f,0));
            Call(shooter,"ApplyBatHitServer",car,Vector3.forward,car.transform.position);
            Call(shooter,"ApplyBatHitServer",truck,Vector3.forward,truck.transform.position);
            yield return null;yield return null;
            Check(car.GetComponent<Rigidbody>().linearVelocity.y>5 && truck.GetComponent<Rigidbody>().linearVelocity.y>5,"Mass-independent vehicle launch missing.");
            Check(!car.IsDestroyed && !truck.IsDestroyed,"Bat destroyed otherwise healthy vehicles.");
            Check(car.IsBatLaunched && truck.IsBatLaunched,"Vehicle driving did not yield during launch.");
            Log("PASS: car and truck receive mass-independent upward impulses while their motors yield without destroying them.");

            var ped=Spawn<PedestrianController>("Assets/_Project/Resources/Pedestrians/Pedestrian_01.prefab",new Vector3(40,.05f,0));
            Field(ped,"m_settings",AssetDatabase.LoadAssetAtPath<PedestrianSettings>("Assets/_Project/Resources/Pedestrians/PedestrianSettings.asset"));
            Field(ped,"m_initialised",true);Field(ped,"m_health",100f);
            Call(shooter,"ApplyBatHitServer",ped,Vector3.forward,ped.transform.position);
            yield return null;yield return null;
            var ragdoll=ped.GetComponentInChildren<PedestrianRagdoll>();
            Check(ped.IsAlive && ragdoll.IsFallen && ragdoll.Body.linearVelocity.y>3,"Living pedestrian was not launched into ragdoll.");
            var thief=Spawn<ThiefHealth>("Assets/_Project/Prefabs/Enemies/Thief.prefab",new Vector3(50,.05f,0));
            Call(shooter,"ApplyBatHitServer",thief,Vector3.forward,thief.transform.position);
            yield return null;yield return null;
            var thiefRagdoll=thief.GetComponentInChildren<PedestrianRagdoll>();
            Check(thief.IsAlive && thiefRagdoll.IsFallen && thiefRagdoll.Body.linearVelocity.y>3,"Living thief did not use airborne ragdoll launch.");
            Log("PASS: surviving pedestrian and thief launch through their shared bone ragdoll.");

            Object.DestroyImmediate(wall);
            shooter.transform.position=new Vector3(0,.05f,-5);Physics.SyncTransforms();
            Check((bool)Call(shooter,"DropOwnedWeaponServer"),"Bat drop failed.");
            var pickup=Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None).First(p=>p.DisplayName=="Baseball Bat");
            var recipient=outside.GetComponent<PlayerWeaponController>();
            outside.transform.position=pickup.transform.position+Vector3.right*.7f;
            shooter.transform.position+=Vector3.back*5;Physics.SyncTransforms();
            Check(pickup.TryPickupServer(recipient),"Another player could not pick up the dropped bat.");
            Check(recipient.EquippedWeapon==PlayerWeaponType.BaseballBat,"Picked-up bat did not equip.");
            Check(!pickup.TryPickupServer(shooter),"Duplicate pickup claim succeeded.");
            Log("PASS: networked drop, second-player pickup/equip and duplicate-claim rejection.");
            until=Time.time+8;while(Time.time<until && (car.IsBatLaunched || truck.IsBatLaunched || ragdoll.IsFallen))yield return null;
            Check(!car.IsBatLaunched && !truck.IsBatLaunched,"Vehicle motors did not recover after landing.");
            Check(!ragdoll.IsFallen && ped.IsAlive,"Living pedestrian did not recover after landing. Hips="+ragdoll.Body.position+
                " velocity="+ragdoll.Body.linearVelocity+" ground="+ragdoll.TryGround(out _));
            Log("PASS: vehicles and living pedestrian recover after landing.\nALL TARGETED CHECKS PASSED.");
        }
    }
}
