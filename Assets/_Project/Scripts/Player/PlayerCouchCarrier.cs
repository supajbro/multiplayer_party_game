using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Input;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Component.Transforming;
using UnityEngine;
using UnityEngine.Serialization;

namespace CouchGuys.Player
{
    /// <summary>
    /// Handles local interaction intent and server-validated couch attachment for one Player.
    /// </summary>
    [RequireComponent(typeof(PlayerInputReader))]
    [RequireComponent(typeof(ThirdPersonPlayerController))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class PlayerCouchCarrier : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private ThirdPersonPlayerController m_playerController;

        [Header("Interaction")]
        [SerializeField, Min(0.1f)] private float m_interactionRange = 1.8f;
        [SerializeField, Min(0f)] private float m_serverRangeTolerance = 0.35f;

        [Header("Carrying")]
        [SerializeField, Min(0f)] private float m_carryDistance = 0.45f;
        [SerializeField, Min(0f)] private float m_carryHeight = 0.45f;
        [SerializeField, Range(0.1f, 1f)] private float m_singleCarrierSpeedMultiplier = 0.35f;
        [SerializeField, Range(0.1f, 1f)] private float m_maximumCooperativeSpeedMultiplier = 1f;
        [FormerlySerializedAs("m_softCarrySeparation")]
        [SerializeField, Min(0.1f)] private float m_comfortableCarryDistance = 0.65f;
        [SerializeField, Min(0.5f)] private float m_maximumCarrySeparation = 1f;

        [Header("Networked Movement Intent")]
        [SerializeField, Min(0.02f)] private float m_intentSendInterval = 0.1f;
        [SerializeField, Min(0.1f)] private float m_intentHeartbeatInterval = 0.5f;
        [SerializeField, Min(0.1f)] private float m_intentTimeout = 0.4f;

        [Header("Debug Bot")]
        [SerializeField] private DebugCouchBotSettings m_debugBot = new();

        private readonly SyncVar<NetworkObject> m_carriedCouch = new();
        private readonly SyncVar<int> m_carriedPointIndex = new(-1);
        private Vector3 m_serverMovementIntent;
        private Vector3 m_lastSentMovementIntent;
        private float m_lastIntentSendTime = float.NegativeInfinity;
        private float m_lastIntentChangeTime = float.NegativeInfinity;
        private float m_lastServerIntentTime = float.NegativeInfinity;
        private float m_appliedSpeedMultiplier = 1f;
        private NetworkObject m_cachedMovementCouchObject;
        private CouchCarryController m_cachedMovementCouch;
        private Transform m_cachedMovementAnchor;
        private int m_cachedMovementPointIndex = int.MinValue;


        public float ComfortableCarryDistance => Mathf.Min(m_comfortableCarryDistance, m_maximumCarrySeparation);
        public float MaximumCarrySeparation => m_maximumCarrySeparation;
        internal DebugCouchBotSettings DebugBotSettings => m_debugBot;
        internal int CarriedPointIndex => m_carriedPointIndex.Value;
        public bool IsCarrying => m_carriedCouch.Value != null;

        private void Awake()
        {
            m_input ??= GetComponent<PlayerInputReader>();
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
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
            if (m_input != null && m_input.TeleportToDeliveryPressedThisFrame && IsCarrying)
            {
                RequestTeleportToDeliveryServerRpc();
            }

            if (m_input != null && m_input.InteractPressedThisFrame)
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

        public override void OnStartServer() => base.OnStartServer();

        public override void OnStopServer()
        {
            ReleaseCurrentCouchServer();
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            RestoreMovementSpeed();
            base.OnStopClient();
        }

        internal Vector3 CalculateDesiredCarryPosition(Vector3 couchCentre)
        {
            Vector3 directionToCouch = Vector3.ProjectOnPlane(couchCentre - transform.position, Vector3.up);
            if (directionToCouch.sqrMagnitude < 0.001f)
            {
                directionToCouch = transform.forward;
            }

            return transform.position + directionToCouch.normalized * m_carryDistance + Vector3.up * m_carryHeight;
        }

        internal Vector3 GetServerMovementIntent()
        {
            float effectiveTimeout = Mathf.Max(
                m_intentTimeout,
                m_intentHeartbeatInterval + m_intentSendInterval);
            return Time.unscaledTime - m_lastServerIntentTime <= effectiveTimeout
                ? m_serverMovementIntent
                : Vector3.zero;
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
            return point != null && Vector3.Distance(transform.position, point.transform.position) <= allowedRange &&
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
                ? couch.CalculateCarrierSpeedMultiplier(
                    m_singleCarrierSpeedMultiplier,
                    m_maximumCooperativeSpeedMultiplier)
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

        private void TryRequestNearestInteraction()
        {
            CouchCarryPoint[] points = FindObjectsByType<CouchCarryPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            CouchCarryPoint closestPoint = null;
            float closestDistanceSquared = m_interactionRange * m_interactionRange;

            foreach (CouchCarryPoint point in points)
            {
                if (point == null || !point.IsAvailable || point.Couch == null || !point.Couch.IsSpawned)
                {
                    continue;
                }

                float distanceSquared = (point.transform.position - transform.position).sqrMagnitude;
                if (distanceSquared <= closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closestPoint = point;
                }
            }

            DeliveryNPC closestNpc = null;
            DeliveryNPC[] npcs = FindObjectsByType<DeliveryNPC>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (DeliveryNPC npc in npcs)
            {
                float distanceSquared = (npc.transform.position - transform.position).sqrMagnitude;
                if (distanceSquared <= closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closestPoint = null;
                    closestNpc = npc;
                }
            }

            if (closestPoint != null)
            {
                RequestGrabServerRpc(closestPoint.Couch.NetworkObject, closestPoint.PointIndex);
            }
            else if (closestNpc != null)
            {
                RequestDeliveryServerRpc();
            }
        }

        [ServerRpc]
        private void RequestDeliveryServerRpc()
        {
            DeliveryNPC[] npcs = FindObjectsByType<DeliveryNPC>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            DeliveryNPC closestNpc = null;
            float allowedRange = m_interactionRange + m_serverRangeTolerance;
            float closestDistanceSquared = allowedRange * allowedRange;
            foreach (DeliveryNPC npc in npcs)
            {
                float distanceSquared = (npc.transform.position - transform.position).sqrMagnitude;
                if (distanceSquared <= closestDistanceSquared)
                {
                    closestDistanceSquared = distanceSquared;
                    closestNpc = npc;
                }
            }

            closestNpc?.InteractServer(this);
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
            if (point == null || Vector3.Distance(transform.position, point.transform.position) > allowedRange)
            {
                return;
            }

            couch.TryGrabPointServer(this, pointIndex);
        }

        [ServerRpc]
        private void RequestReleaseServerRpc()
        {
            ReleaseCurrentCouchServer();
        }

        /// <summary>Temporary test-only shortcut; the DeliveryManager performs all validation.</summary>
        [ServerRpc]
        private void RequestTeleportToDeliveryServerRpc()
        {
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

        private void ReleaseCurrentCouchServer()
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
                ReleaseCurrentCouchServer();
            }
        }

        internal void ReleaseForAiServer()
        {
            if (IsServerInitialized)
            {
                ReleaseCurrentCouchServer();
            }
        }

        internal void ReleaseForStaminaServer()
        {
            if (IsServerInitialized)
            {
                ReleaseCurrentCouchServer();
            }
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
            NetworkObject couchObject = m_carriedCouch.Value;
            int pointIndex = m_carriedPointIndex.Value;
            RefreshMovementCache(couchObject, pointIndex);
            if (m_cachedMovementCouch != null)
            {
                targetMultiplier = CalculateCarryingSpeedMultiplier(m_cachedMovementCouch);

                if (m_cachedMovementAnchor != null)
                {
                    m_playerController.SetExternalFacingTarget(m_cachedMovementAnchor);
                    m_playerController.SetExternalMovementResistance(
                        m_cachedMovementAnchor,
                        ComfortableCarryDistance,
                        MaximumCarrySeparation);
                }
            }
            if (Mathf.Abs(targetMultiplier - m_appliedSpeedMultiplier) < 0.01f)
            {
                return;
            }

            m_playerController.SetExternalSpeedMultiplier(targetMultiplier);
            m_appliedSpeedMultiplier = targetMultiplier;
        }

        private void RestoreMovementSpeed()
        {
            if (m_playerController != null)
            {
                m_playerController.SetExternalSpeedMultiplier(1f);
                m_playerController.ClearExternalMovementResistance();
                m_playerController.ClearExternalFacingTarget();
            }

            m_appliedSpeedMultiplier = 1f;
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
            if (couchObject == null || !couchObject.TryGetComponent(out m_cachedMovementCouch))
            {
                return;
            }

            CouchCarryPoint point = m_cachedMovementCouch.GetPoint(pointIndex);
            m_cachedMovementAnchor = point != null ? point.transform : null;
        }


#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_maximumCarrySeparation = Mathf.Max(0.5f, m_maximumCarrySeparation);
            m_comfortableCarryDistance = Mathf.Clamp(
                m_comfortableCarryDistance,
                0.1f,
                m_maximumCarrySeparation);
            m_maximumCooperativeSpeedMultiplier = Mathf.Max(
                m_singleCarrierSpeedMultiplier,
                m_maximumCooperativeSpeedMultiplier);
            m_intentHeartbeatInterval = Mathf.Max(m_intentSendInterval, m_intentHeartbeatInterval);
            m_intentTimeout = Mathf.Max(
                m_intentTimeout,
                m_intentHeartbeatInterval + m_intentSendInterval);
        }
#endif
    }
}
