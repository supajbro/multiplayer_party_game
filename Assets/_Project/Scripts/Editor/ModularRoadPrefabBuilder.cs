using System;
using System.Collections.Generic;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.EditorTools
{
    /// <summary>
    /// Extracts the five canonical road hierarchies from the combined Blender FBX,
    /// adds the connection metadata required by NeighbourhoodGenerator, and assigns
    /// the resulting prefabs to the Suburbs region.
    /// </summary>
    public static class ModularRoadPrefabBuilder
    {
        private const string ModelPath =
            "Assets/_Project/Models/Roads/ModularRoadKit.fbx";
        private const string PrefabFolder =
            "Assets/_Project/Prefabs/Roads";
        private const string SuburbsRegionPath =
            "Assets/_Project/Settings/Regions/Region_Suburbs.asset";

        private readonly struct RoadDefinition
        {
            public RoadDefinition(string objectName, RoadConnections connections)
            {
                ObjectName = objectName;
                Connections = connections;
            }

            public string ObjectName { get; }
            public RoadConnections Connections { get; }
            public string PrefabPath => $"{PrefabFolder}/{ObjectName}.prefab";
        }

        private static readonly RoadDefinition[] Definitions =
        {
            new RoadDefinition(
                "Road_Straight",
                RoadConnections.North | RoadConnections.South),
            new RoadDefinition(
                "Road_Corner",
                RoadConnections.North | RoadConnections.East),
            new RoadDefinition(
                "Road_T",
                RoadConnections.North | RoadConnections.East | RoadConnections.West),
            new RoadDefinition(
                "Road_Cross",
                RoadConnections.North | RoadConnections.East |
                RoadConnections.South | RoadConnections.West),
            new RoadDefinition("Road_End", RoadConnections.North)
        };

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabsAfterReload()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && RequiresBuild())
                {
                    BuildPrefabs(false);
                }
            };
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (RequiresBuild())
                {
                    BuildPrefabs(false);
                }
            };
        }

        [MenuItem("Couch Guys/Roads/Rebuild Modular Road Prefabs")]
        public static void RebuildPrefabs()
        {
            BuildPrefabs(true);
        }

        [MenuItem("Couch Guys/Roads/Rebuild Modular Road Prefabs", true)]
        private static bool CanRebuildPrefabs() =>
            AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) != null &&
            !EditorApplication.isPlayingOrWillChangePlaymode;

        internal static void QueueRebuildAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    BuildPrefabs(false);
                }
            };
        }

        private static bool RequiresBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                return false;
            }

            for (int index = 0; index < Definitions.Length; index++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    Definitions[index].PrefabPath);
                if (prefab == null || prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
                {
                    return true;
                }
            }

            RegionDefinition region =
                AssetDatabase.LoadAssetAtPath<RegionDefinition>(SuburbsRegionPath);
            if (region == null)
            {
                return false;
            }

            GameObject[] assigned = region.RoadPrefabs;
            if (assigned == null || assigned.Length != Definitions.Length)
            {
                return true;
            }

            HashSet<GameObject> expected = new HashSet<GameObject>();
            for (int index = 0; index < Definitions.Length; index++)
            {
                expected.Add(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Definitions[index].PrefabPath));
            }

            for (int index = 0; index < assigned.Length; index++)
            {
                if (assigned[index] == null || !expected.Remove(assigned[index]))
                {
                    return true;
                }
            }

            return expected.Count != 0;
        }

        private static void BuildPrefabs(bool showDialog)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                ReportFailure(
                    $"No combined road model was found at '{ModelPath}'.",
                    showDialog);
                return;
            }

            GameObject validationInstance =
                PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (validationInstance == null)
            {
                ReportFailure("Unity could not instantiate the modular road FBX.", showDialog);
                return;
            }

            try
            {
                for (int index = 0; index < Definitions.Length; index++)
                {
                    if (FindDescendant(
                            validationInstance.transform,
                            Definitions[index].ObjectName) == null)
                    {
                        ReportFailure(
                            $"The FBX is missing a root named " +
                            $"'{Definitions[index].ObjectName}'. No prefabs were changed.",
                            showDialog);
                        return;
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(validationInstance);
            }

            EnsureAssetFolder(PrefabFolder);
            List<GameObject> generatedPrefabs = new List<GameObject>(Definitions.Length);
            try
            {
                for (int index = 0; index < Definitions.Length; index++)
                {
                    generatedPrefabs.Add(BuildPrefab(model, Definitions[index]));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ReportFailure(
                    "Road prefab generation failed. See the Console for details.",
                    showDialog);
                return;
            }
            finally
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            AssignToSuburbs(generatedPrefabs);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Built {generatedPrefabs.Count} modular road prefabs from " +
                $"'{ModelPath}' and assigned them to Region_Suburbs.");

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Modular Roads",
                    "Created Straight, Corner, T, Cross, and End road prefabs and " +
                    "assigned them to the Suburbs region.",
                    "OK");
            }
        }

        private static GameObject BuildPrefab(
            GameObject model,
            RoadDefinition definition)
        {
            GameObject modelInstance =
                PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (modelInstance == null)
            {
                throw new InvalidOperationException("Could not instantiate the road model.");
            }

            GameObject prefabRoot = null;
            try
            {
                Transform roadModel =
                    FindDescendant(modelInstance.transform, definition.ObjectName);
                if (roadModel == null)
                {
                    throw new InvalidOperationException(
                        $"The model does not contain '{definition.ObjectName}'.");
                }

                prefabRoot = new GameObject(definition.ObjectName);
                CloneVisualHierarchy(roadModel, prefabRoot.transform, true);

                RoadTile roadTile = prefabRoot.AddComponent<RoadTile>();
                SerializedObject roadTileData = new SerializedObject(roadTile);
                roadTileData.FindProperty("m_connections").intValue =
                    (int)definition.Connections;
                roadTileData.ApplyModifiedPropertiesWithoutUndo();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    definition.PrefabPath);
                if (prefab == null)
                {
                    AssetDatabase.ImportAsset(
                        definition.PrefabPath,
                        ImportAssetOptions.ForceSynchronousImport);
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        definition.PrefabPath);
                }

                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Unity could not save '{definition.PrefabPath}'.");
                }

                if (prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
                {
                    throw new InvalidOperationException(
                        $"'{definition.ObjectName}' produced a prefab with no renderers.");
                }

                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(modelInstance);
                if (prefabRoot != null)
                {
                    Object.DestroyImmediate(prefabRoot);
                }
            }
        }

        private static void CloneVisualHierarchy(
            Transform source,
            Transform parent,
            bool isTileRoot)
        {
            GameObject clone = new GameObject(isTileRoot ? "Visual" : source.name);
            Transform cloneTransform = clone.transform;
            cloneTransform.SetParent(parent, false);
            cloneTransform.localPosition = isTileRoot ? Vector3.zero : source.localPosition;
            cloneTransform.localRotation = isTileRoot ? source.rotation : source.localRotation;
            cloneTransform.localScale = isTileRoot ? source.lossyScale : source.localScale;

            if (source.TryGetComponent(out MeshFilter sourceFilter))
            {
                MeshFilter targetFilter = clone.AddComponent<MeshFilter>();
                targetFilter.sharedMesh = sourceFilter.sharedMesh;
            }

            if (source.TryGetComponent(out MeshRenderer sourceRenderer))
            {
                MeshRenderer targetRenderer = clone.AddComponent<MeshRenderer>();
                EditorUtility.CopySerialized(sourceRenderer, targetRenderer);
            }

            if (source.TryGetComponent(out MeshCollider sourceCollider))
            {
                MeshCollider targetCollider = clone.AddComponent<MeshCollider>();
                EditorUtility.CopySerialized(sourceCollider, targetCollider);
            }

            for (int index = 0; index < source.childCount; index++)
            {
                CloneVisualHierarchy(source.GetChild(index), cloneTransform, false);
            }
        }

        private static void AssignToSuburbs(IReadOnlyList<GameObject> prefabs)
        {
            RegionDefinition region =
                AssetDatabase.LoadAssetAtPath<RegionDefinition>(SuburbsRegionPath);
            if (region == null)
            {
                throw new InvalidOperationException(
                    $"No Suburbs region asset was found at '{SuburbsRegionPath}'.");
            }

            SerializedObject regionData = new SerializedObject(region);
            SerializedProperty roadPrefabs = regionData.FindProperty("m_roadPrefabs");
            roadPrefabs.arraySize = prefabs.Count;
            for (int index = 0; index < prefabs.Count; index++)
            {
                roadPrefabs.GetArrayElementAtIndex(index).objectReferenceValue = prefabs[index];
            }

            regionData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(region);
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (string.Equals(root.name, objectName, StringComparison.Ordinal))
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform match = FindDescendant(root.GetChild(index), objectName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] pieces = folderPath.Split('/');
            string current = pieces[0];
            for (int index = 1; index < pieces.Length; index++)
            {
                string next = $"{current}/{pieces[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, pieces[index]);
                }

                current = next;
            }
        }

        private static void ReportFailure(string message, bool showDialog)
        {
            Debug.LogError(message);
            if (showDialog)
            {
                EditorUtility.DisplayDialog("Modular Roads", message, "OK");
            }
        }
    }

    internal sealed class ModularRoadModelPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            for (int index = 0; index < importedAssets.Length; index++)
            {
                if (string.Equals(
                        importedAssets[index],
                        "Assets/_Project/Models/Roads/ModularRoadKit.fbx",
                        StringComparison.OrdinalIgnoreCase))
                {
                    ModularRoadPrefabBuilder.QueueRebuildAfterImport();
                    return;
                }
            }
        }
    }
}
