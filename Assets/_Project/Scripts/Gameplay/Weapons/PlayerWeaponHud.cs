using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Owner-only HUD behaviour backed by an editable canvas prefab.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(PlayerWeaponController), typeof(PlayerHealth))]
    [RequireComponent(typeof(PlayerStamina))]
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponHud : MonoBehaviour
    {
        private Text[] m_slotIcons;
        private static readonly Color CharcoalSoft = new(0.08f, 0.1f, 0.13f, 0.88f);
        private static readonly Color Gold = new(1f, 0.66f, 0.08f, 1f);
        private static readonly Color Inactive = new(0.36f, 0.4f, 0.45f, 1f);
        private static readonly Color Yellow = new(1f, 0.7f, 0.08f, 1f);
        private static readonly Color Red = new(1f, 0.2f, 0.15f, 1f);

        private NetworkObject m_networkObject;
        private VehicleHud m_vehicleHud;
        private PlayerWeaponController m_weapons;
        private PlayerHealth m_health;
        private PlayerStamina m_stamina;
        private DeliveryManager m_delivery;
        private SuburbsChapterManager m_chapter;
        private GameObject m_canvasRoot;
        private bool m_hudBuilt;
        [SerializeField] private PlayerWeaponHudView m_hudPrefab;
        public PlayerWeaponHudView View => m_view;
        private PlayerWeaponHudView m_view;
        private float m_healthTarget = 1f;
        private float m_healthDisplay = 1f;
        private float m_staminaTarget = 1f;
        private float m_staminaDisplay = 1f;
        private float m_nextSlowRefresh;
        private int m_lastSelectedSlot = -1;
        private string m_lastWeaponName;
        private int m_lastMagazine = int.MinValue;
        private int m_lastReserve = int.MinValue;
        private bool m_lastReloading;
        private int m_lastTimerSecond = -1;
        private int m_seenFailureSequence;
        private float m_failureVisibleUntil;

        private void Awake()
        {
            m_networkObject = GetComponent<NetworkObject>();
            m_vehicleHud = GetComponent<VehicleHud>();
            m_weapons = GetComponent<PlayerWeaponController>();
            m_health = GetComponent<PlayerHealth>();
            m_stamina = GetComponent<PlayerStamina>();
        }

        private void Update()
        {
            bool owner = m_networkObject != null && m_networkObject.IsOwner && !m_health.IsAiTeammate;
            if (!owner)
            {
                if (m_view != null) ReleaseHud();
                return;
            }
            if (!m_hudBuilt)
            {
                if (m_hudPrefab == null)
                {
                    Debug.LogError("PlayerWeaponHud prefab is not assigned.", this);
                    enabled = false;
                    return;
                }
                m_view = Instantiate(m_hudPrefab);
                m_slotIcons = new Text[m_view.m_slotBackgrounds.Length];
                for (int slot = 0; slot < m_slotIcons.Length; slot++)
                    m_slotIcons[slot] = m_view.m_slotBackgrounds[slot].transform.Find("Icon")?.GetComponent<Text>();
                m_canvasRoot = m_view.gameObject;
                BindEvents();
                m_hudBuilt = true;
            }

            m_healthDisplay = Mathf.MoveTowards(m_healthDisplay, m_healthTarget, Time.unscaledDeltaTime * 2.8f);
            m_staminaDisplay = Mathf.MoveTowards(m_staminaDisplay, m_staminaTarget, Time.unscaledDeltaTime * 3.5f);
            m_view.m_healthBar.SetValue(m_healthDisplay);
            m_view.m_staminaBar.SetValue(m_staminaDisplay);

            RefreshWeapons();
            if (m_view.m_downedText != null)
            {
                m_view.m_downedText.gameObject.SetActive(m_health.IsDowned);
                if (m_health.IsDowned)
                    m_view.m_downedText.text = $"DOWNED — Respawning in {Mathf.CeilToInt(m_health.DownedRemaining)}s";
            }
            RefreshDeliveryTimer();
            if (Time.unscaledTime >= m_nextSlowRefresh)
            {
                ResolveWorldReferences();
                RefreshDeliveryAndCurrency();
                m_nextSlowRefresh = Time.unscaledTime + 0.25f;
            }
        }

        private void BindEvents()
        {
            m_health.HealthChanged += OnHealthChanged;
            m_stamina.StaminaChanged += OnStaminaChanged;
            OnHealthChanged(m_health.CurrentHealth, m_health.MaximumHealth);
            OnStaminaChanged(m_stamina.CurrentStamina, m_stamina.MaximumStamina);
        }

        private void ResolveWorldReferences()
        {
            if (m_delivery == null)
            {
                m_delivery = FindFirstObjectByType<DeliveryManager>();
                if (m_delivery != null) m_seenFailureSequence = m_delivery.DeliveryFailureSequence;
            }
            if (m_chapter == null) m_chapter = FindFirstObjectByType<SuburbsChapterManager>();
        }

        private void OnHealthChanged(float current, float maximum)
        {
            m_healthTarget = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            m_view.m_healthBar.SetHealthExpression(m_healthTarget);
            if (m_view.m_healthText != null) m_view.m_healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
        }

        private void OnStaminaChanged(float current, float maximum) =>
            m_staminaTarget = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;

        private void RefreshWeapons()
        {
            if (m_slotIcons != null)
            {
                for (int slot = 0; slot < m_slotIcons.Length; slot++)
                {
                    Text icon = m_slotIcons[slot];
                    if (icon == null) continue;
                    int index = m_weapons.GetInventoryWeaponIndex(slot);
                    icon.text = index switch { 0 => "P", 1 => "AR", 2 => "SG", 3 => "G", _ => "—" };
                    icon.fontSize = index == 1 ? 25 : 34;
                    icon.color = index >= 0 ? new Color(1f, 0.55f, 0.12f) : Inactive;
                }
            }
            int selected = m_weapons.SelectedHotbarSlot;
            if (selected != m_lastSelectedSlot)
            {
                for (int i = 0; i < m_view.m_slotBackgrounds.Length; i++)
                {
                    bool active = i == selected;
                    m_view.m_slotBackgrounds[i].color = active ? new Color(0.13f, 0.14f, 0.15f, 0.98f) : CharcoalSoft;
                    m_view.m_slotOutlines[i].effectColor = active ? Gold : Inactive;
                    m_view.m_slotOutlines[i].effectDistance = active ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
                }
                m_lastSelectedSlot = selected;
            }

            bool showAmmo = m_weapons.HasEquippedWeapon;
            if (m_view.m_ammoPanel.activeSelf != showAmmo) m_view.m_ammoPanel.SetActive(showAmmo);
            if (showAmmo)
            {
                string weaponName = m_weapons.EquippedWeaponName;
                int magazine = m_weapons.CurrentMagazine;
                int reserve = m_weapons.CurrentReserve;
                bool reloading = m_weapons.IsReloading;
                if (weaponName != m_lastWeaponName || magazine != m_lastMagazine ||
                    reserve != m_lastReserve || reloading != m_lastReloading)
                {
                    m_view.m_ammoText.text = m_weapons.HasEquippedWeapon && m_weapons.EquippedWeapon == PlayerWeaponType.GrappleGun
                        ? $"{weaponName}\n<size=24>HOLD FIRE TO PULL</size>"
                        : reloading
                        ? $"{weaponName}\n<size=24>RELOADING  {magazine} / {reserve}</size>"
                        : $"{weaponName}\n<size=30>{magazine} / {reserve}</size>";
                    m_lastWeaponName = weaponName;
                    m_lastMagazine = magazine;
                    m_lastReserve = reserve;
                    m_lastReloading = reloading;
                }
            }
        }

        private void RefreshDeliveryAndCurrency()
        {
            m_view.m_currencyText.text = $"$ {(m_delivery != null ? m_delivery.CurrentCurrency : 0):N0}";
            int completed = m_chapter != null ? m_chapter.CompletedDeliveryCount : 0;
            if (m_delivery != null && m_delivery.HasActiveDelivery && m_delivery.ActiveDestination != null)
            {
                DeliveryDestination destination = m_delivery.ActiveDestination;
                int number = m_delivery.ActiveStageIndex >= 0 ? m_delivery.ActiveStageIndex + 1 : completed + 1;
                m_view.m_deliveryTitle.text = $"Delivery {number}";
                m_view.m_deliveryDetail.text = $"Take couch to {destination.DisplayName}";
                Vector3 delta = destination.DropPosition.position - transform.position;
                delta.y = 0f;
                m_view.m_deliveryDistance.text = $"●  {Mathf.RoundToInt(delta.magnitude)} m";
            }
            else
            {
                m_view.m_deliveryTitle.text = completed > 0 ? $"Delivery {completed}" : "Deliveries";
                m_view.m_deliveryDetail.text = m_chapter != null && m_chapter.ChapterCompleted
                    ? "Region complete!"
                    : "Visit the dispatcher for a couch";
                m_view.m_deliveryDistance.text = string.Empty;
            }
        }

        private void RefreshDeliveryTimer()
        {
            if (m_view.m_timerPanel == null) return;
            bool active = m_delivery != null && m_delivery.IsDeliveryTimerActive;
            if (m_view.m_timerPanel.activeSelf != active) m_view.m_timerPanel.SetActive(active);
            if (active)
            {
                float remaining = m_delivery.RemainingDeliveryTime;
                int second = Mathf.Max(0, Mathf.CeilToInt(remaining));
                if (second != m_lastTimerSecond)
                {
                    m_view.m_timerText.text = $"{second / 60:00}:{second % 60:00}";
                    m_lastTimerSecond = second;
                }
                float ratio = m_delivery.DeliveryDuration > 0f
                    ? remaining / m_delivery.DeliveryDuration : 0f;
                SetBar(m_view.m_timerFill, ratio);
                Color urgency = remaining < 10f ? Red : remaining < 30f ? new Color(1f, 0.42f, 0.06f) : Gold;
                m_view.m_timerFill.color = urgency;
                m_view.m_timerText.color = urgency;
                float pulse = remaining < 10f ? 1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.035f : 1f;
                m_view.m_timerText.rectTransform.localScale = Vector3.one * pulse;
            }
            else m_lastTimerSecond = -1;

            if (m_delivery != null && m_seenFailureSequence != m_delivery.DeliveryFailureSequence)
            {
                m_seenFailureSequence = m_delivery.DeliveryFailureSequence;
                m_failureVisibleUntil = Time.unscaledTime + 2.5f;
            }
            m_view.m_failureText.gameObject.SetActive(Time.unscaledTime < m_failureVisibleUntil);
        }


        private void LateUpdate()
        {
            if (!m_hudBuilt) return;
            if (m_view.m_interactionPrompt != null)
            {
                float progress = 0f, duration = 0f;
                string prompt = m_health.GetRevivePrompt(out progress, out duration);
                if (prompt == null && !m_health.IsKnockedDown && m_vehicleHud != null)
                    prompt = m_vehicleHud.GetInteractionPrompt(out progress, out duration);
                m_view.m_interactionPrompt.Present(prompt, progress, duration);
            }
        }

        private static void SetBar(Image fill, float value)
        {
            if (fill != null) fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        private void OnDisable() => ReleaseHud();

        private void ReleaseHud()
        {
            if (m_health != null) m_health.HealthChanged -= OnHealthChanged;
            if (m_stamina != null) m_stamina.StaminaChanged -= OnStaminaChanged;
            if (m_canvasRoot != null) Destroy(m_canvasRoot);
            m_canvasRoot = null;
            m_view = null;
            m_hudBuilt = false;
            m_lastSelectedSlot = -1;
            m_lastWeaponName = null;
            m_lastMagazine = int.MinValue;
            m_lastTimerSecond = -1;
            m_nextSlowRefresh = 0f;
        }

        private void OnDestroy() => ReleaseHud();
    }
}
