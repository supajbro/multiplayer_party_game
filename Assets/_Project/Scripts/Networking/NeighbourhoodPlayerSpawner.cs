using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.AI;

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

        [Header("AI Teammates")]
        [SerializeField, Range(0, 3)] private int m_aiTeammateCount = 3;
        [SerializeField, Min(1f)] private float m_aiSpawnRadius = 3f;
        [SerializeField, Min(0.5f)] private float m_aiNavMeshSampleRadius = 4f;

        private NetworkManager m_networkManager;
        private bool m_aiTeamSpawned;

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
            m_networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }

        private void OnDestroy()
        {
            if (m_networkManager != null && m_networkManager.SceneManager != null)
            {
                m_networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
                m_networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            }
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                m_aiTeamSpawned = false;
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

            SpawnAiTeammatesServer(player, generator);
        }

        private void SpawnAiTeammatesServer(
            NetworkObject leaderObject,
            NeighbourhoodGenerator generator)
        {
            if (m_aiTeamSpawned || m_aiTeammateCount <= 0 || leaderObject == null ||
                generator == null || !generator.IsGenerationReady || !generator.IsNavMeshReady ||
                !leaderObject.TryGetComponent(out PlayerHealth leaderHealth) ||
                !leaderObject.TryGetComponent(out PlayerCouchCarrier leaderCarrier))
            {
                return;
            }

            // Set this before spawning so connection callbacks cannot create a second team.
            m_aiTeamSpawned = true;
            int count = Mathf.Clamp(m_aiTeammateCount, 0, 3);
            for (int index = 0; index < count; index++)
            {
                if (!TryFindAiSpawnPosition(leaderObject.transform, index, count, out Vector3 position))
                {
                    Debug.LogError($"No NavMesh position was found for AI teammate {index + 1}.", this);
                    continue;
                }

                NetworkObject bot = Instantiate(
                    m_playerPrefab,
                    position,
                    leaderObject.transform.rotation);
                if (!bot.TryGetComponent(out DebugCouchBotController controller))
                {
                    Debug.LogError("The Player prefab is missing DebugCouchBotController.", bot);
                    Destroy(bot.gameObject);
                    continue;
                }

                controller.Initialise(leaderCarrier.DebugBotSettings, index, leaderHealth);
                m_networkManager.ServerManager.Spawn(bot);
            }
        }

        private bool TryFindAiSpawnPosition(
            Transform leader,
            int index,
            int count,
            out Vector3 position)
        {
            float angleStep = 360f / Mathf.Max(1, count);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = angleStep * index + attempt * 45f;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) *
                                 (Vector3.back * m_aiSpawnRadius);
                Vector3 candidate = leader.position + offset;
                if (NavMesh.SamplePosition(
                        candidate,
                        out NavMeshHit hit,
                        m_aiNavMeshSampleRadius,
                        NavMesh.AllAreas))
                {
                    position = hit.position;
                    return true;
                }
            }

            if (NavMesh.SamplePosition(
                    leader.position,
                    out NavMeshHit fallback,
                    m_aiNavMeshSampleRadius * 2f,
                    NavMesh.AllAreas))
            {
                position = fallback.position;
                return true;
            }

            position = default;
            return false;
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
