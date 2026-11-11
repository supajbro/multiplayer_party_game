using CouchGuys.Player;
using CouchGuys.Gameplay.Depot;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Local interaction marker for the delivery dispatcher.</summary>
    [DisallowMultipleComponent]
    public sealed partial class DeliveryNPC : MonoBehaviour
    {
        [SerializeField] private DeliveryManager m_deliveryManager;
        [SerializeField] private Vector3 m_couchSpawnOffset = new Vector3(0f, 0.5f, 2.5f);
        [SerializeField, Tooltip("Primary local-space location used for purchased vehicles.")]
        private Vector3 m_vehicleSpawnOffset = new Vector3(6f, 0f, 1.5f);
        [SerializeField] private Transform m_couchSpawnPoint;
        [SerializeField] private Transform m_vehicleSpawnPoint;
        internal DeliveryManager Manager => m_deliveryManager;
        private DepotController m_depot;

        public float InteractionDistanceSquared(Vector3 playerPosition)
        {
            if (m_depot == null) m_depot = GetComponentInParent<DepotController>();
            float distance = (transform.position - playerPosition).sqrMagnitude;
            return m_depot != null ? Mathf.Min(distance,
                m_depot.DispatcherCounterDistanceSquared(transform.position, playerPosition)) : distance;
        }

        public Vector3 CouchSpawnPosition => m_couchSpawnPoint != null
            ? m_couchSpawnPoint.position
            : transform.TransformPoint(m_couchSpawnOffset);
        public Quaternion CouchSpawnRotation => m_couchSpawnPoint != null
            ? m_couchSpawnPoint.rotation
            : transform.rotation;
        public Vector3 VehicleSpawnPosition => m_vehicleSpawnPoint != null
            ? m_vehicleSpawnPoint.position
            : transform.TransformPoint(m_vehicleSpawnOffset);
        public Quaternion VehicleSpawnRotation => m_vehicleSpawnPoint != null
            ? m_vehicleSpawnPoint.rotation
            : transform.rotation;

        public void SetDeliveryManager(DeliveryManager deliveryManager)
        {
            m_deliveryManager = deliveryManager;
            if (TryGetComponent<DispatcherRoaming>(out var roaming)) roaming.Initialise(deliveryManager);
        }

        public void SetDispatchPoints(Transform couchSpawnPoint, Transform vehicleSpawnPoint)
        {
            m_couchSpawnPoint = couchSpawnPoint;
            m_vehicleSpawnPoint = vehicleSpawnPoint;
        }

        public void InteractServer(PlayerCouchCarrier player)
        {
            m_deliveryManager ??= FindFirstObjectByType<DeliveryManager>();
            m_deliveryManager?.TryBeginDialogueServer(this, player);
        }

        private void OnDestroy()
        {
            if (m_deliveryManager != null && m_deliveryManager.IsServerInitialized)
                m_deliveryManager.NotifyNpcUnavailableServer(this);
        }
    }
}
