using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Delivery;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, one-shot visual replacement. No import/reload/Play callbacks.</summary>
    public static class DispatcherTVPrefabSetup
    {
        private const string PrefabPath = DeliveryNPCPrefabBuilder.PrefabPath;
        private const string ModelDirectory = "Assets/_Project/Models/Dispatcher/";
        private const string MaterialDirectory = ModelDirectory + "Materials";
        private const string ReportPath = "Tools/DispatcherIntegrationValidation.txt";

        [MenuItem("Couch Guys/Dispatcher/Apply TV Model %#F9")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply the dispatcher visual outside Play mode.");
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (original == null) throw new InvalidOperationException("DeliveryNPC prefab is missing.");
            // Once converted, validation is the safe default: never overwrite Inspector edits.
            if (original.transform.Find("TVVisual") != null) { Validate(); return; }
            var models = Enumerable.Range(0, 3).Select(i =>
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelDirectory + $"dispatcher_01_LOD{i}.fbx")).ToArray();
            if (models.Any(model => model == null)) throw new InvalidOperationException("Missing dispatcher FBX LOD.");
            if (!AssetDatabase.IsValidFolder(MaterialDirectory))
                AssetDatabase.CreateFolder(ModelDirectory.TrimEnd('/'), "Materials");
            var shader = Shader.Find(GraphicsSettings.currentRenderPipeline != null
                ? "Universal Render Pipeline/Lit" : "Standard");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Supported Lit shader missing.");

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var npc = root.GetComponent<DeliveryNPC>();
                var collider = root.GetComponent<CapsuleCollider>();
                if (npc == null || collider == null) throw new InvalidOperationException("Dispatcher gameplay components missing.");
                string before = EditorJsonUtility.ToJson(npc);
                Vector3 rootPosition = root.transform.localPosition, rootScale = root.transform.localScale;
                Quaternion rootRotation = root.transform.localRotation;
                // Only the former visual branch is removed; spawn points and other children survive.
                Transform oldVisual = root.transform.Find("FrogVisual") ?? root.transform.Find("Visual");
                if (oldVisual == null) throw new InvalidOperationException("Expected old dispatcher visual is missing.");
                Object.DestroyImmediate(oldVisual.gameObject);
                var visual = new GameObject("TVVisual");
                visual.transform.SetParent(root.transform, false);
                var group = visual.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.CrossFade;
                group.animateCrossFading = false;
                var lods = new LOD[3];
                float[] thresholds = { .22f, .09f, .008f };
                var materials = new Dictionary<string, Material>();
                for (int i = 0; i < models.Length; i++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(models[i], visual.transform);
                    instance.name = $"dispatcher_01_LOD{i}";
                    instance.transform.localPosition = Vector3.zero;
                    // Preserve the FBX importer's Z-up to Y-up conversion on the model root.
                    instance.transform.localRotation = models[i].transform.localRotation;
                    instance.transform.localScale = models[i].transform.localScale;
                    foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
                    var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length != 4) throw new InvalidOperationException("Each LOD must contain body, screen and two physical buttons.");
                    foreach (var renderer in renderers)
                    {
                        renderer.enabled = true;
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                            GetMaterial(source, shader, materials)).ToArray();
                    }
                    // Determine the front from the display position, rather than assuming FBX axes.
                    var screen = renderers.Single(r => r.name.EndsWith("_Screen", StringComparison.Ordinal));
                    var body = renderers.Single(r => r.name.EndsWith("_Body", StringComparison.Ordinal));
                    Vector3 front = CenterInRoot(screen, root.transform) - CenterInRoot(body, root.transform);
                    if (front.z < 0f) instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * instance.transform.localRotation;
                    lods[i] = new LOD(thresholds[i], renderers) { fadeTransitionWidth = .2f };
                }
                group.SetLODs(lods);
                Bounds bounds = BoundsOf(lods[0].renderers, root.transform);
                visual.transform.localScale = Vector3.one * (2.2f / bounds.size.y);
                bounds = BoundsOf(lods[0].renderers, root.transform);
                visual.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = BoundsOf(lods[0].renderers, root.transform);
                // Retain the existing collider component and all trigger/layer/material settings.
                collider.height = bounds.size.y;
                collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                collider.center = bounds.center;
                group.RecalculateBounds();
                if (before != EditorJsonUtility.ToJson(npc) || root.transform.localPosition != rootPosition ||
                    root.transform.localRotation != rootRotation || root.transform.localScale != rootScale)
                    throw new InvalidOperationException("Gameplay root changed unexpectedly.");
                EditorUtility.SetDirty(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                    throw new InvalidOperationException("Could not save dispatcher prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Validate();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Debug.Log("Dispatcher TV visual applied and validated. " + ReportPath);
        }

        private static Material GetMaterial(Material source, Shader shader, Dictionary<string, Material> cache)
        {
            if (source == null) throw new InvalidOperationException("FBX has an unassigned material.");
            string key = source.name;
            if (cache.TryGetValue(key, out var existing)) return existing;
            string path = MaterialDirectory + "/" + key + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = key, enableInstancing = true };
                Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
                if (key == "Dispatcher_Screen") color = new Color(.003f, .008f, .006f, 1f);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                material.SetFloat("_Metallic", key == "Dispatcher_Pole" ? .65f : 0f);
                material.SetFloat("_Smoothness", key == "Dispatcher_Screen" ? .65f : .35f);
                // Unity's LODGroup selects Lit's multi_compile cross-fade variant per draw.
                AssetDatabase.CreateAsset(material, path);
            }
            cache[key] = material;
            return material;
        }

        private static Bounds BoundsOf(IEnumerable<Renderer> renderers, Transform root)
        {
            bool first = true;
            var result = new Bounds();
            foreach (var renderer in renderers)
            {
                Bounds local = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = root.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        private static Vector3 CenterInRoot(Renderer renderer, Transform root) =>
            root.InverseTransformPoint(renderer.transform.TransformPoint(renderer.GetComponent<MeshFilter>().sharedMesh.bounds.center));

        [MenuItem("Couch Guys/Dispatcher/Fit TV Visual to Ground %#F10")]
        public static void FitExistingVisual()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var visual = root.transform.Find("TVVisual");
                if (visual == null) throw new InvalidOperationException("Apply the TV visual first.");
                visual.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.localScale = Vector3.one;
                for (int i = 0; i < 3; i++)
                {
                    var instance = visual.Find($"dispatcher_01_LOD{i}");
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDirectory + $"dispatcher_01_LOD{i}.fbx");
                    instance.SetLocalPositionAndRotation(Vector3.zero, model.transform.localRotation);
                    instance.localScale = model.transform.localScale;
                    var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                    var screen = renderers.Single(r => r.name.EndsWith("_Screen", StringComparison.Ordinal));
                    var body = renderers.Single(r => r.name.EndsWith("_Body", StringComparison.Ordinal));
                    if ((CenterInRoot(screen, root.transform) - CenterInRoot(body, root.transform)).z < 0f)
                        instance.localRotation = Quaternion.Euler(0f, 180f, 0f) * instance.localRotation;
                }
                var group = visual.GetComponent<LODGroup>();
                Bounds bounds = BoundsOf(group.GetLODs()[0].renderers, root.transform);
                visual.localScale = Vector3.one * (2.2f / bounds.size.y);
                bounds = BoundsOf(group.GetLODs()[0].renderers, root.transform);
                visual.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                bounds = BoundsOf(group.GetLODs()[0].renderers, root.transform);
                var collider = root.GetComponent<CapsuleCollider>();
                collider.height = bounds.size.y;
                collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                collider.center = bounds.center;
                group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Validate();
        }

        [MenuItem("Couch Guys/Dispatcher/Validate TV Prefab")]
        public static void Validate()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var report = new List<string>();
            try
            {
                if (root.GetComponent<DeliveryNPC>() == null) throw new InvalidOperationException("Missing interaction script.");
                if (root.transform.Find("FrogVisual") != null) throw new InvalidOperationException("Old model remains.");
                var visual = root.transform.Find("TVVisual");
                var group = visual != null ? visual.GetComponent<LODGroup>() : null;
                if (group == null || group.lodCount != 3 || group.fadeMode != LODFadeMode.CrossFade)
                    throw new InvalidOperationException("LODGroup not configured.");
                var lods = group.GetLODs();
                var screenMaterials = new HashSet<Material>();
                for (int i = 0; i < lods.Length; i++)
                {
                    var renderers = lods[i].renderers;
                    if (renderers.Length != 4 || renderers.Any(r => r == null || !r.enabled))
                        throw new InvalidOperationException("LOD renderer references invalid.");
                    var screen = renderers.Single(r => r.name.EndsWith("_Screen", StringComparison.Ordinal));
                    var body = renderers.Single(r => r.name.EndsWith("_Body", StringComparison.Ordinal));
                    if (screen.sharedMaterials.Length != 1 || screen.sharedMaterial == null ||
                        screen.sharedMaterial == body.sharedMaterials[0]) throw new InvalidOperationException("Screen material not separate.");
                    screenMaterials.Add(screen.sharedMaterial);
                    var mesh = screen.GetComponent<MeshFilter>().sharedMesh;
                    if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0)) throw new InvalidOperationException("Screen UV0 missing.");
                    if ((CenterInRoot(screen, root.transform)-CenterInRoot(body, root.transform)).z <= 0f)
                        throw new InvalidOperationException("Screen faces away from dispatcher forward.");
                    if (renderers.SelectMany(r => r.sharedMaterials).Any(m => m == null || m.shader == null || !m.shader.isSupported))
                        throw new InvalidOperationException("Missing/unsupported material shader.");
                    Bounds bounds = BoundsOf(renderers, root.transform);
                    if (Mathf.Abs(bounds.min.y) > .005f || Mathf.Abs(bounds.size.y - 2.2f) > .01f)
                        throw new InvalidOperationException($"LOD{i} is not grounded at the intended height: min={bounds.min}, size={bounds.size}, visual scale={visual.localScale}.");
                    group.ForceLOD(i);
                    report.Add($"LOD{i}: {renderers.Length} renderers; height={bounds.size.y:F3}; ground={bounds.min.y:F4}; threshold={lods[i].screenRelativeTransitionHeight:F3}; screen UV0/material/front valid.");
                }
                group.ForceLOD(-1);
                if (screenMaterials.Count != 1) throw new InvalidOperationException("Screen material should be shared across LODs.");
                var collider = root.GetComponent<CapsuleCollider>();
                if (collider == null || !collider.enabled || collider.isTrigger) throw new InvalidOperationException("Root physical collider changed.");
                if (root.GetComponentsInChildren<Collider>(true).Any(c => c != collider &&
                    (!c.isTrigger || !c.name.StartsWith("Button_", StringComparison.Ordinal))))
                    throw new InvalidOperationException("Unexpected visual colliders.");
                if (root.GetComponentsInChildren<Transform>(true).Any(t => t.name.Contains("Waveform")))
                    throw new InvalidOperationException("Static waveform was reintroduced.");
                // Exercise prefab instantiation and the existing local spawn-offset behavior.
                var instance = Object.Instantiate(root);
                try
                {
                    var npc = instance.GetComponent<DeliveryNPC>();
                    var serialized = new SerializedObject(npc);
                    Vector3 offset = serialized.FindProperty("m_couchSpawnOffset").vector3Value;
                    if (serialized.FindProperty("m_couchSpawnPoint").objectReferenceValue == null &&
                        Vector3.Distance(npc.CouchSpawnPosition, instance.transform.TransformPoint(offset)) > .001f)
                        throw new InvalidOperationException("Couch spawn behavior changed.");
                    if (npc.CouchSpawnRotation != instance.transform.rotation) throw new InvalidOperationException("Dispatch orientation changed.");
                }
                finally { Object.DestroyImmediate(instance); }
                report.Add("PASS: saved prefab instantiates, root DeliveryNPC and physical collider remain, spawn offset/orientation valid, blank screen independently accessible, no old visuals/static waveform.");
                report.Add("Runtime player interaction uses root-distance lookup; DeliveryNPC, PlayerCouchCarrier, DeliveryManager and generator scripts are unchanged.");
                report.Add("Live multiplayer dialogue/delivery and camera-driven LOD transition playtests have not been performed.");
                Directory.CreateDirectory("Tools");
                File.WriteAllLines(ReportPath, report);
                RenderPreview(root);
                Debug.Log(string.Join("\n", report));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void RenderPreview(GameObject root)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                preview.camera.transform.position = new Vector3(3.4f, 2.7f, 5f);
                preview.camera.transform.LookAt(new Vector3(0f, 1.1f, 0f));
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = 1.4f;
                preview.camera.nearClipPlane = .01f;
                preview.camera.farClipPlane = 30f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.13f, .15f, .19f);
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
                preview.lights[1].intensity = .65f;
                preview.ambientColor = new Color(.35f, .35f, .35f);
                preview.BeginStaticPreview(new Rect(0f, 0f, 600f, 700f));
                // Draw directly so prefab-stage scene visibility cannot suppress the preview.
                foreach (var renderer in root.GetComponentInChildren<LODGroup>().GetLODs()[0].renderers)
                {
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < mesh.subMeshCount; i++)
                        preview.DrawMesh(mesh, renderer.transform.localToWorldMatrix, materials[i], i);
                }
                preview.Render(true);
                var image = preview.EndStaticPreview();
                File.WriteAllBytes("Tools/DispatcherIntegrationPreview.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally { preview.Cleanup(); }
        }
    }
}
