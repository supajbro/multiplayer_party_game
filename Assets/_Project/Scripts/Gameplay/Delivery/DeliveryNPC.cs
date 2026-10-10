using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Local interaction marker for the stationary delivery dispatcher.</summary>
    [DisallowMultipleComponent]
    public sealed class DeliveryNPC : MonoBehaviour
    {
        [SerializeField] private DeliveryManager m_deliveryManager;
        [SerializeField] private Vector3 m_couchSpawnOffset = new Vector3(0f, 0.5f, 2.5f);

        public Vector3 CouchSpawnPosition => transform.TransformPoint(m_couchSpawnOffset);
        public Quaternion CouchSpawnRotation => transform.rotation;

        public void SetDeliveryManager(DeliveryManager deliveryManager)
        {
            m_deliveryManager = deliveryManager;
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
