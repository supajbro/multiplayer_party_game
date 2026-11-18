using CouchGuys.Player;
using TMPro;
using UnityEngine;

namespace CouchGuys.UI
{
    public sealed class EmoteWheelView : MonoBehaviour
    {
        [SerializeField] private RectTransform m_wheel;
        [SerializeField] private EmoteWheelSector[] m_segments;
        [SerializeField] private TMP_Text[] m_labels;
        [SerializeField] private TMP_Text m_name;
        [SerializeField] private TMP_Text m_preview;
        [SerializeField] private Color m_normal = new(.055f, .07f, .09f, .96f);
        [SerializeField] private Color m_selected = new(.46f, .12f, .78f, 1);
        [SerializeField, Range(0, 1)] private float m_deadZone = .43f;
        public int Selected { get; private set; } = -1;

        public static int SelectSegment(Vector2 point, float inner, float outer)
        {
            float length = point.magnitude;
            if (length < inner || length > outer) return -1;
            float angle = Mathf.Repeat(Mathf.Atan2(point.x, point.y) * Mathf.Rad2Deg + 30f, 360f);
            return Mathf.FloorToInt(angle / 60f);
        }

        public void Present(EmoteDefinition[] definitions, Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_wheel, screenPoint, null, out Vector2 point);
            float radius = Mathf.Min(m_wheel.rect.width, m_wheel.rect.height) * .5f;
            Selected = SelectSegment(point, radius * m_deadZone, radius);
            for (int i = 0; i < m_segments.Length; i++)
            {
                m_segments[i].color = i == Selected ? m_selected : m_normal;
                m_labels[i].text = i < definitions.Length && definitions[i] != null ?
                    $"<size=30><color=#FFAA14>{i + 1:00}</color></size>\n{definitions[i].DisplayName}" : "";
                m_labels[i].color = i == Selected ? new Color(.2f, 1, 1) : Color.white;
            }
            EmoteDefinition chosen = Selected >= 0 && Selected < definitions.Length ? definitions[Selected] : null;
            m_name.text = chosen != null ? chosen.DisplayName : "EMOTES";
            m_preview.text = chosen != null ? chosen.PreviewLabel + (chosen.Loop ? "\nLOOP" : "\nONE SHOT") : "Point to select\nRelease B to play";
        }
    }
}
