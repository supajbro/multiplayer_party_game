using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    // Editable annular sector; generates only UI mesh geometry, never a hierarchy.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EmoteWheelSector : MaskableGraphic
    {
        [Range(0, 5)] public int Segment;
        [Range(0, 1)] public float InnerRadius = .43f;
        [Range(0, 10)] public float GapDegrees = 2f;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 centre = rectTransform.rect.center;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .5f;
            const int steps = 20;
            for (int i = 0; i <= steps; i++)
            {
                float angle = (Segment * 60f - 30f + GapDegrees * .5f +
                    (60f - GapDegrees) * i / steps) * Mathf.Deg2Rad;
                Vector2 direction = new(Mathf.Sin(angle), Mathf.Cos(angle));
                vh.AddVert(centre + direction * radius * InnerRadius, color, Vector2.zero);
                vh.AddVert(centre + direction * radius, color, Vector2.zero);
                if (i == steps) continue;
                int index = i * 2;
                vh.AddTriangle(index, index + 1, index + 3);
                vh.AddTriangle(index, index + 3, index + 2);
            }
        }
    }
}
