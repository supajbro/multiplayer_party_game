using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [CreateAssetMenu(fileName = "GrappleGun", menuName = "Couch Guys/Weapons/Grapple Gun")]
    public sealed class GrappleGunSettings : ScriptableObject
    {
        [Header("Charge")]
        [InspectorName("Depletion Rate (Seconds)")]
        [Tooltip("Total seconds of attached grapple use available from a full charge. Charge does not regenerate.")]
        [SerializeField, Min(0.1f)] private float m_depletionSeconds = 30f;
        public float DepletionSeconds => Mathf.Max(0.1f, m_depletionSeconds);

        [Header("Directional camera flick")]
        [SerializeField] private bool m_enableFlick = true;
        [Tooltip("Intended camera speed before grapple resistance, in degrees/second, needed to start a throw.")]
        [SerializeField, Min(1f)] private float m_minimumAngularSpeed = 100f;
        [SerializeField, Min(1f)] private float m_fullPowerAngularSpeed = 500f;
        [Tooltip("Minimum consistent swipe in degrees. Filters small camera jitters.")]
        [SerializeField, Min(0.1f)] private float m_minimumSweep = 6f;
        [SerializeField, Min(0f)] private float m_cooldown = 0.65f;

        [Header("Throw power (added velocity in metres/second)")]
        [SerializeField, Min(0f)] private float m_minimumPower = 14f;
        [SerializeField, Min(0f)] private float m_maximumPower = 32f;
        [SerializeField, Min(0f)] private float m_forwardWeight = 1f;
        [Tooltip("Weight along the swipe direction (up, down, left or right), relative to forward weight.")]
        [InspectorName("Swipe Direction Weight")]
        [SerializeField, Min(0f)] private float m_upwardWeight = 0.85f;
        [SerializeField, Min(1f)] private float m_maximumTargetSpeed = 40f;
        [SerializeField, Min(0f)] private float m_couchSpin = 3f;

        [Header("Momentum and release")]
        [Tooltip("Opt in to detaching after a flick. Leave off to keep the rope attached while holding fire.")]
        [SerializeField] private bool m_releaseOnFlick = false;
        [Tooltip("When auto release is off, suspend pulling briefly after a flick.")]
        [SerializeField, Min(0f)] private float m_pullPause = 0.5f;
        [Tooltip("Preserve throw momentum this long before normal character damping / couch safety caps resume.")]
        [SerializeField, Min(0.1f)] private float m_momentumDuration = 3f;
        [SerializeField, Min(0f)] private float m_airDrag = 1.5f;
        [SerializeField, Min(0.1f)] private float m_gravityMultiplier = 1f;

        public bool EnableFlick => m_enableFlick;
        public float Cooldown => Mathf.Max(0.05f, m_cooldown);
        public float MaximumTargetSpeed => Mathf.Max(1f, m_maximumTargetSpeed);
        public float CouchSpin => Mathf.Max(0f, m_couchSpin);
        public bool ReleaseOnFlick => m_releaseOnFlick;
        public float PullPause => Mathf.Max(0f, m_pullPause);
        public float MomentumDuration => Mathf.Max(0.1f, m_momentumDuration);
        public float AirDrag => Mathf.Max(0f, m_airDrag);
        public float GravityMultiplier => Mathf.Max(0.1f, m_gravityMultiplier);

        // Raw look angles ride the existing heartbeat. Power, cooldown and application
        // remain server-authoritative; the camera's visibility correction is never a gesture.
        public bool TryCalculateFlick(Vector2 lookDelta, Vector3 aimDirection, float deltaTime,
            ref Vector2 sweep, out Vector3 impulse)
        {
            impulse = Vector3.zero;
            if (!m_enableFlick || deltaTime < 0.005f || deltaTime > 0.25f ||
                !float.IsFinite(deltaTime) || !Finite(aimDirection) ||
                !float.IsFinite(lookDelta.x) || !float.IsFinite(lookDelta.y) ||
                aimDirection.sqrMagnitude < 0.5f)
            {
                sweep = Vector2.zero;
                return false;
            }
            float speed = lookDelta.magnitude / deltaTime;
            if (speed < m_minimumAngularSpeed)
            {
                sweep = Vector2.zero;
                return false;
            }
            // Reversals start a new sweep rather than building power by wiggling.
            if (Vector2.Dot(sweep, lookDelta) < 0f) sweep = Vector2.zero;
            sweep += lookDelta;
            if (sweep.magnitude < m_minimumSweep) return false;
            Vector2 swipe = sweep.normalized;
            sweep = Vector2.zero;
            Vector3 forward = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 direction = forward * Mathf.Max(0f, m_forwardWeight) +
                (right * swipe.x - Vector3.up * swipe.y) * Mathf.Max(0f, m_upwardWeight);
            float strength = Mathf.InverseLerp(m_minimumAngularSpeed,
                Mathf.Max(m_minimumAngularSpeed + 1f, m_fullPowerAngularSpeed), speed);
            impulse = direction.normalized * Mathf.Min(MaximumTargetSpeed,
                Mathf.Lerp(Mathf.Max(0f, m_minimumPower), Mathf.Max(m_minimumPower, m_maximumPower), strength));
            return impulse.sqrMagnitude > 0f;
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
