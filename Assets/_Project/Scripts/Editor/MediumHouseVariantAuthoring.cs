using System;
using System.IO;
using System.Linq;
using System.Text;
using CouchGuys.ProceduralGeneration;
using CouchGuys.Gameplay.Delivery;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit one-shot offline authoring request; never regenerates assets on normal import.
    public static class MediumHouseVariantAuthoring
    {
        const string Request = "Library/MediumHouseVariant.request";
        const string Model = "Assets/_Project/Models/Houses/MediumHouses/medium_house_02.fbx";
        const string Prefab = "Assets/_Project/Prefabs/Houses/medium_house_02.prefab";
        const string Materials = "Assets/_Project/Materials/Toon/MediumHouse02";

        [InitializeOnLoadMethod]
        static void CheckRequest()
        {
            if (!File.Exists(Request)) return;
            if (File.ReadAllText(Request).Trim() == "preview")
            {
                File.Delete(Request);
                EditorApplication.delayCall += RenderOnly;
            }
            else EditorApplication.delayCall += BuildAndValidate;
        }

        [MenuItem("Couch Guys/Validation/Render Medium House Variant LODs")]
        public static void RenderOnly()
        {
            Render(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            File.WriteAllText("Library/MediumHousePreviewValidation.txt", "PASS: all three forced LODs render non-empty images.");
        }

        [MenuItem("Couch Guys/Validation/Build Missing Medium House Variant And Validate")]
        public static void BuildAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool rebuild = File.Exists(Request) && File.ReadAllText(Request).Trim() == "rebuild";
            if (File.Exists(Request)) File.Delete(Request);
            var report = new StringBuilder();
            try
            {
                if (rebuild || !File.Exists(Prefab)) Build();
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabBuilder.House01PrefabPath);
                var variant = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
                Require(original != null && variant != null, "Both saved medium prefabs exist");
                foreach (string path in new[] { "Assets/_Project/Settings/Regions/Region_Suburbs.asset", "Assets/_Project/Resources/Region_Suburbs_Menu.asset" })
                {
                    var region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(path);
                    var data = new SerializedObject(region);
                    var pool = data.FindProperty("m_mediumHouseVariants");
                    if (pool.arraySize == 0)
                    {
                        pool.arraySize = 2;
                        pool.GetArrayElementAtIndex(0).objectReferenceValue = original;
                        pool.GetArrayElementAtIndex(1).objectReferenceValue = variant;
                        data.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                AssetDatabase.SaveAssets();
                var originalLods = original.GetComponentInChildren<LODGroup>().GetLODs();
                Bounds reference = BoundsOf(originalLods[0].renderers);
                report.AppendLine($"Reference active LOD0 bounds: {reference}");
                Require(AssetDatabase.GetDependencies(HousePrefabBuilder.House01PrefabPath).Contains(HousePrefabBuilder.HouseModelPath), "Original model GUID dependency survives relocation");
                var lods = variant.GetComponentInChildren<LODGroup>().GetLODs();
                Require(lods.Length == 3 && lods.All(l => l.renderers.Length > 0), "Three populated LOD levels");
                long high = Triangles(lods[0]);
                for (int i = 0; i < 3; i++)
                {
                    long tris = Triangles(lods[i]);
                    report.AppendLine($"LOD{i}: {tris} triangles ({100.0*tris/high:F1}%), {lods[i].renderers.Length} renderers");
                    Require(lods[i].renderers.All(r => r.sharedMaterials.All(m => m != null && m.shader.isSupported)), "Supported materials");
                    if (i == 1) Require(tris >= high*.4 && tris <= high*.6, "LOD1 within 40–60%");
                    if (i == 2) Require(tris >= high*.1 && tris <= high*.25, "LOD2 within 10–25%");
                    Bounds b = BoundsOf(lods[i].renderers);
                    report.AppendLine($"LOD{i} bounds: {b}");
                    Require(Vector3.Distance(b.size, BoundsOf(lods[0].renderers).size) < .1f, "Consistent LOD silhouette bounds");
                }
                Bounds size = BoundsOf(lods[0].renderers);
                report.AppendLine($"Original active LOD0 bounds: {reference}; new: {size}");
                Require(Mathf.Abs(size.size.x/reference.size.x-1) <= .05f && Mathf.Abs(size.size.z/reference.size.z-1) <= .05f, "Footprint within 5%");
                Require(Mathf.Abs(size.size.y/reference.size.y-1) <= .1f, "Comparable height");
                var property = variant.GetComponent<GeneratedProperty>();
                Require(!property.IsEasyDeliveryHouse && property.HasFrontDoorAnchor && property.GridFootprint == new Vector2Int(2,2), "Medium identity and plot");
                Require(variant.GetComponent<HouseExteriorModuleSet>().IsConfigured, "Configured access modules");
                Require(Mathf.Sign(property.DrivewayAnchor.localPosition.x-property.DeliveryPoint.localPosition.x) ==
                    Mathf.Sign(original.GetComponent<GeneratedProperty>().DrivewayAnchor.localPosition.x), "Garage side matches reference FBX convention");
                var colliders = variant.GetComponentsInChildren<BoxCollider>(true);
                Require(colliders.Length >= 8 && colliders.All(c => !c.transform.IsChildOf(variant.transform.Find("VisualRoot/medium_house_02"))), "Colliders independent of visual LODs");
                Vector3 probe = property.DeliveryPoint.position + Vector3.up*.8f;
                foreach (var c in colliders.Where(c => c.name.StartsWith("Structure_")))
                    Require(!c.bounds.Intersects(new Bounds(probe,new Vector3(1.4f,.7f,.6f))), "Entrance delivery clearance");
                Render(variant);
                ValidateGeneration(report);
                report.AppendLine("PASS: prefab, materials, colliders, entrance, footprint, LOD ratios, generation and tier validation.");
            }
            catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
            File.WriteAllText("Library/MediumHouseVariantValidation.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (source == null) throw new InvalidOperationException("Import medium_house_02.fbx first");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabBuilder.House01PrefabPath));
            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = "medium_house_02";
                var property = root.GetComponent<GeneratedProperty>();
                var visual = property.VisualRoot;
                Object.DestroyImmediate(visual.GetComponent<LODGroup>());
                foreach (Transform child in visual.Cast<Transform>().ToArray())
                    if (child.name != "ExteriorModuleTemplates" && child.name != "DrivewayAnchor" && child.name != "FootpathAnchor" && child != property.DeliveryPoint)
                        Object.DestroyImmediate(child.gameObject);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                instance.name = "medium_house_02";
                instance.transform.SetParent(visual, false);
                foreach (LODGroup group in instance.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(group);
                Directory.CreateDirectory(Materials); AssetDatabase.Refresh();
                var template = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Toon/Toon_Houses.mat");
                foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterials = r.sharedMaterials.Select(m =>
                    {
                        string path = Materials + "/" + m.name + ".mat";
                        var saved = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (saved != null) return saved;
                        saved = new Material(template) { name = m.name };
                        saved.SetColor("_BaseColor", m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color);
                        AssetDatabase.CreateAsset(saved, path);
                        return saved;
                    }).ToArray();
                }
                var lods = new LOD[3];
                for (int i = 0; i < 3; i++)
                {
                    var renderers = instance.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("_LOD"+i+"_")).ToArray();
                    lods[i] = new LOD(new[] {.60f,.30f,.01f}[i], renderers);
                }
                var groupOnRoot = visual.gameObject.AddComponent<LODGroup>();
                groupOnRoot.SetLODs(lods); groupOnRoot.RecalculateBounds();
                // The project's FBX import mirrors X: Blender x,y,z maps to Unity -x,z,-y.
                // Colliders are saved outside all LOD roots.
                Collider(visual,"CentralRear",new Vector3(.4f,1,1.65f),new Vector3(8,4.1f,3.3f));
                Collider(visual,"EntranceRear",new Vector3(1.6f,-2.3f,1.75f),new Vector3(4.8f,1.5f,3.5f));
                Collider(visual,"StoneTower",new Vector3(-2.7f,-.05f,3.55f),new Vector3(3.4f,6.1f,7.1f));
                Collider(visual,"LeftWing",new Vector3(-6.15f,.05f,1.62f),new Vector3(3.55f,5.9f,3.24f));
                Collider(visual,"Garage",new Vector3(6.35f,.05f,1.62f),new Vector3(4.45f,5.9f,3.24f));
                Collider(visual,"Upper",new Vector3(1.5f,.05f,5.1f),new Vector3(5.8f,5.6f,3.4f));
                Collider(visual,"Porch",new Vector3(1.55f,-3.9f,.35f),new Vector3(4.35f,2.1f,.46f));
                for (int i=0;i<3;i++) Collider(visual,"Step"+i,new Vector3(1.55f,-5-i*.33f,.42f-i*.13f),new Vector3(2.65f,.4f,.24f));
                foreach (float x in new[]{-.55f,3.65f}) Collider(visual,"Post"+x,new Vector3(x,-4.57f,2.02f),new Vector3(.23f,.26f,2.88f));
                property.DeliveryPoint.localPosition = new Vector3(-1.55f,.60f,4.35f);
                visual.Find("FootpathAnchor").localPosition = new Vector3(-1.55f,.23f,5.8f);
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            }
            finally { Object.DestroyImmediate(root); }
        }
        static void Collider(Transform parent,string name,Vector3 centre,Vector3 size)
        {
            var t = new GameObject("Structure_"+name).transform; t.SetParent(parent,false);
            var c = t.gameObject.AddComponent<BoxCollider>();
            c.center = new Vector3(-centre.x,centre.z,-centre.y); c.size = new Vector3(size.x,size.z,size.y);
        }
        static Bounds BoundsOf(Renderer[] renderers)
        {
            Bounds b=renderers[0].bounds; foreach(var r in renderers.Skip(1)) b.Encapsulate(r.bounds); return b;
        }
        static long Triangles(LOD lod) => lod.renderers.Sum(r => (long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);
        static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }

        static void ValidateGeneration(StringBuilder report)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
            try
            {
                var generator=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<NeighbourhoodGenerator>(true)).First();
                var region=AssetDatabase.LoadAssetAtPath<RegionDefinition>("Assets/_Project/Settings/Regions/Region_Suburbs.asset");
                var tiers=new[]{"Easy","Medium","Hard"}.Select(t=>AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{t}Delivery.asset")).ToArray();
                string signature=null, otherSignature=null; int totalNew=0,totalOld=0;
                foreach(int seed in new[]{10000,10001,12345,10000})
                {
                    generator.Generate(region,seed);
                    Require(generator.HasGeneratedNeighbourhood && generator.IsDeliveryDifficultyReady,"Generation and routes ready");
                    int oldCount=0,newCount=0;
                    foreach(var d in generator.DeliveryDestinations)
                    {
                        bool isNew=d.GetComponentsInChildren<Renderer>(true).Any(r=>r.name.StartsWith("MH02_LOD0_"));
                        var p=d.GetComponent<GeneratedProperty>();
                        bool medium=!p.IsEasyDeliveryHouse && d.DestinationType != DeliveryDestinationType.Mansion;
                        if(medium) { if(isNew)newCount++;else oldCount++; }
                        if(isNew) Require(medium && !tiers[0].IsDestinationEligible(d) && !tiers[2].IsDestinationEligible(d),"Variant exclusive to medium deliveries");
                    }
                    string current=string.Join(";",generator.DeliveryDestinations.Select(d=>$"{d.transform.position:F3}/{d.DropPosition.position:F3}/{d.GetComponentsInChildren<Renderer>(true).Any(r=>r.name.StartsWith("MH02_LOD0_"))}"));
                    if(seed==10000)
                    {
                        if(signature!=null) Require(signature==current,"Same seed reproduces variants and delivery positions");
                        signature=current;otherSignature=OtherSignature(generator);
                    }
                    totalNew+=newCount;totalOld+=oldCount;
                    report.AppendLine($"Seed {seed}: medium_house_01={oldCount}, medium_house_02={newCount}");
                }
                Require(totalNew>0 && totalOld>0,"Both variants spawn");
                var baseline=Object.Instantiate(region);
                try
                {
                    var data=new SerializedObject(baseline);data.FindProperty("m_mediumHouseVariants").arraySize=0;data.ApplyModifiedPropertiesWithoutUndo();
                    generator.Generate(baseline,10000);
                    Require(OtherSignature(generator)==otherSignature,"Easy and hard positions and delivery points unchanged from baseline");
                }
                finally { Object.DestroyImmediate(baseline); }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        static string OtherSignature(NeighbourhoodGenerator generator) => string.Join(";",generator.DeliveryDestinations
            .Where(d=>d.GetComponent<GeneratedProperty>().IsEasyDeliveryHouse || d.DestinationType==DeliveryDestinationType.Mansion)
            .Select(d=>$"{d.transform.position:F3}/{d.DropPosition.position:F3}/{d.DestinationType}"));

        static void Render(GameObject prefab)
        {
            var scene=EditorSceneManager.NewPreviewScene();var target=RenderTexture.GetTemporary(1100,800,24);
            var previous=RenderTexture.active;var pixels=new Texture2D(1100,800,TextureFormat.RGB24,false);
            try
            {
                var house=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                var cameraObject=new GameObject("Medium preview",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.targetTexture=target;
                camera.transform.position=new Vector3(29,20,42);camera.transform.LookAt(new Vector3(0,6,0));
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.36f,.45f);
                var sun=new GameObject("Sun",typeof(Light));SceneManager.MoveGameObjectToScene(sun,scene);
                sun.GetComponent<Light>().type=LightType.Directional;sun.GetComponent<Light>().intensity=1.5f;sun.transform.rotation=Quaternion.Euler(40,-30,0);
                // Warm up the preview camera's first culling pass before forcing a level.
                camera.Render();
                for(int i=0;i<3;i++)
                {
                    house.GetComponentInChildren<LODGroup>().ForceLOD(i);camera.Render();RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,1100,800),0,0);pixels.Apply();
                    Require(pixels.GetPixels32().Where((p,index)=>index%101==0).Distinct().Count()>20, $"LOD{i} renders visible geometry");
                    File.WriteAllBytes($"Tools/Blender/Previews/medium_house_02_LOD{i}.png",pixels.EncodeToPNG());
                }
            }
            finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(pixels);EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
