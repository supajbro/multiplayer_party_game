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
        private static readonly int FrustrationHash = Animator.StringToHash("Base Layer.Frustration");
        private static readonly int FrustrationSpeedHash = Animator.StringToHash("FrustrationSpeed");
        private static readonly int WalkHash = Animator.StringToHash("Base Layer.Walk");
        private static readonly int IdleHash = Animator.StringToHash("Base Layer.Idle");

        [SerializeField] private ThiefSettings m_settings;
        [SerializeField] private Animator m_animator;
        private float m_movingThreshold => m_settings != null
            ? m_settings.Animation.MovingThreshold : 0.08f;
        private float m_speedDamping => m_settings != null
            ? m_settings.Animation.SpeedDamping : 0.08f;

        private Vector3 m_previousPosition;
        private bool m_hasPreviousPosition;
        private ThiefNavigator m_navigator;
        private bool m_wasFrustrated;

        public Animator Animator => m_animator;

        private void Awake()
        {
            m_animator ??= GetComponentInChildren<Animator>(true);
            m_navigator = GetComponentInParent<ThiefNavigator>();
            RememberPosition();
            m_wasFrustrated = false;
        }

        private void OnEnable()
        {
            RememberPosition();
            m_wasFrustrated = false;
        }

        private void Update()
        {
            if (m_animator == null)
            {
                return;
            }

            bool frustrated = m_navigator != null && m_navigator.IsFrustrated;
            if (frustrated && !m_wasFrustrated && !m_animator.GetBool(DeadHash) &&
                m_animator.HasState(0, FrustrationHash))
            {
                m_animator.ResetTrigger(ShootHash);
                m_animator.SetFloat(FrustrationSpeedHash, 1f / m_navigator.FrustrationDuration);
                m_animator.CrossFadeInFixedTime(FrustrationHash, 0.08f);
            }
            else if (!frustrated && m_wasFrustrated && !m_animator.GetBool(DeadHash))
            {
                // A client joining during the gesture must follow the server's end time.
                int locomotion = m_navigator.IsDeparting ? WalkHash : IdleHash;
                m_animator.CrossFadeInFixedTime(locomotion, 0.08f);
            }
            m_wasFrustrated = frustrated;

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
            if (m_animator != null && !m_animator.GetBool(DeadHash) &&
                (m_navigator == null || m_navigator.CanAttack))
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
            if (m_animator != null && !m_animator.GetBool(DeadHash) &&
                (m_navigator == null || m_navigator.CanAttack))
                m_animator.SetTrigger(ShootHash);
        }

        private void RememberPosition()
        {
            m_previousPosition = transform.position;
            m_hasPreviousPosition = true;
        }

    }
}
