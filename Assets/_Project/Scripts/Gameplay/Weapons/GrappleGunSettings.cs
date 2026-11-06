using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [CreateAssetMenu(fileName = "GrappleGun", menuName = "Couch Guys/Weapons/Grapple Gun")]
    public sealed class GrappleGunSettings : ScriptableObject
    {
        [Header("Attachment")]
        [SerializeField, Min(1f)] private float m_maximumRange = 60f;
        [Tooltip("Detach beyond this distance. Kept at least as large as the firing range.")]
        [SerializeField, Min(1f)] private float m_breakDistance = 80f;
        [SerializeField, Min(0.1f)] private float m_arrivalDistance = 1.5f;
        [Tooltip("Automatically finish a shot after this time, including when blocked by geometry.")]
        [SerializeField, Min(0.1f)] private float m_maximumPullDuration = 8f;
        [Tooltip("Finish pulling a target when its attachment point comes this close to the muzzle.")]
        [SerializeField, Min(0.1f)] private float m_targetArrivalDistance = 2f;
        [Tooltip("Minimum time between new shots. Pressing shoot during an active grapple cancels it immediately.")]
        [SerializeField, Min(0f)] private float m_cooldown = 0.35f;

        [Header("World grapple")]
        [SerializeField, Min(0f)] private float m_playerPullAcceleration = 45f;
        [SerializeField, Min(0f)] private float m_playerLaunchSpeed = 16f;
        [Tooltip("How quickly sideways drift is corrected toward the shot's attachment point. Zero retains free swinging.")]
        [SerializeField, Min(0f)] private float m_trajectoryCorrection = 6f;
        [SerializeField, Min(1f)] private float m_playerMaximumSpeed = 30f;
        [SerializeField, Min(0f)] private float m_swingStrength = 18f;
        [SerializeField, Range(0f, 1f)] private float m_releaseMomentum = 1f;

        [Header("Target pulling (acceleration in metres/second squared)")]
        [SerializeField, Min(0f)] private float m_objectPullForce = 45f;
        [SerializeField, Min(1f)] private float m_objectMaximumSpeed = 12f;
        [SerializeField, Min(0f)] private float m_characterPullForce = 45f;
        [SerializeField, Min(0f)] private float m_vehiclePullForce = 25f;

        public float MaximumRange => Mathf.Max(1f, m_maximumRange);
        public float BreakDistance => Mathf.Max(MaximumRange, m_breakDistance);
        public float ArrivalDistance => Mathf.Max(0.1f, m_arrivalDistance);
        public float MaximumPullDuration => Mathf.Max(0.1f, m_maximumPullDuration);
        public float TargetArrivalDistance => Mathf.Max(0.1f, m_targetArrivalDistance);
        public float PlayerPullAcceleration => Mathf.Max(0f, m_playerPullAcceleration);
        public float PlayerLaunchSpeed => Mathf.Max(0f, m_playerLaunchSpeed);
        public float TrajectoryCorrection => Mathf.Max(0f, m_trajectoryCorrection);
        public float PlayerMaximumSpeed => Mathf.Max(1f, m_playerMaximumSpeed);
        public float SwingStrength => Mathf.Max(0f, m_swingStrength);
        public float ReleaseMomentum => Mathf.Clamp01(m_releaseMomentum);
        public float ObjectPullForce => Mathf.Max(0f, m_objectPullForce);
        public float ObjectMaximumSpeed => Mathf.Max(1f, m_objectMaximumSpeed);
        public float CharacterPullForce => Mathf.Max(0f, m_characterPullForce);
        public float VehiclePullForce => Mathf.Max(0f, m_vehiclePullForce);

        [Header("Charge")]
        [InspectorName("Depletion Rate (Seconds)")]
        [Tooltip("Total seconds of attached grapple use available from a full charge. Charge does not regenerate.")]
        [SerializeField, Min(0.1f)] private float m_depletionSeconds = 30f;
        public float DepletionSeconds => Mathf.Max(0.1f, m_depletionSeconds);

        // Retained serialized flick tuning for compatibility with existing assets and tooling.
        [HideInInspector, SerializeField] private bool m_enableFlick = true;
        [Tooltip("Intended camera speed before grapple resistance, in degrees/second, needed to start a throw.")]
        [HideInInspector, SerializeField, Min(1f)] private float m_minimumAngularSpeed = 100f;
        [HideInInspector, SerializeField, Min(1f)] private float m_fullPowerAngularSpeed = 500f;
        [Tooltip("Minimum consistent swipe in degrees. Filters small camera jitters.")]
        [HideInInspector, SerializeField, Min(0.1f)] private float m_minimumSweep = 6f;


        [HideInInspector, SerializeField, Min(0f)] private float m_minimumPower = 14f;
        [HideInInspector, SerializeField, Min(0f)] private float m_maximumPower = 32f;
        [HideInInspector, SerializeField, Min(0f)] private float m_forwardWeight = 1f;
        [Tooltip("Weight along the swipe direction (up, down, left or right), relative to forward weight.")]
        [InspectorName("Swipe Direction Weight")]
        [HideInInspector, SerializeField, Min(0f)] private float m_upwardWeight = 0.85f;
        [HideInInspector, SerializeField, Min(1f)] private float m_maximumTargetSpeed = 40f;
        [HideInInspector, SerializeField, Min(0f)] private float m_couchSpin = 3f;


        [Tooltip("Opt in to detaching after a flick. Leave off to keep the rope attached while holding fire.")]
        [HideInInspector, SerializeField] private bool m_releaseOnFlick = false;
        [Tooltip("When auto release is off, suspend pulling briefly after a flick.")]
        [HideInInspector, SerializeField, Min(0f)] private float m_pullPause = 0.5f;
        [Header("Momentum after completion")]
        [Tooltip("Preserve player launch momentum this long before normal movement damping resumes.")]
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

        // Legacy gesture calculation retained for existing tooling; one-shot pulls do not use camera flicks.
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
