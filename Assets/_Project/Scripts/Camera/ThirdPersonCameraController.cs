using CouchGuys.Input;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.CameraSystem
{
    /// <summary>
    /// A lightweight mouse-driven orbit camera with over-the-shoulder framing and collision.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)] // After couch wobble and RobotHoverVFX (150).
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform m_cameraTarget;
        [SerializeField] private PlayerInputReader m_input;
        [Tooltip("The local player's visible body. Prevents camera collision from pushing the view into its head.")]
        [SerializeField] private Transform m_playerVisualRoot;

        [Header("Framing")]
        [SerializeField, Min(0.1f)] private float m_cameraDistance = 4.5f;
        [SerializeField, Min(1f)] private float m_couchCarryDistanceMultiplier = 1.25f;
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
        [Tooltip("Inward damping rate in inverse seconds. Keep faster than the return rate.")]
        [SerializeField, Min(0.01f)] private float m_collisionSmoothingSpeed = 12f;
        [SerializeField, Min(0.01f)] private float m_returnSmoothingSpeed = 4f;
        [Tooltip("Preferred closest boom distance. Actual geometry clearance takes priority in tight spaces.")]
        [SerializeField, Min(0.01f)] private float m_minimumCameraDistance = 0.65f;

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
        private RaycastHit[] m_collisionHits = new RaycastHit[16];
        private Collider[] m_pivotOverlaps = new Collider[16];
        private PlayerCouchCarrier m_couchCarrier;
        private Transform m_carriedCouchRoot;
        private float m_preferredDistance;
        private float m_smoothedBoomDistance;
        private float m_collisionReleaseDelay;
        private bool m_hasBoomDistance;
        private readonly List<Renderer> m_carriedCouchRenderers = new();
        private Bounds m_carriedCouchBounds;
        private bool m_hasCarriedCouchBounds;
        private readonly List<Renderer> m_playerVisualRenderers = new();
        private Bounds m_playerVisualBounds;
        private bool m_hasPlayerVisualBounds;
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
        private Transform m_dispatcherFocus;
        private bool m_dispatcherFocusing, m_dispatcherReturning;
        private Vector3 m_dispatcherBlendPosition;
        private Quaternion m_dispatcherBlendRotation;
        private float m_dispatcherBlendTime, m_dispatcherDuration, m_dispatcherStartFov, m_dispatcherFov, m_gameplayFov;
        private System.Action m_dispatcherReturned;

        public bool DispatcherCameraActive => m_dispatcherFocusing || m_dispatcherReturning;

        public void FocusDispatcher(Transform focus, float seconds, float fieldOfView)
        {
            if (focus == null || m_camera == null) return;
            if (!DispatcherCameraActive) m_gameplayFov = m_camera.fieldOfView;
            m_dispatcherFocus = focus;
            m_dispatcherFocusing = true;
            m_dispatcherReturning = false;
            m_dispatcherReturned = null;
            m_dispatcherFov = fieldOfView;
            BeginDispatcherBlend(seconds);
        }

        public void ReturnFromDispatcher(float seconds, System.Action completed, bool immediate = false)
        {
            if (!DispatcherCameraActive) { completed?.Invoke(); return; }
            m_dispatcherFocusing = false;
            m_dispatcherReturning = true;
            m_dispatcherReturned = completed;
            bool snap = immediate || !isActiveAndEnabled;
            BeginDispatcherBlend(snap ? 0f : seconds);
            if (snap) UpdateDispatcherCamera();
        }

        private void BeginDispatcherBlend(float seconds)
        {
            m_dispatcherBlendPosition = transform.position;
            m_dispatcherBlendRotation = transform.rotation;
            m_dispatcherStartFov = m_camera.fieldOfView;
            m_dispatcherDuration = Mathf.Max(0f, seconds);
            m_dispatcherBlendTime = 0f;
        }

        private void UpdateDispatcherCamera()
        {
            if (m_dispatcherFocusing && m_dispatcherFocus == null)
            {
                m_dispatcherFocusing = false;
                m_dispatcherReturning = true;
                BeginDispatcherBlend(.6f);
            }
            Vector3 position;
            Quaternion rotation;
            if (m_dispatcherFocusing)
            {
                // Focus describes the camera itself, even if PlayerCamera has an authored offset.
                Quaternion relativeRotation = Quaternion.Inverse(transform.rotation) * m_camera.transform.rotation;
                Vector3 relativePosition = transform.InverseTransformPoint(m_camera.transform.position);
                rotation = m_dispatcherFocus.rotation * Quaternion.Inverse(relativeRotation);
                position = m_dispatcherFocus.position - rotation * Vector3.Scale(relativePosition, transform.lossyScale);
            }
            else
            {
                rotation = Quaternion.Euler(m_pitch, m_yaw, 0f);
                position = CalculateCollisionPosition(GetPivotPosition(), rotation);
            }
            m_dispatcherBlendTime += Time.unscaledDeltaTime;
            float t = m_dispatcherDuration <= 0f ? 1f : Mathf.Clamp01(m_dispatcherBlendTime / m_dispatcherDuration);
            float eased = t * t * (3f - 2f * t);
            transform.SetPositionAndRotation(Vector3.Lerp(m_dispatcherBlendPosition, position, eased),
                Quaternion.Slerp(m_dispatcherBlendRotation, rotation, eased));
            m_camera.fieldOfView = Mathf.Lerp(m_dispatcherStartFov, m_dispatcherFocusing ? m_dispatcherFov : m_gameplayFov, eased);
            if (!m_dispatcherReturning || t < 1f) return;
            m_dispatcherReturning = false;
            m_dispatcherFocus = null;
            m_previousPivotPosition = GetPivotPosition();
            m_hasPreviousPivotPosition = true;
            var callback = m_dispatcherReturned;
            m_dispatcherReturned = null;
            callback?.Invoke();
        }

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
            m_hasBoomDistance = false;
            m_collisionReleaseDelay = 0f;
            m_previousPivotPosition = GetPivotPosition();
            m_hasPreviousPivotPosition = true;
        }

        private void Awake()
        {
            m_playerRoot = m_cameraTarget != null ? m_cameraTarget.root : null;
            // The orbit controller lives on CameraRig; PlayerCamera is its child.
            m_camera = GetComponentInChildren<Camera>(true);
            m_couchCarrier = m_cameraTarget != null
                ? m_cameraTarget.GetComponentInParent<PlayerCouchCarrier>() : null;
            m_grappleSource = m_cameraTarget != null
                ? m_cameraTarget.GetComponentInParent<PlayerWeaponController>() : null;
            m_yaw = m_playerRoot != null ? m_playerRoot.eulerAngles.y : transform.eulerAngles.y;
            m_pitch = Mathf.Clamp(m_initialVerticalAngle, m_minimumVerticalAngle, m_maximumVerticalAngle);
            UpdatePlayerVisualBounds();

            Vector3 pivot = GetPivotPosition();
            m_previousPivotPosition = pivot;
            m_hasPreviousPivotPosition = true;
            Quaternion orbitRotation = Quaternion.Euler(m_pitch, m_yaw, 0f);
            transform.SetPositionAndRotation(CalculateCollisionPosition(pivot, orbitRotation), orbitRotation);
        }

        private void OnEnable()
        {
            m_hasBoomDistance = false;
            m_collisionReleaseDelay = 0f;
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
            UpdateCarriedCouchBounds(m_couchCarrier != null ? m_couchCarrier.CarriedCouch : null);
            UpdatePlayerVisualBounds();
            if (DispatcherCameraActive)
            {
                UpdateDispatcherCamera();
                ResolveCameraVisualOverlap();
                return;
            }
            if (m_vehicleTarget != null)
            {
                UpdateVehicleCamera();
                ResolveCameraVisualOverlap();
                return;
            }

            if (m_cameraTarget == null || m_input == null)
            {
                return;
            }

            Vector3 pivot = GetPivotPosition();
            CompensateForTargetMovement(pivot);
            pivot = GetClearCollisionPivot(pivot);
            UpdatePreferredDistance(Time.deltaTime);

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
            desiredPosition = SmoothCollisionPosition(pivot, desiredPosition, Time.deltaTime);

            float positionBlend = 1f - Mathf.Exp(-m_followSmoothing * Time.deltaTime);
            float rotationBlend = 1f - Mathf.Exp(-m_rotationSmoothing * Time.deltaTime);
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, positionBlend);
            // Smoothing can cross furniture even when both orbit destinations are clear.
            // Collision correction must be the last position operation of the frame.
            transform.position = ResolveCollisionPosition(pivot, smoothedPosition);
            // Remember emergency clearance so the damper does not fight it next frame.
            if ((transform.position - smoothedPosition).sqrMagnitude > 0.000001f)
                m_smoothedBoomDistance = Vector3.Distance(pivot, transform.position);
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
            float distance = m_preferredDistance > 0f ? m_preferredDistance : m_cameraDistance;
            Vector3 desiredOffset = orbitRotation * new Vector3(m_shoulderOffset, 0f, -distance);
            return ResolveCollisionPosition(pivot, pivot + desiredOffset);
        }

        private void UpdatePreferredDistance(float deltaTime)
        {
            if (m_preferredDistance <= 0f) m_preferredDistance = m_cameraDistance;
            float target = m_cameraDistance * (m_carriedCouchRoot != null ? m_couchCarryDistanceMultiplier : 1f);
            m_preferredDistance = Mathf.Lerp(m_preferredDistance, target,
                1f - Mathf.Exp(-m_returnSmoothingSpeed * deltaTime));
        }

        private Vector3 SmoothCollisionPosition(Vector3 pivot, Vector3 safePosition, float deltaTime)
        {
            Vector3 offset = safePosition - pivot;
            float safeDistance = offset.magnitude;
            if (safeDistance < 0.0001f) return safePosition;
            Vector3 direction = offset / safeDistance;
            // A larger probe starts compression before the hard clearance limit is
            // reached. Only world geometry affects this anticipation, never a carrier.
            float targetDistance = Vector3.Distance(pivot, CastWorldCameraBoom(pivot, safePosition,
                m_collisionRadius + Mathf.Max(0.25f, m_collisionPadding)));
            targetDistance = Mathf.Min(safeDistance, Mathf.Max(m_minimumCameraDistance, targetDistance));
            if (!m_hasBoomDistance)
            {
                m_smoothedBoomDistance = safeDistance;
                m_hasBoomDistance = true;
            }
            // Hold compression through brief gaps at wall edges and moving geometry.
            // Small changes do not alternate inward/outward corrections every frame.
            float preferred = m_preferredDistance > 0f ? m_preferredDistance : m_cameraDistance;
            float preferredBoom = new Vector3(m_shoulderOffset, 0f, preferred).magnitude;
            if (targetDistance < preferredBoom - 0.02f) m_collisionReleaseDelay = 0.15f;
            else m_collisionReleaseDelay = Mathf.Max(0f, m_collisionReleaseDelay - deltaTime);
            bool inward = targetDistance < m_smoothedBoomDistance;
            if (inward || m_collisionReleaseDelay <= 0f)
            {
                float speed = inward ? Mathf.Max(m_collisionSmoothingSpeed, m_returnSmoothingSpeed) : m_returnSmoothingSpeed;
                m_smoothedBoomDistance = Mathf.Lerp(m_smoothedBoomDistance, targetDistance,
                    1f - Mathf.Exp(-speed * deltaTime));
            }
            return pivot + direction * m_smoothedBoomDistance;
        }

        private Vector3 GetClearCollisionPivot(Vector3 pivot)
        {
            // Sphere casts miss initial overlaps. Clear world geometry, but keep
            // the orbit pivot stable when the local body or held couch moves across it.
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
                    if (IsIgnoredBoomCollider(obstacle.transform)) continue;
                    clearHeight = Mathf.Max(clearHeight,
                        obstacle.bounds.max.y + m_collisionRadius + m_collisionPadding + 0.001f);
                }

                if (clearHeight <= pivot.y) break;
                pivot.y = clearHeight;
            }

            return pivot;
        }

        private bool IsIgnoredBoomCollider(Transform colliderTransform)
        {
            return IsPlayerCollider(colliderTransform) ||
                (m_carriedCouchRoot != null && colliderTransform.IsChildOf(m_carriedCouchRoot));
        }

        private bool IsPlayerCollider(Transform colliderTransform)
        {
            return m_playerRoot != null && colliderTransform.IsChildOf(m_playerRoot);
        }

        private Vector3 ResolveCollisionPosition(Vector3 pivot, Vector3 desiredPosition)
        {
            pivot = GetClearCollisionPivot(pivot);
            // The couch may cross the sight line without obstructing the camera.
            // Only an actual near-plane overlap needs a visual safety correction.
            if (m_hasCarriedCouchBounds && GetExpandedCarriedCouchBounds().Contains(desiredPosition))
            {
                pivot.y = Mathf.Max(pivot.y, GetExpandedCarriedCouchBounds().max.y + 0.001f);
                pivot = GetClearCollisionPivot(pivot);
            }
            Vector3 result = CastCameraBoom(pivot, desiredPosition);
            if (m_hasCarriedCouchBounds && GetExpandedCarriedCouchBounds().Contains(result))
            {
                pivot.y = Mathf.Max(pivot.y, GetExpandedCarriedCouchBounds().max.y + 0.001f);
                pivot = GetClearCollisionPivot(pivot);
                result = CastCameraBoom(pivot, desiredPosition);
            }
            if (!m_hasPlayerVisualBounds) return result;

            Bounds playerBounds = GetExpandedVisualBounds(m_playerVisualBounds);
            if (!playerBounds.Contains(result)) return result;

            // The boom normally starts inside the player and ignores their physics
            // colliders. A couch/wall can shorten it back into the face. Retry from
            // above the visible body, then stop before entering it. Shortening this
            // already-tested segment preserves world and held-couch clearance too.
            pivot.y = Mathf.Max(pivot.y, playerBounds.max.y + 0.001f);
            pivot = GetClearCollisionPivot(pivot);
            if (m_hasCarriedCouchBounds && GetExpandedCarriedCouchBounds().Contains(pivot))
                pivot.y = GetExpandedCarriedCouchBounds().max.y + 0.001f;
            result = CastCameraBoom(pivot, desiredPosition);
            Vector3 offset = result - pivot;
            float distance = offset.magnitude;
            if (distance > 0.0001f && playerBounds.IntersectRay(new Ray(pivot, offset / distance), out float hit))
                result = pivot + offset / distance * Mathf.Min(distance, Mathf.Max(0f, hit - 0.001f));
            return result;
        }

        private Vector3 CastCameraBoom(Vector3 pivot, Vector3 desiredPosition)
        {
            Vector3 result = CastWorldCameraBoom(pivot, desiredPosition, m_collisionRadius);
            if (!m_hasCarriedCouchBounds) return result;
            Bounds couchBounds = GetExpandedCarriedCouchBounds();
            if (!couchBounds.Contains(result)) return result;
            Vector3 offset = result - pivot;
            float distance = offset.magnitude;
            if (distance > 0.0001f && couchBounds.IntersectRay(new Ray(pivot, offset / distance), out float hit))
                result = pivot + offset / distance * Mathf.Min(distance, Mathf.Max(0f, hit - 0.001f));
            return result;
        }

        private Vector3 CastWorldCameraBoom(Vector3 pivot, Vector3 desiredPosition, float radius)
        {
            Vector3 desiredOffset = desiredPosition - pivot;
            float desiredDistance = desiredOffset.magnitude;
            if (desiredDistance < 0.0001f) return pivot;
            Vector3 castDirection = desiredOffset / desiredDistance;
            float nearestDistance = desiredDistance;

            int hitCount;
            // NonAlloc results are unordered. A full buffer can omit the nearest hit.
            while ((hitCount = Physics.SphereCastNonAlloc(
                pivot,
                radius,
                castDirection,
                m_collisionHits,
                desiredDistance,
                m_collisionLayers,
                QueryTriggerInteraction.Ignore)) == m_collisionHits.Length)
            {
                System.Array.Resize(ref m_collisionHits, m_collisionHits.Length * 2);
            }

            for (int index = 0; index < hitCount; index++)
            {
                Transform hitTransform = m_collisionHits[index].transform;
                if (IsIgnoredBoomCollider(hitTransform))
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

        private void UpdateCarriedCouchBounds(CouchCarryController couch)
        {
            m_carriedCouchRoot = couch != null ? couch.transform : null;
            m_hasCarriedCouchBounds = false;
            m_carriedCouchRenderers.Clear();
            if (couch == null) return;
            // A networked model variant can arrive/change after attachment. Reuse
            // the list to pick up the actual active model without per-frame garbage.
            couch.GetComponentsInChildren(false, m_carriedCouchRenderers);
            foreach (Renderer visual in m_carriedCouchRenderers)
            {
                if (!(visual is MeshRenderer || visual is SkinnedMeshRenderer) ||
                    !visual.enabled || !visual.gameObject.activeInHierarchy) continue;
                if (m_hasCarriedCouchBounds) m_carriedCouchBounds.Encapsulate(visual.bounds);
                else m_carriedCouchBounds = visual.bounds;
                m_hasCarriedCouchBounds = true;
            }
        }

        private void UpdatePlayerVisualBounds()
        {
            m_hasPlayerVisualBounds = false;
            m_playerVisualRenderers.Clear();
            if (m_playerVisualRoot == null) return;
            m_playerVisualRoot.GetComponentsInChildren(false, m_playerVisualRenderers);
            foreach (Renderer visual in m_playerVisualRenderers)
            {
                // Hover particles, weapon trails and other effects are not the body.
                if (!(visual is MeshRenderer || visual is SkinnedMeshRenderer) || !visual.enabled) continue;
                if (m_hasPlayerVisualBounds) m_playerVisualBounds.Encapsulate(visual.bounds);
                else m_playerVisualBounds = visual.bounds;
                m_hasPlayerVisualBounds = true;
            }
        }

        private void ResolveCameraVisualOverlap()
        {
            // Dispatcher and vehicle blends use their own position smoothing.
            if ((m_hasCarriedCouchBounds && GetExpandedCarriedCouchBounds().Contains(transform.position)) ||
                (m_hasPlayerVisualBounds && GetExpandedVisualBounds(m_playerVisualBounds).Contains(transform.position)))
                transform.position = ResolveCollisionPosition(GetPivotPosition(), transform.position);
        }

        private Bounds GetExpandedCarriedCouchBounds()
        {
            return GetExpandedVisualBounds(m_carriedCouchBounds);
        }

        private Bounds GetExpandedVisualBounds(Bounds bounds)
        {
            // Protect the near-plane corners as well as the camera's centre. A wide
            // viewport or an Inspector-adjusted near clip can exceed the query sphere.
            float radius = m_collisionRadius;
            if (m_camera != null)
            {
                float near = m_camera.nearClipPlane;
                float halfHeight = m_camera.orthographic ? m_camera.orthographicSize
                    : near * Mathf.Tan(m_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                radius = Mathf.Max(radius, new Vector3(halfHeight * m_camera.aspect, halfHeight, near).magnitude);
                // The collision controller moves CameraRig; allow authored child offsets.
                radius += Vector3.Distance(transform.position, m_camera.transform.position);
            }
            bounds.Expand(2f * (radius + m_collisionPadding));
            return bounds;
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
            m_couchCarryDistanceMultiplier = Mathf.Max(1f, m_couchCarryDistanceMultiplier);
            m_collisionSmoothingSpeed = Mathf.Max(0.01f, m_collisionSmoothingSpeed);
            m_returnSmoothingSpeed = Mathf.Max(0.01f, m_returnSmoothingSpeed);
            m_minimumCameraDistance = Mathf.Max(0.01f, m_minimumCameraDistance);
            m_collisionPadding = Mathf.Max(0f, m_collisionPadding);
            m_maximumVerticalAngle = Mathf.Max(m_minimumVerticalAngle, m_maximumVerticalAngle);
            m_initialVerticalAngle = Mathf.Clamp(m_initialVerticalAngle, m_minimumVerticalAngle, m_maximumVerticalAngle);
        }
#endif
    }
}
