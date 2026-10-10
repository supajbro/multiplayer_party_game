using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

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

        [Header("Delivery Shop")]
        [SerializeField] private DeliveryTierConfig[] m_deliveryTiers = new DeliveryTierConfig[3];
        [SerializeField, Min(1f)] private float m_dialogueMaximumDistance = 4f;

        [Header("Delivery Completion")]
        [SerializeField, Min(0.25f)] private float m_collectionRadius = 2.5f;
        [SerializeField, Min(0f)] private float m_markerGroundOffset = 0.035f;

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
        private readonly SyncVar<int> m_activeTierIndex = new(-1);

        private GameObject m_collectionMarker;
        private Material m_runtimeMarkerMaterial;
        private bool m_isCompletingDelivery;
        private readonly HashSet<int> m_usedDestinationIndices = new HashSet<int>();
        private readonly List<int>[] m_tierCandidates =
        {
            new List<int>(), new List<int>(), new List<int>()
        };
        private PlayerCouchCarrier m_dialoguePlayer;
        private DeliveryNPC m_dialogueNpc;
        private bool m_candidatesReady;

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
        public int ActiveTierIndex => m_activeTierIndex.Value;
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
            m_generator.RegionGenerated += OnRegionGenerated;
            m_generator.RegionClearing += OnRegionClearing;
        }

        private void OnDestroy()
        {
            m_destinationIndex.OnChange -= OnDestinationIndexChanged;
            m_currency.OnChange -= OnCurrencyChanged;
            m_state.OnChange -= OnStateChanged;
            m_objectiveStep.OnChange -= OnObjectiveStepChanged;
            if (m_generator != null)
            {
                m_generator.RegionGenerated -= OnRegionGenerated;
                m_generator.RegionClearing -= OnRegionClearing;
            }
            if (IsServerInitialized && m_dialoguePlayer != null && m_dialoguePlayer.Owner.IsValid)
                m_dialoguePlayer.CloseDeliveryDialogueTargetRpc(m_dialoguePlayer.Owner, "Delivery Manager unavailable.");
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
            m_activeTierIndex.Value = -1;
            StopDeliveryTimerServer();
            RebuildCandidateCache();
        }

        private void Update()
        {
            if (IsServerInitialized && IsDeliveryTimerActive && TimeManager.Tick >= m_deliveryDeadlineTick.Value)
                FailAndResetActiveDeliveryServer();
            if (IsServerInitialized && m_dialoguePlayer != null &&
                (m_dialogueNpc == null ||
                 Vector3.Distance(m_dialoguePlayer.transform.position, m_dialogueNpc.transform.position) >
                 m_dialogueMaximumDistance))
            {
                ReleaseDialogueLockServer(m_dialoguePlayer, true, "You moved too far from the Delivery Manager.");
            }
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

        public DeliveryTierConfig GetTierConfig(int index) =>
            m_deliveryTiers != null && index >= 0 && index < m_deliveryTiers.Length
                ? m_deliveryTiers[index]
                : null;

        public void SetDeliveryTiers(DeliveryTierConfig[] tiers)
        {
            m_deliveryTiers = tiers;
            m_candidatesReady = false;
        }

        [Server]
        public bool TryBeginDialogueServer(DeliveryNPC npc, PlayerCouchCarrier player)
        {
            if (!IsServerInitialized || npc == null || player == null || !player.Owner.IsValid)
                return false;
            if (m_dialoguePlayer != null && m_dialoguePlayer != player)
            {
                player.DeliveryNpcBusyTargetRpc(player.Owner);
                return false;
            }
            if (player.TryGetComponent(out PlayerHealth health) && !health.IsAlive) return false;
            m_dialoguePlayer = player;
            m_dialogueNpc = npc;
            SendDialogueState(player);
            return true;
        }

        [Server]
        public void ReleaseDialogueLockServer(PlayerCouchCarrier player)
        {
            ReleaseDialogueLockServer(player, false, string.Empty);
        }

        [Server]
        private void ReleaseDialogueLockServer(PlayerCouchCarrier player, bool closeClient, string message)
        {
            if (player == null || m_dialoguePlayer != player) return;
            m_dialoguePlayer = null;
            m_dialogueNpc = null;
            if (closeClient && player.Owner.IsValid)
                player.CloseDeliveryDialogueTargetRpc(player.Owner, message);
        }

        [Server]
        public void NotifyNpcUnavailableServer(DeliveryNPC npc)
        {
            if (npc != null && npc == m_dialogueNpc && m_dialoguePlayer != null)
                ReleaseDialogueLockServer(m_dialoguePlayer, true, "Delivery Manager unavailable.");
        }

        [Server]
        public bool TryPurchaseDeliveryServer(PlayerCouchCarrier player, int tierIndex)
        {
            m_chapterManager ??= GetComponent<SuburbsChapterManager>();
            DeliveryNPC npc = m_dialogueNpc;
            DeliveryTierConfig tier = GetTierConfig(tierIndex);
            if (!IsServerInitialized || player == null || player != m_dialoguePlayer || npc == null ||
                tier == null || HasActiveDelivery ||
                m_destinationIndex.Value >= 0 || m_activeCouch.Value != null)
            {
                if (player == m_dialoguePlayer) SendDialogueState(player);
                return false;
            }
            if (Vector3.Distance(player.transform.position, npc.transform.position) >
                m_dialogueMaximumDistance)
            {
                ReleaseDialogueLockServer(player, true, "You moved too far from the Delivery Manager.");
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

            EnsureCandidateCache();
            int stageIndex = m_chapterManager.CurrentStageIndex;
            int destinationIndex = SelectDestinationIndex(tierIndex);
            if (destinationIndex < 0)
            {
                SendDialogueState(player);
                return false;
            }
            if (m_currency.Value < tier.PurchasePrice)
            {
                SendDialogueState(player);
                return false;
            }

            SetStateServer(DeliveryState.Accepted, DeliveryObjectiveStep.CarryCouch);
            m_activeStageIndex.Value = m_chapterManager.CurrentStageIndex;
            m_attemptNumber.Value = Mathf.Max(1, m_attemptNumber.Value + 1);
            m_lastReward.Value = 0;
            m_activeTierIndex.Value = tierIndex;
            m_destinationIndex.Value = destinationIndex;
            NetworkObject couch = Instantiate(m_couchPrefab, npc.CouchSpawnPosition, npc.CouchSpawnRotation);
            Spawn(couch);
            couch.GetComponent<CouchCarryController>().SelectRandomModelVariantServer();
            m_activeCouch.Value = couch;
            m_currency.Value -= tier.PurchasePrice;
            m_usedDestinationIndices.Add(destinationIndex);
            StartDeliveryTimerServer(m_generator.DeliveryDestinations[destinationIndex], tier);
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
            ReleaseDialogueLockServer(player, true, $"{tier.DisplayName} started.");
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
            DeliveryTierConfig activeTier = GetTierConfig(m_activeTierIndex.Value);
            if (m_chapterManager != null)
                m_chapterManager.CompleteCurrentStageServer(m_activeStageIndex.Value);
            int configuredReward = activeTier != null ? activeTier.Reward : 0;
            int reward = destination != null
                ? Mathf.RoundToInt(configuredReward * destination.RewardModifier)
                : configuredReward;
            m_lastReward.Value = reward;
            m_activeTierIndex.Value = -1;
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
            m_activeTierIndex.Value = -1;
            m_lastReward.Value = 0;
            SetStateServer(DeliveryState.Available, DeliveryObjectiveStep.AcceptDelivery);
            return true;
        }

        [Server]
        private void StartDeliveryTimerServer(DeliveryDestination destination, DeliveryTierConfig tier)
        {
            if (tier == null)
            {
                StopDeliveryTimerServer();
                return;
            }
            float seconds = tier.CalculateDuration(destination);
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

        [Server]
        private void SetStateServer(DeliveryState state, DeliveryObjectiveStep objectiveStep)
        {
            m_state.Value = state;
            m_objectiveStep.Value = objectiveStep;
        }

        private void OnRegionGenerated(NeighbourhoodGenerator generator)
        {
            RebuildCandidateCache();
        }

        private void OnRegionClearing(NeighbourhoodGenerator generator)
        {
            m_candidatesReady = false;
            for (int i = 0; i < m_tierCandidates.Length; i++) m_tierCandidates[i].Clear();
        }

        private void EnsureCandidateCache()
        {
            if (!m_candidatesReady) RebuildCandidateCache();
        }

        private void RebuildCandidateCache()
        {
            for (int tierIndex = 0; tierIndex < m_tierCandidates.Length; tierIndex++)
                m_tierCandidates[tierIndex].Clear();
            m_candidatesReady = m_generator != null && m_generator.HasGeneratedNeighbourhood &&
                                m_generator.IsDeliveryDifficultyReady;
            if (!m_candidatesReady) return;

            IReadOnlyList<DeliveryDestination> destinations = m_generator.DeliveryDestinations;
            for (int destinationIndex = 0; destinationIndex < destinations.Count; destinationIndex++)
            {
                for (int tierIndex = 0; tierIndex < m_tierCandidates.Length; tierIndex++)
                {
                    DeliveryTierConfig config = GetTierConfig(tierIndex);
                    if (config != null && config.IsDestinationEligible(destinations[destinationIndex]))
                        m_tierCandidates[tierIndex].Add(destinationIndex);
                }
            }
        }

        private int SelectDestinationIndex(int tierIndex)
        {
            EnsureCandidateCache();
            if (tierIndex < 0 || tierIndex >= m_tierCandidates.Length ||
                m_tierCandidates[tierIndex].Count == 0) return -1;

            List<int> candidates = m_tierCandidates[tierIndex];
            List<int> unused = new List<int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                if (!m_usedDestinationIndices.Contains(candidates[i])) unused.Add(candidates[i]);
            List<int> pool = unused.Count > 0 ? unused : candidates;
            unchecked
            {
                int seed = m_generator.CurrentSeed;
                seed = (seed * 397) ^ tierIndex;
                seed = (seed * 397) ^ m_attemptNumber.Value;
                return pool[new System.Random(seed).Next(pool.Count)];
            }
        }

        [Server]
        private void SendDialogueState(PlayerCouchCarrier player)
        {
            if (player == null || !player.Owner.IsValid) return;
            EnsureCandidateCache();
            bool[] available = new bool[3];
            string[] reasons = new string[3];
            for (int index = 0; index < 3; index++)
            {
                DeliveryTierConfig tier = GetTierConfig(index);
                if (tier == null) reasons[index] = "Not configured";
                else if (HasActiveDelivery || (m_state.Value != DeliveryState.Available &&
                         m_state.Value != DeliveryState.ReturnToNPC)) reasons[index] = "A delivery is already active";
                else if (!m_candidatesReady || m_tierCandidates[index].Count == 0) reasons[index] = "No eligible houses in this neighbourhood";
                else if (m_currency.Value < tier.PurchasePrice) reasons[index] = $"Need ${tier.PurchasePrice - m_currency.Value:N0} more";
                else available[index] = true;
            }
            player.OpenDeliveryDialogueTargetRpc(player.Owner,
                available[0], reasons[0], available[1], reasons[1], available[2], reasons[2]);
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
            m_testTeleportOutsideRadius = Mathf.Max(0.25f, m_testTeleportOutsideRadius);
        }
#endif
    }
}
