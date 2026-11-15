using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    /// <summary>A smooth rounded UI surface and stencil mask for the terminal's glass.</summary>
    [AddComponentMenu("Couch Guys/UI/Terminal Screen Graphic")]
    public sealed class TerminalScreenGraphic : MaskableGraphic, ICanvasRaycastFilter
    {
        [SerializeField, Min(0f)] private float m_cornerRadius = 110f;
        [SerializeField, Range(4, 64)] private int m_cornerSegments = 16;

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect rect = GetPixelAdjustedRect();
            float radius = Mathf.Clamp(m_cornerRadius, 0f, Mathf.Min(rect.width, rect.height) * .5f);
            int segments = Mathf.Clamp(m_cornerSegments, 4, 64);
            vertices.AddVert(rect.center, color, new Vector2(.5f, .5f));

            // Walk the four quarter-circles in order. Straight edges connect their endpoints.
            for (int corner = 0; corner < 4; corner++)
            {
                float centreX = corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius;
                float centreY = corner < 2 ? rect.yMax - radius : rect.yMin + radius;
                for (int step = 0; step <= segments; step++)
                {
                    float angle = (corner * 90f + step * 90f / segments) * Mathf.Deg2Rad;
                    Vector2 point = new Vector2(centreX + Mathf.Cos(angle) * radius, centreY + Mathf.Sin(angle) * radius);
                    vertices.AddVert(point, color, new Vector2(.5f, .5f));
                }
            }

            int perimeterCount = vertices.currentVertCount - 1;
            for (int index = 1; index <= perimeterCount; index++)
                vertices.AddTriangle(0, index, index == perimeterCount ? 1 : index + 1);
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out Vector2 localPoint))
                return false;
            Rect rect = rectTransform.rect;
            if (!rect.Contains(localPoint)) return false;
            float radius = Mathf.Clamp(m_cornerRadius, 0f, Mathf.Min(rect.width, rect.height) * .5f);
            Vector2 offset = localPoint - rect.center;
            float x = Mathf.Max(0f, Mathf.Abs(offset.x) - rect.width * .5f + radius);
            float y = Mathf.Max(0f, Mathf.Abs(offset.y) - rect.height * .5f + radius);
            return x * x + y * y <= radius * radius;
        }
    }
}
