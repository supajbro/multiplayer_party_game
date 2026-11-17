// Explicit isolated import/material authoring. Never runs on import or script reload.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ValidateNPCTraffic
{
    static readonly string[] Names={"NPC_CityHatch","NPC_FamilySUV","NPC_UtilityPickup","NPC_SportCoupe"};
    const string Folder="Assets/_Project/Models/Vehicles/NPCTraffic/";
    public static void Run()
    {
        var lines=new List<string>();
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if(!AssetDatabase.IsValidFolder(Folder+"Materials")) AssetDatabase.CreateFolder(Folder.TrimEnd('/'),"Materials");
            var colors=new[]{new Color(.094f,.565f,1),new Color(1,.58f,.035f),new Color(1,.029f,.053f),new Color(.511f,.039f,1)};
            for(int index=0;index<Names.Length;index++)
            {
                string name=Names[index],path=Folder+name+".fbx";
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                Require(importer!=null,name+": missing importer");
                importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;
                importer.isReadable=true;importer.globalScale=1;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
                foreach(string map in new[]{"Normal","ORM"})
                {
                    var mapImporter=(TextureImporter)AssetImporter.GetAtPath(Folder+"Textures/"+name+"_"+map+".png");
                    Require(mapImporter!=null,name+": missing generated "+map+" map");
                    if(map=="Normal")mapImporter.textureType=TextureImporterType.NormalMap;
                    mapImporter.sRGBTexture=false;mapImporter.SaveAndReimport();
                }
                foreach(string suffix in new[]{"Details","BodyPaint"})
                {
                    bool paint=suffix=="BodyPaint";
                    var shader=Shader.Find(paint?"Couch Guys/NPC Traffic Paint":"Couch Guys/Toon Lit");
                    Require(shader!=null,"Traffic shader missing");
                    Require(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Traffic shader compilation error");
                    var mat=new Material(shader){name=name+"_"+suffix,enableInstancing=true};
                    string texturePath=Folder+"Textures/"+name+"_BaseColor.png";
                    var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    Require(tex!=null,name+": texture missing "+texturePath);
                    mat.SetTexture("_BaseMap",tex);mat.SetColor("_BaseColor",paint?colors[index]:Color.white);
                    mat.SetFloat("_Saturation",1);mat.SetFloat("_RimIntensity",.08f);
                    if(paint)
                    {
                        mat.SetColor("_PaintReferenceColor",colors[index]);
                        string maskPath=Folder+"Textures/"+name+"_PaintMask.png";
                        var maskImporter=(TextureImporter)AssetImporter.GetAtPath(maskPath);
                        maskImporter.sRGBTexture=false;maskImporter.SaveAndReimport();
                        mat.SetTexture("_PaintMask",AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
                    }
                    AssetDatabase.CreateAsset(mat,Folder+"Materials/"+mat.name+".mat");
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),mat.name),mat);
                }
                importer.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(model!=null,name+": FBX import failed");
                var instance=UnityEngine.Object.Instantiate(model);
                int triangles=0,paintSlots=0;
                var bounds=new Bounds();bool started=false;
                foreach(var filter in instance.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh=filter.sharedMesh;
                    Require(mesh!=null&&mesh.vertexCount>0,name+": empty mesh");
                    Require(mesh.uv.Length==mesh.vertexCount,name+": UVs missing");
                    Require(mesh.normals.Length==mesh.vertexCount,name+": normals missing");
                    Require(mesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),name+": invalid vertices");
                    triangles+=mesh.triangles.Length/3;
                }
                foreach(var renderer in instance.GetComponentsInChildren<MeshRenderer>())
                {
                    if(!started){bounds=renderer.bounds;started=true;}else bounds.Encapsulate(renderer.bounds);
                    foreach(var mat in renderer.sharedMaterials)
                    {
                        Require(mat&&AssetDatabase.GetAssetPath(mat).EndsWith(".mat"),name+": material remap failed");
                        Require(mat.GetTexture("_BaseMap")!=null,name+": texture reference missing");
                        if(mat.name.EndsWith("BodyPaint"))paintSlots++;
                    }
                }
                Require(triangles>0&&triangles<=11000,name+": triangle budget exceeded");
                Require(paintSlots>0,name+": no separate paint material");
                Require(bounds.size.z>bounds.size.x*1.25f&&bounds.size.y>1,name+": orientation or scale invalid "+bounds.size);
                Require(Mathf.Abs(bounds.min.y)<.015f,name+": pivot not at ground "+bounds.min.y);
                lines.Add(name+": PASS; triangles="+triangles+"; bounds="+bounds.size.ToString("F3")+"; ground="+bounds.min.y+"; separate body paint and textures resolved");
                UnityEngine.Object.DestroyImmediate(instance);
                importer.isReadable=false;importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            File.WriteAllLines("Validation.txt",lines);
            EditorApplication.Exit(0);
        }
        catch(Exception error){lines.Add("FAIL: "+error);File.WriteAllLines("Validation.txt",lines);Debug.LogException(error);EditorApplication.Exit(1);}
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
