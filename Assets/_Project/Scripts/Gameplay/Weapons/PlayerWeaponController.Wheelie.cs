using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        [Header("Optional combat teammate configuration")]
        [SerializeField] private WheelieSettings m_wheelieSettings;

        private float FireInterval(WeaponDefinition weapon) => weapon.FireInterval /
            (m_wheelieSettings != null ? Mathf.Max(.05f, m_wheelieSettings.FireRateMultiplier) : 1f);
        private float ReloadDuration(WeaponDefinition weapon) => weapon.ReloadDuration /
            (m_wheelieSettings != null ? Mathf.Max(.05f, m_wheelieSettings.ReloadSpeedMultiplier) : 1f);

        internal bool HasWheelieLineOfSight(ThiefHealth target)
        {
            if (m_wheelieSettings == null || target == null || !target.IsAlive || !target.IsSpawned) return false;
            Vector3 origin = GetMuzzlePosition(m_equippedIndex.Value, m_health.AimPoint);
            Vector3 direction = target.transform.position + Vector3.up * m_wheelieSettings.HostileAimHeight - origin;
            return direction.sqrMagnitude > .001f && TryGetNearestNonSelfHit(origin, direction.normalized,
                direction.magnitude + m_wheelieSettings.AgentRadius, out RaycastHit first) &&
                first.collider.GetComponentInParent<ThiefHealth>() == target;
        }

        internal bool TryFireWheelieAtThiefServer(ThiefHealth target)
        {
            if (!IsServerInitialized || Owner.IsValid || m_wheelieSettings == null ||
                !HasWheelieLineOfSight(target) || target.gameObject.scene != gameObject.scene) return false;
            Vector3 origin = GetMuzzlePosition(m_equippedIndex.Value, m_health.AimPoint);
            Vector3 direction = target.transform.position + Vector3.up * m_wheelieSettings.HostileAimHeight - origin;
            float range = Mathf.Min(CurrentWeaponRange, m_wheelieSettings.MaximumEngagementDistance);
            if (direction.sqrMagnitude > range * range) return false;
            direction = ApplySpread(direction.normalized,
                m_wheelieSettings.MaximumAimErrorDegrees * (1f - m_wheelieSettings.CombatAccuracy));
            // Never fire an inaccurate centre shot through an ally or civilian.
            if (TryGetNearestNonSelfHit(origin, direction, range, out RaycastHit hit) &&
                (hit.collider.GetComponentInParent<PlayerHealth>() != null ||
                 hit.collider.GetComponentInParent<CouchGuys.Gameplay.Pedestrians.PedestrianController>() != null ||
                 hit.collider.GetComponentInParent<CouchGuys.Gameplay.Delivery.DeliveryNPC>() != null)) return false;
            return TryFireServer(origin, direction);
        }
    }
}
