using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Vehicles;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit authoring command only. Never runs on import or entering Play mode.
internal static class GetawayTruckModelAuthoring
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/ThiefGetawayTruck.prefab";
    private const string MaterialFolder = "Assets/_Project/Materials/Vehicles/ThiefTruck";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [MenuItem("Couch Guys/Tools/Apply Thief Truck Model %#&t")]
    private static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before authoring the prefab.");
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var truck = root.GetComponent<TruckController>();
            var data = new SerializedObject(truck);
            var old = (Transform)data.FindProperty("m_visualModel").objectReferenceValue;
            // Keep gameplay anchors and existing physics, including Inspector tuning.
            foreach (var name in new[] { "m_cameraTarget", "m_rearDoorInteractionPoint", "m_cargoLoadingPoint" })
                Preserve(data.FindProperty(name).objectReferenceValue as Transform, old, root.transform);
            foreach (var name in new[] { "m_seatAnchors", "m_exitPoints", "m_cargoPoints" })
            {
                var list = data.FindProperty(name);
                for (int i = 0; i < list.arraySize; i++) Preserve(list.GetArrayElementAtIndex(i).objectReferenceValue as Transform, old, root.transform);
            }
            foreach (var collider in root.GetComponentsInChildren<Collider>()) Preserve(collider.transform, old, root.transform);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var visual = new GameObject("ThiefTruckVisuals").transform;
            visual.SetParent(root.transform, false);
            var left = Pivot("RearDoor_Left", visual);
            var right = Pivot("RearDoor_Right", visual);
            var wheels = new[] { Pivot("Wheel_Front_Left", visual), Pivot("Wheel_Front_Right", visual),
                Pivot("Wheel_Rear_Left", visual), Pivot("Wheel_Rear_Right", visual) };
            var lods = new LOD[3];
            Renderer body = null;
            var materials = new Dictionary<string, Material>();
            for (int lod = 0; lod < 3; lod++)
            {
                string modelPath = $"Assets/_Project/Models/truck/thief_truck_01_LOD{lod}.fbx";
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), visual);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var all = model.GetComponentsInChildren<Transform>();
                var cabin = all.Single(t => t.name == $"Cabin_LOD{lod}");
                var doors = all.Where(t => t.name.StartsWith("RearCargoDoor_") && !t.name.EndsWith("_Mesh")).ToArray();
                Vector3 rear = (doors[0].position + doors[1].position) * .5f;
                var heading = Vector3.ProjectOnPlane(cabin.position - rear, Vector3.up);
                model.transform.rotation = Quaternion.FromToRotation(heading, root.transform.forward) * model.transform.rotation;
                foreach (var door in doors)
                {
                    var pivot = visual.InverseTransformPoint(door.position).x < 0f ? left : right;
                    Attach(door, pivot, lod == 0);
                }
                foreach (var wheel in all.Where(t => t.name.StartsWith("Wheel_") && !t.name.EndsWith("_Mesh")))
                {
                    Vector3 p = visual.InverseTransformPoint(wheel.position);
                    int index = (p.z > 0f ? 0 : 2) + (p.x < 0f ? 0 : 1);
                    Attach(wheel, wheels[index], lod == 0);
                }
                // Door/wheel meshes remain in the LOD even though their pivots are shared.
                var renderers = all.Where(t => t.GetComponent<Renderer>() != null).Select(t => t.GetComponent<Renderer>()).ToArray();
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => MaterialFor(m, materials)).ToArray();
                if (lod == 0) body = renderers.Single(r => r.name == "CargoBody_LOD0_Mesh");
                lods[lod] = new LOD(new[] { .42f, .18f, .025f }[lod], renderers);
            }
            var group = visual.gameObject.AddComponent<LODGroup>();
            group.SetLODs(lods); group.RecalculateBounds();
            data.Update();
            data.FindProperty("m_visualModel").objectReferenceValue = visual;
            data.FindProperty("m_leftDoorPivot").objectReferenceValue = left;
            data.FindProperty("m_rightDoorPivot").objectReferenceValue = right;
            data.FindProperty("m_rearDoorRotationAxis").vector3Value = Vector3.up;
            data.FindProperty("m_shutterSlats").arraySize = 0;
            data.FindProperty("m_shutterSpace").objectReferenceValue = null;
            data.FindProperty("m_destroyedRenderer").objectReferenceValue = body;
            var wheelRefs = data.FindProperty("m_wheels"); wheelRefs.arraySize = wheels.Length;
            for (int i = 0; i < wheels.Length; i++) wheelRefs.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            truck.CargoLoadingPoint.position = root.transform.TransformPoint(new Vector3(0f, 1.6f, -4.25f));
            truck.RearDoorInteractionPoint.position = root.transform.TransformPoint(new Vector3(0f, 1.3f, -4.45f));
            truck.GetCargoPoint(0).position = root.transform.TransformPoint(new Vector3(0f, 1.99f, -1.75f));
            var getaway = new SerializedObject(root.GetComponent<GetawayTruckController>());
            // Preserve the authored multi-colour palette instead of a whole-renderer tint.
            getaway.FindProperty("m_bodyRenderer").objectReferenceValue = null;
            getaway.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Validate();
    }

    private static Transform Pivot(string name, Transform parent)
    { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
    private static void Preserve(Transform t, Transform old, Transform root)
    { if (t != null && old != null && t != old && t.IsChildOf(old)) t.SetParent(root, true); }
    private static void Attach(Transform item, Transform pivot, bool first)
    { if (first) pivot.position = item.position; item.SetParent(pivot, true); }

    private static Material MaterialFor(Material source, Dictionary<string, Material> cache)
    {
        string name = source.name;
        if (cache.TryGetValue(name, out var found)) return found;
        string path = MaterialFolder + "/" + name + ".mat";
        found = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (found == null)
        {
            found = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            // FBX diffuse colours are imported by Unity; retain their authored values.
            found.SetColor("_BaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color);
            found.SetFloat("_Smoothness", name.Contains("Glass") ? .65f : .25f);
            found.SetFloat("_Metallic", name.Contains("Steel") || name.Contains("Silver") || name.Contains("Armour") ? .45f : 0f);
            AssetDatabase.CreateAsset(found, path);
        }
        return cache[name] = found;
    }

    [MenuItem("Couch Guys/Tests/Validate Thief Truck Visuals %#&y")]
    private static void Validate()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = Object.Instantiate(prefab);
        try
        {
            var truck = instance.GetComponent<TruckController>();
            var data = new SerializedObject(truck);
            var group = instance.GetComponentInChildren<LODGroup>();
            if (group == null || group.lodCount != 3 || group.GetLODs().Any(l => l.renderers.Length != 18 || l.renderers.Any(r => r == null)))
                throw new InvalidOperationException("Missing truck LOD renderers.");
            typeof(TruckController).GetMethod("Awake", Private).Invoke(truck, null);
            var sync = typeof(TruckController).GetField("m_rearDoorsOpen", Private).GetValue(truck);
            sync.GetType().GetProperty("Value").SetValue(sync, true);
            var animate = typeof(TruckController).GetMethod("UpdateVisuals", Private);
            if (animate == null) throw new InvalidOperationException("Truck visual update method missing.");
            // Force a completed door step without entering or modifying the open scene.
            for (int i = 0; i < 120; i++) animate.Invoke(truck, new object[] { .02f });
            var left = (Transform)data.FindProperty("m_leftDoorPivot").objectReferenceValue;
            var right = (Transform)data.FindProperty("m_rightDoorPivot").objectReferenceValue;
            if (truck.RearDoorOpenProgress < .95f || Quaternion.Angle(left.localRotation, Quaternion.identity) < 70f || Quaternion.Angle(right.localRotation, Quaternion.identity) < 70f)
                throw new InvalidOperationException("Rear doors did not open.");
            sync.GetType().GetProperty("Value").SetValue(sync, false);
            for (int i = 0; i < 120; i++) animate.Invoke(truck, new object[] { .02f });
            if (truck.RearDoorOpenProgress > .05f) throw new InvalidOperationException("Rear doors did not close.");
            Debug.Log("PASS THIEF TRUCK MODEL: three LODs, 54 renderers, shared wheels and rear doors; opening and closing validated.");
        }
        finally { Object.DestroyImmediate(instance); }
    }
}
