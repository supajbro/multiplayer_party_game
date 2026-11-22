using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        [Header("Hold Q to throw")]
        [SerializeField, Min(0.1f)] private float m_throwHoldDuration = 0.75f;
        [SerializeField, Min(0f)] private float m_throwSpeed = 12f;
        [SerializeField, Min(0f)] private float m_throwLift = 3f;

        private int m_releaseSlot = -1, m_releaseItem = -1;
        private float m_releaseStartedAt;
        private int m_serverReleaseSlot = -1, m_serverReleaseItem = -1;
        private float m_serverReleaseStartedAt;
        private float ThrowHoldDuration => Mathf.Max(0.1f, m_throwHoldDuration);

        private bool CanReleaseItemLocally => IsOwner && m_input != null && m_input.CanReleaseCarriedItem &&
            m_health != null && m_health.IsAlive && !m_health.IsKnockedDown &&
            !(m_couchCarrier != null && m_couchCarrier.IsCarrying) && m_equippedIndex.Value >= 0;

        private void UpdateItemReleaseInput()
        {
            if (IsServerInitialized && (!CanHandleWeaponServer ||
                m_serverReleaseSlot != SelectedHotbarSlot || m_serverReleaseItem != m_equippedIndex.Value))
                ClearItemReleaseServer();

            if (!CanReleaseItemLocally || (m_releaseSlot >= 0 &&
                (m_releaseSlot != SelectedHotbarSlot || m_releaseItem != m_equippedIndex.Value)))
            {
                CancelLocalItemRelease();
                return;
            }

            if (m_input.DropWeaponPressedThisFrame && m_releaseSlot < 0)
            {
                m_releaseSlot = SelectedHotbarSlot;
                m_releaseItem = m_equippedIndex.Value;
                m_releaseStartedAt = Time.unscaledTime;
                BeginItemReleaseServerRpc(m_releaseSlot, m_releaseItem);
            }
            if (m_releaseSlot < 0) return;
            if (m_input.DropWeaponReleasedThisFrame)
            {
                bool thrown = Time.unscaledTime - m_releaseStartedAt >= ThrowHoldDuration;
                m_releaseSlot = -1;
                FinishItemReleaseServerRpc(thrown);
            }
            else if (!m_input.DropWeaponHeld)
                CancelLocalItemRelease(); // Suppression or device loss must never release an item.
        }

        public string GetItemReleasePrompt(out float progress, out float duration)
        {
            progress = duration = 0f;
            if (m_releaseSlot < 0 || !CanReleaseItemLocally) return null;
            duration = ThrowHoldDuration;
            progress = Mathf.Min(duration, Time.unscaledTime - m_releaseStartedAt);
            return progress >= duration ? "Hold Q to throw (release now)" : "Hold Q to throw";
        }

        private void CancelLocalItemRelease()
        {
            if (m_releaseSlot < 0) return;
            m_releaseSlot = -1;
            if (IsClientInitialized) CancelItemReleaseServerRpc();
        }

        private void OnDisable()
        {
            CancelLocalItemRelease();
            ClearItemReleaseServer();
        }

        [ServerRpc]
        private void BeginItemReleaseServerRpc(int slot, int item)
        {
            if (!CanHandleWeaponServer || slot != SelectedHotbarSlot || item < 0 || item != m_equippedIndex.Value) return;
            // Repeated begins cannot extend or transfer an existing charge.
            if (m_serverReleaseSlot >= 0) return;
            m_serverReleaseSlot = slot;
            m_serverReleaseItem = item;
            m_serverReleaseStartedAt = Time.unscaledTime;
        }

        [ServerRpc]
        private void CancelItemReleaseServerRpc() => ClearItemReleaseServer();

        private void ClearItemReleaseServer()
        {
            m_serverReleaseSlot = -1;
            m_serverReleaseItem = -1;
        }

        [ServerRpc]
        private void FinishItemReleaseServerRpc(bool thrown)
        {
            bool valid = CanHandleWeaponServer && m_serverReleaseSlot == SelectedHotbarSlot &&
                m_serverReleaseItem >= 0 && m_serverReleaseItem == m_equippedIndex.Value;
            // Allow a small amount of packet arrival jitter; clients still require a full local circle.
            bool charged = Time.unscaledTime - m_serverReleaseStartedAt >= ThrowHoldDuration - 0.1f;
            ClearItemReleaseServer();
            if (!valid) return;
            if (thrown && charged) ReleaseOwnedItemServer(null, true);
            else DropSelectedItemServer();
        }
    }
}
