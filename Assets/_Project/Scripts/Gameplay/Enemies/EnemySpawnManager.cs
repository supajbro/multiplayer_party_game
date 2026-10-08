using System.Collections.Generic;
using CouchGuys.Networking;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>
    /// Server-authoritative, region-independent group spawner for weapon-equipped enemy prefabs.
    /// Individual enemy components remain responsible for targeting, combat, and navigation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawnManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private NetworkObject[] m_enemyPrefabs;

        [Header("Spawn Timing")]
        [SerializeField, Min(0.1f)] private float m_minSpawnInterval = 20f;
        [SerializeField, Min(0.1f)] private float m_maxSpawnInterval = 45f;

        [Header("Spawn Distance")]
        [SerializeField, Min(1f)] private float m_minSpawnDistance = 30f;
        [SerializeField, Min(1f)] private float m_maxSpawnDistance = 80f;
        [SerializeField, Min(0.1f)] private float m_navMeshSampleRadius = 8f;
        [SerializeField, Min(1)] private int m_spawnPositionAttempts = 12;

        [Header("Groups")]
        [SerializeField, Min(1)] private int m_minGroupSize = 2;
        [SerializeField, Min(1)] private int m_maxGroupSize = 5;
        [SerializeField, Min(0.5f)] private float m_groupSpread = 4f;
        [SerializeField, Min(1)] private int m_memberPositionAttempts = 6;
        [SerializeField, Min(1)] private int m_maxActiveThieves = 15;

        [Header("Debug")]
        [SerializeField] private bool m_drawSpawnGizmos;

        private readonly List<NetworkPlayerOwnership> m_players = new List<NetworkPlayerOwnership>(4);
        private readonly List<NetworkObject> m_activeThieves = new List<NetworkObject>(16);
        private readonly List<Vector3> m_groupPositions = new List<Vector3>(8);
        private NavMeshPath m_path;
        private bool m_regionReady;
        private bool m_clearingEnemies;
        private bool m_warnedMissingPrefab;
        private float m_nextSpawnTime = float.PositiveInfinity;
        private Vector3 m_lastSpawnOrigin;
        private Vector3 m_lastSpawnTarget;
        private int m_lastSpawnGroupSize;
        private bool m_hasLastSpawn;

        public bool SpawningEnabled => m_regionReady && IsServerStarted();
        public IReadOnlyList<NetworkObject> EnemyPrefabs => m_enemyPrefabs;
        public string CurrentRegionName => m_generator != null ? m_generator.CurrentRegionName : "None";
        public bool NavMeshReady => m_generator != null && m_generator.IsNavMeshReady;
        public int ActiveThiefCount => m_activeThieves.Count;
        public int MaximumActiveThieves => m_maxActiveThieves;
        public float NextSpawnIn => SpawningEnabled
            ? Mathf.Max(0f, m_nextSpawnTime - Time.time)
            : float.PositiveInfinity;
        public int LastSpawnGroupSize => m_lastSpawnGroupSize;

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
            m_path = new NavMeshPath();
        }

        private void OnEnable()
        {
            if (m_generator != null)
            {
                m_generator.RegionClearing += OnRegionClearing;
                m_generator.RegionGenerated += OnRegionGenerated;
            }

            NetworkPlayerOwnership.ServerPlayerStarted += RegisterPlayer;
            NetworkPlayerOwnership.ServerPlayerStopped += UnregisterPlayer;
            NetworkPlayerOwnership[] existingPlayers = FindObjectsByType<NetworkPlayerOwnership>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int index = 0; index < existingPlayers.Length; index++)
            {
                RegisterPlayer(existingPlayers[index]);
            }

            if (m_generator != null && m_generator.IsGenerationReady)
            {
                OnRegionGenerated(m_generator);
            }
        }

        private void OnDisable()
        {
            if (m_generator != null)
            {
                m_generator.RegionClearing -= OnRegionClearing;
                m_generator.RegionGenerated -= OnRegionGenerated;
            }

            NetworkPlayerOwnership.ServerPlayerStarted -= RegisterPlayer;
            NetworkPlayerOwnership.ServerPlayerStopped -= UnregisterPlayer;
            m_players.Clear();
        }

        private void Update()
        {
            if (!SpawningEnabled || Time.time < m_nextSpawnTime)
            {
                return;
            }

            if (!HasEnemyPrefabs())
            {
                if (!m_warnedMissingPrefab)
                {
                    Debug.LogWarning(
                        "Enemy Spawn Manager has no enemy weapon prefabs. Assign at least one prefab to enable spawning.",
                        this);
                    m_warnedMissingPrefab = true;
                }

                ScheduleNextSpawn();
                return;
            }

            TrySpawnGroup();
            ScheduleNextSpawn();
        }

        public void SetGenerator(NeighbourhoodGenerator generator)
        {
            m_generator = generator;
        }

        public void SetEnemyPrefabs(NetworkObject[] enemyPrefabs)
        {
            m_enemyPrefabs = enemyPrefabs;
            m_warnedMissingPrefab = false;
        }

        public void SetThiefPrefab(NetworkObject thiefPrefab)
        {
            SetEnemyPrefabs(thiefPrefab != null ? new[] { thiefPrefab } : System.Array.Empty<NetworkObject>());
        }

        internal void Unregister(NetworkObject thief)
        {
            if (!m_clearingEnemies && thief != null)
            {
                m_activeThieves.Remove(thief);
            }
        }

        private void OnRegionGenerated(NeighbourhoodGenerator generator)
        {
            m_regionReady = generator != null && generator.IsGenerationReady && generator.IsNavMeshReady;
            if (m_regionReady)
            {
                ScheduleNextSpawn();
            }
            else
            {
                m_nextSpawnTime = float.PositiveInfinity;
            }
        }

        private void OnRegionClearing(NeighbourhoodGenerator generator)
        {
            m_regionReady = false;
            m_nextSpawnTime = float.PositiveInfinity;
            DespawnAllThieves();
        }

        private void TrySpawnGroup()
        {
            PruneMissingThieves();
            int availableSlots = m_maxActiveThieves - m_activeThieves.Count;
            if (availableSlots < m_minGroupSize || !TrySelectPlayer(out NetworkPlayerOwnership target))
            {
                return;
            }

            int desiredSize = Random.Range(m_minGroupSize, m_maxGroupSize + 1);
            desiredSize = Mathf.Min(desiredSize, availableSlots);
            if (!TryFindGroupOrigin(target.transform.position, out Vector3 origin))
            {
                return;
            }

            m_groupPositions.Clear();
            for (int index = 0; index < desiredSize; index++)
            {
                if (TryFindMemberPosition(origin, out Vector3 memberPosition))
                {
                    m_groupPositions.Add(memberPosition);
                }
            }

            if (m_groupPositions.Count < m_minGroupSize)
            {
                return;
            }

            int spawnedCount = 0;
            for (int index = 0; index < m_groupPositions.Count; index++)
            {
                NetworkObject selectedPrefab = SelectEnemyPrefab();
                if (selectedPrefab == null)
                {
                    break;
                }

                NetworkObject thief = Instantiate(
                    selectedPrefab,
                    m_groupPositions[index],
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                NavMeshAgent agent = thief.GetComponentInChildren<NavMeshAgent>();
                if (agent == null)
                {
                    Debug.LogError("The assigned enemy prefab requires a NavMeshAgent.", selectedPrefab);
                    Destroy(thief.gameObject);
                    break;
                }

                ThiefSpawnMember member = thief.GetComponent<ThiefSpawnMember>();
                if (member == null)
                {
                    member = thief.gameObject.AddComponent<ThiefSpawnMember>();
                }

                member.Register(this, thief);
                if (!target.TryGetComponent(out PlayerHealth targetHealth))
                {
                    Destroy(thief.gameObject);
                    continue;
                }

                float preferredRange = 1.25f;
                if (thief.TryGetComponent(out EnemyWeapon weapon))
                {
                    weapon.SetTargetServer(targetHealth);
                    preferredRange = weapon.PreferredRange;
                }

                if (thief.TryGetComponent(out ThiefNavigator navigator))
                {
                    navigator.SetTargetServer(targetHealth, preferredRange);
                }

                InstanceFinder.ServerManager.Spawn(thief);
                m_activeThieves.Add(thief);
                spawnedCount++;
            }

            if (spawnedCount > 0)
            {
                m_lastSpawnOrigin = origin;
                m_lastSpawnTarget = target.transform.position;
                m_lastSpawnGroupSize = spawnedCount;
                m_hasLastSpawn = true;
            }
        }

        private bool TrySelectPlayer(out NetworkPlayerOwnership selected)
        {
            selected = null;
            int carryingCount = 0;
            int validCount = 0;
            for (int index = m_players.Count - 1; index >= 0; index--)
            {
                NetworkPlayerOwnership player = m_players[index];
                if (!IsValidPlayer(player))
                {
                    if (player == null)
                    {
                        m_players.RemoveAt(index);
                    }

                    continue;
                }

                validCount++;
                if (player.TryGetComponent(out PlayerCouchCarrier carrier) && carrier.IsCarrying)
                {
                    carryingCount++;
                }
            }

            int desired = carryingCount > 0 ? Random.Range(0, carryingCount) : Random.Range(0, validCount);
            for (int index = 0; index < m_players.Count; index++)
            {
                NetworkPlayerOwnership player = m_players[index];
                if (!IsValidPlayer(player))
                {
                    continue;
                }

                bool carrying = player.TryGetComponent(out PlayerCouchCarrier carrier) && carrier.IsCarrying;
                if (carryingCount > 0 && !carrying)
                {
                    continue;
                }

                if (desired-- == 0)
                {
                    selected = player;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindGroupOrigin(Vector3 targetPosition, out Vector3 origin)
        {
            for (int attempt = 0; attempt < m_spawnPositionAttempts; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Random.Range(m_minSpawnDistance, m_maxSpawnDistance);
                Vector3 candidate = targetPosition +
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (!IsInsideWorld(candidate) ||
                    Random.value > m_generator.GetEnemySpawnOpportunity(candidate) ||
                    !NavMesh.SamplePosition(candidate, out NavMeshHit hit, m_navMeshSampleRadius, NavMesh.AllAreas) ||
                    !IsInsideWorld(hit.position) ||
                    !IsWithinSpawnRange(hit.position, targetPosition) ||
                    !IsFarEnoughFromPlayers(hit.position) ||
                    !CanReachTarget(hit.position, targetPosition))
                {
                    continue;
                }

                origin = hit.position;
                return true;
            }

            origin = default;
            return false;
        }

        private bool TryFindMemberPosition(Vector3 origin, out Vector3 position)
        {
            for (int attempt = 0; attempt < m_memberPositionAttempts; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * m_groupSpread;
                Vector3 candidate = origin + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, m_navMeshSampleRadius, NavMesh.AllAreas) &&
                    IsInsideWorld(hit.position) && IsFarEnoughFromPlayers(hit.position))
                {
                    position = hit.position;
                    return true;
                }
            }

            position = default;
            return false;
        }

        private bool CanReachTarget(Vector3 from, Vector3 target)
        {
            if (!NavMesh.SamplePosition(target, out NavMeshHit targetHit, m_navMeshSampleRadius, NavMesh.AllAreas))
            {
                return false;
            }

            m_path ??= new NavMeshPath();
            return NavMesh.CalculatePath(from, targetHit.position, NavMesh.AllAreas, m_path) &&
                   m_path.status == NavMeshPathStatus.PathComplete;
        }

        private bool IsWithinSpawnRange(Vector3 position, Vector3 target)
        {
            Vector3 offset = position - target;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            return distanceSquared >= m_minSpawnDistance * m_minSpawnDistance &&
                   distanceSquared <= m_maxSpawnDistance * m_maxSpawnDistance;
        }

        private bool IsFarEnoughFromPlayers(Vector3 position)
        {
            float minimumSquared = m_minSpawnDistance * m_minSpawnDistance;
            for (int index = 0; index < m_players.Count; index++)
            {
                NetworkPlayerOwnership player = m_players[index];
                if (IsValidPlayer(player) &&
                    (player.transform.position - position).sqrMagnitude < minimumSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsInsideWorld(Vector3 position)
        {
            Bounds bounds = m_generator.GeneratedWorldBounds;
            return position.x >= bounds.min.x && position.x <= bounds.max.x &&
                   position.z >= bounds.min.z && position.z <= bounds.max.z;
        }

        private static bool IsValidPlayer(NetworkPlayerOwnership player)
        {
            if (player == null || !player.isActiveAndEnabled ||
                player.NetworkObject == null || !player.NetworkObject.IsSpawned ||
                !player.TryGetComponent(out PlayerHealth health) || !health.IsAlive)
            {
                return false;
            }

            return player.Owner.IsValid || health.IsAiTeammate ||
                   (player.TryGetComponent(out DebugCouchBotController bot) &&
                    bot.IsAiTeammate);
        }

        private void RegisterPlayer(NetworkPlayerOwnership player)
        {
            if (player != null && !m_players.Contains(player))
            {
                m_players.Add(player);
            }
        }

        private void UnregisterPlayer(NetworkPlayerOwnership player)
        {
            if (player != null)
            {
                m_players.Remove(player);
            }
        }

        private void ScheduleNextSpawn()
        {
            m_nextSpawnTime = Time.time + Random.Range(m_minSpawnInterval, m_maxSpawnInterval);
        }

        private bool HasEnemyPrefabs()
        {
            if (m_enemyPrefabs == null)
            {
                return false;
            }

            for (int index = 0; index < m_enemyPrefabs.Length; index++)
            {
                if (m_enemyPrefabs[index] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private NetworkObject SelectEnemyPrefab()
        {
            if (!HasEnemyPrefabs())
            {
                return null;
            }

            int startIndex = Random.Range(0, m_enemyPrefabs.Length);
            for (int offset = 0; offset < m_enemyPrefabs.Length; offset++)
            {
                NetworkObject candidate = m_enemyPrefabs[(startIndex + offset) % m_enemyPrefabs.Length];
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private void PruneMissingThieves()
        {
            for (int index = m_activeThieves.Count - 1; index >= 0; index--)
            {
                NetworkObject thief = m_activeThieves[index];
                if (thief == null || !thief.IsSpawned)
                {
                    m_activeThieves.RemoveAt(index);
                }
            }
        }

        private void DespawnAllThieves()
        {
            if (!IsServerStarted())
            {
                m_activeThieves.Clear();
                return;
            }

            m_clearingEnemies = true;
            for (int index = m_activeThieves.Count - 1; index >= 0; index--)
            {
                NetworkObject thief = m_activeThieves[index];
                if (thief != null && thief.IsSpawned)
                {
                    InstanceFinder.ServerManager.Despawn(thief);
                }
            }

            m_activeThieves.Clear();
            m_clearingEnemies = false;
        }

        private static bool IsServerStarted()
        {
            return InstanceFinder.NetworkManager != null &&
                   InstanceFinder.NetworkManager.ServerManager.Started;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_minSpawnInterval = Mathf.Max(0.1f, m_minSpawnInterval);
            m_maxSpawnInterval = Mathf.Max(m_minSpawnInterval, m_maxSpawnInterval);
            m_minSpawnDistance = Mathf.Max(1f, m_minSpawnDistance);
            m_maxSpawnDistance = Mathf.Max(m_minSpawnDistance, m_maxSpawnDistance);
            m_minGroupSize = Mathf.Max(1, m_minGroupSize);
            m_maxGroupSize = Mathf.Max(m_minGroupSize, m_maxGroupSize);
            m_maxActiveThieves = Mathf.Max(m_minGroupSize, m_maxActiveThieves);
            m_spawnPositionAttempts = Mathf.Max(1, m_spawnPositionAttempts);
            m_memberPositionAttempts = Mathf.Max(1, m_memberPositionAttempts);
        }

        private void OnDrawGizmosSelected()
        {
            if (!m_drawSpawnGizmos || !m_hasLastSpawn)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.2f, 0.15f, 0.8f);
            Gizmos.DrawWireSphere(m_lastSpawnOrigin, m_groupSpread);
            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(m_lastSpawnTarget, m_minSpawnDistance);
            Gizmos.DrawWireSphere(m_lastSpawnTarget, m_maxSpawnDistance);
        }
#endif
    }
}
