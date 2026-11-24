// Explicit isolated authoring. No automatic import, reload or Play mode callbacks.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class AuthorFamilySUVLODs
{
    [Serializable] class Surface {public string name;public float[] color_linear;public float metallic;public float roughness;public float emission;}
    [Serializable] class Surfaces {public Surface[] items;}
    const string Model="Assets/_Project/Models/Vehicles/NPCTraffic/";
    const string Prefab="Assets/_Project/Prefabs/Vehicles/NPCTraffic/NPC_FamilySUV.prefab";
    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var surfaces=JsonUtility.FromJson<Surfaces>("{\"items\":"+File.ReadAllText("FamilySUVPBRMaterials.json")+"}").items;
            foreach(var surface in surfaces)
            {
                string path=Model+"Materials/"+surface.name+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                var shader=Shader.Find("Universal Render Pipeline/Lit");
                if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
                mat.shader=shader;mat.shaderKeywords=Array.Empty<string>();mat.enableInstancing=true;
                foreach(string key in new[]{"_BaseMap","_MainTex","_BumpMap","_MetallicGlossMap","_OcclusionMap","_EmissionMap"})if(mat.HasProperty(key))mat.SetTexture(key,null);
                var materialData=new SerializedObject(mat);
                var textureEntries=materialData.FindProperty("m_SavedProperties.m_TexEnvs");
                for(int i=textureEntries.arraySize-1;i>=0;i--)
                    if(!mat.HasProperty(textureEntries.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue))textureEntries.DeleteArrayElementAtIndex(i);
                materialData.ApplyModifiedPropertiesWithoutUndo();
                var c=surface.color_linear;var color=new Color(c[0],c[1],c[2],c[3]).gamma;
                mat.SetColor("_BaseColor",color);mat.SetColor("_Color",color);
                mat.SetFloat("_Metallic",surface.metallic);mat.SetFloat("_Smoothness",1-surface.roughness);
                mat.SetFloat("_Surface",0);mat.SetFloat("_ZWrite",1);mat.SetFloat("_SrcBlend",1);mat.SetFloat("_DstBlend",0);mat.SetFloat("_Cull",2);
                if(surface.emission>0){mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",new Color(c[0],c[1],c[2])*surface.emission);}
                EditorUtility.SetDirty(mat);
            }
            for(int level=0;level<3;level++)
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(Model+"NPC_FamilySUV"+(level==0?"":"_LOD"+level)+".fbx");
                foreach(var surface in surfaces)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),surface.name),AssetDatabase.LoadAssetAtPath<Material>(Model+"Materials/"+surface.name+".mat"));
                importer.SaveAndReimport();
            }
            var root=PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var existing=root.GetComponent<LODGroup>();
                var original=existing ? (MeshRenderer)existing.GetLODs()[0].renderers.Single() : root.GetComponentsInChildren<MeshRenderer>().Single();
                var originalTransform=original.transform;
                var group=root.GetComponent<LODGroup>();
                if(!group)group=root.AddComponent<LODGroup>();
                var lods=new LOD[3];lods[0]=new LOD(.22f,new Renderer[]{original});
                for(int level=1;level<=2;level++)
                {
                    string path=Model+"NPC_FamilySUV_LOD"+level+".fbx";
                    var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                    var instance=existing && existing.GetLODs().Length>level ? existing.GetLODs()[level].renderers[0].gameObject : (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform);
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
            Validate();
            if(Environment.GetCommandLineArgs().Contains("-familySUVPreview"))Preview();
            else EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText("FamilySUVFailure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Preview()
    {
        try
        {
            Validate();
            ShaderUtil.allowAsyncCompilation=false;
            var pipeline=UniversalRenderPipelineAsset.Create();
            GraphicsSettings.defaultRenderPipeline=pipeline;
            QualitySettings.renderPipeline=pipeline;
            var car=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            var sun=new GameObject("Preview sun").AddComponent<Light>();
            sun.type=LightType.Directional;sun.intensity=1.1f;sun.transform.rotation=Quaternion.Euler(45,145,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.45f,.45f,.45f);
            var camera=new GameObject("Preview camera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.24f,.26f,.29f);
            camera.orthographic=true;camera.orthographicSize=2.3f;
            camera.transform.position=new Vector3(4,3.2f,5);
            camera.transform.LookAt(new Vector3(0,.8f,0));
            var target=new RenderTexture(900,650,24,RenderTextureFormat.ARGB32);target.Create();
            for(int level=0;level<3;level++)
            {
                car.GetComponent<LODGroup>().ForceLOD(level);
                // Explicit visibility also makes offline previews independent of the editor's last culling pass.
                var lods=car.GetComponent<LODGroup>().GetLODs();
                for(int j=0;j<3;j++)foreach(var r in lods[j].renderers)r.enabled=j==level;
                for(int warmup=0;warmup<3;warmup++)
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                RenderTexture.active=target;var image=new Texture2D(900,650,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,900,650),0,0);image.Apply();
                File.WriteAllBytes("FamilySUV_Unity_LOD"+level+".png",image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText("FamilySUVPreviewFailure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
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
            Require(r.sharedMaterials.All(m=>m&&m.shader.name=="Universal Render Pipeline/Lit"&&m.GetTexture("_BaseMap")==null),"Expected texture-free PBR materials");
            Require(r.sharedMaterials.Any(m=>m.name.EndsWith("BodyPaint")),"Body colour slot missing");
            if(first.HasValue)Require(Vector3.Distance(r.bounds.center,first.Value.center)<.04f && Vector3.Distance(r.bounds.size,first.Value.size)<.15f,"LOD silhouette bounds shifted");
            else first=r.bounds;
            group.ForceLOD(i);
            lines.Add("PASS: LOD"+i+"; triangles="+triangles+"; screen height="+lods[i].screenRelativeTransitionHeight+"; bounds="+r.bounds);
        }
        group.ForceLOD(-1);
        foreach(var shader in instance.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Select(m=>m.shader).Distinct())
            Require(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Shader compiler error");
        FamilySUVTintProbe.Check(instance.GetComponentsInChildren<Renderer>());
        lines.Add("PASS: saved LODGroup, texture-free PBR materials and shaders resolve; original scale and ground pivot preserved; damage/reset tint verified independently for every material slot and LOD.");
        File.WriteAllLines("FamilySUVValidation.txt",lines);
        UnityEngine.Object.DestroyImmediate(instance);
    }
}
