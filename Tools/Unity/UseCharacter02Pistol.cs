// Explicit one-prefab edit and validation. No automatic callbacks.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class UseCharacter02Pistol
{
    const string Prefab="Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab";
    const string Old="Assets/_Project/Models/Thieves/Character01Thief/Armed/Character01_Thief_Pistol.fbx";
    const string New="Assets/_Project/Models/Thieves/Character02Thief/Armed/Character02_Thief_Pistol.fbx";
    const string Controller="Assets/_Project/Animations/Thieves/Character02/Character02_Thief_Pistol.overrideController";
    static void Require(bool ok,string why){if(!ok)throw new Exception(why);}
    public static void Run()
    {
        var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-character02Report")+1];
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var oldModel=AssetDatabase.LoadAssetAtPath<GameObject>(Old);var newModel=AssetDatabase.LoadAssetAtPath<GameObject>(New);
            Require(oldModel&&newModel,"Missing pistol source model");
            string Canonical(Transform t,Transform root)=>AnimationUtility.CalculateTransformPath(t,root).Replace("Character02","Character01");
            var targets=newModel.GetComponentsInChildren<Transform>(true).ToDictionary(t=>Canonical(t,newModel.transform));
            var map=new Dictionary<long,long>{{100100000,100100000}};
            void Pair(Object a,Object b){if(a&&b){AssetDatabase.TryGetGUIDAndLocalFileIdentifier(a,out string ag,out long ai);AssetDatabase.TryGetGUIDAndLocalFileIdentifier(b,out string bg,out long bi);map[ai]=bi;}}
            foreach(var old in oldModel.GetComponentsInChildren<Transform>(true))
            {
                Require(targets.TryGetValue(Canonical(old,oldModel.transform),out var target),"Missing corresponding source node: "+old.name);
                Pair(old.gameObject,target.gameObject);
                foreach(var c in old.GetComponents<Component>())
                {
                    var peers=target.GetComponents(c.GetType());if(peers.Length==0)continue;Pair(c,peers[0]);
                }
                var os=old.GetComponent<SkinnedMeshRenderer>();var ns=target.GetComponent<SkinnedMeshRenderer>();if(os&&ns)Pair(os.sharedMesh,ns.sharedMesh);
            }
            string oldGuid=AssetDatabase.AssetPathToGUID(Old),newGuid=AssetDatabase.AssetPathToGUID(New);
            string text=File.ReadAllText(Prefab);
            text=Regex.Replace(text,@"\{fileID:\s*(-?\d+), guid: "+oldGuid+@", type:\s*\d+\}",m=>{
                long id=long.Parse(m.Groups[1].Value);Require(map.ContainsKey(id),"Unmapped model reference: "+id);
                return "{fileID: "+map[id]+", guid: "+newGuid+", type: 3}";
            });
            File.WriteAllText(Prefab,text);AssetDatabase.ImportAsset(Prefab,ImportAssetOptions.ForceSynchronousImport);
            if(!AssetDatabase.IsValidFolder("Assets/_Project/Animations/Thieves/Character02"))AssetDatabase.CreateFolder("Assets/_Project/Animations/Thieves","Character02");
            var root=PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var animator=root.GetComponentInChildren<Animator>(true);Require(animator&&animator.runtimeAnimatorController,"Missing current controller");
                var replacement=new AnimatorOverrideController(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Animations/Thieves/Character01/Character01_Thief_Pistol.controller"));
                var clips=AssetDatabase.LoadAllAssetsAtPath(New).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToDictionary(c=>c.name.Split('|').Last());
                var overrides=new List<KeyValuePair<AnimationClip,AnimationClip>>();replacement.GetOverrides(overrides);
                for(int i=0;i<overrides.Count;i++){Require(clips.TryGetValue(overrides[i].Key.name.Split('|').Last(),out var clip),"Missing Character02 clip "+overrides[i].Key.name);overrides[i]=new KeyValuePair<AnimationClip,AnimationClip>(overrides[i].Key,clip);}
                replacement.ApplyOverrides(overrides);AssetDatabase.CreateAsset(replacement,Controller);animator.runtimeAnimatorController=replacement;animator.avatar=null;
                // Save the replacement skeleton in its mesh bind pose, before any
                // Animator sampling; inherited weapon-pose overrides must not win.
                var bodySkin=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s=>s.sharedMesh.vertexCount).First();
                var bind=bodySkin.sharedMesh.bindposes;
                var rest=bodySkin.bones.Select((bone,i)=>new{bone,world=bodySkin.transform.localToWorldMatrix*bind[i].inverse}).OrderBy(v=>v.bone.GetComponentsInParent<Transform>(true).Length).ToArray();
                foreach(var item in rest){var local=item.bone.parent.worldToLocalMatrix*item.world;item.bone.SetLocalPositionAndRotation(local.GetColumn(3),local.rotation);item.bone.localScale=local.lossyScale;}
                RequireTPose(root);PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            var saved=PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                Require(saved.GetComponentsInChildren<Component>(true).All(c=>c),"Missing component");RequireTPose(saved);
                var animator=saved.GetComponentInChildren<Animator>(true);var skins=saved.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Require(skins.All(s=>s.sharedMesh&&AssetDatabase.GetAssetPath(s.sharedMesh)==New&&s.bones.All(b=>b)&&s.sharedMaterials.All(m=>m&&m.shader)),"Incorrect mesh/material/bone reference");
                foreach(var component in saved.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    var so=new SerializedObject(component);
                    foreach(string field in new[]{"m_animator","m_ragdoll","m_muzzle"}){var p=so.FindProperty(field);if(p!=null&&p.propertyType==SerializedPropertyType.ObjectReference)Require(p.objectReferenceValue,"Missing "+field);}
                }
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
                var body=skins.OrderByDescending(s=>s.sharedMesh.vertexCount).First();var baked=new Mesh();Vector3[] firstWalk=null;float motion=0;
                foreach(string state in new[]{"Idle","Walk","Shoot","Death","Frustration"})foreach(float time in new[]{0f,.25f,.5f,.75f})
                {
                    animator.Play("Base Layer."+state,0,time);animator.Update(0);Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer."+state),"State did not play: "+state);
                    foreach(var skin in skins){skin.BakeMesh(baked,true);Require(baked.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Invalid skinning");}
                    if(state=="Walk"){body.BakeMesh(baked,true);if(firstWalk==null)firstWalk=baked.vertices;else motion=Mathf.Max(motion,baked.vertices.Select((v,i)=>body.transform.TransformVector(v-firstWalk[i]).magnitude).Max());}
                }
                var walkClip=AssetDatabase.LoadAllAssetsAtPath(New).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__")&&c.name.EndsWith("Thief_Walk"));
                Require(motion>.003f,"Character02 clips did not deform the replacement mesh; motion="+motion+"; clip paths="+string.Join(",",AnimationUtility.GetCurveBindings(walkClip).Select(b=>b.path).Distinct().Take(6))+"; actual bones="+string.Join(",",body.bones.Take(6).Select(b=>AnimationUtility.CalculateTransformPath(b,animator.transform))));Object.DestroyImmediate(baked);
                File.WriteAllLines(Path.Combine(output,"UnityPistolValidation.txt"),new[]{"PASS: saved Character02 pistol model/prefab defaults to T-pose.","PASS: Character02 meshes, materials, bone references and existing gameplay/weapon links resolve.","PASS: matching Character02 clips run all five existing controller states; walk deformation="+motion.ToString("F4")+"; no nonfinite skinning."});
            }
            finally{PrefabUtility.UnloadPrefabContents(saved);}
            EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"UnityPistolFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void RequireTPose(GameObject root)
    {
        Transform Bone(string suffix)=>root.GetComponentsInChildren<Transform>(true).Single(t=>t.name.EndsWith(suffix,StringComparison.Ordinal));
        var left=Bone("LeftHand");var right=Bone("RightHand");
        Require(Mathf.Abs(left.position.y-Bone("LeftArm").position.y)<.09f&&Mathf.Abs(right.position.y-Bone("RightArm").position.y)<.09f&&Mathf.Abs(left.position.x-right.position.x)>.8f,"Default pose is not a T-pose: hands="+left.position+" / "+right.position+"; upper arms="+Bone("LeftArm").position+" / "+Bone("RightArm").position);
    }
}
