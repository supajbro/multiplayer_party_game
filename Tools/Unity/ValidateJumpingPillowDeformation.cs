using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Editor;
using CouchGuys.Input;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Gameplay.Sled;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ValidateJumpingPillowDeformation
{
    const string Report = "JumpingPillowDeformationValidation.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator probe;
    static float deadline;
    static ValidateJumpingPillowDeformation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Pillow.Deform.Run", false))
            {
                SessionState.SetBool("Pillow.Deform.Run", false);
                deadline = Time.realtimeSinceStartup + 40f; probe = Probe();
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("Pillow.Deform.Exit", false))
                EditorApplication.Exit(SessionState.GetBool("Pillow.Deform.Failed", false) ? 1 : 0);
        };
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Log(string message) { File.AppendAllText(Report,message+"\n"); Debug.Log(message); }
    static T Field<T>(object target,string name) => (T)target.GetType().GetField(name,Private).GetValue(target);
    static void Set(object target,string name,object value) => target.GetType().GetField(name,Private).SetValue(target,value);
    public static void Run()
    {
        File.WriteAllText(Report,"Local pillow deformation and vehicle bounce validation\n");
        var before=AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath).GetComponent<JumpingPillow>().BounceHeight;
        JumpingPillowAuthoring.AssignDeformationSurface();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath);
        Check(prefab.GetComponent<JumpingPillow>().BounceHeight==before,"Existing bounce customization overwritten");
        Check(Field<MeshFilter>(prefab.GetComponent<JumpingPillow>(),"m_jumpingSurface")!=null,"Missing saved deformation reference");
        Check(prefab.GetComponent<FishNet.Object.NetworkObject>()==null,"Pillow unexpectedly became networked");
        Log("PASS: saved surface reference, existing Inspector bounce height preserved, no pillow NetworkObject.");
        SessionState.SetBool("Pillow.Deform.Exit",true);
        SessionState.SetBool("Pillow.Deform.Failed",false);
        SessionState.SetBool("Pillow.Deform.Run",true);
        EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if(probe==null || !EditorApplication.isPlaying) return;
        try
        {
            Check(Time.realtimeSinceStartup<deadline,"Probe timeout");
            if(probe.MoveNext()) return;
            Log("PASS: all local deformation and vehicle bounce checks.");
        }
        catch(Exception e) { Log("FAIL: "+e); SessionState.SetBool("Pillow.Deform.Failed",true); }
        probe=null; EditorApplication.ExitPlaymode();
    }
    static Vector3 SurfacePoint(JumpingPillow pillow,float x,float z)
    {
        Check(pillow.SurfaceCollider.Raycast(new Ray(new Vector3(x,40,z),Vector3.down),out var hit,80),"No surface at test point");
        return hit.point;
    }
    static ThirdPersonPlayerController Avatar(Vector3 feet,bool remote)
    {
        var go=new GameObject(remote?"Remote disabled avatar":"Local avatar"); go.transform.position=feet;
        var cc=go.AddComponent<CharacterController>(); cc.center=Vector3.up; cc.height=2;
        go.AddComponent<PlayerInputReader>().enabled=false;
        var motor=go.AddComponent<ThirdPersonPlayerController>(); motor.enabled=false;
        if(remote) cc.enabled=false;
        return motor;
    }
    static float DepressionNear(MeshFilter f,Vector3[] rest,Vector3 point)
    {
        var actual=f.sharedMesh.vertices; float deepest=0;
        for(int i=0;i<actual.Length;i++)
        {
            Vector3 p=f.transform.TransformPoint(rest[i]);
            if(new Vector2(p.x-point.x,p.z-point.z).sqrMagnitude<.6f)
                deepest=Mathf.Max(deepest,p.y-f.transform.TransformPoint(actual[i]).y);
        }
        return deepest;
    }
    static IEnumerator Probe()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath);
        var root=Object.Instantiate(prefab,new Vector3(0,12,0),Quaternion.identity);
        var pillow=root.GetComponent<JumpingPillow>(); pillow.FitToTerrain(p=>12+.08f*p.x);
        var surface=Field<MeshFilter>(pillow,"m_jumpingSurface");
        var rest=surface.sharedMesh.vertices;
        var colors=surface.sharedMesh.colors;
        var uv=surface.sharedMesh.uv;
        var triangles=surface.sharedMesh.triangles;
        var colliderMesh=pillow.SurfaceCollider.sharedMesh;
        var collision=colliderMesh.vertices;
        var anchor=root.GetComponentsInChildren<MeshFilter>().Single(f=>f!=surface);
        var anchorRest=anchor.sharedMesh.vertices;
        var pointA=SurfacePoint(pillow,-2,0); var pointB=SurfacePoint(pillow,2,0);
        var local=Avatar(pointA+Vector3.up*.04f,false); var remote=Avatar(pointB+Vector3.up*.04f,true);
        float until=Time.time+.65f;
        while(Time.time<until) yield return null;
        Check(DepressionNear(surface,rest,pointA)>.09f,"Local avatar did not depress surface");
        Check(DepressionNear(surface,rest,pointB)>.09f,"Disabled remote avatar did not depress surface");
        var changed=surface.sharedMesh.vertices;
        for(int i=0;i<changed.Length;i++)
        {
            if(colors[i].r<.001f) Check(changed[i]==rest[i],"Pinned perimeter moved");
            Check((changed[i]-rest[i]).magnitude<.75f,"Unbounded deformation");
        }
        Check(surface.sharedMesh.uv.SequenceEqual(uv) && surface.sharedMesh.triangles.SequenceEqual(triangles),"Topology or UVs changed");
        Check(surface.sharedMesh.colors.SequenceEqual(colors),"Colour mask changed");
        Check(anchor.sharedMesh.vertices.SequenceEqual(anchorRest),"Anchor mesh deformed");
        Check(pillow.SurfaceCollider.sharedMesh==colliderMesh && colliderMesh.vertices.SequenceEqual(collision),"Local visual deformation changed physics collider");
        Log("PASS: simultaneous local and disabled remote avatars depress separate regions; pinned edge, anchor, UVs, topology, colours and static collider preserved.");
        local.transform.position+=Vector3.up*8; remote.transform.position+=Vector3.up*8;
        pillow.NotifyPlayerLanding(pointA,18);
        until=Time.time+.22f; while(Time.time<until) yield return null;
        Check(DepressionNear(surface,rest,pointA)>.2f,"Landing impact did not create a deeper depression");
        until=Time.time+2.5f; while(Time.time<until) yield return null;
        Check(surface.sharedMesh.vertices.Zip(rest,(a,b)=>(a-b).magnitude).Max()<.002f,"Surface did not spring back");
        Log("PASS: landing impulse produces a deeper local depression then recovers to rest.");
        Object.Destroy(local.gameObject); Object.Destroy(remote.gameObject);

        // Existing vehicle controller types, with motors disabled to isolate pillow physics.
        foreach(var type in new[]{typeof(TruckController),typeof(TrafficVehicleController),typeof(MagneticHoverboardController),typeof(SledController)})
        {
            Set(pillow,"m_vehicleBounceHeight",type==typeof(TruckController)?1.25f:type==typeof(MagneticHoverboardController)?3.25f:2.5f);
            float expectedSpeed=Mathf.Sqrt(2f*-Physics.gravity.y*pillow.VehicleBounceHeight);
            var vehicle=new GameObject(type.Name+" test"); vehicle.SetActive(false);
            vehicle.transform.position=SurfacePoint(pillow,0,0)+Vector3.up*.7f;
            var body=vehicle.AddComponent<Rigidbody>(); body.mass=type==typeof(TruckController)?1800:100;
            body.constraints=RigidbodyConstraints.FreezeRotation;
            var box=vehicle.AddComponent<BoxCollider>(); box.size=new Vector3(1.5f,.5f,2);
            ((MonoBehaviour)vehicle.AddComponent(type)).enabled=false;
            vehicle.GetComponent<FishNet.Object.NetworkObject>().SetIsNetworked(false);
            foreach(var behaviour in vehicle.GetComponents<MonoBehaviour>()) behaviour.enabled=false;
            vehicle.SetActive(true); body.isKinematic=false; body.detectCollisions=true;
            body.linearVelocity=new Vector3(2,0,0); Physics.SyncTransforms();
            float maxVelocity=0; until=Time.time+.25f;
            while(Time.time<until) { yield return null; maxVelocity=Mathf.Max(maxVelocity,body.linearVelocity.y); }
            Check(maxVelocity>expectedSpeed-.8f && maxVelocity<expectedSpeed+.3f,type.Name+" did not receive the configured bounded, mass-independent bounce: "+maxVelocity+
                "; kinematic="+body.isKinematic+" active="+vehicle.activeInHierarchy+" bounds="+box.bounds+" loads="+Field<System.Collections.ICollection>(pillow,"m_vehicleLoads").Count);
            Check(body.linearVelocity.x>1.7f,"Bounce removed horizontal momentum");
            Log("PASS: "+type.Name+" ("+body.mass+" kg) bounced, horizontal momentum retained; upward speed="+maxVelocity);
            Object.Destroy(vehicle); yield return null;
        }
        // Remote physics replicas are visual inputs only.
        var proxy=new GameObject("Kinematic vehicle replica"); proxy.SetActive(false);
        proxy.transform.position=SurfacePoint(pillow,0,0)+Vector3.up*.6f;
        var rb=proxy.AddComponent<Rigidbody>(); rb.isKinematic=true;
        proxy.AddComponent<BoxCollider>(); proxy.AddComponent<TruckController>().enabled=false;
        proxy.GetComponent<FishNet.Object.NetworkObject>().SetIsNetworked(false);
        foreach(var behaviour in proxy.GetComponents<MonoBehaviour>()) behaviour.enabled=false;
        proxy.SetActive(true); var position=proxy.transform.position;
        until=Time.time+.4f; while(Time.time<until) yield return null;
        Check(rb.isKinematic && proxy.transform.position==position,"Remote kinematic replica was launched");
        Check(DepressionNear(surface,rest,position)>.1f,"Remote vehicle did not deform surface locally");
        Log("PASS: kinematic remote vehicle deforms locally without receiving a physics impulse.");
        Object.Destroy(proxy);
        yield return null;
        var guarded=new GameObject("Non-authoritative dynamic network vehicle"); guarded.SetActive(false);
        guarded.transform.position=SurfacePoint(pillow,0,0)+Vector3.up*.6f;
        var guardedBody=guarded.AddComponent<Rigidbody>(); guardedBody.useGravity=false;
        guarded.AddComponent<BoxCollider>(); guarded.AddComponent<TruckController>().enabled=false;
        // Keep the NetworkObject uninitialized and disabled: this simulates an active
        // replica before network initialization, when authority must not be inferred.
        foreach(var behaviour in guarded.GetComponents<MonoBehaviour>()) behaviour.enabled=false;
        guarded.SetActive(true); guardedBody.isKinematic=false;
        until=Time.time+.3f; while(Time.time<until) yield return null;
        Check(Mathf.Abs(guardedBody.linearVelocity.y)<.01f,"Non-authoritative dynamic network object was launched locally");
        Log("PASS: uninitialized/non-authoritative network vehicle receives no local physics impulse.");
        Object.Destroy(guarded);
        yield return null;
        var groundVehicle=new GameObject("Ground-following vehicle"); groundVehicle.SetActive(false);
        groundVehicle.transform.position=new Vector3(0,12.3f,0);
        var groundBody=groundVehicle.AddComponent<Rigidbody>(); groundBody.useGravity=false;
        var hull=groundVehicle.AddComponent<BoxCollider>(); hull.size=new Vector3(1.5f,.5f,2);
        groundVehicle.AddComponent<TruckController>().enabled=false;
        groundVehicle.GetComponent<FishNet.Object.NetworkObject>().SetIsNetworked(false);
        foreach(var behaviour in groundVehicle.GetComponents<MonoBehaviour>()) behaviour.enabled=false;
        groundVehicle.SetActive(true); groundBody.isKinematic=false;
        // This fixture tests overlap detection below the crown. Suppress unrelated
        // vehicle collision callbacks which require a fully spawned FishNet object.
        Physics.IgnoreCollision(hull,pillow.SurfaceCollider,true);
        float groundSpeed=0; until=Time.time+.2f;
        while(Time.time<until) { yield return null; groundSpeed=Mathf.Max(groundSpeed,groundBody.linearVelocity.y); }
        Check(groundSpeed>6f,"Ground-following vehicle below raised crown was missed");
        Log("PASS: ground-following vehicle entering the raised crown is detected and bounced.");
        Object.Destroy(groundVehicle);
        Object.Destroy(root);
    }
}
