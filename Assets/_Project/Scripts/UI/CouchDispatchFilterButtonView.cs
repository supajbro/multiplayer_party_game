using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class CouchDispatchFilterButtonView : MonoBehaviour
    {
        [SerializeField] private string m_group = "All";
        [SerializeField] private Button m_button;
        [SerializeField] private Image m_background;
        [SerializeField] private TMP_Text m_label;
        [SerializeField] private Color m_selectedColour = new(1f, 0.5f, 0.08f, 0.95f);
        [SerializeField] private Color m_normalColour = new(0.08f, 0.3f, 0.55f, 0.95f);

        public string Group => m_group;

        public void Bind(bool visible, bool selected, Action selectedAction)
        {
            gameObject.SetActive(visible);
            if (!visible) return;
            m_label.text = m_group.ToUpperInvariant();
            Color colour = selected ? m_selectedColour : m_normalColour;
            m_background.color = colour;
            ColorBlock colours = m_button.colors;
            colours.normalColor = colour;
            colours.highlightedColor = Color.Lerp(colour, Color.white, 0.18f);
            colours.pressedColor = Color.Lerp(colour, Color.black, 0.18f);
            colours.selectedColor = colours.highlightedColor;
            m_button.colors = colours;
            m_button.onClick.RemoveAllListeners();
            m_button.onClick.AddListener(() => selectedAction?.Invoke());
        }

#if UNITY_EDITOR
        public void EditorConfigure(string group, Button button, Image background, TMP_Text label)
        {
            m_group = group;
            m_button = button;
            m_background = background;
            m_label = label;
        }
#endif
    }
}
