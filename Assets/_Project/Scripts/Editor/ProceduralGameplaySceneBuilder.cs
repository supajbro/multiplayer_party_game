using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    /// <summary>Creates and verifies the networked neighbourhood object in the gameplay scene.</summary>
    public static class ProceduralGameplaySceneBuilder
    {
        private const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";
        private const ulong NeighbourhoodSceneId = 0x4347555953454544UL;

        [InitializeOnLoadMethod]
        private static void BuildOpenGameplaySceneAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    if (SceneManager.GetSceneAt(index).isDirty)
                    {
                        return;
                    }
                }

                BuildProceduralGameplayScene();
            };
        }

        [MenuItem("Couch Guys/Build Procedural Gameplay Scene")]
        public static void BuildProceduralGameplayScene()
        {
            GameObject bootstrap = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/SteamMultiplayerBootstrap.prefab");
            if (bootstrap == null || bootstrap.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>() == null)
            {
                SteamMultiplayerPrefabBuilder.BuildSteamMultiplayerAssets();
            }

            Scene scene = SceneManager.GetSceneByPath(GameplayScenePath);
            bool openedTemporarily = !scene.IsValid() || !scene.isLoaded;
            if (openedTemporarily)
            {
                scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive);
            }

            if (IsConfigured(scene))
            {
                if (openedTemporarily)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                return;
            }

            NeighbourhoodGenerator generator = FindGenerator(scene);
            if (generator == null)
            {
                GameObject root = new GameObject("NeighbourhoodGenerator");
                SceneManager.MoveGameObjectToScene(root, scene);
                generator = root.AddComponent<NeighbourhoodGenerator>();
            }

            NeighbourhoodSeedSynchroniser synchroniser =
                generator.GetComponent<NeighbourhoodSeedSynchroniser>();
            if (synchroniser == null)
            {
                synchroniser = generator.gameObject.AddComponent<NeighbourhoodSeedSynchroniser>();
            }

            NetworkObject networkObject = generator.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = generator.gameObject.AddComponent<NetworkObject>();
            }

            ConfigureNetworkBehaviour(networkObject, synchroniser);
            networkObject.SetSceneId(NeighbourhoodSceneId);
            EditorUtility.SetDirty(networkObject);
            EditorUtility.SetDirty(synchroniser);
            EditorUtility.SetDirty(generator);

            // Store a visible seed-12345 greybox in the scene. At runtime the server's
            // synchronised seed clears and replaces this preview before Players spawn.
            generator.Generate(12345);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            VerifyScene(scene);
            if (!openedTemporarily)
            {
                Selection.activeGameObject = generator.gameObject;
            }

            Debug.Log("Couch Guys procedural gameplay scene built successfully.", generator);
            if (openedTemporarily)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static NeighbourhoodGenerator FindGenerator(Scene scene)
        {
            NeighbourhoodGenerator result = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                NeighbourhoodGenerator[] generators =
                    roots[rootIndex].GetComponentsInChildren<NeighbourhoodGenerator>(true);
                for (int index = 0; index < generators.Length; index++)
                {
                    if (result != null && result != generators[index])
                    {
                        throw new UnityException("The gameplay scene contains multiple NeighbourhoodGenerator components.");
                    }

                    result = generators[index];
                }
            }

            return result;
        }

        private static void ConfigureNetworkBehaviour(
            NetworkObject networkObject,
            NeighbourhoodSeedSynchroniser synchroniser)
        {
            SerializedObject serialisedNetworkObject = new SerializedObject(networkObject);
            SerializedProperty behaviours = serialisedNetworkObject.FindProperty("NetworkBehaviours");
            if (behaviours == null)
            {
                throw new UnityException("FishNet NetworkObject behaviour list was not found.");
            }

            behaviours.arraySize = 1;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue = synchroniser;
            serialisedNetworkObject.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serialisedSynchroniser = new SerializedObject(synchroniser);
            serialisedSynchroniser.FindProperty("m_generator").objectReferenceValue =
                synchroniser.GetComponent<NeighbourhoodGenerator>();
            serialisedSynchroniser.FindProperty("_componentIndexCache").intValue = 0;
            serialisedSynchroniser.FindProperty("_addedNetworkObject").objectReferenceValue = networkObject;
            serialisedSynchroniser.FindProperty("_networkObjectCache").objectReferenceValue = networkObject;
            serialisedSynchroniser.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void VerifyScene(Scene scene)
        {
            NeighbourhoodGenerator generator = FindGenerator(scene);
            NeighbourhoodSeedSynchroniser synchroniser =
                generator != null ? generator.GetComponent<NeighbourhoodSeedSynchroniser>() : null;
            NetworkObject networkObject = generator != null ? generator.GetComponent<NetworkObject>() : null;
            if (generator == null || synchroniser == null || networkObject == null ||
                networkObject.NetworkBehaviours.Count != 1 ||
                networkObject.NetworkBehaviours[0] != synchroniser ||
                !generator.HasGeneratedNeighbourhood ||
                generator.GeneratedStartingArea.PlayerSpawnPoints.Length < 4)
            {
                throw new UnityException("Procedural gameplay scene verification failed.");
            }
        }

        private static bool IsConfigured(Scene scene)
        {
            NeighbourhoodGenerator generator = FindGenerator(scene);
            if (generator == null)
            {
                return false;
            }

            NetworkObject networkObject = generator.GetComponent<NetworkObject>();
            NeighbourhoodSeedSynchroniser synchroniser =
                generator.GetComponent<NeighbourhoodSeedSynchroniser>();
            return networkObject != null && synchroniser != null &&
                   networkObject.NetworkBehaviours.Count == 1 &&
                   networkObject.NetworkBehaviours[0] == synchroniser &&
                   generator.HasGeneratedNeighbourhood;
        }
    }
}
