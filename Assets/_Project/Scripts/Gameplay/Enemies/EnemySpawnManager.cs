using CouchGuys.Gameplay.Depot;
using System.Collections.Generic;
using CouchGuys.Networking;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Couch;
using FishNet;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    [System.Serializable]
    public sealed class CargoThreatSettings
    {
        [Tooltip("Easy, Medium and Hard couch threat weights.")]
        public Vector3 TierWeights = new Vector3(1f, 2f, 3f);
        [Range(0f, 1f)] public float AdditionalCouchContribution = .25f;
        [Min(0f), Tooltip("Nearby unloaded cargo keeps sharing pressure. Separate vehicles also share within this distance.")]
        public float EncounterMergeDistance = 25f;
        [Min(1f), Tooltip("Only players near an encounter can attract its reinforcements.")]
        public float PlayerDistance = 100f;
        [Tooltip("Active cap for multiple grouped deliveries only. Single deliveries use Max Active Thieves.")]
        public AnimationCurve ActiveEnemyCap = AnimationCurve.Linear(1f, 4f, 5.25f, 12f);
        [Tooltip("Scales the hardest tier's minimum wave size by curve(combined threat) / curve(single tier weight).")]
        public AnimationCurve MinimumWaveSize = AnimationCurve.Linear(1f, 1f, 5.25f, 3f);
        [Tooltip("Scales the hardest tier's maximum wave size relative to its single-couch baseline.")]
        public AnimationCurve MaximumWaveSize = AnimationCurve.Linear(1f, 2f, 5.25f, 5f);
        [Tooltip("Scales the hardest tier's spawn interval range relative to its single-couch baseline. Does not replace tier timing.")]
        public AnimationCurve WaveIntervalSeconds = AnimationCurve.Linear(1f, 20f, 5.25f, 10f);
        [Tooltip("Additional shared limits apply only to multiple grouped deliveries. Tier limits still apply.")]
        [Min(0)] public int MaximumRockets = 1;
        [Min(0)] public int MaximumGrapplers = 1;

        public float Weight(DeliveryTier tier) => Mathf.Max(.01f,
            tier == DeliveryTier.Easy ? TierWeights.x : tier == DeliveryTier.Medium ? TierWeights.y : TierWeights.z);
        public float Threat(float sum, float largest) => largest +
            Mathf.Max(0f, sum - largest) * Mathf.Clamp01(AdditionalCouchContribution);
        public int Cap(float threat) => Mathf.Max(1, Mathf.RoundToInt(ActiveEnemyCap.Evaluate(threat)));
        private float Scale(AnimationCurve curve, float threat, DeliveryTierConfig tier) =>
            Mathf.Max(.01f, curve.Evaluate(threat)) / Mathf.Max(.01f, curve.Evaluate(Weight(tier.Tier)));
        public int MinWave(DeliveryTierConfig tier, float threat, int count) => count <= 1 ? tier.MinThievesPerWave :
            Mathf.Max(1, Mathf.RoundToInt(tier.MinThievesPerWave * Scale(MinimumWaveSize, threat, tier)));
        public int MaxWave(DeliveryTierConfig tier, float threat, int count) => count <= 1 ? tier.MaxThievesPerWave :
            Mathf.Max(MinWave(tier, threat, count), Mathf.RoundToInt(tier.MaxThievesPerWave * Scale(MaximumWaveSize, threat, tier)));
        public float Interval(DeliveryTierConfig tier, float threat, int count, float roll) =>
            Mathf.Max(.1f, Mathf.Lerp(tier.MinSpawnInterval, tier.MaxSpawnInterval, Mathf.Clamp01(roll)) *
                (count <= 1 ? 1f : Scale(WaveIntervalSeconds, threat, tier)));
    }

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

        [Header("Spawn Distance")]
        [SerializeField, Min(1f)] private float m_minSpawnDistance = 30f;
        [SerializeField, Min(1f)] private float m_maxSpawnDistance = 80f;
        [SerializeField, Min(0.1f)] private float m_navMeshSampleRadius = 8f;
        [SerializeField, Min(1)] private int m_spawnPositionAttempts = 12;

        [Header("Groups")]
        [SerializeField, Min(0.5f)] private float m_groupSpread = 4f;
        [SerializeField, Min(1)] private int m_memberPositionAttempts = 6;
        [SerializeField, Min(1)] private int m_maxActiveThieves = 15;

        [Header("Shared Cargo Threat")]
        [SerializeField] private CargoThreatSettings m_cargoThreat = new CargoThreatSettings();

        [Header("Debug")]
        [SerializeField] private bool m_drawSpawnGizmos;

        private readonly List<NetworkPlayerOwnership> m_players = new List<NetworkPlayerOwnership>(4);
        private readonly List<NetworkObject> m_activeThieves = new List<NetworkObject>(16);
        private readonly List<Vector3> m_groupPositions = new List<Vector3>(8);
        private NavMeshPath m_path;
        private DeliveryManager m_deliveryManager;
        private readonly Dictionary<int, float> m_deliverySpawnTimes = new();
        private int m_spawningDeliveryId;
        private CargoEncounter m_encounter;
        private bool HasSharedCargo => m_encounter != null && m_encounter.Jobs.Count > 1;
        private float m_nextEncounterUpdate;
        private readonly List<CargoEncounter> m_encounters = new();
        private int SpawningTierIndex => m_encounter != null ? m_encounter.TierIndex :
            m_deliveryManager.TryGetDelivery(m_spawningDeliveryId, out var job) ? job.TierIndex : -1;
        public void RemoveDeliveryEnemiesServer(int id, bool departNaturally = false)
        {
            m_deliverySpawnTimes.Remove(id);
            for (int i = m_activeThieves.Count - 1; i >= 0; i--)
            {
                var thief = m_activeThieves[i];
                if (thief != null && thief.TryGetComponent(out ThiefSpawnMember member) && member.DeliveryId == id)
                {
                    if (departNaturally && thief.IsSpawned && thief.TryGetComponent(out ThiefNavigator navigator))
                    {
                        navigator.BeginDepartureServer();
                        continue;
                    }
                    m_activeThieves.RemoveAt(i);
                    if (thief.IsSpawned) InstanceFinder.ServerManager.Despawn(thief);
                }
            }
        }
        private bool m_regionReady;
        private bool m_clearingEnemies;
        private float m_nextSpawnTime = float.PositiveInfinity;
        private Vector3 m_lastSpawnOrigin;
        private Vector3 m_lastSpawnTarget;
        private int m_lastSpawnGroupSize;
        private bool m_hasLastSpawn;

        public bool SpawningEnabled => m_regionReady && IsServerStarted() &&
            m_deliveryManager != null && m_deliveryManager.HasActiveDelivery &&
            (m_deliveryManager.State == DeliveryState.CouchSpawned ||
             m_deliveryManager.State == DeliveryState.Transport);
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
            m_deliveryManager = GetComponent<DeliveryManager>();
            m_path = new NavMeshPath();
        }

        private void OnEnable()
        {
            m_deliveryManager ??= GetComponent<DeliveryManager>() ?? FindFirstObjectByType<DeliveryManager>();
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
            m_nextSpawnTime = float.PositiveInfinity;
        }

        private void Update()
        {
            if (!SpawningEnabled)
            {
                m_nextSpawnTime = float.PositiveInfinity;
                return;
            }

            if (Time.time < m_nextEncounterUpdate) return;
            m_nextEncounterUpdate = Time.time + .5f;
            BuildCargoEncounters();
            m_nextSpawnTime = float.PositiveInfinity;
            foreach (CargoEncounter encounter in m_encounters)
            {
                m_encounter = encounter;
                m_spawningDeliveryId = encounter.Jobs[Random.Range(0, encounter.Jobs.Count)].Id;
                float next = EncounterDeadline(encounter);
                if (float.IsNegativeInfinity(next) || Time.time >= next)
                {
                    if (!float.IsNegativeInfinity(next) && HasEnemyPrefabs()) TrySpawnGroup();
                    ScheduleNextSpawn();
                    next = m_nextSpawnTime;
                }
                foreach (var job in encounter.Jobs) m_deliverySpawnTimes[job.Id] = next;
            }
            m_encounter = null;
            m_nextSpawnTime = float.PositiveInfinity;
            foreach (var encounter in m_encounters)
                m_nextSpawnTime = Mathf.Min(m_nextSpawnTime, m_deliverySpawnTimes[encounter.Jobs[0].Id]);
        }

        public void SetGenerator(NeighbourhoodGenerator generator)
        {
            m_generator = generator;
        }

        public void SetEnemyPrefabs(NetworkObject[] enemyPrefabs)
        {
            m_enemyPrefabs = enemyPrefabs;
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
            m_nextSpawnTime = float.PositiveInfinity;
        }

        private void OnRegionClearing(NeighbourhoodGenerator generator)
        {
            m_deliverySpawnTimes.Clear();
            m_regionReady = false;
            m_nextSpawnTime = float.PositiveInfinity;
            DespawnAllThieves();
        }

        private sealed class CargoJob
        {
            public int Id;
            public int TierIndex;
            public DeliveryTier Tier;
            public float Weight;
            public Vector3 Position;
            public Transform Vehicle;
        }

        private sealed class CargoEncounter
        {
            public readonly List<CargoJob> Jobs = new();
            public float Threat;
            public int TierIndex;
        }

        private float EncounterDeadline(CargoEncounter encounter)
        {
            float next = float.NegativeInfinity;
            // Preserve the latest existing deadline when encounters merge. Splits inherit it.
            foreach (CargoJob job in encounter.Jobs)
                if (m_deliverySpawnTimes.TryGetValue(job.Id, out float scheduled)) next = Mathf.Max(next, scheduled);
            return next;
        }

        private void BuildCargoEncounters()
        {
            m_encounters.Clear();
            foreach (var delivery in m_deliveryManager.Deliveries)
            {
                if (delivery.Status != DeliveryState.Transport || delivery.Couch == null ||
                    !delivery.Couch.TryGetComponent(out CouchCarryController couch)) continue;
                DeliveryTierConfig tier = m_deliveryManager.GetTierConfig(delivery.TierIndex);
                if (tier == null) continue;
                couch.TryGetDeliveryVehicle(out Transform vehicle);
                var encounter = new CargoEncounter();
                encounter.Jobs.Add(new CargoJob { Id = delivery.Id, TierIndex = delivery.TierIndex,
                    Tier = tier.Tier, Weight = m_cargoThreat.Weight(tier.Tier),
                    Position = couch.transform.position, Vehicle = vehicle });
                // Merge connected cargo, including nearby couches just unloaded from a vehicle.
                for (int i = 0; i < m_encounters.Count;)
                {
                    if (!EncountersTouch(encounter, m_encounters[i])) { i++; continue; }
                    encounter.Jobs.AddRange(m_encounters[i].Jobs);
                    m_encounters.RemoveAt(i);
                    i = 0;
                }
                float sum = 0f, largest = 0f;
                DeliveryTier hardest = DeliveryTier.Easy;
                encounter.TierIndex = delivery.TierIndex;
                foreach (CargoJob job in encounter.Jobs)
                {
                    sum += job.Weight;
                    largest = Mathf.Max(largest, job.Weight);
                    if (job.Tier >= hardest) { hardest = job.Tier; encounter.TierIndex = job.TierIndex; }
                }
                encounter.Threat = m_cargoThreat.Threat(sum, largest);
                m_encounters.Add(encounter);
            }
        }

        private bool EncountersTouch(CargoEncounter a, CargoEncounter b)
        {
            float distance = Mathf.Max(0f, m_cargoThreat.EncounterMergeDistance);
            foreach (CargoJob first in a.Jobs)
                foreach (CargoJob second in b.Jobs)
                    if ((first.Vehicle != null && first.Vehicle == second.Vehicle) ||
                        (first.Position - second.Position).sqrMagnitude <= distance * distance) return true;
            return false;
        }

        private bool IsEncounterDelivery(int id)
        {
            if (m_encounter == null) return id == m_spawningDeliveryId;
            foreach (CargoJob job in m_encounter.Jobs) if (job.Id == id) return true;
            return false;
        }

        private bool IsNearEncounter(Vector3 position, float distance)
        {
            if (m_encounter == null) return true;
            foreach (CargoJob job in m_encounter.Jobs)
                if ((job.Position - position).sqrMagnitude <= distance * distance) return true;
            return false;
        }

        private bool IsEncounterPlayer(NetworkPlayerOwnership player) => IsValidPlayer(player) &&
            (!HasSharedCargo || IsNearEncounter(player.transform.position, Mathf.Max(1f, m_cargoThreat.PlayerDistance)));

        private bool IsEncounterEnemy(NetworkObject enemy, ThiefSpawnMember member) =>
            !(enemy.TryGetComponent(out ThiefNavigator navigator) && navigator.IsDeparting) &&
            (IsEncounterDelivery(member.DeliveryId) || (HasSharedCargo &&
                IsNearEncounter(enemy.transform.position, Mathf.Max(0f, m_cargoThreat.EncounterMergeDistance))));

        private int CountEncounterEnemies()
        {
            int count = 0;
            foreach (NetworkObject enemy in m_activeThieves)
                if (enemy != null && enemy.IsSpawned && enemy.TryGetComponent(out ThiefSpawnMember member) &&
                    IsEncounterEnemy(enemy, member) && enemy.TryGetComponent(out ThiefHealth health) && health.IsAlive) count++;
            return count;
        }

        private void TrySpawnGroup()
        {
            PruneMissingThieves();
            if (!TryGetCurrentSpawnRange(out int minimumGroupSize, out int maximumGroupSize))
            {
                return;
            }
            int availableSlots = Mathf.Min(m_maxActiveThieves - m_activeThieves.Count,
                HasSharedCargo ? m_cargoThreat.Cap(m_encounter.Threat) - CountEncounterEnemies() : m_maxActiveThieves);
            if (availableSlots < minimumGroupSize || !TrySelectPlayer(out NetworkPlayerOwnership target))
            {
                return;
            }

            int desiredSize = Random.Range(minimumGroupSize, maximumGroupSize + 1);
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

            if (m_groupPositions.Count < minimumGroupSize)
            {
                return;
            }

            if (!target.TryGetComponent(out PlayerHealth targetHealth) || !targetHealth.IsAlive) return;
            // Keep filtering ahead of support selection, including invalid prefabs.
            var group = new List<NetworkObject>(m_groupPositions.Count);
            int plannedRockets = 0, plannedGrapplers = 0;
            for (int index = 0; index < m_groupPositions.Count; index++)
            {
                NetworkObject selectedPrefab = SelectEnemyPrefab(plannedRockets, plannedGrapplers);
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

                if (thief.GetComponent<ThiefNavigator>() == null || thief.GetComponent<ThiefHealth>() == null)
                {
                    Destroy(thief.gameObject);
                    continue;
                }
                group.Add(thief);
                if (thief.TryGetComponent(out EnemyWeapon plannedWeapon) && plannedWeapon.IsRocketLauncher) plannedRockets++;
                if (thief.GetComponent<GrapplerThief>() != null) plannedGrapplers++;
            }

            DeliveryTierConfig tier = m_deliveryManager.GetTierConfig(SpawningTierIndex);
            int replacement = SpeakerThief.SelectReplacement(group, tier, Random.value, Random.Range(0, int.MaxValue));
            if (replacement >= 0)
            {
                // Commit this slot last: at least one companion must actually be spawned.
                NetworkObject reserved = group[replacement];
                group.RemoveAt(replacement);group.Add(reserved);
                replacement = group.Count - 1;
            }
            int spawnedCount = 0;
            for (int index = 0; index < group.Count; index++)
            {
                NetworkObject thief = group[index];
                if (index == replacement && HasLivingGroupCompanion(group))
                {
                    NetworkObject speaker = Instantiate(tier.SpeakerThiefPrefab, thief.transform.position, thief.transform.rotation);
                    if (speaker.GetComponent<SpeakerThief>() != null && speaker.GetComponent<NavMeshAgent>() != null &&
                        speaker.GetComponent<ThiefNavigator>() != null && speaker.GetComponent<ThiefHealth>() != null &&
                        speaker.GetComponent<EnemyWeapon>() == null && speaker.GetComponent<GrapplerThief>() == null)
                    {
                        Destroy(thief.gameObject);thief = speaker;group[index] = thief;
                    }
                    else Destroy(speaker.gameObject); // Preserve the regular slot if misconfigured.
                }
                ThiefSpawnMember member = thief.GetComponent<ThiefSpawnMember>();
                if (member == null)
                {
                    member = thief.gameObject.AddComponent<ThiefSpawnMember>();
                }

                member.Register(this, thief);
                member.AssignDelivery(m_spawningDeliveryId);
                member.AssignGroup(group);

                float preferredRange = 1.25f;
                if (thief.TryGetComponent(out EnemyWeapon weapon))
                {
                    weapon.SetTargetServer(targetHealth);
                    preferredRange = weapon.PreferredRange;
                }

                if (thief.TryGetComponent(out ThiefNavigator navigator))
                {
                    navigator.ConfigureTacticalSlotServer(index, group.Count);
                    if (thief.GetComponent<SpeakerThief>() == null) navigator.SetTargetServer(targetHealth, preferredRange);
                }

                InstanceFinder.ServerManager.Spawn(thief);
                if (!thief.IsSpawned) { group[index] = null;Destroy(thief.gameObject);continue; }
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

        private static bool HasLivingGroupCompanion(IReadOnlyList<NetworkObject> group)
        {
            foreach (NetworkObject member in group)
                if (member != null && member.IsSpawned && member.GetComponent<SpeakerThief>() == null &&
                    member.TryGetComponent(out ThiefHealth health) && health.IsAlive) return true;
            return false;
        }

        private bool TryGetCurrentSpawnRange(out int minimum, out int maximum)
        {
            minimum = 0;
            maximum = 0;
            if (!SpawningEnabled)
            {
                return false;
            }

            DeliveryTierConfig tier = m_deliveryManager.GetTierConfig(SpawningTierIndex);
            if (tier == null) return false;
            minimum = m_encounter != null ? m_cargoThreat.MinWave(tier, m_encounter.Threat, m_encounter.Jobs.Count) : tier.MinThievesPerWave;
            maximum = m_encounter != null ? m_cargoThreat.MaxWave(tier, m_encounter.Threat, m_encounter.Jobs.Count) : tier.MaxThievesPerWave;
            if (HasSharedCargo)
            {
                minimum = Mathf.Min(minimum, m_cargoThreat.Cap(m_encounter.Threat));
                maximum = Mathf.Clamp(maximum, minimum, m_cargoThreat.Cap(m_encounter.Threat));
            }
            return true;
        }

        private bool TrySelectPlayer(out NetworkPlayerOwnership selected)
        {
            selected = null;
            int carryingCount = 0;
            int validCount = 0;
            for (int index = m_players.Count - 1; index >= 0; index--)
            {
                NetworkPlayerOwnership player = m_players[index];
                if (!IsEncounterPlayer(player) || DepotSafeZone.Contains(player.transform.position))
                {
                    if (player == null)
                    {
                        m_players.RemoveAt(index);
                    }

                    continue;
                }

                validCount++;
                if (player.TryGetComponent(out PlayerCouchCarrier carrier) && carrier.CarriedCouch != null && IsEncounterDelivery(carrier.CarriedCouch.DeliveryId))
                {
                    carryingCount++;
                }
            }

            int desired = carryingCount > 0 ? Random.Range(0, carryingCount) : Random.Range(0, validCount);
            for (int index = 0; index < m_players.Count; index++)
            {
                NetworkPlayerOwnership player = m_players[index];
                if (!IsEncounterPlayer(player) || DepotSafeZone.Contains(player.transform.position))
                {
                    continue;
                }

                bool carrying = player.TryGetComponent(out PlayerCouchCarrier carrier) && carrier.CarriedCouch != null && IsEncounterDelivery(carrier.CarriedCouch.DeliveryId);
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
                    !IsInsideWorld(hit.position) || DepotSafeZone.Contains(hit.position) ||
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
                    IsInsideWorld(hit.position) && !DepotSafeZone.Contains(hit.position) && IsFarEnoughFromPlayers(hit.position))
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
            DeliveryTierConfig tier = SpawningEnabled
                ? m_deliveryManager.GetTierConfig(SpawningTierIndex)
                : null;
            if (m_encounter != null)
            {
                m_nextSpawnTime = tier != null ? Time.time +
                    m_cargoThreat.Interval(tier, m_encounter.Threat, m_encounter.Jobs.Count, Random.value) : float.PositiveInfinity;
                return;
            }
            m_nextSpawnTime = tier != null
                ? Time.time + Random.Range(tier.MinSpawnInterval, tier.MaxSpawnInterval)
                : float.PositiveInfinity;
        }

        private bool HasEnemyPrefabs()
        {
            if (m_enemyPrefabs == null)
            {
                return false;
            }

            for (int index = 0; index < m_enemyPrefabs.Length; index++)
            {
                if (m_enemyPrefabs[index] != null && m_enemyPrefabs[index].GetComponent<SpeakerThief>() == null)
                {
                    return true;
                }
            }

            return false;
        }

        private NetworkObject SelectEnemyPrefab(int plannedRockets = 0, int plannedGrapplers = 0)
        {
            DeliveryTierConfig tier = m_deliveryManager != null
                ? m_deliveryManager.GetTierConfig(SpawningTierIndex) : null;
            if (tier != null && (!HasSharedCargo || CountActiveRocketThieves() + plannedRockets < Mathf.Max(0, m_cargoThreat.MaximumRockets)) &&
                tier.ShouldReplaceWithRocketThief(CountActiveRocketThieves() + plannedRockets, Random.value))
                return tier.RocketThiefPrefab;
            if (tier != null && (!HasSharedCargo || CountActiveGrapplers() + plannedGrapplers < Mathf.Max(0, m_cargoThreat.MaximumGrapplers)) &&
                tier.ShouldReplaceWithGrappler(CountActiveGrapplers() + plannedGrapplers, Random.value))
                return tier.GrapplerThiefPrefab;

            if (!HasEnemyPrefabs())
            {
                return null;
            }

            int startIndex = Random.Range(0, m_enemyPrefabs.Length);
            for (int offset = 0; offset < m_enemyPrefabs.Length; offset++)
            {
                NetworkObject candidate = m_enemyPrefabs[(startIndex + offset) % m_enemyPrefabs.Length];
                if (SpeakerThief.IsRegular(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private int CountActiveRocketThieves()
        {
            int count = 0;
            foreach (NetworkObject thief in m_activeThieves)
                if (thief != null && thief.IsSpawned && thief.TryGetComponent(out ThiefSpawnMember member) &&
                    IsEncounterEnemy(thief, member) && thief.TryGetComponent(out EnemyWeapon weapon) &&
                    weapon.IsRocketLauncher && thief.TryGetComponent(out ThiefHealth health) && health.IsAlive) count++;
            return count;
        }

        private int CountActiveGrapplers()
        {
            int count = 0;
            foreach (NetworkObject thief in m_activeThieves)
                if (thief != null && thief.IsSpawned && thief.TryGetComponent(out ThiefSpawnMember member) &&
                    IsEncounterEnemy(thief, member) && thief.TryGetComponent(out GrapplerThief _) &&
                    thief.TryGetComponent(out ThiefHealth health) && health.IsAlive) count++;
            return count;
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
            m_minSpawnDistance = Mathf.Max(1f, m_minSpawnDistance);
            m_maxSpawnDistance = Mathf.Max(m_minSpawnDistance, m_maxSpawnDistance);
            m_maxActiveThieves = Mathf.Max(1, m_maxActiveThieves);
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
