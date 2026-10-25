using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Local 3D footsteps from replicated motion; alerts come from server events.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefAudioEmitter : MonoBehaviour
    {
        [SerializeField] private ThiefSettings m_settings;

        private AudioClip m_footstepLoop => m_settings != null
            ? m_settings.Audio.FootstepLoop : null;
        private AudioClip m_alertClip => m_settings != null
            ? m_settings.Audio.AlertClip : null;
        private float m_footstepVolume => m_settings != null
            ? m_settings.Audio.FootstepVolume : 0.55f;
        private float m_alertVolume => m_settings != null
            ? m_settings.Audio.AlertVolume : 1f;
        private float m_movingThreshold => m_settings != null
            ? m_settings.Audio.MovingThreshold : 0.12f;

        private AudioSource m_footsteps;
        private AudioSource m_alert;
        private ThiefHealth m_health;
        private ThiefNavigator m_navigator;
        private Vector3 m_previousPosition;
        private bool m_localStarted;

        public void StartLocal()
        {
            m_health = GetComponent<ThiefHealth>();
            m_navigator = GetComponent<ThiefNavigator>();
            m_previousPosition = transform.position;
            m_localStarted = true;
            if (m_footsteps == null && m_footstepLoop != null)
            {
                m_footsteps = CreateSource("Footsteps", m_settings != null ? m_settings.Audio.FootstepMinimumDistance : 2f,
                    m_settings != null ? m_settings.Audio.FootstepMaximumDistance : 20f);
                m_footsteps.clip = m_footstepLoop;
                m_footsteps.loop = true;
                m_footsteps.volume = m_footstepVolume;
            }
            if (m_alert == null && m_alertClip != null)
                m_alert = CreateSource("Alert", m_settings != null ? m_settings.Audio.AlertMinimumDistance : 4f,
                    m_settings != null ? m_settings.Audio.AlertMaximumDistance : 30f);
        }

        private void LateUpdate()
        {
            if (!m_localStarted) return;
            Vector3 displacement = Vector3.ProjectOnPlane(transform.position - m_previousPosition, Vector3.up);
            m_previousPosition = transform.position;
            if (m_health != null && !m_health.IsAlive)
            {
                StopLocal();
                return;
            }
            if (m_footsteps == null) return;
            float speed = Time.deltaTime > 0f ? displacement.magnitude / Time.deltaTime : 0f;
            bool moving = speed >= m_movingThreshold && displacement.sqrMagnitude < 9f &&
                (m_navigator == null || !m_navigator.IsAlerting) &&
                (m_health == null || !m_health.IsBeingKnockedBack);
            if (moving)
            {
                float referenceSpeed = m_settings != null ? m_settings.Movement.Speed : 4.5f;
                m_footsteps.pitch = Mathf.Clamp(speed / Mathf.Max(0.1f, referenceSpeed), 0.65f, 1.25f);
                if (!m_footsteps.isPlaying) m_footsteps.Play();
            }
            else if (m_footsteps.isPlaying) m_footsteps.Stop();
        }

        public void PlayAlert()
        {
            if (!m_localStarted || m_alert == null || (m_health != null && !m_health.IsAlive)) return;
            if (m_footsteps != null) m_footsteps.Stop();
            m_alert.PlayOneShot(m_alertClip, m_alertVolume);
        }

        public void StopLocal()
        {
            m_localStarted = false;
            if (m_footsteps != null) m_footsteps.Stop();
            if (m_alert != null) m_alert.Stop();
        }

        private void OnDisable() => StopLocal();

        private AudioSource CreateSource(string sourceName, float minDistance, float maxDistance)
        {
            var voice = new GameObject(sourceName);
            voice.transform.SetParent(transform, false);
            AudioSource source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
