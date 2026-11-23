// Explicit isolated authoring. No automatic import, reload or Play mode callbacks.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class AuthorCityHatchLODs
{
    const string Model="Assets/_Project/Models/Vehicles/NPCTraffic/";
    const string Prefab="Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_CityHatch.prefab";
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var root=PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var original=root.GetComponentsInChildren<MeshRenderer>().Single();
                var originalTransform=original.transform;
                var group=root.GetComponent<LODGroup>()??root.AddComponent<LODGroup>();
                var lods=new LOD[3];lods[0]=new LOD(.22f,new Renderer[]{original});
                for(int level=1;level<=2;level++)
                {
                    string path=Model+"NPC_CityHatch_LOD"+level+".fbx";
                    var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                    foreach(string suffix in new[]{"Details","BodyPaint"})
                    {
                        string name="NPC_CityHatch_"+suffix;
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),name),AssetDatabase.LoadAssetAtPath<Material>(Model+"Materials/"+name+".mat"));
                    }
                    importer.SaveAndReimport();
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform);
                    instance.transform.localPosition=originalTransform.localPosition;
                    instance.transform.localRotation=originalTransform.localRotation;
                    instance.transform.localScale=originalTransform.localScale;
                    lods[level]=new LOD(level==1?.09f:.018f,instance.GetComponentsInChildren<Renderer>());
                }
                group.SetLODs(lods);group.fadeMode=LODFadeMode.None;group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            Validate();EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText("CityHatchFailure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Validate()
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        var instance=UnityEngine.Object.Instantiate(asset);
        var group=instance.GetComponent<LODGroup>();var lods=group.GetLODs();
        Require(lods.Length==3,"Expected three LODs");
        var lines=new System.Collections.Generic.List<string>();
        int previous=int.MaxValue;Bounds? first=null;
        for(int i=0;i<3;i++)
        {
            var r=lods[i].renderers.Single();var mesh=r.GetComponent<MeshFilter>().sharedMesh;
            int triangles=(int)Enumerable.Range(0,mesh.subMeshCount).Sum(s=>(long)mesh.GetIndexCount(s))/3;
            Require(triangles<previous,"LOD triangle count must decrease");previous=triangles;
            Require(r.sharedMaterials.All(m=>m&&m.GetTexture("_BaseMap")),"Unresolved material/texture");
            Require(r.sharedMaterials.Any(m=>m.name.EndsWith("BodyPaint")),"Body colour slot missing");
            if(first.HasValue)Require(Vector3.Distance(r.bounds.center,first.Value.center)<.04f && Vector3.Distance(r.bounds.size,first.Value.size)<.15f,"LOD silhouette bounds shifted");
            else first=r.bounds;
            group.ForceLOD(i);
            lines.Add("PASS: LOD"+i+"; triangles="+triangles+"; screen height="+lods[i].screenRelativeTransitionHeight+"; bounds="+r.bounds);
        }
        group.ForceLOD(-1);
        foreach(var shader in instance.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Select(m=>m.shader).Distinct())
            Require(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Shader compiler error");
        lines.Add("PASS: saved LODGroup, materials, body colour slot and shaders resolve; original scale and ground pivot preserved.");
        File.WriteAllLines("CityHatchValidation.txt",lines);
        UnityEngine.Object.DestroyImmediate(instance);
    }
}
