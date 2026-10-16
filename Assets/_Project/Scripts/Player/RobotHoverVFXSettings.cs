using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Artist-facing tuning shared by every locally simulated robot hover effect.</summary>
    [CreateAssetMenu(fileName = "RobotHoverVFXSettings", menuName = "Couch Guys/Robot Hover VFX Settings")]
    public sealed class RobotHoverVFXSettings : ScriptableObject
    {
        [SerializeField] private float m_ringRotationSpeed = 135f;
        [SerializeField, Range(0.03f, 0.06f)] private float m_hoverBobAmplitude = 0.045f;
        [SerializeField, Min(0.01f)] private float m_hoverBobFrequency = 1.8f;
        [SerializeField, Min(0f)] private float m_emissionBrightness = 3.2f;
        [SerializeField, Min(0f)] private float m_emissionPulseSpeed = 2.1f;
        [SerializeField, Min(0f)] private float m_rippleSpawnRate = 1.25f;
        [SerializeField, Min(0f)] private float m_particleEmissionRate = 9f;
        [SerializeField, Min(0f)] private float m_groundGlowIntensity = 0.55f;

        public float RingRotationSpeed => m_ringRotationSpeed;
        public float HoverBobAmplitude => m_hoverBobAmplitude;
        public float HoverBobFrequency => m_hoverBobFrequency;
        public float EmissionBrightness => m_emissionBrightness;
        public float EmissionPulseSpeed => m_emissionPulseSpeed;
        public float RippleSpawnRate => m_rippleSpawnRate;
        public float ParticleEmissionRate => m_particleEmissionRate;
        public float GroundGlowIntensity => m_groundGlowIntensity;

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_hoverBobAmplitude = Mathf.Clamp(m_hoverBobAmplitude, 0.03f, 0.06f);
            m_hoverBobFrequency = Mathf.Max(0.01f, m_hoverBobFrequency);
            m_emissionBrightness = Mathf.Max(0f, m_emissionBrightness);
            m_emissionPulseSpeed = Mathf.Max(0f, m_emissionPulseSpeed);
            m_rippleSpawnRate = Mathf.Max(0f, m_rippleSpawnRate);
            m_particleEmissionRate = Mathf.Max(0f, m_particleEmissionRate);
            m_groundGlowIntensity = Mathf.Max(0f, m_groundGlowIntensity);
        }
#endif
    }
}
