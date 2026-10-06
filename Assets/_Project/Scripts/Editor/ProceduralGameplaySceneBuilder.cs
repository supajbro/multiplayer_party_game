using CouchGuys.ProceduralGeneration;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using FishNet.Object;
using Unity.AI.Navigation;
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

            GameObject defaultDeliveryNpcPrefab = DeliveryNPCPrefabBuilder.EnsureDeliveryNpcPrefab();
            GameObject[] defaultHousePrefabs = HousePrefabBuilder.LoadHousePrefabs();
            GameObject[] enemyPrefabs = ThiefPrefabBuilder.EnsureEnemyPrefabs();

            Scene scene = SceneManager.GetSceneByPath(GameplayScenePath);
            bool openedTemporarily = !scene.IsValid() || !scene.isLoaded;
            if (openedTemporarily)
            {
                scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive);
            }

            NeighbourhoodGenerator generator = FindGenerator(scene);
            if (generator == null)
            {
                GameObject root = new GameObject("NeighbourhoodGenerator");
                SceneManager.MoveGameObjectToScene(root, scene);
                generator = root.AddComponent<NeighbourhoodGenerator>();
            }

            if (generator.DeliveryNpcPrefab == null)
            {
                generator.SetDeliveryNpcPrefab(defaultDeliveryNpcPrefab);
            }

            bool assignedDefaultHouses = generator.TrySetDefaultHousePrefabs(defaultHousePrefabs);
            RegionAssetBuilder.EnsureDefaultRegions(generator, defaultHousePrefabs);

            EnemySpawnManager enemySpawnManager = generator.GetComponent<EnemySpawnManager>();
            bool addedEnemySpawnManager = enemySpawnManager == null;
            if (addedEnemySpawnManager)
            {
                enemySpawnManager = generator.gameObject.AddComponent<EnemySpawnManager>();
            }

            enemySpawnManager.SetGenerator(generator);
            NetworkObject[] enemyNetworkPrefabs = new NetworkObject[enemyPrefabs.Length];
            for (int index = 0; index < enemyPrefabs.Length; index++)
            {
                enemyNetworkPrefabs[index] = enemyPrefabs[index] != null
                    ? enemyPrefabs[index].GetComponent<NetworkObject>()
                    : null;
            }

            enemySpawnManager.SetEnemyPrefabs(enemyNetworkPrefabs);

            if (IsConfigured(scene))
            {
                if (assignedDefaultHouses || addedEnemySpawnManager)
                {
                    generator.Generate(12345);
                    EditorUtility.SetDirty(generator);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    AssetDatabase.SaveAssets();
                }

                if (openedTemporarily)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                return;
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

            DeliveryManager deliveryManager = generator.GetComponent<DeliveryManager>();
            if (deliveryManager == null)
            {
                deliveryManager = generator.gameObject.AddComponent<DeliveryManager>();
            }

            NavMeshSurface navMeshSurface = generator.GetComponent<NavMeshSurface>();
            if (navMeshSurface == null)
            {
                navMeshSurface = generator.gameObject.AddComponent<NavMeshSurface>();
            }

            navMeshSurface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            SerializedObject serialisedGenerator = new SerializedObject(generator);
            serialisedGenerator.FindProperty("m_navMeshSurface").objectReferenceValue = navMeshSurface;
            serialisedGenerator.ApplyModifiedPropertiesWithoutUndo();

            GameObject couchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Couch.prefab");
            deliveryManager.SetCouchPrefab(
                couchPrefab != null ? couchPrefab.GetComponent<NetworkObject>() : null);

            ConfigureNetworkBehaviours(networkObject, synchroniser, deliveryManager);
            networkObject.SetSceneId(NeighbourhoodSceneId);
            EditorUtility.SetDirty(networkObject);
            EditorUtility.SetDirty(synchroniser);
            EditorUtility.SetDirty(deliveryManager);
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

        private static void ConfigureNetworkBehaviours(
            NetworkObject networkObject,
            NeighbourhoodSeedSynchroniser synchroniser,
            DeliveryManager deliveryManager)
        {
            SerializedObject serialisedNetworkObject = new SerializedObject(networkObject);
            SerializedProperty behaviours = serialisedNetworkObject.FindProperty("NetworkBehaviours");
            if (behaviours == null)
            {
                throw new UnityException("FishNet NetworkObject behaviour list was not found.");
            }

            behaviours.arraySize = 2;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue = synchroniser;
            behaviours.GetArrayElementAtIndex(1).objectReferenceValue = deliveryManager;
            serialisedNetworkObject.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serialisedSynchroniser = new SerializedObject(synchroniser);
            serialisedSynchroniser.FindProperty("m_generator").objectReferenceValue =
                synchroniser.GetComponent<NeighbourhoodGenerator>();
            serialisedSynchroniser.FindProperty("_componentIndexCache").intValue = 0;
            serialisedSynchroniser.FindProperty("_addedNetworkObject").objectReferenceValue = networkObject;
            serialisedSynchroniser.FindProperty("_networkObjectCache").objectReferenceValue = networkObject;
            serialisedSynchroniser.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serialisedDeliveryManager = new SerializedObject(deliveryManager);
            serialisedDeliveryManager.FindProperty("m_generator").objectReferenceValue =
                deliveryManager.GetComponent<NeighbourhoodGenerator>();
            serialisedDeliveryManager.FindProperty("_componentIndexCache").intValue = 1;
            serialisedDeliveryManager.FindProperty("_addedNetworkObject").objectReferenceValue = networkObject;
            serialisedDeliveryManager.FindProperty("_networkObjectCache").objectReferenceValue = networkObject;
            serialisedDeliveryManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void VerifyScene(Scene scene)
        {
            NeighbourhoodGenerator generator = FindGenerator(scene);
            NeighbourhoodSeedSynchroniser synchroniser =
                generator != null ? generator.GetComponent<NeighbourhoodSeedSynchroniser>() : null;
            DeliveryManager deliveryManager =
                generator != null ? generator.GetComponent<DeliveryManager>() : null;
            EnemySpawnManager enemySpawnManager =
                generator != null ? generator.GetComponent<EnemySpawnManager>() : null;
            NavMeshSurface navMeshSurface =
                generator != null ? generator.GetComponent<NavMeshSurface>() : null;
            NetworkObject networkObject = generator != null ? generator.GetComponent<NetworkObject>() : null;
            if (generator == null || synchroniser == null || deliveryManager == null || enemySpawnManager == null ||
                navMeshSurface == null ||
                networkObject == null ||
                networkObject.NetworkBehaviours.Count != 2 ||
                networkObject.NetworkBehaviours[0] != synchroniser ||
                networkObject.NetworkBehaviours[1] != deliveryManager ||
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
            DeliveryManager deliveryManager = generator.GetComponent<DeliveryManager>();
            EnemySpawnManager enemySpawnManager = generator.GetComponent<EnemySpawnManager>();
            NavMeshSurface navMeshSurface = generator.GetComponent<NavMeshSurface>();
            return networkObject != null && synchroniser != null && deliveryManager != null &&
                   enemySpawnManager != null &&
                   navMeshSurface != null &&
                   generator.DeliveryNpcPrefab != null &&
                   networkObject.NetworkBehaviours.Count == 2 &&
                   networkObject.NetworkBehaviours[0] == synchroniser &&
                   networkObject.NetworkBehaviours[1] == deliveryManager &&
                   generator.HasGeneratedNeighbourhood;
        }
    }
}
