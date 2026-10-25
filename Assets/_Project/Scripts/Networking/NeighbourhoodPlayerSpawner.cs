using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using CouchGuys.UI;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

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
        private readonly HashSet<int> m_pendingConnections = new HashSet<int>();
        private readonly HashSet<int> m_spawnedConnections = new HashSet<int>();

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
            m_networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void OnDestroy()
        {
            if (m_networkManager != null && m_networkManager.SceneManager != null)
            {
                m_networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
                m_networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
                m_networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                m_aiTeamSpawned = false;
                m_pendingConnections.Clear();
                m_spawnedConnections.Clear();
            }
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            m_pendingConnections.Remove(connection.ClientId);
            m_spawnedConnections.Remove(connection.ClientId);
        }

        public void SetPlayerPrefab(NetworkObject playerPrefab)
        {
            m_playerPrefab = playerPrefab;
        }

        public void ResetForRetry()
        {
            StopAllCoroutines();
            m_pendingConnections.Clear();
            m_spawnedConnections.Clear();
            m_aiTeamSpawned = false;
        }

        public void SpawnForRetry(NetworkConnection connection) => OnClientLoadedStartScenes(connection, true);

        private void OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer)
            {
                return;
            }

            int clientId = connection.ClientId;
            if (m_spawnedConnections.Contains(clientId) || !m_pendingConnections.Add(clientId)) return;
            StartCoroutine(SpawnPlayerWhenNeighbourhoodReady(connection));
        }

        private IEnumerator SpawnPlayerWhenNeighbourhoodReady(NetworkConnection connection)
        {
            if (m_playerPrefab == null)
            {
                Debug.LogError($"Player prefab is missing; connection {connection.ClientId} cannot spawn.", this);
                m_pendingConnections.Remove(connection.ClientId);
                yield break;
            }

            float sceneTimeoutAt = Time.realtimeSinceStartup + 600f;
            NeighbourhoodGenerator generator = FindGameplayGenerator();
            while (generator == null && connection.IsActive && Time.realtimeSinceStartup < sceneTimeoutAt)
            {
                yield return null;
                generator = FindGameplayGenerator();
            }

            if (generator == null)
            {
                m_pendingConnections.Remove(connection.ClientId);
                if (connection.IsActive)
                    Debug.LogError("The gameplay scene did not provide a NeighbourhoodGenerator before the scene-load timeout; Player spawning was stopped.", this);
                yield break;
            }

            // The initial connection callback can run while the client is still in
            // the lobby. Wait for FishNet's gameplay-scene acknowledgement before
            // spawning owned objects (owners can bypass observer conditions).
            while (connection.IsActive && !connection.Scenes.Contains(generator.gameObject.scene) &&
                   Time.realtimeSinceStartup < sceneTimeoutAt)
                yield return null;
            if (!connection.IsActive || !connection.Scenes.Contains(generator.gameObject.scene))
            {
                m_pendingConnections.Remove(connection.ClientId);
                if (connection.IsActive)
                    Debug.LogError($"Client {connection.ClientId} did not finish loading the gameplay scene.", this);
                yield break;
            }

            NeighbourhoodSeedSynchroniser seedSynchroniser =
                generator.GetComponent<NeighbourhoodSeedSynchroniser>();
            if (seedSynchroniser != null)
            {
                seedSynchroniser.EnsureAuthoritativeGeneration();
            }
            else
            {
                // Give the generator's normal Start method the first opportunity to run.
                yield return null;
                if (!generator.HasGeneratedNeighbourhood && !generator.IsGenerating)
                {
                    if (GameLaunchContext.UseLoadingFlow)
                    {
                        int seed = GameLaunchContext.AuthoritativeSeed != 0
                            ? GameLaunchContext.AuthoritativeSeed
                            : generator.SelectSeed();
                        generator.GenerateAsync(seed);
                    }
                    else
                    {
                        generator.Generate();
                    }
                }
            }

            // Scenes without the optional network synchroniser still generate from the
            // launch-context seed. This is the current Solo scene setup and also serves
            // as a deterministic Steam-lobby fallback.
            float timeoutAt = Time.realtimeSinceStartup + 120f;
            while (!generator.HasGeneratedNeighbourhood && connection.IsActive && Time.realtimeSinceStartup < timeoutAt)
                yield return null;
            if (!generator.HasGeneratedNeighbourhood)
            {
                m_pendingConnections.Remove(connection.ClientId);
                if (connection.IsActive)
                    Debug.LogError("The host neighbourhood did not become ready within 120 seconds; Player spawning was stopped.", this);
                yield break;
            }

            Transform spawnPoint = SelectSpawnPoint(generator.GeneratedStartingArea, connection.ClientId);
            if (spawnPoint == null)
            {
                Debug.LogError("The generated Starting Area has no valid PlayerSpawnPoint; Player spawning was stopped.", this);
                m_pendingConnections.Remove(connection.ClientId);
                yield break;
            }

            NetworkObject player = m_networkManager.GetPooledInstantiated(
                m_playerPrefab,
                spawnPoint.position,
                spawnPoint.rotation,
                true);
            m_networkManager.ServerManager.Spawn(player, connection);
            m_pendingConnections.Remove(connection.ClientId);
            m_spawnedConnections.Add(connection.ClientId);

            if (m_addToDefaultScene)
            {
                m_networkManager.SceneManager.AddOwnerToDefaultScene(player);
            }

            SpawnAiTeammatesServer(player, generator);
        }

        private static NeighbourhoodGenerator FindGameplayGenerator()
        {
            foreach (NeighbourhoodGenerator generator in
                     FindObjectsByType<NeighbourhoodGenerator>(FindObjectsSortMode.None))
            {
                if (generator.gameObject.scene.name == "SampleScene") return generator;
            }
            return null;
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
