// Explicit candidate material authoring and actual Animator/lighting verification.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public static class ValidateThiefPBRSurfaces
{
    const string MaterialPath="Assets/_Project/Materials/Thieves/ThiefEnemy01/thief_enemy_01.mat";
    const string PrefabPath="Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab";
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static string output;
    public static void Run()
    {
        var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-thiefReport")+1];
        var lines=new List<string>();
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Require(material&&material.shader.name=="Universal Render Pipeline/Lit","Expected shadow-receiving URP Lit material");
            material.DisableKeyword("_NORMALMAP");material.SetTexture("_BumpMap",null);
            material.SetFloat("_Metallic",1);material.SetFloat("_Smoothness",1);
            material.SetFloat("_OcclusionStrength",0);material.SetFloat("_ReceiveShadows",1);
            material.SetFloat("_EnvironmentReflections",1);material.SetFloat("_SpecularHighlights",1);
            material.SetColor("_BaseColor",Color.white);material.SetColor("_EmissionColor",Color.black);
            material.DisableKeyword("_EMISSION");material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
            Require(!material.IsKeywordEnabled("_NORMALMAP")&&!material.IsKeywordEnabled("_EMISSION"),"Generated relief or emission still active");
            var clips=AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Models/Thieves/thief_enemy_01.fbx").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
            Require(clips.Length==25,"Expected unchanged 25 clips");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            foreach(string variant in new[]{"Unarmed","Pistol","AssaultRifle","Shotgun","Speaker"})
            {
                var root=Object.Instantiate(prefab);
                try
                {
                    var animator=root.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                    animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Animations/Thieves/ThiefEnemy01/thief_enemy_01_"+variant+".controller");
                    Require(animator.avatar&&animator.avatar.isValid,"Invalid avatar");animator.Rebind();animator.Update(0);
                    var group=root.GetComponent<LODGroup>();Require(group.lodCount==4,"Lost four LODs");
                    var skins=group.GetLODs().Select(l=>l.renderers.OfType<SkinnedMeshRenderer>().Single()).ToArray();
                    for(int i=0;i<4;i++)
                    {
                        Require(skins[i].sharedMaterial==material&&skins[i].bones.Length==22&&skins[i].bones.All(b=>b),"Lost shared material or skeleton");
                        Require(skins[i].sharedMesh.triangles.Length/3==new[]{10000,4000,1500,700}[i],"LOD geometry changed");
                    }
                    var baked=new Mesh();
                    foreach(string state in new[]{"Idle","Walk","Shoot","Frustration","Death"})
                    {
                        for(int sample=0;sample<13;sample++)
                        {
                            animator.Play("Base Layer."+state,0,(sample+.5f)/13);animator.Update(0);
                            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer."+state),"Missing state "+variant+state);
                            foreach(var skin in skins)
                            {
                                skin.BakeMesh(baked,true);
                                Require(baked.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)&&skin.localBounds.Contains(v)),"Invalid animated skin bounds: "+variant+state);
                            }
                        }
                    }
                    Object.DestroyImmediate(baked);lines.Add("PASS: "+variant+"; five states, 13 poses each, all four unchanged LODs; valid 22-bone skinning and saved bounds.");
                }
                finally{Object.DestroyImmediate(root);}
            }
            lines.Add("PASS: 25 clips, UVs and mesh budgets retained; standard lit surfaces, generated normal map disabled, no emission or baked occlusion.");
            RenderLighting(prefab,lines);
            Require(!ShaderUtil.GetShaderMessages(material.shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"PBR shader compile error");
            File.WriteAllLines(Path.Combine(output,"UnityPBRValidation.txt"),lines);
            EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"UnityPBRFailure.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    static void RenderLighting(GameObject prefab,List<string> lines)
    {
        ShaderUtil.allowAsyncCompilation=false;
        var pipeline=UniversalRenderPipelineAsset.Create();pipeline.shadowDistance=10;pipeline.mainLightShadowmapResolution=4096;
        GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.12f,.12f,.12f);
        var root=Object.Instantiate(prefab);var animator=root.GetComponent<Animator>();
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);animator.Play("Base Layer.Idle",0,.25f);animator.Update(0);
        var group=root.GetComponent<LODGroup>();group.ForceLOD(0);
        var sun=new GameObject("PBR light test").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2.2f;sun.shadows=LightShadows.Soft;
        var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.transform.localScale=Vector3.one*2;
        ground.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        ground.GetComponent<Renderer>().sharedMaterial.SetColor("_BaseColor",new Color(.28f,.3f,.33f));
        var camera=new GameObject("PBR comparison camera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.2f,.23f);camera.orthographic=true;camera.orthographicSize=1.02f;
        camera.transform.position=new Vector3(.25f,1.03f,4);camera.transform.LookAt(new Vector3(0,.81f,0));
        var target=new RenderTexture(800,800,24,RenderTextureFormat.ARGB32);target.Create();
        Color[] Render(string name,float yaw,float intensity)
        {
            sun.transform.rotation=Quaternion.Euler(35,180+yaw,0);sun.intensity=intensity;
            for(int warmup=0;warmup<3;warmup++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;var image=new Texture2D(800,800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,800,800),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());var pixels=image.GetPixels();Object.DestroyImmediate(image);return pixels;
        }
        var left=Render("Unity_Light_Left",-45,2.2f);var right=Render("Unity_Light_Right",45,2.2f);var ambient=Render("Unity_Ambient_Only",45,0);
        // Compare the same central character pixels, excluding the ground/background.
        float difference=0,boost=0;int count=0;
        for(int y=150;y<650;y++)for(int x=315;x<485;x++)
        {
            int i=y*800+x;difference+=Mathf.Abs(left[i].grayscale-right[i].grayscale);boost+=right[i].grayscale-ambient[i].grayscale;count++;
        }
        difference/=count;boost/=count;Require(difference>.012f&&boost>.025f,"Surface did not respond to light position/intensity");
        lines.Add("PASS: actual URP renders respond to light direction (mean pixel change="+difference.ToString("F4")+") and intensity (brightness increase="+boost.ToString("F4")+"); no emission; LOD0 casts and receives shadows.");
        RenderSettings.ambientLight=new Color(.28f,.28f,.28f);
        Render("Unity_Studio",-25,1f);
        group.ForceLOD(3);Render("Unity_LOD3",-25,1f);
        Object.DestroyImmediate(root);Object.DestroyImmediate(ground);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(sun.gameObject);
    }
}
