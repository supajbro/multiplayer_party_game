using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using System.Collections;

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
        [SerializeField] private ShopCatalogue m_shopCatalogue;
        [SerializeField, Min(1f)] private float m_dialogueMaximumDistance = 4f;

        [Header("Vehicle Shop Spawning")]
        [SerializeField] private LayerMask m_vehicleGroundMask = ~0;
        [SerializeField] private LayerMask m_vehicleObstructionMask = ~0;
        [SerializeField] private Vector3 m_vehicleClearanceSize = new Vector3(3.4f, 2.8f, 7f);
        [SerializeField, Min(1)] private int m_vehicleSpawnAttempts = 12;
        [SerializeField, Min(0.5f)] private float m_vehicleSpawnSearchStep = 4.5f;
        [SerializeField, Min(1f)] private float m_vehicleGroundProbeHeight = 20f;
        [SerializeField, Min(1f)] private float m_vehicleGroundProbeDistance = 50f;
        [SerializeField, Range(0f, 45f)] private float m_vehicleMaximumSlope = 18f;
        [SerializeField, Min(0f)] private float m_vehicleGroundOffset = 0.12f;

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
        private readonly SyncVar<bool> m_exitDoorOpen = new();
        private readonly SyncVar<bool> m_vehicleGarageDoorOpen = new();

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
        private uint m_lastShopPurchaseRequestId;
        private bool m_candidatesReady;
        private readonly Collider[] m_vehicleClearanceHits = new Collider[64];
        private DepotController m_depot;
        private Coroutine m_conveyorDispatch;

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
        public ShopCatalogue ShopCatalogue => m_shopCatalogue;

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
            m_shopCatalogue ??= Resources.Load<ShopCatalogue>("Shop/DeliveryManagerShop");
            m_destinationIndex.OnChange += OnDestinationIndexChanged;
            m_currency.OnChange += OnCurrencyChanged;
            m_state.OnChange += OnStateChanged;
            m_objectiveStep.OnChange += OnObjectiveStepChanged;
            m_exitDoorOpen.OnChange += OnExitDoorOpenChanged;
            m_vehicleGarageDoorOpen.OnChange += OnVehicleGarageDoorOpenChanged;
            m_generator.RegionGenerated += OnRegionGenerated;
            m_generator.RegionClearing += OnRegionClearing;
        }

        private void OnDestroy()
        {
            m_destinationIndex.OnChange -= OnDestinationIndexChanged;
            m_currency.OnChange -= OnCurrencyChanged;
            m_state.OnChange -= OnStateChanged;
            m_objectiveStep.OnChange -= OnObjectiveStepChanged;
            m_exitDoorOpen.OnChange -= OnExitDoorOpenChanged;
            m_vehicleGarageDoorOpen.OnChange -= OnVehicleGarageDoorOpenChanged;
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
            m_exitDoorOpen.Value = false;
            m_vehicleGarageDoorOpen.Value = false;
            m_state.Value = DeliveryState.Available;
            m_objectiveStep.Value = DeliveryObjectiveStep.AcceptDelivery;
            m_activeStageIndex.Value = -1;
            m_attemptNumber.Value = 0;
            m_lastReward.Value = 0;
            m_activeTierIndex.Value = -1;
            StopDeliveryTimerServer();
            RebuildCandidateCache();
        }

        /// <summary>Adds shared test currency from an already-authorised server action.</summary>
        [Server]
        public void AddTestCurrencyServer(int amount)
        {
            if (amount > 0) m_currency.Value += amount;
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
            if (IsServerInitialized && m_depot != null)
            {
                if (!m_exitDoorOpen.Value &&
                    m_depot.IsDoorwayBlocked(DepotDoorKind.Exit))
                    m_exitDoorOpen.Value = true;
                if (!m_vehicleGarageDoorOpen.Value &&
                    m_depot.IsDoorwayBlocked(DepotDoorKind.VehicleGarage))
                    m_vehicleGarageDoorOpen.Value = true;
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

        public void SetShopCatalogue(ShopCatalogue catalogue)
        {
            m_shopCatalogue = catalogue;
        }

        public ShopItemDefinition GetShopItem(int index) =>
            m_shopCatalogue != null && index >= 0 && index < m_shopCatalogue.Items.Count
                ? m_shopCatalogue.Items[index]
                : null;

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
            m_lastShopPurchaseRequestId = 0;
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
            ResolveDepot();
            Vector3 couchSpawnPosition = m_depot != null && m_depot.HasConveyor
                ? m_depot.CouchHiddenSpawn.position
                : npc.CouchSpawnPosition;
            Quaternion couchSpawnRotation = m_depot != null && m_depot.HasConveyor
                ? m_depot.CouchHiddenSpawn.rotation
                : npc.CouchSpawnRotation;
            NetworkObject couch = Instantiate(m_couchPrefab, couchSpawnPosition, couchSpawnRotation);
            Spawn(couch);
            CouchCarryController couchController = couch.GetComponent<CouchCarryController>();
            couchController.SelectRandomModelVariantServer();
            m_activeCouch.Value = couch;
            m_currency.Value -= tier.PurchasePrice;
            m_usedDestinationIndices.Add(destinationIndex);
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
            if (m_depot != null && m_depot.HasConveyor)
            {
                couchController.SetDepotTransportActiveServer(true);
                if (m_conveyorDispatch != null) StopCoroutine(m_conveyorDispatch);
                m_conveyorDispatch = StartCoroutine(DispatchCouchAlongConveyorServer(
                    couchController, m_generator.DeliveryDestinations[destinationIndex], tier));
            }
            else
            {
                StartDeliveryTimerServer(m_generator.DeliveryDestinations[destinationIndex], tier);
                SetStateServer(DeliveryState.Transport, DeliveryObjectiveStep.DeliverCouch);
            }
            ReleaseDialogueLockServer(player, true, $"{tier.DisplayName} started.");
            return true;
        }

        [Server]
        public bool TryPurchaseShopItemServer(
            PlayerCouchCarrier player,
            string itemId,
            uint requestId)
        {
            DeliveryNPC npc = m_dialogueNpc;
            if (!IsServerInitialized || player == null || player != m_dialoguePlayer || npc == null)
                return false;

            // A monotonically increasing client request ID makes retransmitted or
            // duplicated button input harmless while the dialogue remains open.
            if (requestId == 0 || requestId <= m_lastShopPurchaseRequestId)
                return false;
            m_lastShopPurchaseRequestId = requestId;

            if (Vector3.Distance(player.transform.position, npc.transform.position) >
                m_dialogueMaximumDistance)
            {
                ReleaseDialogueLockServer(player, true, "You moved too far from the Delivery Manager.");
                return false;
            }

            ShopItemDefinition item = m_shopCatalogue != null ? m_shopCatalogue.Find(itemId) : null;
            if (item == null || item.PurchaseBehaviour == null)
            {
                SendDialogueState(player, "That item is not available.", true);
                return false;
            }

            if (player.TryGetComponent(out PlayerHealth health) && !health.IsAlive)
            {
                SendDialogueState(player, "You cannot shop right now.", true);
                return false;
            }

            if (m_currency.Value < item.Price)
            {
                SendDialogueState(player, $"You need ${item.Price - m_currency.Value:N0} more.", true);
                return false;
            }

            if (!item.PurchaseBehaviour.CanPurchase(this, npc, player, out string reason) ||
                !item.PurchaseBehaviour.TryFulfil(this, npc, player, out reason))
            {
                SendDialogueState(player, string.IsNullOrWhiteSpace(reason)
                    ? "The purchase could not be completed."
                    : reason, true);
                return false;
            }

            // Fulfilment has succeeded on the authoritative server. Charging here
            // ensures a blocked spawn can never consume the player's money.
            m_currency.Value -= item.Price;
            SendDialogueState(player, $"Purchased {item.DisplayName}!", true);
            return true;
        }

        [Server]
        public bool CanSpawnPurchasedVehicleServer(
            NetworkObject vehiclePrefab,
            DeliveryNPC npc,
            out string failureReason)
        {
            if (vehiclePrefab == null || npc == null)
            {
                failureReason = "Vehicle is not configured";
                return false;
            }

            bool found = TryFindVehicleSpawnPose(npc, out _, out _);
            failureReason = found ? string.Empty : "No clear vehicle spawn location nearby";
            return found;
        }

        [Server]
        public bool TrySpawnPurchasedVehicleServer(
            NetworkObject vehiclePrefab,
            DeliveryNPC npc,
            out string failureReason)
        {
            if (vehiclePrefab == null || !TryFindVehicleSpawnPose(npc, out Vector3 position, out Quaternion rotation))
            {
                failureReason = "No clear vehicle spawn location nearby";
                return false;
            }

            NetworkObject vehicle = Instantiate(vehiclePrefab, position, rotation);
            if (vehicle == null)
            {
                failureReason = "Vehicle could not be created";
                return false;
            }

            Spawn(vehicle);
            m_vehicleGarageDoorOpen.Value = true;
            failureReason = string.Empty;
            return true;
        }

        private bool TryFindVehicleSpawnPose(
            DeliveryNPC npc,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (npc == null) return false;

            ResolveDepot();
            if (m_depot != null)
            {
                foreach (Transform marker in m_depot.EnumerateVehicleSpawnMarkers())
                {
                    Quaternion depotHeading = m_depot.VehicleArrival != null
                        ? m_depot.VehicleArrival.rotation : marker.rotation;
                    if (TryValidateVehicleSpawnPose(marker.position, depotHeading, npc,
                            out position, out rotation))
                        return true;
                }
                return false;
            }

            Vector3 primary = npc.VehicleSpawnPosition;
            Vector3 right = Vector3.ProjectOnPlane(npc.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(npc.transform.forward, Vector3.up).normalized;
            Quaternion heading = Quaternion.Euler(0f, npc.VehicleSpawnRotation.eulerAngles.y, 0f);
            int attempts = Mathf.Max(1, m_vehicleSpawnAttempts);
            for (int index = 0; index < attempts; index++)
            {
                Vector3 offset = Vector3.zero;
                if (index > 0)
                {
                    int ring = ((index - 1) / 8) + 1;
                    float angle = ((index - 1) % 8) * 45f * Mathf.Deg2Rad;
                    offset = (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) *
                             (ring * m_vehicleSpawnSearchStep);
                }

                if (TryValidateVehicleSpawnPose(primary + offset, heading, npc,
                        out position, out rotation)) return true;
            }

            return false;
        }

        private bool TryValidateVehicleSpawnPose(Vector3 markerPosition,
            Quaternion markerRotation, DeliveryNPC npc, out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = default;
            Vector3 probeOrigin = markerPosition + Vector3.up * m_vehicleGroundProbeHeight;
            if (!TryFindVehicleGround(probeOrigin, npc, out RaycastHit ground) ||
                Vector3.Angle(ground.normal, Vector3.up) > m_vehicleMaximumSlope)
                return false;

            Quaternion heading = Quaternion.Euler(0f, markerRotation.eulerAngles.y, 0f);
            Quaternion candidateRotation = Quaternion.FromToRotation(Vector3.up, ground.normal) * heading;
            Vector3 candidatePosition = ground.point + ground.normal * m_vehicleGroundOffset;
            Vector3 halfExtents = Vector3.Max(m_vehicleClearanceSize * 0.5f, Vector3.one * 0.1f);
            Vector3 centre = candidatePosition + candidateRotation * (Vector3.up * halfExtents.y);
            int hitCount = Physics.OverlapBoxNonAlloc(centre,
                new Vector3(halfExtents.x, Mathf.Max(0.05f, halfExtents.y - 0.08f), halfExtents.z),
                m_vehicleClearanceHits, candidateRotation, m_vehicleObstructionMask,
                QueryTriggerInteraction.Ignore);
            bool blocked = hitCount >= m_vehicleClearanceHits.Length;
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                Collider hit = m_vehicleClearanceHits[hitIndex];
                if (hit == null || hit.isTrigger) continue;
                blocked = true;
                break;
            }
            if (blocked) return false;
            position = candidatePosition;
            rotation = candidateRotation;
            return true;
        }

        private bool TryFindVehicleGround(
            Vector3 probeOrigin,
            DeliveryNPC npc,
            out RaycastHit ground)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                probeOrigin,
                Vector3.down,
                m_vehicleGroundProbeDistance,
                m_vehicleGroundMask,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int index = 0; index < hits.Length; index++)
            {
                Collider collider = hits[index].collider;
                if (collider == null || collider.transform.IsChildOf(npc.transform) ||
                    collider.GetComponentInParent<TruckController>() != null ||
                    collider.GetComponentInParent<PlayerCouchCarrier>() != null)
                {
                    continue;
                }

                ground = hits[index];
                return true;
            }

            ground = default;
            return false;
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

        [Server]
        public bool TryToggleNearbyDepotDoorServer(PlayerCouchCarrier player, float maximumDistance)
        {
            ResolveDepot();
            if (!IsServerInitialized || player == null || m_depot == null ||
                !m_depot.HasNearbyButton(player.transform.position, maximumDistance,
                    out DepotDoorKind kind))
                return false;

            if (kind == DepotDoorKind.Exit)
            {
                bool open = !m_exitDoorOpen.Value;
                if (!open && m_depot.IsDoorwayBlocked(kind)) return false;
                m_exitDoorOpen.Value = open;
            }
            else
            {
                bool open = !m_vehicleGarageDoorOpen.Value;
                if (!open && m_depot.IsDoorwayBlocked(kind)) return false;
                m_vehicleGarageDoorOpen.Value = open;
            }
            return true;
        }

        [Server]
        private IEnumerator DispatchCouchAlongConveyorServer(
            CouchCarryController couch, DeliveryDestination destination,
            DeliveryTierConfig tier)
        {
            if (m_depot == null || couch == null) yield break;
            if (m_depot.ArrivalDelay > 0f)
                yield return new WaitForSeconds(m_depot.ArrivalDelay);

            IReadOnlyList<Transform> path = m_depot.ConveyorPath;
            for (int index = 0; index < path.Count; index++)
            {
                Transform waypoint = path[index];
                if (waypoint == null) continue;
                while (couch != null && couch.IsSpawned &&
                       Vector3.Distance(couch.transform.position, waypoint.position) > 0.015f)
                {
                    Vector3 next = Vector3.MoveTowards(couch.transform.position,
                        waypoint.position, m_depot.ConveyorSpeed * Time.fixedDeltaTime);
                    Quaternion rotation = Quaternion.RotateTowards(couch.transform.rotation,
                        waypoint.rotation, 120f * Time.fixedDeltaTime);
                    couch.CouchRigidbody.MovePosition(next);
                    couch.CouchRigidbody.MoveRotation(rotation);
                    yield return new WaitForFixedUpdate();
                }
                if (couch == null || !couch.IsSpawned) yield break;
                couch.CouchRigidbody.position = waypoint.position;
                couch.CouchRigidbody.rotation = waypoint.rotation;
            }

            if (couch == null || !couch.IsSpawned) yield break;
            couch.SetDepotTransportActiveServer(false);
            StartDeliveryTimerServer(destination, tier);
            SetStateServer(DeliveryState.Transport, DeliveryObjectiveStep.DeliverCouch);
            m_conveyorDispatch = null;
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
            StopConveyorDispatchServer();
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
            StopConveyorDispatchServer();
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
            ResolveDepot();
            m_depot?.ApplyDoorStates(m_exitDoorOpen.Value,
                m_vehicleGarageDoorOpen.Value, true);
            RebuildCandidateCache();
        }

        private void OnRegionClearing(NeighbourhoodGenerator generator)
        {
            StopConveyorDispatchServer();
            m_depot = null;
            m_candidatesReady = false;
            for (int i = 0; i < m_tierCandidates.Length; i++) m_tierCandidates[i].Clear();
        }

        private void ResolveDepot()
        {
            m_depot = m_generator != null && m_generator.GeneratedStartingArea != null
                ? m_generator.GeneratedStartingArea.Depot
                : null;
        }

        [Server]
        private void StopConveyorDispatchServer()
        {
            if (m_conveyorDispatch == null) return;
            StopCoroutine(m_conveyorDispatch);
            m_conveyorDispatch = null;
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
        private void SendDialogueState(
            PlayerCouchCarrier player,
            string notice = "",
            bool openShop = false)
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

            int shopCount = m_shopCatalogue != null ? m_shopCatalogue.Items.Count : 0;
            bool[] shopAvailable = new bool[shopCount];
            string[] shopReasons = new string[shopCount];
            for (int index = 0; index < shopCount; index++)
            {
                ShopItemDefinition item = m_shopCatalogue.Items[index];
                if (item == null) shopReasons[index] = "Not configured";
                else if (item.PurchaseBehaviour == null) shopReasons[index] = "Purchase behaviour is missing";
                else if (m_currency.Value < item.Price)
                    shopReasons[index] = $"Need ${item.Price - m_currency.Value:N0} more";
                else if (!item.PurchaseBehaviour.CanPurchase(this, m_dialogueNpc, player, out string reason))
                    shopReasons[index] = string.IsNullOrWhiteSpace(reason) ? "Currently unavailable" : reason;
                else shopAvailable[index] = true;
            }

            player.OpenDeliveryDialogueTargetRpc(player.Owner,
                available[0], reasons[0], available[1], reasons[1], available[2], reasons[2],
                shopAvailable, shopReasons, m_currency.Value, notice, openShop);
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

        private void OnExitDoorOpenChanged(bool previous, bool next, bool asServer)
        {
            ResolveDepot();
            m_depot?.ApplyDoorStates(next, m_vehicleGarageDoorOpen.Value);
        }

        private void OnVehicleGarageDoorOpenChanged(bool previous, bool next, bool asServer)
        {
            ResolveDepot();
            m_depot?.ApplyDoorStates(m_exitDoorOpen.Value, next);
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
