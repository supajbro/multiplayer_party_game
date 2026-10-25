using System;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Animates Inspector-authored HUD graphics; never creates UI objects at runtime.</summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup m_visibility;
        [SerializeField] private RectTransform m_motionRoot;
        [SerializeField] private CanvasGroup m_content;
        [SerializeField] private Text m_key;
        [SerializeField] private GameObject m_keycap;
        [SerializeField] private Text m_action;
        [SerializeField] private Image m_holdFill;
        [SerializeField] private GameObject m_holdTrack;
        [Header("Transitions (unscaled seconds)")]
        [SerializeField, Min(0.01f)] private float m_appearDuration = 0.22f;
        [SerializeField, Min(0.01f)] private float m_disappearDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float m_changeDuration = 0.18f;
        [SerializeField, Range(0.5f, 1f)] private float m_hiddenScale = 0.9f;
        [SerializeField, Range(0f, 0.2f)] private float m_overshoot = 0.06f;

        private string m_requested;
        private string m_displayed;
        private bool m_visible;
        private bool m_transitioning;
        private float m_time, m_startAlpha, m_startScale;
        private Vector3 m_authoredScale;
        private float m_contentAlpha = 1f;

        private void Awake()
        {
            m_authoredScale = m_motionRoot.localScale;
            m_visibility.alpha = 0f;
            m_visibility.interactable = false;
            m_visibility.blocksRaycasts = false;
            m_motionRoot.localScale = m_authoredScale * m_hiddenScale;
        }

        // Called once per local HUD frame, after interaction detection has run.
        public void Present(string prompt, float progress, float duration)
        {
            bool visible = !string.IsNullOrEmpty(prompt);
            m_requested = prompt;
            if (visible != m_visible)
            {
                m_visible = visible;
                m_time = 0f;
                m_startAlpha = m_visibility.alpha;
                m_startScale = m_motionRoot.localScale.x / m_authoredScale.x;
                m_transitioning = true;
                if (visible && m_startAlpha <= 0f)
                {
                    ApplyContent(prompt);
                    m_contentAlpha = 1f;
                    m_content.alpha = 1f;
                }
            }

            float dt = Time.unscaledDeltaTime;
            if (m_transitioning)
            {
                m_time += dt;
                float t = Mathf.Clamp01(m_time / Mathf.Max(0.01f, m_visible ? m_appearDuration : m_disappearDuration));
                float smooth = t * t * (3f - 2f * t);
                m_visibility.alpha = Mathf.Lerp(m_startAlpha, m_visible ? 1f : 0f, smooth);
                float scale = Mathf.Lerp(m_startScale, m_visible ? 1f : m_hiddenScale, smooth);
                // A small late bump settles exactly at the authored scale, including on reversal.
                if (m_visible) scale += Mathf.Sin(t * Mathf.PI) * t * m_overshoot * 2f;
                m_motionRoot.localScale = m_authoredScale * scale;
                if (t >= 1f) m_transitioning = false;
            }

            if (m_visible)
            {
                // Latest request wins while fading out. Retarget from the current alpha,
                // without restarting the backing animation or scheduling delayed callbacks.
                bool changing = !string.Equals(m_requested, m_displayed, StringComparison.Ordinal);
                m_contentAlpha = Mathf.MoveTowards(m_contentAlpha, changing ? 0f : 1f,
                    dt * 2f / Mathf.Max(0.01f, m_changeDuration));
                if (changing && m_contentAlpha <= 0f) ApplyContent(m_requested);
                if (m_content.alpha != m_contentAlpha) m_content.alpha = m_contentAlpha;
            }

            bool hold = visible && duration > 0f;
            if (m_holdTrack.activeSelf != hold) m_holdTrack.SetActive(hold);
            float fill = hold ? Mathf.Clamp01(progress / duration) : 0f;
            if (m_holdFill.fillAmount != fill) m_holdFill.fillAmount = fill;
        }

        private void ApplyContent(string prompt)
        {
            m_displayed = prompt;
            bool hold = prompt.StartsWith("Hold ", StringComparison.Ordinal);
            bool press = prompt.StartsWith("Press ", StringComparison.Ordinal);
            int separator = hold || press ? prompt.IndexOf(" to ", StringComparison.Ordinal) : -1;
            bool hasKey = separator > (hold ? 5 : 6);
            m_keycap.SetActive(hasKey);
            if (hasKey)
            {
                int start = hold ? 5 : 6;
                m_key.text = prompt.Substring(start, separator - start);
                string action = prompt.Substring(separator + 4);
                m_action.text = hold ? "Hold to " + action : action;
            }
            else m_action.text = prompt;
        }
    }
}
