using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    /// <summary>Client-local data binding for the saved world-space menu prefab.</summary>
    public sealed class DispatcherMenuUI : MonoBehaviour
    {
        [SerializeField] private Canvas m_canvas;
        [SerializeField] private CanvasGroup m_group;
        [SerializeField] private GameObject m_deliveriesPanel, m_shopPanel;
        [SerializeField] private DispatcherDeliveryButton[] m_deliveries;
        [SerializeField] private DispatcherDeliveryButton m_shopCardPrefab;
        [SerializeField] private Transform m_shopContent;
        [SerializeField] private ScrollRect m_shopScroll;
        [SerializeField] private TMP_Text m_funds, m_feedback;
        [SerializeField] private CouchDispatchUIStyle m_artwork;
        private readonly List<DispatcherDeliveryButton> m_shopCards = new();
        public int SelectedCategory { get; private set; }

        public void ConfigureCamera(Camera camera) { m_canvas.worldCamera = camera; }
        public void SetInteractive(bool interactive) { m_group.interactable = m_group.blocksRaycasts = interactive; }

        public void Bind(IReadOnlyList<DialogueShopCategory> categories, int funds, string feedback)
        {
            m_funds.text = $"TEAM ${funds:N0}";
            if (!string.IsNullOrWhiteSpace(feedback)) m_feedback.text = feedback;
            var options = categories.Count > 0 ? categories[0].Options : null;
            for (int i = 0; i < m_deliveries.Length; i++)
            {
                bool visible = options != null && i < options.Count;
                m_deliveries[i].gameObject.SetActive(visible);
                if (visible) m_deliveries[i].Bind(options[i], ShowNotice);
            }
            options = categories.Count > 1 ? categories[1].Options : null;
            int count = options != null ? options.Count : 0;
            // Instantiate the saved reusable card; never construct a UI hierarchy at runtime.
            while (m_shopCards.Count < count) m_shopCards.Add(Instantiate(m_shopCardPrefab, m_shopContent));
            for (int i = 0; i < m_shopCards.Count; i++)
            {
                m_shopCards[i].gameObject.SetActive(i < count);
                if (i >= count) continue;
                var option = options[i];
                string icon = option.Group == "Weapons" ? "WeaponsIcon" : option.Group == "Upgrades" ? "UpgradeCrate" : "TruckIcon";
                m_shopCards[i].Bind(option, ShowNotice, m_artwork != null ? m_artwork.Get(icon) : null);
            }
        }

        public void ShowNotice(string text) { m_feedback.text = text; }
        public void ShowCategory(int index)
        {
            SelectedCategory = index == 1 ? 1 : 0;
            m_deliveriesPanel.SetActive(SelectedCategory == 0);
            m_shopPanel.SetActive(SelectedCategory == 1);
            if (SelectedCategory == 1) m_shopScroll.verticalNormalizedPosition = 1f;
        }
    }
}
