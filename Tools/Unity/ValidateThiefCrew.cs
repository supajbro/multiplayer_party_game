// Explicit read-only validation in the isolated Unity project. No import hooks.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class ValidateThiefCrew
{
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Run()
    {
        var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-thiefReport")+1];
        var lines=new List<string>();
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Thieves/ThiefEnemy01/thief_enemy_01.mat");
            Require(material && material.shader && material.shader.name=="Universal Render Pipeline/Lit","Missing URP material/shader");
            foreach(string property in new[]{"_BaseMap","_BumpMap","_MetallicGlossMap","_OcclusionMap"})
                Require(material.GetTexture(property)!=null,"Missing crew map: "+property);
            Require(material.GetTexture("_BaseMap").width==1024,"Unexpected runtime atlas size");
            var clips=AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Models/Thieves/thief_enemy_01.fbx").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            Require(clips.Length==25,"Expected all 25 original clips");
            lines.Add("PASS: Shared URP material imports all crew textures; 1024px runtime atlas and 25 original clips retained.");
            foreach(string name in new[]{"thief_enemy_01","Thief","Thief_Pistol","Thief_AssaultRifle","Thief_Shotgun","Thief_RocketLauncher","GrapplerThief","SpeakerThief"})
            {
                var root=PrefabUtility.LoadPrefabContents($"Assets/_Project/Prefabs/Enemies/{name}.prefab");
                try
                {
                    Require(root.GetComponentsInChildren<Component>(true).All(c=>c!=null),name+": missing component");
                    var animator=root.GetComponentInChildren<Animator>(true);
                    Require(animator&&animator.avatar&&animator.avatar.isValid,name+": invalid rig/avatar");
                    animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
                    var group=animator.GetComponent<LODGroup>();Require(group&&group.lodCount==4,name+": lost LODs");
                    var skins=group.GetLODs().Select(l=>l.renderers.OfType<SkinnedMeshRenderer>().Single()).ToArray();
                    for(int i=0;i<4;i++)
                    {
                        Require(skins[i].sharedMaterial==material,name+": LOD does not use crew material");
                        Require(skins[i].sharedMesh.triangles.Length/3==new[]{10000,4000,1500,700}[i],name+": LOD geometry changed");
                        Require(skins[i].bones.Length==22&&skins[i].bones.All(b=>b),name+": missing bone");
                    }
                    var hand=animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Hand.R");
                    var handle=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="HandleCenter");
                    var grip=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="WeaponGrip");
                    var baked=new Mesh();
                    foreach(string state in new[]{"Idle","Walk","Shoot","Frustration","Death"})
                    {
                        for(int sample=0;sample<13;sample++)
                        {
                            animator.Play("Base Layer."+state,0,(sample+.5f)/13f);animator.Update(0);
                            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer."+state),name+": failed "+state);
                            foreach(var skin in skins)
                            {
                                skin.BakeMesh(baked,true);
                                foreach(var v in baked.vertices)
                                {
                                    Require(float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z),name+": nonfinite skinning");
                                    Require(skin.localBounds.Contains(v),name+": animated vertex outside saved bounds");
                                }
                            }
                            if(handle&&grip)Require(Vector3.Distance(handle.position,grip.position)<.001f&&grip.parent==hand,name+": weapon mount drift");
                        }
                        lines.Add($"PASS: {name} {state}, 13 poses on all four LODs; finite skinning, saved bounds and weapon attachments.");
                    }
                    Object.DestroyImmediate(baked);
                    animator.SetBool("Dead",false);animator.SetFloat("Speed",0);animator.Play("Base Layer.Idle",0,0);animator.Update(0);
                    animator.SetFloat("Speed",1);for(int i=0;i<20;i++)animator.Update(1f/30);
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Walk"),name+": walk transition failed");
                    animator.SetBool("Dead",true);animator.SetTrigger("Shoot");for(int i=0;i<20;i++)animator.Update(1f/30);
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Death"),name+": death priority failed");
                    lines.Add("PASS: "+name+" Speed transition and Death priority.");
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            File.WriteAllLines(Path.Combine(output,"UnityCrewValidation.txt"),lines);
            Debug.Log("THIEF_CREW_VALIDATION_PASS");
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"UnityCrewFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
