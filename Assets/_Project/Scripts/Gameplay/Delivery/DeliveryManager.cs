using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Server-authoritative state for the single active couch delivery.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NeighbourhoodGenerator))]
    [DisallowMultipleComponent]
    public sealed class DeliveryManager : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private SuburbsChapterManager m_chapterManager;
        [SerializeField] private NetworkObject m_couchPrefab;
        [SerializeField] private GameObject m_collectionMarkerPrefab;

        [Header("Delivery Completion")]
        [SerializeField, Min(0.25f)] private float m_collectionRadius = 2.5f;
        [SerializeField, Min(0)] private int m_rewardPerDelivery = 100;
        [SerializeField, Min(0f)] private float m_markerGroundOffset = 0.035f;

        [Header("Delivery Timer")]
        [SerializeField, Min(0f)] private float m_baseDeliveryTime = 45f;
        [SerializeField, Min(0.1f)] private float m_expectedCouchCarrySpeed = 2.5f;
        [SerializeField, Min(0.1f)] private float m_deliveryTimeMultiplier = 1.25f;
        [SerializeField, Min(1f)] private float m_minimumDeliveryTime = 60f;
        [SerializeField, Min(1f)] private float m_maximumDeliveryTime = 600f;
        [SerializeField, Min(0f)] private float m_uphillDistanceMultiplier = 2f;

        [Header("Debug/Test Teleport")]
        [SerializeField, Min(0.25f)] private float m_testTeleportOutsideRadius = 1.5f;
        [SerializeField, Min(1f)] private float m_groundProbeHeight = 40f;
        [SerializeField, Min(1f)] private float m_groundProbeDistance = 100f;

        private readonly SyncVar<NetworkObject> m_activeCouch = new();
        private readonly SyncVar<int> m_destinationIndex = new(-1);
        private readonly SyncVar<int> m_currency = new();
        private readonly SyncVar<DeliveryState> m_state = new(DeliveryState.Unavailable);
        private readonly SyncVar<DeliveryObjectiveStep> m_objectiveStep = new(DeliveryObjectiveStep.None);
        private readonly SyncVar<int> m_activeStageIndex = new(-1);
        private readonly SyncVar<int> m_attemptNumber = new();
        private readonly SyncVar<int> m_lastReward = new();
        private readonly SyncVar<uint> m_deliveryDeadlineTick = new();
        private readonly SyncVar<float> m_deliveryDuration = new();
        private readonly SyncVar<int> m_deliveryFailureSequence = new();

        private GameObject m_collectionMarker;
        private Material m_runtimeMarkerMaterial;
        private bool m_isCompletingDelivery;
        private readonly HashSet<int> m_usedDestinationIndices = new HashSet<int>();

        public event Action<int> CurrencyChanged;
        public event Action<DeliveryState> StateChanged;
        public event Action<DeliveryObjectiveStep> ObjectiveStepChanged;

        public NetworkObject ActiveCouch => m_activeCouch.Value;
        public int ActiveDestinationIndex => m_destinationIndex.Value;
        public int CurrentCurrency => m_currency.Value;
        public DeliveryState State => m_state.Value;
        public DeliveryObjectiveStep ObjectiveStep => m_objectiveStep.Value;
        public int ActiveStageIndex => m_activeStageIndex.Value;
        public int AttemptNumber => m_attemptNumber.Value;
        public int LastReward => m_lastReward.Value;
        public float DeliveryDuration => m_deliveryDuration.Value;
        public int DeliveryFailureSequence => m_deliveryFailureSequence.Value;
        public bool IsDeliveryTimerActive =>
            m_state.Value == DeliveryState.Transport && m_deliveryDeadlineTick.Value != 0;
        public float RemainingDeliveryTime
        {
            get
            {
                if (!IsDeliveryTimerActive || TimeManager == null) return 0f;
                uint now = TimeManager.Tick;
                return now >= m_deliveryDeadlineTick.Value
                    ? 0f
                    : (float)TimeManager.TicksToTime(m_deliveryDeadlineTick.Value - now);
            }
        }
        public bool HasActiveDelivery => m_destinationIndex.Value >= 0 && m_activeCouch.Value != null;

        public DeliveryDestination ActiveDestination
        {
            get
            {
                m_generator ??= GetComponent<NeighbourhoodGenerator>();
                IReadOnlyList<DeliveryDestination> destinations = m_generator.DeliveryDestinations;
                int index = m_destinationIndex.Value;
                return index >= 0 && index < destinations.Count ? destinations[index] : null;
            }
        }

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
            m_chapterManager ??= GetComponent<SuburbsChapterManager>();
            m_destinationIndex.OnChange += OnDestinationIndexChanged;
            m_currency.OnChange += OnCurrencyChanged;
            m_state.OnChange += OnStateChanged;
            m_objectiveStep.OnChange += OnObjectiveStepChanged;
        }

        private void OnDestroy()
        {
            m_destinationIndex.OnChange -= OnDestinationIndexChanged;
            m_currency.OnChange -= OnCurrencyChanged;
            m_state.OnChange -= OnStateChanged;
            m_objectiveStep.OnChange -= OnObjectiveStepChanged;
            DestroyCollectionMarker();
            if (m_runtimeMarkerMaterial != null)
            {
                Destroy(m_runtimeMarkerMaterial);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RefreshCollectionMarker();
            CurrencyChanged?.Invoke(m_currency.Value);
            StateChanged?.Invoke(m_state.Value);
            ObjectiveStepChanged?.Invoke(m_objectiveStep.Value);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_usedDestinationIndices.Clear();
            m_state.Value = DeliveryState.Available;
            m_objectiveStep.Value = DeliveryObjectiveStep.AcceptDelivery;
            m_activeStageIndex.Value = -1;
            m_attemptNumber.Value = 0;
            m_lastReward.Value = 0;
                    StopDeliveryTimerServer();
        }

        private void Update()
        {
            if (IsServerInitialized && IsDeliveryTimerActive && TimeManager.Tick >= m_deliveryDeadlineTick.Value)
                FailAndResetActiveDeliveryServer();
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || m_state.Value != DeliveryState.Transport ||
                m_isCompletingDelivery || !HasActiveDelivery ||
                !m_activeCouch.Value.TryGetComponent(out CouchCarryController couch) ||
                couch.ServerCarrierCount <= 0)
            {
                return;
            }

            DeliveryDestination destination = ActiveDestination;
            if (destination == null || !destination.CanReceiveDelivery)
            {
                return;
            }

            Vector3 offset = couch.transform.position - destination.DropPosition.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= m_collectionRadius * m_collectionRadius)
            {
                CompleteDeliveryServer(couch);
            }
        }

        public void SetCouchPrefab(NetworkObject couchPrefab)
        {
            m_couchPrefab = couchPrefab;
        }

        public bool TryStartDeliveryServer(DeliveryNPC npc, PlayerCouchCarrier player)
        {
            m_chapterManager ??= GetComponent<SuburbsChapterManager>();
            if (!IsServerInitialized || npc == null || player == null || HasActiveDelivery ||
                m_destinationIndex.Value >= 0 || m_activeCouch.Value != null)
            {
                return false;
            }

            if (m_state.Value == DeliveryState.ReturnToNPC)
            {
                SetStateServer(DeliveryState.Available, DeliveryObjectiveStep.AcceptDelivery);
            }

            if (m_state.Value != DeliveryState.Available || m_chapterManager == null ||
                !m_chapterManager.CanStartCurrentStageServer())
            {
                return false;
            }

            if (player.TryGetComponent(out PlayerHealth health) && !health.IsAlive)
            {
                return false;
            }

            if (m_couchPrefab == null || m_couchPrefab.GetComponent<CouchCarryController>() == null)
            {
                Debug.LogError("Delivery Manager needs a registered Couch prefab with CouchCarryController.", this);
                return false;
            }

            SetStateServer(DeliveryState.NPCInteraction, DeliveryObjectiveStep.AcceptDelivery);
            int stageIndex = m_chapterManager.CurrentStageIndex;
            int destinationIndex = SelectDestinationIndex(stageIndex);
            if (destinationIndex < 0)
            {
                Debug.LogWarning("A delivery could not start because the neighbourhood has no valid delivery points.", this);
                SetStateServer(DeliveryState.Available, DeliveryObjectiveStep.AcceptDelivery);
                return false;
            }

            SetStateServer(DeliveryState.Accepted, DeliveryObjectiveStep.CarryCouch);
            m_activeStageIndex.Value = m_chapterManager.CurrentStageIndex;
            m_attemptNumber.Value = Mathf.Max(1, m_attemptNumber.Value + 1);
            m_lastReward.Value = 0;
            m_destinationIndex.Value = destinationIndex;
            NetworkObject couch = Instantiate(m_couchPrefab, npc.CouchSpawnPosition, npc.CouchSpawnRotation);
            Spawn(couch);
            m_activeCouch.Value = couch;
            m_usedDestinationIndices.Add(destinationIndex);
            StartDeliveryTimerServer(npc.CouchSpawnPosition, m_generator.DeliveryDestinations[destinationIndex]);
            DeliveryRouteMetrics selectedMetrics = m_generator.DeliveryDestinations[destinationIndex].RouteMetrics;
            if (selectedMetrics != null)
            {
                Debug.Log(
                    $"Stage {stageIndex + 1} selected destination {destinationIndex}: " +
                    $"{selectedMetrics.ProgressionZone}/{selectedMetrics.Zone}, " +
                    $"route {selectedMetrics.RouteLength:F0}m, " +
                    $"ascent {selectedMetrics.CumulativeElevationGain:F1}m, " +
                    $"max grade {selectedMetrics.MaximumRoadGrade:F1}°, " +
                    $"score {selectedMetrics.DifficultyScore:F0}.",
                    this);
            }
            SetStateServer(DeliveryState.CouchSpawned, DeliveryObjectiveStep.CarryCouch);
            SetStateServer(DeliveryState.Transport, DeliveryObjectiveStep.DeliverCouch);
            return true;
        }

        /// <summary>Server-validated temporary test shortcut used by the T input.</summary>
        public bool TryTeleportActiveDeliveryServer(PlayerCouchCarrier requestingPlayer)
        {
            CouchCarryController requestedCouch = requestingPlayer != null
                ? requestingPlayer.GetCarriedCouchServer()
                : null;
            if (!IsServerInitialized || m_isCompletingDelivery || requestedCouch == null ||
                !HasActiveDelivery || requestedCouch.NetworkObject != m_activeCouch.Value ||
                !m_activeCouch.Value.TryGetComponent(out CouchCarryController couch) ||
                couch.ServerCarrierCount <= 0)
            {
                return false;
            }

            DeliveryDestination destination = ActiveDestination;
            if (destination == null || !destination.CanReceiveDelivery)
            {
                return false;
            }

            Vector3 awayFromHouse = Vector3.ProjectOnPlane(
                destination.DropPosition.position - destination.transform.position,
                Vector3.up);
            if (awayFromHouse.sqrMagnitude < 0.01f)
            {
                awayFromHouse = Vector3.ProjectOnPlane(destination.DropPosition.forward, Vector3.up);
            }

            awayFromHouse = awayFromHouse.sqrMagnitude > 0.01f
                ? awayFromHouse.normalized
                : Vector3.forward;
            Vector3 couchPosition = destination.DropPosition.position +
                awayFromHouse * (m_collectionRadius + m_testTeleportOutsideRadius);
            float pivotToBottom = CalculatePivotToBottom(couch);
            couchPosition.y = FindGroundHeight(couchPosition, destination.DropPosition.position.y) + pivotToBottom;
            Quaternion couchRotation = Quaternion.LookRotation(-awayFromHouse, Vector3.up);

            couch.TeleportServer(couchPosition, couchRotation);
            for (int index = 0; index < CouchCarryController.MaximumCarryPoints; index++)
            {
                PlayerCouchCarrier carrier = couch.GetServerCarrier(index);
                CouchCarryPoint point = couch.GetPoint(index);
                if (carrier == null || point == null)
                {
                    continue;
                }

                Vector3 outward = Vector3.ProjectOnPlane(point.transform.position - couchPosition, Vector3.up);
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : -awayFromHouse;
                Vector3 playerPosition = point.transform.position + outward * 0.75f;
                playerPosition.y = FindGroundHeight(
                    playerPosition,
                    destination.DropPosition.position.y,
                    couch.transform);
                Vector3 facing = Vector3.ProjectOnPlane(couchPosition - playerPosition, Vector3.up);
                Quaternion playerRotation = facing.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                    : couchRotation;
                carrier.TeleportWithCouchServer(playerPosition, playerRotation);
            }

            return true;
        }

        private void CompleteDeliveryServer(CouchCarryController couch)
        {
            if (!IsServerInitialized || m_state.Value != DeliveryState.Transport ||
                m_isCompletingDelivery || couch == null ||
                couch.NetworkObject != m_activeCouch.Value)
            {
                return;
            }

            m_isCompletingDelivery = true;
            StopDeliveryTimerServer();
            SetStateServer(DeliveryState.DestinationReached, DeliveryObjectiveStep.DeliverCouch);
            NetworkObject deliveredCouch = m_activeCouch.Value;
            DeliveryDestination destination = ActiveDestination;
            m_activeCouch.Value = null;
            m_destinationIndex.Value = -1;
            couch.ReleaseAllOccupantsServer();
            SetStateServer(DeliveryState.Completed, DeliveryObjectiveStep.None);
            int configuredReward = m_chapterManager != null
                ? m_chapterManager.CompleteCurrentStageServer(m_activeStageIndex.Value)
                : m_rewardPerDelivery;
            int reward = destination != null
                ? Mathf.RoundToInt(configuredReward * destination.RewardModifier)
                : configuredReward;
            m_lastReward.Value = reward;
            SetStateServer(DeliveryState.Reward, DeliveryObjectiveStep.None);
            m_currency.Value += reward;
            Debug.Log(
                $"Delivery {m_activeStageIndex.Value + 1} complete for {reward}. " +
                $"Team currency: {m_currency.Value}",
                this);
            if (deliveredCouch != null && deliveredCouch.IsSpawned)
            {
                Despawn(deliveredCouch);
            }

            bool chapterComplete = m_chapterManager != null && m_chapterManager.ChapterCompleted;
            SetStateServer(
                chapterComplete ? DeliveryState.Unavailable : DeliveryState.ReturnToNPC,
                chapterComplete
                    ? DeliveryObjectiveStep.ChapterComplete
                    : DeliveryObjectiveStep.ReturnToSeller);
            m_isCompletingDelivery = false;
        }

        /// <summary>
        /// Cleans up an unsuccessful attempt while preserving its chapter stage and
        /// all previously completed progression. Phase 4 will call this on party wipe.
        /// </summary>
        [Server]
        public bool FailAndResetActiveDeliveryServer()
        {
            if (m_isCompletingDelivery || !HasActiveDelivery ||
                (m_state.Value != DeliveryState.Transport &&
                 m_state.Value != DeliveryState.PartyWipe))
            {
                return false;
            }

            StopDeliveryTimerServer();
            m_deliveryFailureSequence.Value++;
            SetStateServer(DeliveryState.Failed, DeliveryObjectiveStep.None);
            NetworkObject failedCouch = m_activeCouch.Value;
            m_activeCouch.Value = null;
            m_destinationIndex.Value = -1;
            if (failedCouch != null && failedCouch.IsSpawned)
            {
                if (failedCouch.TryGetComponent(out CouchCarryController couch))
                {
                    couch.ReleaseAllOccupantsServer();
                }

                Despawn(failedCouch);
            }

            SetStateServer(DeliveryState.Resetting, DeliveryObjectiveStep.None);
            m_activeStageIndex.Value = -1;
            m_lastReward.Value = 0;
            SetStateServer(DeliveryState.Available, DeliveryObjectiveStep.AcceptDelivery);
            return true;
        }

        [Server]
        private void StartDeliveryTimerServer(Vector3 startPosition, DeliveryDestination destination)
        {
            float distance = CalculateTravelDistance(startPosition, destination);
            float seconds = Mathf.Clamp(m_baseDeliveryTime +
                distance / Mathf.Max(0.1f, m_expectedCouchCarrySpeed) * m_deliveryTimeMultiplier,
                m_minimumDeliveryTime, m_maximumDeliveryTime);
            m_deliveryDuration.Value = seconds;
            uint ticks = TimeManager.TimeToTicks(seconds);
            m_deliveryDeadlineTick.Value = TimeManager.Tick + (ticks == 0 ? 1u : ticks);
        }

        [Server]
        private void StopDeliveryTimerServer()
        {
            m_deliveryDeadlineTick.Value = 0;
            m_deliveryDuration.Value = 0f;
        }

        private float CalculateTravelDistance(Vector3 startPosition, DeliveryDestination destination)
        {
            if (destination == null) return 0f;
            DeliveryRouteMetrics metrics = destination.RouteMetrics;
            if (metrics != null && metrics.IsReachable)
                return metrics.RouteLength + metrics.FinalCarryDistance +
                    (metrics.CumulativeElevationGain + metrics.FinalCarryElevationGain) * m_uphillDistanceMultiplier;
            NavMeshPath path = new NavMeshPath();
            if (NavMesh.CalculatePath(startPosition, destination.DropPosition.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                float distance = 0f;
                for (int i = 1; i < path.corners.Length; i++)
                    distance += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                return distance;
            }
            return Vector3.Distance(startPosition, destination.DropPosition.position);
        }
        [Server]
        private void SetStateServer(DeliveryState state, DeliveryObjectiveStep objectiveStep)
        {
            m_state.Value = state;
            m_objectiveStep.Value = objectiveStep;
        }

        private int SelectDestinationIndex(int stageIndex)
        {
            if (m_generator == null || !m_generator.HasGeneratedNeighbourhood ||
                !m_generator.IsDeliveryDifficultyReady)
            {
                return -1;
            }

            IReadOnlyList<DeliveryDestination> destinations = m_generator.DeliveryDestinations;
            DeliveryDestinationQuery query = DeliveryDestinationQuery.ForSuburbsStage(stageIndex);

            int selected = FindBestDestination(destinations, query, stageIndex, true, true);
            if (selected < 0)
            {
                selected = FindBestDestination(destinations, query, stageIndex, false, true);
            }

            if (selected < 0)
            {
                selected = FindBestDestination(destinations, query, stageIndex, true, false);
            }

            return selected >= 0
                ? selected
                : FindBestDestination(destinations, query, stageIndex, false, false);
        }

        private int FindBestDestination(
            IReadOnlyList<DeliveryDestination> destinations,
            DeliveryDestinationQuery query,
            int stageIndex,
            bool requireStrictMatch,
            bool requireUnused)
        {
            int selectedIndex = -1;
            float selectedRank = float.MaxValue;
            for (int index = 0; index < destinations.Count; index++)
            {
                DeliveryDestination destination = destinations[index];
                DeliveryRouteMetrics metrics = destination != null ? destination.RouteMetrics : null;
                if (destination == null || !destination.CanReceiveDelivery ||
                    metrics == null || !metrics.IsReachable ||
                    (requireUnused && m_usedDestinationIndices.Contains(index)) ||
                    (requireStrictMatch && !query.Matches(metrics)))
                {
                    continue;
                }

                float rank = query.CalculateSuitability(metrics, stageIndex) +
                    DeterministicTieBreaker(index, stageIndex);
                if (rank < selectedRank)
                {
                    selectedRank = rank;
                    selectedIndex = index;
                }
            }

            return selectedIndex;
        }

        private float DeterministicTieBreaker(int destinationIndex, int stageIndex)
        {
            unchecked
            {
                int value = m_generator.CurrentSeed;
                value = (value * 397) ^ destinationIndex;
                value = (value * 397) ^ stageIndex;
                value = (value * 397) ^ m_attemptNumber.Value;
                return (value & 0x7fffffff) / (float)int.MaxValue * 0.01f;
            }
        }

        private void OnDestinationIndexChanged(int previous, int next, bool asServer)
        {
            RefreshCollectionMarker();
        }

        private void OnCurrencyChanged(int previous, int next, bool asServer)
        {
            CurrencyChanged?.Invoke(next);
        }

        private void OnStateChanged(DeliveryState previous, DeliveryState next, bool asServer)
        {
            StateChanged?.Invoke(next);
        }

        private void OnObjectiveStepChanged(
            DeliveryObjectiveStep previous,
            DeliveryObjectiveStep next,
            bool asServer)
        {
            ObjectiveStepChanged?.Invoke(next);
        }

        private void RefreshCollectionMarker()
        {
            DestroyCollectionMarker();
            DeliveryDestination destination = ActiveDestination;
            if (destination == null || !destination.CanReceiveDelivery || m_destinationIndex.Value < 0)
            {
                return;
            }

            Vector3 markerPosition = destination.DropPosition.position + Vector3.up * m_markerGroundOffset;
            if (m_collectionMarkerPrefab != null)
            {
                m_collectionMarker = Instantiate(
                    m_collectionMarkerPrefab,
                    markerPosition,
                    Quaternion.identity,
                    transform);
                m_collectionMarker.name = "ActiveDeliveryCollectionMarker";
                return;
            }

            m_collectionMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m_collectionMarker.name = "ActiveDeliveryCollectionMarker_Runtime";
            m_collectionMarker.transform.SetParent(transform, true);
            m_collectionMarker.transform.SetPositionAndRotation(markerPosition, Quaternion.identity);
            m_collectionMarker.transform.localScale = new Vector3(
                m_collectionRadius * 2f,
                0.025f,
                m_collectionRadius * 2f);
            Destroy(m_collectionMarker.GetComponent<Collider>());

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                m_runtimeMarkerMaterial ??= new Material(shader)
                {
                    name = "Runtime Delivery Marker Material",
                    color = new Color(0.1f, 0.95f, 0.2f, 0.72f)
                };
                m_collectionMarker.GetComponent<Renderer>().sharedMaterial = m_runtimeMarkerMaterial;
            }
        }

        private void DestroyCollectionMarker()
        {
            if (m_collectionMarker != null)
            {
                Destroy(m_collectionMarker);
                m_collectionMarker = null;
            }
        }

        private float FindGroundHeight(
            Vector3 position,
            float fallbackHeight,
            Transform ignoredRoot = null)
        {
            Vector3 origin = position + Vector3.up * m_groundProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                m_groundProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float highestGround = float.NegativeInfinity;
            for (int index = 0; index < hits.Length; index++)
            {
                Transform hitTransform = hits[index].collider.transform;
                if (ignoredRoot != null &&
                    (hitTransform == ignoredRoot || hitTransform.IsChildOf(ignoredRoot)))
                {
                    continue;
                }

                highestGround = Mathf.Max(highestGround, hits[index].point.y);
            }

            return float.IsNegativeInfinity(highestGround) ? fallbackHeight : highestGround;
        }

        private static float CalculatePivotToBottom(CouchCarryController couch)
        {
            Collider[] colliders = couch.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0)
            {
                return 0.5f;
            }

            float minimumY = float.PositiveInfinity;
            for (int index = 0; index < colliders.Length; index++)
            {
                if (colliders[index] != null && !colliders[index].isTrigger)
                {
                    minimumY = Mathf.Min(minimumY, colliders[index].bounds.min.y);
                }
            }

            return float.IsPositiveInfinity(minimumY)
                ? 0.5f
                : Mathf.Max(0f, couch.transform.position.y - minimumY);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_collectionRadius = Mathf.Max(0.25f, m_collectionRadius);
            m_rewardPerDelivery = Mathf.Max(0, m_rewardPerDelivery);
            m_testTeleportOutsideRadius = Mathf.Max(0.25f, m_testTeleportOutsideRadius);
            m_expectedCouchCarrySpeed = Mathf.Max(0.1f, m_expectedCouchCarrySpeed);
            m_deliveryTimeMultiplier = Mathf.Max(0.1f, m_deliveryTimeMultiplier);
            m_minimumDeliveryTime = Mathf.Max(1f, m_minimumDeliveryTime);
            m_maximumDeliveryTime = Mathf.Max(m_minimumDeliveryTime, m_maximumDeliveryTime);
        }
#endif
    }
}
