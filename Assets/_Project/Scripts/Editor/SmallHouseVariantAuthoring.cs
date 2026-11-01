using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CouchGuys.ProceduralGeneration;
using CouchGuys.Gameplay.Delivery;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, non-destructive authoring and validation of the second easy house.</summary>
    public static class SmallHouseVariantAuthoring
    {
        private const string Request = "Library/SmallHouseVariant.request";

        [InitializeOnLoadMethod]
        private static void CheckRequest()
        {
            // A request is explicitly created by the offline authoring workflow, never on import.
            if (File.Exists(Request)) EditorApplication.delayCall += BuildAndValidate;
        }

        [MenuItem("Couch Guys/Validation/Build Missing Small House Variant And Validate")]
        public static void BuildAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool rebuild = File.Exists(Request) && File.ReadAllText(Request).Trim() == "rebuild";
            if (File.Exists(Request)) File.Delete(Request);
            var report = new StringBuilder();
            try
            {
                HousePrefabBuilder.BuildSmallHouse02(rebuild);
                GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabBuilder.House02PrefabPath);
                GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabBuilder.SmallHouse02PrefabPath);
                Require(original != null && variant != null, "Both saved prefabs exist");
                foreach (string path in new[] { "Assets/_Project/Settings/Regions/Region_Suburbs.asset",
                    "Assets/_Project/Resources/Region_Suburbs_Menu.asset" })
                {
                    var region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(path);
                    var data = new SerializedObject(region);
                    var pool = data.FindProperty("m_easyHouseVariants");
                    // Initial hookup only. Subsequent validation preserves Inspector customization.
                    if (pool.arraySize == 0)
                    {
                        pool.arraySize = 2;
                        pool.GetArrayElementAtIndex(0).objectReferenceValue = original;
                        pool.GetArrayElementAtIndex(1).objectReferenceValue = variant;
                        data.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                AssetDatabase.SaveAssets();
                Require(AssetDatabase.GetDependencies(HousePrefabBuilder.House02PrefabPath)
                    .Contains(HousePrefabBuilder.House02ModelPath), "Original GUID linkage survives relocation");
                Bounds? originalBounds = null;
                foreach (GameObject prefab in new[] { original, variant })
                {
                    var property = prefab.GetComponent<GeneratedProperty>();
                    Require(property.IsEasyDeliveryHouse && property.HasFrontDoorAnchor, "Easy identity and door anchor");
                    Require(property.GridFootprint == new Vector2Int(2, 2), "Same 2x2 plot");
                    Require(prefab.GetComponent<HouseExteriorModuleSet>().IsConfigured, "Driveway and path modules");
                    Require(prefab.GetComponentsInChildren<BoxCollider>(true).Length > 0, "Structural colliders");
                    Require(prefab.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != null && m.shader.isSupported)), "Materials and shaders");
                    var visual = prefab.transform.Find("VisualRoot");
                    Require(Quaternion.Angle(visual.localRotation, Quaternion.identity) < .01f, "Upright root");
                    var lods = visual.GetComponent<LODGroup>().GetLODs();
                    Require(lods.Length >= 3 && lods.All(l => l.renderers.Length > 0), "At least three populated LODs");
                    long previous = long.MaxValue;
                    foreach (var lod in lods)
                    {
                        long tris = lod.renderers.Sum(r => (long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                        Require(tris > 0 && tris < previous, "LOD geometry decreases");
                        report.AppendLine($"{prefab.name}: {tris} triangles, {lod.renderers.Length} renderers");
                        previous = tris;
                    }
                    var bounds = new Bounds();
                    bool first = true;
                    foreach (Renderer renderer in lods[0].renderers)
                    {
                        if (first) { bounds = renderer.bounds; first = false; }
                        else bounds.Encapsulate(renderer.bounds);
                    }
                    if (originalBounds == null) originalBounds = bounds;
                    else
                    {
                        Vector3 reference = originalBounds.Value.size;
                        Require(Mathf.Abs(bounds.size.x / reference.x - 1f) < .1f &&
                            Mathf.Abs(bounds.size.z / reference.z - 1f) < .2f &&
                            Mathf.Abs(bounds.size.y / reference.y - 1f) < .1f, "Similar width, depth and height class");
                    }
                    report.AppendLine($"{prefab.name}: bounds {bounds}, delivery {property.DeliveryPoint.position}");
                }
                typeof(EasyHouseValidation).GetMethod("RenderLods", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { variant });
                Directory.CreateDirectory("Tools/Blender/Previews");
                for (int i = 0; i < 4; i++)
                    File.Copy($"Library/House02_LOD{i}.png", $"Tools/Blender/Previews/small_house_02_LOD{i}.png", true);

                var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
                try
                {
                    var generator = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NeighbourhoodGenerator>(true)).First();
                    var region = AssetDatabase.LoadAssetAtPath<RegionDefinition>("Assets/_Project/Settings/Regions/Region_Suburbs.asset");
                    var tiers = new[] { "Easy", "Medium", "Hard" }.Select(t => AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{t}Delivery.asset")).ToArray();
                    string signature = null, mediumHardSignature = null;
                    int totalOriginal = 0, totalVariant = 0;
                    foreach (int seed in new[] { 10000, 10001, 12345, 10000 })
                    {
                        generator.Generate(region, seed);
                        Require(generator.HasGeneratedNeighbourhood && generator.IsDeliveryDifficultyReady, "Generated delivery routes");
                        int oldCount = 0, newCount = 0;
                        var current = new StringBuilder();
                        foreach (var destination in generator.DeliveryDestinations)
                        {
                            var identity = destination.GetComponent<GeneratedProperty>();
                            bool isVariant = destination.transform.Find("VisualRoot/small_house_02_LOD0") != null;
                            if (identity.IsEasyDeliveryHouse) { if (isVariant) newCount++; else oldCount++; }
                            if (isVariant) Require(identity.Zone <= SuburbZone.Easy, "Variant restricted to easy zone");
                            if (isVariant)
                            {
                                Bounds probe = new Bounds(destination.DropPosition.position + Vector3.up, new Vector3(1.4f,.8f,.7f));
                                foreach (BoxCollider collider in destination.GetComponentsInChildren<BoxCollider>())
                                    if (collider.name.StartsWith("Structure_"))
                                    {
                                        // Transform all eight corners: generated houses can face any road direction.
                                        Bounds local = new Bounds(collider.transform.InverseTransformPoint(probe.center), Vector3.zero);
                                        for (int corner = 0; corner < 8; corner++)
                                            local.Encapsulate(collider.transform.InverseTransformPoint(probe.center + Vector3.Scale(probe.extents,
                                                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                                        Require(!new Bounds(collider.center, collider.size).Intersects(local), "Couch delivery clearance");
                                    }
                            }
                            for (int t = 0; t < 3; t++)
                                if (tiers[t].IsDestinationEligible(destination))
                                    Require(identity.IsEasyDeliveryHouse == (t == 0), "Easy/medium/hard exclusivity");
                            current.Append($"{destination.transform.position:F3}/{destination.DropPosition.position:F3}/{isVariant};");
                        }
                        if (seed == 10000)
                        {
                            if (signature != null) Require(signature == current.ToString(), "Deterministic variant assignment");
                            signature = current.ToString();
                            mediumHardSignature = NonEasySignature(generator);
                        }
                        totalOriginal += oldCount; totalVariant += newCount;
                        report.AppendLine($"Seed {seed}: small_house_01={oldCount}, small_house_02={newCount}");
                    }
                    Require(totalOriginal > 0 && totalVariant > 0, "Both variants spawn");
                    var baselineRegion = UnityEngine.Object.Instantiate(region);
                    try
                    {
                        var baselineData = new SerializedObject(baselineRegion);
                        baselineData.FindProperty("m_easyHouseVariants").arraySize = 0;
                        baselineData.ApplyModifiedPropertiesWithoutUndo();
                        generator.Generate(baselineRegion, 10000);
                        Require(NonEasySignature(generator) == mediumHardSignature,
                            "Medium/hard houses and drop positions identical with variant pool disabled");
                        report.AppendLine("Medium/hard generation matches the no-variant baseline.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(baselineRegion); }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
                report.AppendLine("PASS: source linkage, prefabs, LOD reduction, materials, plot size, generation, tier exclusivity, deterministic selection.");
            }
            catch (Exception error) { report.AppendLine("FAIL: " + error); Debug.LogException(error); }
            File.WriteAllText("Library/SmallHouseVariantValidation.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        private static string NonEasySignature(NeighbourhoodGenerator generator) =>
            string.Join(";", generator.DeliveryDestinations
                .Where(d => !d.GetComponent<GeneratedProperty>().IsEasyDeliveryHouse)
                .Select(d => $"{d.transform.position:F3}/{d.DropPosition.position:F3}/{d.DestinationType}"));

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
