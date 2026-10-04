using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>
    /// Converts replicated player state into Animator parameters. Movement is measured
    /// from the transform so the same component animates local players, remote players,
    /// and server-driven debug bots without sending separate animation messages.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CouchGuyAnimationDriver : MonoBehaviour
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int CarryingHash = Animator.StringToHash("IsCarrying");
        private static readonly int HoldingItemHash = Animator.StringToHash("HoldingItem");
        private static readonly int PointingHash = Animator.StringToHash("Pointing");
        private static readonly int PickupHash = Animator.StringToHash("Pickup");
        private static readonly int DropHash = Animator.StringToHash("Drop");
        private static readonly int HitHash = Animator.StringToHash("Hit");

        [Header("References")]
        [SerializeField] private Animator m_animator;
        [SerializeField] private ThirdPersonPlayerController m_playerController;
        [SerializeField] private PlayerCouchCarrier m_couchCarrier;

        [Header("Movement Animation")]
        [SerializeField, Min(0.1f)] private float m_fullMoveSpeed = 3.5f;
        [SerializeField, Min(0f)] private float m_movementDampTime = 0.12f;
        [SerializeField, Min(1f)] private float m_teleportSpeed = 15f;

        private Vector3 m_previousPosition;
        private bool m_wasCarrying;
        private bool m_holdingItem;
        private bool m_pointing;
        private bool m_hasPositionSample;

        public Animator Animator => m_animator;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            CacheReferences();
            m_previousPosition = transform.position;
            m_hasPositionSample = true;
            m_wasCarrying = m_couchCarrier != null && m_couchCarrier.IsCarrying;

            if (m_animator != null)
            {
                m_animator.SetBool(CarryingHash, m_wasCarrying);
                m_animator.SetBool(HoldingItemHash, m_holdingItem);
                m_animator.SetBool(PointingHash, m_pointing);
            }
        }

        private void LateUpdate()
        {
            if (m_animator == null)
            {
                CacheReferences();
                if (m_animator == null)
                {
                    return;
                }
            }

            UpdateMovementParameters();
            UpdateCarryingParameters();
        }

        /// <summary>Allows weapon/gameplay code to enter and leave the pointing pose.</summary>
        public void SetPointing(bool pointing)
        {
            m_pointing = pointing;
            if (m_animator != null)
            {
                m_animator.SetBool(PointingHash, pointing);
            }
        }

        /// <summary>Uses the generic two-handed holding loop for non-couch objects.</summary>
        public void SetHoldingItem(bool holdingItem)
        {
            m_holdingItem = holdingItem;
            if (m_animator != null)
            {
                m_animator.SetBool(HoldingItemHash, holdingItem);
            }
        }

        /// <summary>Plays the authored hit reaction without affecting gameplay state.</summary>
        public void PlayHitReaction()
        {
            m_animator?.SetTrigger(HitHash);
        }

        private void UpdateMovementParameters()
        {
            Vector3 currentPosition = transform.position;
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 worldVelocity = m_hasPositionSample
                ? (currentPosition - m_previousPosition) / deltaTime
                : Vector3.zero;
            m_previousPosition = currentPosition;
            m_hasPositionSample = true;

            if (worldVelocity.magnitude >= m_teleportSpeed)
            {
                worldVelocity = Vector3.zero;
            }

            bool isGrounded = m_playerController == null || !m_playerController.enabled
                ? Mathf.Abs(worldVelocity.y) < 0.15f
                : m_playerController.IsGrounded;
            worldVelocity = Vector3.ProjectOnPlane(worldVelocity, Vector3.up);

            Vector3 localVelocity = transform.InverseTransformDirection(worldVelocity);
            Vector2 normalizedMovement = Vector2.ClampMagnitude(
                new Vector2(localVelocity.x, localVelocity.z) / m_fullMoveSpeed,
                1f);

            m_animator.SetFloat(
                MoveXHash,
                normalizedMovement.x,
                m_movementDampTime,
                deltaTime);
            m_animator.SetFloat(
                MoveYHash,
                normalizedMovement.y,
                m_movementDampTime,
                deltaTime);
            m_animator.SetFloat(
                SpeedHash,
                normalizedMovement.magnitude,
                m_movementDampTime,
                deltaTime);
            m_animator.SetBool(GroundedHash, isGrounded);
        }

        private void UpdateCarryingParameters()
        {
            bool isCarrying = m_couchCarrier != null && m_couchCarrier.IsCarrying;
            m_animator.SetBool(CarryingHash, isCarrying);

            if (isCarrying == m_wasCarrying)
            {
                return;
            }

            m_animator.ResetTrigger(isCarrying ? DropHash : PickupHash);
            m_animator.SetTrigger(isCarrying ? PickupHash : DropHash);
            m_wasCarrying = isCarrying;
        }

        private void CacheReferences()
        {
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
            m_couchCarrier ??= GetComponent<PlayerCouchCarrier>();
            m_animator ??= GetComponentInChildren<Animator>(true);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_fullMoveSpeed = Mathf.Max(0.1f, m_fullMoveSpeed);
            m_movementDampTime = Mathf.Max(0f, m_movementDampTime);
            m_teleportSpeed = Mathf.Max(1f, m_teleportSpeed);
            CacheReferences();
        }
#endif
    }
}
