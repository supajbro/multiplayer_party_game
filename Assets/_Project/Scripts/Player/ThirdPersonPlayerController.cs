using CouchGuys.Input;
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
        [SerializeField, Min(0.01f)] private float m_rotationSmoothTime = 0.1f;

        [Header("Uphill Movement")]
        [Tooltip("Inclines at or below this angle do not slow the player.")]
        [SerializeField, Range(0f, 89f)] private float m_minimumUphillSlopeAngle = 4f;
        [Tooltip("Inclines at or above this angle receive the full uphill slowdown.")]
        [SerializeField, Range(0f, 89f)] private float m_maximumUphillSlopeAngle = 40f;
        [Tooltip("The maximum portion of speed removed while moving directly uphill.")]
        [SerializeField, Range(0f, 0.95f)] private float m_uphillSlowdownStrength = 0.45f;

        [Header("Jumping And Gravity")]
        [SerializeField, Min(0f)] private float m_jumpHeight = 1.2f;
        [SerializeField] private float m_gravity = -25f;
        [SerializeField, Min(0f)] private float m_groundedForce = 2f;
        [SerializeField, Min(0f)] private float m_terminalVelocity = 50f;

        [Header("External Impacts")]
        [SerializeField, Min(0f)] private float m_knockbackDamping = 8f;

        private CharacterController m_characterController;
        private float m_currentSpeed;
        private float m_verticalVelocity;
        private float m_rotationVelocity;
        private float m_externalSpeedMultiplier = 1f;
        private Transform m_resistanceAnchor;
        private Transform m_facingTarget;
        private float m_comfortableResistanceDistance;
        private float m_maximumResistanceDistance;
        // CharacterController supplies this through its normal collision callback;
        // retaining it avoids an additional ground raycast every frame.
        private Vector3 m_groundNormal = Vector3.up;
        private Vector3 m_externalVelocity;
        private bool m_movementLocked;

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
        /// Restricts only movement which increases horizontal distance from an external anchor.
        /// </summary>
        public void SetExternalMovementResistance(
            Transform anchor,
            float comfortableDistance,
            float maximumDistance)
        {
            m_resistanceAnchor = anchor;
            m_maximumResistanceDistance = Mathf.Max(0f, maximumDistance);
            m_comfortableResistanceDistance = Mathf.Clamp(
                comfortableDistance,
                0f,
                m_maximumResistanceDistance);
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
            if (m_resistanceAnchor != null)
            {
                moveDirection = ApplyDirectionalResistance(
                    moveDirection,
                    transform.position,
                    m_resistanceAnchor.position,
                    m_comfortableResistanceDistance,
                    m_maximumResistanceDistance);
            }

            MovementIntent = intendedMoveDirection * inputMagnitude;
            Vector3 facingDirection = m_facingTarget != null
                ? Vector3.ProjectOnPlane(m_facingTarget.position - transform.position, Vector3.up)
                : moveDirection;
            if (facingDirection.sqrMagnitude > 0.001f)
            {
                float targetAngle = Mathf.Atan2(facingDirection.x, facingDirection.z) * Mathf.Rad2Deg;
                float smoothedAngle = Mathf.SmoothDampAngle(
                    transform.eulerAngles.y,
                    targetAngle,
                    ref m_rotationVelocity,
                    m_rotationSmoothTime);

                transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
            }

            Vector3 velocity = moveDirection * m_currentSpeed;
            velocity.y = m_verticalVelocity;
            Vector3 attachmentCorrection = m_resistanceAnchor != null
                ? CalculateAttachmentCorrection(
                    transform.position,
                    m_resistanceAnchor.position,
                    m_maximumResistanceDistance)
                : Vector3.zero;
            // Start collecting the normal for next frame before Move invokes
            // OnControllerColliderHit for this movement.
            m_groundNormal = Vector3.zero;
            m_characterController.Move(
                (velocity + m_externalVelocity) * Time.deltaTime + attachmentCorrection);
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
            float slopeProgress = Mathf.InverseLerp(
                m_minimumUphillSlopeAngle,
                m_maximumUphillSlopeAngle,
                slopeAngle);
            if (slopeProgress <= 0f)
            {
                return 1f;
            }

            Vector3 uphill = Vector3.ProjectOnPlane(Vector3.up, m_groundNormal);
            Vector3 horizontalUphill = Vector3.ProjectOnPlane(uphill, Vector3.up);
            if (horizontalUphill.sqrMagnitude < 0.0001f)
            {
                return 1f;
            }

            float uphillAmount = Mathf.Max(0f, Vector3.Dot(
                movementDirection.normalized,
                horizontalUphill.normalized));
            float slowdown = m_uphillSlowdownStrength * slopeProgress * uphillAmount;
            return 1f - slowdown;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > m_groundNormal.y)
            {
                m_groundNormal = hit.normal;
            }
        }

        internal static Vector3 ApplyDirectionalResistance(
            Vector3 movement,
            Vector3 playerPosition,
            Vector3 anchorPosition,
            float comfortableDistance,
            float maximumDistance)
        {
            Vector3 awayFromAnchor = Vector3.ProjectOnPlane(playerPosition - anchorPosition, Vector3.up);
            float separation = awayFromAnchor.magnitude;
            if (separation <= comfortableDistance || awayFromAnchor.sqrMagnitude < 0.0001f)
            {
                return movement;
            }

            Vector3 awayDirection = awayFromAnchor / separation;
            float outwardAmount = Vector3.Dot(movement, awayDirection);
            if (outwardAmount <= 0f)
            {
                return movement;
            }

            float resistance = maximumDistance > comfortableDistance + 0.0001f
                ? Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(comfortableDistance, maximumDistance, separation))
                : 1f;
            return movement - awayDirection * outwardAmount * resistance;
        }

        internal static Vector3 CalculateAttachmentCorrection(
            Vector3 playerPosition,
            Vector3 anchorPosition,
            float maximumDistance)
        {
            Vector3 awayFromAnchor = Vector3.ProjectOnPlane(playerPosition - anchorPosition, Vector3.up);
            float maximumDistanceSquared = maximumDistance * maximumDistance;
            float separationSquared = awayFromAnchor.sqrMagnitude;
            if (separationSquared <= maximumDistanceSquared || separationSquared < 0.0001f)
            {
                return Vector3.zero;
            }

            float separation = Mathf.Sqrt(separationSquared);
            return -awayFromAnchor * ((separation - maximumDistance) / separation);
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_walkSpeed = Mathf.Max(0f, m_walkSpeed);
            m_runSpeed = Mathf.Max(m_walkSpeed, m_runSpeed);
            m_gravity = Mathf.Min(-0.01f, m_gravity);
            m_terminalVelocity = Mathf.Max(0f, m_terminalVelocity);
            m_knockbackDamping = Mathf.Max(0f, m_knockbackDamping);
            m_maximumUphillSlopeAngle = Mathf.Max(
                m_minimumUphillSlopeAngle + 0.01f,
                m_maximumUphillSlopeAngle);
        }
#endif
    }
}
