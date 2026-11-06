using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit FishNet host + independent loopback client checks. No test-scene asset.</summary>
    [InitializeOnLoad]
    public static class MagneticHoverboardValidation
    {
        private const string Pending="HoverboardValidation.Pending", Result="Logs/hoverboard-play-validation.txt";
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static IEnumerator s_probe;
        private static NetworkManager s_host,s_remote;
        private static bool s_failed;
        static MagneticHoverboardValidation() { EditorApplication.playModeStateChanged+=StateChanged; }
        [MenuItem("Couch Guys/Vehicles/Test Magnetic Hoverboard Networking")]
        public static void Run()
        {
            Directory.CreateDirectory("Logs"); File.WriteAllText(Result,"START: FishNet hoverboard integration\n");
            SessionState.SetBool("HoverboardValidation.Failed",false);
            SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false))
            { SessionState.SetBool(Pending,false); s_failed=false; s_probe=Probe(); EditorApplication.update+=Tick; }
            if(state==PlayModeStateChange.ExitingPlayMode) { EditorApplication.update-=Tick; s_probe=null; }
            if(state==PlayModeStateChange.EnteredEditMode && Application.isBatchMode)
                EditorApplication.Exit(SessionState.GetBool("HoverboardValidation.Failed",false)?1:0);
        }
        private static void Tick()
        {
            try
            {
                // Separate clients normally have separate Physics worlds. Keep all
                // loopback client colliders inert when exercising both in one process.
                if(s_remote!=null && s_remote.IsClientStarted)
                    foreach(var observed in s_remote.ClientManager.Objects.Spawned.Values)
                        foreach(var c in observed.GetComponentsInChildren<Collider>())c.enabled=false;
                if(s_probe!=null && !s_probe.MoveNext())Finish();
            }
            catch(Exception e) { s_failed=true; File.AppendAllText(Result,"FAIL: "+e+"\n"); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update-=Tick; s_probe=null;
            SessionState.SetBool("HoverboardValidation.Failed",s_failed);
            if(s_remote!=null)s_remote.ClientManager.StopConnection();
            if(s_host!=null) { s_host.ServerManager.StopConnection(false); s_host.ClientManager.StopConnection(); }
            EditorApplication.ExitPlaymode();
        }
        private static void Check(bool result,string message)
        { if(!result)throw new Exception(message); File.AppendAllText(Result,"PASS: "+message+"\n"); }
        private static void Request(PlayerHoverboardInteractor player,MagneticHoverboardController board,bool magnet)
        { typeof(PlayerHoverboardInteractor).GetMethod("RequestInteractionServerRpc",Private).Invoke(player,new object[]{board.NetworkObject,magnet}); }
        private static void Sync<T>(object target,string field,T value)
        { ((SyncVar<T>)target.GetType().GetField(field,Private).GetValue(target)).Value=value; }
        private static void AtLoadingSide(NetworkObject player,MagneticHoverboardController board,float side)
        {
            // Freeze the motor after ownership initialization, so its carry
            // attachment correction cannot undo the explicit test placement.
            player.GetComponent<ThirdPersonPlayerController>().enabled=false;
            var capsule=player.GetComponent<CharacterController>();
            Vector3 position=board.transform.TransformPoint(new Vector3(side*1.55f,0,-.85f));
            position.y=board.transform.position.y-board.Settings.HoverHeight+.08f-capsule.center.y+capsule.height*.5f;
            player.GetComponent<ThirdPersonPlayerController>().Teleport(position,Quaternion.identity);
            player.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
        }
        private static IEnumerator Probe()
        {
            Application.runInBackground=true;
            for(int i=0;i<SceneManager.sceneCount;i++)foreach(var root in SceneManager.GetSceneAt(i).GetRootGameObjects())root.SetActive(false);
            foreach(var network in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None))Object.DestroyImmediate(network.gameObject);
            Vector3 origin=new(20000,0,20000);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=origin+Vector3.down*.5f; floor.transform.localScale=new Vector3(500,1,500);
            var boot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"));
            s_host=boot.GetComponentInChildren<NetworkManager>();
            Object.DestroyImmediate(s_host.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>());
            yield return null;
            s_host.TransportManager.Transport.SetMaximumClients(4);
            s_host.TransportManager.Transport.SetPort(7789);
            s_host.ServerManager.StartConnection(); s_host.ClientManager.StartConnection();
            float deadline=Time.realtimeSinceStartup+20;
            while((!s_host.IsClientStarted || s_host.ServerManager.Clients.Count==0)&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(s_host.IsClientStarted && s_host.ServerManager.Clients.Count>0,"FishNet loopback host ready");
            var owner=s_host.ServerManager.Clients.Values.First(); s_host.SceneManager.AddConnectionToScene(owner,SceneManager.GetActiveScene());
            var boardObject=Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>(MagneticHoverboardAuthoring.PrefabPath),origin+Vector3.up*.42f,Quaternion.identity);
            s_host.ServerManager.Spawn(boardObject); var board=boardObject.GetComponent<MagneticHoverboardController>();
            var playerPrefab=AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab");
            var player=Object.Instantiate(playerPrefab,board.MountPosition+Vector3.right,Quaternion.identity); s_host.ServerManager.Spawn(player,owner);
            var rival=Object.Instantiate(playerPrefab,board.MountPosition+Vector3.left,Quaternion.identity); s_host.ServerManager.Spawn(rival,owner);
            foreach(var p in new[]{player,rival}) { p.GetComponent<ThirdPersonPlayerController>().enabled=false; p.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled=false; }
            deadline=Time.realtimeSinceStartup+10; while(!player.IsClientInitialized&&Time.realtimeSinceStartup<deadline)yield return null;
            float until=Time.time+1.2f; while(Time.time<until)yield return null;
            Check(board.transform.position.y>.2f && board.transform.position.y<.7f,"Hover spring settles above flat ground");
            var driver=player.GetComponent<PlayerHoverboardInteractor>(); var other=rival.GetComponent<PlayerHoverboardInteractor>();
            Request(driver,board,false); until=Time.time+.2f; while(Time.time<until)yield return null;
            Check(driver.CurrentBoard==board && board.Driver==player,"Owner mount RPC sets matching occupancy on player and board");
            Request(other,board,false); yield return null;
            Check(board.Driver==player && !other.IsMounted,"Competing driver rejected");
            Check(!player.GetComponent<CharacterController>().enabled && !player.GetComponent<PlayerWeaponController>().CanHandleWeaponServer,"Driver capsule, weapons and normal movement suppressed");
            var hitbox=player.transform.Find("HoverboardDriverHitbox").GetComponent<CapsuleCollider>();
            Check(hitbox.enabled && hitbox.GetComponentInParent<PlayerHealth>()==player.GetComponent<PlayerHealth>(),"Mounted driver retains a damage hitbox associated with their health");
            Vector3 start=board.transform.position;
            driver.enabled=false; // Test supplies motor input directly rather than reading a keyboard.
            board.SetInputServer(rival,Vector2.one); yield return null;
            Check(board.Speed<.3f,"Non-driver movement input rejected");
            until=Time.time+1.6f;
            while(Time.time<until) { board.SetInputServer(player,Vector2.up); yield return null; }
            Check(Vector3.Distance(start,board.transform.position)>4 && board.Speed<=board.Settings.MaximumForwardSpeed+1,"Authoritative acceleration and forward speed limit");
            until=Time.time+board.Speed/board.Settings.Deceleration+board.Settings.InputTimeout+1f; while(Time.time<until)yield return null;
            Check(board.Speed<.6f,"Input timeout safely brakes unattended vehicle");
            driver.enabled=true;
            driver.ReleaseServer(false); yield return null;
            Check(!driver.IsMounted && board.Driver==null && player.GetComponent<CharacterController>().enabled,"Dismount restores controls at clear ground beside vehicle");

            // Load an existing couch, preserving its objective identity.
            var couchObject=Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Couch.prefab"),board.MagnetPosition+Vector3.right*1.35f,Quaternion.identity);
            s_host.ServerManager.Spawn(couchObject); var couch=couchObject.GetComponent<CouchCarryController>(); couch.AssignDeliveryServer(731);
            AtLoadingSide(player,board,-1f);
            until=Time.time+.6f; while(Time.time<until)yield return null;
            var grab=typeof(CouchCarryController).GetMethod("TryGrabPointServer",Private,null,new[]{typeof(PlayerCouchCarrier),typeof(int)},null);
            Check((bool)grab.Invoke(couch,new object[]{rival.GetComponent<PlayerCouchCarrier>(),0}),"Existing couch carrying contract accepts an available carrier");
            Check(board.FindAvailableCouch()==null,"Carried couch excluded from magnet candidates");
            typeof(CouchCarryController).GetMethod("ReleaseAllOccupantsServer",Private).Invoke(couch,null);
            couch.SetDepotTransportActiveServer(true); Check(board.FindAvailableCouch()==null,"Depot-locked couch excluded from attachment"); couch.SetDepotTransportActiveServer(false);
            AtLoadingSide(player,board,-1f);
            player.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
            File.AppendAllText(Result,$"MAGNET diagnostic: interact={driver.CanInteract} speed={board.Speed} near={board.IsNearMagnet(driver.LoadingOrigin)} candidate={board.FindAvailableCouch()?.name} source={couch.transform.position} target={board.MagnetPosition} clearance={typeof(MagneticHoverboardController).GetMethod("HasMagnetClearance",Private).Invoke(board,new object[]{couch,null})}\n");
            Request(driver,board,true); until=Time.time+1f; while(Time.time<until)yield return null;
            Check(board.Couch==couch && couch.IsLoaded && !couch.IsPickupAvailable && couch.CouchRigidbody.isKinematic,"Magnet RPC attaches couch and disables conflicting physics / pickup");
            Check(!(bool)grab.Invoke(couch,new object[]{rival.GetComponent<PlayerCouchCarrier>(),0}),"Mounted couch rejects the existing carry contract");
            Check(couch.DeliveryId==731 && couch.transform.parent==board.CouchAnchor && Vector3.Distance(couch.transform.position,board.CouchAnchor.position)<.01f,"Attachment retains delivery ID and exact local anchor");
            player.GetComponent<ThirdPersonPlayerController>().Teleport(board.MountPosition+Vector3.right,Quaternion.identity);
            player.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
            Request(driver,board,false); until=Time.time+.2f; while(Time.time<until)yield return null;
            Check(driver.IsMounted,"Driver mounts a loaded board"); driver.enabled=false;
            until=Time.time+1.6f; while(Time.time<until) { board.SetInputServer(player,new Vector2(.15f,1)); yield return null; }
            Check(board.Speed<=board.Settings.LoadedSpeed+1 && Vector3.Distance(couch.transform.position,board.CouchAnchor.position)<.01f,"Loaded speed cap and rigid cargo pose while steering");
            until=Time.time+board.Speed/board.Settings.Deceleration+board.Settings.InputTimeout+1f;
            while(Time.time<until)yield return null; driver.enabled=true;
            driver.ReleaseServer(false); yield return null;
            // A joining client must reconstruct attachment from SyncVars, without an old RPC.
            var remoteRoot=new GameObject("Hoverboard loopback client"); remoteRoot.SetActive(false); s_remote=remoteRoot.AddComponent<NetworkManager>();
            typeof(NetworkManager).GetField("_persistence",Private).SetValue(s_remote,NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad",Private).SetValue(s_remote,false); s_remote.SpawnablePrefabs=s_host.SpawnablePrefabs;
            remoteRoot.SetActive(true); yield return null;
            s_remote.TransportManager.Transport.SetClientAddress("127.0.0.1"); s_remote.TransportManager.Transport.SetPort(7789); s_remote.ClientManager.StartConnection();
            deadline=Time.realtimeSinceStartup+15;
            while((!s_remote.IsClientStarted||s_host.ServerManager.Clients.Count<2)&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(s_remote.IsClientOnlyStarted&&s_host.ServerManager.Clients.Count==2,"Independent client joins existing session");
            var remoteOwner=s_host.ServerManager.Clients.Values.First(c=>c.ClientId!=owner.ClientId); s_host.SceneManager.AddConnectionToScene(remoteOwner,player.gameObject.scene);
            deadline=Time.realtimeSinceStartup+10;
            while((!s_remote.ClientManager.Objects.Spawned.ContainsKey(boardObject.ObjectId)||!s_remote.ClientManager.Objects.Spawned.ContainsKey(couchObject.ObjectId))&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(s_remote.ClientManager.Objects.Spawned.ContainsKey(boardObject.ObjectId)&&s_remote.ClientManager.Objects.Spawned.ContainsKey(couchObject.ObjectId),"Late client receives board and existing cargo");
            // Co-located client replica colliders must not affect the host's shared test Physics world.
            foreach(var observedReplica in s_remote.ClientManager.Objects.Spawned.Values)foreach(var c in observedReplica.GetComponentsInChildren<Collider>())c.enabled=false;
            var remoteBoard=s_remote.ClientManager.Objects.Spawned[boardObject.ObjectId].GetComponent<MagneticHoverboardController>();
            var remoteCouch=s_remote.ClientManager.Objects.Spawned[couchObject.ObjectId].GetComponent<CouchCarryController>();
            until=Time.time+.8f; while(Time.time<until)yield return null;
            Check(remoteBoard.Couch==remoteCouch && remoteCouch.IsLoaded && remoteCouch.transform.parent==remoteBoard.CouchAnchor,"Late client reconstructs mounted couch without network physics jitter");
            AtLoadingSide(player,board,-1f);
            player.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
            object[] exitArgs={couch,Vector3.zero,Quaternion.identity};
            File.AppendAllText(Result,$"UNLOAD diagnostic: near={board.IsNearMagnet(driver.LoadingOrigin)} interact={driver.CanInteract} speed={board.Speed} safe={typeof(MagneticHoverboardController).GetMethod("TryGetCouchExit",Private).Invoke(board,exitArgs)}\n");
            Request(driver,board,true); until=Time.time+.8f; while(Time.time<until)yield return null;
            Check(board.Couch==null&&!couch.IsLoaded&&couch.IsPickupAvailable&&!couch.CouchRigidbody.isKinematic&&couch.DeliveryId==731,"Unload restores physics and carry availability without changing delivery identity");
            Check(!remoteCouch.IsLoaded&&remoteCouch.transform.parent==null,"Remote client detaches cargo consistently");
            foreach(float side in new[]{-1f,1f})
            {
                AtLoadingSide(player,board,side);
                Check(board.IsNearMagnet(driver.LoadingOrigin),"Grounded player can reach side loading point: "+side);
                Vector3 source=board.transform.TransformPoint(new Vector3(side*2f,0,-.85f));
                source.y=.08f;
                typeof(CouchCarryController).GetMethod("TeleportServer",Private).Invoke(couch,new object[]{source,Quaternion.identity});
                int gripIndex=side<0?1:0;
                Check((bool)grab.Invoke(couch,new object[]{player.GetComponent<PlayerCouchCarrier>(),gripIndex}),"Player grips couch at side "+side);
                if(side<0)
                {
                    AtLoadingSide(rival,board,side);
                    Check((bool)grab.Invoke(couch,new object[]{rival.GetComponent<PlayerCouchCarrier>(),gripIndex+2}),"Second carrier shares couch");
                    Request(driver,board,true); until=Time.time+.2f; while(Time.time<until)yield return null;
                    Check(board.Couch==null&&couch.ActiveCarrierCount==2,"Loading cannot steal a couch from another carrier");
                    typeof(CouchCarryController).GetMethod("ReleasePointServer",Private,null,new[]{typeof(int),typeof(PlayerCouchCarrier)},null)
                        .Invoke(couch,new object[]{gripIndex+2,rival.GetComponent<PlayerCouchCarrier>()});
                    rival.GetComponent<ThirdPersonPlayerController>().Teleport(board.transform.position+Vector3.right*8,Quaternion.identity);
                    rival.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
                    var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.transform.position=board.CouchAnchor.position+Vector3.up*.45f;
                    blocker.transform.localScale=Vector3.one*.3f;
                    until=Time.time+.5f; while(Time.time<until)yield return null;
                    AtLoadingSide(player,board,side); Request(driver,board,true);
                    until=Time.time+.2f; while(Time.time<until)yield return null;
                    Check(board.Couch==null&&player.GetComponent<PlayerCouchCarrier>().IsCarrying,"Blocked loading keeps the player's couch grip");
                    Object.DestroyImmediate(blocker);
                    until=Time.time+.5f; while(Time.time<until)yield return null;
                }
                AtLoadingSide(player,board,side);
                Check(driver.HasPriorityWorldInteraction(),"Side loading takes E priority over ordinary couch release: "+side);
                Request(driver,board,true); until=Time.time+.8f; while(Time.time<until)yield return null;
                File.AppendAllText(Result,$"SIDE RESULT {side}: feedback={typeof(PlayerHoverboardInteractor).GetField("m_feedback",Private).GetValue(driver)} loaded={couch.IsLoaded} grips={couch.ActiveCarrierCount} held={player.GetComponent<PlayerCouchCarrier>().IsCarrying} speed={board.Speed} near={board.IsNearMagnet(driver.LoadingOrigin)} range={board.IsCouchInLoadingRange(couch)}\n");
                Check(board.Couch==couch&&!player.GetComponent<PlayerCouchCarrier>().IsCarrying&&couch.DeliveryId==731,"Carried couch loads from side "+side);
                Check(remoteBoard.Couch==remoteCouch&&remoteCouch.IsLoaded,"Side loading replicates to remote client: "+side);
                AtLoadingSide(player,board,side); Request(driver,board,true);
                until=Time.time+.8f; while(Time.time<until)yield return null;
                Check(board.Couch==null&&couch.IsPickupAvailable,"Side point can release couch again: "+side);
            }
            player.GetComponent<ThirdPersonPlayerController>().Teleport(board.MountPosition+Vector3.right,Quaternion.identity);
            player.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
            until=Time.time+.5f; while(Time.time<until)yield return null;
            Request(driver,board,false); until=Time.time+.2f; while(Time.time<until)yield return null;
            Check(driver.IsMounted,"Driver can remount after unload");
            Sync(player.GetComponent<PlayerHealth>(),"m_currentHealth",0f);
            until=Time.time+.2f; while(Time.time<until)yield return null;
            Check(board.Driver==null&&!driver.IsMounted,"Driver death releases occupancy");
            // Test slope alignment with the real hover probes and motor.
            var ramp=GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.transform.position=origin+new Vector3(35,1.2f,0);
            ramp.transform.localScale=new Vector3(8,.5f,14); ramp.transform.rotation=Quaternion.Euler(12,0,0);
            var body=board.GetComponent<Rigidbody>(); body.position=origin+new Vector3(35,1.9f,0); body.rotation=Quaternion.identity;
            body.linearVelocity=Vector3.zero; board.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
            until=Time.time+1.5f; while(Time.time<until)yield return null;
            Check(Vector3.Dot(board.transform.up,ramp.transform.up)>.98f,"Hover motor follows a 12-degree slope without wheels");
            player.GetComponent<ThirdPersonPlayerController>().Teleport(origin+Vector3.left*50,Quaternion.identity);
            player.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
            var remotePlayer=Object.Instantiate(playerPrefab,board.MountPosition+Vector3.right,Quaternion.identity);
            s_host.ServerManager.Spawn(remotePlayer,remoteOwner); remotePlayer.GetComponent<ThirdPersonPlayerController>().enabled=false;
            deadline=Time.realtimeSinceStartup+8;
            while(!s_remote.ClientManager.Objects.Spawned.ContainsKey(remotePlayer.ObjectId)&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(s_remote.ClientManager.Objects.Spawned.ContainsKey(remotePlayer.ObjectId),"Remote-owned player spawned");
            var replica=s_remote.ClientManager.Objects.Spawned[remotePlayer.ObjectId];
            replica.GetComponent<ThirdPersonPlayerController>().enabled=false; replica.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled=false;
            until=Time.time+.6f; while(Time.time<until)yield return null;
            // Align both authority and owner at the driver's point on the slope.
            remotePlayer.GetComponent<ThirdPersonPlayerController>().Teleport(board.MountPosition+Vector3.right,Quaternion.identity);
            replica.GetComponent<ThirdPersonPlayerController>().Teleport(board.MountPosition+Vector3.right,Quaternion.identity);
            replica.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
            Request(replica.GetComponent<PlayerHoverboardInteractor>(),remoteBoard,false);
            until=Time.time+.8f; while(Time.time<until)yield return null;
            Check(board.Driver==remotePlayer && replica.GetComponent<PlayerHoverboardInteractor>().IsMounted,"Independent remote owner mounts through an actual ServerRpc");
            float expectedHealth = board.Health - board.Settings.BulletDamage(100f);
            board.ApplyBulletDamageServer(100f);
            until=Time.time+.5f; while(Time.time<until)yield return null;
            Check(Mathf.Abs(board.Health-expectedHealth)<.01f && Mathf.Abs(remoteBoard.Health-expectedHealth)<.01f,
                "Server damage uses half truck scaling and health replicates to an independent client");
            s_remote.ClientManager.StopConnection(); until=Time.time+1f; while(Time.time<until)yield return null;
            Check(board.Driver==null,"Remote driver disconnect releases vehicle");
            rival.GetComponent<ThirdPersonPlayerController>().Teleport(board.MountPosition+Vector3.right,Quaternion.identity);
            rival.GetComponent<FishNet.Component.Transforming.NetworkTransform>().Teleport();
            Request(other,board,false); until=Time.time+.5f; while(Time.time<until)yield return null;
            Check(board.Driver==rival,"Living host player mounts before destruction test");
            Check((bool)typeof(CouchCarryController).GetMethod("MountOnHoverboardServer",Private)
                .Invoke(couch,new object[]{board,null}),"Destruction fixture attaches the original delivery couch");
            Sync(board,"m_couch",couchObject);
            board.ApplyBulletDamageServer(board.Settings.MaximumHealth / Mathf.Max(.001f,board.Settings.BulletDamage(1f)) + 1f);
            until=Time.time+.5f; while(Time.time<until)yield return null;
            Check(board.IsDestroyed && board.Health==0f && board.Driver==null && !other.IsMounted,
                "Lethal damage destroys the board and releases rider occupancy");
            Check(board.Couch==null && !couch.IsLoaded && couch.IsPickupAvailable && !couch.CouchRigidbody.isKinematic && couch.DeliveryId==731,
                "Destroyed board releases its delivery couch with physics and identity intact");
            Check(!board.TryMountServer(other),"Destroyed board rejects remounting");
            File.AppendAllText(Result,"COMPLETE: host movement, authority, occupancy, cargo identity, late join, detach, slopes, remote mounting and disconnect recovery passed.\n");
        }
    }
}
