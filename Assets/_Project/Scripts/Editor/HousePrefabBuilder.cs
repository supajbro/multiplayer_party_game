using System.Text;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Builds non-destructive wrapper prefabs around the imported house FBX assets.</summary>
    public static class HousePrefabBuilder
    {
        public const string House01ModelPath = "Assets/_Project/Models/Houses/house_01.fbx";
        public const string House02ModelPath = "Assets/_Project/Models/Houses/house_02.fbx";
        public const string House01PrefabPath = "Assets/_Project/Prefabs/Houses/House_01.prefab";
        public const string House02PrefabPath = "Assets/_Project/Prefabs/Houses/House_02.prefab";

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabsAfterImport()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(House01PrefabPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(House02PrefabPath) == null)
            {
                EditorApplication.delayCall += EnsureHousePrefabs;
            }
        }

        [MenuItem("Couch Guys/Build House Prefabs")]
        public static void EnsureHousePrefabs()
        {
            EnsureFolder("Assets/_Project/Prefabs", "Houses");
            BuildWrapper(House01ModelPath, House01PrefabPath, "House_01");
            BuildWrapper(House02ModelPath, House02PrefabPath, "House_02");
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

                Transform roadConnection = CreatePoint(root.transform, "RoadConnection", new Vector3(0f, 0f, 5f));
                Transform deliveryPoint = CreatePoint(root.transform, "DeliveryPoint", new Vector3(0f, 0f, 5.5f));
                GeneratedProperty property = root.AddComponent<GeneratedProperty>();
                property.ConfigurePrefab(
                    Vector2Int.one,
                    roadConnection,
                    deliveryPoint,
                    visualRoot,
                    true);

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

            visualRoot.localRotation = Quaternion.Euler(-90f, 0f, 0f);
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
    }
}
