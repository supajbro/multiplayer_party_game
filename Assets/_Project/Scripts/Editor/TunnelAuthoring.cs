using System;
using System.IO;
using System.Linq;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.EditorTools
{
    /// <summary>Explicit one-time authoring. Existing prefab and material edits are never overwritten.</summary>
    public static class TunnelAuthoring
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Environment/Tunnel/CouchGuys_Tunnel.prefab";
        const string Models = "Assets/_Project/Models/Environment/Tunnel/";
        const string Materials = "Assets/_Project/Materials/Environment/Tunnel/";

        [MenuItem("Tools/Couch Guys/Tunnel/Create Saved Modular Tunnel")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))
                throw new InvalidOperationException("Tunnel already exists; edit the saved prefab in the Inspector.");
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            var names = new[] { "Tunnel_Concrete", "Tunnel_Interior", "Tunnel_Yellow", "Tunnel_Dark", "Tunnel_Light" };
            var colors = new[] { new Color(.65f,.67f,.72f),new Color(.38f,.40f,.43f),
                new Color(1f,.68f,.055f),new Color(.10f,.12f,.15f),new Color(1f,.84f,.42f) };
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("Missing project's URP/Lit shader.");
            var materials = names.Select((name,i) => {
                string path = Materials + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m) return m;
                m = new Material(shader) {name = name, enableInstancing = true};
                m.SetColor("_BaseColor", colors[i]); m.SetFloat("_Smoothness",.12f);
                if (i == 4) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor",colors[i]*2f); }
                AssetDatabase.CreateAsset(m,path); return m;
            }).ToArray();
            foreach (string name in new[]{"Tunnel_Bay_3m","Tunnel_Portal"})
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(Models+name+".fbx");
                if (!importer) throw new InvalidOperationException("Import tunnel FBXs first.");
                importer.importAnimation=false; importer.importLights=false; importer.importCameras=false;
                importer.bakeAxisConversion=true; importer.globalScale=1; importer.useFileScale=true;
                importer.generateSecondaryUV=true; importer.importNormals=ModelImporterNormals.Import;
                importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
            }
            var root = new GameObject("CouchGuys_Tunnel");
            try
            {
                void Module(string name, Vector3 pos, float yaw)
                {
                    var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Models+name+".fbx");
                    var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,root.transform);
                    go.transform.localPosition=pos; go.transform.localRotation=Quaternion.Euler(0,yaw,0);
                    foreach(var r in go.GetComponentsInChildren<MeshRenderer>())
                        r.sharedMaterials=r.sharedMaterials.Select(m=>materials.First(x=>m.name.StartsWith(x.name,StringComparison.Ordinal))).ToArray();
                    foreach(var f in go.GetComponentsInChildren<MeshFilter>())
                    { var c=f.gameObject.AddComponent<MeshCollider>(); c.sharedMesh=f.sharedMesh; c.convex=false; }
                }
                for(int i=0;i<4;i++) Module("Tunnel_Bay_3m",new Vector3(0,0,-4.5f+i*3),0);
                Module("Tunnel_Portal",new Vector3(0,0,-5.5f),0);
                Module("Tunnel_Portal",new Vector3(0,0,5.5f),180);
                foreach(float z in new[]{-3f,3f})
                {
                    var go=new GameObject("Warm ceiling light"); go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=new Vector3(0,7.35f,z);
                    var light=go.AddComponent<Light>(); light.type=LightType.Point;
                    light.color=new Color(1,.84f,.55f); light.intensity=2; light.range=10;
                    light.shadows=LightShadows.None; light.renderMode=LightRenderMode.Auto;
                }
                var triggerObject=new GameObject("RegionTransitionTrigger"); triggerObject.transform.SetParent(root.transform,false);
                triggerObject.transform.localPosition=new Vector3(0,0,4.2f);
                var volume=triggerObject.AddComponent<BoxCollider>(); volume.isTrigger=true; volume.size=new Vector3(9.6f,6.4f,6f);
                var trigger=triggerObject.AddComponent<RegionTransitionTrigger>(); triggerObject.SetActive(false);
                var profile=root.AddComponent<TunnelGeometry>(); var so=new SerializedObject(profile);
                so.FindProperty("m_transition").objectReferenceValue=trigger;
                so.FindProperty("m_transitionVolume").objectReferenceValue=volume; so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            foreach(string path in new[]{"Assets/_Project/Settings/Regions/Region_Suburbs.asset","Assets/_Project/Resources/Region_Suburbs_Menu.asset"})
            {
                var region=AssetDatabase.LoadAssetAtPath<RegionDefinition>(path); var so=new SerializedObject(region);
                so.FindProperty("m_border.StandardTunnelPrefab").objectReferenceValue=prefab;
                so.FindProperty("m_border.SummitGatewayPrefab").objectReferenceValue=prefab;
                so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(region);
            }
            AssetDatabase.SaveAssets();
            AddEmbankment();
            Debug.Log("Saved modular Couch Guys tunnel and assigned both suburban region configurations.");
        }

        // Explicit addition of the saved hill envelope; preserves all existing prefab customizations.
        [MenuItem("Tools/Couch Guys/Tunnel/Add Saved Terrain Transition")]
        public static void AddEmbankment()
        {
            const string modelPath=Models+"Tunnel_Embankment.fbx";
            AssetDatabase.ImportAsset(modelPath,ImportAssetOptions.ForceSynchronousImport);
            var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.importAnimation=false; importer.importLights=false; importer.importCameras=false;
            importer.bakeAxisConversion=true; importer.generateSecondaryUV=true; importer.SaveAndReimport();
            var root=PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                if(root.transform.Find("Terrain transition")) return;
                var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath),root.transform);
                go.name="Terrain transition";
                var names=new[]{"Tunnel_Embankment","Tunnel_Grass"};
                var colors=new[]{new Color(.40f,.42f,.37f),new Color(.29f,.52f,.15f)};
                var materials=names.Select((name,i)=>{
                    string path=Materials+name+".mat"; var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!mat) { mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,enableInstancing=true};
                        mat.SetColor("_BaseColor",colors[i]); mat.SetFloat("_Smoothness",0); AssetDatabase.CreateAsset(mat,path); }
                    return mat;
                }).ToArray();
                foreach(var r in go.GetComponentsInChildren<MeshRenderer>())
                    r.sharedMaterials=r.sharedMaterials.Select(m=>materials.First(x=>m.name.StartsWith(x.name,StringComparison.Ordinal))).ToArray();
                foreach(var f in go.GetComponentsInChildren<MeshFilter>())
                    f.gameObject.AddComponent<MeshCollider>().sharedMesh=f.sharedMesh;
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }
    }
}
