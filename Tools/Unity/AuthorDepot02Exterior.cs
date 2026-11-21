// Explicit, isolated asset authoring. No reload/import callbacks.
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
public static class AuthorDepot02Exterior
{
    [Serializable] public class Submesh { public int[] indices; }
    [Serializable] public class Part { public string name; public Vector3[] vertices; public string[] materials; public Submesh[] submeshes; }
    [Serializable] public class Model { public Part[] meshes; }
    public static void Build()
    {
        const string folder="Assets/_Project/Generated/Depot02/Exterior";
        Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var model=JsonUtility.FromJson<Model>(File.ReadAllText("exterior.json"));
        var root=new GameObject("Depot02_ExteriorDetails");
        foreach(var part in model.meshes)
        {
            var mesh=new Mesh{name=part.name,indexFormat=IndexFormat.UInt32};
            mesh.vertices=part.vertices;mesh.subMeshCount=part.submeshes.Length;
            for(int i=0;i<part.submeshes.Length;i++)mesh.SetTriangles(part.submeshes[i].indices,i);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=folder+"/"+part.name+".asset";
            AssetDatabase.CreateAsset(mesh,path);
            var go=new GameObject(part.name);go.transform.SetParent(root.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials=part.materials.Select(n=>AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Generated/Depot02/"+(n=="D02_ExteriorGreen"?"DepotSafeGreen":n)+".mat")).ToArray();
        }
        PrefabUtility.SaveAsPrefabAsset(root,"Assets/ExteriorDetails.prefab");
        AssetDatabase.SaveAssets();
        // Confirm every serialized mesh and material resolves before delivery.
        var saved=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ExteriorDetails.prefab");
        if(saved.GetComponentsInChildren<MeshFilter>().Any(f=>f.sharedMesh==null) || saved.GetComponentsInChildren<MeshRenderer>().Any(r=>r.sharedMaterials.Any(m=>m==null)))throw new Exception("Unresolved exterior reference");
        File.WriteAllText("validation.txt","PASS: "+model.meshes.Length+" exterior assemblies, all mesh and material references resolved.");
        UnityEngine.Object.DestroyImmediate(root);
    }
}
