using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>In-place vehicle migration. Retains prefab GUIDs, physics, networking IDs and scene overrides.</summary>
    public static class VehicleAssetUpgrade
    {
        public const string HatchbackPath = "Assets/_Project/Models/Cars/GreenHatchback.fbx";
        private const string ShutterPath = "Assets/_Project/Models/truck/TruckRollerShutter.fbx";
        private const string VisualPath = "Assets/_Project/Resources/Traffic/CarModels/GreenHatchback.prefab";
        private static readonly string[] SeatSuffixes = { "Front_Right", "Front_Left", "Rear_Right", "Rear_Left" };

        [InitializeOnLoadMethod]
        private static void Schedule() => EditorApplication.delayCall += EnsureAssets;

        [MenuItem("Couch Guys/Vehicles/Upgrade Truck Doors And Traffic")]
        public static void EnsureAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ShutterPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(HatchbackPath) == null) return;
            foreach (string name in new[] { "LightTruck", "GoodTruck", "BestTruck" })
            {
                string path = $"Assets/_Project/Prefabs/Vehicles/{name}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || Find(prefab.transform, "DoorEntryExit_Front_Right") != null) continue;
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    UpgradeTruckRoot(contents);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            GameObject traffic = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Traffic/TrafficCar.prefab");
            if (traffic == null || traffic.GetComponentInChildren<TrafficWheelAnimator>(true) == null ||
                traffic.GetComponentsInChildren<LODGroup>(true).Length != 1)
                TrafficSystemBuilder.BuildTrafficSystem();
            ShopAssetBuilder.EnsureDefaultCatalogue();
            AssetDatabase.SaveAssets();
            Debug.Log("Truck door/seat migration and single hatchback traffic integration complete.");
        }

        public static void UpgradeTruckRoot(GameObject root)
        {
            TruckController controller = root.GetComponent<TruckController>();
            Transform imported = Find(root.transform, "ImportedTruckModel");
            if (controller == null || imported == null) throw new UnityException("Truck model/controller is missing.");
            Transform visual = imported.parent;
            SerializedObject so = new(controller);
            Transform[] seats = SeatSuffixes.Select(s => Require(imported, "SeatAnchor_" + s)).ToArray();
            SetArray(so, "m_seatAnchors", seats);
            so.FindProperty("m_seatedRootOffset").vector3Value = new Vector3(0f, -.8f, 0f);
            Transform[] doors = new Transform[4];
            Bounds bounds = new(root.transform.position, Vector3.zero);
            foreach (Renderer renderer in imported.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(renderer.bounds);
            for (int i = 0; i < doors.Length; i++)
            {
                string[] parts = SeatSuffixes[i].Split('_');
                Transform surface = Require(imported, $"Cab_{parts[1]}_{parts[0]}_DoorSurface");
                Vector3 doorCentre = surface.GetComponent<Renderer>().bounds.center;
                Vector3 local = root.transform.InverseTransformPoint(doorCentre);
                float side = Mathf.Sign(root.transform.InverseTransformPoint(seats[i].position).x);
                // Read the imported door's real world centre, then clear its step, tyre and body colliders.
                local.x += side * .65f;
                local.y = root.transform.InverseTransformPoint(new Vector3(doorCentre.x, bounds.min.y, doorCentre.z)).y;
                GameObject marker = new("DoorEntryExit_" + SeatSuffixes[i]);
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition = local;
                marker.transform.localRotation = Quaternion.LookRotation(Vector3.right * side, Vector3.up);
                doors[i] = marker.transform;
            }
            SetArray(so, "m_exitPoints", doors);
            foreach (string old in new[] { "Seats", "ExitPointLeft", "ExitPointRight" })
            {
                Transform previous = root.transform.Find(old);
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
            }
            GameObject shutterSource = AssetDatabase.LoadAssetAtPath<GameObject>(ShutterPath);
            if (shutterSource == null) throw new UnityException("Generate TruckRollerShutter.fbx first.");
            GameObject shutter = (GameObject)PrefabUtility.InstantiatePrefab(shutterSource, visual);
            PrefabUtility.UnpackPrefabInstance(shutter, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            shutter.name = "RearRollerShutter";
            shutter.transform.SetLocalPositionAndRotation(imported.localPosition, imported.localRotation);
            shutter.transform.localScale = imported.localScale;
            Transform[] slats = new Transform[15];
            for (int i = 0; i < slats.Length; i++)
            {
                Transform mesh = Require(shutter.transform, $"RollerDoor_Slat_{i:00}");
                GameObject pivot = new($"ShutterSlatPivot_{i:00}");
                pivot.transform.SetParent(visual, false);
                pivot.transform.SetPositionAndRotation(mesh.GetComponent<Renderer>().bounds.center, visual.rotation);
                mesh.SetParent(pivot.transform, true);
                slats[i] = pivot.transform;
            }
            SetArray(so, "m_shutterSlats", slats);
            so.FindProperty("m_shutterSpace").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static GameObject BuildTrafficVisual()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(HatchbackPath);
            if (source == null) throw new UnityException("Generate GreenHatchback.fbx first.");
            GameObject root = new("GreenHatchback");
            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // Unity can infer a group from the FBX's LOD names. The wrapper owns all levels.
                foreach (LODGroup importedGroup in model.GetComponentsInChildren<LODGroup>(true))
                    Object.DestroyImmediate(importedGroup);
                model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                model.transform.localScale = Vector3.one * 100f;
                LODGroup group = root.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.None;
                group.animateCrossFading = false;
                LOD[] lods = new LOD[3];
                var front = new System.Collections.Generic.List<Transform>();
                var rear = new System.Collections.Generic.List<Transform>();
                for (int level = 0; level < 3; level++)
                {
                    Transform lod = Require(model.transform, $"LOD{level}");
                    foreach (string axle in new[] { "Front", "Rear" })
                    foreach (string side in new[] { "Left", "Right" })
                    {
                        Transform wheel = Require(lod, $"Wheel_{axle}_{side}_LOD{level}");
                        GameObject pivot = new($"{axle}{side}WheelPivot_LOD{level}");
                        pivot.transform.SetParent(lod, false);
                        pivot.transform.SetPositionAndRotation(wheel.position, root.transform.rotation);
                        wheel.SetParent(pivot.transform, true);
                        (axle == "Front" ? front : rear).Add(pivot.transform);
                    }
                    Renderer[] renderers = lod.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer renderer in renderers)
                    {
                        Material[] materials = renderer.sharedMaterials;
                        for (int m = 0; m < materials.Length; m++) materials[m] = TrafficMaterial(materials[m]);
                        renderer.sharedMaterials = materials;
                    }
                    // Same screen-height convention as the house: hard swaps, genuine reduced meshes.
                    lods[level] = new LOD(level == 0 ? .60f : level == 1 ? .30f : .01f, renderers);
                }
                group.SetLODs(lods);
                group.RecalculateBounds();
                TrafficWheelAnimator animator = root.AddComponent<TrafficWheelAnimator>();
                SerializedObject so = new(animator);
                SetArray(so, "m_frontWheels", front.ToArray());
                SetArray(so, "m_rearWheels", rear.ToArray());
                so.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, VisualPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material TrafficMaterial(Material source)
        {
            const string folder = "Assets/_Project/Resources/Traffic";
            string path = $"{folder}/{source.name}.mat";
            Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result != null) return result;
            bool glass = source.name.Contains("Glass");
            Shader shader = Shader.Find(glass ? "Universal Render Pipeline/Lit" : "Couch Guys/Toon Lit");
            if (shader == null) throw new UnityException("Required vehicle material shader was not found.");
            result = new Material(shader) { name = source.name };
            Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
            result.SetColor("_BaseColor", color);
            if (glass)
            {
                color.a = .28f;
                result.SetColor("_BaseColor", color);
                result.SetFloat("_Surface", 1f);
                result.SetFloat("_Blend", 0f);
                result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                result.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                result.SetFloat("_ZWrite", 0f);
                result.SetFloat("_Cull", 0f);
                result.SetFloat("_Smoothness", .28f);
                result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                result.SetOverrideTag("RenderType", "Transparent");
                result.renderQueue = (int)RenderQueue.Transparent;
                result.SetShaderPassEnabled("ShadowCaster", false);
            }
            AssetDatabase.CreateAsset(result, path);
            return result;
        }

        private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        private static Transform Require(Transform root, string name) => Find(root, name) ?? throw new UnityException($"Missing model part {name}");
        private static void SetArray(SerializedObject so, string property, Transform[] values)
        {
            SerializedProperty array = so.FindProperty(property);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
