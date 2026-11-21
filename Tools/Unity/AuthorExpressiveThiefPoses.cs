// Explicit animation/prefab updates only; no import, reload or Play-mode builders.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;

public static class AuthorExpressiveThiefPoses
{
    const string Model="Assets/_Project/Models/Thieves/thief_enemy_01.fbx";
    const string Visual="Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab";
    const string Controllers="Assets/_Project/Animations/Thieves/ThiefEnemy01";
    static readonly string[] Names={"thief_enemy_01","Thief","Thief_Pistol","Thief_AssaultRifle","Thief_Shotgun","Thief_RocketLauncher","GrapplerThief","SpeakerThief"};
    static readonly string[] Variants={"Pistol","AssaultRifle","Shotgun","Unarmed","Speaker"};
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static string Output { get { var a=Environment.GetCommandLineArgs();return a[Array.IndexOf(a,"-thiefReport")+1]; } }
    static string Normalize(string name)=>name.Substring(name.LastIndexOf('|')+1);
    static string Variant(string name)=>name=="Thief"||name=="thief_enemy_01"?"Unarmed":name=="SpeakerThief"?"Speaker":name.Contains("AssaultRifle")||name.Contains("Rocket")?"AssaultRifle":name.Contains("Shotgun")?"Shotgun":"Pistol";

    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
            var clips=importer.clipAnimations.ToList();
            var defaults=importer.defaultClipAnimations;
            Require(defaults.Length==25,"Expected 25 named actions in candidate FBX");
            foreach(var item in defaults)
            {
                string name=Normalize(item.name);
                if(clips.Any(c=>c.name==name))continue;
                item.name=name;item.loopTime=item.loopPose=name.EndsWith("_Idle")||name.EndsWith("_Walk");
                item.lockRootRotation=item.lockRootHeightY=item.lockRootPositionXZ=true;
                item.keepOriginalOrientation=item.keepOriginalPositionY=item.keepOriginalPositionXZ=true;
                clips.Add(item);
            }
            importer.clipAnimations=clips.ToArray();importer.SaveAndReimport();
            var motions=AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToDictionary(c=>c.name);
            Require(motions.Count==25,"Not all authored clips imported");
            foreach(string variant in Variants)
            {
                string path=$"{Controllers}/thief_enemy_01_{variant}.controller";
                if(!File.Exists(path))Require(AssetDatabase.CopyAsset($"{Controllers}/thief_enemy_01_Pistol.controller",path),"Could not create "+variant+" controller");
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                foreach(var child in controller.layers[0].stateMachine.states)
                {
                    if(motions.TryGetValue(variant+"_Thief_"+child.state.name,out var clip))
                    { if(child.state.motion!=clip){child.state.motion=clip;EditorUtility.SetDirty(child.state);} }
                }
            }
            AssetDatabase.SaveAssets();
            UpdateBounds(motions.Values.ToArray());
            foreach(string name in Names)
            {
                string path=$"Assets/_Project/Prefabs/Enemies/{name}.prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var animator=root.GetComponentInChildren<Animator>(true);
                    animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<AnimatorController>($"{Controllers}/thief_enemy_01_{Variant(name)}.controller");
                    if(name=="SpeakerThief")FitSpeakerToCarryingSide(root,animator);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();
            ValidateAndPreview();
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Output,"UnityPoseFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }

    static void UpdateBounds(AnimationClip[] clips)
    {
        var root=PrefabUtility.LoadPrefabContents(Visual);
        try
        {
            var transforms=root.GetComponentsInChildren<Transform>(true);
            var positions=transforms.Select(t=>t.localPosition).ToArray();
            var rotations=transforms.Select(t=>t.localRotation).ToArray();
            var scales=transforms.Select(t=>t.localScale).ToArray();
            var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds=skins.Select(s=>s.localBounds).ToArray();
            var mesh=new Mesh();
            for(int i=0;i<skins.Length;i++)bounds[i].Encapsulate(skins[i].sharedMesh.bounds);
            foreach(var clip in clips)
                for(int sample=0;sample<=12;sample++)
                {
                    clip.SampleAnimation(root,clip.length*sample/12f);
                    for(int i=0;i<skins.Length;i++)
                    {skins[i].BakeMesh(mesh,true);mesh.RecalculateBounds();bounds[i].Encapsulate(mesh.bounds);}
                }
            Object.DestroyImmediate(mesh);
            for(int i=0;i<transforms.Length;i++){transforms[i].SetLocalPositionAndRotation(positions[i],rotations[i]);transforms[i].localScale=scales[i];}
            for(int i=0;i<skins.Length;i++)
            {
                var s=skins[i].transform.lossyScale;
                bounds[i].Expand(new Vector3(.06f/Mathf.Abs(s.x),.06f/Mathf.Abs(s.y),.06f/Mathf.Abs(s.z)));
                skins[i].localBounds=bounds[i];
            }
            PrefabUtility.SaveAsPrefabAsset(root,Visual);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    static void FitSpeakerToCarryingSide(GameObject root,Animator animator)
    {
        var transforms=animator.GetComponentsInChildren<Transform>(true);
        var mount=transforms.Single(t=>t.name=="ShoulderSpeaker");
        var shoulder=transforms.Single(t=>t.name=="Shoulder.R");
        var head=transforms.Single(t=>t.name=="Head");
        float side=Mathf.Sign(Vector3.Dot(shoulder.position-head.position,root.transform.right));
        var renderers=mount.GetComponent<LODGroup>().GetLODs()[0].renderers;
        var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);
        float clearance=.26f*animator.transform.lossyScale.x+.04f;
        float offset=side>0?head.position.x+clearance-bounds.min.x:head.position.x-clearance-bounds.max.x;
        mount.position+=root.transform.right*offset;
    }

    public static void ValidateAndPreview()
    {
        var lines=new List<string>();
        foreach(string name in Names)
        {
            var root=PrefabUtility.LoadPrefabContents($"Assets/_Project/Prefabs/Enemies/{name}.prefab");
            try
            {
                Require(root.GetComponentsInChildren<Component>(true).All(c=>c!=null),name+": missing script");
                var animator=root.GetComponentInChildren<Animator>(true);
                Require(animator.avatar&&animator.avatar.isValid,name+": avatar is invalid");
                string variant=Variant(name);
                Require(animator.runtimeAnimatorController.name=="thief_enemy_01_"+variant,name+": wrong controller");
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
                var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);Require(skins.Length==4,"LOD count changed");
                var mesh=new Mesh();
                var left=animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Hand.L");
                var right=animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Hand.R");
                float separation=float.PositiveInfinity;
                foreach(string state in new[]{"Idle","Walk","Shoot","Frustration","Death"})
                    for(int sample=0;sample<8;sample++)
                    {
                        animator.Play("Base Layer."+state,0,(sample+.5f)/8f);animator.Update(0);
                        Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer."+state),name+": failed state "+state);
                        if(state!="Death")separation=Mathf.Min(separation,Vector3.Distance(left.position,right.position));
                        foreach(var skin in skins)
                        {
                            skin.BakeMesh(mesh,true);
                            foreach(var v in mesh.vertices)
                            {
                                Require(skin.localBounds.Contains(v),name+": pose outside skin bounds");
                                var world=root.transform.InverseTransformPoint(skin.transform.TransformPoint(v));
                                Require(float.IsFinite(world.x)&&float.IsFinite(world.y)&&float.IsFinite(world.z)&&world.magnitude<3f,name+": invalid animated geometry");
                            }
                        }
                    }
                Require(separation>.22f,name+": hands still too close");
                Object.DestroyImmediate(mesh);
                // Restore saved culling mode on the temporary preview object only.
                var export=typeof(UpgradeThiefEnemyPrefabs).GetMethod("ExportPreview",BindingFlags.Static|BindingFlags.NonPublic);
                export.Invoke(null,new object[]{root,animator.gameObject,name,variant});
                lines.Add($"PASS {name}: {variant} acting, all five states, four unchanged body LODs, valid avatar and references, pose-aware bounds, minimum sampled wrist separation {separation:F3}m.");
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        File.WriteAllLines(Path.Combine(Output,"UnityPoseValidation.txt"),lines);
        Debug.Log("EXPRESSIVE_THIEF_POSES_PASS");
    }
}
