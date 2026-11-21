// Explicit asset authoring in an isolated project; no import or reload callbacks.
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
public static class AuthorRecyclingBin
{
    const string Model="Assets/_Project/Models/Props/RecyclingBin/";
    const string Prefab="Assets/_Project/Prefabs/Props/RecyclingBin.prefab";
    public static void Build()
    {
        AssetDatabase.Refresh();
        var importer=(ModelImporter)AssetImporter.GetAtPath(Model+"recycling_bin.fbx");
        importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;
        importer.isReadable=true;importer.addCollider=false;importer.globalScale=1;
        importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
        string[] names={"D02_Silver","D02_Shutter","D02_Steel","D02_Teal","D02_Yellow","D02_Rubber","D02_Light","RB_GreenEmission","RB_InteriorGlow"};
        foreach(string n in names)
        {
            string path=n.StartsWith("D02_")?"Assets/_Project/Generated/Depot02/"+n+".mat":"Assets/_Project/Models/Props/RecyclingBin/Materials/"+n+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material)throw new Exception("Missing material "+n);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),n),material);
        }
        importer.SaveAndReimport();
        var ci=(ModelImporter)AssetImporter.GetAtPath(Model+"recycling_bin_collision.fbx");
        ci.importCameras=false;ci.importLights=false;ci.importAnimation=false;ci.isReadable=true;ci.addCollider=false;ci.materialImportMode=ModelImporterMaterialImportMode.None;ci.SaveAndReimport();
        var root=new GameObject("RecyclingBin");
        var visual=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model+"recycling_bin.fbx"),root.transform);
        visual.name="Visuals";
        foreach(var old in visual.GetComponentsInChildren<LODGroup>())UnityEngine.Object.DestroyImmediate(old);
        var renderers=visual.GetComponentsInChildren<MeshRenderer>();
        if(renderers.Length!=3)throw new Exception("Expected three LOD renderers, got "+renderers.Length);
        var lod=root.AddComponent<LODGroup>();
        lod.SetLODs(new[]{new LOD(.45f,new Renderer[]{renderers.Single(r=>r.name.EndsWith("LOD0"))}),new LOD(.20f,new Renderer[]{renderers.Single(r=>r.name.EndsWith("LOD1"))}),new LOD(.035f,new Renderer[]{renderers.Single(r=>r.name.EndsWith("LOD2"))})});
        lod.RecalculateBounds();
        var collision=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model+"recycling_bin_collision.fbx"),root.transform);collision.name="Collision";
        foreach(var f in collision.GetComponentsInChildren<MeshFilter>())
        {
            var c=f.gameObject.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;c.convex=true;
            var renderer=f.GetComponent<MeshRenderer>();if(renderer)UnityEngine.Object.DestroyImmediate(renderer);
            UnityEngine.Object.DestroyImmediate(f);
        }
        Physics.SyncTransforms();
        var all=root.GetComponentsInChildren<MeshCollider>();if(all.Length!=9)throw new Exception("Expected nine convex collision parts");
        // Mouth must stay open throughout the central throw area.
        for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
        {
            if(!Physics.Raycast(new Vector3(x*.65f,3,z*.65f),Vector3.down,out var hit,4))throw new Exception("Missing bin floor");
            if(hit.point.y>.31f || hit.point.y<.19f)throw new Exception("Mouth obstructed at "+hit.point);
        }
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI/4;var direction=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
            if(!Physics.Raycast(direction*3+Vector3.up*.8f,-direction,out var hit,2))throw new Exception("Missing wall collision");
        }
        // Drop a weapon-sized rigidbody through the real compound collision.
        var test=GameObject.CreatePrimitive(PrimitiveType.Cube);test.transform.position=new Vector3(0,2.5f,0);test.transform.localScale=new Vector3(1.4f,.12f,.2f);test.transform.rotation=Quaternion.Euler(12,25,8);
        var rb=test.AddComponent<Rigidbody>();rb.mass=2;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        Physics.simulationMode=SimulationMode.Script;Physics.SyncTransforms();
        for(int i=0;i<180;i++)Physics.Simulate(1f/60);
        if(test.transform.position.y<.24f || test.transform.position.y>.6f)throw new Exception("Dropped item did not settle inside bin: "+test.transform.position);
        UnityEngine.Object.DestroyImmediate(test);
        var bounds=renderers.Single(r=>r.name.EndsWith("LOD0")).bounds;
        if(bounds.size.y<1.60f || bounds.size.y>1.67f || bounds.min.y<-.01f)throw new Exception("Incorrect import scale/origin "+bounds);
        if(renderers.Any(r=>r.sharedMaterials.Length!=9 || r.sharedMaterials.Any(m=>m==null)))throw new Exception("Invalid material slots");
        Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
        PrefabUtility.SaveAsPrefabAsset(root,Prefab);AssetDatabase.SaveAssets();
        var saved=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if(!saved || saved.GetComponentsInChildren<MeshCollider>().Length!=9 || saved.GetComponent<LODGroup>().lodCount!=3)throw new Exception("Prefab roundtrip failed");
        File.WriteAllText("validation.txt","PASS: FBX scale and bottom origin, nine resolved material slots per LOD, three LODs, nine convex colliders, nine unobstructed mouth rays, eight wall rays, weapon-sized rigidbody drop, saved prefab reload. Bounds: "+bounds);
        UnityEngine.Object.DestroyImmediate(root);
    }
}
