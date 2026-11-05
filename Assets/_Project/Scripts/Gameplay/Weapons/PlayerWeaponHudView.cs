using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed class PlayerWeaponHudView : MonoBehaviour
    {
        [SerializeField] public InteractionPromptView m_interactionPrompt;
        [SerializeField] public RectTransform m_minimapRoot;
        [Tooltip("Decorative frame only; adjust its RectTransform independently of the live map.")]
        [SerializeField] public Image m_minimapFrame;
        [SerializeField] public RectTransform m_expandedMapRoot;
        [SerializeField] public Text m_healthText;
        [SerializeField] public Text m_downedText;
        [SerializeField] public Text m_deliveryTitle;
        [SerializeField] public Text m_deliveryDetail;
        [SerializeField] public Text m_deliveryDistance;
        [SerializeField] public Text m_currencyText;
        [SerializeField] public Image m_currencyHolder;
        [SerializeField] public Image m_currencySymbol;
        [SerializeField] public TMP_Text m_timerText;
        [SerializeField] public Text m_failureText;
        [SerializeField] public Text m_ammoText;
        [SerializeField] public Slider m_grappleChargeSlider;
        [SerializeField] public PlayerStatusBar m_healthBar;
        [SerializeField] public PlayerStatusBar m_staminaBar;
        [SerializeField] public Image m_timerFill;
        [SerializeField] public GameObject m_timerPanel;
        [SerializeField] public Image[] m_slotBackgrounds;
        [SerializeField] public Outline[] m_slotOutlines;
        [SerializeField] public GameObject m_ammoPanel;

        [SerializeField] public Sprite m_deliveryCouchSprite;
        [SerializeField] public DeliveryTimerHudView m_deliveryTimerTemplate;
        [SerializeField, Min(0f)] public float m_deliveryTimerSpacing = 4f;
        [SerializeField, Min(0f)] public float m_deliveryTimerBottomMargin = 204f;

        [Header("Delivery timer")]
        [SerializeField] private Color m_timerFillColour = new(0.05f, 0.9f, 1f, 1f);
        [SerializeField] private Color m_timerCountdownColour = new(1f, 0.7f, 0.08f, 1f);
        [SerializeField, Range(0f, 1f)] private float m_timerWarningThreshold = 0.3f;
        [SerializeField, Range(0f, 1f)] private float m_timerCriticalThreshold = 0.1f;
        [SerializeField] private Color m_timerWarningColour = new(1f, 0.42f, 0.06f, 1f);
        [SerializeField] private Color m_timerCriticalColour = new(1f, 0.15f, 0.1f, 1f);
        [SerializeField, Range(0f, 0.5f)] private float m_timerPulseIntensity = 0.18f;
        [Tooltip("Critical fill pulses per second.")]
        [SerializeField, Min(0f)] private float m_timerPulseSpeed = 2f;
        [Tooltip("Visual response to network ticks; zero disables smoothing.")]
        [SerializeField, Min(0f)] private float m_timerFillResponse = 18f;

        public void PresentDeliveryTimer(float ratio, bool reset)
            => PresentDeliveryTimer(ratio, reset, m_timerFill, m_timerText);

        public void PresentDeliveryTimer(float ratio, bool reset, Image fill, TMP_Text countdown)
        {
            ratio = Mathf.Clamp01(ratio);
            bool critical = ratio <= Mathf.Min(m_timerCriticalThreshold, m_timerWarningThreshold);
            Color colour = critical ? m_timerCriticalColour :
                ratio <= m_timerWarningThreshold ? m_timerWarningColour : m_timerFillColour;
            if (critical)
                colour.a *= 1f - m_timerPulseIntensity *
                    (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * m_timerPulseSpeed * Mathf.PI * 2f));
            if (fill != null)
            {
                fill.fillAmount = reset || ratio <= 0f || m_timerFillResponse <= 0f ? ratio :
                    Mathf.Lerp(fill.fillAmount, ratio,
                        1f - Mathf.Exp(-m_timerFillResponse * Time.unscaledDeltaTime));
                fill.color = colour;
            }
            if (countdown != null)
                countdown.color = critical ? m_timerCriticalColour : m_timerCountdownColour;
        }

        [Header("Item hotbar")]
        [SerializeField] private Image m_hotbarHolder;
        [SerializeField] private Image m_activeSlotHighlight;
        [SerializeField] private RectTransform[] m_hotbarSlots;
        [SerializeField] private Vector2 m_highlightOffset;
        [SerializeField] private Vector2 m_highlightScale = Vector2.one;
        [SerializeField, Min(0f)] private float m_highlightAnimationDuration = 0.12f;
        private int m_highlightSlot = -1;
        private Vector3 m_highlightFrom;
        private float m_highlightElapsed;

        public void PresentHotbar(int selected)
        {
            if (m_activeSlotHighlight == null) return;
            bool visible = m_hotbarSlots != null && selected >= 0 && selected < m_hotbarSlots.Length &&
                m_hotbarSlots[selected] != null;
            m_activeSlotHighlight.gameObject.SetActive(visible);
            if (!visible)
            {
                m_highlightSlot = -1;
                return;
            }
            RectTransform highlight = m_activeSlotHighlight.rectTransform;
            RectTransform slot = m_hotbarSlots[selected];
            Vector3 target = highlight.parent.InverseTransformPoint(slot.TransformPoint(slot.rect.center));
            target += (Vector3)m_highlightOffset;
            if (selected != m_highlightSlot)
            {
                m_highlightFrom = m_highlightSlot < 0 ? target : highlight.localPosition;
                m_highlightSlot = selected;
                m_highlightElapsed = 0f;
            }
            m_highlightElapsed += Time.unscaledDeltaTime;
            float t = m_highlightAnimationDuration > 0f
                ? Mathf.Clamp01(m_highlightElapsed / m_highlightAnimationDuration) : 1f;
            highlight.localPosition = Vector3.Lerp(m_highlightFrom, target, Mathf.SmoothStep(0f, 1f, t));
            highlight.localScale = new Vector3(m_highlightScale.x, m_highlightScale.y, 1f);
        }

        [Header("Weapon display (independent of reticle)")]
        [SerializeField] private CanvasGroup m_ammoGroup;
        [SerializeField] private WeaponReloadRing m_reloadRing;
        [SerializeField] private Outline m_ammoGlow;
        [SerializeField] private Outline m_chargeGlow;
        [SerializeField] private Shadow m_ammoShadow;
        [SerializeField] private Shadow m_chargeShadow;
        [SerializeField] private Color m_weaponColour = new(0.15f, 0.94f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float m_glowIntensity = 0.35f;
        [SerializeField, Min(0f)] private float m_glowSpread = 2f;
        [SerializeField] private Color m_shadowColour = new(0f, 0.03f, 0.06f, 0.8f);
        [SerializeField] private Vector2 m_shadowOffset = new(1f, -2f);
        [SerializeField, Min(0f)] private float m_ammoTransitionDuration = 0.15f;
        [SerializeField, Range(0.1f, 1f)] private float m_hiddenAmmoScale = 0.75f;
        [SerializeField, Min(0.1f)] private float m_ammoScale = 1f;
        [SerializeField, Min(0f)] private float m_chargeResponse = 12f;
        private float m_ammoVisibility = 1f;
        private bool m_wasCharge;
        private int m_presentedSlot = -1;

        public void PresentWeapon(PlayerWeaponController weapons)
        {
            bool healing = weapons.EquippedHealingItem != null;
            bool equipped = weapons.HasEquippedWeapon;
            bool melee = equipped && weapons.EquippedWeapon == PlayerWeaponType.BaseballBat;
            bool charge = equipped && weapons.EquippedWeapon == PlayerWeaponType.GrappleGun;
            bool reloading = equipped && !charge && weapons.IsReloading;
            bool switched = m_presentedSlot != weapons.SelectedHotbarSlot || charge != m_wasCharge;
            m_presentedSlot = weapons.SelectedHotbarSlot;
            m_wasCharge = charge;
            m_ammoPanel.SetActive(healing || (equipped && !melee));
            m_ammoText.gameObject.SetActive(healing || (equipped && !charge));
            m_ammoText.color = m_weaponColour;
            string ammo = healing ? $"{weapons.EquippedWeaponName}  |  Click to use" : $"{weapons.CurrentMagazine} / {weapons.CurrentReserve}";
            if (!charge && m_ammoText.text != ammo) m_ammoText.text = ammo;
            float target = healing || (equipped && !charge && !reloading) ? 1f : 0f;
            m_ammoVisibility = switched ? target : Mathf.MoveTowards(m_ammoVisibility, target,
                m_ammoTransitionDuration > 0f ? Time.unscaledDeltaTime / m_ammoTransitionDuration : 1f);
            if (m_ammoGroup != null) m_ammoGroup.alpha = m_ammoVisibility;
            m_ammoText.rectTransform.localScale = Vector3.one *
                (m_ammoScale * Mathf.Lerp(m_hiddenAmmoScale, 1f, m_ammoVisibility));
            Style(m_ammoGlow, m_ammoShadow);
            Style(m_chargeGlow, m_chargeShadow);
            if (m_reloadRing != null)
            {
                m_reloadRing.gameObject.SetActive(reloading);
                m_reloadRing.color = m_weaponColour;
                m_reloadRing.Progress = reloading ? weapons.ReloadProgress : 0f;
            }
            if (m_grappleChargeSlider != null)
            {
                m_grappleChargeSlider.gameObject.SetActive(charge);
                float value = weapons.GrappleChargeFraction;
                float displayed = switched || m_chargeResponse <= 0f || value <= m_grappleChargeSlider.value ? value : Mathf.Lerp(
                    m_grappleChargeSlider.value, value, 1f - Mathf.Exp(-m_chargeResponse * Time.unscaledDeltaTime));
                m_grappleChargeSlider.SetValueWithoutNotify(displayed);
                if (m_grappleChargeSlider.fillRect != null)
                    m_grappleChargeSlider.fillRect.GetComponent<Image>().color = m_weaponColour;
            }
        }

        private void Style(Outline glow, Shadow shadow)
        {
            if (glow != null)
            {
                Color colour = m_weaponColour;
                colour.a *= m_glowIntensity;
                glow.effectColor = colour;
                glow.effectDistance = Vector2.one * m_glowSpread;
            }
            if (shadow != null)
            {
                shadow.effectColor = m_shadowColour;
                shadow.effectDistance = m_shadowOffset;
            }
        }
    }
}
