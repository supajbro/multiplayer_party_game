using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    [DisallowMultipleComponent]
    public sealed class WeaponReticleView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup m_canvasGroup;
        [SerializeField] private Image m_centreDot;
        [SerializeField] private Image[] m_crosshairLines = new Image[4];
        [SerializeField] private Image[] m_circleSegments = new Image[8];
        [SerializeField] private CanvasGroup m_hitMarkerGroup;
        [SerializeField] private Image[] m_hitMarkerLines = new Image[4];

        private ReticleSettings m_settings;
        private float m_hitMarkerEndsAt;
        private float m_hitMarkerStartedAt;

        public void SetSettings(ReticleSettings settings)
        {
            m_settings = settings;
            bool visible = settings != null;
            if (m_canvasGroup != null) m_canvasGroup.alpha = visible ? 1f : 0f;
            if (!visible) return;
            bool circle = settings.Style == ReticleStyle.Circle;
            SetImagesActive(m_crosshairLines, !circle);
            SetImagesActive(m_circleSegments, circle);
            ConfigureHitMarker();
            UpdatePresentation(0f, settings.NormalColour);
        }

        public void UpdatePresentation(float expansion, Color colour)
        {
            if (m_settings == null) return;
            if (m_centreDot != null)
            {
                m_centreDot.color = colour;
                m_centreDot.rectTransform.sizeDelta = Vector2.one * m_settings.CentreDotSize;
            }

            if (m_settings.Style == ReticleStyle.Circle)
                UpdateCircle(expansion, colour);
            else
                UpdateCrosshair(expansion, colour);
        }

        public void PlayHitMarker(bool elimination)
        {
            if (m_settings == null || !m_settings.HitMarkerEnabled || m_hitMarkerGroup == null) return;
            m_hitMarkerStartedAt = Time.unscaledTime;
            m_hitMarkerEndsAt = m_hitMarkerStartedAt + m_settings.HitMarkerDuration;
            m_hitMarkerGroup.alpha = 1f;
            Color colour = elimination ? m_settings.EliminationColour : m_settings.HitMarkerColour;
            foreach (Image line in m_hitMarkerLines)
                if (line != null) line.color = colour;
        }

        private void Update()
        {
            if (m_hitMarkerGroup == null || m_hitMarkerGroup.alpha <= 0f) return;
            float duration = Mathf.Max(0.01f, m_hitMarkerEndsAt - m_hitMarkerStartedAt);
            float progress = Mathf.InverseLerp(m_hitMarkerStartedAt, m_hitMarkerEndsAt, Time.unscaledTime);
            float pop = 1f + Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI) * 0.28f;
            m_hitMarkerGroup.transform.localScale = Vector3.one * pop;
            m_hitMarkerGroup.alpha = 1f - Mathf.Clamp01((Time.unscaledTime - m_hitMarkerStartedAt) / duration);
        }

        private void UpdateCrosshair(float expansion, Color colour)
        {
            if (m_crosshairLines == null || m_crosshairLines.Length < 4) return;
            float length = m_settings.LineLength;
            float baseGap = Mathf.Max(m_settings.CentreGap,
                (m_settings.DefaultSize - length) * 0.5f);
            float distance = baseGap + expansion + length * 0.5f;
            SetLine(m_crosshairLines[0], new Vector2(distance, 0f), new Vector2(length, m_settings.LineThickness), colour);
            SetLine(m_crosshairLines[1], new Vector2(-distance, 0f), new Vector2(length, m_settings.LineThickness), colour);
            SetLine(m_crosshairLines[2], new Vector2(0f, distance), new Vector2(m_settings.LineThickness, length), colour);
            SetLine(m_crosshairLines[3], new Vector2(0f, -distance), new Vector2(m_settings.LineThickness, length), colour);
        }

        private void UpdateCircle(float expansion, Color colour)
        {
            if (m_circleSegments == null) return;
            float radius = m_settings.DefaultSize * 0.5f + expansion;
            for (int index = 0; index < m_circleSegments.Length; index++)
            {
                Image segment = m_circleSegments[index];
                if (segment == null) continue;
                float angle = index * Mathf.PI * 2f / m_circleSegments.Length;
                RectTransform rect = segment.rectTransform;
                rect.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                rect.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
                rect.sizeDelta = new Vector2(m_settings.LineLength, m_settings.LineThickness);
                segment.color = colour;
            }
        }

        private void ConfigureHitMarker()
        {
            if (m_hitMarkerGroup == null || m_hitMarkerLines == null) return;
            m_hitMarkerGroup.alpha = 0f;
            float size = m_settings.HitMarkerSize;
            for (int index = 0; index < m_hitMarkerLines.Length; index++)
            {
                Image line = m_hitMarkerLines[index];
                if (line == null) continue;
                float x = index % 2 == 0 ? 1f : -1f;
                float y = index < 2 ? 1f : -1f;
                line.rectTransform.anchoredPosition = new Vector2(x, y) * size * 0.42f;
                line.rectTransform.sizeDelta = new Vector2(size * 0.58f, Mathf.Max(2f, m_settings.LineThickness * 0.8f));
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, x * y > 0f ? 45f : -45f);
            }
        }

        private static void SetLine(Image image, Vector2 position, Vector2 size, Color colour)
        {
            if (image == null) return;
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            image.color = colour;
        }

        private static void SetImagesActive(Image[] images, bool active)
        {
            if (images == null) return;
            foreach (Image image in images)
                if (image != null) image.gameObject.SetActive(active);
        }
    }
}
