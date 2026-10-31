using CouchGuys.Input;
using CouchGuys.Gameplay.Weapons;
using UnityEngine;

namespace CouchGuys.CameraSystem
{
    /// <summary>
    /// A lightweight mouse-driven orbit camera with over-the-shoulder framing and collision.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform m_cameraTarget;
        [SerializeField] private PlayerInputReader m_input;

        [Header("Framing")]
        [SerializeField, Min(0.1f)] private float m_cameraDistance = 4.5f;
        [SerializeField] private float m_pivotHeight = 0.15f;
        [SerializeField] private float m_shoulderOffset = 0.65f;

        [Header("Orbit")]
        [SerializeField, Min(0f)] private float m_mouseSensitivity = 0.12f;
        [SerializeField, Range(-89f, 0f)] private float m_minimumVerticalAngle = -35f;
        [SerializeField, Range(0f, 89f)] private float m_maximumVerticalAngle = 65f;
        [SerializeField] private float m_initialVerticalAngle = 12f;

        [Header("Smoothing")]
        [SerializeField, Min(0f)] private float m_followSmoothing = 45f;
        [SerializeField, Min(0f)] private float m_rotationSmoothing = 45f;

        [Header("Camera Collision")]
        [SerializeField] private LayerMask m_collisionLayers = ~0;
        [SerializeField, Min(0.01f)] private float m_collisionRadius = 0.2f;
        [SerializeField, Min(0f)] private float m_collisionPadding = 0.1f;

        [Header("Grapple Camera Tension")]
        [SerializeField, Min(0f)] private float m_grappleResistance = 8f;
        [Tooltip("Inset from each viewport edge, as a fraction of screen size.")]
        [SerializeField, Range(0f, 0.2f)] private float m_grappleScreenMargin = 0.04f;
        [Tooltip("Angular band in degrees over which looking away becomes heavier.")]
        [SerializeField, Min(0.1f)] private float m_grappleTensionAngle = 15f;
        [Tooltip("Time in seconds for resistance to respond to changes.")]
        [SerializeField, Min(0.001f)] private float m_grappleSmoothing = 0.08f;

        [Header("Cursor")]
        [SerializeField] private bool m_lockCursor = true;

        private float m_yaw;
        private float m_pitch;
        private Transform m_playerRoot;
        private Vector3 m_previousPivotPosition;
        private bool m_hasPreviousPivotPosition;
        private readonly RaycastHit[] m_collisionHits = new RaycastHit[16];
        private Collider[] m_pivotOverlaps = new Collider[16];
        private Transform m_vehicleTarget;
        private float m_vehicleDistance;
        private float m_vehicleHeight;
        private float m_vehicleLookSensitivity;
        private float m_vehicleMinimumPitch;
        private float m_vehicleMaximumPitch;
        private float m_vehicleFollowSmoothing;
        private float m_vehicleRotationSmoothing;
        private float m_vehicleCollisionRadius;
        private Camera m_camera;
        private PlayerWeaponController m_grappleSource;
        private Vector2 m_grappleLookScale = Vector2.one;

        public float VerticalAngle
        {
            get => m_pitch;
            set => m_pitch = m_vehicleTarget != null
                ? Mathf.Clamp(value, m_vehicleMinimumPitch, m_vehicleMaximumPitch)
                : Mathf.Clamp(value, m_minimumVerticalAngle, m_maximumVerticalAngle);
        }

        public void FollowVehicle(
            Transform target,
            float distance,
            float height,
            float lookSensitivity,
            float minimumPitch,
            float maximumPitch,
            float followSmoothing,
            float rotationSmoothing,
            float collisionRadius)
        {
            m_vehicleTarget = target;
            m_vehicleDistance = Mathf.Max(0.1f, distance);
            m_vehicleHeight = height;
            m_vehicleLookSensitivity = Mathf.Max(0f, lookSensitivity);
            m_vehicleMinimumPitch = Mathf.Clamp(minimumPitch, -89f, 0f);
            m_vehicleMaximumPitch = Mathf.Clamp(maximumPitch, 0f, 89f);
            m_pitch = Mathf.Clamp(m_pitch, m_vehicleMinimumPitch, m_vehicleMaximumPitch);
            m_vehicleFollowSmoothing = Mathf.Max(0.01f, followSmoothing);
            m_vehicleRotationSmoothing = Mathf.Max(0.01f, rotationSmoothing);
            m_vehicleCollisionRadius = Mathf.Max(0.01f, collisionRadius);
            m_hasPreviousPivotPosition = false;
        }

        public void FollowPlayer()
        {
            m_vehicleTarget = null;
            m_previousPivotPosition = GetPivotPosition();
            m_hasPreviousPivotPosition = true;
        }

        private void Awake()
        {
            m_playerRoot = m_cameraTarget != null ? m_cameraTarget.root : null;
            // The orbit controller lives on CameraRig; PlayerCamera is its child.
            m_camera = GetComponentInChildren<Camera>(true);
            m_grappleSource = m_cameraTarget != null
                ? m_cameraTarget.GetComponentInParent<PlayerWeaponController>() : null;
            m_yaw = m_playerRoot != null ? m_playerRoot.eulerAngles.y : transform.eulerAngles.y;
            m_pitch = Mathf.Clamp(m_initialVerticalAngle, m_minimumVerticalAngle, m_maximumVerticalAngle);

            Vector3 pivot = GetPivotPosition();
            m_previousPivotPosition = pivot;
            m_hasPreviousPivotPosition = true;
            Quaternion orbitRotation = Quaternion.Euler(m_pitch, m_yaw, 0f);
            transform.SetPositionAndRotation(CalculateCollisionPosition(pivot, orbitRotation), orbitRotation);
        }

        private void OnEnable()
        {
            m_previousPivotPosition = GetPivotPosition();
            m_hasPreviousPivotPosition = true;
            ApplyCursorState();
        }

        private void OnDisable()
        {
            if (m_lockCursor && Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void LateUpdate()
        {
            if (m_vehicleTarget != null)
            {
                UpdateVehicleCamera();
                return;
            }

            if (m_cameraTarget == null || m_input == null)
            {
                return;
            }

            Vector3 pivot = GetPivotPosition();
            CompensateForTargetMovement(pivot);
            pivot = GetClearCollisionPivot(pivot);

            Vector2 lookInput = m_input.Look;
            Bounds grappleBounds = default;
            bool constrained = m_camera != null && m_grappleSource != null &&
                m_grappleSource.TryGetLocalGrappleBounds(out grappleBounds);
            Vector2 lookDelta = new Vector2(lookInput.x, -lookInput.y) * m_mouseSensitivity;
            // Throw intent must survive resistance and the hard visibility limit.
            m_grappleSource?.RecordLocalGrappleLook(lookDelta);
            if (constrained) lookDelta = ApplyGrappleResistance(lookDelta, grappleBounds);
            else m_grappleLookScale = Vector2.one;
            m_yaw += lookDelta.x;
            m_pitch = Mathf.Clamp(
                m_pitch + lookDelta.y,
                // A moving grapple can require looking beyond the ordinary orbit limits.
                // Let input return from there without snapping on release.
                Mathf.Min(m_pitch, m_minimumVerticalAngle),
                Mathf.Max(m_pitch, m_maximumVerticalAngle));

            Quaternion desiredRotation = Quaternion.Euler(m_pitch, m_yaw, 0f);
            Vector3 desiredPosition = CalculateCollisionPosition(pivot, desiredRotation);
            if (constrained)
            {
                desiredRotation = ConstrainGrappleRotation(desiredPosition, desiredRotation, grappleBounds);
                SetOrbitAngles(desiredRotation);
                desiredPosition = CalculateCollisionPosition(pivot, desiredRotation);
            }

            float positionBlend = 1f - Mathf.Exp(-m_followSmoothing * Time.deltaTime);
            float rotationBlend = 1f - Mathf.Exp(-m_rotationSmoothing * Time.deltaTime);
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, positionBlend);
            // Smoothing can cross furniture even when both orbit destinations are clear.
            // Collision correction must be the last position operation of the frame.
            transform.position = ResolveCollisionPosition(pivot, smoothedPosition);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
            if (constrained)
            {
                // Enforce visibility at the actual, collision-corrected camera pose too.
                // Smoothing alone can otherwise let a moving target cross the frame edge.
                Quaternion corrected = ConstrainGrappleRotation(transform.position, transform.rotation, grappleBounds, true);
                if (Quaternion.Angle(transform.rotation, corrected) > 0.001f)
                {
                    transform.rotation = corrected;
                    SetOrbitAngles(corrected);
                }
            }
        }

        private Vector2 ApplyGrappleResistance(Vector2 delta, Bounds bounds)
        {
            // Integrate large mouse deltas in small angular steps so resistance cannot
            // be skipped by a fast swipe or a long frame. Mouse deltas already include time.
            int steps = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude / 2f), 1, 180);
            Vector2 step = delta / steps;
            Vector2 applied = Vector2.zero;
            float blend = 1f - Mathf.Exp(-Time.deltaTime / (steps * Mathf.Max(0.001f, m_grappleSmoothing)));
            for (int i = 0; i < steps; i++)
            {
                Quaternion rotation = Quaternion.Euler(m_pitch + applied.y, m_yaw + applied.x, 0f);
                GetGrappleAngularLimits(transform.position, rotation, bounds, out Vector2 lower, out Vector2 upper);
                for (int axis = 0; axis < 2; axis++)
                {
                    float clearance = step[axis] >= 0f ? upper[axis] : -lower[axis];
                    float tension = 1f - Mathf.Clamp01(clearance / Mathf.Max(0.1f, m_grappleTensionAngle));
                    float scale = 1f / (1f + m_grappleResistance * tension * tension);
                    // Returning toward the allowed region always retains full sensitivity.
                    if ((step[axis] > 0f && lower[axis] > 0f) ||
                        (step[axis] < 0f && upper[axis] < 0f)) scale = 1f;
                    m_grappleLookScale[axis] = Mathf.Lerp(m_grappleLookScale[axis], scale, blend);
                    applied[axis] += step[axis] * m_grappleLookScale[axis];
                }
            }
            return applied;
        }

        private Quaternion ConstrainGrappleRotation(Vector3 position, Quaternion rotation, Bounds bounds, bool safetyLimit = false)
        {
            // Re-evaluate after each correction because yaw and pitch interact near corners.
            for (int i = 0; i < 8; i++)
            {
                // The inset is a damping buffer. Only enforce the outer frame immediately,
                // allowing ordinary target motion to be followed by the existing smoothing.
                GetGrappleAngularLimits(position, rotation, bounds, out Vector2 lower, out Vector2 upper,
                    safetyLimit ? 0.001f : m_grappleScreenMargin);
                float yaw = lower.x <= upper.x ? Mathf.Clamp(0f, lower.x, upper.x) : (lower.x + upper.x) * 0.5f;
                float pitch = lower.y <= upper.y ? Mathf.Clamp(0f, lower.y, upper.y) : (lower.y + upper.y) * 0.5f;
                if (Mathf.Abs(yaw) + Mathf.Abs(pitch) < 0.001f) break;
                Vector3 angles = rotation.eulerAngles;
                rotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x) + pitch, -89f, 89f), angles.y + yaw, 0f);
            }
            return rotation;
        }

        private void GetGrappleAngularLimits(Vector3 position, Quaternion rotation, Bounds bounds,
            out Vector2 lower, out Vector2 upper, float margin = -1f)
        {
            if (margin < 0f) margin = m_grappleScreenMargin;
            float vertical = Mathf.Atan(Mathf.Tan(m_camera.fieldOfView * Mathf.Deg2Rad * 0.5f) *
                (1f - 2f * margin)) * Mathf.Rad2Deg;
            float horizontal = Mathf.Atan(Mathf.Tan(vertical * Mathf.Deg2Rad) * m_camera.aspect) * Mathf.Rad2Deg;
            lower = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            upper = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Quaternion inverse = Quaternion.Inverse(rotation);
            Vector3 center = inverse * (bounds.center - position);
            // A close object may surround the camera or exceed its FOV. In that case
            // preserve a visible portion by constraining its bounds centre instead.
            bool useCenter = center.magnitude <= bounds.extents.magnitude || center.z <= 0f;
            for (int i = 0; i < (useCenter ? 1 : 8); i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 local = useCenter ? center : inverse * (corner - position);
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg;
                lower = Vector2.Max(lower, new Vector2(yaw - horizontal, pitch - vertical));
                upper = Vector2.Min(upper, new Vector2(yaw + horizontal, pitch + vertical));
            }
        }

        private void SetOrbitAngles(Quaternion rotation)
        {
            m_yaw += Mathf.DeltaAngle(m_yaw, rotation.eulerAngles.y);
            m_pitch = Mathf.DeltaAngle(0f, rotation.eulerAngles.x);
        }

        private void UpdateVehicleCamera()
        {
            Vector2 lookInput = m_input != null ? m_input.VehicleLook : Vector2.zero;
            m_yaw += lookInput.x * m_vehicleLookSensitivity;
            m_pitch = Mathf.Clamp(
                m_pitch - lookInput.y * m_vehicleLookSensitivity,
                m_vehicleMinimumPitch,
                m_vehicleMaximumPitch);

            // World-up framing keeps vehicle roll and pitch from tilting the camera.
            Vector3 pivot = m_vehicleTarget.position + Vector3.up * m_vehicleHeight;
            Quaternion desiredRotation = Quaternion.Euler(m_pitch, m_yaw, 0f);
            Vector3 direction = desiredRotation * Vector3.back;
            float clearDistance = m_vehicleDistance;
            int hitCount = Physics.SphereCastNonAlloc(
                pivot, m_vehicleCollisionRadius, direction, m_collisionHits, m_vehicleDistance,
                m_collisionLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < hitCount; index++)
            {
                Transform hit = m_collisionHits[index].transform;
                if (hit != null && hit.IsChildOf(m_vehicleTarget.root)) continue;
                clearDistance = Mathf.Min(clearDistance, m_collisionHits[index].distance);
            }

            Vector3 desiredPosition = pivot + direction * Mathf.Max(0f, clearDistance - m_collisionPadding);
            float positionBlend = 1f - Mathf.Exp(-m_vehicleFollowSmoothing * Time.deltaTime);
            float rotationBlend = 1f - Mathf.Exp(-m_vehicleRotationSmoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, positionBlend);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
        }

        private void CompensateForTargetMovement(Vector3 pivot)
        {
            if (!m_hasPreviousPivotPosition)
            {
                m_previousPivotPosition = pivot;
                m_hasPreviousPivotPosition = true;
                return;
            }

            // A locally owned camera rig is detached from the rotating network Player.
            // Carry it by the target's exact translation before applying orbit smoothing,
            // otherwise normal movement appears to lag or rubber-band behind the Player.
            bool inheritsPlayerTransform = m_playerRoot != null && transform.IsChildOf(m_playerRoot);
            if (!inheritsPlayerTransform)
            {
                transform.position += pivot - m_previousPivotPosition;
            }

            m_previousPivotPosition = pivot;
        }

        private Vector3 GetPivotPosition()
        {
            return m_cameraTarget != null
                ? m_cameraTarget.position + Vector3.up * m_pivotHeight
                : transform.position;
        }

        private Vector3 CalculateCollisionPosition(Vector3 pivot, Quaternion orbitRotation)
        {
            pivot = GetClearCollisionPivot(pivot);
            Vector3 desiredOffset = orbitRotation * new Vector3(m_shoulderOffset, 0f, -m_cameraDistance);
            return ResolveCollisionPosition(pivot, pivot + desiredOffset);
        }

        private Vector3 GetClearCollisionPivot(Vector3 pivot)
        {
            // A carrier can overlap the couch because their mutual collision is ignored.
            // Sphere casts do not detect colliders overlapping their starting sphere.
            // Lift the query origin above those colliders before testing the camera boom.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int count;
                while ((count = Physics.OverlapSphereNonAlloc(pivot, m_collisionRadius,
                           m_pivotOverlaps, m_collisionLayers, QueryTriggerInteraction.Ignore))
                       == m_pivotOverlaps.Length)
                {
                    System.Array.Resize(ref m_pivotOverlaps, m_pivotOverlaps.Length * 2);
                }

                float clearHeight = pivot.y;
                for (int index = 0; index < count; index++)
                {
                    Collider obstacle = m_pivotOverlaps[index];
                    if (IsPlayerCollider(obstacle.transform)) continue;
                    clearHeight = Mathf.Max(clearHeight,
                        obstacle.bounds.max.y + m_collisionRadius + m_collisionPadding + 0.001f);
                }

                if (clearHeight <= pivot.y) break;
                pivot.y = clearHeight;
            }

            return pivot;
        }

        private bool IsPlayerCollider(Transform colliderTransform)
        {
            return m_playerRoot != null && colliderTransform.IsChildOf(m_playerRoot);
        }

        private Vector3 ResolveCollisionPosition(Vector3 pivot, Vector3 desiredPosition)
        {
            Vector3 desiredOffset = desiredPosition - pivot;
            float desiredDistance = desiredOffset.magnitude;
            if (desiredDistance < 0.0001f) return pivot;
            Vector3 castDirection = desiredOffset / desiredDistance;
            float nearestDistance = desiredDistance;

            int hitCount = Physics.SphereCastNonAlloc(
                pivot,
                m_collisionRadius,
                castDirection,
                m_collisionHits,
                desiredDistance,
                m_collisionLayers,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < hitCount; index++)
            {
                Transform hitTransform = m_collisionHits[index].transform;
                if (IsPlayerCollider(hitTransform))
                {
                    continue;
                }

                nearestDistance = Mathf.Min(nearestDistance, m_collisionHits[index].distance);
            }

            float correctedDistance = nearestDistance < desiredDistance
                ? Mathf.Max(0f, nearestDistance - m_collisionPadding)
                : desiredDistance;
            return pivot + castDirection * correctedDistance;
        }

        private void ApplyCursorState()
        {
            if (!m_lockCursor)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_cameraDistance = Mathf.Max(0.1f, m_cameraDistance);
            m_maximumVerticalAngle = Mathf.Max(m_minimumVerticalAngle, m_maximumVerticalAngle);
            m_initialVerticalAngle = Mathf.Clamp(m_initialVerticalAngle, m_minimumVerticalAngle, m_maximumVerticalAngle);
        }
#endif
    }
}
