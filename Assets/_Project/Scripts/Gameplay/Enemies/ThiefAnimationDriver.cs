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

        [SerializeField] private ThiefSettings m_settings;
        [SerializeField] private Animator m_animator;
        private float m_movingThreshold => m_settings != null
            ? m_settings.Animation.MovingThreshold : 0.08f;
        private float m_speedDamping => m_settings != null
            ? m_settings.Animation.SpeedDamping : 0.08f;

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

    }
}
