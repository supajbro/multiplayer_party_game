using CouchGuys.Gameplay.Couch;
using CouchGuys.Input;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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
        [SerializeField, Range(0.1f, 1f)] private float m_carryingSpeedMultiplier = 0.72f;
        [SerializeField, Min(0.5f)] private float m_maximumCarrySeparation = 3f;

        private readonly SyncVar<NetworkObject> m_carriedCouch = new();
        private readonly SyncVar<int> m_carriedPointIndex = new(-1);
        private bool m_speedReduced;

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

        internal void SetCarriedCouchServer(CouchCarryController couch, int pointIndex)
        {
            if (!IsServerInitialized || couch == null)
            {
                return;
            }

            m_carriedCouch.Value = couch.NetworkObject;
            m_carriedPointIndex.Value = pointIndex;
        }

        internal void ClearCarriedCouchServer(CouchCarryController couch, int pointIndex)
        {
            if (!IsServerInitialized || m_carriedCouch.Value != couch?.NetworkObject || m_carriedPointIndex.Value != pointIndex)
            {
                return;
            }

            m_carriedCouch.Value = null;
            m_carriedPointIndex.Value = -1;
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

        private void UpdateMovementSpeed()
        {
            bool shouldReduceSpeed = IsCarrying;
            if (shouldReduceSpeed == m_speedReduced || m_playerController == null)
            {
                return;
            }

            m_playerController.SetExternalSpeedMultiplier(shouldReduceSpeed ? m_carryingSpeedMultiplier : 1f);
            m_speedReduced = shouldReduceSpeed;
        }

        private void RestoreMovementSpeed()
        {
            if (m_playerController != null)
            {
                m_playerController.SetExternalSpeedMultiplier(1f);
            }

            m_speedReduced = false;
        }
    }
}
