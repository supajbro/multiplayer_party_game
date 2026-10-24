using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    public enum DialogueShopVisualState { Available, Locked, Unaffordable, Owned, Equipped }

    public readonly struct DialogueShopOption
    {
        public readonly string Title;
        public readonly string Description;
        public readonly string Price;
        public readonly string Reward;
        public readonly Color Colour;
        public readonly bool Available;
        public readonly string UnavailableReason;
        public readonly Sprite Icon;
        public readonly Action Selected;
        public readonly string Group;
        public readonly DialogueShopVisualState VisualState;

        public DialogueShopOption(string title, string description, string price, string reward,
            Color colour, bool available, string unavailableReason, Action selected, Sprite icon = null,
            string group = "", DialogueShopVisualState? visualState = null)
        {
            Title = title;
            Description = description;
            Price = price;
            Reward = reward;
            Colour = colour;
            Available = available;
            UnavailableReason = unavailableReason;
            Selected = selected;
            Icon = icon;
            Group = group;
            VisualState = visualState ?? InferState(available, unavailableReason);
        }

        private static DialogueShopVisualState InferState(bool available, string reason)
        {
            if (available) return DialogueShopVisualState.Available;
            string value = reason ?? string.Empty;
            if (value.IndexOf("equipped", StringComparison.OrdinalIgnoreCase) >= 0)
                return DialogueShopVisualState.Equipped;
            if (value.IndexOf("owned", StringComparison.OrdinalIgnoreCase) >= 0)
                return DialogueShopVisualState.Owned;
            return value.StartsWith("Need $", StringComparison.OrdinalIgnoreCase)
                ? DialogueShopVisualState.Unaffordable
                : DialogueShopVisualState.Locked;
        }
    }

    public readonly struct DialogueShopCategory
    {
        public readonly string Title;
        public readonly IReadOnlyList<DialogueShopOption> Options;

        public DialogueShopCategory(string title, IReadOnlyList<DialogueShopOption> options)
        {
            Title = title;
            Options = options;
        }
    }

    /// <summary>
    /// Binds gameplay data and callbacks to an entirely prefab-authored visual hierarchy.
    /// No visual objects or layout components are constructed by this runtime presenter.
    /// </summary>
    public sealed class DialogueShopUI : MonoBehaviour
    {
        private const string StyleResource = "UI/CouchDispatchUIStyle";
        private static DialogueShopUI s_instance;

        [Header("Prefab-authored Root")]
        [SerializeField] private GameObject m_canvasRoot;
        [SerializeField] private RectTransform m_window;
        [SerializeField] private CanvasGroup m_windowGroup;
        [SerializeField] private TMP_Text m_greeting;
        [SerializeField] private Button m_closeButton;

        [Header("Prefab-authored Tabs")]
        [SerializeField] private Button m_deliveriesTab;
        [SerializeField] private Image m_deliveriesTabHighlight;
        [SerializeField] private Button m_shopTab;
        [SerializeField] private Image m_shopTabHighlight;

        [Header("Prefab-authored Content")]
        [SerializeField] private RectTransform m_viewport;
        [SerializeField] private ScrollRect m_scrollRect;
        [SerializeField] private RectTransform m_deliveryCardsRoot;
        [SerializeField] private CouchDispatchOptionCardView[] m_deliveryCards = Array.Empty<CouchDispatchOptionCardView>();
        [SerializeField] private RectTransform m_shopCardsRoot;
        [SerializeField] private CouchDispatchOptionCardView m_shopCardTemplate;
        [SerializeField] private GameObject m_filtersRoot;
        [SerializeField] private CouchDispatchFilterButtonView[] m_filters = Array.Empty<CouchDispatchFilterButtonView>();
        [SerializeField] private CouchDispatchUIStyle m_style;

        [Header("Prefab-authored Input")]
        [SerializeField] private GameObject m_dialogueEventSystemObject;
        [SerializeField] private EventSystem m_dialogueEventSystem;

        private readonly List<CouchDispatchOptionCardView> m_shopCards = new();
        private readonly List<EventSystem> m_suspendedEventSystems = new();
        private IReadOnlyList<DialogueShopCategory> m_categories;
        private Action m_closed;
        private int m_selectedCategory;
        private string m_selectedFilter = "All";
        private Coroutine m_transition;

        public static DialogueShopUI Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                CouchDispatchUIStyle style = Resources.Load<CouchDispatchUIStyle>(StyleResource);
                GameObject prefab = style != null ? style.Prefab : null;
                if (prefab == null)
                {
                    Debug.LogError($"Missing Couch Dispatch prefab reference in Resources/{StyleResource}. Rebuild it from Couch Guys > UI > Rebuild Couch Dispatch UI.");
                    return new GameObject("MissingCouchDispatchUI").AddComponent<DialogueShopUI>();
                }
                return Instantiate(prefab).GetComponent<DialogueShopUI>();
            }
        }

        public bool IsOpen => m_canvasRoot != null && m_canvasRoot.activeSelf;

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;
            DontDestroyOnLoad(gameObject);
            if (!ValidatePrefab()) return;
            WireStaticButtons();
            m_shopCardTemplate.gameObject.SetActive(false);
            m_canvasRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        private bool ValidatePrefab()
        {
            bool valid = m_canvasRoot != null && m_window != null && m_windowGroup != null &&
                         m_greeting != null && m_closeButton != null && m_deliveriesTab != null &&
                         m_shopTab != null && m_scrollRect != null && m_deliveryCardsRoot != null &&
                         m_shopCardsRoot != null && m_shopCardTemplate != null &&
                         m_dialogueEventSystemObject != null && m_dialogueEventSystem != null;
            if (!valid) Debug.LogError("CouchDispatchUI prefab references are incomplete. Rebuild the prefab or assign them in the Inspector.", this);
            return valid;
        }

        private void WireStaticButtons()
        {
            m_closeButton.onClick.RemoveAllListeners();
            m_closeButton.onClick.AddListener(() => Close());
            m_deliveriesTab.onClick.RemoveAllListeners();
            m_deliveriesTab.onClick.AddListener(() => ShowCategory(0, true));
            m_shopTab.onClick.RemoveAllListeners();
            m_shopTab.onClick.AddListener(() => ShowCategory(1, true));
        }

        public void Open(string greeting, IReadOnlyList<DialogueShopOption> options, Action closed) =>
            Open(greeting, new[] { new DialogueShopCategory("Deliveries", options) }, closed);

        public void Open(string greeting, IReadOnlyList<DialogueShopCategory> categories, Action closed,
            int selectedCategory = 0)
        {
            if (!ValidatePrefab()) return;
            bool alreadyOpen = IsOpen;
            if (!alreadyOpen) ActivateDialogueInput();
            CancelInvoke(nameof(HideNotice));
            m_closed = closed;
            m_greeting.text = greeting;
            m_categories = categories;
            m_selectedCategory = categories != null && categories.Count > 0
                ? Mathf.Clamp(selectedCategory, 0, categories.Count - 1)
                : 0;
            m_selectedFilter = "All";
            ShowCategory(m_selectedCategory, false);
            m_canvasRoot.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!alreadyOpen) PlayWindowTransition();
        }

        public void Close(bool notifyProvider = true)
        {
            if (!IsOpen) return;
            if (m_transition != null) StopCoroutine(m_transition);
            m_transition = StartCoroutine(CloseRoutine(notifyProvider));
        }

        public void ShowNotice(string message)
        {
            if (!ValidatePrefab()) return;
            m_greeting.text = message;
            m_canvasRoot.SetActive(true);
            PlayWindowTransition();
            CancelInvoke(nameof(HideNotice));
            Invoke(nameof(HideNotice), 2.5f);
        }

        private void HideNotice()
        {
            if (m_closed == null && m_canvasRoot != null) m_canvasRoot.SetActive(false);
        }

        private void ShowCategory(int categoryIndex, bool animate)
        {
            if (m_categories == null || categoryIndex < 0 || categoryIndex >= m_categories.Count) return;
            m_selectedCategory = categoryIndex;
            m_selectedFilter = "All";
            bool deliveries = string.Equals(m_categories[categoryIndex].Title, "Deliveries", StringComparison.OrdinalIgnoreCase);
            m_deliveryCardsRoot.gameObject.SetActive(deliveries);
            m_shopCardsRoot.gameObject.SetActive(!deliveries);
            m_filtersRoot.SetActive(!deliveries);
            m_viewport.offsetMax = new Vector2(-82f, deliveries ? -244f : -292f);
            m_scrollRect.content = deliveries ? m_deliveryCardsRoot : m_shopCardsRoot;
            RefreshTabSelection();
            RefreshFilters();
            RefreshCards(deliveries);
            m_scrollRect.verticalNormalizedPosition = 1f;
            if (animate) StartCoroutine(PulseContent(deliveries ? m_deliveryCardsRoot : m_shopCardsRoot));
        }

        private void RefreshTabSelection()
        {
            bool deliveries = m_selectedCategory == 0;
            m_deliveriesTabHighlight.color = deliveries
                ? new Color(1f, 0.72f, 0.2f, 0.2f)
                : new Color(0.05f, 0.15f, 0.3f, 0.08f);
            m_shopTabHighlight.color = !deliveries
                ? new Color(1f, 0.72f, 0.2f, 0.2f)
                : new Color(0.05f, 0.15f, 0.3f, 0.08f);
        }

        private void RefreshFilters()
        {
            if (m_selectedCategory == 0 || m_categories == null) return;
            HashSet<string> groups = new(StringComparer.OrdinalIgnoreCase) { "All" };
            IReadOnlyList<DialogueShopOption> options = m_categories[m_selectedCategory].Options;
            if (options != null)
                foreach (DialogueShopOption option in options)
                    if (!string.IsNullOrWhiteSpace(option.Group)) groups.Add(option.Group);

            foreach (CouchDispatchFilterButtonView filter in m_filters)
            {
                if (filter == null) continue;
                string captured = filter.Group;
                filter.Bind(groups.Contains(captured), string.Equals(captured, m_selectedFilter, StringComparison.OrdinalIgnoreCase), () =>
                {
                    m_selectedFilter = captured;
                    RefreshFilters();
                    RefreshCards(false);
                    StartCoroutine(PulseContent(m_shopCardsRoot));
                });
            }
        }

        private void RefreshCards(bool deliveries)
        {
            IReadOnlyList<DialogueShopOption> options = m_categories[m_selectedCategory].Options;
            if (deliveries)
            {
                for (int index = 0; index < m_deliveryCards.Length; index++)
                {
                    bool visible = options != null && index < options.Count;
                    m_deliveryCards[index].gameObject.SetActive(visible);
                    if (!visible) continue;
                    Sprite artwork = m_style != null
                        ? m_style.Get(index switch { 0 => "DeliveryEasy", 1 => "DeliveryMedium", _ => "DeliveryHard" })
                        : null;
                    m_deliveryCards[index].Bind(options[index], true, artwork);
                }
                return;
            }

            foreach (CouchDispatchOptionCardView card in m_shopCards)
                if (card != null) Destroy(card.gameObject);
            m_shopCards.Clear();
            if (options == null) return;
            foreach (DialogueShopOption option in options)
            {
                if (m_selectedFilter != "All" && !string.Equals(option.Group, m_selectedFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                CouchDispatchOptionCardView card = Instantiate(m_shopCardTemplate, m_shopCardsRoot);
                card.name = $"ShopCard_{option.Title}";
                Sprite artwork = ResolveShopArtwork(option.Group);
                card.Bind(option, false, artwork);
                m_shopCards.Add(card);
            }
        }

        private Sprite ResolveShopArtwork(string group)
        {
            if (m_style == null) return null;
            if (string.Equals(group, "Weapons", StringComparison.OrdinalIgnoreCase)) return m_style.Get("WeaponsIcon");
            if (string.Equals(group, "Upgrades", StringComparison.OrdinalIgnoreCase)) return m_style.Get("UpgradeCrate");
            return m_style.Get("TruckIcon");
        }

        private void ActivateDialogueInput()
        {
            m_suspendedEventSystems.Clear();
            EventSystem[] systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (EventSystem eventSystem in systems)
            {
                if (eventSystem == null || eventSystem == m_dialogueEventSystem || !eventSystem.enabled) continue;
                m_suspendedEventSystems.Add(eventSystem);
                eventSystem.enabled = false;
            }
            m_dialogueEventSystemObject.SetActive(true);
            m_dialogueEventSystem.enabled = true;
        }

        private void DeactivateDialogueInput()
        {
            m_dialogueEventSystemObject.SetActive(false);
            foreach (EventSystem eventSystem in m_suspendedEventSystems)
                if (eventSystem != null) eventSystem.enabled = true;
            m_suspendedEventSystems.Clear();
        }

        private void PlayWindowTransition()
        {
            if (m_transition != null) StopCoroutine(m_transition);
            m_transition = StartCoroutine(OpenRoutine());
        }

        private IEnumerator OpenRoutine()
        {
            m_window.localScale = Vector3.one * 0.82f;
            m_windowGroup.alpha = 0f;
            float elapsed = 0f;
            const float duration = 0.28f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                float bounce = 1f + Mathf.Sin(t * Mathf.PI) * 0.055f * (1f - t);
                m_window.localScale = Vector3.one * Mathf.Lerp(0.82f, bounce, eased);
                m_windowGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.5f));
                yield return null;
            }
            m_window.localScale = Vector3.one;
            m_windowGroup.alpha = 1f;
            m_transition = null;
        }

        private IEnumerator CloseRoutine(bool notifyProvider)
        {
            float elapsed = 0f;
            Vector3 start = m_window.localScale;
            while (elapsed < 0.14f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.14f);
                m_window.localScale = Vector3.LerpUnclamped(start, Vector3.one * 0.88f, t * t);
                m_windowGroup.alpha = 1f - t;
                yield return null;
            }
            m_canvasRoot.SetActive(false);
            m_window.localScale = Vector3.one;
            m_windowGroup.alpha = 1f;
            DeactivateDialogueInput();
            Action closed = m_closed;
            m_closed = null;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            m_transition = null;
            if (notifyProvider) closed?.Invoke();
        }

        private IEnumerator PulseContent(RectTransform content)
        {
            if (content == null) yield break;
            CanvasGroup group = content.GetComponent<CanvasGroup>();
            content.localScale = new Vector3(0.985f, 0.985f, 1f);
            group.alpha = 0.25f;
            float elapsed = 0f;
            while (elapsed < 0.16f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.16f);
                content.localScale = Vector3.one * Mathf.Lerp(0.985f, 1f, t);
                group.alpha = Mathf.SmoothStep(0.25f, 1f, t);
                yield return null;
            }
            content.localScale = Vector3.one;
            group.alpha = 1f;
        }

#if UNITY_EDITOR
        public void EditorConfigure(GameObject canvasRoot, RectTransform window, CanvasGroup windowGroup,
            TMP_Text greeting, Button closeButton, Button deliveriesTab, Image deliveriesTabHighlight,
            Button shopTab, Image shopTabHighlight, RectTransform viewport, ScrollRect scrollRect,
            RectTransform deliveryCardsRoot, CouchDispatchOptionCardView[] deliveryCards,
            RectTransform shopCardsRoot, CouchDispatchOptionCardView shopCardTemplate,
            GameObject filtersRoot, CouchDispatchFilterButtonView[] filters, CouchDispatchUIStyle style,
            GameObject eventSystemObject, EventSystem eventSystem)
        {
            m_canvasRoot = canvasRoot;
            m_window = window;
            m_windowGroup = windowGroup;
            m_greeting = greeting;
            m_closeButton = closeButton;
            m_deliveriesTab = deliveriesTab;
            m_deliveriesTabHighlight = deliveriesTabHighlight;
            m_shopTab = shopTab;
            m_shopTabHighlight = shopTabHighlight;
            m_viewport = viewport;
            m_scrollRect = scrollRect;
            m_deliveryCardsRoot = deliveryCardsRoot;
            m_deliveryCards = deliveryCards;
            m_shopCardsRoot = shopCardsRoot;
            m_shopCardTemplate = shopCardTemplate;
            m_filtersRoot = filtersRoot;
            m_filters = filters;
            m_style = style;
            m_dialogueEventSystemObject = eventSystemObject;
            m_dialogueEventSystem = eventSystem;
        }
#endif
    }
}
