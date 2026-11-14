using System;
using System.Collections.Generic;
using System.Globalization;
using CouchGuys.Gameplay.Shop;
using TMPro;
using UnityEngine;

namespace CouchGuys.UI
{
    /// <summary>Local, read-only advertising using two saved panels and the shared shop catalogue.</summary>
    public sealed class WeaponAdvertisementDisplay : MonoBehaviour
    {
        [Serializable]
        private sealed class AdvertisementPanel
        {
            [SerializeField] internal RectTransform m_rect;
            [SerializeField] internal TMP_Text m_name;
            [SerializeField] internal TMP_Text m_price;

            internal void Show(ShopItemDefinition item)
            {
                m_name.text = item.DisplayName;
                m_price.text = "$" + item.Price.ToString("N0", CultureInfo.InvariantCulture);
            }
        }

        [SerializeField] private ShopCatalogue m_catalogue;
        [Tooltip("Seconds between the start of each slide, including transition time.")]
        [SerializeField, Min(0.1f)] private float m_scrollInterval = 5f;
        [SerializeField, Min(0f)] private float m_transitionDuration = 0.65f;
        [SerializeField] private RectTransform m_viewport;
        [SerializeField] private AdvertisementPanel m_firstPanel;
        [SerializeField] private AdvertisementPanel m_secondPanel;

        private readonly List<ShopItemDefinition> m_weapons = new();
        private AdvertisementPanel m_current, m_next;
        private int m_index;
        private float m_elapsed;
        private bool m_sliding;

        private void OnEnable()
        {
            if (m_viewport == null || !IsWired(m_firstPanel) || !IsWired(m_secondPanel))
            {
                Debug.LogError("Weapon advertisement display has missing panel references.", this);
                enabled = false;
                return;
            }
            m_current = m_firstPanel;
            m_next = m_secondPanel;
            m_index = 0;
            m_elapsed = 0f;
            m_sliding = false;
            RefreshWeapons();
            m_current.m_rect.gameObject.SetActive(m_weapons.Count > 0);
            m_next.m_rect.gameObject.SetActive(false);
            SetX(m_current, 0f);
            if (m_weapons.Count > 0) m_current.Show(m_weapons[0]);
        }

        private static bool IsWired(AdvertisementPanel panel) => panel != null &&
            panel.m_rect != null && panel.m_name != null && panel.m_price != null;

        private void RefreshWeapons()
        {
            m_weapons.Clear();
            if (m_catalogue == null || m_catalogue.Items == null) return;
            foreach (var item in m_catalogue.Items)
                if (item != null && item.Category == ShopItemCategory.Weapon &&
                    item.PurchaseBehaviour != null && !m_weapons.Contains(item))
                    m_weapons.Add(item);
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        // Kept separate from Update so Editor validation can exercise real timing deterministically.
        private void Advance(float deltaTime)
        {
            float interval = Mathf.Max(0.1f, m_scrollInterval);
            float duration = Mathf.Clamp(m_transitionDuration, 0f, interval);
            m_elapsed += Mathf.Max(0f, deltaTime);
            if (!m_sliding)
            {
                if (m_elapsed < interval) return;
                m_elapsed -= interval;
                var previous = m_weapons.Count > 0 ? m_weapons[m_index] : null;
                RefreshWeapons();
                m_current.m_rect.gameObject.SetActive(m_weapons.Count > 0);
                if (m_weapons.Count == 0) { m_index = 0; return; }
                int previousIndex = m_weapons.IndexOf(previous);
                m_index = previousIndex >= 0 ? previousIndex : 0;
                m_current.Show(m_weapons[m_index]);
                if (m_weapons.Count == 1) { m_index = 0; return; }
                m_index = previousIndex >= 0 ? (m_index + 1) % m_weapons.Count : 0;
                m_next.Show(m_weapons[m_index]);
                m_next.m_rect.gameObject.SetActive(true);
                m_sliding = true;
            }

            float progress = duration > 0f ? Mathf.Clamp01(m_elapsed / duration) : 1f;
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float width = m_viewport.rect.width;
            SetX(m_current, -width * eased);
            SetX(m_next, width * (1f - eased));
            if (progress < 1f) return;
            m_current.m_rect.gameObject.SetActive(false);
            (m_current, m_next) = (m_next, m_current);
            SetX(m_current, 0f);
            m_sliding = false;
            // m_elapsed includes transition time, keeping slide starts exactly interval seconds apart.
        }

        private static void SetX(AdvertisementPanel panel, float x)
        {
            var position = panel.m_rect.anchoredPosition;
            position.x = x;
            panel.m_rect.anchoredPosition = position;
        }
    }
}
