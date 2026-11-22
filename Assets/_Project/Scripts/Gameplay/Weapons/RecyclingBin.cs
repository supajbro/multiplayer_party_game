using System.Collections.Generic;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Shop;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Saved scene prop. Target selection, pickup consumption and team payouts run on the server.</summary>
    [DisallowMultipleComponent]
    public sealed class RecyclingBin : MonoBehaviour
    {
        private static readonly HashSet<RecyclingBin> ActiveBins = new();
        [Header("Intake")]
        [SerializeField] private BoxCollider m_intake;
        [SerializeField] private Vector3 m_entryPoint = new(0f, 1f, 0f);
        [Header("Throw assistance")]
        [SerializeField, Min(0.1f)] private float m_assistDistance = 4.5f;
        [SerializeField, Range(0f, 90f)] private float m_facingAngle = 35f;
        [SerializeField, Min(0.1f)] private float m_flightDuration = 0.65f;
        [SerializeField, Min(0.1f)] private float m_arcHeight = 2.2f;
        [SerializeField] private LayerMask m_obstructionMask = ~0;
        [Header("Team refund")]
        [SerializeField, Range(0f, 1f)] private float m_refundFraction = 0.25f;

        public Vector3 EntryPoint => transform.TransformPoint(m_entryPoint);
        public float FlightDuration => Mathf.Max(0.1f, m_flightDuration);
        private void OnEnable() => ActiveBins.Add(this);
        private void OnDisable() => ActiveBins.Remove(this);

        public static RecyclingBin FindThrowTarget(PlayerWeaponController player, Vector3 releasePosition)
        {
            RecyclingBin best = null;
            float nearest = float.PositiveInfinity;
            foreach (var bin in ActiveBins)
            {
                if (bin == null || !bin.CanAssist(player, releasePosition)) continue;
                float distance = (bin.EntryPoint - player.transform.position).sqrMagnitude;
                if (distance < nearest) { best = bin; nearest = distance; }
            }
            return best;
        }

        public bool CanAssist(PlayerWeaponController player, Vector3 releasePosition)
        {
            if (!isActiveAndEnabled || m_intake == null || !m_intake.enabled || !m_intake.isTrigger ||
                player == null || !player.CanHandleWeaponServer || !IsInRangeAndFacing(player.transform)) return false;
            return HasClearThrowPath(player.transform, releasePosition);
        }

        private bool IsInRangeAndFacing(Transform player)
        {
            Vector3 offset = EntryPoint - player.position;
            if (offset.sqrMagnitude > m_assistDistance * m_assistDistance) return false;
            offset.y = 0f;
            Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            if (offset.sqrMagnitude > 0.001f && Vector3.Angle(forward, offset) > m_facingAngle) return false;
            return true;
        }

        private bool HasClearThrowPath(Transform player, Vector3 releasePosition)
        {
            // Validate the actual assisted arc, not just a ray which could miss a low ceiling.
            Vector3 previous = releasePosition;
            for (int step = 1; step <= 16; step++)
            {
                Vector3 next = GetFlightPosition(releasePosition, step / 16f);
                Vector3 delta = next - previous;
                foreach (var hit in Physics.SphereCastAll(previous, 0.1f, delta.normalized, delta.magnitude,
                             m_obstructionMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(player) ||
                        hit.collider.GetComponentInParent<WeaponPickup>() != null) continue;
                    return false;
                }
                previous = next;
            }
            return true;
        }

        // Cubic curve approaches the intake vertically, so wide weapons clear the rim.
        public Vector3 GetFlightPosition(Vector3 start, float progress)
        {
            float t = Mathf.Clamp01(progress), u = 1f - t;
            Vector3 end = EntryPoint;
            float top = Mathf.Max(start.y, end.y) + Mathf.Max(0.1f, m_arcHeight);
            Vector3 lift = new(start.x, top, start.z);
            Vector3 approach = new(end.x, top, end.z);
            return u * u * u * start + 3f * u * u * t * lift + 3f * u * t * t * approach + t * t * t * end;
        }

        public bool GuideThrowServer(WeaponPickup pickup) => isActiveAndEnabled && pickup != null &&
            pickup.BeginRecyclingThrowServer(this);

        public bool Contains(Vector3 position)
        {
            if (m_intake == null || !m_intake.enabled || !m_intake.isTrigger) return false;
            Vector3 point = m_intake.transform.InverseTransformPoint(position) - m_intake.center;
            Vector3 half = m_intake.size * 0.5f;
            return Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y && Mathf.Abs(point.z) <= half.z;
        }

        public static bool TryGetRefund(WeaponPickup pickup, ShopCatalogue catalogue, float fraction, out int refund)
        {
            refund = 0;
            if (pickup == null) return false;
            int price;
            if (pickup.HealingSettings != null) price = pickup.HealingSettings.PurchasePrice;
            else
            {
                var item = catalogue != null ? catalogue.FindInventoryItem(pickup.InventoryId) : null;
                if (item == null) return false; // Keep unpriced items instead of silently destroying them.
                price = item.Price;
            }
            refund = (int)System.Math.Floor(price * (double)(float.IsNaN(fraction) ? 0f : Mathf.Clamp01(fraction)));
            return true;
        }

        public bool TryRecycleServer(WeaponPickup pickup)
        {
            if (!isActiveAndEnabled || pickup == null || !pickup.IsServerInitialized || !Contains(pickup.RecyclingPosition)) return false;
            foreach (var manager in FindObjectsByType<DeliveryManager>(FindObjectsSortMode.None))
            {
                if (!manager.IsServerInitialized || manager.NetworkManager != pickup.NetworkManager) continue;
                if (!TryGetRefund(pickup, manager.ShopCatalogue, m_refundFraction, out int refund)) return false;
                return pickup.RecycleServer(this, manager, refund);
            }
            return false;
        }

        private void OnTriggerEnter(Collider other) => TryRecycleServer(other.GetComponentInParent<WeaponPickup>());
        private void OnTriggerStay(Collider other) => TryRecycleServer(other.GetComponentInParent<WeaponPickup>());

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(EntryPoint, 0.15f);
            Gizmos.DrawWireSphere(transform.position, m_assistDistance);
        }
    }
}
