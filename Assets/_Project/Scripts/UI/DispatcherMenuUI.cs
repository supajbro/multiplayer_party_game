using System.Collections;
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
        [Header("Purchase Confirmation")]
        [SerializeField] private RectTransform m_menuContent;
        [SerializeField] private CanvasGroup m_contentGroup;
        [SerializeField] private TMP_Text m_confirmationText;
        [SerializeField, Min(0f)] private float m_confirmationSeconds = 2f;
        [SerializeField, Min(.01f)] private float m_confirmationTransitionSeconds = .18f;
        [SerializeField, Range(.1f, 1f)] private float m_confirmationScale = .75f;
        private readonly List<DispatcherDeliveryButton> m_shopCards = new();
        private Coroutine m_confirmation;
        private bool m_interactiveRequested = true;
        public bool IsConfirmingPurchase { get; private set; }
        public int SelectedCategory { get; private set; }

        public void ConfigureCamera(Camera camera) { m_canvas.worldCamera = camera; }
        public void SetInteractive(bool interactive)
        {
            m_interactiveRequested = interactive;
            if (m_group != null) m_group.interactable = m_group.blocksRaycasts = interactive && !IsConfirmingPurchase;
        }

        public void ShowPurchaseConfirmation(string message)
        {
            if (m_confirmation != null) StopCoroutine(m_confirmation);
            IsConfirmingPurchase = true;
            SetInteractive(m_interactiveRequested);
            m_confirmationText.text = message;
            m_confirmationText.gameObject.SetActive(true);
            m_confirmation = StartCoroutine(PurchaseConfirmation());
        }

        private IEnumerator PurchaseConfirmation()
        {
            yield return AnimateConfirmation(true);
            yield return new WaitForSecondsRealtime(m_confirmationSeconds);
            yield return AnimateConfirmation(false);
            m_confirmationText.gameObject.SetActive(false);
            IsConfirmingPurchase = false;
            m_confirmation = null;
            SetInteractive(m_interactiveRequested);
        }

        private IEnumerator AnimateConfirmation(bool showing)
        {
            Vector3 startScale = m_menuContent.localScale;
            Vector3 targetScale = Vector3.one * (showing ? m_confirmationScale : 1f);
            float startAlpha = m_contentGroup.alpha, startTextAlpha = m_confirmationText.alpha;
            float elapsed = 0f;
            while (elapsed < m_confirmationTransitionSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / m_confirmationTransitionSeconds));
                m_menuContent.localScale = Vector3.Lerp(startScale, targetScale, t);
                m_contentGroup.alpha = Mathf.Lerp(startAlpha, showing ? 0f : 1f, t);
                m_confirmationText.alpha = Mathf.Lerp(startTextAlpha, showing ? 1f : 0f, t);
                yield return null;
            }
            m_menuContent.localScale = targetScale;
            m_contentGroup.alpha = showing ? 0f : 1f;
            m_confirmationText.alpha = showing ? 1f : 0f;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            m_confirmation = null;
            IsConfirmingPurchase = false;
            if (m_menuContent != null) m_menuContent.localScale = Vector3.one;
            if (m_contentGroup != null) m_contentGroup.alpha = 1f;
            if (m_confirmationText != null) { m_confirmationText.alpha = 0f; m_confirmationText.gameObject.SetActive(false); }
            SetInteractive(m_interactiveRequested);
        }

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
