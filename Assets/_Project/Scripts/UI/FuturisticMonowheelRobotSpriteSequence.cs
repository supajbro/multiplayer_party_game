using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class FuturisticMonowheelRobotSpriteSequence : MonoBehaviour
    {
        [SerializeField] private Image m_sprite;
        [SerializeField] private Sprite[] m_frames;
        [SerializeField, Min(0.01f)] private float m_cycleDuration = 1.25f;
        [SerializeField, Min(0f)] private float m_travelDuration = 1.25f;
        [SerializeField, Min(0f)] private float m_offscreenPadding = 16f;

        private Vector2 m_centreAnchor;
        private Vector2 m_centrePosition;
        private bool m_playing;
        private float m_playbackStartTime;

        public void PrepareEntrance()
        {
            RectTransform rect = m_sprite.rectTransform;
            m_centreAnchor = rect.anchorMin;
            m_centrePosition = rect.anchoredPosition;
            SetPosition(new Vector2(0f, m_centreAnchor.y), OffscreenPosition(false));
            m_playing = false;
            if (m_frames.Length > 0)
                m_sprite.sprite = m_frames[0];
        }

        public IEnumerator Enter()
        {
            m_playbackStartTime = Time.unscaledTime;
            m_playing = true;
            return Travel(false);
        }

        public IEnumerator Exit() => Travel(true);

        private void Update()
        {
            if (!m_playing || m_frames.Length == 0) return;

            float elapsed = Time.unscaledTime - m_playbackStartTime;
            float cycle = Mathf.Repeat(elapsed / m_cycleDuration, 1f);
            int frame = Mathf.Min((int)(cycle * m_frames.Length), m_frames.Length - 1);
            m_sprite.sprite = m_frames[frame];
        }

        private IEnumerator Travel(bool exiting)
        {
            Vector2 edgeAnchor = new Vector2(exiting ? 1f : 0f, m_centreAnchor.y);
            Vector2 edgePosition = OffscreenPosition(exiting);
            Vector2 startAnchor = exiting ? m_centreAnchor : edgeAnchor;
            Vector2 endAnchor = exiting ? edgeAnchor : m_centreAnchor;
            Vector2 startPosition = exiting ? m_centrePosition : edgePosition;
            Vector2 endPosition = exiting ? edgePosition : m_centrePosition;

            for (float elapsed = 0f; elapsed < m_travelDuration; elapsed += Time.unscaledDeltaTime)
            {
                float travel = Mathf.SmoothStep(0f, 1f, elapsed / m_travelDuration);
                SetPosition(Vector2.Lerp(startAnchor, endAnchor, travel),
                    Vector2.Lerp(startPosition, endPosition, travel));
                yield return null;
            }

            SetPosition(endAnchor, endPosition);
        }

        private Vector2 OffscreenPosition(bool right)
        {
            RectTransform rect = m_sprite.rectTransform;
            float width = rect.rect.width * Mathf.Abs(rect.localScale.x);
            float distance = width * (right ? rect.pivot.x : 1f - rect.pivot.x) + m_offscreenPadding;
            return new Vector2(right ? distance : -distance, m_centrePosition.y);
        }

        private void SetPosition(Vector2 anchor, Vector2 position)
        {
            RectTransform rect = m_sprite.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
        }
    }
}
