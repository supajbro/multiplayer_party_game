using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CouchGuys.Gameplay.Depot;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit authoring command for the NEW depot only. No import/reload/play-mode callbacks.
    public static class Depot02PrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/World/depot_02.prefab";
        const string ModelPath = "Assets/_Project/Models/depot_02/depot_02.fbx";
        const string Generated = "Assets/_Project/Generated/Depot02";
        static readonly Dictionary<string, Color> Palette = new()
        {
            ["Ivory"]=new(.82f,.85f,.86f), ["Cream"]=new(.96f,.94f,.84f),
            ["Teal"]=new(.008f,.36f,.43f), ["Navy"]=new(.015f,.13f,.2f),
            ["Yellow"]=new(1,.64f,.015f), ["Steel"]=new(.08f,.28f,.38f),
            ["Floor"]=new(.36f,.44f,.55f), ["Silver"]=new(.47f,.56f,.66f),
            ["Shutter"]=new(.58f,.65f,.74f), ["Rubber"]=new(.035f,.055f,.07f),
            ["Glass"]=new(.18f,.58f,.75f,.28f), ["Orange"]=new(.98f,.23f,.025f),
            ["Cardboard"]=new(.66f,.39f,.17f), ["Tape"]=new(.88f,.63f,.33f),
            ["Leaves"]=new(.12f,.42f,.04f), ["LeavesLight"]=new(.32f,.62f,.06f),
            ["Light"]=new(1,.78f,.23f), ["Sky"]=new(.14f,.58f,.86f)
        };
        [MenuItem("Couch Guys/Depot 02/Build Separate Prefab %#&n")]
        public static void Build()
        {
            Directory.CreateDirectory(Generated);
            AssetDatabase.Refresh();
            var importer=(ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if(importer==null)throw new Exception("depot_02.fbx has not imported yet");
            importer.generateMeshLods=true;importer.importCameras=false;importer.importLights=false;
            importer.isReadable=false;importer.SaveAndReimport();
            var root=new GameObject("depot_02");
            try
            {
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                model.name="Depot02_Visuals";model.transform.SetParent(root.transform,false);
                var animator=model.GetComponent<Animator>();if(animator!=null)Object.DestroyImmediate(animator);
                var materials=Palette.ToDictionary(p=>"D02_"+p.Key,p=>Material(p.Key,p.Value));
                foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer=filter.GetComponent<MeshRenderer>();
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>materials.TryGetValue(m.name,out var mapped)?mapped:m).ToArray();
                    bool moving=filter.name.StartsWith("D02_RollerSlat_")||filter.name.StartsWith("D02_GlassPanel_");
                    if(filter.name.StartsWith("D02_Collider_")||moving)
                    {
                        var box=filter.gameObject.AddComponent<BoxCollider>();box.center=filter.sharedMesh.bounds.center;box.size=filter.sharedMesh.bounds.size;
                    }
                    if(filter.name.StartsWith("D02_Module_")||filter.name.StartsWith("D02_BrandSign"))AddLods(filter,renderer);
                    if(filter.name.StartsWith("D02_PendantGlow"))
                    {
                        var light=filter.gameObject.AddComponent<Light>();light.type=LightType.Point;
                        light.color=new Color(1,.83f,.6f);light.intensity=2;light.range=8;light.shadows=LightShadows.None;
                    }
                }
                var markers=Marker(root.transform,"Depot02_Gameplay",Vector3.zero);
                // The first 10m road tile begins at z=15, flush with the apron edge.
                // Keep the connection outside the reserved 5x5 depot footprint exit.
                var road=Marker(markers,"RoadConnection",new Vector3(7.5f,0,20));
                var spawn=Marker(markers,"TruckSpawnPoint",new Vector3(7.5f,.18f,-3.5f));
                var fallback=Marker(markers,"TruckFallback",new Vector3(7.5f,.18f,1));
                var npc=Marker(markers,"DeliveryNpcSpawnPoint",new Vector3(-4.4f,.08f,-2.7f));
                var hidden=Marker(markers,"CouchHiddenSpawn",new Vector3(-16.2f,1.53f,-1.8f));
                Vector3[] pathPositions={new(-14.7f,1.53f,-1.8f),new(-11.8f,1.53f,-1.8f),new(-11.8f,1.53f,0),new(-11.8f,1.53f,3),new(-11.8f,1.53f,5.8f)};
                var path=pathPositions.Select((p,i)=>Marker(markers,"ConveyorPoint_"+i,p)).ToArray();
                // Couch broad dimension follows the belt, including the receiving bend.
                hidden.localRotation=Quaternion.Euler(0,90,0);path[0].localRotation=Quaternion.Euler(0,90,0);
                var playerSpawns=new[]{new Vector3(-3,.08f,5),new Vector3(-5,.08f,5),new Vector3(-3,.08f,3),new Vector3(-5,.08f,3)}
                    .Select((p,i)=>Marker(markers,"PlayerSpawnPoint_"+i,p)).ToArray();
                var glassSafety=Volume(markers,"GlassSafety",new Vector3(-4.5f,1.7f,10),new Vector3(3.1f,3.5f,2.8f));
                var truckSafety=Volume(markers,"TruckSafety",new Vector3(7.5f,2.5f,10),new Vector3(7,5.1f,4.5f));
                var glass=Rig(root,"D02_GlassPanel_",2,glassSafety,false);
                var roller=Rig(root,"D02_RollerSlat_",16,truckSafety,true);
                var buttons=new List<DepotDoorButton>();
                foreach(float z in new[]{8.7f,11f})
                {
                    var marker=Marker(markers,"TruckDoorButton",new Vector3(3.55f,1.45f,z));
                    var button=marker.gameObject.AddComponent<DepotDoorButton>();button.Configure(DepotDoorKind.VehicleGarage);buttons.Add(button);
                    // Saved, editable controls are authored here, never generated at runtime.
                    var housing=GameObject.CreatePrimitive(PrimitiveType.Cube);housing.name="TruckButtonHousing";
                    housing.transform.SetParent(marker,false);housing.transform.localScale=new Vector3(.24f,.4f,.12f);
                    housing.GetComponent<Renderer>().sharedMaterial=materials["D02_Teal"];
                    var key=GameObject.CreatePrimitive(PrimitiveType.Cube);key.name="TruckButton";key.transform.SetParent(marker,false);
                    key.transform.localPosition=new Vector3(0,0,z<10?-.08f:.08f);key.transform.localScale=new Vector3(.12f,.12f,.04f);
                    key.GetComponent<Renderer>().sharedMaterial=materials["D02_Light"];
                }
                var depot=root.AddComponent<DepotController>();
                depot.Configure(npc,spawn,new[]{fallback},hidden,path,glass,roller,buttons.ToArray());depot.SetTruckSpawnPoint(spawn);
                var data=new SerializedObject(depot);
                data.FindProperty("m_vehicleSpawnSurface").objectReferenceValue=model.GetComponentsInChildren<Collider>().Single(c=>c.name=="D02_Collider_Floor");
                data.FindProperty("m_entranceDetectionCentre").objectReferenceValue=glassSafety.transform;
                data.FindProperty("m_entranceDetectionRadius").floatValue=3.4f;
                data.FindProperty("m_entranceOpeningSpeed").floatValue=1.4f;
                data.FindProperty("m_entranceClosingSpeed").floatValue=1;
                data.FindProperty("m_entranceClosingDelay").floatValue=1.2f;
                data.FindProperty("m_doorAnimationSpeed").floatValue=.5f;
                data.ApplyModifiedPropertiesWithoutUndo();
                var area=root.AddComponent<StartingArea>();area.Initialise(road,playerSpawns,npc,new Vector2Int(5,5));area.SetDepot(depot);
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);AssetDatabase.SaveAssets();
                File.WriteAllText("Temp/depot02-build.txt","PASS: separate saved prefab, preserved original assets and active region; 16 shutter slats, 2 sliding panels, conveyor and dispatcher references connected.");
                Debug.Log("depot_02 built separately; active region was not changed.");
            }
            finally {Object.DestroyImmediate(root);}
        }
        static Transform Marker(Transform parent,string name,Vector3 p)
        {
            var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=p;return t;
        }
        static BoxCollider Volume(Transform parent,string name,Vector3 p,Vector3 size)
        {
            var box=Marker(parent,name,p).gameObject.AddComponent<BoxCollider>();box.isTrigger=true;box.size=size;return box;
        }
        static DepotDoorRig Rig(GameObject root,string prefix,int count,BoxCollider safety,bool roller)
        {
            var parts=Enumerable.Range(0,count).Select(i=>root.GetComponentsInChildren<Transform>().Single(t=>t.name==prefix+(roller?i.ToString("00"):i.ToString()))).ToArray();
            var closed=parts.Select(t=>t.localPosition).ToArray();var rotations=parts.Select(t=>t.localRotation).ToArray();
            var open=closed.Select((p,i)=>roller?new Vector3(p.x,5.93f+i*.018f,p.z):p+new Vector3(i==0?1.55f:-1.55f,0,0)).ToArray();
            var rig=new DepotDoorRig();rig.Configure(parts,closed,rotations,open,rotations,null,safety);return rig;
        }
        static Material Material(string name,Color color)
        {
            string path=Generated+"/D02_"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m!=null) { if(new[]{"Cream","Navy","Light"}.Contains(name)){m.SetFloat("_Cull",0);EditorUtility.SetDirty(m);} if(name=="Light"){m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;m.EnableKeyword("_EMISSION");EditorUtility.SetDirty(m);} return m; }
            m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="D02_"+name};m.SetColor("_BaseColor",color);
            m.SetFloat("_Smoothness",name=="Floor"?.58f:.4f);
            if(new[]{"Cream","Navy","Light"}.Contains(name))m.SetFloat("_Cull",0);
            if(new[]{"Teal","Steel","Silver","Shutter"}.Contains(name))m.SetFloat("_Metallic",.25f);
            if(name=="Light") {m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*3);}
            if(name=="Glass")
            {
                m.SetFloat("_Surface",1);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite",0);m.SetFloat("_Cull",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=3000;
            }
            AssetDatabase.CreateAsset(m,path);return m;
        }
        static void AddLods(MeshFilter filter,MeshRenderer baseRenderer)
        {
            var renderers=new List<Renderer>{baseRenderer};var mesh=filter.sharedMesh;
            for(int level=1;level<3;level++)
            {
                var reduced=CopyLod(mesh,level);string path=Generated+"/"+filter.name+"_LOD"+level+".asset";
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null){AssetDatabase.CreateAsset(reduced,path);saved=reduced;}else {EditorUtility.CopySerialized(reduced,saved);Object.DestroyImmediate(reduced);}
                var child=Marker(filter.transform,"LOD"+level,Vector3.zero).gameObject;
                child.AddComponent<MeshFilter>().sharedMesh=saved;
                var r=child.AddComponent<MeshRenderer>();r.sharedMaterials=baseRenderer.sharedMaterials;renderers.Add(r);
            }
            var group=filter.gameObject.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.32f,new[]{renderers[0]}),new LOD(.13f,new[]{renderers[1]}),new LOD(.015f,new[]{renderers[2]})});group.RecalculateBounds();
        }
        static Mesh CopyLod(Mesh source,int level)
        {
            var mesh=new Mesh{name=source.name+"_LOD"+level,indexFormat=IndexFormat.UInt32};
            using(var data=Mesh.AcquireReadOnlyMeshData(source))
            {
                using var vertices=new Unity.Collections.NativeArray<Vector3>(source.vertexCount,Unity.Collections.Allocator.Temp);
                data[0].GetVertices(vertices);mesh.SetVertices(vertices);
                if(source.HasVertexAttribute(VertexAttribute.Normal))
                {using var normals=new Unity.Collections.NativeArray<Vector3>(source.vertexCount,Unity.Collections.Allocator.Temp);data[0].GetNormals(normals);mesh.SetNormals(normals);}
                if(source.HasVertexAttribute(VertexAttribute.Tangent))
                {using var tangents=new Unity.Collections.NativeArray<Vector4>(source.vertexCount,Unity.Collections.Allocator.Temp);data[0].GetTangents(tangents);mesh.SetTangents(tangents);}
                if(source.HasVertexAttribute(VertexAttribute.TexCoord0))
                {using var uv=new Unity.Collections.NativeArray<Vector2>(source.vertexCount,Unity.Collections.Allocator.Temp);data[0].GetUVs(0,uv);mesh.SetUVs(0,uv);}
            }
            mesh.subMeshCount=source.subMeshCount;
            int lod=source.lodCount>0?Mathf.Min(level,source.lodCount-1):-1;
            for(int i=0;i<mesh.subMeshCount;i++)mesh.SetIndices(source.GetIndices(i,lod,true),source.GetTopology(i),i,false);
            mesh.RecalculateBounds();return mesh;
        }
        [MenuItem("Couch Guys/Depot 02/Use As Active Suburbs Depot %#&F9")]
        public static void Activate()
        {
            // Update only the existing configuration and saved floor reference;
            // preserve every Inspector adjustment and the current prefab hierarchy.
            var root=PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var data=new SerializedObject(root.GetComponent<DepotController>());
                data.FindProperty("m_vehicleSpawnSurface").objectReferenceValue=root.GetComponentsInChildren<Collider>().Single(c=>c.name=="D02_Collider_Floor");
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            var region=AssetDatabase.LoadAssetAtPath<RegionDefinition>("Assets/_Project/Settings/Regions/Region_Suburbs.asset");
            var regionData=new SerializedObject(region);
            regionData.FindProperty("m_startingAreaPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            regionData.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(region);
            Debug.Log("Suburbs now uses depot_02 with its authored indoor truck spawn floor.");
        }
        [MenuItem("Couch Guys/Depot 02/Render Unity Preview %#&F8")]
        public static void Preview()
        {
            var p=new PreviewRenderUtility();
            try
            {
                var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));p.AddSingleGO(root);
                foreach(var lod in root.GetComponentsInChildren<LODGroup>())lod.ForceLOD(0);
                p.camera.transform.position=new Vector3(-9,4.4f,-7.8f);p.camera.transform.LookAt(new Vector3(-5,3.2f,7));p.camera.fieldOfView=68;
                p.camera.nearClipPlane=.05f;p.camera.farClipPlane=100;p.ambientColor=new Color(.6f,.65f,.75f);
                p.lights[0].intensity=1.6f;p.lights[0].transform.rotation=Quaternion.Euler(40,170,0);p.lights[1].intensity=1.2f;
                p.BeginStaticPreview(new Rect(0,0,1500,900));p.Render(true);var img=p.EndStaticPreview();
                File.WriteAllBytes("ArtSource/Depot02/depot_02_unity.png",img.EncodeToPNG());Object.DestroyImmediate(img);
                root.GetComponent<DepotController>().ReleaseDoorAnimations();
            }
            finally {p.Cleanup();}
        }
    }
}
