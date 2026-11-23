// Explicit offline asset authoring and verification. No import or play hooks.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class AuthorHotwireShotgun
{
    const string Prefab="Assets/_Project/Prefabs/Enemies/Thief_Shotgun.prefab";
    const string Model="Assets/_Project/Models/Thieves/Hotwire/Hotwire.fbx";
    const string Original="Assets/_Project/Models/Thieves/thief_enemy_01.fbx";
    const string Material="Assets/_Project/Materials/Thieves/Hotwire/Hotwire.mat";
    static string Output {get{var a=Environment.GetCommandLineArgs();return a[Array.IndexOf(a,"-hotwireReport")+1];}}
    static void Need(bool ok,string message){if(!ok)throw new Exception(message);}
    static AnimationClip[] Clips()=>AssetDatabase.LoadAllAssetsAtPath(Original).OfType<AnimationClip>().Where(c=>c.name.StartsWith("Shotgun_Thief_")).ToArray();
    static SkinnedMeshRenderer[] Skins(Animator animator)=>animator.GetComponent<LODGroup>().GetLODs().Select(l=>l.renderers.OfType<SkinnedMeshRenderer>().Single()).ToArray();
    public static void Build()
    {
        try
        {
            string originalPrefabText=File.ReadAllText(Prefab);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(Model);Need(model,"Missing Hotwire FBX");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Material);Need(material&&material.shader,"Missing Hotwire material");
            var source=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(s=>s.name);
            Need(source.Count==4,"Expected four Hotwire body LODs");
            var root=PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var animator=root.GetComponentInChildren<Animator>(true);var bones=animator.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
                var skins=Skins(animator);Need(skins.Length==4,"Original body LODs missing");
                foreach(var skin in skins)
                {
                    var next=source[skin.name];
                    Need(next.sharedMesh.bindposes.Length==next.bones.Length,"Invalid Hotwire bind poses");
                    skin.sharedMesh=next.sharedMesh;skin.sharedMaterials=new[]{material};
                    skin.bones=next.bones.Select(b=>bones[b.name]).ToArray();skin.rootBone=bones[next.rootBone.name];
                    EditorUtility.SetDirty(skin);
                }
                // Evaluate the original shotgun motions and include the actual new
                // headwear and gloves in static culling bounds; restore T-pose.
                var transforms=root.GetComponentsInChildren<Transform>(true);var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
                var bounds=new Bounds[4];var initialized=new bool[4];var baked=new Mesh();
                foreach(var clip in Clips())for(int f=0;f<=Mathf.CeilToInt(clip.length*30);f++)
                {
                    clip.SampleAnimation(animator.gameObject,Mathf.Min(f/30f,clip.length));
                    for(int i=0;i<4;i++)
                    {
                        skins[i].BakeMesh(baked,true);
                        foreach(var v in baked.vertices){Need(float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z),"Nonfinite skinning");if(!initialized[i]){bounds[i]=new Bounds(v,Vector3.zero);initialized[i]=true;}else bounds[i].Encapsulate(v);}
                    }
                }
                for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];}
                for(int i=0;i<4;i++){skins[i].BakeMesh(baked,true);foreach(var v in baked.vertices)bounds[i].Encapsulate(v);bounds[i].Expand(.08f);skins[i].localBounds=bounds[i];}
                Object.DestroyImmediate(baked);
                PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            // Keep root gameplay serialization exact: saving with newer script
            // definitions otherwise adds unrelated default serialized fields.
            string saved=File.ReadAllText(Prefab);
            foreach(Match block in Regex.Matches(originalPrefabText,@"^--- !u!114 &(-?\d+).*?(?=^--- !u!|\z)",RegexOptions.Multiline|RegexOptions.Singleline))
            {
                string pattern=@"^--- !u!114 &"+Regex.Escape(block.Groups[1].Value)+@"\r?\n.*?(?=^--- !u!|\z)";
                saved=Regex.Replace(saved,pattern,m=>block.Value,RegexOptions.Multiline|RegexOptions.Singleline);
            }
            File.WriteAllText(Prefab,saved);AssetDatabase.ImportAsset(Prefab,ImportAssetOptions.ForceSynchronousImport);Validate();
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Output,"UnityHotwireFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    public static void Validate()
    {
        try
        {
            var root=PrefabUtility.LoadPrefabContents(Prefab);var lines=new List<string>();
            try
            {
                Need(root.GetComponentsInChildren<Component>(true).All(c=>c!=null),"Missing script reference");
                var animator=root.GetComponentInChildren<Animator>(true);Need(animator&&animator.avatar&&animator.avatar.isValid,"Invalid existing avatar");
                Need(animator.runtimeAnimatorController.name=="thief_enemy_01_Shotgun","Shotgun controller replaced");
                var skins=Skins(animator);var material=AssetDatabase.LoadAssetAtPath<Material>(Material);
                for(int i=0;i<4;i++)
                {
                    Need(AssetDatabase.GetAssetPath(skins[i].sharedMesh)==Model,"Old body mesh still assigned");
                    Need(skins[i].sharedMaterial==material,"Wrong body material");
                    Need(skins[i].sharedMesh.triangles.Length/3<=new[]{10000,4000,1500,700}[i],"LOD budget exceeded");
                    Need(skins[i].bones.All(b=>b&&b.IsChildOf(animator.transform)),"Broken skin bone reference");
                }
                var grip=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="WeaponGrip");var handle=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="HandleCenter");var muzzle=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Muzzle");var hand=grip.parent;Need(hand.name=="Hand.R","Weapon mount no longer uses existing hand");
                Export(root,"Tpose",skins[0]);
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
                var baked=new Mesh();
                foreach(string state in new[]{"Idle","Walk","Shoot","Frustration","Death"})
                {
                    var clip=Clips().Single(c=>c.name=="Shotgun_Thief_"+state);int count=Mathf.Max(13,Mathf.CeilToInt(clip.length*30));
                    for(int f=0;f<count;f++)
                    {
                        animator.Play("Base Layer."+state,0,(f+.5f)/count);animator.Update(0);Need(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer."+state),"State failed: "+state);
                        foreach(var skin in skins)
                        {
                            skin.BakeMesh(baked,true);
                            foreach(var v in baked.vertices)
                            {
                                Need(float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z),"Invalid skinning: "+state);
                                Need(skin.localBounds.Contains(v),"Out of skin bounds: "+state+" "+skin.name);
                                var world=root.transform.InverseTransformPoint(skin.transform.TransformPoint(v));Need(world.magnitude<3,"Exploded mesh or bind-pose mismatch");
                            }
                        }
                        Need(Vector3.Distance(grip.position,handle.position)<.001f,"Shotgun handle drift");Need(Vector3.Dot(muzzle.forward,hand.up)>.99f,"Shotgun muzzle axis changed");
                    }
                    animator.Play("Base Layer."+state,0,.4f);animator.Update(0);Export(root,state,skins[0]);
                    lines.Add($"PASS: Shotgun {state}, {count} Animator poses, all four Hotwire LODs, finite geometry, saved bounds, original grip/muzzle alignment.");
                }
                // Render every distant mesh in the same actual saved prefab pose.
                animator.Play("Base Layer.Idle",0,.4f);animator.Update(0);for(int i=1;i<4;i++)Export(root,"LOD"+i,skins[i]);
                animator.SetBool("Dead",false);animator.SetFloat("Speed",0);animator.Play("Base Layer.Idle",0,0);animator.Update(0);animator.SetFloat("Speed",1);for(int f=0;f<20;f++)animator.Update(1f/30);
                Need(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Walk"),"Walk transition failed");animator.SetBool("Dead",true);animator.SetTrigger("Shoot");for(int f=0;f<20;f++)animator.Update(1f/30);Need(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Death"),"Death priority failed");
                Object.DestroyImmediate(baked);
                lines.Add("PASS: Original valid avatar, Shotgun controller, four saved mesh LODs, original mounts, Speed transition and Death priority retained.");
                File.WriteAllLines(Path.Combine(Output,"UnityHotwireValidation.txt"),lines);Debug.Log("HOTWIRE_SHOTGUN_VALIDATION_PASS");
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Output,"UnityHotwireFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    [Serializable] class Preview{public List<Surface> surfaces=new List<Surface>();}
    [Serializable] class Surface{public Vector3[] vertices;public Vector3[] normals;public Vector2[] uv;public int[] triangles;public Color color;public string texture;}
    static void Export(GameObject root,string name,SkinnedMeshRenderer body)
    {
        var result=new Preview();
        foreach(var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if(r is SkinnedMeshRenderer&&r!=body)continue;
            if(r is LineRenderer||r is ParticleSystemRenderer)continue;
            if(r!=body&&(!r.enabled||!r.gameObject.activeInHierarchy))continue;
            Mesh mesh;bool temp=r is SkinnedMeshRenderer;
            if(r is SkinnedMeshRenderer skin){mesh=new Mesh();skin.BakeMesh(mesh,true);}else{var filter=r.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;mesh=filter.sharedMesh;}
            var vertices=mesh.vertices;var normals=mesh.normals;for(int i=0;i<vertices.Length;i++)vertices[i]=r.transform.TransformPoint(vertices[i]);for(int i=0;i<normals.Length;i++)normals[i]=r.transform.TransformDirection(normals[i]).normalized;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                var mat=r.sharedMaterials[Math.Min(sub,r.sharedMaterials.Length-1)];var tex=mat.HasProperty("_BaseMap")?mat.GetTexture("_BaseMap"):null;
                result.surfaces.Add(new Surface{vertices=vertices,normals=normals,uv=mesh.uv,triangles=mesh.GetTriangles(sub),color=mat.HasProperty("_BaseColor")?mat.GetColor("_BaseColor"):Color.white,texture=tex?Path.GetFullPath(AssetDatabase.GetAssetPath(tex)):""});
            }
            if(temp)Object.DestroyImmediate(mesh);
        }
        File.WriteAllText(Path.Combine(Output,"Hotwire_"+name+"_Preview.json"),JsonUtility.ToJson(result));
    }
}
