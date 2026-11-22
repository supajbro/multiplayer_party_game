using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Healing;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class WeaponPickup
    {
        private RecyclingBin m_recycleTarget;
        private Vector3 m_recycleStart;
        private float m_recycleElapsed;
        private bool m_guidedThrow;
        private bool m_savedDetectCollisions;
        public int InventoryId => m_healingItem != null
            ? HealingItemCatalogue.Load()?.IdOf(m_healingItem) ?? -1 : (int)m_weaponType;
        internal Vector3 RecyclingPosition => m_body != null ? m_body.position : transform.position;

        internal bool BeginRecyclingThrowServer(RecyclingBin bin)
        {
            if (!IsServerInitialized || !IsSpawned || m_claimed || m_guidedThrow || MountedSled != null || bin == null) return false;
            m_recycleTarget = bin;
            m_recycleStart = m_body.position;
            m_recycleElapsed = 0f;
            m_guidedThrow = true;
            m_savedDetectCollisions = m_body.detectCollisions;
            m_body.linearVelocity = Vector3.zero;
            m_body.angularVelocity = Vector3.zero;
            m_body.isKinematic = true;
            m_body.detectCollisions = false;
            return true;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || !m_guidedThrow) return;
            if (m_recycleTarget == null || !m_recycleTarget.isActiveAndEnabled)
            {
                EndRecyclingThrowServer();
                return;
            }
            m_recycleElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(m_recycleElapsed / m_recycleTarget.FlightDuration);
            m_body.position = m_recycleTarget.GetFlightPosition(m_recycleStart, t);
            if (t < 1f) return;
            // Credit only at arrival. Failure leaves a normal, recoverable item in the bin.
            var bin = m_recycleTarget;
            if (!bin.TryRecycleServer(this)) EndRecyclingThrowServer();
        }

        private void EndRecyclingThrowServer()
        {
            m_recycleTarget = null;
            m_guidedThrow = false;
            m_body.isKinematic = false;
            m_body.detectCollisions = m_savedDetectCollisions;
        }

        internal bool RecycleServer(RecyclingBin bin, DeliveryManager manager, int reward)
        {
            if (!IsServerInitialized || !IsSpawned || m_claimed || MountedSled != null || manager == null ||
                !manager.IsServerInitialized || manager.NetworkManager != NetworkManager || bin == null ||
                !bin.Contains(RecyclingPosition) || (m_recycleTarget != null && m_recycleTarget != bin)) return false;
            m_claimed = true; // Atomic with pickup claims; multi-collider callbacks cannot pay twice.
            manager.AddCurrencyServer(reward);
            Despawn(NetworkObject);
            return true;
        }
    }
}
