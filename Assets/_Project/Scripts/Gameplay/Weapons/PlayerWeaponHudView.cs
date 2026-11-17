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
        [SerializeField] public CouchGuys.Gameplay.Shop.ShopCatalogue m_itemCatalogue;
        [SerializeField] public Image[] m_slotIcons;
        [SerializeField] public Image[] m_slotBackgrounds;
        [SerializeField] public Outline[] m_slotOutlines;
        [SerializeField] public GameObject m_ammoPanel;

        [SerializeField] public Sprite m_deliveryCouchSprite;
        [SerializeField] public DeliveryTimerHudView m_deliveryTimerTemplate;
        [SerializeField, Min(0f)] public float m_deliveryTimerSpacing = 4f;
        [SerializeField, Min(0f)] public float m_deliveryTimerBottomMargin = 204f;

        [Header("Currency feedback")]
        [Tooltip("Seconds to count to a new balance. Zero applies it immediately.")]
        [SerializeField, Min(0f)] private float m_currencyAnimationDuration = 0.5f;
        [Tooltip("Peak text scale relative to its authored scale.")]
        [SerializeField, Range(1f, 1.3f)] private float m_currencyPulseScale = 1.1f;
        [SerializeField] private Color m_currencyIncreaseColour = new(0.2f, 1f, 0.3f, 1f);
        [SerializeField] private Color m_currencyDecreaseColour = new(1f, 0.25f, 0.2f, 1f);
        private bool m_currencyInitialized;
        private int m_currencyTarget;
        private double m_currencyFrom, m_currencyDisplay;
        private float m_currencyElapsed;
        private Color m_currencyBaseColour;
        private Vector3 m_currencyBaseScale;

        public void PresentCurrency(int amount, float deltaTime)
        {
            if (m_currencyText == null) return;
            if (!m_currencyInitialized)
            {
                // Show the initial balance without treating HUD creation as a gain.
                m_currencyInitialized = true;
                m_currencyBaseColour = m_currencyText.color;
                m_currencyBaseScale = m_currencyText.rectTransform.localScale;
                m_currencyFrom = m_currencyDisplay = m_currencyTarget = amount;
                m_currencyElapsed = m_currencyAnimationDuration;
                m_currencyText.text = $"$ {amount:N0}";
                return;
            }

            if (amount != m_currencyTarget)
            {
                m_currencyFrom = m_currencyDisplay;
                m_currencyTarget = amount;
                m_currencyElapsed = 0f;
            }
            else if (m_currencyElapsed >= m_currencyAnimationDuration) return;

            m_currencyElapsed += Mathf.Max(0f, deltaTime);
            float t = m_currencyAnimationDuration > 0f
                ? Mathf.Clamp01(m_currencyElapsed / m_currencyAnimationDuration) : 1f;
            float eased = Mathf.SmoothStep(0f, 1f, t);
            m_currencyDisplay = m_currencyFrom + (m_currencyTarget - m_currencyFrom) * eased;
            int displayed = (int)System.Math.Round(m_currencyDisplay, System.MidpointRounding.AwayFromZero);
            string label = $"$ {displayed:N0}";
            if (m_currencyText.text != label) m_currencyText.text = label;

            bool changing = t < 1f && m_currencyFrom != m_currencyTarget;
            Color colour = m_currencyTarget > m_currencyFrom ? m_currencyIncreaseColour : m_currencyDecreaseColour;
            colour.a = m_currencyBaseColour.a;
            m_currencyText.color = changing ? colour : m_currencyBaseColour;
            float pulse = changing ? Mathf.Sin(t * Mathf.PI) : 0f;
            m_currencyText.rectTransform.localScale = m_currencyBaseScale *
                Mathf.Lerp(1f, m_currencyPulseScale, pulse);
        }

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
        [SerializeField] private Text[] m_healingQuantities;
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
            if (m_healingQuantities != null)
                for (int slot = 0; slot < m_healingQuantities.Length; slot++)
                {
                    Text label = m_healingQuantities[slot];
                    if (label == null) continue;
                    int quantity = weapons.GetInventoryHealingQuantity(slot);
                    label.gameObject.SetActive(quantity > 1);
                    label.text = quantity > 1 ? quantity.ToString() : string.Empty;
                }
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
