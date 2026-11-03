using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Only plays saved weapon components; never constructs a visual hierarchy.</summary>
    public sealed class BaseballBatPresentation : MonoBehaviour
    {
        [SerializeField] private TrailRenderer m_trail;
        [SerializeField] private AudioSource m_audio;
        [SerializeField] private AudioClip m_swingSound;
        [SerializeField] private AudioClip m_impactSound;
        private float m_startedAt = float.NegativeInfinity;
        private bool m_sounded;

        public void PlaySwing(float age)
        {
            m_startedAt = Time.time - age;
            m_sounded = false;
            if (m_trail != null) { m_trail.Clear(); m_trail.emitting = false; }
        }

        public void PlayImpact()
        {
            if (m_audio != null && m_impactSound != null) m_audio.PlayOneShot(m_impactSound, .9f);
        }

        private void Update()
        {
            float age = Time.time - m_startedAt;
            bool active = age >= BaseballBatAssets.ActiveStart && age <= BaseballBatAssets.ActiveEnd;
            if (m_trail != null) m_trail.emitting = active;
            if (!m_sounded && age >= BaseballBatAssets.ActiveStart && age < BaseballBatAssets.SwingDuration)
            {
                m_sounded = true;
                if (m_audio != null && m_swingSound != null) m_audio.PlayOneShot(m_swingSound, .6f);
            }
        }

        private void OnDisable()
        {
            m_startedAt = float.NegativeInfinity;
            if (m_trail != null) { m_trail.emitting = false; m_trail.Clear(); }
            if (m_audio != null) m_audio.Stop();
        }
    }
}
