using CouchGuys.Input;
using CouchGuys.Gameplay.Couch;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>
    /// Converts player intent into camera-relative CharacterController movement.
    /// The class contains no device polling so authority can be replaced for networking later.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputReader))]
    [DisallowMultipleComponent]
    public sealed class ThirdPersonPlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private Transform m_cameraTransform;
        [SerializeField] private PlayerStamina m_stamina;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float m_walkSpeed = 3.5f;
        [SerializeField, Min(0f)] private float m_runSpeed = 6f;
        [SerializeField, Min(0f)] private float m_speedChangeRate = 12f;

        [Header("Rotation")]
        [Tooltip("Maximum horizontal turning speed in degrees per second.")]
        [SerializeField, Min(0f)] private float m_rotationSpeed = 720f;
        [Tooltip("Time taken to smooth the player toward the camera-facing direction.")]
        [SerializeField, Min(0.01f)] private float m_rotationSmoothTime = 0.1f;

        [Header("Uphill Movement")]
        [Tooltip("Inclines at or below this angle do not slow the player.")]
        [SerializeField, Range(0f, 89f)] private float m_minimumUphillSlopeAngle = 4f;
        [Tooltip("Inclines at or above this angle receive the full uphill slowdown.")]
        [SerializeField, Range(0f, 89f)] private float m_maximumUphillSlopeAngle = 40f;
        [Tooltip("Uphill input is removed above this angle; players may still move sideways or downhill.")]
        [SerializeField, Range(0f, 89f)] private float m_maximumWalkableUphillAngle = 24f;
        [Tooltip("The maximum portion of speed removed while moving directly uphill.")]
        [SerializeField, Range(0f, 0.95f)] private float m_uphillSlowdownStrength = 0.45f;

        [Header("Jumping And Gravity")]
        [SerializeField, Min(0f)] private float m_jumpHeight = 1.2f;
        [SerializeField] private float m_gravity = -25f;
        [SerializeField, Min(0f)] private float m_groundedForce = 2f;
        [SerializeField, Min(0f)] private float m_terminalVelocity = 50f;

        [Header("External Impacts")]
        [SerializeField, Min(0f)] private float m_knockbackDamping = 8f;

        private const float CouchBounceHeight = 2.7f;
        private const float MinimumCouchLandingSpeed = 1.5f;
        private const float CouchBounceCooldown = 0.22f;
        private const float MaximumBounceUpwardSpeed = 13f;

        private CharacterController m_characterController;
        private float m_currentSpeed;
        private float m_verticalVelocity;
        private float m_rotationVelocity;
        private float m_externalSpeedMultiplier = 1f;
        private Transform m_resistanceAnchor;
        private Transform m_facingTarget;
        private float m_minimumResistanceDistance;
        private float m_comfortableResistanceDistance;
        private float m_maximumResistanceDistance;
        private float m_inwardResistanceSpeedMultiplier = 1f;
        private Vector3 m_resistanceOutwardDirection;
        // CharacterController supplies this through its normal collision callback;
        // retaining it avoids an additional ground raycast every frame.
        private Vector3 m_groundNormal = Vector3.up;
        private Vector3 m_externalVelocity;
        private bool m_movementLocked;
        private bool m_externalControlPaused;
        private bool m_externalRotationLocked;
        private float m_nextCouchBounceTime;

        public bool IsGrounded => m_characterController != null && m_characterController.isGrounded;
        public Vector3 MovementIntent { get; private set; }
        public bool MovementLocked => m_movementLocked;
        public bool IsSprinting { get; private set; }

        public void SetMovementLocked(bool locked)
        {
            m_movementLocked = locked;
            m_currentSpeed = 0f;
            MovementIntent = Vector3.zero;
        }

        public void SetExternalControlPaused(bool paused)
        {
            m_externalControlPaused = paused;
            m_currentSpeed = 0f;
            m_verticalVelocity = 0f;
            MovementIntent = Vector3.zero;
        }

        public void ApplyExternalImpulse(Vector3 impulse)
        {
            m_externalVelocity += impulse;
        }

        /// <summary>
        /// Applies a gameplay speed modifier without coupling movement to the carrying system.
        /// </summary>
        public void SetExternalSpeedMultiplier(float multiplier)
        {
            m_externalSpeedMultiplier = Mathf.Clamp01(multiplier);
        }

        /// <summary>
        /// Keeps movement within a horizontal band around an external anchor.
        /// </summary>
        public void SetExternalMovementResistance(
            Transform anchor,
            Vector3 outwardDirection,
            float minimumDistance,
            float comfortableDistance,
            float maximumDistance,
            float inwardSpeedMultiplier)
        {
            m_resistanceAnchor = anchor;
            Vector3 horizontalOutward = Vector3.ProjectOnPlane(outwardDirection, Vector3.up);
            if (horizontalOutward.sqrMagnitude < 0.0001f && anchor != null)
            {
                horizontalOutward = Vector3.ProjectOnPlane(
                    transform.position - anchor.position,
                    Vector3.up);
            }
            m_resistanceOutwardDirection = horizontalOutward.sqrMagnitude > 0.0001f
                ? horizontalOutward.normalized
                : transform.forward;
            m_maximumResistanceDistance = Mathf.Max(0f, maximumDistance);
            m_minimumResistanceDistance = Mathf.Clamp(
                minimumDistance,
                0f,
                m_maximumResistanceDistance);
            m_comfortableResistanceDistance = Mathf.Clamp(
                comfortableDistance,
                m_minimumResistanceDistance,
                m_maximumResistanceDistance);
            m_inwardResistanceSpeedMultiplier = Mathf.Clamp01(inwardSpeedMultiplier);
        }

        public void ClearExternalMovementResistance()
        {
            m_resistanceAnchor = null;
        }

        public void SetExternalFacingTarget(Transform target)
        {
            m_facingTarget = target;
        }

        public void ClearExternalFacingTarget()
        {
            m_facingTarget = null;
        }

        public void SetExternalRotationLocked(bool locked)
        {
            if (m_externalRotationLocked == locked)
            {
                return;
            }

            m_externalRotationLocked = locked;
            m_rotationVelocity = 0f;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = m_characterController != null && m_characterController.enabled;
            if (wasEnabled)
            {
                m_characterController.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);
            m_verticalVelocity = 0f;
            m_currentSpeed = 0f;
            MovementIntent = Vector3.zero;

            if (wasEnabled)
            {
                m_characterController.enabled = true;
            }
        }

        private void Awake()
        {
            m_characterController = GetComponent<CharacterController>();

            if (m_input == null)
            {
                m_input = GetComponent<PlayerInputReader>();
            }

            m_stamina ??= GetComponent<PlayerStamina>();
        }

        private void Update()
        {
            if (m_externalControlPaused)
            {
                return;
            }
            UpdateVerticalMovement();
            if (m_movementLocked)
            {
                UpdateLockedMovement();
            }
            else
            {
                UpdateHorizontalMovement();
            }

            m_externalVelocity = Vector3.MoveTowards(
                m_externalVelocity,
                Vector3.zero,
                m_knockbackDamping * Time.deltaTime);
        }

        private void UpdateVerticalMovement()
        {
            if (IsGrounded && m_verticalVelocity < 0f)
            {
                m_verticalVelocity = -m_groundedForce;
            }

            if (!m_movementLocked && IsGrounded && m_input.JumpPressedThisFrame)
            {
                m_verticalVelocity = Mathf.Sqrt(m_jumpHeight * -2f * m_gravity);
            }

            if (m_verticalVelocity > -m_terminalVelocity)
            {
                m_verticalVelocity += m_gravity * Time.deltaTime;
            }
        }

        private void UpdateHorizontalMovement()
        {
            Vector2 moveInput = Vector2.ClampMagnitude(m_input.Move, 1f);
            float inputMagnitude = moveInput.magnitude;
            IsSprinting = inputMagnitude > 0.05f && m_input.SprintHeld &&
                (m_stamina == null || m_stamina.CanSprint);
            float topSpeed = IsSprinting ? m_runSpeed : m_walkSpeed;
            Vector3 intendedMoveDirection = CalculateCameraRelativeDirection(moveInput);
            float uphillMultiplier = CalculateUphillSpeedMultiplier(intendedMoveDirection);
            float targetSpeed = topSpeed * inputMagnitude * m_externalSpeedMultiplier * uphillMultiplier;
            m_currentSpeed = Mathf.MoveTowards(m_currentSpeed, targetSpeed, m_speedChangeRate * Time.deltaTime);

            Vector3 moveDirection = intendedMoveDirection;
            Vector3 carryingMovementIntent = intendedMoveDirection;
            if (m_resistanceAnchor != null)
            {
                moveDirection = ApplyDirectionalResistance(
                    moveDirection,
                    transform.position,
                    m_resistanceAnchor.position,
                    m_resistanceOutwardDirection,
                    m_minimumResistanceDistance,
                    m_comfortableResistanceDistance,
                    m_maximumResistanceDistance,
                    m_inwardResistanceSpeedMultiplier,
                    out carryingMovementIntent);
            }

            // Attachment resistance constrains only the Player's locomotion around
            // the grip point. The couch must receive the original world-space intent;
            // otherwise pushing inward is weakened and pulling at maximum separation
            // can lose all translational input purely because of grab geometry.
            MovementIntent = carryingMovementIntent * inputMagnitude;
            // Facing is independent from locomotion so backward and lateral input
            // backpedals/strafes instead of turning the player away from their aim.
            // External interactions may lock rotation while retaining free movement.
            if (!m_externalRotationLocked)
            {
                Vector3 facingDirection = CalculateFacingDirection();
                if (facingDirection.sqrMagnitude > 0.001f)
                {
                    float targetAngle = Mathf.Atan2(facingDirection.x, facingDirection.z) * Mathf.Rad2Deg;
                    float smoothedAngle = Mathf.SmoothDampAngle(
                        transform.eulerAngles.y,
                        targetAngle,
                        ref m_rotationVelocity,
                        m_rotationSmoothTime,
                        m_rotationSpeed,
                        Time.deltaTime);

                    transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
                }
            }

            Vector3 velocity = moveDirection * m_currentSpeed;
            velocity.y = m_verticalVelocity;
            Vector3 displacement = (velocity + m_externalVelocity) * Time.deltaTime;
            Vector3 attachmentCorrection = m_resistanceAnchor != null
                ? CalculateAttachmentCorrection(
                    transform.position + displacement,
                    m_resistanceAnchor.position,
                    m_resistanceOutwardDirection,
                    m_minimumResistanceDistance,
                    m_maximumResistanceDistance)
                : Vector3.zero;
            // Start collecting the normal for next frame before Move invokes
            // OnControllerColliderHit for this movement.
            m_groundNormal = Vector3.zero;
            m_characterController.Move(displacement + attachmentCorrection);
        }

        private void UpdateLockedMovement()
        {
            MovementIntent = Vector3.zero;
            m_currentSpeed = 0f;
            m_groundNormal = Vector3.zero;
            Vector3 velocity = m_externalVelocity;
            velocity.y += m_verticalVelocity;
            m_characterController.Move(velocity * Time.deltaTime);
        }

        private float CalculateUphillSpeedMultiplier(Vector3 movementDirection)
        {
            if (!IsGrounded || movementDirection.sqrMagnitude < 0.0001f)
            {
                return 1f;
            }

            float slopeAngle = Vector3.Angle(m_groundNormal, Vector3.up);
            Vector3 uphill = Vector3.ProjectOnPlane(Vector3.up, m_groundNormal);
            Vector3 horizontalUphill = Vector3.ProjectOnPlane(uphill, Vector3.up);
            if (horizontalUphill.sqrMagnitude < 0.0001f)
            {
                return 1f;
            }

            float uphillAmount = Mathf.Max(0f, Vector3.Dot(
                movementDirection.normalized,
                horizontalUphill.normalized));
            if (slopeAngle >= m_maximumWalkableUphillAngle && uphillAmount > 0.001f)
            {
                return 0f;
            }

            float slopeProgress = Mathf.InverseLerp(
                m_minimumUphillSlopeAngle,
                m_maximumUphillSlopeAngle,
                slopeAngle);
            if (slopeProgress <= 0f)
            {
                return 1f;
            }

            float slowdown = m_uphillSlowdownStrength * slopeProgress * uphillAmount;
            return 1f - slowdown;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > m_groundNormal.y)
            {
                m_groundNormal = hit.normal;
            }

            TryBounceFromCouch(hit);
        }

        private void TryBounceFromCouch(ControllerColliderHit hit)
        {
            if (Time.time < m_nextCouchBounceTime || m_externalControlPaused ||
                hit.normal.y < 0.65f || m_verticalVelocity > -MinimumCouchLandingSpeed ||
                hit.collider == null)
            {
                return;
            }

            CouchCarryController couch = hit.collider.GetComponentInParent<CouchCarryController>();
            if (couch == null || couch.IsLoaded)
            {
                return;
            }

            float landingSpeed = -m_verticalVelocity;
            float bounceSpeed = Mathf.Sqrt(CouchBounceHeight * -2f * m_gravity);
            // Preserve a modest amount of upward couch motion without allowing a fast
            // tumbling couch to create an extreme player launch.
            float inheritedUpwardSpeed = Mathf.Clamp(couch.CurrentVelocity.y, 0f, 3f);
            m_verticalVelocity = Mathf.Min(
                Mathf.Max(m_verticalVelocity, bounceSpeed + inheritedUpwardSpeed),
                MaximumBounceUpwardSpeed);
            m_nextCouchBounceTime = Time.time + CouchBounceCooldown;

            if (TryGetComponent(out PlayerCouchCarrier carrier))
            {
                Vector3 playerVelocity = MovementIntent * m_currentSpeed + Vector3.down * landingSpeed;
                carrier.NotifyCouchBounce(couch, landingSpeed, playerVelocity);
            }
        }

        internal static Vector3 ApplyDirectionalResistance(
            Vector3 movement,
            Vector3 playerPosition,
            Vector3 anchorPosition,
            Vector3 outwardDirection,
            float minimumDistance,
            float comfortableDistance,
            float maximumDistance,
            float inwardSpeedMultiplier,
            out Vector3 movementIntent)
        {
            // The caller sends this unmodified world-space intent to the authoritative
            // couch simulation. Only the returned Player locomotion is constrained.
            movementIntent = movement;
            Vector3 horizontalOutward = Vector3.ProjectOnPlane(outwardDirection, Vector3.up);
            Vector3 awayFromAnchor = Vector3.ProjectOnPlane(
                playerPosition - anchorPosition,
                Vector3.up);
            float separation = awayFromAnchor.magnitude;
            if (horizontalOutward.sqrMagnitude < 0.0001f)
            {
                return movement;
            }

            horizontalOutward.Normalize();
            float inwardAmount = Vector3.Dot(movement, horizontalOutward);
            if (inwardAmount < 0f)
            {
                Vector3 resistedInwardMovement = movement - horizontalOutward * inwardAmount *
                    (1f - Mathf.Clamp01(inwardSpeedMultiplier));
                float outwardSeparation = Vector3.Dot(awayFromAnchor, horizontalOutward);
                movement = outwardSeparation <= minimumDistance
                    ? movement - horizontalOutward * inwardAmount
                    : resistedInwardMovement;
            }

            if (separation <= comfortableDistance || separation < 0.0001f)
            {
                return movement;
            }

            Vector3 awayDirection = awayFromAnchor / separation;
            float radialAmount = Vector3.Dot(movement, awayDirection);
            if (radialAmount <= 0f)
            {
                return movement;
            }

            float resistance = maximumDistance > comfortableDistance + 0.0001f
                ? Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(comfortableDistance, maximumDistance, separation))
                : 1f;
            Vector3 resistedMovement = movement - awayDirection * radialAmount * resistance;
            return resistedMovement;
        }

        internal static Vector3 CalculateAttachmentCorrection(
            Vector3 playerPosition,
            Vector3 anchorPosition,
            Vector3 outwardDirection,
            float minimumDistance,
            float maximumDistance)
        {
            Vector3 awayFromAnchor = Vector3.ProjectOnPlane(
                playerPosition - anchorPosition,
                Vector3.up);
            Vector3 horizontalOutward = Vector3.ProjectOnPlane(outwardDirection, Vector3.up);
            if (horizontalOutward.sqrMagnitude < 0.0001f)
            {
                horizontalOutward = awayFromAnchor;
            }
            if (horizontalOutward.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            horizontalOutward.Normalize();
            Vector3 correction = Vector3.zero;
            float outwardSeparation = Vector3.Dot(awayFromAnchor, horizontalOutward);
            if (outwardSeparation < minimumDistance)
            {
                correction = horizontalOutward * (minimumDistance - outwardSeparation);
                awayFromAnchor += correction;
            }

            float maximumDistanceSquared = maximumDistance * maximumDistance;
            float separationSquared = awayFromAnchor.sqrMagnitude;
            if (separationSquared <= maximumDistanceSquared)
            {
                return correction;
            }

            if (separationSquared < 0.0001f)
            {
                return correction;
            }

            float separation = Mathf.Sqrt(separationSquared);
            return correction - awayFromAnchor * ((separation - maximumDistance) / separation);
        }

        private Vector3 CalculateCameraRelativeDirection(Vector2 moveInput)
        {
            if (moveInput.sqrMagnitude < 0.001f)
            {
                return Vector3.zero;
            }

            Transform referenceTransform = m_cameraTransform != null ? m_cameraTransform : transform;
            Vector3 cameraForward = Vector3.ProjectOnPlane(referenceTransform.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(referenceTransform.right, Vector3.up).normalized;
            return (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;
        }

        private Vector3 CalculateFacingDirection()
        {
            if (m_facingTarget != null)
            {
                return Vector3.ProjectOnPlane(
                    m_facingTarget.position - transform.position,
                    Vector3.up);
            }

            Transform referenceTransform = m_cameraTransform != null ? m_cameraTransform : transform;
            return Vector3.ProjectOnPlane(referenceTransform.forward, Vector3.up);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_walkSpeed = Mathf.Max(0f, m_walkSpeed);
            m_runSpeed = Mathf.Max(m_walkSpeed, m_runSpeed);
            m_rotationSpeed = Mathf.Max(0f, m_rotationSpeed);
            m_rotationSmoothTime = Mathf.Max(0.01f, m_rotationSmoothTime);
            m_gravity = Mathf.Min(-0.01f, m_gravity);
            m_terminalVelocity = Mathf.Max(0f, m_terminalVelocity);
            m_knockbackDamping = Mathf.Max(0f, m_knockbackDamping);
            m_maximumUphillSlopeAngle = Mathf.Max(
                m_minimumUphillSlopeAngle + 0.01f,
                m_maximumUphillSlopeAngle);
            m_maximumWalkableUphillAngle = Mathf.Clamp(
                m_maximumWalkableUphillAngle,
                m_minimumUphillSlopeAngle,
                89f);
        }
#endif
    }
}
