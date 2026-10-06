using CouchGuys.Networking;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishySteamworks;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace CouchGuys.Editor
{
    /// <summary>
    /// Builds the generated assets which connect Steam, FishNet, and the existing Player prefab.
    /// </summary>
    public static class SteamMultiplayerPrefabBuilder
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string CouchPrefabPath = "Assets/_Project/Prefabs/Couch.prefab";
        private const string SpawnablePrefabsPath = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";
        private const string BootstrapPrefabPath = "Assets/_Project/Resources/SteamMultiplayerBootstrap.prefab";

        [InitializeOnLoadMethod]
        private static void BuildMissingAssetsAfterImport()
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject bootstrapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootstrapPrefabPath);
            if (playerPrefab == null ||
                playerPrefab.GetComponent<NetworkObject>() == null ||
                playerPrefab.GetComponent<NetworkObject>().NetworkBehaviours.Count == 0 ||
                playerPrefab.GetComponent<NeighbourhoodMap>() == null ||
                playerPrefab.GetComponent<PlayerHealth>() == null ||
                bootstrapPrefab == null ||
                bootstrapPrefab.GetComponent<NeighbourhoodPlayerSpawner>() == null)
            {
                EditorApplication.delayCall += BuildSteamMultiplayerAssets;
            }
        }

        [MenuItem("Couch Guys/Build Steam Multiplayer Assets")]
        public static void BuildSteamMultiplayerAssets()
        {
            EnsureFolders();
            UpgradePlayerPrefab();

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            NetworkObject playerNetworkObject = playerPrefab.GetComponent<NetworkObject>();
            GameObject couchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CouchPrefabPath);
            NetworkObject couchNetworkObject = couchPrefab != null ? couchPrefab.GetComponent<NetworkObject>() : null;
            SinglePrefabObjects spawnablePrefabs = BuildSpawnablePrefabs(playerNetworkObject, couchNetworkObject);
            BuildBootstrapPrefab(playerNetworkObject, spawnablePrefabs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            VerifyAssets();
            Debug.Log("Couch Guys Steam multiplayer assets built successfully.");
        }

        private static void UpgradePlayerPrefab()
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                PlayerPrefabBuilder.BuildPlayerPrefab();
                return;
            }

            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                PlayerPrefabBuilder.EnsureNetworkConfiguration(playerRoot);
                PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(playerRoot);
            }
        }

        private static SinglePrefabObjects BuildSpawnablePrefabs(NetworkObject playerNetworkObject, NetworkObject couchNetworkObject)
        {
            SinglePrefabObjects spawnablePrefabs = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablePrefabsPath);
            if (spawnablePrefabs == null)
            {
                spawnablePrefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                AssetDatabase.CreateAsset(spawnablePrefabs, SpawnablePrefabsPath);
            }

            List<NetworkObject> preservedPrefabs = new List<NetworkObject>();
            for (int index = 0; index < spawnablePrefabs.Prefabs.Count; index++)
            {
                NetworkObject existing = spawnablePrefabs.Prefabs[index];
                string existingPath = existing != null ? AssetDatabase.GetAssetPath(existing) : string.Empty;
                if (existing != null && existing != playerNetworkObject && existing != couchNetworkObject &&
                    existingPath != "Assets/_Project/Prefabs/Enemies/Thief.prefab")
                {
                    preservedPrefabs.Add(existing);
                }
            }

            spawnablePrefabs.Clear();
            spawnablePrefabs.AddObject(playerNetworkObject, false, false);
            if (couchNetworkObject != null)
            {
                spawnablePrefabs.AddObject(couchNetworkObject, false, false);
            }
            spawnablePrefabs.AddObjects(preservedPrefabs, true, false);
            EditorUtility.SetDirty(spawnablePrefabs);
            return spawnablePrefabs;
        }

        private static void BuildBootstrapPrefab(NetworkObject playerNetworkObject, SinglePrefabObjects spawnablePrefabs)
        {
            GameObject root = new GameObject("SteamMultiplayerBootstrap");
            try
            {
                NetworkManager networkManager = root.AddComponent<NetworkManager>();
                TransportManager transportManager = root.AddComponent<TransportManager>();
                FishySteamworks.FishySteamworks transport = root.AddComponent<FishySteamworks.FishySteamworks>();
                NeighbourhoodPlayerSpawner playerSpawner = root.AddComponent<NeighbourhoodPlayerSpawner>();
                SteamClientBootstrap steamBootstrap = root.AddComponent<SteamClientBootstrap>();
                SteamLobbyController lobbyController = root.AddComponent<SteamLobbyController>();
                SteamLobbyDebugInterface debugInterface = root.AddComponent<SteamLobbyDebugInterface>();

                networkManager.SpawnablePrefabs = spawnablePrefabs;
                transportManager.Transport = transport;
                playerSpawner.SetPlayerPrefab(playerNetworkObject);

                SerializedObject serialisedTransport = new SerializedObject(transport);
                serialisedTransport.FindProperty("_peerToPeer").boolValue = true;
                serialisedTransport.FindProperty("_maximumClients").intValue = 4;
                serialisedTransport.ApplyModifiedPropertiesWithoutUndo();

                SetObjectReference(lobbyController, "m_networkManager", networkManager);
                SetObjectReference(lobbyController, "m_transport", transport);
                SetObjectReference(lobbyController, "m_steamBootstrap", steamBootstrap);
                SetObjectReference(debugInterface, "m_lobbyController", lobbyController);

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, BootstrapPrefabPath);
                if (savedPrefab == null)
                {
                    throw new UnityException($"Failed to save multiplayer bootstrap at {BootstrapPrefabPath}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serialisedObject = new SerializedObject(target);
            SerializedProperty property = serialisedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new UnityException($"Missing serialised property {propertyName} on {target.GetType().Name}.");
            }

            property.objectReferenceValue = value;
            serialisedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolders()
        {
            CreateFolderIfMissing("Assets/_Project", "Settings");
            CreateFolderIfMissing("Assets/_Project", "Resources");
        }

        private static void CreateFolderIfMissing(string parent, string folderName)
        {
            string path = $"{parent}/{folderName}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        private static void VerifyAssets()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject bootstrap = AssetDatabase.LoadAssetAtPath<GameObject>(BootstrapPrefabPath);
            SinglePrefabObjects spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablePrefabsPath);
            GameObject couch = AssetDatabase.LoadAssetAtPath<GameObject>(CouchPrefabPath);
            int minimumSpawnableCount = couch == null ? 1 : 2;

            if (player == null ||
                player.GetComponent<NetworkObject>() == null ||
                player.GetComponent<NetworkPlayerOwnership>() == null ||
                bootstrap == null ||
                bootstrap.GetComponent<NetworkManager>() == null ||
                bootstrap.GetComponent<SteamLobbyController>() == null ||
                bootstrap.GetComponent<NeighbourhoodPlayerSpawner>()?.PlayerPrefab == null ||
                spawnables == null ||
                spawnables.GetObjectCount() < minimumSpawnableCount)
            {
                throw new UnityException("Steam multiplayer asset verification failed.");
            }
        }
    }
}
