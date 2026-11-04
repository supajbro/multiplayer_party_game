using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed class DeliveryTimerHudView : MonoBehaviour
    {
        [SerializeField] private Image m_fill;
        [SerializeField] private TMP_Text m_countdown;
        [SerializeField] private TMP_Text m_identifier;
        private int m_lastSecond = -1;
        public int DeliveryId { get; private set; }

        public void Bind(int id, string label)
        {
            DeliveryId = id;
            m_identifier.text = label;
        }

        public void Present(PlayerWeaponHudView style, float remaining, float duration, bool carrying)
        {
            int second = Mathf.Max(0, Mathf.CeilToInt(remaining));
            bool reset = m_lastSecond < 0;
            if (m_lastSecond != second)
            {
                m_countdown.text = $"{second / 60:00}:{second % 60:00}";
                m_lastSecond = second;
            }
            style.PresentDeliveryTimer(duration > 0f ? remaining / duration : 0f, reset, m_fill, m_countdown);
            m_identifier.color = carrying ? new Color(1f, 0.7f, 0.08f) : new Color(0.4f, 0.95f, 1f);
        }
    }
}
