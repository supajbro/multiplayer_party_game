using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Editor;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ValidateJumpingPillowGeneration
{
    public static void Run()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath);
        if (!prefab) throw new Exception("Saved pillow prefab missing");
        var region=ScriptableObject.CreateInstance<RegionDefinition>();
        var settings=RegionEnvironmentSettings.SuburbsDefault;
        settings.Prefabs.SmallPark=prefab; settings.Prefabs.MediumPark=prefab; settings.Prefabs.LargePark=prefab;
        settings.Lighting.Enabled=false; settings.Landmarks.Enabled=false;
        settings.Streetlights.Enabled=false;
        region.ConfigureEnvironment(settings);
        var so=new SerializedObject(region);
        so.FindProperty("m_buildNavMesh").boolValue=false;
        so.FindProperty("m_border.Enabled").boolValue=false;
        so.ApplyModifiedPropertiesWithoutUndo();
        var go=new GameObject("Pillow generator test");
        var generator=go.AddComponent<NeighbourhoodGenerator>();
        try
        {
            foreach(int seed in new[]{1847,2864})
            {
                generator.Generate(region,seed);
                var pillows=go.GetComponentsInChildren<JumpingPillow>();
                if(pillows.Length<1 || pillows.Length!=generator.GeneratedParkCount)
                    throw new Exception("Park slots did not all spawn saved jumping pillows for seed "+seed);
                foreach(var pillow in pillows)
                {
                    var expected=prefab.GetComponentsInChildren<Transform>().Length;
                    if(pillow.GetComponentsInChildren<Transform>().Length!=expected)
                        throw new Exception("Legacy park features were added through a pillow");
                    foreach(var f in pillow.GetComponentsInChildren<MeshFilter>())
                    {
                        var source=prefab.GetComponentsInChildren<MeshFilter>().Single(x=>x.name==f.name);
                        var before=source.sharedMesh.vertices; var after=f.sharedMesh.vertices;
                        for(int i=0;i<after.Length;i+=37)
                        {
                            var world=f.transform.TransformPoint(after[i]);
                            float localHeight=source.transform.TransformPoint(before[i]).y-prefab.transform.position.y;
                            if(Mathf.Abs(world.y-(generator.SampleTerrainHeight(world)+localHeight+.01f))>.002f)
                                throw new Exception("Generated pillow not fitted to terrain for seed "+seed);
                        }
                    }
                }
                File.AppendAllText("JumpingPillowValidation.txt","PASS: actual neighbourhood seed "+seed+", "+pillows.Length+" fitted saved pillow(s), no legacy park assemblies.\n");
            }
        }
        finally { generator.Clear(); Object.DestroyImmediate(go); Object.DestroyImmediate(region); }
        Debug.Log("PASS: jumping pillow neighbourhood integration.");
    }
}
