using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    /// <summary>Reusable whole-card delivery/shop view. Its hierarchy is authored in the prefab.</summary>
    public sealed class DispatcherDeliveryButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler
    {
        [SerializeField] private Button m_button;
        [SerializeField] private Image m_artwork;
        [SerializeField, Tooltip("Editable house/item image used when an option has no icon. Override per delivery card.")]
        private Sprite m_placeholder;
        [SerializeField] private Image m_highlight;
        [SerializeField] private TMP_Text m_title, m_description, m_economy, m_reason;
        private bool m_hovered, m_selected;

        public void Bind(DialogueShopOption option, Action<string> notice, Sprite artwork = null)
        {
            m_title.text = option.Title;
            m_description.text = option.Description;
            m_economy.text = string.IsNullOrEmpty(option.Reward) ? option.Price : $"Cost {option.Price}  ·  Payout {option.Reward}";
            m_reason.text = option.Available ? string.Empty : option.UnavailableReason;
            m_artwork.sprite = option.Icon != null ? option.Icon : artwork != null ? artwork : m_placeholder;
            m_artwork.enabled = m_artwork.sprite != null;
            m_title.alpha = option.Available ? 1f : .65f;
            m_button.onClick.RemoveAllListeners();
            // Unavailable cards still explain why. Only the existing authorised callback can purchase.
            m_button.onClick.AddListener(() =>
            {
                if (option.Available) option.Selected?.Invoke();
                else notice?.Invoke(option.UnavailableReason);
            });
            RefreshHighlight();
        }

        public void OnPointerEnter(PointerEventData data) { m_hovered = true; RefreshHighlight(); }
        public void OnPointerExit(PointerEventData data) { m_hovered = false; RefreshHighlight(); }
        public void OnSelect(BaseEventData data) { m_selected = true; RefreshHighlight(); }
        public void OnDeselect(BaseEventData data) { m_selected = false; RefreshHighlight(); }
        private void OnDisable() { m_selected = m_hovered = false; RefreshHighlight(); }
        private void RefreshHighlight() { if (m_highlight != null) m_highlight.enabled = m_hovered || m_selected; }
    }
}
