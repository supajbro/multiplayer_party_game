using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    // A hollow UI graphic avoids a new texture and never draws over the reticle centre.
    [AddComponentMenu("UI/Weapon Reload Ring")]
    public sealed class WeaponReloadRing : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] private float m_progress;
        [SerializeField, Min(1f)] private float m_thickness = 4f;
        [SerializeField, Min(0f)] private float m_glowWidth = 5f;
        [SerializeField, Range(0f, 1f)] private float m_glowIntensity = 0.3f;
        [SerializeField, Range(16, 192)] private int m_segments = 96;

        public float Progress
        {
            get => m_progress;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(m_progress, value)) return;
                m_progress = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            float outer = Mathf.Min(rect.width, rect.height) * 0.5f - m_glowWidth;
            float inner = Mathf.Max(0f, outer - m_thickness);
            Color glow = color;
            glow.a *= m_glowIntensity;
            Color clear = color;
            clear.a = 0f;
            Band(vh, rect.center, outer, outer + m_glowWidth, glow, clear);
            Band(vh, rect.center, Mathf.Max(0f, inner - m_glowWidth), inner, clear, glow);
            Band(vh, rect.center, inner, outer, color, color);
        }

        private void Band(VertexHelper vh, Vector2 centre, float inner, float outer, Color inside, Color outside)
        {
            int count = Mathf.CeilToInt(m_segments * m_progress);
            for (int i = 0; i < count; i++)
            {
                float start = Mathf.Min(i / (float)m_segments, m_progress) * Mathf.PI * 2f;
                float end = Mathf.Min((i + 1f) / m_segments, m_progress) * Mathf.PI * 2f;
                Vector2 a = new(Mathf.Sin(start), Mathf.Cos(start));
                Vector2 b = new(Mathf.Sin(end), Mathf.Cos(end));
                int index = vh.currentVertCount;
                vh.AddVert(centre + a * inner, inside, Vector2.zero);
                vh.AddVert(centre + a * outer, outside, Vector2.zero);
                vh.AddVert(centre + b * outer, outside, Vector2.zero);
                vh.AddVert(centre + b * inner, inside, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
