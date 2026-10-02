using CouchGuys.Gameplay.Couch;
using CouchGuys.Input;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

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
        [SerializeField, Range(0.1f, 1f)] private float m_singleCarrierSpeedMultiplier = 0.52f;
        [SerializeField, Range(0.1f, 1f)] private float m_maximumCooperativeSpeedMultiplier = 0.82f;
        [SerializeField, Min(0.1f)] private float m_softCarrySeparation = 1.6f;
        [SerializeField, Min(0.5f)] private float m_maximumCarrySeparation = 3f;

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
        private NetworkObject m_spawnedDebugBot;

        public float SoftCarrySeparation => Mathf.Min(m_softCarrySeparation, m_maximumCarrySeparation);
        public float MaximumCarrySeparation => m_maximumCarrySeparation;
        public bool IsCarrying => m_carriedCouch.Value != null;

        private void Awake()
        {
            m_input ??= GetComponent<PlayerInputReader>();
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
        }

        private void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            UpdateMovementSpeed();
            UpdateMovementIntent();
            if (m_input != null && m_input.InteractPressedThisFrame)
            {
                if (IsCarrying)
                {
                    RequestReleaseServerRpc();
                }
                else
                {
                    TryRequestNearestPoint();
                }
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (Owner.IsLocalClient && m_debugBot != null && m_debugBot.SpawnBot)
            {
                SpawnDebugBotServer();
            }
        }

        public override void OnStopServer()
        {
            ReleaseCurrentCouchServer();
            if (m_spawnedDebugBot != null && m_spawnedDebugBot.IsSpawned)
            {
                Despawn(m_spawnedDebugBot);
            }

            m_spawnedDebugBot = null;
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
            return Time.unscaledTime - m_lastServerIntentTime <= m_intentTimeout
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

        internal float CalculateSeparationInfluence(float separation)
        {
            return 1f - Mathf.InverseLerp(SoftCarrySeparation, m_maximumCarrySeparation, separation);
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

        private void TryRequestNearestPoint()
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

            if (closestPoint != null)
            {
                RequestGrabServerRpc(closestPoint.Couch.NetworkObject, closestPoint.PointIndex);
            }
        }

        [ServerRpc]
        private void RequestGrabServerRpc(NetworkObject couchObject, int pointIndex)
        {
            if (IsCarrying || couchObject == null || !couchObject.IsSpawned ||
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
            NetworkObject couchObject = m_carriedCouch.Value;
            int pointIndex = m_carriedPointIndex.Value;
            if (couchObject != null && couchObject.TryGetComponent(out CouchCarryController couch))
            {
                targetMultiplier = CalculateCarryingSpeedMultiplier(couch);

                CouchCarryPoint point = couch.GetPoint(pointIndex);
                if (point != null)
                {
                    float separation = Vector3.Distance(transform.position, point.transform.position);
                    float separationInfluence = CalculateSeparationInfluence(separation);
                    targetMultiplier *= Mathf.Lerp(0.25f, 1f, separationInfluence);
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
            }

            m_appliedSpeedMultiplier = 1f;
        }

        private void SpawnDebugBotServer()
        {
            NetworkObject playerPrefab = NetworkManager.GetPrefab(NetworkObject.PrefabId, true);
            if (playerPrefab == null)
            {
                Debug.LogError("Debug couch bot could not find the registered Player prefab.", this);
                return;
            }

            Vector3 spawnPosition = transform.position + transform.TransformDirection(m_debugBot.SpawnOffset);
            NetworkObject bot = Instantiate(playerPrefab, spawnPosition, transform.rotation);
            if (!bot.TryGetComponent(out DebugCouchBotController botController))
            {
                Debug.LogError("The Player prefab is missing DebugCouchBotController.", bot);
                Destroy(bot.gameObject);
                return;
            }

            botController.Initialise(m_debugBot);
            Spawn(bot);
            m_spawnedDebugBot = bot;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_maximumCarrySeparation = Mathf.Max(0.5f, m_maximumCarrySeparation);
            m_softCarrySeparation = Mathf.Clamp(m_softCarrySeparation, 0.1f, m_maximumCarrySeparation);
            m_maximumCooperativeSpeedMultiplier = Mathf.Max(
                m_singleCarrierSpeedMultiplier,
                m_maximumCooperativeSpeedMultiplier);
            m_intentHeartbeatInterval = Mathf.Max(m_intentSendInterval, m_intentHeartbeatInterval);
        }
#endif
    }
}
