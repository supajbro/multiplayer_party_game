using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [CreateAssetMenu(fileName = "GrappleGun", menuName = "Couch Guys/Weapons/Grapple Gun")]
    public sealed class GrappleGunSettings : ScriptableObject
    {
        [Header("Upward camera flick")]
        [SerializeField] private bool m_enableFlick = true;
        [Tooltip("Upward camera speed in degrees/second needed to start a throw.")]
        [SerializeField, Min(1f)] private float m_minimumAngularSpeed = 100f;
        [SerializeField, Min(1f)] private float m_fullPowerAngularSpeed = 500f;
        [Tooltip("Minimum upward sweep in degrees. Filters small camera jitters.")]
        [SerializeField, Min(0.1f)] private float m_minimumSweep = 6f;
        [SerializeField, Min(0f)] private float m_cooldown = 0.65f;

        [Header("Throw power (added velocity in metres/second)")]
        [SerializeField, Min(0f)] private float m_minimumPower = 14f;
        [SerializeField, Min(0f)] private float m_maximumPower = 32f;
        [SerializeField, Min(0f)] private float m_forwardWeight = 1f;
        [SerializeField, Min(0f)] private float m_upwardWeight = 0.85f;
        [SerializeField, Min(1f)] private float m_maximumTargetSpeed = 40f;
        [SerializeField, Min(0f)] private float m_couchSpin = 3f;

        [Header("Momentum and release")]
        [Tooltip("Automatically let go when flicking so the grapple cannot reel the throw back in.")]
        [SerializeField] private bool m_releaseOnFlick = true;
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

        // Only direction samples arrive from the owner. Power is derived from this
        // server-side settings asset, never from an impulse supplied by a client.
        public bool TryCalculateFlick(Vector3 previous, Vector3 current, float deltaTime,
            ref float upwardSweep, out Vector3 impulse)
        {
            impulse = Vector3.zero;
            if (!m_enableFlick || deltaTime < 0.005f || deltaTime > 0.25f ||
                !float.IsFinite(deltaTime) || !Finite(previous) || !Finite(current) ||
                previous.sqrMagnitude < 0.5f || current.sqrMagnitude < 0.5f)
            {
                upwardSweep = 0f;
                return false;
            }
            previous.Normalize();
            current.Normalize();
            float rise = (Mathf.Asin(Mathf.Clamp(current.y, -1f, 1f)) -
                Mathf.Asin(Mathf.Clamp(previous.y, -1f, 1f))) * Mathf.Rad2Deg;
            float speed = rise / deltaTime;
            if (speed < m_minimumAngularSpeed)
            {
                upwardSweep = 0f;
                return false;
            }
            upwardSweep += rise;
            if (upwardSweep < m_minimumSweep) return false;
            upwardSweep = 0f;
            Vector3 forward = Vector3.ProjectOnPlane(current, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(previous, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            Vector3 direction = forward.normalized * Mathf.Max(0f, m_forwardWeight) +
                Vector3.up * Mathf.Max(0f, m_upwardWeight);
            float strength = Mathf.InverseLerp(m_minimumAngularSpeed,
                Mathf.Max(m_minimumAngularSpeed + 1f, m_fullPowerAngularSpeed), speed);
            impulse = direction.normalized * Mathf.Min(MaximumTargetSpeed,
                Mathf.Lerp(Mathf.Max(0f, m_minimumPower), Mathf.Max(m_minimumPower, m_maximumPower), strength));
            return impulse.sqrMagnitude > 0f;
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
