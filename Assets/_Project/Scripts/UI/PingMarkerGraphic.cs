using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    // Vector artwork stays sharp on both maps and the world-space canvas.
    public sealed class PingMarkerGraphic : MaskableGraphic
    {
        [SerializeField] private bool m_beacon;
        [SerializeField] private Color m_outline = new(.025f, .08f, .13f, 1f);
        [SerializeField] private Color m_accent = Color.white;
        public Color Accent
        {
            set { if (m_accent == value) return; m_accent = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float unit = Mathf.Min(r.width / 64f, r.height / (m_beacon ? 164f : 64f));
            Vector2 origin = new(r.center.x, r.yMin);
            if (m_beacon)
            {
                Quad(vh, origin + new Vector2(-5, 5) * unit, origin + new Vector2(5, 100) * unit,
                    new Color(color.r, color.g, color.b, .13f));
                Quad(vh, origin + new Vector2(-1.5f, 5) * unit, origin + new Vector2(1.5f, 98) * unit, color);
                Ring(vh, origin + Vector2.up * 7 * unit, new Vector2(29, 6) * unit, .77f, color);
            }
            float y = m_beacon ? 120 : 40;
            Vector2 centre = origin + Vector2.up * y * unit;
            Triangle(vh, centre + new Vector2(-24, -6) * unit,
                centre + new Vector2(24, -6) * unit, centre + Vector2.down * 39 * unit, m_outline);
            Disc(vh, centre, 27 * unit, m_outline);
            Triangle(vh, centre + new Vector2(-21, -6) * unit,
                centre + new Vector2(21, -6) * unit, centre + Vector2.down * 34 * unit, color);
            Disc(vh, centre, 23 * unit, color);
            Disc(vh, centre + Vector2.up * 2 * unit, 11 * unit, m_outline);
            Disc(vh, centre + Vector2.up * 2 * unit, 7 * unit, m_accent);
        }

        private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color colour)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, colour, Vector2.zero); vh.AddVert(b, colour, Vector2.zero); vh.AddVert(c, colour, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }

        private static void Quad(VertexHelper vh, Vector2 min, Vector2 max, Color colour)
        {
            Triangle(vh, min, new Vector2(min.x, max.y), max, colour);
            Triangle(vh, min, max, new Vector2(max.x, min.y), colour);
        }

        private static void Disc(VertexHelper vh, Vector2 centre, float radius, Color colour)
        {
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                Triangle(vh, centre, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                    centre + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, colour);
            }
        }

        private static void Ring(VertexHelper vh, Vector2 centre, Vector2 radius, float inner, Color colour)
        {
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI / 16, b = (i + 1) * Mathf.PI / 16;
                Vector2 p = Vector2.Scale(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), radius);
                Vector2 q = Vector2.Scale(new Vector2(Mathf.Cos(b), Mathf.Sin(b)), radius);
                Triangle(vh, centre + p, centre + q, centre + q * inner, colour);
                Triangle(vh, centre + p, centre + q * inner, centre + p * inner, colour);
            }
        }
    }
}
