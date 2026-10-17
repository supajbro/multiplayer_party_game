using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class CouchDispatchOptionCardView : MonoBehaviour
    {
        [Header("Editable Visual References")]
        [SerializeField] private Image m_background;
        [SerializeField] private Image m_artwork;
        [SerializeField] private TMP_Text m_title;
        [SerializeField] private TMP_Text m_description;
        [SerializeField] private TMP_Text m_economy;
        [SerializeField] private TMP_Text m_unavailableReason;
        [SerializeField] private Button m_actionButton;
        [SerializeField] private Image m_actionBackground;
        [SerializeField] private TMP_Text m_actionLabel;

        [SerializeField] private Color m_shopBaseColour = new(0.08f, 0.14f, 0.22f, 0.98f);
        [SerializeField] private Color m_availableColour = new(1f, 0.48f, 0.06f, 1f);
        [SerializeField] private Color m_lockedColour = new(0.28f, 0.3f, 0.36f, 1f);
        [SerializeField] private Color m_unaffordableColour = new(0.32f, 0.35f, 0.42f, 1f);
        [SerializeField] private Color m_ownedColour = new(0.12f, 0.48f, 0.75f, 1f);
        [SerializeField] private Color m_equippedColour = new(0.15f, 0.62f, 0.42f, 1f);

        public Button ActionButton => m_actionButton;

        public void Bind(DialogueShopOption option, bool delivery, Sprite fallbackArtwork)
        {
            gameObject.SetActive(true);
            m_background.color = delivery
                ? Color.Lerp(m_deliveryBaseColour, option.Colour, 0.18f)
                : m_shopBaseColour;
            m_artwork.sprite = option.Icon != null ? option.Icon : fallbackArtwork;
            m_artwork.enabled = m_artwork.sprite != null;
            m_title.text = option.Title;
            m_description.text = option.Description;
            m_economy.text = string.IsNullOrWhiteSpace(option.Reward)
                ? option.Price
                : $"{option.Reward} PAYOUT\n<size=70%>Cost: {option.Price}</size>";

            string label = delivery ? "SELECT" : "BUY";
            Color actionColour = m_availableColour;
            switch (option.VisualState)
            {
                case DialogueShopVisualState.Unaffordable:
                    label = "NEED CASH";
                    actionColour = m_unaffordableColour;
                    break;
                case DialogueShopVisualState.Owned:
                    label = "OWNED";
                    actionColour = m_ownedColour;
                    break;
                case DialogueShopVisualState.Equipped:
                    label = "EQUIPPED";
                    actionColour = m_equippedColour;
                    break;
                case DialogueShopVisualState.Locked:
                    label = "LOCKED";
                    actionColour = m_lockedColour;
                    break;
            }

            m_actionLabel.text = label;
            ApplyButtonColours(actionColour);
            m_actionButton.onClick.RemoveAllListeners();
            m_actionButton.interactable = option.Available;
            if (option.Available) m_actionButton.onClick.AddListener(() => option.Selected?.Invoke());

            bool unavailable = !option.Available && !string.IsNullOrWhiteSpace(option.UnavailableReason);
            m_unavailableReason.gameObject.SetActive(unavailable);
            m_unavailableReason.text = unavailable ? option.UnavailableReason : string.Empty;
        }

        private void ApplyButtonColours(Color colour)
        {
            m_actionBackground.color = colour;
            ColorBlock colours = m_actionButton.colors;
            colours.normalColor = colour;
            colours.highlightedColor = Color.Lerp(colour, Color.white, 0.18f);
            colours.pressedColor = Color.Lerp(colour, Color.black, 0.18f);
            colours.selectedColor = colours.highlightedColor;
            colours.disabledColor = new Color(colour.r * 0.6f, colour.g * 0.6f, colour.b * 0.6f, 0.85f);
            m_actionButton.colors = colours;
        }

#if UNITY_EDITOR
        public void EditorConfigure(Image background, Image artwork, TMP_Text title, TMP_Text description,
            TMP_Text economy, TMP_Text unavailableReason, Button actionButton, Image actionBackground,
            TMP_Text actionLabel)
        {
            m_background = background;
            m_artwork = artwork;
            m_title = title;
            m_description = description;
            m_economy = economy;
            m_unavailableReason = unavailableReason;
            m_actionButton = actionButton;
            m_actionBackground = actionBackground;
            m_actionLabel = actionLabel;
        }
#endif
    }
}
