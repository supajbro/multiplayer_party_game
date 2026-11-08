using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit authoring only. Never rebuilds assets on import or entering Play mode.</summary>
    [InitializeOnLoad]
    public static class EasyCouchAuthoring
    {
        private const string Request = "Temp/EasyCouches.Author.request";
        private const string BoundsRequest = "Temp/EasyCouches.Bounds.request";
        public const string Prefabs = "Assets/_Project/Prefabs/Couches/Easy";
        private const string Models = "Assets/_Project/Models/Couch/Easy";
        private const string Materials = "Assets/_Project/Materials/Couch/Easy";
        private const string Report = "Tools/Blender/Exports/EasyCouches/unity_validation.txt";
        [Serializable] private class Palette { public Entry[] materials; }
        [Serializable] private class Entry { public string name; public float[] color; public string texture; }

        static EasyCouchAuthoring() { EditorApplication.update += CheckExplicitRequest; }
        private static void CheckExplicitRequest()
        {
            if (File.Exists(BoundsRequest) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(BoundsRequest);
                try { AddMissingBounds(); Validate(); }
                catch (Exception e) { File.WriteAllText(Report, "FAIL: " + e); Debug.LogException(e); }
            }
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { Author(); }
            catch (Exception e) { File.WriteAllText(Report, "FAIL: " + e); Debug.LogException(e); }
        }

        [MenuItem("Couch Guys/Couches/Create Missing Easy Couch Assets")]
        public static void Author()
        {
            Directory.CreateDirectory(Prefabs); Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var palette = JsonUtility.FromJson<Palette>(File.ReadAllText("Tools/Blender/Exports/EasyCouches/materials.json"));
            var materials = new Dictionary<string, Material>();
            foreach (var entry in palette.materials)
            {
                string path = Materials + "/" + entry.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Couch Guys/Toon Lit"));
                    material.name = entry.name;
                    material.SetColor("_BaseColor", string.IsNullOrEmpty(entry.texture) ?
                        new Color(entry.color[0], entry.color[1], entry.color[2], entry.color[3]) : Color.white);
                    if (!string.IsNullOrEmpty(entry.texture)) material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(entry.texture));
                    material.SetFloat("_RimIntensity", .06f);
                    AssetDatabase.CreateAsset(material, path);
                }
                materials.Add(entry.name, material);
            }
            for (int number = 1; number <= 10; number++)
            {
                string modelPath = $"{Models}/easy_couch_{number:00}.fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                foreach (var entry in palette.materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.name), materials[entry.name]);
                importer.SaveAndReimport();
                string prefabPath = $"{Prefabs}/EasyCouch_{number:00}.prefab";
                // Preserve all existing Inspector edits on repeated authoring runs.
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) continue;
                var root = new GameObject($"EasyCouch_{number:00}");
                try
                {
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
                    model.transform.SetParent(root.transform, false);
                    foreach (var group in model.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(group);
                    var levels = new LOD[3];
                    for (int level = 0; level < 3; level++)
                    {
                        var renderers = model.GetComponentsInChildren<Renderer>(true).Where(r => r.name.EndsWith($"LOD{level}")).ToArray();
                        if (renderers.Length != 1) throw new Exception($"{modelPath}: missing LOD{level}");
                        foreach (var renderer in renderers) { renderer.gameObject.SetActive(true); renderer.enabled = true; }
                        levels[level] = new LOD(new[] { .55f, .25f, .065f }[level], renderers);
                    }
                    var lods = root.AddComponent<LODGroup>(); lods.SetLODs(levels); lods.RecalculateBounds();
                    AddBounds(root);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { Object.DestroyImmediate(root); }
            }
            var tier = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/EasyDelivery.asset");
            var settings = new SerializedObject(tier); var list = settings.FindProperty("m_couchVisualPrefabs");
            if (list.arraySize == 0)
            {
                list.arraySize = 10;
                for (int i = 0; i < 10; i++) list.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/EasyCouch_{i + 1:00}.prefab");
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            var couch = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Couch.prefab");
            try
            {
                var controller = new SerializedObject(couch.GetComponent<CouchCarryController>());
                var reference = controller.FindProperty("m_easyDeliveryVisuals");
                if (reference.objectReferenceValue == null)
                {
                    reference.objectReferenceValue = tier; controller.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(couch, "Assets/_Project/Prefabs/Couch.prefab");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(couch); }
            AssetDatabase.SaveAssets();
            Validate();
        }

        private static void AddBounds(GameObject root)
        {
            if (root.GetComponent<CouchVisualBounds>() != null) return;
            var renderer = root.GetComponent<LODGroup>().GetLODs()[0].renderers.Single();
            var world = renderer.bounds;
            var component = new SerializedObject(root.AddComponent<CouchVisualBounds>());
            component.FindProperty("m_collisionBounds").boundsValue = new Bounds(
                root.transform.InverseTransformPoint(world.center), world.size);
            component.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Couch Guys/Couches/Add Missing Easy Collider Profiles")]
        public static void AddMissingBounds()
        {
            for (int i = 1; i <= 10; i++)
            {
                string path = $"{Prefabs}/EasyCouch_{i:00}.prefab";
                var prefab = PrefabUtility.LoadPrefabContents(path);
                try { AddBounds(prefab); PrefabUtility.SaveAsPrefabAsset(prefab, path); }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Couch Guys/Couches/Validate Easy Couch Assets")]
        public static void Validate()
        {
            var lines = new List<string>();
            var tier = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>("Assets/_Project/Settings/Delivery/EasyDelivery.asset");
            if (tier.CouchVisualCount != 10) throw new Exception("Easy delivery list must contain all ten visuals.");
            var shared = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Couch.prefab");
            var serialized = new SerializedObject(shared.GetComponent<CouchCarryController>());
            if (serialized.FindProperty("m_modelPrefabs").arraySize != 3) throw new Exception("Original model list changed.");
            if (serialized.FindProperty("m_easyDeliveryVisuals").objectReferenceValue != tier) throw new Exception("Shared couch config missing.");
            if (shared.GetComponentsInChildren<CouchCarryPoint>(true).Length != 4) throw new Exception("Grab points missing.");
            lines.Add("PASS: shared network couch retains original three models, four grab points and Easy config reference.");
            var unique = new HashSet<GameObject>();
            for (int i = 0; i < 10; i++)
            {
                var prefab = tier.GetCouchVisual(i);
                if (prefab == null || !unique.Add(prefab)) throw new Exception("Missing or duplicate Easy visual.");
                var lods = prefab.GetComponent<LODGroup>().GetLODs();
                if (lods.Length != 3) throw new Exception("Missing LOD group.");
                var tris = new List<int>();
                Bounds bounds = default;
                for (int level = 0; level < 3; level++)
                {
                    var renderer = lods[level].renderers.Single();
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    int count = 0; for (int sub = 0; sub < mesh.subMeshCount; sub++) count += (int)mesh.GetIndexCount(sub) / 3;
                    tris.Add(count);
                    if (level == 0) bounds = renderer.bounds;
                    foreach (var mat in renderer.sharedMaterials)
                        if (mat == null || mat.shader == null || mat.shader.name != "Couch Guys/Toon Lit") throw new Exception(prefab.name + " has invalid material.");
                }
                if (bounds.size.x < 2f || bounds.size.x > 2.3f || bounds.size.y < 1.2f || bounds.size.y > 1.7f ||
                    bounds.size.z < .8f || bounds.size.z > 1.3f) throw new Exception(prefab.name + " unexpected metre bounds: " + bounds);
                if (!(tris[0] > tris[1] && tris[1] > tris[2])) throw new Exception("LODs do not reduce geometry.");
                var profile = prefab.GetComponent<CouchVisualBounds>();
                if (profile == null || Vector3.Distance(profile.CollisionBounds.size, bounds.size) > .01f)
                    throw new Exception(prefab.name + " collision envelope does not match LOD0.");
                lines.Add($"PASS: {prefab.name}: bounds {bounds.size}, triangles {string.Join("/", tris)}, 3 LODs, valid Toon materials.");
            }
            foreach (string name in new[] { "Medium", "Hard" })
            {
                var other = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>($"Assets/_Project/Settings/Delivery/{name}Delivery.asset");
                if (other.CouchVisualCount != 0) throw new Exception(name + " must use legacy models.");
            }
            lines.Add("PASS: Medium and Hard retain legacy visual selection.");
            File.WriteAllLines(Report, lines); Debug.Log(string.Join("\n", lines));
        }
    }
}
