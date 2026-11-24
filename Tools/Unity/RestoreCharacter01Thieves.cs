// Explicit saved-asset authoring/verification; no automatic callbacks.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using FishNet.Object;
using Object=UnityEngine.Object;

public static class RestoreCharacter01Thieves
{
    static readonly string[] Names={"Thief","Thief_Pistol","Thief_AssaultRifle","Thief_Shotgun","Thief_RocketLauncher","GrapplerThief","SpeakerThief","thief_enemy_01"};
    const string Folder="Assets/_Project/Prefabs/Enemies/";
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Run()
    {
        var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-restoreReport")+1];
        var lines=new List<string>();
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var basePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"Thief.prefab");
            var defaultController=basePrefab.GetComponentInChildren<Animator>(true).runtimeAnimatorController;
            var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Thieves/Character01Thief/Character01_Thief.fbx");
            Require(model,"Missing original Character01 model");
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(model);
            var animator=visual.GetComponentInChildren<Animator>(true);
            Require(animator&&defaultController,"Character01 model has no Animator/controller");
            // The original Character01 controllers contain Generic transform clips.
            // Their archived gameplay Animator setup intentionally has no Avatar.
            animator.avatar=null;animator.runtimeAnimatorController=defaultController;animator.applyRootMotion=false;
            PrefabUtility.SaveAsPrefabAsset(visual,Folder+"thief_enemy_01.prefab");Object.DestroyImmediate(visual);
            AssetDatabase.SaveAssets();
            foreach(string name in Names)
            {
                var root=PrefabUtility.LoadPrefabContents(Folder+name+".prefab");
                try
                {
                    Require(root.GetComponentsInChildren<Component>(true).All(c=>c),name+": missing component/script");
                    var animators=root.GetComponentsInChildren<Animator>(true);Require(animators.Length==1,name+": wrong Animator count");
                    animator=animators[0];Require(animator.runtimeAnimatorController&&(!animator.avatar||animator.avatar.isValid),name+": invalid Generic animation setup");
                    var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);Require(skins.Length>0,name+": no character skin");
                    foreach(var skin in skins)
                    {
                        Require(skin.sharedMesh&&skin.bones.All(b=>b),name+": missing mesh/bones");
                        string meshPath=AssetDatabase.GetAssetPath(skin.sharedMesh);
                        Require(meshPath.StartsWith("Assets/_Project/Models/Thieves/Character01Thief/"),name+": still uses a different character: "+meshPath);
                        Require(skin.sharedMaterials.All(m=>m&&m.shader),name+": missing material");
                    }
                    if(name!="thief_enemy_01")
                    {
                        Require(root.GetComponent<NetworkObject>()&&root.GetComponent<ThiefHealth>()&&root.GetComponent<ThiefNavigator>(),name+": lost gameplay/network components");
                        Require(root.GetComponent<ThiefAnimationDriver>().Animator==animator,name+": stale animation driver");
                        var rag=new SerializedObject(root.GetComponent<ThiefHealth>()).FindProperty("m_ragdoll").objectReferenceValue as PedestrianRagdoll;
                        Require(rag&&rag.transform.IsChildOf(root.transform),name+": missing ragdoll");
                        var rs=new SerializedObject(rag);
                        foreach(string field in new[]{"m_bones","m_colliders"})
                        {
                            var a=rs.FindProperty(field);Require(a.arraySize>0,name+": empty ragdoll");
                            for(int i=0;i<a.arraySize;i++)Require(a.GetArrayElementAtIndex(i).objectReferenceValue,name+": stale ragdoll reference");
                        }
                        Require(rag.GetComponentsInChildren<CharacterJoint>(true).All(j=>j.connectedBody),name+": unconnected ragdoll joint");
                        var weapon=root.GetComponent<EnemyWeapon>();
                        if(weapon)
                        {
                            var so=new SerializedObject(weapon);Require(so.FindProperty("m_muzzle").objectReferenceValue is Transform,name+": missing muzzle");
                            var held=so.FindProperty("m_heldWeaponRenderers");Require(held.arraySize>0,name+": no held weapon");
                            for(int i=0;i<held.arraySize;i++)Require(held.GetArrayElementAtIndex(i).objectReferenceValue is Renderer,name+": stale weapon renderer");
                        }
                        var grapple=root.GetComponent<GrapplerThief>();
                        if(grapple)
                        {
                            var so=new SerializedObject(grapple);
                            foreach(string f in new[]{"m_gun","m_muzzle","m_rope"})Require(so.FindProperty(f).objectReferenceValue,name+": missing grapple "+f);
                        }
                        if(root.GetComponent<SpeakerThief>())Require(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="ShoulderSpeaker"),"Speaker prop lost");
                    }
                    animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
                    var baked=new Mesh();
                    foreach(string state in new[]{"Idle","Walk","Shoot","Death","Frustration"})
                    {
                        Require(animator.HasState(0,Animator.StringToHash("Base Layer."+state)),name+": missing state "+state);
                        foreach(float time in new[]{0f,.25f,.5f,.75f,.99f})
                        {
                            animator.Play("Base Layer."+state,0,time);animator.Update(0);
                            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer."+state),name+": cannot enter "+state);
                            foreach(var skin in skins)
                            {
                                skin.BakeMesh(baked,true);
                                Require(baked.vertexCount>0&&baked.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),name+": invalid animated skin");
                            }
                        }
                    }
                    Object.DestroyImmediate(baked);
                    lines.Add("PASS: "+name+" uses Character01_Thief meshes, valid materials/avatar/controller; five states sampled at five times; gameplay, ragdoll and weapon/prop references resolve.");
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            File.WriteAllLines(Path.Combine(output,"UnityRestoreValidation.txt"),lines);EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"UnityRestoreFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
