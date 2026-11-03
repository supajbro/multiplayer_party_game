using UnityEngine;

namespace CouchGuys.Gameplay.Depot
{
    /// <summary>Local music only. DepotController supplies its existing ownership/bounds decision.</summary>
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodMusic : MonoBehaviour
    {
        [SerializeField] private AudioSource m_source;
        [SerializeField] private AudioClip m_musicClip;
        [SerializeField, Range(0f, 1f)] private float m_maximumVolume = 0.15f;
        [SerializeField, Min(0f)] private float m_fadeInDuration = 2.5f;
        [SerializeField, Min(0f)] private float m_fadeOutDuration = 2f;
        [SerializeField, Min(0f)] private float m_minimumPlayDuration = 120f;
        [SerializeField, Min(0f)] private float m_maximumPlayDuration = 240f;
        [SerializeField, Min(0f)] private float m_minimumSilenceDuration = 60f;
        [SerializeField, Min(0f)] private float m_maximumSilenceDuration = 90f;
        [SerializeField, Min(0f)] private float m_breakFadeOutDuration = 3f;
        [SerializeField, Min(0f)] private float m_breakFadeInDuration = 3f;
        [SerializeField, Range(0f, 1f)] private float m_combatVolumeMultiplier = 0.6f;
        [SerializeField, Min(0f)] private float m_combatReductionDuration = 1f;
        [SerializeField, Min(0f)] private float m_combatRestoreDuration = 2f;

        private enum Cycle { Playing, FadingOut, Silent }
        private Cycle m_cycle;
        private bool m_outside, m_started, m_paused, m_inCombat;
        private float m_level, m_combatGain = 1f, m_timer, m_fadeDuration;

        private void Awake()
        {
            if (m_source == null) return;
            m_source.playOnAwake = false;
            m_source.loop = true;
            m_source.spatialBlend = 0f;
            m_source.clip = m_musicClip;
            m_source.volume = 0f;
        }

        /// <summary>
        /// Integration hook for a future reliable local combat state (never a remote player's state).
        /// Enemy targeting currently runs on the server, so it must not drive client music directly.
        /// </summary>
        public void SetCombatActive(bool active) => m_inCombat = active;

        // Called by the depot's existing update; no extra player searches, triggers or Update polling.
        internal void TickLocalMusic(bool outside)
        {
            if (!isActiveAndEnabled || m_source == null || m_musicClip == null) return;
            float delta = Time.unscaledDeltaTime;
            if (outside != m_outside)
            {
                m_outside = outside;
                m_cycle = Cycle.Playing;
                m_timer = 0f;
                m_fadeDuration = outside ? m_fadeInDuration : m_fadeOutDuration;
                if (outside)
                {
                    Resume();
                    m_timer = NextPlayDuration();
                }
            }

            if (outside)
            {
                if (m_cycle == Cycle.Playing || m_cycle == Cycle.Silent)
                {
                    m_timer -= delta;
                    if (m_timer <= 0f)
                    {
                        if (m_cycle == Cycle.Playing)
                        {
                            m_cycle = Cycle.FadingOut;
                            m_fadeDuration = m_breakFadeOutDuration;
                        }
                        else
                        {
                            m_cycle = Cycle.Playing;
                            m_fadeDuration = m_breakFadeInDuration;
                            Resume();
                            m_timer = NextPlayDuration();
                        }
                    }
                }
            }

            bool audible = outside && m_cycle == Cycle.Playing;
            m_level = Move(m_level, audible ? 1f : 0f, m_fadeDuration, delta);
            float combatTarget = m_inCombat ? Mathf.Clamp01(m_combatVolumeMultiplier) : 1f;
            float combatDuration = m_inCombat ? m_combatReductionDuration : m_combatRestoreDuration;
            // Independent gain prevents combat changes from overriding boundary/break envelopes.
            float combatRange = 1f - Mathf.Clamp01(m_combatVolumeMultiplier);
            m_combatGain = combatDuration <= 0f ? combatTarget : Mathf.MoveTowards(
                m_combatGain, combatTarget, combatRange * delta / combatDuration);
            m_source.volume = Mathf.Clamp01(m_maximumVolume) * m_level * m_combatGain;
            if (!audible && m_level == 0f)
            {
                Pause();
                if (outside && m_cycle == Cycle.FadingOut)
                {
                    m_cycle = Cycle.Silent;
                    m_timer = RandomDuration(m_minimumSilenceDuration, m_maximumSilenceDuration);
                }
            }
        }

        private float NextPlayDuration()
        {
            float minimum = Mathf.Max(0f, m_minimumPlayDuration);
            float maximum = Mathf.Max(minimum, m_maximumPlayDuration);
            float duration = Random.Range(minimum, maximum);
            float length = m_musicClip.length;
            if (length <= 0f) return duration;
            // Prefer ending the fade at a loop boundary, but keep the configured play range.
            float position = m_source.time + Mathf.Max(0f, m_breakFadeOutDuration);
            float boundary = Mathf.Ceil((position + duration) / length) * length - position;
            if (boundary > maximum) boundary -= length;
            return boundary >= minimum && boundary <= maximum ? boundary : duration;
        }

        private void Resume()
        {
            if (!m_started) { m_source.Play(); m_started = true; }
            else if (m_paused) m_source.UnPause();
            m_paused = false;
        }

        private void Pause()
        {
            if (m_source == null) return;
            m_source.volume = 0f;
            if (m_started && !m_paused) m_source.Pause();
            m_paused = m_started;
        }

        internal void Suspend()
        {
            Pause();
            m_outside = false;
            m_level = m_timer = 0f;
            m_cycle = Cycle.Playing;
            m_inCombat = false;
            m_combatGain = 1f;
        }

        private void OnDisable() => Suspend();

        private static float Move(float current, float target, float duration, float delta) =>
            duration <= 0f ? target : Mathf.MoveTowards(current, target, delta / duration);

        private static float RandomDuration(float minimum, float maximum) =>
            Random.Range(Mathf.Max(0f, minimum), Mathf.Max(Mathf.Max(0f, minimum), maximum));
    }
}
