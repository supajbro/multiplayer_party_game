using CouchGuys.Gameplay.Weapons;
using UnityEngine;

namespace CouchGuys.Player
{
    // Values 0-4 are already serialized by emote assets; never reorder them.
    public enum RobotFaceExpression
    {
        Normal = 0, Happy = 1, Angry = 2, Surprised = 3, Focused = 4,
        Excited = 5, Confused = 6, Damaged = 7, Downed = 8, Reviving = 9, Celebrating = 10
    }

    /// <summary>Adapts existing FishNet-visible gameplay to the reusable CRT display.</summary>
    [DisallowMultipleComponent]
    public sealed class RobotFaceExpressionController : RobotFaceDisplay
    {
        [Header("Gameplay Expression Timing")]
        [SerializeField, Min(0.05f)] private float m_surprisedDuration = 0.65f;
        [SerializeField, Min(0.05f)] private float m_angryHoldDuration = 0.32f;
        private PlayerHealth m_health;
        private PlayerCouchCarrier m_carrier;
        private RobotFaceExpression? m_emoteExpression;
        private float m_damageUntil, m_angryUntil, m_punchUntil, m_rebootUntil;
        private bool m_wasDowned;

        protected override void Awake()
        {
            base.Awake();
            m_health = GetComponent<PlayerHealth>();
            m_carrier = GetComponent<PlayerCouchCarrier>();
        }

        public void SetEmoteExpression(RobotFaceExpression? expression) => m_emoteExpression = expression;
        public void NotifyShot(PlayerWeaponType weapon) => m_angryUntil = Time.time +
            m_angryHoldDuration * (weapon == PlayerWeaponType.AssaultRifle ? 1.2f : 1f);
        public void NotifyPunch(float remainingDuration) => m_punchUntil = Time.time + remainingDuration;
        public void NotifyDamage()
        {
            m_damageUntil = Time.time + m_surprisedDuration;
            PlayDamageGlitch();
        }

        protected override void LateUpdate()
        {
            // Replicated health/reviver/knockdown state includes late joiners and AI.
            // Wait for FishNet's initial state; zero is the health SyncVar default.
            bool ready = m_health != null && (m_health.IsClientInitialized || m_health.IsServerInitialized);
            bool downed = ready && m_health.IsDowned;
            if (m_wasDowned && !downed && m_health.IsAlive)
            {
                PlayReboot();
                m_rebootUntil = Time.time + 0.85f / AnimationSpeed;
            }
            m_wasDowned = downed;
            SetExpression(ResolveExpression(Time.time, ready, downed));
            base.LateUpdate();
        }

        private RobotFaceExpression ResolveExpression(float now, bool ready, bool downed)
        {
            // Unassisted downed state has first priority. An active replicated
            // revive lease changes presentation to the heart, without reviving gameplay.
            bool receivingRevive = ready && m_health.IsBeingRevived;
            if (downed && !receivingRevive) return RobotFaceExpression.Downed;
            if (receivingRevive || now < m_rebootUntil) return RobotFaceExpression.Reviving;
            if (now < m_damageUntil) return RobotFaceExpression.Surprised;
            if (ready && m_health.IsAlive && m_health.CurrentHealth <= m_health.MaximumHealth * LowHealthFraction)
                return RobotFaceExpression.Damaged;
            if (m_emoteExpression.HasValue)
                return m_emoteExpression.Value == RobotFaceExpression.Happy
                    ? RobotFaceExpression.Celebrating : m_emoteExpression.Value;
            if (now < m_punchUntil) return RobotFaceExpression.Focused;
            if (now < m_angryUntil) return RobotFaceExpression.Angry;
            if (m_carrier != null && m_carrier.IsCarrying) return RobotFaceExpression.Focused;
            return RobotFaceExpression.Happy;
        }
    }
}
