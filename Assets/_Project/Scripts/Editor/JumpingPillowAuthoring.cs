using System;
using System.IO;
using System.Linq;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit authoring only. Existing saved prefab/material customizations are preserved.</summary>
    public static class JumpingPillowAuthoring
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Environment/JumpingPillow/JumpingPillow.prefab";
        private const string Models = "Assets/_Project/Models/JumpingPillow/";
        private const string Materials = "Assets/_Project/Materials/Environment/JumpingPillow/";

        [MenuItem("Tools/Couch Guys/Jumping Pillow/Create Saved Prefab")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            foreach (string file in new[] { "AustralianJumpingPillow.fbx", "AustralianJumpingPillow_Collision.fbx" })
            {
                string path = Models + file;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.isReadable = true;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.bakeAxisConversion = true;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importNormals = ModelImporterNormals.Import;
                importer.SaveAndReimport();
            }
            string[] names = { "Pillow_Red", "Pillow_Blue", "Pillow_Yellow", "Pillow_Anchor" };
            Color[] colors = { new Color(.86f,.12f,.07f), new Color(.04f,.53f,.87f),
                new Color(1f,.79f,.12f), new Color(.39f,.31f,.19f) };
            var materials = names.Select((name, i) =>
            {
                string path = Materials + name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) return mat;
                mat = new Material(Shader.Find("Couch Guys/Toon Lit")) { name = name, enableInstancing = true };
                mat.SetColor("_BaseColor", colors[i]);
                mat.SetFloat("_SpecularEnabled", 1f);
                mat.SetFloat("_SpecularIntensity", .18f);
                mat.SetFloat("_SpecularSize", .25f);
                mat.SetFloat("_SpecularSoftness", .12f);
                mat.SetFloat("_BandSoftness", .16f);
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }).ToArray();
            var root = new GameObject("JumpingPillow");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Models + "AustralianJumpingPillow.fbx"), root.transform);
                visual.name = "Visuals";
                foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
                        materials.First(x => m.name.StartsWith(x.name, StringComparison.Ordinal))).ToArray();

                // Copy only the authored collision mesh and its transform; no collision renderer.
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + "AustralianJumpingPillow_Collision.fbx");
                var source = model.GetComponentsInChildren<MeshFilter>().Single();
                var collision = new GameObject("JumpingSurfaceCollider");
                collision.transform.SetParent(root.transform, false);
                collision.transform.localPosition = model.transform.InverseTransformPoint(source.transform.position);
                collision.transform.localRotation = Quaternion.Inverse(model.transform.rotation) * source.transform.rotation;
                collision.transform.localScale = source.transform.lossyScale;
                var collider = collision.AddComponent<MeshCollider>();
                collider.sharedMesh = source.sharedMesh;
                collider.convex = false;
                var pillow = root.AddComponent<JumpingPillow>();
                var so = new SerializedObject(pillow);
                var filters = visual.GetComponentsInChildren<MeshFilter>();
                var references = so.FindProperty("m_visualMeshes");
                references.arraySize = filters.Length;
                for (int i = 0; i < filters.Length; i++) references.GetArrayElementAtIndex(i).objectReferenceValue = filters[i];
                so.FindProperty("m_jumpingSurface").objectReferenceValue = filters.Single(f => f.name == "JumpingSurface");
                so.FindProperty("m_surfaceCollider").objectReferenceValue = collider;
                so.ApplyModifiedPropertiesWithoutUndo();
                root.AddComponent<EnvironmentPlacementMetadata>().Configure(new Vector2(12.312f, 9.234f), 1f, true, true);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("Saved configurable JumpingPillow prefab.");
        }

        [MenuItem("Tools/Couch Guys/Jumping Pillow/Assign Deformation Surface")]
        public static void AssignDeformationSurface()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var so = new SerializedObject(root.GetComponent<JumpingPillow>());
                var surface = so.FindProperty("m_jumpingSurface");
                if (surface.objectReferenceValue != null) return;
                surface.objectReferenceValue = root.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "JumpingSurface");
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
