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

        [MenuItem("Couch Guys/Update Depot Prefab %#&d")]
        public static void Build()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer.isReadable) { importer.isReadable = false; importer.SaveAndReimport(); }
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            GameObject model = null;
            try
            {
                Transform visuals = Find(root.transform, "DepotVisuals");
                Transform oldLods = root.transform.Find("DepotStaticLODs");
                if (oldLods != null) Object.DestroyImmediate(oldLods.gameObject);
                model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                var inputs = model.GetComponentsInChildren<MeshFilter>(true);
                var names = new HashSet<string>(inputs.Select(f => f.name));
                foreach (MeshFilter old in visuals.GetComponentsInChildren<MeshFilter>(true))
                    if (!names.Contains(old.name))
                    {
                        // Authored architecture modules share renderers; preserve the saved simple collision shapes.
                        if (old.GetComponent<Collider>() != null)
                        {
                            var renderer = old.GetComponent<MeshRenderer>();
                            if (renderer != null) Object.DestroyImmediate(renderer);
                            Object.DestroyImmediate(old);
                        }
                        else Object.DestroyImmediate(old.gameObject);
                    }
                foreach (MeshFilter source in inputs)
                {
                    Transform existing = Find(visuals, source.name);
                    if (existing == null)
                    {
                        existing = Object.Instantiate(source.gameObject, visuals, false).transform;
                        existing.name = source.name;
                    }
                    existing.localPosition = source.transform.localPosition;
                    existing.localRotation = source.transform.localRotation;
                    existing.localScale = source.transform.localScale;
                    existing.GetComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                    MeshRenderer renderer = existing.GetComponent<MeshRenderer>();
                    if (renderer == null) renderer = existing.gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
                    // Refit retained collision after structural changes.
                    BoxCollider box = existing.GetComponent<BoxCollider>();
                    bool solid = existing.name.Contains("WallInfill") || existing.name.Contains("Entrance_Header") ||
                        existing.name.Contains("Frame_Jamb") || existing.name.Contains("Bollard") ||
                        existing.name.Contains("Hub_Carton") || existing.name.Contains("Hub_PlantPot") ||
                        existing.name.Contains("Hub_GlassPanel") || existing.name.Contains("_Slat_");
                    if (box == null && solid) box = existing.gameObject.AddComponent<BoxCollider>();
                    if (box != null) { box.center = source.sharedMesh.bounds.center; box.size = source.sharedMesh.bounds.size; }
                    if (existing.name.StartsWith("Hub_GlassPanel")) ConfigureGlassMaterial(renderer);
                }
                Object.DestroyImmediate(model); model = null;
                var data = new SerializedObject(root.GetComponent<DepotController>());
                // Keep the purchased truck heading aligned with the existing exit.
                Transform spawn = Find(root.transform, "TruckSpawnPoint");
                spawn.rotation = Find(root.transform, "RoadConnection").rotation;
                ConfigureDoor(root, visuals, "m_vehicleGarageDoor", "TruckGarageDoor");
                ConfigureGlass(root, visuals);
                data.Update();
                // Individual moving slats now provide physical collision throughout travel.
                var garage = data.FindProperty("m_vehicleGarageDoor");
                var blocker = (BoxCollider)garage.FindPropertyRelative("m_blockingCollider").objectReferenceValue;
                if (blocker != null) blocker.enabled = false;
                garage.FindPropertyRelative("m_blockingCollider").objectReferenceValue = null;
                Transform centre = Find(root.transform, "CGD_MARK_ExitDoor_SafetyVolume");
                data.FindProperty("m_entranceDetectionCentre").objectReferenceValue = centre;
                data.ApplyModifiedPropertiesWithoutUndo();
                foreach (DepotDoorButton button in root.GetComponentsInChildren<DepotDoorButton>(true))
                {
                    button.Configure(DepotDoorKind.VehicleGarage);
                    Vector3 p = button.transform.localPosition;
                    p.x = 2.5f; p.y = 1.6f; button.transform.localPosition = p;
                }
                // Keep decorative props editable, combine only fixed architectural parts.
                BuildLods(root, visuals);
                foreach (Transform lamp in visuals.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Hub_PendantGlow")))
                {
                    Light light = lamp.GetComponent<Light>();
                    if (light == null) light = lamp.gameObject.AddComponent<Light>();
                    light.type = LightType.Point; light.color = new Color(1f,.83f,.55f);
                    light.intensity = 2f; light.range = 8f; light.shadows = LightShadows.None;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Validate();
                Debug.Log("Depot concept integration complete: one truck roller, two automatic glass panels, preserved gameplay references.");
            }
            finally
            {
                if (model != null) Object.DestroyImmediate(model);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Couch Guys/Preview Depot %#&p")]
        public static void Preview()
        {
            var preview = new PreviewRenderUtility();
            try
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                preview.AddSingleGO(instance);
                foreach (LODGroup group in instance.GetComponentsInChildren<LODGroup>()) group.ForceLOD(0);
                preview.camera.transform.position = new Vector3(0,4.4f,-3.2f);
                preview.camera.transform.LookAt(new Vector3(0,3.5f,10f));
                preview.camera.fieldOfView = 90;
                preview.camera.nearClipPlane = .05f; preview.camera.farClipPlane = 100;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.12f,.2f,.3f);
                preview.lights[0].intensity = 1.8f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35,160,0);
                preview.lights[1].intensity = 1.3f;
                preview.ambientColor = new Color(.55f,.6f,.7f);
                preview.BeginStaticPreview(new Rect(0,0,1400,850));
                preview.Render(true);
                Texture2D result = preview.EndStaticPreview();
                File.WriteAllBytes("Temp/depot-unity-preview.png", result.EncodeToPNG());
                Object.DestroyImmediate(result);
                instance.GetComponent<DepotController>().ReleaseDoorAnimations();
            }
            finally { preview.Cleanup(); }
        }
        private static void ConfigureGlassMaterial(MeshRenderer renderer)
        {
            const string path = "Assets/_Project/Generated/DepotLods/DepotGlass.mat";
            Material glass = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (glass == null)
            {
                glass = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                glass.name = "DepotGlass";
                glass.SetFloat("_Surface", 1); glass.SetFloat("_Blend", 0);
                glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                glass.SetFloat("_ZWrite", 0); glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                glass.SetColor("_BaseColor", new Color(.12f,.62f,.76f,.32f));
                glass.SetFloat("_Smoothness", .75f); glass.renderQueue = 3000;
                AssetDatabase.CreateAsset(glass, path);
            }
            renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m.name.Contains("TintedGlass") ? glass : m).ToArray();
        }

        private static void ConfigureGlass(GameObject root, Transform visuals)
        {
            var data = new SerializedObject(root.GetComponent<DepotController>());
            var rig = data.FindProperty("m_exitDoor");
            var panels = new[] { Find(visuals, "Hub_GlassPanel_0"), Find(visuals, "Hub_GlassPanel_1") };
            rig.FindPropertyRelative("m_authoredAnimation").objectReferenceValue = null;
            rig.FindPropertyRelative("m_animationRoot").objectReferenceValue = null;
            var blocker = (BoxCollider)rig.FindPropertyRelative("m_blockingCollider").objectReferenceValue;
            if (blocker != null) blocker.enabled = false;
            rig.FindPropertyRelative("m_blockingCollider").objectReferenceValue = null;
            foreach (string field in new[] { "m_slats", "m_closedPositions", "m_openPositions", "m_closedRotations", "m_openRotations" })
                rig.FindPropertyRelative(field).arraySize = 2;
            for (int i=0;i<2;i++)
            {
                rig.FindPropertyRelative("m_slats").GetArrayElementAtIndex(i).objectReferenceValue = panels[i];
                Vector3 closed = panels[i].localPosition;
                rig.FindPropertyRelative("m_closedPositions").GetArrayElementAtIndex(i).vector3Value = closed;
                rig.FindPropertyRelative("m_openPositions").GetArrayElementAtIndex(i).vector3Value = closed + new Vector3(i == 0 ? 2.05f : -2.05f,0,0);
                rig.FindPropertyRelative("m_closedRotations").GetArrayElementAtIndex(i).quaternionValue = panels[i].localRotation;
                rig.FindPropertyRelative("m_openRotations").GetArrayElementAtIndex(i).quaternionValue = panels[i].localRotation;
                GameObjectUtility.SetStaticEditorFlags(panels[i].gameObject,0);
            }
            BoxCollider safety = (BoxCollider)rig.FindPropertyRelative("m_safetyVolume").objectReferenceValue;
            safety.size = new Vector3(4f,3.5f,2.5f);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Couch Guys/Validate Depot")]
        public static void Validate()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(asset);
            try
            {
                var depot = instance.GetComponent<DepotController>();
                if (!depot.HasConveyor || depot.ConveyorPath.Count != 5) throw new Exception("Conveyor references lost");
                if (Find(instance.transform,"ExitRollerDoor_Slat_00") != null) throw new Exception("Extra roller remains");
                var panels = new[] { Find(instance.transform,"Hub_GlassPanel_0"), Find(instance.transform,"Hub_GlassPanel_1") };
                var closed = panels.Select(p => p.localPosition).ToArray();
                depot.ApplyDoorStates(true,true,true);
                for(int i=0;i<2;i++) if (Vector3.Distance(closed[i],panels[i].localPosition)<2f) throw new Exception("Glass panel failed to open");
                depot.ApplyDoorStates(false,false,true);
                for(int i=0;i<2;i++) if (Vector3.Distance(closed[i],panels[i].localPosition)>.001f) throw new Exception("Glass panel failed to close");
                if (instance.GetComponentsInChildren<MeshFilter>().Any(f => f.sharedMesh == null)) throw new Exception("Missing mesh");
                if(Vector3.Dot(depot.VehicleArrival.forward,Find(instance.transform,"RoadConnection").forward)<.999f) throw new Exception("Truck heading mismatch");
                File.WriteAllText("Temp/depot-validation.txt", "PASS: saved prefab, 2 sliding panels, animated roller, conveyor references, truck heading, mesh references");
            }
            finally { instance.GetComponent<DepotController>().ReleaseDoorAnimations(); Object.DestroyImmediate(instance); }
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
                .Where(r => !r.name.Contains("_Slat_") && !IsAdjustable(r.name)).ToArray();
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

        private static bool IsAdjustable(string name) => name.StartsWith("Hub_");

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
                using var positions = new Unity.Collections.NativeArray<Vector3>(source.vertexCount, Unity.Collections.Allocator.Temp);
                data[0].GetVertices(positions); mesh.SetVertices(positions);
                if (source.HasVertexAttribute(VertexAttribute.Normal))
                {
                    using var normals = new Unity.Collections.NativeArray<Vector3>(source.vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetNormals(normals); mesh.SetNormals(normals);
                }
                if (source.HasVertexAttribute(VertexAttribute.Tangent))
                {
                    using var tangents = new Unity.Collections.NativeArray<Vector4>(source.vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetTangents(tangents); mesh.SetTangents(tangents);
                }
                for (int channel=0;channel<2;channel++)
                {
                    if (!source.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0+channel))) continue;
                    using var uv = new Unity.Collections.NativeArray<Vector2>(source.vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetUVs(channel,uv); mesh.SetUVs(channel,uv);
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
