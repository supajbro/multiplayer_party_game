using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Depot;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Updates the existing depot wrapper without replacing gameplay children.</summary>
    public static class DepotPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/World/CouchGuysDepotV2.prefab";
        private const string ModelPath = "Assets/_Project/Models/depot/depot.fbx";
        private const string MeshPath = "Assets/_Project/Generated/DepotLods/Depot_CombinedLods.asset";

        [MenuItem("Couch Guys/Update Depot Prefab")]
        public static void Build()
        {
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            // Same imported mesh reduction used by the house builder.
            if (!importer.generateMeshLods)
            {
                importer.generateMeshLods = true;
                importer.SaveAndReimport();
            }
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            GameObject model = null;
            try
            {
                Transform visuals = Find(root.transform, "DepotVisuals");
                if (visuals == null) throw new InvalidOperationException("DepotVisuals is missing.");
                Transform oldLods = root.transform.Find("DepotStaticLODs");
                if (oldLods != null) Object.DestroyImmediate(oldLods.gameObject);
                foreach (Transform child in visuals.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null) continue;
                    if (IsRetired(child.name) || child.name.StartsWith("DepotPolish_"))
                        Object.DestroyImmediate(child.gameObject);
                }

                model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                // Existing FBX mesh references and all functional objects keep their IDs.
                foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                {
                    if (!child.name.StartsWith("DepotPolish_"))
                    {
                        // Restore only build inputs on repeat runs. The saved prefab
                        // contains combined static renderers, not duplicate originals.
                        MeshRenderer source = child.GetComponent<MeshRenderer>();
                        Transform existing = Find(visuals, child.name);
                        if (source != null && existing != null && !IsRetired(child.name) &&
                            !child.name.Contains("_Slat_"))
                        {
                            MeshRenderer renderer = existing.GetComponent<MeshRenderer>();
                            if (renderer == null) renderer = existing.gameObject.AddComponent<MeshRenderer>();
                            renderer.sharedMaterials = source.sharedMaterials;
                        }
                        continue;
                    }
                    GameObject part = Object.Instantiate(child.gameObject, visuals, false);
                    part.name = child.name;
                    if (part.name.Contains("RearWall_Closure"))
                    {
                        Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
                        BoxCollider wall = part.AddComponent<BoxCollider>();
                        wall.center = mesh.bounds.center;
                        wall.size = mesh.bounds.size;
                    }
                }
                Object.DestroyImmediate(model);
                model = null;

                Transform rearHeader = Find(visuals, "TruckArrival_RearWallHeader");
                if (rearHeader != null && rearHeader.GetComponent<Collider>() == null)
                {
                    Mesh mesh = rearHeader.GetComponent<MeshFilter>().sharedMesh;
                    BoxCollider wall = rearHeader.gameObject.AddComponent<BoxCollider>();
                    wall.center = mesh.bounds.center;
                    wall.size = mesh.bounds.size;
                }

                DepotController depot = root.GetComponent<DepotController>();
                Transform spawn = root.transform.Find("TruckSpawnPoint");
                if (spawn == null)
                {
                    spawn = new GameObject("TruckSpawnPoint").transform;
                    spawn.SetParent(root.transform, false);
                    spawn.position = Find(root.transform, "CGD_MARK_Truck_DispatchStaging").position;
                    spawn.rotation = Find(root.transform, "RoadConnection").rotation;
                }
                depot.SetTruckSpawnPoint(spawn);
                // The old offstage marker is retained for compatibility, but no longer used.
                SerializedObject data = new SerializedObject(depot);
                Transform[] fallbacks = { Find(root.transform, "CGD_MARK_Truck_Path_02_Centre"),
                    Find(root.transform, "CGD_MARK_Truck_Path_01_InsideRear") };
                SerializedProperty fallbackData = data.FindProperty("m_vehicleFallbacks");
                fallbackData.arraySize = fallbacks.Length;
                for (int i = 0; i < fallbacks.Length; i++)
                    fallbackData.GetArrayElementAtIndex(i).objectReferenceValue = fallbacks[i];
                data.ApplyModifiedPropertiesWithoutUndo();
                ConfigureDoor(root, visuals, "m_exitDoor", "ExitRollerDoor");
                ConfigureDoor(root, visuals, "m_vehicleGarageDoor", "TruckGarageDoor");
                BuildLods(root, visuals);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Depot prefab updated: interior spawn, enclosed rear, authored doors and three combined LODs.");
            }
            finally
            {
                if (model != null) Object.DestroyImmediate(model);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureDoor(GameObject root, Transform visuals, string field, string prefix)
        {
            SerializedObject data = new SerializedObject(root.GetComponent<DepotController>());
            SerializedProperty rig = data.FindProperty(field);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"Assets/_Project/Animations/Depot/{prefix}_Open.anim");
            Transform bindingRoot = visuals.Find(prefix + "_Playback");
            if (bindingRoot == null)
            {
                bindingRoot = new GameObject(prefix + "_Playback").transform;
                bindingRoot.SetParent(visuals, false);
            }
            Animator animator = bindingRoot.GetComponent<Animator>();
            if (animator == null) animator = bindingRoot.gameObject.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            Transform[] slats = Enumerable.Range(0, 12)
                .Select(i => Find(visuals, $"{prefix}_Slat_{i:00}")).ToArray();
            foreach (Transform slat in slats)
            {
                if (slat == null) throw new InvalidOperationException("Missing slat: " + prefix);
                slat.SetParent(bindingRoot, false);
                GameObjectUtility.SetStaticEditorFlags(slat.gameObject, 0);
            }
            rig.FindPropertyRelative("m_animationRoot").objectReferenceValue = bindingRoot.gameObject;
            rig.FindPropertyRelative("m_authoredAnimation").objectReferenceValue = clip;
            // Sample the whole clip once per endpoint, never one slat at a time.
            // Restore a complete closed pose in the saved prefab and repair its fallback.
            for (int endpoint = 1; endpoint >= 0; endpoint--)
            {
                clip.SampleAnimation(bindingRoot.gameObject, endpoint * clip.length);
                SerializedProperty positions = rig.FindPropertyRelative(endpoint == 0 ? "m_closedPositions" : "m_openPositions");
                SerializedProperty rotations = rig.FindPropertyRelative(endpoint == 0 ? "m_closedRotations" : "m_openRotations");
                positions.arraySize = rotations.arraySize = slats.Length;
                for (int i = 0; i < slats.Length; i++)
                {
                    positions.GetArrayElementAtIndex(i).vector3Value = slats[i].localPosition;
                    rotations.GetArrayElementAtIndex(i).quaternionValue = slats[i].localRotation;
                }
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildLods(GameObject root, Transform visuals)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Generated"))
                AssetDatabase.CreateFolder("Assets/_Project", "Generated");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Generated/DepotLods"))
                AssetDatabase.CreateFolder("Assets/_Project/Generated", "DepotLods");
            // Retain subasset IDs on subsequent builds.
            Dictionary<string, Mesh> stored = AssetDatabase.LoadAllAssetsAtPath(MeshPath)
                .OfType<Mesh>().ToDictionary(m => m.name);
            GameObject lodRoot = new GameObject("DepotStaticLODs");
            lodRoot.transform.SetParent(root.transform, false);
            MeshRenderer[] sources = visuals.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !r.name.Contains("_Slat_")).ToArray();
            LOD[] lods = new LOD[3];
            for (int level = 0; level < 3; level++)
            {
                GameObject levelRoot = new GameObject($"Depot_LOD{level}");
                levelRoot.transform.SetParent(lodRoot.transform, false);
                Dictionary<Material, List<CombineInstance>> groups = new();
                List<Mesh> temporary = new();
                try
                {
                    foreach (MeshRenderer source in sources)
                    {
                        if (OmitDetail(source.name, level)) continue;
                        MeshFilter filter = source.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null) continue;
                        Mesh mesh = CopyMeshLod(filter.sharedMesh, level);
                        temporary.Add(mesh);
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            Material mat = source.sharedMaterials[Mathf.Min(sub, source.sharedMaterials.Length - 1)];
                            if (mat == null) throw new InvalidOperationException("Missing depot material: " + source.name);
                            if (!groups.TryGetValue(mat, out var list)) groups.Add(mat, list = new());
                            list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub,
                                transform = levelRoot.transform.worldToLocalMatrix * source.transform.localToWorldMatrix });
                        }
                    }
                    List<Renderer> renderers = new();
                    foreach (var group in groups.OrderBy(g => g.Key.name))
                    {
                        string name = $"Depot_LOD{level}_{group.Key.name}";
                        Mesh combined = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                        combined.CombineMeshes(group.Value.ToArray(), true, true);
                        combined.RecalculateBounds();
                        if (stored.TryGetValue(name, out Mesh persisted))
                        {
                            EditorUtility.CopySerialized(combined, persisted);
                            Object.DestroyImmediate(combined);
                            combined = persisted;
                        }
                        else if (!File.Exists(MeshPath)) AssetDatabase.CreateAsset(combined, MeshPath);
                        else AssetDatabase.AddObjectToAsset(combined, MeshPath);
                        GameObject part = new GameObject(name);
                        part.transform.SetParent(levelRoot.transform, false);
                        part.AddComponent<MeshFilter>().sharedMesh = combined;
                        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = group.Key;
                        renderers.Add(renderer);
                    }
                    lods[level] = new LOD(new[] { .60f, .30f, .01f }[level], renderers.ToArray());
                    Debug.Log($"Depot LOD{level}: {renderers.Count} renderers, " +
                        renderers.Sum(r => ((MeshRenderer)r).GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3) + " triangles");
                }
                finally { foreach (Mesh mesh in temporary) Object.DestroyImmediate(mesh); }
            }
            // Keep every collider, marker, button and animated slat active at every distance.
            foreach (MeshRenderer source in sources) Object.DestroyImmediate(source);
            LODGroup lodGroup = lodRoot.AddComponent<LODGroup>();
            lodGroup.fadeMode = LODFadeMode.None;
            lodGroup.animateCrossFading = false;
            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
        }

        private static bool OmitDetail(string name, int level)
        {
            if (level == 0) return false;
            string[] fine = { "Mortar", "Seam", "PipeClip", "VentLouvre", "JambSafety", "CanopyHighlight",
                "ConveyorRoller", "MotorAccent", "DoorHandle", "CounterPaper", "CounterPen" };
            if (fine.Any(name.Contains)) return true;
            if (level == 1) return false;
            string[] small = { "Batten", "RainPipe", "CanopyBracket", "PierBoot", "PierCapital", "Roof_SquareServiceVent",
                "VentHood", "VentCurb", "ExteriorLamp", "EndDriveMotor", "GroundSill", "VerticalSeam", "WindowTrim",
                "Conveyor", "BackstageFloorPad", "FrogCounter", "Crate", "Cardboard", "CeilingLight" };
            return small.Any(name.Contains);
        }

        private static Mesh CopyMeshLod(Mesh source, int level)
        {
            Mesh mesh = new Mesh { indexFormat = source.indexFormat };
            using (Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(source))
            {
                mesh.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    var vertices = data[0].GetVertexData<byte>(stream);
                    mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, stream);
                }
            }
            int lod = source.lodCount > 0 ? Mathf.Clamp(level, 0, source.lodCount - 1) : -1;
            mesh.subMeshCount = source.subMeshCount;
            for (int sub = 0; sub < source.subMeshCount; sub++)
                mesh.SetIndices(source.GetIndices(sub, lod, true), source.GetTopology(sub), sub, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool IsRetired(string name) => new[] { "RearTruckPortal_", "RearPortal_", "RearArrival_", "ArrivalServiceTunnel_" }
            .Any(name.StartsWith);

        private static Transform Find(Transform root, string name) => root
            .GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    }
}
