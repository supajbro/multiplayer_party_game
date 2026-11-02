using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    [CreateAssetMenu(menuName = "Couch Guys/Enemies/Grappler Thief")]
    public sealed class GrapplerThiefSettings : ScriptableObject
    {
        [Min(1f)] public float GrappleRange = 14f;
        [Min(1f)] public float PreferredDistance = 9f;
        [Min(0.1f)] public float AttackCooldown = 8f;
        [Min(0.1f)] public float AimWindup = 0.6f;
        [Min(0.1f)] public float HoldDuration = 0.5f;
        [Min(0f)] public float ThrowHorizontalVelocity = 12f;
        [Min(0f)] public float ThrowUpwardVelocity = 5f;
        [Range(0f, 30f)] public float LateralVariationDegrees = 12f;
        [Min(0.1f)] public float TargetEvaluationInterval = 1f;
        [Tooltip("Protection after a grapple ends, shared by all Grappler Thieves.")]
        [Min(1f)] public float PlayerRecoveryTime = 4f;
        [Min(0f)] public float PullAcceleration = 18f;
        [Min(0f)] public float PullSpeed = 3f;
        [Min(0.1f)] public float MomentumDuration = 3f;
        [Min(0f)] public float AirDrag = 1.5f;
        public LayerMask HitMask = ~0;

        public Vector3 CalculateThrowVelocity(Vector3 player, Vector3 couch, Vector3 fallback, float variation)
        {
            Vector3 away = Vector3.ProjectOnPlane(player - couch, Vector3.up);
            if (away.sqrMagnitude < 0.001f) away = Vector3.ProjectOnPlane(fallback, Vector3.up);
            if (away.sqrMagnitude < 0.001f) away = Vector3.forward;
            away = Quaternion.AngleAxis(Mathf.Clamp(variation, -LateralVariationDegrees,
                LateralVariationDegrees), Vector3.up) * away.normalized;
            return away * ThrowHorizontalVelocity + Vector3.up * ThrowUpwardVelocity;
        }
    }
}
