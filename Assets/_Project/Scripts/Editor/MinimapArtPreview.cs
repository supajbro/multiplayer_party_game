using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CouchGuys.Editor
{
    /// <summary>Manual, isolated artwork QA using the production rasteriser and saved HUD frame.</summary>
    public static class MinimapArtPreview
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);

        [MenuItem("Couch Guys/Minimap/Export HUD Artwork Preview")]
        public static void Export()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            Texture2D mapTexture = null;
            Camera previewCamera = null;
            var oldActive = RenderTexture.active;
            try
            {
                var fixture = new GameObject("Inactive minimap QA fixture");
                fixture.SetActive(false);
                SceneManager.MoveGameObjectToScene(fixture, scene);
                var generator = fixture.AddComponent<NeighbourhoodGenerator>();
                var map = fixture.AddComponent<NeighbourhoodMap>();
                Set(map, "m_generator", generator);
                Set(map, "m_mapBounds", new Bounds(Vector3.zero, new Vector3(150, 1, 150)));
                var roads = (List<GeneratedRoad>)generator.GeneratedRoads;
                for (int i = -7; i <= 7; i++)
                {
                    AddRoad(fixture.transform, roads, new Vector3(i * 10, 0, 0), i == 0 ?
                        (RoadConnections)15 : RoadConnections.East | RoadConnections.West);
                    if (i != 0) AddRoad(fixture.transform, roads, new Vector3(0, 0, i * 10), RoadConnections.North | RoadConnections.South);
                }
                var properties = (List<GeneratedProperty>)generator.GeneratedProperties;
                string[] icons = { "HouseGabled", "HouseCompact", "HouseLarge" };
                for (int side = 0; side < 4; side++)
                for (int i = 0; i < 2; i++)
                {
                    var go = new GameObject("House fixture");
                    go.transform.SetParent(fixture.transform, false);
                    Quaternion rotation = Quaternion.Euler(0, side * 90, 0);
                    go.transform.SetPositionAndRotation(rotation * new Vector3(22 + i * 25, 0, 22), rotation * Quaternion.Euler(0, 180, 0));
                    var property = go.AddComponent<GeneratedProperty>();
                    property.ConfigurePrefab(new Vector2Int(2, 2), null, null, null, false);
                    Set(property, "m_minimapHouseIcon", AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/_Project/Art/UI/Minimap/{icons[(side + i) % 3]}.png"));
                    property.SetDrivewayReservation(rotation * Vector3.back,
                        rotation * new Vector3(22 + i * 25, 0, 6), go.transform.position, 3.5f, 0);
                    properties.Add(property);
                }
                typeof(NeighbourhoodMap).GetMethod("BuildStaticTexture", Flags).Invoke(map, null);
                mapTexture = (Texture2D)typeof(NeighbourhoodMap).GetField("m_mapTexture", Flags).GetValue(map);
                // This camera exists only in the temporary Editor preview scene, never in a game asset.
                target = new RenderTexture(384, 384, 24);
                target.Create();
                var cameraObject = new GameObject("Editor QA Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                previewCamera = camera;
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.07f, 0.1f);
                camera.targetTexture = target;
                camera.orthographic = true;
                camera.orthographicSize = 192;
                camera.transform.position = new Vector3(0, 0, -100);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1000;
                var canvasObject = new GameObject("Editor QA Canvas", typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10;
                var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/PlayerWeaponHud.prefab").GetComponent<PlayerWeaponHudView>();
                var frame = Object.Instantiate(hud.m_minimapRoot, canvas.transform, false);
                frame.gameObject.SetActive(true);
                frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
                frame.anchoredPosition = Vector2.zero;
                var image = frame.Find("MapMask/StaticMap").GetComponent<RawImage>();
                image.texture = mapTexture;
                image.uvRect = new Rect(0.5f - 65f / 150f, 0.5f - 65f / 150f, 130f / 150f, 130f / 150f);
                var markers = (RectTransform)frame.Find("MapMask/DynamicMarkers");
                markers.Find("LocalPlayer").gameObject.SetActive(false);
                markers.Find("DeliveryWaypoint").gameObject.SetActive(false);
                frame.Find("DeliveryDistance").gameObject.SetActive(false);
                var markerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/MinimapPlayerMarker.prefab").GetComponent<MinimapPlayerMarker>();
                Color[] colours = { new Color(0.12f, 0.95f, 1), new Color(0.25f, 0.58f, 1), new Color(0.35f, 1, 0.42f) };
                Vector2[] positions = { Vector2.zero, new Vector2(46, 10), new Vector2(-35, -39) };
                for (int i = 0; i < 3; i++)
                {
                    var marker = Object.Instantiate(markerPrefab, markers, false);
                    ((RectTransform)marker.transform).anchoredPosition = positions[i];
                    marker.SetAppearance(colours[i], i * 110f);
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                var output = new Texture2D(384, 384, TextureFormat.RGBA32, false);
                output.ReadPixels(new Rect(0, 0, 384, 384), 0, 0);
                output.Apply();
                Directory.CreateDirectory("Temp/MinimapArt");
                File.WriteAllBytes("Temp/MinimapArt/HudPreview.png", output.EncodeToPNG());
                Object.DestroyImmediate(output);
                // OnDisable owns disposal of the map's runtime-only raster.
                Set(map, "m_mapTexture", null);
            }
            finally
            {
                RenderTexture.active = oldActive;
                if (previewCamera != null) previewCamera.targetTexture = null;
                if (mapTexture != null) Object.DestroyImmediate(mapTexture);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void AddRoad(Transform parent, List<GeneratedRoad> roads, Vector3 position, RoadConnections connections)
        {
            var go = new GameObject("Road fixture");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var road = go.AddComponent<GeneratedRoad>();
            road.Initialise(GridCoordinate.Zero, connections);
            roads.Add(road);
        }
    }
}
