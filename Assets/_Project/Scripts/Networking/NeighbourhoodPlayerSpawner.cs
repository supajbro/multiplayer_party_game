using CouchGuys.ProceduralGeneration;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Networking
{
    /// <summary>
    /// Spawns each connected Player only after the authoritative neighbourhood exists,
    /// using the generated Starting Area spawn markers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodPlayerSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject m_playerPrefab;
        [SerializeField] private bool m_addToDefaultScene = true;

        private NetworkManager m_networkManager;

        public NetworkObject PlayerPrefab => m_playerPrefab;

        private void Awake()
        {
            m_networkManager = GetComponentInParent<NetworkManager>();
            m_networkManager ??= InstanceFinder.NetworkManager;
            if (m_networkManager == null)
            {
                Debug.LogError("Neighbourhood Player Spawner could not find the FishNet NetworkManager.", this);
                return;
            }

            m_networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
        }

        private void OnDestroy()
        {
            if (m_networkManager != null && m_networkManager.SceneManager != null)
            {
                m_networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
            }
        }

        public void SetPlayerPrefab(NetworkObject playerPrefab)
        {
            m_playerPrefab = playerPrefab;
        }

        private void OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer)
            {
                return;
            }

            if (m_playerPrefab == null)
            {
                Debug.LogError($"Player prefab is missing; connection {connection.ClientId} cannot spawn.", this);
                return;
            }

            NeighbourhoodGenerator generator = FindFirstObjectByType<NeighbourhoodGenerator>();
            if (generator == null)
            {
                Debug.LogError("The gameplay scene has no NeighbourhoodGenerator; Player spawning was stopped.", this);
                return;
            }

            NeighbourhoodSeedSynchroniser seedSynchroniser =
                generator.GetComponent<NeighbourhoodSeedSynchroniser>();
            if (seedSynchroniser == null || !seedSynchroniser.EnsureAuthoritativeGeneration())
            {
                Debug.LogError("The host neighbourhood was not ready; Player spawning was stopped.", this);
                return;
            }

            Transform spawnPoint = SelectSpawnPoint(generator.GeneratedStartingArea, connection.ClientId);
            if (spawnPoint == null)
            {
                Debug.LogError("The generated Starting Area has no valid PlayerSpawnPoint; Player spawning was stopped.", this);
                return;
            }

            NetworkObject player = m_networkManager.GetPooledInstantiated(
                m_playerPrefab,
                spawnPoint.position,
                spawnPoint.rotation,
                true);
            m_networkManager.ServerManager.Spawn(player, connection);

            if (m_addToDefaultScene)
            {
                m_networkManager.SceneManager.AddOwnerToDefaultScene(player);
            }
        }

        /// <summary>Returns the stable depot spawn assigned to a network client.</summary>
        public static Transform SelectSpawnPoint(StartingArea startingArea, int clientId)
        {
            if (startingArea == null || startingArea.PlayerSpawnPoints == null ||
                startingArea.PlayerSpawnPoints.Length == 0)
            {
                return null;
            }

            Transform[] spawnPoints = startingArea.PlayerSpawnPoints;
            int firstIndex = Mathf.Abs(clientId) % spawnPoints.Length;
            for (int offset = 0; offset < spawnPoints.Length; offset++)
            {
                Transform spawnPoint = spawnPoints[(firstIndex + offset) % spawnPoints.Length];
                if (spawnPoint != null)
                {
                    return spawnPoint;
                }
            }

            return null;
        }
    }
}
