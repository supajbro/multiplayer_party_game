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

        [Header("Movement")]
        [SerializeField, Min(0f)] private float m_walkSpeed = 3.5f;
        [SerializeField, Min(0f)] private float m_runSpeed = 6f;
        [SerializeField, Min(0f)] private float m_speedChangeRate = 12f;
        [SerializeField, Min(0.01f)] private float m_rotationSmoothTime = 0.1f;

        [Header("Jumping And Gravity")]
        [SerializeField, Min(0f)] private float m_jumpHeight = 1.2f;
        [SerializeField] private float m_gravity = -25f;
        [SerializeField, Min(0f)] private float m_groundedForce = 2f;
        [SerializeField, Min(0f)] private float m_terminalVelocity = 50f;

        private CharacterController m_characterController;
        private float m_currentSpeed;
        private float m_verticalVelocity;
        private float m_rotationVelocity;
        private float m_externalSpeedMultiplier = 1f;
        private Transform m_resistanceAnchor;
        private float m_comfortableResistanceDistance;
        private float m_maximumResistanceDistance;

        public bool IsGrounded => m_characterController != null && m_characterController.isGrounded;
        public Vector3 MovementIntent { get; private set; }

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
            m_comfortableResistanceDistance = Mathf.Max(0f, comfortableDistance);
            m_maximumResistanceDistance = Mathf.Max(
                m_comfortableResistanceDistance + 0.01f,
                maximumDistance);
        }

        public void ClearExternalMovementResistance()
        {
            m_resistanceAnchor = null;
        }

        private void Awake()
        {
            m_characterController = GetComponent<CharacterController>();

            if (m_input == null)
            {
                m_input = GetComponent<PlayerInputReader>();
            }
        }

        private void Update()
        {
            UpdateVerticalMovement();
            UpdateHorizontalMovement();
        }

        private void UpdateVerticalMovement()
        {
            if (IsGrounded && m_verticalVelocity < 0f)
            {
                m_verticalVelocity = -m_groundedForce;
            }

            if (IsGrounded && m_input.JumpPressedThisFrame)
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
            float topSpeed = m_input.SprintHeld ? m_runSpeed : m_walkSpeed;
            float targetSpeed = topSpeed * inputMagnitude * m_externalSpeedMultiplier;
            m_currentSpeed = Mathf.MoveTowards(m_currentSpeed, targetSpeed, m_speedChangeRate * Time.deltaTime);

            Vector3 moveDirection = CalculateCameraRelativeDirection(moveInput);
            if (m_resistanceAnchor != null)
            {
                moveDirection = ApplyDirectionalResistance(
                    moveDirection,
                    transform.position,
                    m_resistanceAnchor.position,
                    m_comfortableResistanceDistance,
                    m_maximumResistanceDistance);
            }

            MovementIntent = moveDirection * inputMagnitude;
            if (moveDirection.sqrMagnitude > 0.001f)
            {
                float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
                float smoothedAngle = Mathf.SmoothDampAngle(
                    transform.eulerAngles.y,
                    targetAngle,
                    ref m_rotationVelocity,
                    m_rotationSmoothTime);

                transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
            }

            Vector3 velocity = moveDirection * m_currentSpeed;
            velocity.y = m_verticalVelocity;
            m_characterController.Move(velocity * Time.deltaTime);
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

            float resistance = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(comfortableDistance, maximumDistance, separation));
            return movement - awayDirection * outwardAmount * resistance;
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
        }
#endif
    }
}
