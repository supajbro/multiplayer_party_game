using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Traffic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit Editor command only: saved assets remain editable without rerunning this tool.
internal static class RogueCarModelAuthoring
{
    private const string RoguePath = "Assets/_Project/Prefabs/Enemies/RogueCar.prefab";
    private const string VisualPath = "Assets/_Project/Prefabs/Vehicles/RogueCarVisual.prefab";
    private const string MaterialsPath = "Assets/_Project/Materials/Vehicles/RogueCar";

    [MenuItem("Couch Guys/Tools/Apply Rogue Car Model %#&F1")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before editing the Rogue Car prefab.");
        var root = PrefabUtility.LoadPrefabContents(RoguePath);
        GameObject visual = null;
        try
        {
            var data = new SerializedObject(root.GetComponent<TrafficVehicleController>());
            var anchor = (Transform)data.FindProperty("m_visualAnchor").objectReferenceValue;
            if (anchor == null) throw new InvalidOperationException("Missing existing visual anchor.");
            var oldBounds = BoundsOf(anchor.GetComponentsInChildren<Renderer>(true));
            var physicsBefore = root.GetComponents<Component>()
                .Where(c => c is Collider || c is Rigidbody).Select(c => EditorJsonUtility.ToJson(c)).ToArray();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
            if (asset == null)
            {
                visual = new GameObject("RogueCarVisual");
                var materials = new Dictionary<string, Material>();
                var lods = new LOD[3];
                var pivots = new Dictionary<string, Transform>();
                for (int lod = 0; lod < 3; lod++)
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                        $"Assets/_Project/Models/Cars/rogue_car_LOD{lod}.fbx");
                    if (source == null) throw new InvalidOperationException($"Missing Rogue Car LOD{lod}.");
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(source, visual.transform);
                    PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    var transforms = model.GetComponentsInChildren<Transform>(true);
                    var front = transforms.Single(t => t.name == $"Wheel_FL_LOD{lod}");
                    var rear = transforms.Single(t => t.name == $"Wheel_RL_LOD{lod}");
                    var heading = Vector3.ProjectOnPlane(front.position - rear.position, Vector3.up);
                    model.transform.rotation = Quaternion.FromToRotation(heading, Vector3.forward) * model.transform.rotation;
                    var renderers = model.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers)
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(m => MaterialFor(m, materials)).ToArray();
                    foreach (string key in new[] { "FL", "FR", "RL", "RR" })
                    {
                        var wheel = transforms.Single(t => t.name == $"Wheel_{key}_LOD{lod}");
                        if (!pivots.TryGetValue(key, out var pivot))
                        {
                            pivot = new GameObject("WheelPivot_" + key).transform;
                            pivot.SetParent(visual.transform, false);
                            pivot.position = wheel.position;
                            pivots.Add(key, pivot);
                        }
                        wheel.SetParent(pivot, true);
                    }
                    lods[lod] = new LOD(new[] { .45f, .20f, .025f }[lod], renderers);
                }
                var bounds = BoundsOf(lods[0].renderers);
                // Keep the old visual's road footprint and ground contact; physics is unchanged.
                float scale = oldBounds.size.z / bounds.size.z;
                visual.transform.localScale = Vector3.one * scale;
                visual.transform.position = new Vector3(oldBounds.center.x - bounds.center.x * scale,
                    oldBounds.min.y - bounds.min.y * scale, oldBounds.center.z - bounds.center.z * scale);
                var group = visual.AddComponent<LODGroup>();
                group.SetLODs(lods);
                group.RecalculateBounds();
                var wheels = new SerializedObject(visual.AddComponent<TrafficWheelAnimator>());
                SetReferences(wheels.FindProperty("m_frontWheels"), new Object[] { pivots["FL"], pivots["FR"] });
                SetReferences(wheels.FindProperty("m_rearWheels"), new Object[] { pivots["RL"], pivots["RR"] });
                wheels.FindProperty("m_radius").floatValue = .73f * scale;
                wheels.ApplyModifiedPropertiesWithoutUndo();
                asset = PrefabUtility.SaveAsPrefabAsset(visual, VisualPath);
                Debug.Log($"Rogue visual fitted at scale {scale:F4}; bounds {BoundsOf(lods[0].renderers)}");
            }
            // Replace only the current saved visual hierarchy, retaining its anchor and all root components.
            foreach (Transform child in anchor.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, anchor);
            data.Update();
            SetReferences(data.FindProperty("m_visualPrefabs"), new Object[] { asset });
            SetReferences(data.FindProperty("m_visualVariants"), new Object[] { instance });
            SetReferences(data.FindProperty("m_renderers"), instance.GetComponentsInChildren<Renderer>(true));
            data.ApplyModifiedPropertiesWithoutUndo();
            var physicsAfter = root.GetComponents<Component>()
                .Where(c => c is Collider || c is Rigidbody).Select(c => EditorJsonUtility.ToJson(c)).ToArray();
            if (!physicsBefore.SequenceEqual(physicsAfter)) throw new InvalidOperationException("Physics configuration changed.");
            PrefabUtility.SaveAsPrefabAsset(root, RoguePath);
        }
        finally
        {
            if (visual != null) Object.DestroyImmediate(visual);
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        Validate();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(RoguePath);
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    private static Bounds BoundsOf(Renderer[] renderers)
    {
        if (renderers.Length == 0) throw new InvalidOperationException("No visual renderers.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void SetReferences(SerializedProperty property, Object[] references)
    {
        property.arraySize = references.Length;
        for (int i = 0; i < references.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = references[i];
    }

    private static Material MaterialFor(Material source, Dictionary<string, Material> cache)
    {
        if (source == null) throw new InvalidOperationException("Missing imported material.");
        if (cache.TryGetValue(source.name, out var result)) return result;
        Directory.CreateDirectory(MaterialsPath);
        string path = MaterialsPath + "/" + source.name + ".mat";
        result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader unavailable.");
            result = new Material(shader) { name = source.name, enableInstancing = true };
            var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", source.name.Contains("Glass") ? .55f : .2f);
            result.SetFloat("_Metallic", source.name.Contains("Steel") ? .35f : 0f);
            float emission = source.name.Contains("Cyan") ? 2f : source.name.Contains("Headlight") ? .6f : 0f;
            if (emission > 0f)
            {
                result.EnableKeyword("_EMISSION");
                result.SetColor("_EmissionColor", color * emission);
                result.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            AssetDatabase.CreateAsset(result, path);
        }
        return cache[source.name] = result;
    }

    [MenuItem("Couch Guys/Tests/Validate Rogue Car Model")]
    public static void Validate()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(RoguePath);
        var data = new SerializedObject(root.GetComponent<TrafficVehicleController>());
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
        if (asset == null || data.FindProperty("m_visualPrefabs").GetArrayElementAtIndex(0).objectReferenceValue != asset)
            throw new InvalidOperationException("Rogue visual prefab reference is missing.");
        var group = root.GetComponentInChildren<LODGroup>(true);
        if (group == null || group.lodCount != 3) throw new InvalidOperationException("Expected three Rogue LODs.");
        foreach (var lod in group.GetLODs())
        {
            if (lod.renderers.Length == 0 || lod.renderers.Any(r => r == null)) throw new InvalidOperationException("Invalid LOD renderer references.");
            foreach (var renderer in lod.renderers)
                if (renderer.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))
                    throw new InvalidOperationException("Invalid Rogue material.");
        }
        var wheels = new SerializedObject(root.GetComponentInChildren<TrafficWheelAnimator>(true));
        foreach (string property in new[] { "m_frontWheels", "m_rearWheels" })
        {
            var list = wheels.FindProperty(property);
            if (list.arraySize != 2) throw new InvalidOperationException("Expected two wheel pivots per axle.");
            for (int i = 0; i < list.arraySize; i++)
            {
                var pivot = (Transform)list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (pivot == null || pivot.GetComponentsInChildren<MeshRenderer>(true).Length != 3)
                    throw new InvalidOperationException("Each wheel pivot must animate all three LODs.");
            }
        }
        Debug.Log("PASS ROGUE CAR MODEL: saved visual prefab, three LODs, URP materials, four wheel pivots; original physics preserved.");
    }
}
