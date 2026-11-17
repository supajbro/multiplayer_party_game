using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Saved marker artwork: the head stays upright while only the heading rotates.</summary>
    public sealed class MinimapPlayerMarker : MonoBehaviour
    {
        [SerializeField] private Image m_head;
        [SerializeField] private Image m_direction;
        public void SetAppearance(Color colour, float heading)
        {
            if (m_head != null) m_head.color = colour;
            if (m_direction == null) return;
            m_direction.color = colour;
            m_direction.rectTransform.localRotation = Quaternion.Euler(0f, 0f, heading);
        }
    }
}
