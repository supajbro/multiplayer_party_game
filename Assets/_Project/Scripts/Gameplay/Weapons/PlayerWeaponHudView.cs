using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed class PlayerWeaponHudView : MonoBehaviour
    {
        [SerializeField] public RectTransform m_minimapRoot;
        [SerializeField] public Text m_healthText;
        [SerializeField] public Text m_deliveryTitle;
        [SerializeField] public Text m_deliveryDetail;
        [SerializeField] public Text m_deliveryDistance;
        [SerializeField] public Text m_currencyText;
        [SerializeField] public Text m_timerText;
        [SerializeField] public Text m_failureText;
        [SerializeField] public Text m_ammoText;
        [SerializeField] public PlayerStatusBar m_healthBar;
        [SerializeField] public PlayerStatusBar m_staminaBar;
        [SerializeField] public Image m_timerFill;
        [SerializeField] public GameObject m_timerPanel;
        [SerializeField] public Image[] m_slotBackgrounds;
        [SerializeField] public Outline[] m_slotOutlines;
        [SerializeField] public GameObject m_ammoPanel;
    }
}
