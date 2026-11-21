// Explicit asset migration, intended for an isolated authoring project only.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Animations;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using Object=UnityEngine.Object;

public static class UpgradeThiefEnemyPrefabs
{
    static readonly string[] Names={"Thief","Thief_Pistol","Thief_AssaultRifle","Thief_Shotgun","Thief_RocketLauncher","GrapplerThief","SpeakerThief"};
    static string Report { get { var a=Environment.GetCommandLineArgs();return a[Array.IndexOf(a,"-thiefReport")+1]; } }
    static string PathFor(string n)=>$"Assets/_Project/Prefabs/Enemies/{n}.prefab";
    static string PathOf(Transform t,Transform root)=>AnimationUtility.CalculateTransformPath(t,root);
    static string SourcePath(GameObject go)=>AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go));
    const string VisualPath="Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab";
    const string AttachmentFolder="Assets/_Project/Models/Thieves/ThiefEnemy01Attachments";
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static string Canonical(string name)
    {
        name=name.Replace("mixamorig:","");
        var simple=new Dictionary<string,string>{{"weapon_socket","Hand.R"},{"Pelvis","Hips"},{"Spine1","Spine"},{"Spine2","Chest"},{"LeftShoulder","Shoulder.L"},{"RightShoulder","Shoulder.R"}};
        if(simple.TryGetValue(name,out var value))return value;
        foreach(var pair in new[]{("Arm","UpperArm"),("ForeArm","Forearm"),("Hand","Hand"),("UpLeg","Thigh"),("Leg","Shin"),("Foot","Foot"),("ToeBase","Toes")})
        {
            if(name=="Left"+pair.Item1)return pair.Item2+".L";
            if(name=="Right"+pair.Item1)return pair.Item2+".R";
        }
        return name.Replace("LowerArm.","Forearm.").Replace("UpperLeg.","Thigh.").Replace("LowerLeg.","Shin.");
    }
    static void BindPose(SkinnedMeshRenderer r)
    {
        var bind=r.sharedMesh.bindposes;
        var items=r.bones.Select((bone,i)=>new{bone,world=r.transform.localToWorldMatrix*bind[i].inverse}).OrderBy(x=>x.bone.GetComponentsInParent<Transform>(true).Length).ToArray();
        foreach(var item in items)
        {
            var matrix=item.bone.parent.worldToLocalMatrix*item.world;
            item.bone.SetLocalPositionAndRotation(matrix.GetColumn(3),matrix.rotation);item.bone.localScale=matrix.lossyScale;
        }
    }
    public static void Migrate()
    {
        try
        {
            Directory.CreateDirectory(AttachmentFolder);AssetDatabase.Refresh();
            var lines=new List<string>();
            foreach(string name in Names)Upgrade(name,lines);
            AssetDatabase.SaveAssets();
            File.WriteAllLines(System.IO.Path.Combine(Report,"PrefabUpgradeValidation.txt"),lines);
            Debug.Log("THIEF_PREFAB_UPGRADE_PASS");
        }
        catch(Exception e){File.WriteAllText(System.IO.Path.Combine(Report,"PrefabUpgradeFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Upgrade(string name,List<string> report)
    {
        var root=PrefabUtility.LoadPrefabContents(PathFor(name));
        try
        {
            var oldAnimator=root.GetComponentInChildren<Animator>(true);
            Require(oldAnimator!=null && oldAnimator.GetComponent<LODGroup>()==null,"Missing old visual or prefab already upgraded: "+name);
            var old=oldAnimator.gameObject;
            var oldRagdoll=old.GetComponent<PedestrianRagdoll>();
            Require(oldRagdoll!=null,"Missing ragdoll: "+name);
            var bodySkin=old.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>!r.name.Contains("Weapon"));
            BindPose(bodySkin);
            string oldAsset=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old);
            var customRoots=old.GetComponentsInChildren<Transform>(true).Where(t=>t!=old.transform && t.parent &&
                SourcePath(t.gameObject)!=oldAsset && SourcePath(t.parent.gameObject)==oldAsset).ToArray();
            var weaponSkins=old.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.IndexOf("Weapon",StringComparison.OrdinalIgnoreCase)>=0 || r.sharedMaterials.Any(m=>m && m.name.IndexOf("Weapon",StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
            var oldBodies=old.GetComponentsInChildren<Rigidbody>(true);
            var oldTransforms=old.GetComponentsInChildren<Transform>(true);
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath),root.transform);
            visual.name="ThiefModel";
            // Match the previous visual height independently of customized gameplay colliders.
            var oldSurface=new Mesh();bodySkin.BakeMesh(oldSurface,true);
            var oldVertices=oldSurface.vertices.Select(v=>root.transform.InverseTransformPoint(bodySkin.transform.TransformPoint(v))).ToArray();
            float bottom=oldVertices.Min(v=>v.y);float height=oldVertices.Max(v=>v.y)-bottom;
            Object.DestroyImmediate(oldSurface);
            float scale=height/1.65f;
            visual.transform.SetLocalPositionAndRotation(new Vector3(0,bottom,0),Quaternion.identity);visual.transform.localScale=Vector3.one*scale;
            var animator=visual.GetComponent<Animator>();
            string variant=name.Contains("AssaultRifle") || name.Contains("Rocket")?"AssaultRifle":name.Contains("Shotgun")?"Shotgun":"Pistol";
            animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<AnimatorController>($"Assets/_Project/Animations/Thieves/ThiefEnemy01/thief_enemy_01_{variant}.controller");
            Require(animator.runtimeAnimatorController!=null,"Missing retargeted controller");
            var bones=visual.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
            var remap=new Dictionary<Object,Object>{{oldAnimator,animator}};
            foreach(var t in oldTransforms)if(bones.TryGetValue(Canonical(t.name),out var bone))remap[t]=bone;
            remap[old.transform]=visual.transform;
            // FBX weapons are rigidly weighted skins. Bake them once into saved meshes and mount to the new hand.
            var sourceHand=oldTransforms.First(t=>Canonical(t.name)=="Hand.R");
            var targetHand=bones["Hand.R"];
            foreach(var skin in weaponSkins)
            {
                var mesh=new Mesh();skin.BakeMesh(mesh,true);
                var vertices=mesh.vertices;var normals=mesh.normals;
                for(int i=0;i<vertices.Length;i++)vertices[i]=skin.transform.TransformPoint(vertices[i])-sourceHand.position;
                for(int i=0;i<normals.Length;i++)normals[i]=skin.transform.TransformDirection(normals[i]).normalized;
                mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateBounds();mesh.name=name+"_"+skin.name;
                string meshPath=$"{AttachmentFolder}/{mesh.name}.asset";
                Require(!File.Exists(meshPath),"Refusing to overwrite attachment mesh "+meshPath);
                AssetDatabase.CreateAsset(mesh,meshPath);
                var go=new GameObject(skin.name);go.transform.SetParent(targetHand,false);
                go.transform.SetPositionAndRotation(targetHand.position,Quaternion.identity);
                var ls=targetHand.lossyScale;go.transform.localScale=new Vector3(1/ls.x,1/ls.y,1/ls.z);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=skin.sharedMaterials;
                renderer.enabled=skin.enabled;go.SetActive(skin.gameObject.activeSelf);
                renderer.shadowCastingMode=skin.shadowCastingMode;renderer.receiveShadows=skin.receiveShadows;
                remap[skin]=renderer;remap[skin.transform]=go.transform;remap[skin.gameObject]=go;
            }
            // Added props are preserved as existing objects, including nested speaker LODs and grapple/rocket helpers.
            foreach(var attachment in customRoots)
            {
                var parent=attachment.parent;
                while(parent && !remap.ContainsKey(parent))parent=parent.parent;
                Require(parent!=null,"Unmapped attachment "+attachment.name);
                var next=(Transform)remap[parent];
                Vector3 position=attachment.position-parent.position+next.position;
                Quaternion rotation=attachment.rotation;Vector3 worldScale=attachment.lossyScale;
                attachment.SetParent(next,true);attachment.SetPositionAndRotation(position,rotation);
                var ps=next.lossyScale;attachment.localScale=new Vector3(worldScale.x/ps.x,worldScale.y/ps.y,worldScale.z/ps.z);
                if(attachment.name=="ShoulderSpeaker")
                {
                    // Keep the retained speaker outside the new character's larger head.
                    var renderers=attachment.GetComponent<LODGroup>().GetLODs()[0].renderers;
                    var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
                    float overlap=bounds.max.x-(bones["Head"].position.x-.26f*scale-.04f);
                    if(overlap>0)attachment.position-=root.transform.right*overlap;
                }
            }
            // Retain the muzzle's actual rest-pose position/orientation, with the new hand origin.
            foreach(var oldMuzzle in oldTransforms.Where(t=>t.name.Equals("Muzzle",StringComparison.OrdinalIgnoreCase)||t.name=="muzzle"))
            {
                if(!oldMuzzle.IsChildOf(old.transform))continue; // Already kept with a custom gun.
                var muzzle=new GameObject("Muzzle").transform;muzzle.SetParent(targetHand,false);
                muzzle.SetPositionAndRotation(oldMuzzle.position-sourceHand.position+targetHand.position,oldMuzzle.rotation);
                remap[oldMuzzle]=muzzle;remap[oldMuzzle.gameObject]=muzzle.gameObject;
            }
            var ragdoll=visual.AddComponent<PedestrianRagdoll>();ragdoll.enabled=false;remap[oldRagdoll]=ragdoll;
            var bodies=new List<Rigidbody>();var colliders=new List<Collider>();
            foreach(var oldBody in oldBodies)
            {
                string key=Canonical(oldBody.name);Require(bones.ContainsKey(key),"Unmapped ragdoll bone "+oldBody.name);
                var bone=bones[key];var body=bone.gameObject.AddComponent<Rigidbody>();
                body.mass=oldBody.mass;body.linearDamping=oldBody.linearDamping;body.angularDamping=oldBody.angularDamping;
                body.useGravity=oldBody.useGravity;body.isKinematic=true;body.interpolation=oldBody.interpolation;
                body.collisionDetectionMode=oldBody.collisionDetectionMode;body.constraints=oldBody.constraints;
                var sphere=bone.gameObject.AddComponent<SphereCollider>();
                float radius = key == "Head" ? .26f : key == "Hips" ? .14f : (key == "Chest" || key == "Spine") ? .16f : key.StartsWith("UpperArm") ? .065f : key.StartsWith("Forearm") ? .06f : key.StartsWith("Thigh") ? .085f : .075f;
                Vector3 center=bone.position;
                if(key=="Head")center+=root.transform.up*.25f*scale;
                else if(key=="Hips")center+=root.transform.up*.055f*scale;
                else
                {
                    string nextName=key=="Chest"?"Neck":key.Replace("UpperArm","Forearm").Replace("Forearm","Hand");
                    if(key=="Spine")nextName="Chest";
                    if(key.StartsWith("UpperArm"))nextName=key.Replace("UpperArm","Forearm");
                    if(key.StartsWith("Thigh"))nextName=key.Replace("Thigh","Shin");
                    if(key.StartsWith("Shin"))nextName=key.Replace("Shin","Foot");
                    if(bones.TryGetValue(nextName,out var end))center=(bone.position+end.position)*.5f;
                }
                sphere.center=bone.InverseTransformPoint(center);sphere.radius=radius*scale/Mathf.Abs(bone.lossyScale.x);sphere.enabled=false;
                var oldCollider=oldBody.GetComponent<Collider>();if(oldCollider){sphere.sharedMaterial=oldCollider.sharedMaterial;remap[oldCollider]=sphere;}
                remap[oldBody]=body;bodies.Add(body);colliders.Add(sphere);
            }
            foreach(var oldBody in oldBodies)
            {
                var oldJoint=oldBody.GetComponent<CharacterJoint>();if(!oldJoint)continue;
                var body=(Rigidbody)remap[oldBody];var joint=body.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody=oldJoint.connectedBody?(Rigidbody)remap[oldJoint.connectedBody]:null;
                joint.axis=body.transform.InverseTransformDirection(oldBody.transform.TransformDirection(oldJoint.axis)).normalized;
                joint.swingAxis=body.transform.InverseTransformDirection(oldBody.transform.TransformDirection(oldJoint.swingAxis)).normalized;
                joint.lowTwistLimit=oldJoint.lowTwistLimit;joint.highTwistLimit=oldJoint.highTwistLimit;
                joint.swing1Limit=oldJoint.swing1Limit;joint.swing2Limit=oldJoint.swing2Limit;
                joint.enableProjection=oldJoint.enableProjection;joint.projectionDistance=oldJoint.projectionDistance;joint.projectionAngle=oldJoint.projectionAngle;
                joint.enableCollision=oldJoint.enableCollision;joint.enablePreprocessing=oldJoint.enablePreprocessing;
                joint.massScale=oldJoint.massScale;joint.connectedMassScale=oldJoint.connectedMassScale;
                joint.autoConfigureConnectedAnchor=true;remap[oldJoint]=joint;
            }
            var rag=new SerializedObject(ragdoll);rag.FindProperty("m_animator").objectReferenceValue=animator;
            SetArray(rag.FindProperty("m_bones"),bodies.Cast<Object>().ToArray());SetArray(rag.FindProperty("m_colliders"),colliders.Cast<Object>().ToArray());rag.ApplyModifiedPropertiesWithoutUndo();
            foreach(var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(!component || component.transform.IsChildOf(old.transform))continue;
                var so=new SerializedObject(component);var p=so.GetIterator();bool changed=false;
                while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue && remap.TryGetValue(p.objectReferenceValue,out var replacement))
                {p.objectReferenceValue=replacement;changed=true;}
                if(changed)so.ApplyModifiedPropertiesWithoutUndo();
            }
            Object.DestroyImmediate(old);
            // Ungrouped held renderers follow character visibility; specialist prop LOD groups remain independent.
            var bodyLODs=visual.GetComponent<LODGroup>();
            var held=visual.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.GetComponentInParent<LODGroup>()==bodyLODs).ToArray();
            var levels=bodyLODs.GetLODs();
            for(int i=0;i<levels.Length;i++)levels[i].renderers=levels[i].renderers.Concat(held).Distinct().ToArray();
            bodyLODs.SetLODs(levels);
            Require(root.GetComponentsInChildren<Animator>(true).Length==1,"Duplicate Animator");
            Require(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==4,"Expected four new LOD skins");
            Require(root.GetComponentsInChildren<Rigidbody>(true).Length==oldBodies.Length,"Ragdoll count changed");
            Require(root.GetComponent<ThiefAnimationDriver>().Animator==animator,"Animation driver is not rewired");
            var health=new SerializedObject(root.GetComponent<ThiefHealth>());Require(health.FindProperty("m_ragdoll").objectReferenceValue==ragdoll,"Health ragdoll reference is stale");
            var weapon=root.GetComponent<EnemyWeapon>();
            if(weapon){var so=new SerializedObject(weapon);Require(so.FindProperty("m_muzzle").objectReferenceValue!=null,"Missing muzzle");}
            PrefabUtility.SaveAsPrefabAsset(root,PathFor(name));
            ExportPreview(root,visual,name,variant);
            report.Add($"PASS {name}: four body LODs, {variant} controller, {bodies.Count} ragdoll bodies, {weaponSkins.Length} baked weapon skins, {customRoots.Length} preserved attachment roots; gameplay root preserved.");
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void SetArray(SerializedProperty p,Object[] items){p.arraySize=items.Length;for(int i=0;i<items.Length;i++)p.GetArrayElementAtIndex(i).objectReferenceValue=items[i];}
    [Serializable] class Preview { public List<Surface> surfaces=new List<Surface>(); }
    [Serializable] class Surface { public Vector3[] vertices;public Vector3[] normals;public Vector2[] uv;public int[] triangles;public Color color;public string texture; }
    static void ExportPreview(GameObject root,GameObject visual,string name,string variant)
    {
        var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Models/Thieves/thief_enemy_01.fbx").OfType<AnimationClip>().First(c=>c.name==variant+"_Thief_Idle");
        clip.SampleAnimation(visual,.3f);
        var preview=new Preview();
        foreach(var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if(!r.enabled || !r.gameObject.activeInHierarchy)continue;
            if(r is LineRenderer || r is ParticleSystemRenderer)continue;
            if(r is SkinnedMeshRenderer skin && skin.name!="thief_enemy_01")continue;
            var group=r.GetComponentInParent<LODGroup>();
            if(group && group.GetLODs().Any(l=>l.renderers.Contains(r)) && !group.GetLODs()[0].renderers.Contains(r))continue;
            Mesh mesh;
            bool temporary=r is SkinnedMeshRenderer;
            if(r is SkinnedMeshRenderer skinned){mesh=new Mesh();skinned.BakeMesh(mesh,true);}
            else{var filter=r.GetComponent<MeshFilter>();if(!filter||!filter.sharedMesh)continue;mesh=filter.sharedMesh;}
            var v=mesh.vertices;var normals=mesh.normals;
            for(int i=0;i<v.Length;i++)v[i]=r.transform.TransformPoint(v[i]);
            for(int i=0;i<normals.Length;i++)normals[i]=r.transform.TransformDirection(normals[i]).normalized;
            for(int i=0;i<mesh.subMeshCount;i++)
            {
                var mat=r.sharedMaterials[Mathf.Min(i,r.sharedMaterials.Length-1)];
                var tex=mat.HasProperty("_BaseMap")?mat.GetTexture("_BaseMap"):mat.HasProperty("_MainTex")?mat.GetTexture("_MainTex"):null;
                preview.surfaces.Add(new Surface{vertices=v,normals=normals,uv=mesh.uv,triangles=mesh.GetTriangles(i),color=mat.HasProperty("_BaseColor")?mat.GetColor("_BaseColor"):mat.HasProperty("_Color")?mat.GetColor("_Color"):Color.white,texture=tex?System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(tex)):""});
            }
            if(temporary)Object.DestroyImmediate(mesh);
        }
        File.WriteAllText(System.IO.Path.Combine(Report,name+"_Preview.json"),JsonUtility.ToJson(preview));
    }
    public static void Inventory()
    {
        var lines=new List<string>();
        foreach(string n in Names)
        {
            var root=PrefabUtility.LoadPrefabContents(PathFor(n));
            try
            {
                var animator=root.GetComponentInChildren<Animator>(true);
                lines.Add(n+": animator="+PathOf(animator.transform,root.transform)+" scale="+animator.transform.lossyScale+" position="+animator.transform.position);
                foreach(var r in animator.GetComponentsInChildren<Renderer>(true))
                    lines.Add("  RENDERER "+r.GetType().Name+" "+PathOf(r.transform,root.transform)+" material="+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"null")));
                var oldPath=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(animator);
                lines.Add("  OLD MODEL "+oldPath);
                foreach(var t in animator.GetComponentsInChildren<Transform>(true))
                {
                    if(t==animator.transform)continue;
                    string own=SourcePath(t.gameObject);
                    string parent=t.parent?SourcePath(t.parent.gameObject):"";
                    if(own!=oldPath && parent==oldPath)lines.Add("  ATTACHMENT "+PathOf(t,root.transform)+" prefab="+own);
                }
                foreach(var body in animator.GetComponentsInChildren<Rigidbody>(true))lines.Add("  BODY "+body.name+" mass="+body.mass+" scale="+body.transform.lossyScale);
                foreach(var c in root.GetComponents<MonoBehaviour>())
                {
                    if(!c){lines.Add("  MISSING SCRIPT");continue;}
                    var so=new SerializedObject(c);var p=so.GetIterator();
                    while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue is Component item && item.transform.IsChildOf(animator.transform))
                        lines.Add("  REF "+c.GetType().Name+"."+p.propertyPath+" => "+item.GetType().Name+" "+PathOf(item.transform,root.transform));
                }
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        Directory.CreateDirectory(Report);File.WriteAllLines(System.IO.Path.Combine(Report,"PrefabInventory.txt"),lines);Debug.Log("THIEF_PREFAB_INVENTORY_PASS");
    }
}
