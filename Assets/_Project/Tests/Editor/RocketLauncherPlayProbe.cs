using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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
    // Registers a test callback only. No prefab authoring or gameplay changes run on reload.
    [InitializeOnLoad]
    public static class RocketLauncherPlayProbe
    {
        private const string Pending="RocketLauncherProbe.Pending";
        private const string Result="Temp/RocketLauncher/tests.txt";
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static IEnumerator s_routine;
        private static NetworkManager s_network;
        static RocketLauncherPlayProbe(){EditorApplication.playModeStateChanged+=StateChanged;}
        [MenuItem("Couch Guys/Weapons/Test Rocket Launcher %#&t")]
        public static void Run()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
            Directory.CreateDirectory("Temp/RocketLauncher");File.WriteAllText(Result,"START: rocket launcher targeted tests\n");
            try
            {
                var tests=new RocketLauncherTests();
                tests.SplashFallsOffAndStopsAtRadius(0,80);tests.SplashFallsOffAndStopsAtRadius(2.5f,40);
                tests.SplashFallsOffAndStopsAtRadius(5,0);tests.SplashFallsOffAndStopsAtRadius(7,0);
                tests.SavedPlayerUsesConfiguredRocketAndExistingAmmoArchitecture();
                tests.SavedPrefabsAreServerOwnedAndEffectsAreLocalAndBounded();
                tests.SweepFindsThinWallBetweenFramesAndClientSimulationDoesNothing();
                Log("PASS: falloff, range cutoff, configured defaults, saved network/VFX assets, thin-wall sweep, client simulation guard.");
            }
            catch(Exception e){Log("FAIL: "+e);throw;}
            SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false))
            {SessionState.SetBool(Pending,false);s_routine=Probe();EditorApplication.update+=Tick;}
            if(state==PlayModeStateChange.ExitingPlayMode){EditorApplication.update-=Tick;s_routine=null;}
        }
        private static void Tick()
        {
            try{if(s_routine!=null && !s_routine.MoveNext())Finish();}
            catch(Exception e){Log("FAIL: "+e);Finish();}
        }
        private static void Finish()
        {
            EditorApplication.update-=Tick;s_routine=null;
            if(s_network!=null){s_network.ClientManager.StopConnection();s_network.ServerManager.StopConnection(true);}
            EditorApplication.ExitPlaymode();
        }
        private static void Log(string message)=>File.AppendAllText(Result,message+"\n");
        private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        private static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Flags).Invoke(target,args);
        private static void Field(object target,string field,object value)=>target.GetType().GetField(field,Flags).SetValue(target,value);
        private static NetworkObject SpawnPlayer(Vector3 position)
        {
            var source=AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab");
            var player=Object.Instantiate(source,position,Quaternion.identity);
            var health=player.GetComponent<PlayerHealth>();Field(health,"m_maximumHealth",500f);
            s_network.ServerManager.Spawn(player);
            // No local input or bots in this controlled server test.
            player.GetComponent<ThirdPersonPlayerController>().enabled=false;
            return player;
        }
        private static IEnumerator Probe()
        {
            for(int i=0;i<SceneManager.sceneCount;i++)foreach(var root in SceneManager.GetSceneAt(i).GetRootGameObjects())root.SetActive(false);
            foreach(var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None))Object.DestroyImmediate(old.gameObject);
            var bootstrap=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"));
            s_network=bootstrap.GetComponent<NetworkManager>();
            var spawner=s_network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>();if(spawner!=null)Object.DestroyImmediate(spawner);
            yield return null;s_network.ServerManager.StartConnection();s_network.ClientManager.StartConnection();
            float deadline=Time.realtimeSinceStartup+12;
            while(s_network.ServerManager.Clients.Count==0 && Time.realtimeSinceStartup<deadline)yield return null;
            Check(s_network.ServerManager.Clients.Count>0,"Local FishNet host failed to connect.");
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(400,1,400);
            var shooter=SpawnPlayer(new Vector3(0,.05f,17));
            var direct=SpawnPlayer(new Vector3(0,.05f,20));
            var near=SpawnPlayer(new Vector3(2,.05f,20));
            var far=SpawnPlayer(new Vector3(8,.05f,20));
            for(int i=0;i<3;i++){var child=new GameObject("CompoundHurtbox");child.transform.SetParent(direct.transform,false);child.transform.localPosition=Vector3.up;child.AddComponent<BoxCollider>().size=Vector3.one*.7f;}
            yield return null;Physics.SyncTransforms();
            var controller=shooter.GetComponent<PlayerWeaponController>();
            var health=shooter.GetComponent<PlayerHealth>();var directHealth=direct.GetComponent<PlayerHealth>();
            var nearHealth=near.GetComponent<PlayerHealth>();var farHealth=far.GetComponent<PlayerHealth>();
            var weapon=((WeaponDefinition[])typeof(PlayerWeaponController).GetField("m_weapons",Flags).GetValue(controller))[4];
            Field(weapon,"m_knockback",0f);
            var rocket=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Projectile),new Vector3(0,1,20),Quaternion.identity).GetComponent<RocketProjectile>();
            rocket.InitializeServer(controller,health,weapon,~0);s_network.ServerManager.Spawn(rocket.NetworkObject);
            float initial=directHealth.CurrentHealth;float self=health.CurrentHealth;float nearby=nearHealth.CurrentHealth;float outside=farHealth.CurrentHealth;
            Call(rocket,"ExplodeServer",new Vector3(0,1,20),direct.GetComponent<Collider>());
            Check(Mathf.Abs(initial-directHealth.CurrentHealth-140)<.02f,"Direct hit must total exactly 60 + 80 once despite compound colliders.");
            Check(health.CurrentHealth<self,"Shooter did not receive splash damage.");
            Check(nearHealth.CurrentHealth<nearby && nearby-nearHealth.CurrentHealth<80,"Nearby target falloff incorrect.");
            Check(farHealth.CurrentHealth==outside,"Target beyond 5 metres took damage.");
            Log("PASS: authoritative blast applies exactly 140 direct damage once across compound colliders; self/friendly splash, falloff and outside-radius cutoff.");
            yield return null;
            var vfx=Object.FindObjectsByType<RocketExplosionVfx>(FindObjectsSortMode.None);
            Check(vfx.Length==1,"Host should play one local explosion, not duplicate RPC effects: "+vfx.Length);
            Log("PASS: host explosion event produced exactly one local VFX instance.");
            Check(controller.AcquireWeaponServer(PlayerWeaponType.RocketLauncher,0,3),"Acquire failed.");
            Check(controller.CurrentMagazine==0 && controller.CurrentReserve==3,"Pickup ammo was not retained.");
            Check((bool)Call(controller,"DropOwnedWeaponServer"),"Drop failed.");
            var pickup=Object.FindFirstObjectByType<WeaponPickup>();
            Check(pickup!=null,"No networked drop.");
            direct.transform.position=pickup.transform.position+Vector3.right*.8f;Physics.SyncTransforms();
            // Place the initial shooter away so the pickup line-of-sight remains clear.
            shooter.transform.position=new Vector3(-10,.05f,17);Physics.SyncTransforms();
            var other=direct.GetComponent<PlayerWeaponController>();
            Check(pickup.TryPickupServer(other),"Second player could not claim the dropped weapon.");
            Check(other.CurrentMagazine==0 && other.CurrentReserve==3,"Dropped ammo did not survive transfer.");
            Check(!pickup.TryPickupServer(controller),"Duplicate pickup claim succeeded.");
            Log("PASS: physical drop, second-player pickup, reserve/magazine transfer, duplicate-claim rejection.");
            Call(other,"BeginReloadServer");
            float until=Time.time+2.7f;while(Time.time<until)yield return null;
            Check(other.CurrentMagazine==1 && other.CurrentReserve==2,"Reload transfer incorrect.");
            Log("PASS: reload completed through existing server ammo state (1 loaded, 2 reserve).");
            // Fire through the normal server weapon path at a distant thin wall.
            direct.transform.position=new Vector3(0,.05f,40);Physics.SyncTransforms();
            Field(other,"m_switchCompleteAt",0f);
            Vector3 muzzle=(Vector3)Call(other,"GetMuzzlePosition",4,Vector3.up);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=muzzle+Vector3.forward*5;wall.transform.localScale=new Vector3(3,3,.02f);Physics.SyncTransforms();
            Check((bool)Call(other,"TryFireServer",muzzle,Vector3.forward),"Normal firing path rejected loaded launcher.");
            Check(other.CurrentMagazine==0,"Shot did not spend ammo.");
            Check(!(bool)Call(other,"TryFireServer",muzzle,Vector3.forward),"Cooldown accepted duplicate fire.");
            until=Time.time+.6f;while(Time.time<until)yield return null;
            Check(Object.FindObjectsByType<RocketProjectile>(FindObjectsSortMode.None).Length==0,"Rocket failed to hit thin wall.");
            Log("PASS: normal firing spends one rocket, enforces cooldown and collides with a thin wall.");
            var timed=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Projectile),new Vector3(80,10,0),Quaternion.identity).GetComponent<RocketProjectile>();
            timed.InitializeServer(other,directHealth,weapon,~0);s_network.ServerManager.Spawn(timed.NetworkObject);
            // Shooter-to-muzzle obstruction is irrelevant for an isolated lifetime probe.
            Field(timed,"m_firstStep",false);
            until=Time.time+6.3f;while(Time.time<until)yield return null;
            Check(timed==null || !timed.IsSpawned,"Six-second lifetime did not detonate the rocket.");
            until=Time.time+1.05f;while(Time.time<until)yield return null;
            Check(Object.FindObjectsByType<RocketExplosionVfx>(FindObjectsSortMode.None).Length==0,"Explosion did not clear within a second.");
            Log("PASS: six-second rocket expiry and local VFX pool return.\nALL TARGETED CHECKS PASSED.");
        }
    }
}
