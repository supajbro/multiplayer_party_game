using System;
using System.Collections.Generic;
using System.Linq;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using System.Collections;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Server-authoritative state for independent couch deliveries.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NeighbourhoodGenerator))]
    [DisallowMultipleComponent]
    public sealed partial class DeliveryManager : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private SuburbsChapterManager m_chapterManager;
        [SerializeField] private NetworkObject m_couchPrefab;

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

        [Header("Debug/Test Teleport")]
        [SerializeField, Min(0.25f)] private float m_testTeleportOutsideRadius = 1.5f;
        [SerializeField, Min(1f)] private float m_groundProbeHeight = 40f;
        [SerializeField, Min(1f)] private float m_groundProbeDistance = 100f;

        private readonly SyncVar<int> m_currency = new();
        private readonly SyncVar<DeliveryState> m_state = new(DeliveryState.Unavailable);
        private readonly SyncVar<DeliveryObjectiveStep> m_objectiveStep = new(DeliveryObjectiveStep.None);
        private readonly SyncVar<int> m_activeStageIndex = new(-1);
        private readonly SyncVar<int> m_attemptNumber = new();
        private readonly SyncVar<int> m_lastReward = new();
        private readonly SyncVar<int> m_deliveryFailureSequence = new();
        private readonly SyncVar<int> m_activeTierIndex = new(-1);
        private readonly SyncVar<bool> m_exitDoorOpen = new();
        private readonly SyncVar<bool> m_vehicleGarageDoorOpen = new();

        private readonly List<PedestrianController> m_recipients = new();

        private readonly HashSet<int> m_usedDestinationIndices = new HashSet<int>();
        private readonly List<int>[] m_tierCandidates =
        {
            new List<int>(), new List<int>(), new List<int>()
        };
        // Ownership is server-only; replicate activity only on open/close for spectators and late joiners.
        private readonly SyncVar<bool> m_dispatcherSpeaking = new();
        public bool IsDispatcherSpeaking => m_dispatcherSpeaking.Value;
        private sealed class DispatcherSession
        {
            public DeliveryNPC Npc;
            public uint LastPurchaseRequest;
            public uint Id;
        }
        // A separate server-authorised session for every player; cosmetic state stays local.
        private readonly Dictionary<PlayerCouchCarrier, DispatcherSession> m_dialogues = new();
        private uint m_nextDialogueId;
        private bool m_refreshDialogues;
        public bool IsDispatcherInDialogue(DeliveryNPC npc) =>
            npc != null && m_dialogues.Values.Any(session => session.Npc == npc);
        private bool m_candidatesReady;
        private readonly Collider[] m_vehicleClearanceHits = new Collider[64];
        private DepotController m_depot;

        public event Action<int> CurrencyChanged;
        public event Action<DeliveryState> StateChanged;
        public event Action<DeliveryObjectiveStep> ObjectiveStepChanged;

        // Compatibility views for consumers that only need a general activity signal.
        public NetworkObject ActiveCouch => m_deliveries.Count > 0 ? m_deliveries[0].Couch : null;
        public int ActiveDestinationIndex => m_deliveries.Count > 0 ? m_deliveries[0].DestinationIndex : -1;
        public int CurrentCurrency => m_currency.Value;
        public DeliveryState State => m_state.Value;
        public DeliveryObjectiveStep ObjectiveStep => m_objectiveStep.Value;
        public int ActiveStageIndex => m_activeStageIndex.Value;
        public int AttemptNumber => m_attemptNumber.Value;
        public int LastReward => m_lastReward.Value;
        public int DeliveryFailureSequence => m_deliveryFailureSequence.Value;
        public int ActiveTierIndex => m_deliveries.Count > 0 ? m_deliveries[0].TierIndex : -1;
        public bool HasActiveDelivery => m_deliveries.Count > 0;
        public ShopCatalogue ShopCatalogue => m_shopCatalogue;
        public DeliveryDestination ActiveDestination => GetDestination(ActiveDestinationIndex);

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
            m_chapterManager ??= GetComponent<SuburbsChapterManager>();
            m_shopCatalogue ??= Resources.Load<ShopCatalogue>("Shop/DeliveryManagerShop");
            m_currency.OnChange += OnCurrencyChanged;
            m_state.OnChange += OnStateChanged;
            m_objectiveStep.OnChange += OnObjectiveStepChanged;
            m_exitDoorOpen.OnChange += OnExitDoorOpenChanged;
            m_vehicleGarageDoorOpen.OnChange += OnVehicleGarageDoorOpenChanged;
            m_deliveries.OnChange += OnDeliveriesChanged;
            m_generator.RegionGenerated += OnRegionGenerated;
            m_generator.RegionClearing += OnRegionClearing;
        }

        private void OnDestroy()
        {
            m_deliveries.OnChange -= OnDeliveriesChanged;
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
            if (IsServerInitialized)
                foreach (var player in m_dialogues.Keys.ToArray())
                    ReleaseDialogueLockServer(player, true, "Delivery Manager unavailable.");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DeliveriesChanged?.Invoke();

            CurrencyChanged?.Invoke(m_currency.Value);
            StateChanged?.Invoke(m_state.Value);
            ObjectiveStepChanged?.Invoke(m_objectiveStep.Value);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_dispatcherSpeaking.Value = false;
            m_dialogues.Clear();
            ClearDeliveriesServer();
            m_usedDestinationIndices.Clear();
            m_exitDoorOpen.Value = false;
            m_vehicleGarageDoorOpen.Value = false;
            m_state.Value = DeliveryState.Available;
            m_objectiveStep.Value = DeliveryObjectiveStep.AcceptDelivery;
            m_activeStageIndex.Value = -1;
            m_attemptNumber.Value = 0;
            m_lastReward.Value = 0;
            m_activeTierIndex.Value = -1;

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
            if (IsServerInitialized) UpdateDeliveriesServer();
            if (IsServerInitialized)
            {
                foreach (var pair in m_dialogues.ToArray())
                {
                    var player = pair.Key;
                    if (player == null || !player.IsSpawned || !player.Owner.IsValid ||
                        pair.Value.Npc == null ||
                        (player.TryGetComponent(out PlayerHealth health) && !health.IsAlive) ||
                        pair.Value.Npc.InteractionDistanceSquared(player.transform.position) >
                            m_dialogueMaximumDistance * m_dialogueMaximumDistance)
                        ReleaseDialogueLockServer(player, true, "Dispatcher interaction ended.");
                }
                if (m_refreshDialogues)
                {
                    m_refreshDialogues = false;
                    foreach (var player in m_dialogues.Keys.ToArray()) SendDialogueState(player);
                }
            }
            if (IsServerInitialized && m_depot != null)
            {
                bool entranceOpen = m_depot.ShouldOpenAutomaticEntrance();
                if (m_exitDoorOpen.Value != entranceOpen)
                    m_exitDoorOpen.Value = entranceOpen;
                if (m_depot.IsDoorwayBlocked(DepotDoorKind.VehicleGarage)) m_depot.HoldGarageDoorOpen();
                bool garageOpen = m_depot.ShouldOpenAutomaticGarage();
                if (m_vehicleGarageDoorOpen.Value != garageOpen)
                    m_vehicleGarageDoorOpen.Value = garageOpen;
            }
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized) return;
            for (int i = m_deliveries.Count - 1; i >= 0; i--)
            {
                DeliveryInstance job = m_deliveries[i];
                if (job.Status != DeliveryState.Transport || job.Couch == null ||
                    !job.Couch.TryGetComponent(out CouchCarryController couch) || couch.ServerCarrierCount <= 0) continue;
                DeliveryDestination destination = GetDestination(job.DestinationIndex);
                if (destination == null || !destination.CanReceiveDelivery) continue;
                if (m_jobs.TryGetValue(job.Id, out JobRuntime runtime) && runtime.Recipient != null && !runtime.Recipient.IsAlive) continue;
                Vector3 offset = couch.transform.position - destination.DropPosition.position;
                offset.y = 0f;
                if (offset.sqrMagnitude <= m_collectionRadius * m_collectionRadius) ResolveDeliveryServer(job.Id, true);
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
            if (npc.InteractionDistanceSquared(player.transform.position) > m_dialogueMaximumDistance * m_dialogueMaximumDistance)
                return false;
            if (player.TryGetComponent(out PlayerHealth health) && !health.IsAlive) return false;
            if (!m_dialogues.TryGetValue(player, out var session))
            {
                session = new DispatcherSession { Npc = npc, Id = ++m_nextDialogueId };
                m_dialogues.Add(player, session);
            }
            m_dispatcherSpeaking.Value = true;
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
            if (ReferenceEquals(player, null) || !m_dialogues.Remove(player)) return;
            m_dispatcherSpeaking.Value = m_dialogues.Count > 0;
            if (closeClient && player != null && player.Owner.IsValid)
                player.CloseDeliveryDialogueTargetRpc(player.Owner, message);
        }

        [Server]
        public void NotifyNpcUnavailableServer(DeliveryNPC npc)
        {
            foreach (var pair in m_dialogues.ToArray())
                if (pair.Value.Npc == npc)
                    ReleaseDialogueLockServer(pair.Key, true, "Delivery Manager unavailable.");
        }

        [Server]
        public bool TryPurchaseDeliveryServer(PlayerCouchCarrier player, int tierIndex)
        {
            m_chapterManager ??= GetComponent<SuburbsChapterManager>();
            if (!IsServerInitialized || player == null || !m_dialogues.TryGetValue(player, out var session)) return false;
            DeliveryNPC npc = session.Npc;
            DeliveryTierConfig tier = GetTierConfig(tierIndex);
            if (npc == null || tier == null) return false;
            if (npc.InteractionDistanceSquared(player.transform.position) >
                m_dialogueMaximumDistance * m_dialogueMaximumDistance)
            {
                ReleaseDialogueLockServer(player, true, "You moved too far from the Delivery Manager.");
                return false;
            }

            if (player.TryGetComponent(out PlayerHealth health) && !health.IsAlive)
            {
                return false;
            }

            if (!TryStartDeliveryServer(npc, tierIndex))
            {
                SendDialogueState(player);
                return false;
            }
            SendDialogueState(player, $"Started {tier.DisplayName}", purchaseSucceeded: true);
            m_refreshDialogues = true;
            return true;
        }

        // Shared validated purchase path for dispatcher interaction and authoritative AI.
        [Server]
        public bool TryStartDeliveryServer(DeliveryNPC npc, int tierIndex)
        {
            m_chapterManager ??= GetComponent<SuburbsChapterManager>();
            DeliveryTierConfig tier = GetTierConfig(tierIndex);
            if (!IsServerInitialized || npc == null || !npc.isActiveAndEnabled || npc.Manager != this || tier == null ||
                m_couchPrefab == null || m_couchPrefab.GetComponent<CouchCarryController>() == null ||
                m_currency.Value < tier.PurchasePrice) return false;
            EnsureCandidateCache();
            int destinationIndex = SelectDestinationIndex(tierIndex);
            if (destinationIndex < 0) return false;
            BeginDeliveryServer(tierIndex, destinationIndex, npc);
            m_currency.Value -= tier.PurchasePrice;
            m_refreshDialogues = true;
            return true;
        }

        [Server]
        public bool TryPurchaseShopItemServer(
            PlayerCouchCarrier player,
            string itemId,
            uint requestId)
        {
            if (!IsServerInitialized || player == null || !m_dialogues.TryGetValue(player, out var session)) return false;
            DeliveryNPC npc = session.Npc;
            if (npc == null) return false;

            // A monotonically increasing client request ID makes retransmitted or
            // duplicated button input harmless while the dialogue remains open.
            if (requestId == 0 || requestId <= session.LastPurchaseRequest)
                return false;
            session.LastPurchaseRequest = requestId;

            if (npc.InteractionDistanceSquared(player.transform.position) >
                m_dialogueMaximumDistance * m_dialogueMaximumDistance)
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
            SendDialogueState(player, $"Purchased {item.DisplayName}", true, purchaseSucceeded: true);
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
            // The saved depot floor is authoritative for indoor spawn markers.
            // A probe from above the building must not choose its roof or skylights.
            if (m_depot != null && m_depot.VehicleSpawnSurface != null)
                return m_depot.VehicleSpawnSurface.Raycast(new Ray(probeOrigin, Vector3.down),
                    out ground, m_vehicleGroundProbeDistance);
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
            if (!IsServerInitialized || requestedCouch == null ||
                !TryGetDelivery(requestedCouch.DeliveryId, out DeliveryInstance job) ||
                job.Couch != requestedCouch.NetworkObject || requestedCouch.ServerCarrierCount <= 0)
            {
                return false;
            }

            CouchCarryController couch = requestedCouch;
            DeliveryDestination destination = GetDestination(job.DestinationIndex);
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
                if (!open && m_depot.ShouldOpenAutomaticGarage()) return false;
                if (open) m_depot.HoldGarageDoorOpen();
                m_vehicleGarageDoorOpen.Value = open;
            }
            return true;
        }

        internal static IEnumerator MoveCouchAlongConveyor(Rigidbody body,
            IReadOnlyList<Transform> path, float speed)
        {
            if (body == null || path == null) yield break;
            speed = Mathf.Max(.1f, speed);
            for (int index = 0; index < path.Count; index++)
            {
                Transform waypoint = path[index];
                if (waypoint == null) continue;
                if (body == null) yield break;
                Vector3 start = body.position;
                Quaternion startRotation = body.rotation;
                Vector3 target = waypoint.position;
                Quaternion targetRotation = waypoint.rotation;
                float duration = Vector3.Distance(start, target) / speed;
                float elapsed = 0f;
                // Interpolated render transforms can lag physics. A timed segment
                // cannot wait forever for that displayed pose to reach a waypoint.
                while (elapsed < duration)
                {
                    if (body == null) yield break;
                    elapsed = Mathf.Min(duration, elapsed + Time.fixedDeltaTime);
                    body.MovePosition(Vector3.Lerp(start, target, elapsed / duration));
                    body.MoveRotation(Quaternion.Slerp(startRotation, targetRotation, elapsed / duration));
                    yield return new WaitForFixedUpdate();
                }
                body.position = target;
                body.rotation = targetRotation;
            }
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
            ClearDeliveriesServer();
            ClearGetawaysServer();
            ClearRecipientsServer();
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
            if (unused.Count == 0) return -1;
            List<int> pool = unused;
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
            bool openShop = false,
            bool purchaseSucceeded = false)
        {
            if (player == null || !player.Owner.IsValid || !m_dialogues.TryGetValue(player, out var session) || session.Npc == null) return;
            EnsureCandidateCache();
            bool[] available = new bool[3];
            string[] reasons = new string[3];
            for (int index = 0; index < 3; index++)
            {
                DeliveryTierConfig tier = GetTierConfig(index);
                if (tier == null) reasons[index] = "Not configured";
                else if (SelectDestinationIndex(index) < 0) reasons[index] = $"No available {tier.Tier} delivery houses";
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
                else if (!item.PurchaseBehaviour.CanPurchase(this, session.Npc, player, out string reason))
                    shopReasons[index] = string.IsNullOrWhiteSpace(reason) ? "Currently unavailable" : reason;
                else shopAvailable[index] = true;
            }

            player.OpenDeliveryDialogueTargetRpc(player.Owner, session.Npc.transform.position, session.Id,
                available[0], reasons[0], available[1], reasons[1], available[2], reasons[2],
                shopAvailable, shopReasons, m_currency.Value, notice, openShop, purchaseSucceeded);
        }

        private void OnCurrencyChanged(int previous, int next, bool asServer)
        {
            CurrencyChanged?.Invoke(next);
            if (asServer) m_refreshDialogues = true;
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

        internal void RecipientDiedServer(PedestrianController recipient)
        {
            if (!IsServerInitialized) return;
            foreach (var entry in m_jobs)
                if (entry.Value.Recipient == recipient)
                {
                    entry.Value.Recipient = null;
                    ResolveDeliveryServer(entry.Key, false);
                    break;
                }
        }

        private void ClearRecipientsServer()
        {
            if (!IsServerInitialized) return;
            foreach (var recipient in m_recipients)
                if (recipient != null && recipient.IsSpawned) Despawn(recipient.NetworkObject);
            m_recipients.Clear();
        }

        public override void OnStopServer()
        {
            ClearDeliveriesServer();
            ClearGetawaysServer();
            ClearRecipientsServer();
            base.OnStopServer();
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
