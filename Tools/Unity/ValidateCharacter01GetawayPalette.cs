using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

// Explicit read-only validation in the isolated authoring project.
public static class ValidateCharacter01GetawayPalette
{
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("CHARACTER01_VALIDATION_OUTPUT");
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var lines=new System.Collections.Generic.List<string>();
            string folder="Assets/_Project/Models/Thieves/Character01Thief/";
            foreach(var suffix in new[]{"","Pistol","AssaultRifle","Shotgun"})
            {
                string path=folder+(suffix==""?"":"Armed/")+"Character01_Thief"+(suffix==""?"":"_"+suffix)+".fbx";
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(model,"Missing model: "+path);
                var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s=>s.sharedMesh&&s.sharedMesh.name=="Cube.001");
                if(!skin)skin=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s=>s.sharedMesh);
                Require(skin&&skin.bones.All(b=>b),"Missing character mesh or bones: "+path);
                Require(skin.sharedMaterials.All(m=>m&&m.shader),"Missing character material");
                var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
                if(suffix!="")Require(clips.Length>=5,"Animation clips lost: "+path);
                lines.Add("PASS model "+(suffix==""?"Unarmed":suffix)+": "+skin.sharedMesh.vertexCount+" imported vertices, valid bones/materials, "+clips.Length+" clips");
            }
            foreach(string name in new[]{"Thief","Thief_Pistol","Thief_AssaultRifle","Thief_Shotgun","Thief_RocketLauncher","GrapplerThief","SpeakerThief","thief_enemy_01"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/"+name+".prefab");
                Require(prefab,"Missing prefab "+name);
                Require(prefab.GetComponentsInChildren<Component>(true).All(c=>c),"Missing component "+name);
                var skins=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Require(skins.Length>0&&skins.All(s=>s.sharedMesh&&s.bones.All(b=>b)),"Broken mesh/bone reference "+name);
                Require(skins.All(s=>s.sharedMaterials.All(m=>m&&m.shader)),"Broken material "+name);
                lines.Add("PASS prefab "+name+": existing model, bone and material references resolve");
            }
            File.WriteAllLines(output,lines);
            EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText(output,"FAIL: "+e);Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
