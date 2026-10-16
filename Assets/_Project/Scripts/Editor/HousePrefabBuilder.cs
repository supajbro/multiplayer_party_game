using System.Collections.Generic;
using System.Text;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Builds non-destructive wrapper prefabs around the imported house FBX assets.</summary>
    public static class HousePrefabBuilder
    {
        public const string HouseModelPath =
            "Assets/_Project/Models/Houses/CouchGuys_House_01.fbx";
        public const string House01PrefabPath = "Assets/_Project/Prefabs/Houses/House_01.prefab";
        public const string House02PrefabPath = "Assets/_Project/Prefabs/Houses/House_02.prefab";

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabsAfterImport()
        {
            if (!PrefabUsesCurrentModel(House01PrefabPath) ||
                !PrefabUsesCurrentModel(House02PrefabPath))
            {
                EditorApplication.delayCall += EnsureHousePrefabs;
            }
        }

        [MenuItem("Couch Guys/Build House Prefabs")]
        public static void EnsureHousePrefabs()
        {
            EnsureFolder("Assets/_Project/Prefabs", "Houses");
            // Keep both wrapper assets (and therefore all existing lot/scene references) while
            // intentionally sourcing them from the same approved Couch Guys house model.
            BuildWrapper(HouseModelPath, House01PrefabPath, "House_01");
            BuildWrapper(HouseModelPath, House02PrefabPath, "House_02");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static GameObject[] LoadHousePrefabs()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(House01PrefabPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(House02PrefabPath) == null)
            {
                EnsureHousePrefabs();
            }

            return new[]
            {
                AssetDatabase.LoadAssetAtPath<GameObject>(House01PrefabPath),
                AssetDatabase.LoadAssetAtPath<GameObject>(House02PrefabPath)
            };
        }

        public static void BuildFromCommandLine()
        {
            EnsureHousePrefabs();
        }

        private static void BuildWrapper(string modelPath, string prefabPath, string wrapperName)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogWarning($"House model is unavailable at {modelPath}; its wrapper was not built.");
                return;
            }

            GameObject root = new GameObject(wrapperName);
            try
            {
                Transform visualRoot = new GameObject("VisualRoot").transform;
                visualRoot.SetParent(root.transform, false);
                GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                modelInstance.name = model.name;
                modelInstance.transform.SetParent(visualRoot, false);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localRotation = Quaternion.identity;
                modelInstance.transform.localScale = Vector3.one;

                LogModelInspection(modelPath, modelInstance);
                CorrectModelOrientationAndPivot(visualRoot, modelInstance);
                // Scale around the corrected ground-level wrapper pivot.
                visualRoot.localScale = Vector3.one * 2f;
                HouseExteriorModuleSet exteriorModules = ConfigureExteriorModules(
                    root.transform,
                    visualRoot,
                    modelInstance);
                AddCompoundStructuralColliders(modelInstance);

                Transform roadConnection = CreatePoint(root.transform, "RoadConnection", new Vector3(0f, 0f, 10f));
                Transform deliveryPoint = CreatePoint(root.transform, "DeliveryPoint", new Vector3(0f, 0f, 10.5f));
                GeneratedProperty property = root.AddComponent<GeneratedProperty>();
                property.ConfigurePrefab(
                    new Vector2Int(2, 2),
                    roadConnection,
                    deliveryPoint,
                    visualRoot,
                    true,
                    exteriorModules != null ? exteriorModules.DrivewayAnchor : null);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (saved == null)
                {
                    throw new UnityException($"Failed to save house wrapper at {prefabPath}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CorrectModelOrientationAndPivot(Transform visualRoot, GameObject modelInstance)
        {
            if (!TryGetRendererBounds(modelInstance.GetComponentsInChildren<Renderer>(true), out Bounds bounds))
            {
                throw new UnityException($"House model {modelInstance.name} contains no renderers.");
            }

            // CouchGuys_House_01.fbx is imported by Unity with its Blender Z-up axis already
            // converted to Unity Y-up. The legacy houses required an extra -90 degree tilt;
            // applying it here lays the new house on its back.
            visualRoot.localRotation = Quaternion.identity;
            if (!TryGetRendererBounds(modelInstance.GetComponentsInChildren<Renderer>(true), out bounds))
            {
                return;
            }

            visualRoot.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        private static void LogModelInspection(string modelPath, GameObject modelInstance)
        {
            Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
            TryGetRendererBounds(renderers, out Bounds bounds);
            StringBuilder hierarchy = new StringBuilder();
            AppendHierarchy(modelInstance.transform, hierarchy, 0);
            Debug.Log(
                $"House model inspection: {modelPath}\n" +
                $"Import root scale={modelInstance.transform.localScale}, rotation={modelInstance.transform.localEulerAngles}\n" +
                $"Renderer count={renderers.Length}, world bounds center={bounds.center}, size={bounds.size}, minY={bounds.min.y}\n" +
                hierarchy,
                modelInstance);
        }

        private static void AddCompoundStructuralColliders(GameObject modelInstance)
        {
            MeshFilter[] meshFilters = modelInstance.GetComponentsInChildren<MeshFilter>(true);
            int colliderCount = 0;
            for (int index = 0; index < meshFilters.Length; index++)
            {
                MeshFilter meshFilter = meshFilters[index];
                if (meshFilter == null || meshFilter.sharedMesh == null ||
                    !ShouldCreateStructuralCollider(meshFilter.gameObject.name))
                {
                    continue;
                }

                BoxCollider collider = meshFilter.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    collider = meshFilter.gameObject.AddComponent<BoxCollider>();
                }

                Bounds meshBounds = meshFilter.sharedMesh.bounds;
                collider.center = meshBounds.center;
                collider.size = meshBounds.size;
                colliderCount++;
            }

            if (colliderCount == 0)
            {
                Debug.LogWarning(
                    $"No named structural meshes were found beneath {modelInstance.name}; " +
                    "the neighbourhood generator will fall back to a bounds collider.",
                    modelInstance);
            }
        }

        private static HouseExteriorModuleSet ConfigureExteriorModules(
            Transform houseRoot,
            Transform visualRoot,
            GameObject modelInstance)
        {
            List<Transform> drivewaySlabs = FindNamedDescendants(modelInstance.transform, "Driveway_Slab_");
            List<Transform> footpathSlabs = FindNamedDescendants(modelInstance.transform, "Front_Path_Step_");
            List<Transform> lightParts = new List<Transform>();
            lightParts.AddRange(FindNamedDescendants(modelInstance.transform, "PathLight_Post_00"));
            lightParts.AddRange(FindNamedDescendants(modelInstance.transform, "PathLight_Glow_00"));
            lightParts.AddRange(FindNamedDescendants(modelInstance.transform, "PathLight_Cap_00"));
            if (drivewaySlabs.Count == 0 || footpathSlabs.Count == 0 || lightParts.Count == 0)
            {
                Debug.LogWarning(
                    $"House model '{modelInstance.name}' is missing one or more exterior module groups; " +
                    "the modular driveway and front path cannot be configured.",
                    modelInstance);
                return null;
            }

            Transform templatesRoot = new GameObject("ExteriorModuleTemplates").transform;
            templatesRoot.SetParent(visualRoot, false);
            Transform drivewayTemplate = CreateNormalisedTemplate(
                templatesRoot,
                "DrivewaySlabTemplate",
                new[] { drivewaySlabs[0] },
                out Vector3 drivewaySize);
            Transform footpathTemplate = CreateNormalisedTemplate(
                templatesRoot,
                "FootpathSlabTemplate",
                new[] { footpathSlabs[0] },
                out Vector3 footpathSize);
            Transform lightTemplate = CreateNormalisedTemplate(
                templatesRoot,
                "PathLightTemplate",
                lightParts,
                out Vector3 lightSize);
            float drivewaySpacing = CalculateLocalSpacing(drivewaySlabs, visualRoot);
            float footpathSpacing = CalculateLocalSpacing(footpathSlabs, visualRoot);

            Bounds firstDrivewayBounds = CalculateWorldBounds(drivewaySlabs[0]);
            Bounds firstPathBounds = CalculateWorldBounds(footpathSlabs[0]);
            Vector3 drivewayAnchorPosition = new Vector3(
                firstDrivewayBounds.center.x,
                firstDrivewayBounds.max.y,
                firstDrivewayBounds.min.z);
            Vector3 footpathAnchorPosition = new Vector3(
                firstPathBounds.center.x,
                firstPathBounds.max.y,
                firstPathBounds.min.z);
            Transform drivewayAnchor = CreateWorldPoint(
                visualRoot,
                "DrivewayAnchor",
                drivewayAnchorPosition);
            Transform footpathAnchor = CreateWorldPoint(
                visualRoot,
                "FootpathAnchor",
                footpathAnchorPosition);

            DisableObjects(drivewaySlabs);
            DisableObjects(footpathSlabs);
            DisableObjects(FindNamedDescendants(modelInstance.transform, "PathLight_"));
            templatesRoot.gameObject.SetActive(false);

            HouseExteriorModuleSet moduleSet = houseRoot.gameObject.AddComponent<HouseExteriorModuleSet>();
            moduleSet.Configure(
                drivewayTemplate,
                footpathTemplate,
                lightTemplate,
                drivewayAnchor,
                footpathAnchor,
                drivewaySize,
                footpathSize,
                lightSize,
                drivewaySpacing,
                footpathSpacing);
            return moduleSet;
        }

        private static float CalculateLocalSpacing(
            IReadOnlyList<Transform> sources,
            Transform reference)
        {
            if (sources.Count < 2)
            {
                return 1f;
            }

            Vector3 first = reference.InverseTransformPoint(CalculateWorldBounds(sources[0]).center);
            Vector3 second = reference.InverseTransformPoint(CalculateWorldBounds(sources[1]).center);
            Vector3 separation = second - first;
            separation.y = 0f;
            return Mathf.Max(0.05f, separation.magnitude);
        }

        private static Transform CreateNormalisedTemplate(
            Transform parent,
            string templateName,
            IReadOnlyList<Transform> sources,
            out Vector3 localSize)
        {
            Bounds sourceBounds = CalculateWorldBounds(sources);
            Transform template = new GameObject(templateName).transform;
            template.SetParent(parent, false);
            template.position = new Vector3(
                sourceBounds.center.x,
                sourceBounds.min.y,
                sourceBounds.center.z);
            template.rotation = parent.rotation;

            for (int index = 0; index < sources.Count; index++)
            {
                GameObject copy = Object.Instantiate(sources[index].gameObject);
                copy.name = sources[index].name;
                copy.transform.SetParent(template, false);
                copy.transform.localPosition = template.InverseTransformPoint(sources[index].position);
                copy.transform.localRotation = Quaternion.Inverse(template.rotation) * sources[index].rotation;
                copy.transform.localScale = DivideScale(
                    sources[index].lossyScale,
                    template.lossyScale);
                copy.SetActive(true);
            }

            localSize = CalculateLocalBounds(template).size;
            return template;
        }

        private static Vector3 DivideScale(Vector3 numerator, Vector3 denominator) => new Vector3(
            Mathf.Abs(denominator.x) > 0.0001f ? numerator.x / denominator.x : numerator.x,
            Mathf.Abs(denominator.y) > 0.0001f ? numerator.y / denominator.y : numerator.y,
            Mathf.Abs(denominator.z) > 0.0001f ? numerator.z / denominator.z : numerator.z);

        private static Bounds CalculateWorldBounds(Transform source) =>
            CalculateWorldBounds(new[] { source });

        private static Bounds CalculateWorldBounds(IReadOnlyList<Transform> sources)
        {
            Bounds bounds = default;
            bool found = false;
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                Renderer[] renderers = sources[sourceIndex].GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    if (!found)
                    {
                        bounds = renderers[rendererIndex].bounds;
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderers[rendererIndex].bounds);
                    }
                }
            }

            return bounds;
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            Bounds bounds = default;
            bool found = false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Bounds rendererBounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 localCorner = rendererBounds.center + Vector3.Scale(
                        rendererBounds.extents,
                        new Vector3(
                            (corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f));
                    Vector3 point = root.InverseTransformPoint(renderer.transform.TransformPoint(localCorner));
                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return bounds;
        }

        private static Transform CreateWorldPoint(Transform parent, string name, Vector3 worldPosition)
        {
            Transform point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.position = worldPosition;
            point.rotation = parent.rotation;
            return point;
        }

        private static List<Transform> FindNamedDescendants(Transform root, string namePrefix)
        {
            List<Transform> results = new List<Transform>();
            CollectNamedDescendants(root, namePrefix, results);
            results.Sort((first, second) => string.CompareOrdinal(first.name, second.name));
            return results;
        }

        private static void CollectNamedDescendants(
            Transform root,
            string namePrefix,
            ICollection<Transform> results)
        {
            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
                if (child.name.StartsWith(namePrefix))
                {
                    results.Add(child);
                }

                CollectNamedDescendants(child, namePrefix, results);
            }
        }

        private static void DisableObjects(IReadOnlyList<Transform> objects)
        {
            for (int index = 0; index < objects.Count; index++)
            {
                objects[index].gameObject.SetActive(false);
            }
        }

        private static bool ShouldCreateStructuralCollider(string objectName)
        {
            return objectName.StartsWith("Structure_") ||
                   objectName == "Foundation_Main" ||
                   objectName == "Porch_Slab" ||
                   objectName.StartsWith("Porch_Step_") ||
                   objectName.StartsWith("Porch_Base_") ||
                   objectName.StartsWith("Porch_Column_");
        }

        private static void AppendHierarchy(Transform current, StringBuilder output, int depth)
        {
            output.Append(' ', depth * 2).Append(current.name)
                .Append(" pos=").Append(current.localPosition)
                .Append(" rot=").Append(current.localEulerAngles)
                .Append(" scale=").Append(current.localScale)
                .AppendLine();
            for (int index = 0; index < current.childCount; index++)
            {
                AppendHierarchy(current.GetChild(index), output, depth + 1);
            }
        }

        private static bool TryGetRendererBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] == null)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderers[index].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }
            }

            return found;
        }

        private static Transform CreatePoint(Transform parent, string name, Vector3 localPosition)
        {
            Transform point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.localPosition = localPosition;
            point.localRotation = Quaternion.identity;
            return point;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static bool PrefabUsesCurrentModel(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return false;
            }

            Transform visualRoot = prefab.transform.Find("VisualRoot");
            if (visualRoot == null || Quaternion.Angle(visualRoot.localRotation, Quaternion.identity) > 0.01f)
            {
                return false;
            }

            HouseExteriorModuleSet moduleSet = prefab.GetComponent<HouseExteriorModuleSet>();
            if (moduleSet == null || !moduleSet.IsConfigured || moduleSet.ConfigurationVersion < 2)
            {
                return false;
            }

            string[] dependencies = AssetDatabase.GetDependencies(prefabPath, true);
            for (int index = 0; index < dependencies.Length; index++)
            {
                if (dependencies[index] == HouseModelPath)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
