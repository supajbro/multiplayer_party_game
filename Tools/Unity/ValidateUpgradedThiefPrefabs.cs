// Read-only checks on the saved upgraded production prefabs.
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

public static class ValidateUpgradedThiefPrefabs
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run()
    {
        var args=Environment.GetCommandLineArgs();string output=args[Array.IndexOf(args,"-thiefReport")+1];
        var lines=new List<string>();
        try
        {
            foreach(string name in new[]{"Thief","Thief_Pistol","Thief_AssaultRifle","Thief_Shotgun","Thief_RocketLauncher","GrapplerThief","SpeakerThief"})
            {
                var root=PrefabUtility.LoadPrefabContents($"Assets/_Project/Prefabs/Enemies/{name}.prefab");
                try
                {
                    Require(root.GetComponentsInChildren<Component>(true).All(c=>c!=null),name+": missing script");
                    Require(root.GetComponent<NetworkObject>() && root.GetComponent<ThiefNavigator>() && root.GetComponent<ThiefHealth>(),name+": missing gameplay lifecycle");
                    var animator=root.GetComponentInChildren<Animator>(true);
                    Require(root.GetComponentsInChildren<Animator>(true).Length==1 && animator.avatar && animator.avatar.isValid,name+": invalid animator/avatar");
                    var visual=animator.gameObject;var group=visual.GetComponent<LODGroup>();
                    Require(group && group.GetLODs().Length==4,name+": missing body LODs");
                    int[] budgets={10000,4000,1500,700};var levels=group.GetLODs();
                    for(int i=0;i<4;i++)
                    {
                        var skins=levels[i].renderers.OfType<SkinnedMeshRenderer>().ToArray();
                        Require(skins.Length==1 && skins[0].sharedMesh.triangles.Length/3==budgets[i],name+": invalid body LOD budget");
                        Require(skins[0].bones.All(b=>b && b.IsChildOf(visual.transform)),name+": broken skin bone");
                    }
                    var driver=root.GetComponent<ThiefAnimationDriver>();Require(driver.Animator==animator,name+": stale animation reference");
                    var health=new SerializedObject(root.GetComponent<ThiefHealth>());
                    var ragdoll=health.FindProperty("m_ragdoll").objectReferenceValue as PedestrianRagdoll;
                    Require(ragdoll && ragdoll.gameObject==visual,name+": stale ragdoll reference");
                    var rag=new SerializedObject(ragdoll);var bodies=rag.FindProperty("m_bones");var colliders=rag.FindProperty("m_colliders");
                    Require(bodies.arraySize==11 && colliders.arraySize==11 && ((Rigidbody)bodies.GetArrayElementAtIndex(0).objectReferenceValue).name=="Hips",name+": invalid ragdoll arrays");
                    for(int i=0;i<11;i++)
                    {
                        var body=bodies.GetArrayElementAtIndex(i).objectReferenceValue as Rigidbody;
                        var sphere=colliders.GetArrayElementAtIndex(i).objectReferenceValue as SphereCollider;
                        Require(body && body.isKinematic && body.transform.IsChildOf(visual.transform),name+": invalid ragdoll body");
                        Require(sphere && !sphere.enabled && sphere.radius*Mathf.Abs(sphere.transform.lossyScale.x)>.04f && sphere.radius*Mathf.Abs(sphere.transform.lossyScale.x)<.4f,name+": invalid fitted collider");
                    }
                    Require(visual.GetComponentsInChildren<CharacterJoint>(true).Length==10 && visual.GetComponentsInChildren<CharacterJoint>(true).All(j=>j.connectedBody),name+": disconnected ragdoll joints");
                    var weapon=root.GetComponent<EnemyWeapon>();
                    if(weapon)
                    {
                        var so=new SerializedObject(weapon);var muzzle=so.FindProperty("m_muzzle").objectReferenceValue as Transform;
                        Require(muzzle && muzzle.IsChildOf(visual.transform),name+": broken weapon muzzle");
                        var held=so.FindProperty("m_heldWeaponRenderers");
                        Require(held.arraySize>0,name+": missing held weapon renderers");
                        for(int i=0;i<held.arraySize;i++)Require(held.GetArrayElementAtIndex(i).objectReferenceValue is Renderer,name+": stale held renderer");
                    }
                    if(root.GetComponent<GrapplerThief>())
                    {
                        var so=new SerializedObject(root.GetComponent<GrapplerThief>());
                        foreach(string field in new[]{"m_gun","m_muzzle"})Require(so.FindProperty(field).objectReferenceValue is Transform t && t.IsChildOf(visual.transform),"Grappler lost "+field);
                        Require(so.FindProperty("m_rope").objectReferenceValue!=null,"Grappler lost rope");
                    }
                    if(root.GetComponent<SpeakerThief>())
                    {
                        Require(!weapon,"Speaker gained attack component");
                        var speaker=visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="ShoulderSpeaker");
                        Require(speaker && speaker.parent.name=="Shoulder.R" && speaker.GetComponent<LODGroup>().GetLODs().Length==3,"Speaker mount/prop LODs lost");
                    }
                    animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
                    foreach(string state in new[]{"Idle","Walk","Shoot","Death","Frustration"})
                    {
                        Require(animator.HasState(0,Animator.StringToHash("Base Layer."+state)),name+": missing state "+state);
                        animator.Play("Base Layer."+state,0,.4f);animator.Update(0);
                        Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer."+state),name+": state playback failed");
                        var skin=levels[0].renderers.OfType<SkinnedMeshRenderer>().Single();var mesh=new Mesh();skin.BakeMesh(mesh,true);
                        var positions=mesh.vertices.Select(v=>root.transform.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                        Require(positions.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)&&v.magnitude<3f),name+": invalid animated geometry");
                        if(state=="Idle")Require(positions.Max(v=>v.y)<1.9f && positions.Max(v=>v.y)>1.7f,name+": visual scale changed");
                        Object.DestroyImmediate(mesh);
                    }
                    lines.Add("PASS "+name+": saved prefab loads with no missing scripts, four LOD budgets, shared new skeleton, valid weapons/attachments, 11 fitted ragdoll bodies/10 joints, remapped gameplay references, and all five Animator states playing at the original visual scale.");
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            File.WriteAllLines(Path.Combine(output,"SavedPrefabValidation.txt"),lines);Debug.Log("UPGRADED_THIEF_PREFABS_VALIDATION_PASS");
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"SavedPrefabValidationFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
