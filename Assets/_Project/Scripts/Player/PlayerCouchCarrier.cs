using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Input;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using FishNet.Transporting;
using FishNet.Component.Transforming;
using UnityEngine;
using CouchGuys.UI;
using System.Collections.Generic;

namespace CouchGuys.Player
{
    /// <summary>
    /// Handles local interaction intent and server-validated couch attachment for one Player.
    /// </summary>
    [RequireComponent(typeof(PlayerInputReader))]
    [RequireComponent(typeof(ThirdPersonPlayerController))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class PlayerCouchCarrier : NetworkBehaviour, ICouchCarrierServer
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private ThirdPersonPlayerController m_playerController;

        [Header("Interaction")]
        [SerializeField, Min(0.1f)] private float m_interactionRange = 1.8f;
        [SerializeField, Min(0f)] private float m_serverRangeTolerance = 0.35f;

        [Header("Networked Movement Intent")]
        [SerializeField, Min(0.02f)] private float m_intentSendInterval = 0.1f;
        [SerializeField, Min(0.1f)] private float m_intentHeartbeatInterval = 0.5f;
        [SerializeField, Min(0.1f)] private float m_intentTimeout = 0.4f;

        [Header("Debug Bot")]
        [SerializeField] private DebugCouchBotSettings m_debugBot = new();

        internal const float CarryDistance = 0.45f;
        internal const float CarryHeight = 0.45f;
        internal const float SoftConstraintStartRatio = 0.7f;
        private const float CarrierSpeedTransitionRate = 2.5f;
        internal const float CouchPushingSpeedMultiplier = 0.3f;
        private const float MinimumGrabFacingDot = 0.35f;
        private const float ServerGrabFacingTolerance = 0.15f;

        private readonly SyncVar<NetworkObject> m_carriedCouch = new();
        private readonly SyncVar<int> m_carriedPointIndex = new(-1);
        private Vector3 m_serverMovementIntent;
        private Vector3 m_lastSentMovementIntent;
        private float m_lastIntentSendTime = float.NegativeInfinity;
        private float m_lastIntentChangeTime = float.NegativeInfinity;
        private float m_lastServerIntentTime = float.NegativeInfinity;
        private float m_appliedSpeedMultiplier = 1f;
        private float m_nextServerBounceTime;
        private CharacterController m_carrierCollider;
        private CouchCarryController m_collisionIgnoredCouch;
        private NetworkObject m_cachedMovementCouchObject;
        private CouchCarryController m_cachedMovementCouch;
        private Transform m_cachedMovementAnchor;
        private int m_cachedMovementPointIndex = int.MinValue;
        private uint m_shopPurchaseRequestId;
        private uint m_closedDispatcherSession;
        private uint m_dispatcherSession;
        private DispatcherMenuController m_dispatcherMenu;
        private string m_sledInteractionPrompt = string.Empty;
        private string m_sledFeedback;
        private float m_sledFeedbackUntil;


        internal DebugCouchBotSettings DebugBotSettings => m_debugBot;
        internal int CarriedPointIndex => m_carriedPointIndex.Value;
        public bool IsCarrying => m_carriedCouch.Value != null;
        public CouchCarryController CarriedCouch => m_carriedCouch.Value != null &&
            m_carriedCouch.Value.TryGetComponent(out CouchCarryController couch) ? couch : null;
        public SledController CarriedSled => CarriedCouch != null ? CarriedCouch.GetComponent<SledController>() : null;
        public string SledInteractionPrompt => Time.unscaledTime < m_sledFeedbackUntil ? m_sledFeedback : m_sledInteractionPrompt;
        // The root can sit below the floor because the character capsule has an offset.
        // Measure reach from the body so raised handles remain within arm's reach.
        internal Vector3 SledGrabOrigin => m_carrierCollider != null
            ? transform.TransformPoint(m_carrierCollider.center)
            : transform.position + Vector3.up * CarryHeight;
        internal Vector3 GetCarryInteractionOrigin(CouchCarryController couch) =>
            couch != null && couch.IsSled ? SledGrabOrigin : transform.position;
        internal bool IsAvailableForServerCarry => IsSpawned && !IsCarrying &&
            !(TryGetComponent(out PlayerHoverboardInteractor hoverboard) && hoverboard.IsMounted) &&
            (!TryGetComponent(out PlayerHealth health) || health.IsAlive);
        internal bool IsValidServerCarrier => IsSpawned &&
            (!TryGetComponent(out PlayerHealth health) || health.IsAlive);

        NetworkObject ICouchCarrierServer.CarrierNetworkObject => NetworkObject;
        bool ICouchCarrierServer.IsCarrierAvailable => IsSpawned &&
            !(TryGetComponent(out PlayerHoverboardInteractor hoverboard) && hoverboard.IsMounted) &&
            (!TryGetComponent(out PlayerHealth health) || health.IsAlive);

        private void Awake()
        {
            m_input ??= GetComponent<PlayerInputReader>();
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
            m_carrierCollider = GetComponent<CharacterController>();
            m_carriedCouch.OnChange += OnCarriedCouchChanged;
        }

        private void Update()
        {
            if (TryGetComponent(out PlayerHealth health) && health.IsKnockedDown)
            {
                return;
            }

            if (!IsOwner)
            {
                return;
            }

            UpdateMovementSpeed();
            UpdateMovementIntent();
            UpdateSledInteractionPrompt();
            if (m_input != null && m_input.TeleportToDeliveryPressedThisFrame && IsCarrying)
            {
                RequestTeleportToDeliveryServerRpc();
            }

            PlayerTruckInteractor truckInteractor = GetComponent<PlayerTruckInteractor>();
            if (m_input != null && m_input.InteractPressedThisFrame &&
                !(TryGetComponent(out PlayerHoverboardInteractor hoverboard) && hoverboard.HasPriorityWorldInteraction()) &&
                (health == null || !health.HasReviveInteraction) &&
                (truckInteractor == null || !truckInteractor.HasPriorityWorldInteraction()) &&
                m_input.TryConsumeInteractPress())
            {
                if (IsCarrying)
                {
                    RequestReleaseServerRpc();
                }
                else
                {
                    TryRequestNearestInteraction();
                }
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            UpdateCarriedCouchCollision(m_carriedCouch.Value);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            UpdateCarriedCouchCollision(m_carriedCouch.Value);
        }

        public override void OnStopClient()
        {
            if (IsOwner)
            {
                m_dispatcherMenu?.CloseLocal(false, true);
            }
            RestoreMovementSpeed();
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            DeliveryManager deliveryManager = FindFirstObjectByType<DeliveryManager>();
            deliveryManager?.ReleaseDialogueLockServer(this);
            ReleaseCurrentCarryableServer();
            base.OnStopServer();
        }

        private void OnDestroy()
        {
            m_carriedCouch.OnChange -= OnCarriedCouchChanged;
            UpdateCarriedCouchCollision(null);
        }

        internal Vector3 CalculateDesiredCarryPosition(Vector3 couchCentre)
        {
            Vector3 directionToCouch = Vector3.ProjectOnPlane(couchCentre - transform.position, Vector3.up);
            if (directionToCouch.sqrMagnitude < 0.001f)
            {
                directionToCouch = transform.forward;
            }

            return transform.position + directionToCouch.normalized * CarryDistance + Vector3.up * CarryHeight;
        }

        private static bool IsFacingTarget(
            Vector3 playerPosition,
            Vector3 playerForward,
            Vector3 targetPosition,
            float minimumFacingDot)
        {
            Vector3 directionToTarget = Vector3.ProjectOnPlane(
                targetPosition - playerPosition,
                Vector3.up);
            Vector3 horizontalForward = Vector3.ProjectOnPlane(playerForward, Vector3.up);
            if (directionToTarget.sqrMagnitude < 0.0001f)
            {
                return true;
            }
            if (horizontalForward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            return Vector3.Dot(
                horizontalForward.normalized,
                directionToTarget.normalized) >= Mathf.Clamp(minimumFacingDot, -1f, 1f);
        }

        Vector3 ICouchCarrierServer.CalculateDesiredCarryPosition(Vector3 couchCentre) =>
            CalculateDesiredCarryPosition(couchCentre);

        internal Vector3 GetServerMovementIntent()
        {
            float effectiveTimeout = Mathf.Max(
                m_intentTimeout,
                m_intentHeartbeatInterval + m_intentSendInterval);
            return Time.unscaledTime - m_lastServerIntentTime <= effectiveTimeout
                ? m_serverMovementIntent
                : Vector3.zero;
        }

        Vector3 ICouchCarrierServer.GetServerMovementIntent() => GetServerMovementIntent();

        /// <summary>
        /// Returns the same inward-facing direction used by an owner-controlled carrier.
        /// Server-driven teammates use this instead of facing their NavMesh velocity.
        /// </summary>
        internal bool TryGetCarryFacingDirection(out Vector3 facingDirection)
        {
            Transform anchor = GetCurrentCarryAnchor();
            if (anchor == null)
            {
                facingDirection = Vector3.zero;
                return false;
            }

            facingDirection = CalculateCarryFacingDirection(
                transform.position,
                anchor.position);
            return facingDirection.sqrMagnitude > 0.0001f;
        }

        internal static Vector3 CalculateCarryFacingDirection(
            Vector3 carrierPosition,
            Vector3 anchorPosition) =>
            Vector3.ProjectOnPlane(anchorPosition - carrierPosition, Vector3.up);

        /// <summary>
        /// Exposes the same attachment band used by the local player so server-driven
        /// teammates cannot bypass it with their navigation movement.
        /// </summary>
        internal bool TryGetCarryMovementConstraint(
            out Transform anchor,
            out Vector3 outwardDirection,
            out float minimumDistance,
            out float comfortableDistance,
            out float maximumDistance,
            out float inwardSpeedMultiplier,
            out float speedMultiplier)
        {
            NetworkObject carryable = m_carriedCouch.Value;
            RefreshMovementCache(carryable, m_carriedPointIndex.Value);

            anchor = m_cachedMovementAnchor;
            minimumDistance = CarryDistance;
            inwardSpeedMultiplier = CouchPushingSpeedMultiplier;
            if (m_cachedMovementCouch != null)
            {
                maximumDistance = m_cachedMovementCouch.MaxCarryDistance;
                speedMultiplier = CalculateCarryingSpeedMultiplier(m_cachedMovementCouch);
                outwardDirection = anchor != null
                    ? Vector3.ProjectOnPlane(
                        anchor.position - m_cachedMovementCouch.transform.position,
                        Vector3.up)
                    : Vector3.zero;
            }
            else
            {
                outwardDirection = Vector3.zero;
                comfortableDistance = 0f;
                maximumDistance = 0f;
                speedMultiplier = 1f;
                return false;
            }

            comfortableDistance = maximumDistance * SoftConstraintStartRatio;
            return anchor != null;
        }

        private Transform GetCurrentCarryAnchor()
        {
            int pointIndex = m_carriedPointIndex.Value;
            if (m_carriedCouch.Value != null &&
                m_carriedCouch.Value.TryGetComponent(out CouchCarryController couch))
            {
                CouchCarryPoint point = couch.GetPoint(pointIndex);
                return point != null ? point.transform : null;
            }

            return null;
        }

        internal CouchCarryController GetCarriedCouchServer()
        {
            NetworkObject couchObject = m_carriedCouch.Value;
            return IsServerInitialized && couchObject != null &&
                couchObject.TryGetComponent(out CouchCarryController couch)
                ? couch
                : null;
        }

        internal bool TryGrabPointForDebugBotServer(CouchCarryController couch, int pointIndex)
        {
            if (!IsServerInitialized || Owner.IsValid || IsCarrying || couch == null || !couch.IsSpawned)
            {
                return false;
            }

            CouchCarryPoint point = couch.GetPoint(pointIndex);
            float allowedRange = m_interactionRange + m_serverRangeTolerance;
            float serverFacingDot = MinimumGrabFacingDot - ServerGrabFacingTolerance;
            return point != null && Vector3.Distance(GetCarryInteractionOrigin(couch), point.transform.position) <= allowedRange &&
                IsFacingTarget(
                    transform.position,
                    transform.forward,
                    couch.CouchRigidbody.worldCenterOfMass,
                    serverFacingDot) &&
                couch.TryGrabPointServer(this, pointIndex);
        }

        internal void SetDebugBotMovementIntentServer(Vector3 movementIntent)
        {
            if (!IsServerInitialized || Owner.IsValid || !IsCarrying)
            {
                return;
            }

            m_serverMovementIntent = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(movementIntent, Vector3.up),
                1f);
            m_lastServerIntentTime = Time.unscaledTime;
        }

        internal float CalculateCarryingSpeedMultiplier(CouchCarryController couch)
        {
            return couch != null
                ? couch.CalculateCarrierMovementSpeedMultiplier() * GetCouchStaminaSpeedMultiplier(couch)
                : 1f;
        }

        internal float GetCouchStaminaSpeedMultiplier(CouchCarryController couch)
        {
            return TryGetComponent(out PlayerStamina stamina) && stamina.IsExhausted
                ? couch.ExhaustedSpeedMultiplier
                : 1f;
        }

        internal void SetCarriedCouchServer(CouchCarryController couch, int pointIndex)
        {
            if (!IsServerInitialized || couch == null)
            {
                return;
            }

            m_carriedCouch.Value = couch.NetworkObject;
            m_carriedPointIndex.Value = pointIndex;
            m_serverMovementIntent = Vector3.zero;
            m_lastServerIntentTime = float.NegativeInfinity;
        }

        void ICouchCarrierServer.SetCarriedCouchServer(CouchCarryController couch, int pointIndex) =>
            SetCarriedCouchServer(couch, pointIndex);

        internal void ClearCarriedCouchServer(CouchCarryController couch, int pointIndex)
        {
            if (!IsServerInitialized || m_carriedCouch.Value != couch?.NetworkObject || m_carriedPointIndex.Value != pointIndex)
            {
                return;
            }

            m_carriedCouch.Value = null;
            m_carriedPointIndex.Value = -1;
            m_serverMovementIntent = Vector3.zero;
            m_lastServerIntentTime = float.NegativeInfinity;
        }

        void ICouchCarrierServer.ClearCarriedCouchServer(CouchCarryController couch, int pointIndex) =>
            ClearCarriedCouchServer(couch, pointIndex);

        internal SledController GetCarriedSledServer() => IsServerInitialized ? CarriedSled : null;

        internal bool TryGrabSledPointForDebugBotServer(SledController sled, int pointIndex) =>
            sled != null && TryGrabPointForDebugBotServer(sled.CarryController, pointIndex);

        private void TryRequestNearestInteraction()
        {
            FindNearestInteraction(out CouchCarryPoint closestPoint, out SledController closestUnloadSled,
                out DeliveryNPC closestNpc, out WeaponPickup closestWeapon, out bool depotDoorButton);
            if (closestPoint != null)
            {
                RequestGrabServerRpc(closestPoint.Couch.NetworkObject, closestPoint.PointIndex);
            }
            else if (closestUnloadSled != null)
            {
                RequestSledUnloadServerRpc(closestUnloadSled.NetworkObject);
            }
            else if (closestNpc != null)
            {
                RequestDeliveryServerRpc();
            }
            else if (closestWeapon != null)
            {
                RequestWeaponPickupServerRpc(closestWeapon.NetworkObject);
            }
            else if (depotDoorButton)
            {
                RequestDepotDoorServerRpc();
            }
        }

        // Share target selection with the prompt so it describes what pressing E will do.
        private void FindNearestInteraction(out CouchCarryPoint closestPoint, out SledController closestUnloadSled,
            out DeliveryNPC closestNpc, out WeaponPickup closestWeapon, out bool depotDoorButton)
        {
            closestPoint = null;
            closestUnloadSled = null;
            closestNpc = null;
            closestWeapon = null;
            depotDoorButton = false;
            var rackPickup = FindNearbyRackPickup();
            if (rackPickup != null)
            {
                closestWeapon = rackPickup;
                return;
            }
            CouchCarryPoint[] points = FindObjectsByType<CouchCarryPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float closestDistanceSquared = m_interactionRange * m_interactionRange;

            foreach (CouchCarryPoint point in points)
            {
                if (point == null || !point.IsAvailable || point.Couch == null || !point.Couch.IsSpawned)
                {
                    continue;
                }

                if (!IsFacingTarget(
                        transform.position,
                        transform.forward,
                        point.Couch.CouchRigidbody.worldCenterOfMass,
                        MinimumGrabFacingDot))
                {
                    continue;
                }

                float distanceSquared = (point.transform.position - GetCarryInteractionOrigin(point.Couch)).sqrMagnitude;
                if (distanceSquared <= closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closestPoint = point;
                    closestUnloadSled = null;
                }
            }

            foreach (SledController sled in FindObjectsByType<SledController>(FindObjectsSortMode.None))
            {
                if (!sled.IsSpawned || sled.MountedCouchCount == 0 || sled.UnloadPoint == null) continue;
                float distanceSquared = (sled.UnloadPoint.position - SledGrabOrigin).sqrMagnitude;
                if (distanceSquared > closestDistanceSquared) continue;
                closestDistanceSquared = distanceSquared;
                closestPoint = null;
                closestUnloadSled = sled;
            }

            DeliveryNPC[] npcs = FindObjectsByType<DeliveryNPC>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (DeliveryNPC npc in npcs)
            {
                float distanceSquared = npc.InteractionDistanceSquared(transform.position);
                if (distanceSquared <= closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closestPoint = null;
                    closestUnloadSled = null;
                    closestNpc = npc;
                }
            }

            foreach (WeaponPickup pickup in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
            {
                float distance = (pickup.transform.position - transform.position).sqrMagnitude;
                if (!pickup.IsSpawned || distance > closestDistanceSquared) continue;
                closestDistanceSquared = distance;
                closestWeapon = pickup;
                closestPoint = null;
                closestUnloadSled = null;
                closestNpc = null;
            }

            DepotController depot = FindFirstObjectByType<DepotController>();
            if (depot != null && depot.HasNearbyButton(transform.position,
                    Mathf.Sqrt(closestDistanceSquared), out _))
            {
                depotDoorButton = true;
                closestWeapon = null;
                closestPoint = null;
                closestUnloadSled = null;
                closestNpc = null;
            }

        }

        [ServerRpc]
        private void RequestWeaponPickupServerRpc(NetworkObject pickupObject)
        {
            if (pickupObject != null && pickupObject.TryGetComponent(out WeaponPickup pickup) &&
                TryGetComponent(out PlayerWeaponController weapons)) pickup.TryPickupServer(weapons);
        }

        private WeaponPickup FindNearbyRackPickup()
        {
            WeaponPickup nearest = null;
            float distance = Mathf.Min(m_interactionRange, 1.15f);
            foreach (var pickup in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
            {
                if (!pickup.IsSpawned || pickup.MountedSled == null ||
                    !IsFacingTarget(transform.position, transform.forward, pickup.transform.position, .6f)) continue;
                float candidate = Vector3.Distance(SledGrabOrigin, pickup.transform.position);
                if (candidate >= distance) continue;
                nearest = pickup;
                distance = candidate;
            }
            return nearest;
        }

        private string GetStoreItemHint(SledController sled) =>
            sled != null && sled.HasCargoSpace && TryGetComponent(out PlayerWeaponController weapon) && weapon.HasEquippedWeapon
                ? " | Q to store held item" : string.Empty;

        private void UpdateSledInteractionPrompt()
        {
            if (CarriedSled != null)
            {
                m_sledInteractionPrompt = "Press E to release sled";
                return;
            }
            if (IsCarrying)
            {
                var loadingSled = SledController.FindLoadingSled(transform.position + Vector3.up);
                m_sledInteractionPrompt = loadingSled == null ? "Press E to release couch" :
                    loadingSled.HasCargoSpace ? $"Press E to load couch | {loadingSled.CargoStatus}" : loadingSled.LoadingStatus;
                return;
            }
            DepotController depot = FindFirstObjectByType<DepotController>();
            if (depot != null && depot.HasNearbyButton(
                    transform.position, m_interactionRange, out _))
            {
                m_sledInteractionPrompt = "Press E to operate roller door";
                return;
            }
            var rackPickup = FindNearbyRackPickup();
            if (rackPickup != null)
            {
                m_sledInteractionPrompt = $"Press E to pick up {rackPickup.DisplayName} | {rackPickup.MountedSled.CargoStatus}";
                return;
            }
            FindNearestInteraction(out CouchCarryPoint carryPoint, out _, out _, out _, out _);
            if (carryPoint != null && !carryPoint.Couch.IsSled &&
                (!TryGetComponent(out PlayerStamina stamina) || stamina.CanAttachToCouch))
            {
                m_sledInteractionPrompt = "Press E to carry couch";
                return;
            }
            foreach (var sled in FindObjectsByType<SledController>(FindObjectsSortMode.None))
                if (sled.IsSpawned && sled.MountedCouchCount > 0 && sled.UnloadPoint != null &&
                    Vector3.Distance(SledGrabOrigin, sled.UnloadPoint.position) <= m_interactionRange &&
                    sled.GetPoint(0) != null &&
                    Vector3.Distance(SledGrabOrigin, sled.UnloadPoint.position) <
                    System.Math.Min(Vector3.Distance(SledGrabOrigin, sled.GetPoint(0).transform.position),
                        Vector3.Distance(SledGrabOrigin, sled.GetPoint(1).transform.position)))
                {
                    m_sledInteractionPrompt = $"Press E to unload top couch | {sled.LoadingStatus}" + GetStoreItemHint(sled);
                    return;
                }
            float rangeSquared = m_interactionRange * m_interactionRange;
            foreach (SledGrabPoint point in FindObjectsByType<SledGrabPoint>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (point != null && point.IsAvailable &&
                    (point.transform.position - SledGrabOrigin).sqrMagnitude <= rangeSquared)
                {
                    m_sledInteractionPrompt = $"Press E to grab sled | {point.Sled.LoadingStatus}" + GetStoreItemHint(point.Sled);
                    return;
                }
            }
            var nearbySled = SledController.FindLoadingSled(transform.position + Vector3.up);
            m_sledInteractionPrompt = nearbySled != null ? nearbySled.LoadingStatus : string.Empty;
            if (nearbySled != null && TryGetComponent(out PlayerWeaponController weapons) && weapons.HasEquippedWeapon)
                m_sledInteractionPrompt += nearbySled.HasCargoSpace ? " | Q to store held item" : string.Empty;
            foreach (WeaponPickup pickup in FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None))
            {
                if (pickup.IsSpawned && (pickup.transform.position - transform.position).sqrMagnitude <= rangeSquared)
                {
                    m_sledInteractionPrompt = $"Press E to pick up {pickup.DisplayName}";
                    break;
                }
            }
        }

        [ServerRpc]
        private void RequestDepotDoorServerRpc()
        {
            if (TryGetComponent(out PlayerHealth health) && health.IsKnockedDown) return;
            DeliveryManager deliveryManager = FindFirstObjectByType<DeliveryManager>();
            deliveryManager?.TryToggleNearbyDepotDoorServer(
                this, m_interactionRange + m_serverRangeTolerance);
        }

        [ServerRpc]
        private void RequestDeliveryServerRpc()
        {
            if (TryGetComponent(out PlayerHealth health) && health.IsKnockedDown) return;
            DeliveryNPC[] npcs = FindObjectsByType<DeliveryNPC>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            DeliveryNPC closestNpc = null;
            float allowedRange = m_interactionRange + m_serverRangeTolerance;
            float closestDistanceSquared = allowedRange * allowedRange;
            foreach (DeliveryNPC npc in npcs)
            {
                float distanceSquared = npc.InteractionDistanceSquared(transform.position);
                if (distanceSquared <= closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closestNpc = npc;
                }
            }

            closestNpc?.InteractServer(this);
        }

        [ServerRpc]
        private void RequestDeliveryPurchaseServerRpc(int tierIndex)
        {
            if (TryGetComponent(out PlayerHealth health) && health.IsKnockedDown) return;
            DeliveryManager deliveryManager = FindFirstObjectByType<DeliveryManager>();
            deliveryManager?.TryPurchaseDeliveryServer(this, tierIndex);
        }

        [ServerRpc]
        private void RequestShopPurchaseServerRpc(string itemId, uint requestId)
        {
            if (TryGetComponent(out PlayerHealth health) && health.IsKnockedDown) return;
            DeliveryManager deliveryManager = FindFirstObjectByType<DeliveryManager>();
            deliveryManager?.TryPurchaseShopItemServer(this, itemId, requestId);
        }

        [ServerRpc]
        private void RequestCloseDialogueServerRpc()
        {
            DeliveryManager deliveryManager = FindFirstObjectByType<DeliveryManager>();
            deliveryManager?.ReleaseDialogueLockServer(this);
        }

        [TargetRpc]
        public void OpenDeliveryDialogueTargetRpc(NetworkConnection connection, Vector3 dispatcherPosition, uint sessionId,
            bool easyAvailable, string easyReason, bool mediumAvailable, string mediumReason,
            bool hardAvailable, string hardReason,
            bool[] shopAvailable, string[] shopReasons, int currentCurrency,
            string notice, bool openShop, bool purchaseSucceeded)
        {
            if (!IsOwner) return;
            if (sessionId <= m_closedDispatcherSession || (m_dispatcherMenu != null && m_dispatcherMenu.IsClosing)) return;
            DeliveryManager manager = FindFirstObjectByType<DeliveryManager>();
            List<DialogueShopOption> deliveryOptions = new();
            bool[] available = { easyAvailable, mediumAvailable, hardAvailable };
            string[] reasons = { easyReason, mediumReason, hardReason };
            for (int index = 0; index < 3; index++)
            {
                int capturedTier = index;
                DeliveryTierConfig config = manager != null ? manager.GetTierConfig(index) : null;
                if (config == null) continue;
                deliveryOptions.Add(new DialogueShopOption(
                    config.DisplayName,
                    config.Description,
                    config.PurchasePrice == 0 ? "FREE" : $"${config.PurchasePrice:N0}",
                    $"${config.Reward:N0}",
                    config.Colour,
                    available[index],
                    reasons[index],
                    () => RequestDeliveryPurchaseServerRpc(capturedTier)));
            }

            List<DialogueShopOption> shopOptions = new();
            int shopCount = manager != null && manager.ShopCatalogue != null
                ? manager.ShopCatalogue.Items.Count
                : 0;
            for (int index = 0; index < shopCount; index++)
            {
                ShopItemDefinition item = manager.GetShopItem(index);
                if (item == null) continue;
                string capturedItemId = item.ItemId;
                bool itemAvailable = shopAvailable != null && index < shopAvailable.Length &&
                                     shopAvailable[index];
                string reason = shopReasons != null && index < shopReasons.Length
                    ? shopReasons[index]
                    : "Currently unavailable";
                shopOptions.Add(new DialogueShopOption(
                    item.DisplayName,
                    item.Description,
                    item.Price == 0 ? "FREE" : $"${item.Price:N0}",
                    string.Empty,
                    new Color(0.18f, 0.52f, 0.72f),
                    itemAvailable,
                    reason,
                    () =>
                    {
                        m_shopPurchaseRequestId++;
                        if (m_shopPurchaseRequestId == 0) m_shopPurchaseRequestId++;
                        RequestShopPurchaseServerRpc(capturedItemId, m_shopPurchaseRequestId);
                    },
                    item.Icon,
                    item.Category switch
                    {
                        ShopItemCategory.Vehicle => "Vehicles",
                        ShopItemCategory.Weapon => "Weapons",
                        ShopItemCategory.Equipment => "Upgrades",
                        ShopItemCategory.PlayerUpgrade => "Upgrades",
                        _ => "Other"
                    }));
            }

            List<DialogueShopCategory> categories = new()
            {
                new DialogueShopCategory("Deliveries", deliveryOptions),
                new DialogueShopCategory("Shop", shopOptions)
            };

            if (m_dispatcherMenu == null || m_dispatcherSession != sessionId)
            {
                float nearest = float.PositiveInfinity;
                foreach (var npc in FindObjectsByType<DeliveryNPC>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    float distance = (npc.transform.position - dispatcherPosition).sqrMagnitude;
                    if (distance >= nearest || !npc.TryGetComponent(out DispatcherMenuController menu)) continue;
                    nearest = distance;
                    m_dispatcherMenu = menu;
                }
            }
            m_dispatcherSession = sessionId;
            if (m_dispatcherMenu == null || !m_dispatcherMenu.OpenLocal(this, categories, currentCurrency, notice, () =>
                {
                    m_closedDispatcherSession = sessionId;
                    RequestCloseDialogueServerRpc();
                }))
            {
                m_closedDispatcherSession = sessionId;
                RequestCloseDialogueServerRpc();
            }
            else if (purchaseSucceeded) m_dispatcherMenu.ShowPurchaseConfirmation(notice);
        }

        [TargetRpc]
        public void CloseDeliveryDialogueTargetRpc(NetworkConnection connection, string message)
        {
            if (!IsOwner) return;
            m_closedDispatcherSession = m_dispatcherSession;
            m_dispatcherMenu?.CloseLocal(false);
            if (!string.IsNullOrWhiteSpace(message)) Debug.Log(message, this);
        }

        [TargetRpc]
        public void DeliveryNpcBusyTargetRpc(NetworkConnection connection)
        {
            if (IsOwner) DialogueShopUI.Instance.ShowNotice("The Delivery Manager is currently busy.");
        }

        [ServerRpc]
        private void RequestGrabServerRpc(NetworkObject couchObject, int pointIndex)
        {
            if ((TryGetComponent(out PlayerHealth health) && health.IsKnockedDown) ||
                (TryGetComponent(out PlayerStamina stamina) && !stamina.CanAttachToCouch) ||
                IsCarrying || couchObject == null || !couchObject.IsSpawned ||
                !couchObject.TryGetComponent(out CouchCarryController couch))
            {
                return;
            }

            CouchCarryPoint point = couch.GetPoint(pointIndex);
            float allowedRange = m_interactionRange + m_serverRangeTolerance;
            float serverFacingDot = MinimumGrabFacingDot - ServerGrabFacingTolerance;
            if (point == null || Vector3.Distance(GetCarryInteractionOrigin(couch), point.transform.position) > allowedRange ||
                !IsFacingTarget(
                    transform.position,
                    transform.forward,
                    couch.CouchRigidbody.worldCenterOfMass,
                    serverFacingDot))
            {
                return;
            }

            couch.TryGrabPointServer(this, pointIndex);
        }

        [ServerRpc]
        private void RequestSledUnloadServerRpc(NetworkObject sledObject)
        {
            if (IsCarrying || sledObject == null || !sledObject.IsSpawned ||
                !sledObject.TryGetComponent(out SledController sled) || sled.UnloadPoint == null ||
                (TryGetComponent(out PlayerHealth health) && !health.IsAlive) ||
                Vector3.Distance(SledGrabOrigin, sled.UnloadPoint.position) > m_interactionRange + m_serverRangeTolerance) return;
            if (sled.MountedCouchCount == 0) return;
            if (!sled.TryUnloadTopCouchServer(out string reason) && Owner.IsValid)
                ShowSledFeedbackTargetRpc(Owner, reason);
        }

        [TargetRpc]
        private void ShowSledFeedbackTargetRpc(NetworkConnection connection, string message)
        {
            if (!IsOwner) return;
            m_sledFeedback = message;
            m_sledFeedbackUntil = Time.unscaledTime + 2.5f;
        }

        [ServerRpc]
        private void RequestReleaseServerRpc()
        {
            var couch = GetCarriedCouchServer();
            var sled = couch != null && !couch.IsSled
                ? SledController.FindLoadingSled(transform.position + Vector3.up) : null;
            ReleaseCurrentCarryableServer();
            // Only the final carrier's explicit release loads the couch. No forced releases.
            if (sled != null && couch != null && couch.ActiveCarrierCount == 0)
            {
                if (!sled.HasCargoSpace && Owner.IsValid)
                    ShowSledFeedbackTargetRpc(Owner, "Sled full. Couch released.");
                else sled.TryLoadCouchServer(couch);
            }
        }

        [TargetRpc]
        private void DialogueShopNoticeTargetRpc(NetworkConnection connection, string message) =>
            DialogueShopUI.Instance.ShowNotice(message);

        /// <summary>Temporary test-only shortcut; the DeliveryManager performs all validation.</summary>
        [ServerRpc]
        private void RequestTeleportToDeliveryServerRpc()
        {
            if (TryGetComponent(out PlayerHealth health) && health.IsKnockedDown) return;
            DeliveryManager deliveryManager = FindFirstObjectByType<DeliveryManager>();
            deliveryManager?.TryTeleportActiveDeliveryServer(this);
        }

        internal void TeleportWithCouchServer(Vector3 position, Quaternion rotation)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            ApplyTeleport(position, rotation);
            ApplyTeleportObserversRpc(position, rotation);
        }

        [ObserversRpc(ExcludeServer = true)]
        private void ApplyTeleportObserversRpc(Vector3 position, Quaternion rotation)
        {
            if (IsOwner)
            {
                ApplyTeleport(position, rotation);
            }
        }

        private void ApplyTeleport(Vector3 position, Quaternion rotation)
        {
            m_playerController?.Teleport(position, rotation);
            NetworkTransform networkTransform = GetComponent<NetworkTransform>();
            networkTransform?.Teleport();
        }

        private void ReleaseCurrentCarryableServer()
        {
            NetworkObject couchObject = m_carriedCouch.Value;
            int pointIndex = m_carriedPointIndex.Value;
            if (couchObject != null && couchObject.TryGetComponent(out CouchCarryController couch))
            {
                couch.ReleasePointServer(pointIndex, this);
            }
            else if (IsServerInitialized)
            {
                m_carriedCouch.Value = null;
                m_carriedPointIndex.Value = -1;
            }
        }

        internal void ReleaseForDamageServer()
        {
            if (IsServerInitialized)
            {
                ReleaseCurrentCarryableServer();
            }
        }

        internal void ReleaseForAiServer()
        {
            if (IsServerInitialized)
            {
                ReleaseCurrentCarryableServer();
            }
        }

        internal void ReleaseForStaminaServer()
        {
            if (IsServerInitialized)
            {
                ReleaseCurrentCarryableServer();
            }
        }

        internal void NotifyCouchBounce(
            CouchCarryController couch,
            float landingSpeed,
            Vector3 playerVelocity)
        {
            if (!IsOwner || couch == null || couch.IsSled || couch.IsLoaded)
            {
                return;
            }

            RequestCouchBounceServerRpc(
                couch.NetworkObject,
                Mathf.Clamp(landingSpeed, 0f, 15f),
                Vector3.ClampMagnitude(playerVelocity, 15f));
        }

        [ServerRpc]
        private void RequestCouchBounceServerRpc(
            NetworkObject couchObject,
            float landingSpeed,
            Vector3 playerVelocity)
        {
            if (Time.unscaledTime < m_nextServerBounceTime || couchObject == null ||
                !couchObject.IsSpawned ||
                !couchObject.TryGetComponent(out CouchCarryController couch) || couch.IsSled || couch.IsLoaded ||
                landingSpeed < 1.5f)
            {
                return;
            }

            Vector3 couchToPlayer = transform.position - couch.transform.position;
            Vector3 horizontalOffset = Vector3.ProjectOnPlane(couchToPlayer, Vector3.up);
            if (horizontalOffset.sqrMagnitude > 1.8f * 1.8f ||
                couchToPlayer.y < 0.25f || couchToPlayer.y > 2.4f)
            {
                return;
            }

            m_nextServerBounceTime = Time.unscaledTime + 0.18f;
            couch.ApplyPlayerBounceServer(
                Mathf.Clamp(landingSpeed, 0f, 15f),
                Vector3.ClampMagnitude(playerVelocity, 15f));
        }

        private void UpdateMovementIntent()
        {
            if (!IsCarrying || m_playerController == null)
            {
                return;
            }

            Vector3 movementIntent = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(m_playerController.MovementIntent, Vector3.up),
                1f);
            float now = Time.unscaledTime;
            bool changed = (movementIntent - m_lastSentMovementIntent).sqrMagnitude >= 0.01f;
            bool heartbeatDue = now - m_lastIntentChangeTime >= m_intentHeartbeatInterval;
            if (now - m_lastIntentSendTime < m_intentSendInterval || (!changed && !heartbeatDue))
            {
                return;
            }

            SubmitMovementIntentServerRpc(movementIntent);
            m_lastSentMovementIntent = movementIntent;
            m_lastIntentSendTime = now;
            m_lastIntentChangeTime = now;
        }

        [ServerRpc]
        private void SubmitMovementIntentServerRpc(
            Vector3 movementIntent,
            Channel channel = Channel.Unreliable)
        {
            if (!IsCarrying)
            {
                return;
            }

            m_serverMovementIntent = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(movementIntent, Vector3.up),
                1f);
            m_lastServerIntentTime = Time.unscaledTime;
        }

        private void UpdateMovementSpeed()
        {
            if (m_playerController == null)
            {
                return;
            }

            float targetMultiplier = 1f;
            m_playerController.ClearExternalMovementResistance();
            m_playerController.ClearExternalFacingTarget();
            m_playerController.SetExternalRotationLocked(false);
            if (TryGetCarryMovementConstraint(
                    out Transform anchor,
                    out Vector3 outwardDirection,
                    out float minimumDistance,
                    out float comfortableDistance,
                    out float maximumDistance,
                    out float inwardSpeedMultiplier,
                    out float carrySpeedMultiplier))
            {
                targetMultiplier = carrySpeedMultiplier;
                m_playerController.SetExternalFacingTarget(anchor);
                m_playerController.SetExternalMovementResistance(
                    anchor,
                    outwardDirection,
                    minimumDistance,
                    comfortableDistance,
                    maximumDistance,
                    inwardSpeedMultiplier);
            }
            float nextMultiplier = Mathf.MoveTowards(
                m_appliedSpeedMultiplier,
                targetMultiplier,
                CarrierSpeedTransitionRate * Time.deltaTime);
            if (Mathf.Approximately(nextMultiplier, m_appliedSpeedMultiplier))
            {
                return;
            }

            m_playerController.SetExternalSpeedMultiplier(nextMultiplier);
            m_appliedSpeedMultiplier = nextMultiplier;
        }

        private void RestoreMovementSpeed()
        {
            if (m_playerController != null)
            {
                m_playerController.SetExternalSpeedMultiplier(1f);
                m_playerController.ClearExternalMovementResistance();
                m_playerController.ClearExternalFacingTarget();
                m_playerController.SetExternalRotationLocked(false);
            }

            m_appliedSpeedMultiplier = 1f;
        }

        private void OnCarriedCouchChanged(NetworkObject previous, NetworkObject next, bool asServer)
        {
            UpdateCarriedCouchCollision(next);
        }

        private void UpdateCarriedCouchCollision(NetworkObject couchObject)
        {
            CouchCarryController nextCouch = null;
            if (couchObject != null)
            {
                couchObject.TryGetComponent(out nextCouch);
            }

            if (nextCouch == m_collisionIgnoredCouch)
            {
                return;
            }

            if (m_collisionIgnoredCouch != null)
            {
                m_collisionIgnoredCouch.SetCarrierCollisionIgnored(m_carrierCollider, false);
            }

            m_collisionIgnoredCouch = nextCouch;
            if (m_collisionIgnoredCouch != null)
            {
                m_collisionIgnoredCouch.SetCarrierCollisionIgnored(m_carrierCollider, true);
            }
        }

        private void RefreshMovementCache(NetworkObject couchObject, int pointIndex)
        {
            if (couchObject == m_cachedMovementCouchObject && pointIndex == m_cachedMovementPointIndex)
            {
                return;
            }

            m_cachedMovementCouchObject = couchObject;
            m_cachedMovementPointIndex = pointIndex;
            m_cachedMovementCouch = null;
            m_cachedMovementAnchor = null;
            if (couchObject == null)
            {
                return;
            }
            if (couchObject.TryGetComponent(out m_cachedMovementCouch))
            {
                CouchCarryPoint point = m_cachedMovementCouch.GetPoint(pointIndex);
                m_cachedMovementAnchor = point != null ? point.transform : null;
            }

        }


#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_intentHeartbeatInterval = Mathf.Max(m_intentSendInterval, m_intentHeartbeatInterval);
            m_intentTimeout = Mathf.Max(
                m_intentTimeout,
                m_intentHeartbeatInterval + m_intentSendInterval);
        }
#endif
    }
}
