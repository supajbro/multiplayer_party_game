using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.ProceduralGeneration;

namespace CouchGuys.Editor
{
    // Explicit menu actions only; never rewrites assets on import or entering play mode.
    public static class MinimapArtTools
    {

        [MenuItem("Couch Guys/Minimap/Configure Generated Artwork")]
        public static void ConfigureArtwork()
        {
            const string art = "Assets/_Project/Art/UI/Minimap/";
            const string markerPath = "Assets/_Project/Prefabs/UI/MinimapPlayerMarker.prefab";
            // This is a manual initial setup command. Never overwrite an Inspector-customised marker.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(markerPath) != null)
                throw new System.InvalidOperationException("Marker already exists; edit its saved prefab in the Inspector.");
            Directory.CreateDirectory(art);
            var triangle = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                float u = (x + 0.5f) / 128f;
                float v = (y + 0.5f) / 128f;
                bool inside = v >= 0.55f && v <= 0.96f && Mathf.Abs(u - 0.5f) <= (0.96f - v) * 0.44f;
                triangle.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
            triangle.Apply();
            File.WriteAllBytes(art + "DirectionTriangle.png", triangle.EncodeToPNG());
            Object.DestroyImmediate(triangle);
            AssetDatabase.Refresh();
            foreach (string file in new[] { "HouseGabled", "HouseCompact", "HouseLarge", "RobotHead", "DirectionTriangle" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(art + file + ".png");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = file.StartsWith("House");
                importer.maxTextureSize = file.StartsWith("House") ? 256 : 128;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.crunchedCompression = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
            var root = new GameObject("MinimapPlayerMarker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MinimapPlayerMarker));
            try
            {
                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = Vector2.one * 32f;
                root.GetComponent<Image>().color = Color.clear;
                root.GetComponent<Image>().raycastTarget = false;
                Image direction = MakeImage("Direction", root.transform,
                    AssetDatabase.LoadAssetAtPath<Sprite>(art + "DirectionTriangle.png"), 48f);
                Image head = MakeImage("RobotHead", root.transform,
                    AssetDatabase.LoadAssetAtPath<Sprite>(art + "RobotHead.png"), 34f);
                var marker = new SerializedObject(root.GetComponent<MinimapPlayerMarker>());
                marker.FindProperty("m_head").objectReferenceValue = head;
                marker.FindProperty("m_direction").objectReferenceValue = direction;
                marker.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, markerPath);
            }
            finally { Object.DestroyImmediate(root); }

            var player = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player.prefab");
            try
            {
                var map = new SerializedObject(player.GetComponent<NeighbourhoodMap>());
                map.FindProperty("m_playerMarkerPrefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(markerPath).GetComponent<MinimapPlayerMarker>();
                map.FindProperty("m_defaultHouseIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(art + "HouseCompact.png");
                map.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, "Assets/_Project/Prefabs/Player.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            string[] houses = { "House_01", "House_02", "House_03", "medium_house_02", "small_house_02" };
            string[] icons = { "HouseGabled", "HouseCompact", "HouseLarge", "HouseGabled", "HouseCompact" };
            for (int i = 0; i < houses.Length; i++)
            {
                string path = $"Assets/_Project/Prefabs/Houses/{houses[i]}.prefab";
                var house = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var property = new SerializedObject(house.GetComponent<GeneratedProperty>());
                    property.FindProperty("m_minimapHouseIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(art + icons[i] + ".png");
                    property.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(house, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(house); }
            }
            File.WriteAllText("Temp/MinimapArtConfigured.txt", "Artwork imported; saved marker and five house prefabs configured.");
        }

        private static Image MakeImage(string name, Transform parent, Sprite sprite, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * size;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }
        [MenuItem("Couch Guys/Minimap/Export Model References %&m")]
        public static void ExportReferences()
        {
            Directory.CreateDirectory("Temp/MinimapArt");
            foreach (string name in new[] { "House_01", "House_02", "House_03" })
                RenderReference($"Assets/_Project/Prefabs/Houses/{name}.prefab", name, false);
            RenderReference("Assets/_Project/Prefabs/UI/MainMenuRobotVisual.prefab", "Robot", true);
        }

        private static void RenderReference(string path, string name, bool head)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                preview.AddSingleGO(go);
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (head)
                {
                    foreach (var r in renderers)
                        r.enabled = r.transform.AncestorsAndSelf().Any(t => t.name == "Head") ||
                            r.name.Contains("Face") || r.name.Contains("Eye") || r.name.Contains("Ear") || r.name.Contains("Head_");
                    renderers = renderers.Where(r => r.enabled).ToArray();
                }
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 0.7f;
                var direction = head ? new Vector3(0, 0.1f, 1) : new Vector3(0, 1, 0.45f);
                preview.camera.transform.position = bounds.center + direction.normalized * bounds.size.magnitude * 3f;
                preview.camera.transform.LookAt(bounds.center, head ? Vector3.up : Vector3.back);
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.farClipPlane = bounds.size.magnitude * 10f;
                preview.camera.clearFlags = CameraClearFlags.Color;
                preview.camera.backgroundColor = new Color(0.24f, 0.28f, 0.32f, 1);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40, 30, 0);
                preview.lights[1].intensity = 0.8f;
                preview.ambientColor = Color.gray;
                preview.BeginStaticPreview(new Rect(0, 0, 512, 512));
                preview.camera.Render();
                var image = preview.EndStaticPreview();
                File.WriteAllBytes($"Temp/MinimapArt/{name}.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally { preview.Cleanup(); }
        }

        private static System.Collections.Generic.IEnumerable<Transform> AncestorsAndSelf(this Transform t)
        {
            while (t != null) { yield return t; t = t.parent; }
        }
    }
}
