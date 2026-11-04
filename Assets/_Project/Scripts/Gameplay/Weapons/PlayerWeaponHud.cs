using System.Collections.Generic;
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
        private float m_nextSlowRefresh;
        private readonly Dictionary<int, DeliveryTimerHudView> m_deliveryTimers = new();
        private readonly List<int> m_removedTimers = new();
        private readonly List<DeliveryInstance> m_sortedDeliveries = new();
        private PlayerCouchCarrier m_carrier;
        private bool m_timerLayoutDirty;
        private Vector2 m_timerCanvasSize;
        private int m_seenFailureSequence;
        private float m_failureVisibleUntil;

        private void Awake()
        {
            m_networkObject = GetComponent<NetworkObject>();
            m_vehicleHud = GetComponent<VehicleHud>();
            m_weapons = GetComponent<PlayerWeaponController>();
            m_health = GetComponent<PlayerHealth>();
            m_stamina = GetComponent<PlayerStamina>();
            m_carrier = GetComponent<PlayerCouchCarrier>();
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
            m_view.m_healthBar.SetValue(m_healthDisplay);
            m_view.m_staminaBar.SetValue(m_staminaTarget);

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
                if (m_delivery != null)
                {
                    m_seenFailureSequence = m_delivery.DeliveryFailureSequence;
                    m_delivery.DeliveriesChanged += SyncDeliveryTimers;
                    SyncDeliveryTimers();
                }
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
                    icon.text = index switch { 0 => "P", 1 => "AR", 2 => "SG", 3 => "G", 4 => "RL", 5 => "BAT", _ => "—" };
                    icon.fontSize = index == 5 ? 20 : index == 1 || index == 4 ? 25 : 34;
                    icon.color = index >= 0 ? new Color(1f, 0.55f, 0.12f) : Inactive;
                }
            }
            m_view.PresentHotbar(m_weapons.SelectedHotbarSlot);

            m_view.PresentWeapon(m_weapons);
        }

        private void RefreshDeliveryAndCurrency()
        {
            m_view.m_currencyText.text = $"$ {(m_delivery != null ? m_delivery.CurrentCurrency : 0):N0}";
            int completed = m_chapter != null ? m_chapter.CompletedDeliveryCount : 0;
            DeliveryDestination destination = m_delivery != null ? m_delivery.GetDestinationForCouch(m_carrier != null ? m_carrier.CarriedCouch : null) : null;
            if (destination != null)
            {
                int number = m_carrier.CarriedCouch.DeliveryId;
                m_view.m_deliveryTitle.text = $"Delivery {number}";
                m_view.m_deliveryDetail.text = $"Take couch to {destination.DisplayName}";
                Vector3 delta = destination.DropPosition.position - transform.position;
                delta.y = 0f;
                m_view.m_deliveryDistance.text = $"●  {Mathf.RoundToInt(delta.magnitude)} m";
            }
            else
            {
                bool active = m_delivery != null && m_delivery.HasActiveDelivery;
                m_view.m_deliveryTitle.text = active ? $"{m_delivery.Deliveries.Count} Active Deliveries" : "Deliveries";
                m_view.m_deliveryDetail.text = active ? "Collect a Couch" : "Visit the dispatcher for a couch";
                m_view.m_deliveryDistance.text = string.Empty;
            }
        }

        private void RefreshDeliveryTimer()
        {
            if (m_view.m_deliveryTimerTemplate == null) return;
            m_view.m_deliveryTimerTemplate.gameObject.SetActive(false);
            if (m_timerLayoutDirty || m_timerCanvasSize != ((RectTransform)m_view.transform).rect.size) LayoutDeliveryTimers();
            int carriedId = m_carrier != null && m_carrier.CarriedCouch != null ? m_carrier.CarriedCouch.DeliveryId : 0;
            if (m_delivery != null)
                foreach (var job in m_delivery.Deliveries)
                    if (m_deliveryTimers.TryGetValue(job.Id, out var timer))
                        timer.Present(m_view, m_delivery.GetRemainingTime(job), job.Duration, job.Id == carriedId);

            if (m_delivery != null && m_seenFailureSequence != m_delivery.DeliveryFailureSequence)
            {
                m_seenFailureSequence = m_delivery.DeliveryFailureSequence;
                m_failureVisibleUntil = Time.unscaledTime + 2.5f;
            }
            m_view.m_failureText.gameObject.SetActive(Time.unscaledTime < m_failureVisibleUntil);
        }


        private void SyncDeliveryTimers()
        {
            if (m_view == null || m_delivery == null || m_view.m_deliveryTimerTemplate == null) return;
            var template = m_view.m_deliveryTimerTemplate;
            template.gameObject.SetActive(false);
            m_sortedDeliveries.Clear();
            foreach (var job in m_delivery.Deliveries)
            {
                m_sortedDeliveries.Add(job);
                if (m_deliveryTimers.ContainsKey(job.Id)) continue;
                var timer = Instantiate(template, template.transform.parent, false);
                timer.Bind(job.Id, $"{(DeliveryTier)job.TierIndex} {job.Id:00}".ToUpperInvariant());
                timer.gameObject.SetActive(true);
                m_deliveryTimers.Add(job.Id, timer);
            }
            m_removedTimers.Clear();
            foreach (var entry in m_deliveryTimers)
                if (!m_delivery.TryGetDelivery(entry.Key, out _))
                {
                    entry.Value.gameObject.SetActive(false);
                    Destroy(entry.Value.gameObject);
                    m_removedTimers.Add(entry.Key);
                }
            foreach (int id in m_removedTimers) m_deliveryTimers.Remove(id);
            m_sortedDeliveries.Sort((a, b) => a.TierIndex != b.TierIndex ? a.TierIndex.CompareTo(b.TierIndex) : a.Id.CompareTo(b.Id));
            m_timerLayoutDirty = true;
        }

        private void LayoutDeliveryTimers()
        {
            m_timerLayoutDirty = false;
            var template = (RectTransform)m_view.m_deliveryTimerTemplate.transform;
            m_timerCanvasSize = ((RectTransform)m_view.transform).rect.size;
            float stride = template.rect.height * Mathf.Abs(template.localScale.y) + m_view.m_deliveryTimerSpacing;
            float available = Mathf.Max(1f, m_timerCanvasSize.y + template.anchoredPosition.y - m_view.m_deliveryTimerBottomMargin);
            float scale = Mathf.Min(1f, available / Mathf.Max(1f, m_sortedDeliveries.Count * stride));
            for (int i = 0; i < m_sortedDeliveries.Count; i++)
            {
                if (!m_deliveryTimers.TryGetValue(m_sortedDeliveries[i].Id, out var timer)) continue;
                var rect = (RectTransform)timer.transform;
                rect.anchoredPosition = template.anchoredPosition + Vector2.down * (i * stride * scale);
                rect.localScale = template.localScale * scale;
            }
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

        private void OnDisable() => ReleaseHud();

        private void ReleaseHud()
        {
            if (m_health != null) m_health.HealthChanged -= OnHealthChanged;
            if (m_stamina != null) m_stamina.StaminaChanged -= OnStaminaChanged;
            if (m_canvasRoot != null) Destroy(m_canvasRoot);
            m_canvasRoot = null;
            m_view = null;
            m_hudBuilt = false;
            if (m_delivery != null) m_delivery.DeliveriesChanged -= SyncDeliveryTimers;
            m_delivery = null;
            m_deliveryTimers.Clear();
            m_nextSlowRefresh = 0f;
        }

        private void OnDestroy() => ReleaseHud();
    }
}
