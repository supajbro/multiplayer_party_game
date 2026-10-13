using UnityEngine;

namespace CouchGuys.Gameplay.Sled
{
    [CreateAssetMenu(
        menuName = "Couch Guys/Gameplay/Sled Settings",
        fileName = "SledSettings")]
    public sealed class SledSettings : ScriptableObject
    {
        [Header("Movement Speed")]
        [Tooltip("Maximum player-driven speed while exactly one carrier is attached.")]
        [SerializeField, Min(0.1f)] private float m_speedOnePlayer = 1.8f;
        [Tooltip("Maximum player-driven speed while exactly two carriers are attached.")]
        [SerializeField, Min(0.1f)] private float m_speedTwoPlayers = 2.5f;
        [Tooltip("Maximum player-driven speed while exactly three carriers are attached.")]
        [SerializeField, Min(0.1f)] private float m_speedThreePlayers = 3.2f;
        [Tooltip("Maximum player-driven speed while exactly four carriers are attached.")]
        [SerializeField, Min(0.1f)] private float m_speedFourPlayers = 4f;

        [Header("Steering")]
        [Tooltip("How strongly off-centre player input turns the sled.")]
        [SerializeField, Min(0f)] private float m_steeringStrength = 1f;

        [Header("Sliding")]
        [Tooltip("Ground friction while sliding. Lower values preserve more downhill momentum.")]
        [SerializeField, Range(0f, 1f)] private float m_slidingFriction = 0.18f;

        public float SteeringStrength => m_steeringStrength;
        public float SlidingFriction => m_slidingFriction;

        public float GetSpeed(int carrierCount) => Mathf.Clamp(carrierCount, 1, 4) switch
        {
            1 => m_speedOnePlayer,
            2 => m_speedTwoPlayers,
            3 => m_speedThreePlayers,
            _ => m_speedFourPlayers
        };

        private void OnValidate()
        {
            m_speedOnePlayer = Mathf.Max(0.1f, m_speedOnePlayer);
            m_speedTwoPlayers = Mathf.Max(0.1f, m_speedTwoPlayers);
            m_speedThreePlayers = Mathf.Max(0.1f, m_speedThreePlayers);
            m_speedFourPlayers = Mathf.Max(0.1f, m_speedFourPlayers);
            m_steeringStrength = Mathf.Max(0f, m_steeringStrength);
            m_slidingFriction = Mathf.Clamp01(m_slidingFriction);
        }
    }
}
