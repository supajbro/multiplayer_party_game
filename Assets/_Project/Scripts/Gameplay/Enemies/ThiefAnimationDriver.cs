using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Drives thief animation from replicated transform motion and combat events.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefAnimationDriver : MonoBehaviour
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int ShootHash = Animator.StringToHash("Shoot");
        private static readonly int DeadHash = Animator.StringToHash("Dead");

        [SerializeField] private Animator m_animator;
        [SerializeField, Min(0.01f)] private float m_movingThreshold = 0.08f;
        [SerializeField, Min(0f)] private float m_speedDamping = 0.08f;

        private Vector3 m_previousPosition;
        private bool m_hasPreviousPosition;

        public Animator Animator => m_animator;

        private void Awake()
        {
            m_animator ??= GetComponentInChildren<Animator>(true);
            RememberPosition();
        }

        private void OnEnable()
        {
            RememberPosition();
        }

        private void Update()
        {
            if (m_animator == null)
            {
                return;
            }

            Vector3 position = transform.position;
            if (!m_hasPreviousPosition || Time.deltaTime <= 0f)
            {
                m_previousPosition = position;
                m_hasPreviousPosition = true;
                return;
            }

            float worldSpeed = Vector3.ProjectOnPlane(
                position - m_previousPosition,
                Vector3.up).magnitude / Time.deltaTime;
            float animationSpeed = worldSpeed >= m_movingThreshold ? worldSpeed : 0f;
            m_animator.SetFloat(SpeedHash, animationSpeed, m_speedDamping, Time.deltaTime);
            m_previousPosition = position;
        }

        public void TriggerShoot()
        {
            if (m_animator != null && !m_animator.GetBool(DeadHash))
            {
                m_animator.SetTrigger(ShootHash);
            }
        }

        public void TriggerDeath()
        {
            if (m_animator != null)
            {
                m_animator.SetBool(DeadHash, true);
            }
        }

        public void TriggerHit()
        {
            // The compact thief rig has no separate hit clip yet; restarting the short
            // shoot upper-body reaction provides readable damage feedback without a new layer.
            if (m_animator != null && !m_animator.GetBool(DeadHash))
                m_animator.SetTrigger(ShootHash);
        }

        private void RememberPosition()
        {
            m_previousPosition = transform.position;
            m_hasPreviousPosition = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_movingThreshold = Mathf.Max(0.01f, m_movingThreshold);
            m_speedDamping = Mathf.Max(0f, m_speedDamping);
        }
#endif
    }
}
