using System;
using System.IO;
using CouchGuys.Gameplay.Depot;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Changes assets only on an explicit menu command or one-shot request, never on import/reload alone.
    [InitializeOnLoad]
    public static class DepotSafeZoneAuthoring
    {
        static DepotSafeZoneAuthoring() => EditorApplication.update += CheckRequest;
        private static void CheckRequest()
        {
            const string request = "Temp/DepotSafeZone.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (command == "author") Author();
                Validate();
                File.WriteAllText("Temp/DepotSafeZone.result", "PASS: prefab references, boundary transforms, attack segments, exterior recovery and thief-only navigation exclusion.\n");
            }
            catch (Exception e) { File.WriteAllText("Temp/DepotSafeZone.result", e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Couch Guys/Depot 02/Add Safe Area")]
        public static void RunBatch()
        {
            Author();
            Validate();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/DepotSafeZone.result", "PASS: prefab references, transformed bounds, attack segments, exterior recovery and thief-only navigation exclusion.\n");
        }

        public static void Author()
        {
            string path = Depot02PrefabBuilder.PrefabPath;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var existing = root.GetComponentInChildren<DepotSafeZone>(true);
                if (existing != null)
                {
                    var data = new SerializedObject(existing);
                    data.FindProperty("m_boundary").objectReferenceValue = existing.GetComponent<NavMeshModifierVolume>();
                    data.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    return;
                }
                var zoneObject = new GameObject("Depot Safe Area");
                zoneObject.transform.SetParent(root.transform, false);
                var volume = zoneObject.AddComponent<NavMeshModifierVolume>();
                volume.center = new Vector3(0, 4, 0);
                volume.size = new Vector3(30, 12, 30);
                volume.area = DepotSafeZone.NavigationArea;
                var zone = zoneObject.AddComponent<DepotSafeZone>();

                const string materialPath = "Assets/_Project/Generated/Depot02/DepotSafeGreen.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    material.SetColor("_BaseColor", new Color(.08f, .8f, .32f));
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                // Saved strips mark the loading apron and the outside perimeter.
                Strip(zoneObject.transform, material, "Street Boundary", new Vector3(0, .025f, 15), new Vector3(30, .025f, .16f));
                Strip(zoneObject.transform, material, "Left Boundary", new Vector3(-15, .025f, 0), new Vector3(.16f, .025f, 30));
                Strip(zoneObject.transform, material, "Right Boundary", new Vector3(15, .025f, 0), new Vector3(.16f, .025f, 30));
                Strip(zoneObject.transform, material, "Rear Boundary", new Vector3(0, .025f, -15), new Vector3(30, .025f, .16f));
                foreach (float x in new[] { -6.2f, -2.8f, 3.8f, 11.2f })
                {
                    var lamp = new GameObject("Green Security Light");
                    lamp.transform.SetParent(zoneObject.transform, false);
                    lamp.transform.localPosition = new Vector3(x, 3.5f, 10.4f);
                    var light = lamp.AddComponent<Light>();
                    light.type = LightType.Point; light.color = new Color(.15f, 1, .4f);
                    light.range = 3; light.intensity = 1.2f; light.shadows = LightShadows.None;
                    Strip(lamp.transform, material, "Lamp Lens", Vector3.zero, new Vector3(.24f, .12f, .12f));
                }
                var sign = new GameObject("Safe Area Sign", typeof(TextMeshPro));
                sign.transform.SetParent(zoneObject.transform, false);
                sign.transform.localPosition = new Vector3(0, 2.9f, 10.5f);
                sign.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var signText = sign.GetComponent<TextMeshPro>();
                signText.text = "DEPOT\nSAFE AREA"; signText.fontSize = 3;
                signText.color = new Color(.3f, 1, .55f); signText.alignment = TextAlignmentOptions.Center;
                signText.rectTransform.sizeDelta = new Vector2(3, 1.2f);

                var serialized = new SerializedObject(zone);
                serialized.FindProperty("m_boundary").objectReferenceValue = volume;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        private static void Strip(Transform parent, Material material, string name, Vector3 position, Vector3 scale)
        {
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube); strip.name = name;
            strip.transform.SetParent(parent, false); strip.transform.localPosition = position; strip.transform.localScale = scale;
            Object.DestroyImmediate(strip.GetComponent<Collider>());
            strip.GetComponent<Renderer>().sharedMaterial = material;
        }

        [MenuItem("Couch Guys/Depot 02/Validate Safe Area")]
        public static void Validate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Depot02PrefabBuilder.PrefabPath);
            var saved = prefab.GetComponentInChildren<DepotSafeZone>(true);
            Require(saved != null, "Missing saved zone");
            Require(saved.GetComponent<NavMeshModifierVolume>().area == DepotSafeZone.NavigationArea, "Missing navigation area");
            var host = new GameObject("SafeAreaValidation");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            NavMeshSurface surface = null;
            try
            {
                var volume = host.AddComponent<NavMeshModifierVolume>();
                volume.center = new Vector3(0, 4, 0); volume.size = new Vector3(30, 12, 30); volume.area = DepotSafeZone.NavigationArea;
                var testZone = host.AddComponent<DepotSafeZone>();
                typeof(DepotSafeZone).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(testZone, null);
                Require(DepotSafeZone.Contains(Vector3.zero), "Origin protected");
                Require(!DepotSafeZone.Contains(Vector3.forward * 16), "Road exposed");
                Require(DepotSafeZone.Intersects(Vector3.forward * -20, Vector3.forward * 20), "Through-zone attacks blocked");
                Require(!DepotSafeZone.Intersects(new Vector3(20, 0, -20), new Vector3(20, 0, 20)), "Outside combat allowed");
                Require(DepotSafeZone.TryGetExterior(Vector3.zero, 1.5f, out var exterior) && !DepotSafeZone.Contains(exterior), "Recovery exits boundary");
                host.transform.SetPositionAndRotation(new Vector3(1000, 0, 1000), Quaternion.Euler(0, 73, 0));
                host.transform.localScale = new Vector3(1.5f, 1, 2);
                Require(DepotSafeZone.Contains(host.transform.TransformPoint(new Vector3(14, 0, 14))), "Rotated/scaled containment");
                Require(!DepotSafeZone.Contains(host.transform.TransformPoint(new Vector3(16, 0, 0))), "Rotated/scaled exterior");
                floor.transform.position = host.transform.position - Vector3.up * .1f;
                floor.transform.localScale = new Vector3(100, .2f, 100);
                surface = floor.AddComponent<NavMeshSurface>(); surface.collectObjects = CollectObjects.All;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.BuildNavMesh();
                var centre = host.transform.position;
                Require(NavMesh.SamplePosition(centre, out var hit, 1, NavMesh.AllAreas), "Teammates retain depot navigation");
                Require(!NavMesh.SamplePosition(centre, out hit, 1, DepotSafeZone.ThiefAreaMask), "Thieves excluded from depot navigation");
                var path = new NavMeshPath();
                NavMesh.CalculatePath(centre + Vector3.right * 40, centre, DepotSafeZone.ThiefAreaMask, path);
                Require(path.status != NavMeshPathStatus.PathComplete, "Thieves cannot route inside");
            }
            finally
            {
                if (surface != null) surface.RemoveData();
                var registered = host.GetComponent<DepotSafeZone>();
                if (registered != null) typeof(DepotSafeZone).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(registered, null);
                Object.DestroyImmediate(floor); Object.DestroyImmediate(host);
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

        public static void RenderPreview()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Depot02PrefabBuilder.PrefabPath));
            var cameraObject = new GameObject("Safe area preview camera", typeof(Camera));
            var lightObject = new GameObject("Safe area preview light", typeof(Light));
            var target = new RenderTexture(1200, 900, 24);
            try
            {
                var light = lightObject.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
                lightObject.transform.rotation = Quaternion.Euler(48, -35, 0);
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.position = new Vector3(29, 25, 40);
                camera.transform.LookAt(new Vector3(0, 1, 1));
                camera.orthographic = true; camera.orthographicSize = 23;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f, .2f, .25f);
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                var picture = new Texture2D(1200, 900, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); picture.Apply();
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/DepotSafeZone-preview.png", picture.EncodeToPNG());
                Object.DestroyImmediate(picture);
            }
            finally
            {
                cameraObject.GetComponent<Camera>().targetTexture = null;
                RenderTexture.active = null; target.Release(); Object.DestroyImmediate(target);
                Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
            }
        }
    }
}
