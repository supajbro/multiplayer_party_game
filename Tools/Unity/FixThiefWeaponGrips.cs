// Explicit saved-prefab grip authoring. No runtime/import hooks.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CouchGuys.Gameplay.Enemies;
using Object=UnityEngine.Object;

public static class FixThiefWeaponGrips
{
    static readonly string[] Names={"Thief_Pistol","Thief_AssaultRifle","Thief_Shotgun","GrapplerThief"};
    static string Output{get{var a=Environment.GetCommandLineArgs();return a[Array.IndexOf(a,"-thiefReport")+1];}}
    static string PathFor(string name)=>$"Assets/_Project/Prefabs/Enemies/{name}.prefab";
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
    static void Weapon(GameObject root,out Transform gun,out Transform muzzle)
    {
        var grapple=root.GetComponent<GrapplerThief>();
        if(grapple)
        {
            var so=new SerializedObject(grapple);gun=(Transform)so.FindProperty("m_gun").objectReferenceValue;muzzle=(Transform)so.FindProperty("m_muzzle").objectReferenceValue;
        }
        else
        {
            var so=new SerializedObject(root.GetComponent<EnemyWeapon>());muzzle=(Transform)so.FindProperty("m_muzzle").objectReferenceValue;
            gun=((Renderer)so.FindProperty("m_heldWeaponRenderers").GetArrayElementAtIndex(0).objectReferenceValue).transform;
        }
    }
    public static void Fix()
    {
        try
        {
            var lines=new List<string>();
            foreach(string name in Names)
            {
                var root=PrefabUtility.LoadPrefabContents(PathFor(name));
                try
                {
                    Weapon(root,out var gun,out var muzzle);var hand=Find(root,"Hand.R");
                    Require(!hand.Cast<Transform>().Any(t=>t.name=="WeaponGrip"),"Grip already authored: "+name);
                    // Measured centers of the actual vertical handle components, not weapon bounds centers.
                    Vector3 gripLocal=name=="GrapplerThief"?new Vector3(0,-.0334f,-.0663f):new Vector3(.0403f,-.0282f,.0121f);
                    Vector3 handle=gun.TransformPoint(gripLocal);
                    Vector3 barrel=gun.forward.normalized;
                    Vector3 desiredForward=hand.up.normalized;
                    Quaternion turn=Quaternion.FromToRotation(barrel,desiredForward);
                    // The old baked firearm muzzle rotations were down-facing; the mesh barrel is +Z.
                    muzzle.rotation=Quaternion.LookRotation(barrel,root.transform.up);
                    var pivot=new GameObject("WeaponGrip").transform;pivot.SetParent(hand,false);
                    pivot.SetPositionAndRotation(handle,Quaternion.identity);
                    var scale=hand.lossyScale;pivot.localScale=new Vector3(1f/scale.x,1f/scale.y,1f/scale.z);
                    gun.SetParent(pivot,true);
                    if(!muzzle.IsChildOf(gun))muzzle.SetParent(pivot,true);
                    // Roughly the center of the stylized palm; the grip remains editable as a saved Transform.
                    float characterScale=root.GetComponentInChildren<Animator>(true).transform.lossyScale.x;
                    Vector3 palm=hand.position+desiredForward*(.075f*characterScale);
                    pivot.SetPositionAndRotation(palm,turn);
                    var marker=new GameObject("HandleCenter").transform;marker.SetParent(gun,false);marker.localPosition=gripLocal;
                    if(root.GetComponent<GrapplerThief>())
                    {
                        // GrapplerThief rotates m_gun while aiming: rotate about the palm, not the mesh origin.
                        var so=new SerializedObject(root.GetComponent<GrapplerThief>());
                        so.FindProperty("m_gun").objectReferenceValue=pivot;so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    Require(Vector3.Distance(marker.position,pivot.position)<.0001f,"Handle not centered in grip");
                    Require(Vector3.Dot(muzzle.forward,hand.up)>.995f,"Muzzle is not aligned with hand direction");
                    PrefabUtility.SaveAsPrefabAsset(root,PathFor(name));
                    lines.Add($"AUTHORED {name}: handle centered at palm, barrel follows fingers; saved WeaponGrip and HandleCenter transforms; original gun, muzzle and renderer objects retained.");
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Validate(lines);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Output,"GripFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }

    static void Validate(List<string> lines)
    {
        foreach(string name in Names)
        {
            var root=PrefabUtility.LoadPrefabContents(PathFor(name));
            try
            {
                Weapon(root,out var gun,out var muzzle);var hand=Find(root,"Hand.R");var grip=Find(root,"WeaponGrip");var handle=Find(root,"HandleCenter");
                Require(grip.parent==hand && (gun==grip||gun.IsChildOf(grip)) && muzzle.IsChildOf(grip),"Mount hierarchy invalid");
                Require(root.GetComponentsInChildren<Component>(true).All(c=>c!=null),"Missing script");
                var animator=root.GetComponentInChildren<Animator>(true);animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                // Snapshot before Animator evaluates: this is the user's prefab T-pose case.
                Check(name,"Tpose",hand,grip,handle,muzzle);
                Export(root,name+"_Tpose");
                animator.Rebind();animator.Update(0);
                foreach(string state in new[]{"Idle","Walk","Shoot","Frustration","Death"})
                {
                    for(int sample=0;sample<13;sample++)
                    {
                        animator.Play("Base Layer."+state,0,(sample+.5f)/13f);animator.Update(0);
                        Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer."+state),"Playback failed");
                        Check(name,state,hand,grip,handle,muzzle);
                    }
                    if(state=="Idle"||state=="Shoot")Export(root,name+"_"+state);
                    lines.Add($"PASS {name} {state}: 13 sampled poses, handle remains in palm and muzzle follows finger/barrel axis.");
                }
                if(name=="GrapplerThief")
                {
                    foreach(var aim in new[]{Vector3.forward,new Vector3(.4f,.2f,1),new Vector3(-.4f,-.2f,1)})
                    {
                        gun.rotation=Quaternion.LookRotation(aim);
                        Require(Vector3.Distance(handle.position,grip.position)<.001f,"Grapple handle drifts when aiming");
                        Require(Vector3.Dot(muzzle.forward,aim.normalized)>.99f,"Grapple muzzle fails runtime aim");
                    }
                    lines.Add("PASS GrapplerThief runtime aiming: gun pivots at palm; handle does not orbit or leave hand.");
                }
                lines.Add("PASS "+name+" Tpose: correct grip position, orientation and muzzle; original component references retained.");
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        File.WriteAllLines(Path.Combine(Output,"GripValidation.txt"),lines);Debug.Log("THIEF_WEAPON_GRIPS_PASS");
    }
    static void Check(string name,string state,Transform hand,Transform grip,Transform handle,Transform muzzle)
    {
        Require(Vector3.Distance(grip.position,handle.position)<.001f,name+" "+state+": handle drift");
        Require(Vector3.Dot(muzzle.forward,hand.up)>.99f,name+" "+state+": incorrect barrel alignment");
        Require(Vector3.Distance(grip.position,hand.position)>.06f&&Vector3.Distance(grip.position,hand.position)<.11f,name+": palm offset incorrect");
    }
    [Serializable] class Preview{public List<Surface> surfaces=new List<Surface>();}
    [Serializable] class Surface{public Vector3[] vertices;public Vector3[] normals;public Vector2[] uv;public int[] triangles;public Color color;public string texture;}
    static void Export(GameObject root,string name)
    {
        var result=new Preview();
        foreach(var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if(!r.enabled||!r.gameObject.activeInHierarchy||r is LineRenderer||r is ParticleSystemRenderer)continue;
            if(r is SkinnedMeshRenderer && r.name!="thief_enemy_01")continue;
            var group=r.GetComponentInParent<LODGroup>();if(group&&group.GetLODs().Any(l=>l.renderers.Contains(r))&&!group.GetLODs()[0].renderers.Contains(r))continue;
            Mesh mesh;bool temporary=r is SkinnedMeshRenderer;
            if(r is SkinnedMeshRenderer skin){mesh=new Mesh();skin.BakeMesh(mesh,true);}else{var f=r.GetComponent<MeshFilter>();if(!f||!f.sharedMesh)continue;mesh=f.sharedMesh;}
            var vertices=mesh.vertices;var normals=mesh.normals;
            for(int i=0;i<vertices.Length;i++)vertices[i]=r.transform.TransformPoint(vertices[i]);
            for(int i=0;i<normals.Length;i++)normals[i]=r.transform.TransformDirection(normals[i]).normalized;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var m=r.sharedMaterials[Mathf.Min(sub,r.sharedMaterials.Length-1)];var tex=m.HasProperty("_BaseMap")?m.GetTexture("_BaseMap"):m.HasProperty("_MainTex")?m.GetTexture("_MainTex"):null;
                result.surfaces.Add(new Surface{vertices=vertices,normals=normals,uv=mesh.uv,triangles=mesh.GetTriangles(sub),color=m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):m.HasProperty("_Color")?m.GetColor("_Color"):Color.white,texture=tex?Path.GetFullPath(AssetDatabase.GetAssetPath(tex)):""});
            }
            if(temporary)Object.DestroyImmediate(mesh);
        }
        File.WriteAllText(Path.Combine(Output,name+"_Preview.json"),JsonUtility.ToJson(result));
    }
    public static void Inventory()
    {
        var lines=new List<string>();
        foreach(string name in Names)
        {
            var root=PrefabUtility.LoadPrefabContents(PathFor(name));
            try
            {
                Weapon(root,out var gun,out var muzzle);var hand=Find(root,"Hand.R");
                lines.Add($"{name} hand={hand.position} fingers={hand.up} handForward={hand.forward} gun={gun.position} scale={gun.lossyScale} muzzle={muzzle.position} forward={muzzle.forward}");
                foreach(var f in gun.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!f.GetComponent<Renderer>().enabled)continue;
                    var mesh=f.sharedMesh;var vertices=mesh.vertices;var renderer=f.GetComponent<Renderer>();
                    // Weld hard-normal/UV splits for connected-part bounds; geometry is read-only.
                    var ids=new Dictionary<Vector3Int,int>();var weld=new int[vertices.Length];var parent=new List<int>();
                    for(int i=0;i<vertices.Length;i++)
                    {
                        var v=vertices[i];var key=new Vector3Int(Mathf.RoundToInt(v.x*100000),Mathf.RoundToInt(v.y*100000),Mathf.RoundToInt(v.z*100000));
                        if(!ids.TryGetValue(key,out int id)){id=ids.Count;ids[key]=id;parent.Add(id);}weld[i]=id;
                    }
                    Func<int,int> find=null;find=i=>parent[i]==i?i:parent[i]=find(parent[i]);
                    for(int s=0;s<mesh.subMeshCount;s++)
                    {
                        for(int i=0;i<parent.Count;i++)parent[i]=i;
                        var tri=mesh.GetTriangles(s);
                        for(int i=0;i<tri.Length;i+=3){parent[find(weld[tri[i+1]])]=find(weld[tri[i]]);parent[find(weld[tri[i+2]])]=find(weld[tri[i]]);}
                        var groups=new Dictionary<int,Bounds>();
                        foreach(int i in tri.Distinct())
                        {
                            int id=find(weld[i]);Vector3 p=f.transform.TransformPoint(vertices[i])-hand.position;
                            if(!groups.TryGetValue(id,out var b))b=new Bounds(p,Vector3.zero);else b.Encapsulate(p);groups[id]=b;
                        }
                        foreach(var b in groups.Values.Where(b=>b.size.y>.035f))lines.Add($"  {f.name} {renderer.sharedMaterials[Mathf.Min(s,renderer.sharedMaterials.Length-1)].name} center={b.center.ToString("F4")} size={b.size.ToString("F4")}");
                    }
                }
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        File.WriteAllLines(Path.Combine(Output,"GripInventory.txt"),lines);Debug.Log("THIEF_GRIP_INVENTORY_PASS");
    }
}
