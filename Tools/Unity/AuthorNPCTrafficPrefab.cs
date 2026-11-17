// Explicit offline prefab authoring and verification; no automatic callbacks.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using CouchGuys.Gameplay.Traffic;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class AuthorNPCTrafficPrefab
{
    const string Path="Assets/_Project/Prefabs/Vehicles/TrafficCar.prefab";
    const string VisualFolder="Assets/_Project/Prefabs/Vehicles/NPCTraffic";
    static readonly string[] Names={"NPC_CityHatch","NPC_FamilySUV","NPC_UtilityPickup","NPC_SportCoupe"};
    public static void Run()
    {
        var report=new List<string>();
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if(!AssetDatabase.IsValidFolder(VisualFolder))AssetDatabase.CreateFolder("Assets/_Project/Prefabs/Vehicles","NPCTraffic");
            var visuals=new GameObject[4];
            for(int i=0;i<4;i++)
            {
                var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/Vehicles/NPCTraffic/"+Names[i]+".fbx");
                Require(model!=null,Names[i]+": model missing");
                var wrapper=new GameObject(Names[i]);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(wrapper.transform,false);
                var renderers=instance.GetComponentsInChildren<Renderer>();
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                // Keep proportions and ground pivot while fitting the existing traffic footprint.
                float scale=Mathf.Min(2f/bounds.size.x,3.75f/bounds.size.z);
                instance.transform.localScale=Vector3.one*scale;
                visuals[i]=PrefabUtility.SaveAsPrefabAsset(wrapper,VisualFolder+"/"+Names[i]+".prefab");
                Object.DestroyImmediate(wrapper);
                report.Add(Names[i]+": saved visual; uniform model scale="+scale);
            }
            var root=PrefabUtility.LoadPrefabContents(Path);
            try
            {
                var controller=root.GetComponent<TrafficVehicleController>();
                var serialized=new SerializedObject(controller);
                var anchor=(Transform)serialized.FindProperty("m_visualAnchor").objectReferenceValue;
                Require(anchor!=null,"Missing saved visual anchor");
                var network=root.GetComponent<NetworkObject>();ulong hash=network.AssetPathHash;
                var body=root.GetComponent<Rigidbody>();float mass=body.mass;
                var colliders=root.GetComponents<Collider>();int colliderCount=colliders.Length;
                foreach(Transform child in anchor.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
                var variants=new GameObject[4];
                for(int i=0;i<4;i++)
                {
                    variants[i]=(GameObject)PrefabUtility.InstantiatePrefab(visuals[i],anchor);
                    variants[i].SetActive(i==0);
                }
                SetArray(serialized,"m_visualPrefabs",visuals);
                SetArray(serialized,"m_visualVariants",variants);
                SetArray(serialized,"m_renderers",variants[0].GetComponentsInChildren<Renderer>(true));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Require(network.AssetPathHash==hash&&body.mass==mass&&root.GetComponents<Collider>().Length==colliderCount,"Nonvisual settings changed");
                Require(controller.VisualVariantCount==4,"Wrong variant count");
                var apply=typeof(TrafficVehicleController).GetMethod("ApplyVisualVariant",BindingFlags.Instance|BindingFlags.NonPublic);
                foreach(int selected in new[]{0,1,2,3,7,0})
                {
                    apply.Invoke(controller,new object[]{selected});
                    Require(variants.Count(v=>v.activeSelf)==1&&variants[selected%4].activeSelf,"Variant selection failed: "+selected);
                    var rendererArray=new SerializedObject(controller).FindProperty("m_renderers");
                    Require(rendererArray.arraySize==variants[selected%4].GetComponentsInChildren<Renderer>(true).Length,"Renderer cache mismatch");
                }
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root)==0,"Missing traffic script");
                PrefabUtility.SaveAsPrefabAsset(root,Path);
                report.Add("TrafficCar: PASS; 4 selectable variants; exactly one active for every index; renderer cache follows selection; existing Rigidbody, colliders and FishNet hash preserved ("+hash+").");
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            var settings=AssetDatabase.LoadAssetAtPath<TrafficSettings>("Assets/_Project/Resources/Traffic/TrafficSettings.asset");
            var settingsSo=new SerializedObject(settings);
            settingsSo.FindProperty("m_carPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            settingsSo.ApplyModifiedPropertiesWithoutUndo();
            Require(settings.CarPrefab!=null,"Traffic settings prefab reference missing");
            AssetDatabase.SaveAssets();
            report.Add("TrafficSettings: PASS; serialized prefab reference resolves outside Resources.");
            File.WriteAllLines("TrafficPrefabValidation.txt",report);EditorApplication.Exit(0);
        }
        catch(Exception error){report.Add("FAIL: "+error);File.WriteAllLines("TrafficPrefabValidation.txt",report);Debug.LogException(error);EditorApplication.Exit(1);}
    }
    static void SetArray(SerializedObject so,string property,Object[] values)
    {
        var array=so.FindProperty(property);array.arraySize=values.Length;
        for(int i=0;i<values.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=values[i];
    }
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
