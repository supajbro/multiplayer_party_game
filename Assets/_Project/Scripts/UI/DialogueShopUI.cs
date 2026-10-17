using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
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
            if (value.StartsWith("Need $", StringComparison.OrdinalIgnoreCase))
                return DialogueShopVisualState.Unaffordable;
            return DialogueShopVisualState.Locked;
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
    /// Local presentation for the server-authoritative delivery/shop flow. It deliberately owns no
    /// economy, ownership, equipment or networking logic; providers supply all data and callbacks.
    /// </summary>
    public sealed class DialogueShopUI : MonoBehaviour
    {
        private const string PrefabResource = "UI/CouchDispatchUI";
        private const string StyleResource = "UI/CouchDispatchUIStyle";
        private static DialogueShopUI s_instance;

        [Header("Generated View References")]
        [SerializeField] private GameObject m_canvasRoot;
        [SerializeField] private RectTransform m_window;
        [SerializeField] private CanvasGroup m_windowGroup;
        [SerializeField] private RectTransform m_optionsRoot;
        [SerializeField] private RectTransform m_tabsRoot;
        [SerializeField] private RectTransform m_filtersRoot;
        [SerializeField] private TMP_Text m_greeting;
        [SerializeField] private Button m_closeButton;
        [SerializeField] private CouchDispatchUIStyle m_style;

        private Action m_closed;
        [SerializeField] private GameObject m_dialogueEventSystemObject;
        [SerializeField] private EventSystem m_dialogueEventSystem;
        private readonly List<GameObject> m_optionObjects = new();
        private readonly List<GameObject> m_tabObjects = new();
        private readonly List<GameObject> m_filterObjects = new();
        private readonly List<EventSystem> m_suspendedEventSystems = new();
        private IReadOnlyList<DialogueShopCategory> m_categories;
        private int m_selectedCategory;
        private string m_selectedFilter = "All";
        private Coroutine m_transition;

        public static DialogueShopUI Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                GameObject prefab = Resources.Load<GameObject>(PrefabResource);
                s_instance = prefab != null
                    ? Instantiate(prefab).GetComponent<DialogueShopUI>()
                    : new GameObject("DialogueShopUI").AddComponent<DialogueShopUI>();
                return s_instance;
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
            Build();
            WireCloseButton();
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        public void Open(string greeting, IReadOnlyList<DialogueShopOption> options, Action closed) =>
            Open(greeting, new[] { new DialogueShopCategory("Deliveries", options) }, closed);

        public void Open(string greeting, IReadOnlyList<DialogueShopCategory> categories, Action closed,
            int selectedCategory = 0)
        {
            Build();
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
            RebuildTabs();
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
            Build();
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

        private void PlayWindowTransition()
        {
            if (m_window == null) return;
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

        private void Build()
        {
            if (m_canvasRoot != null) return;
            m_style ??= Resources.Load<CouchDispatchUIStyle>(StyleResource);
            BuildDialogueEventSystem();

            m_canvasRoot = new GameObject("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            m_canvasRoot.transform.SetParent(transform, false);
            Canvas canvas = m_canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = m_canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject shade = Panel("BackdropShade", m_canvasRoot.transform, null, new Color(0.015f, 0.035f, 0.08f, 0.64f));
            Stretch(shade.GetComponent<RectTransform>());
            GameObject windowObject = Panel("CouchDispatchWindow", shade.transform, Sprite("WindowFull"), Color.white);
            m_window = windowObject.GetComponent<RectTransform>();
            m_window.anchorMin = m_window.anchorMax = new Vector2(0.5f, 0.5f);
            m_window.pivot = new Vector2(0.5f, 0.5f);
            m_window.sizeDelta = new Vector2(1220f, 730f);
            windowObject.GetComponent<Image>().preserveAspect = true;
            m_windowGroup = windowObject.AddComponent<CanvasGroup>();

            m_greeting = Label("FundsAndNotice", m_window, string.Empty, 24f, FontStyles.Bold,
                new Color32(255, 240, 194, 255));
            SetRect(m_greeting.rectTransform, new Vector2(0.52f, 1f), new Vector2(0.9f, 1f),
                new Vector2(0f, -177f), new Vector2(0f, 38f));
            m_greeting.alignment = TextAlignmentOptions.MidlineRight;

            m_tabsRoot = new GameObject("TabHitAreas", typeof(RectTransform)).GetComponent<RectTransform>();
            m_tabsRoot.SetParent(m_window, false);
            Stretch(m_tabsRoot);

            m_filtersRoot = new GameObject("ShopCategoryFilters", typeof(RectTransform), typeof(HorizontalLayoutGroup))
                .GetComponent<RectTransform>();
            m_filtersRoot.SetParent(m_window, false);
            SetRect(m_filtersRoot, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(88f, -238f), new Vector2(-176f, 46f));
            HorizontalLayoutGroup filters = m_filtersRoot.GetComponent<HorizontalLayoutGroup>();
            filters.spacing = 12f;
            filters.childControlWidth = true;
            filters.childForceExpandWidth = true;
            filters.childControlHeight = true;
            filters.childForceExpandHeight = true;

            GameObject viewport = Panel("ScrollableContent", m_window, null, Color.clear);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(82f, 157f);
            viewportRect.offsetMax = new Vector2(-82f, -244f);
            viewport.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            GameObject options = new("Cards", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            options.transform.SetParent(viewport.transform, false);
            m_optionsRoot = options.GetComponent<RectTransform>();
            m_optionsRoot.anchorMin = new Vector2(0f, 1f);
            m_optionsRoot.anchorMax = new Vector2(1f, 1f);
            m_optionsRoot.pivot = new Vector2(0.5f, 1f);
            m_optionsRoot.anchoredPosition = Vector2.zero;
            m_optionsRoot.sizeDelta = Vector2.zero;
            scroll.viewport = viewportRect;
            scroll.content = m_optionsRoot;
            VerticalLayoutGroup layout = options.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 7, 7);
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            options.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            m_closeButton = InvisibleButton("CloseButton", m_window);
            SetRect(m_closeButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-124f, -28f), new Vector2(98f, 88f));
            WireCloseButton();
            m_canvasRoot.SetActive(false);
        }

        private void WireCloseButton()
        {
            if (m_closeButton == null) return;
            m_closeButton.onClick.RemoveAllListeners();
            m_closeButton.onClick.AddListener(() => Close());
        }

        private void BuildDialogueEventSystem()
        {
            if (m_dialogueEventSystemObject != null) return;
            Transform existing = transform.Find("DialogueEventSystem");
            if (existing != null)
            {
                m_dialogueEventSystemObject = existing.gameObject;
                m_dialogueEventSystem = existing.GetComponent<EventSystem>();
                if (m_dialogueEventSystem != null) return;
            }
            m_dialogueEventSystemObject = new GameObject("DialogueEventSystem");
            m_dialogueEventSystemObject.SetActive(false);
            m_dialogueEventSystemObject.transform.SetParent(transform, false);
            m_dialogueEventSystem = m_dialogueEventSystemObject.AddComponent<EventSystem>();
            InputSystemUIInputModule module = m_dialogueEventSystemObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private void ActivateDialogueInput()
        {
            BuildDialogueEventSystem();
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
            if (m_dialogueEventSystemObject != null) m_dialogueEventSystemObject.SetActive(false);
            foreach (EventSystem eventSystem in m_suspendedEventSystems)
                if (eventSystem != null) eventSystem.enabled = true;
            m_suspendedEventSystems.Clear();
        }

        private void RebuildTabs()
        {
            DestroyObjects(m_tabObjects);
            if (m_categories == null) return;
            for (int index = 0; index < m_categories.Count; index++)
            {
                int captured = index;
                Button tab = InvisibleButton($"{m_categories[index].Title}Tab", m_tabsRoot);
                RectTransform rect = tab.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = index == 0 ? new Vector2(126f, -142f) : new Vector2(614f, -142f);
                rect.sizeDelta = new Vector2(480f, 72f);
                tab.onClick.AddListener(() => ShowCategory(captured, true));
                m_tabObjects.Add(tab.gameObject);
            }
            RefreshTabSelection();
        }

        private void ShowCategory(int categoryIndex, bool animate)
        {
            if (m_categories == null || categoryIndex < 0 || categoryIndex >= m_categories.Count) return;
            m_selectedCategory = categoryIndex;
            m_selectedFilter = "All";
            RefreshTabSelection();
            RebuildFilters();
            RebuildOptions();
            if (animate) StartCoroutine(PulseContent());
        }

        private void RefreshTabSelection()
        {
            for (int index = 0; index < m_tabObjects.Count; index++)
            {
                Image highlight = m_tabObjects[index].GetComponent<Image>();
                highlight.color = index == m_selectedCategory
                    ? new Color(1f, 0.72f, 0.2f, 0.2f)
                    : new Color(0.05f, 0.15f, 0.3f, 0.08f);
            }
        }

        private void RebuildFilters()
        {
            DestroyObjects(m_filterObjects);
            bool shop = m_categories != null && m_selectedCategory < m_categories.Count &&
                        string.Equals(m_categories[m_selectedCategory].Title, "Shop", StringComparison.OrdinalIgnoreCase);
            m_filtersRoot.gameObject.SetActive(shop);
            RectTransform viewport = m_optionsRoot.parent as RectTransform;
            if (viewport != null) viewport.offsetMax = new Vector2(-82f, shop ? -292f : -244f);
            if (!shop) return;

            HashSet<string> groups = new(StringComparer.OrdinalIgnoreCase) { "All" };
            foreach (DialogueShopOption option in m_categories[m_selectedCategory].Options)
                if (!string.IsNullOrWhiteSpace(option.Group)) groups.Add(option.Group);
            foreach (string group in groups)
            {
                string captured = group;
                Button filter = TextButton($"Filter{group}", m_filtersRoot, group.ToUpperInvariant(),
                    group == m_selectedFilter ? new Color(1f, 0.5f, 0.08f, 0.95f) : new Color(0.08f, 0.3f, 0.55f, 0.95f));
                filter.onClick.AddListener(() =>
                {
                    m_selectedFilter = captured;
                    RebuildFilters();
                    RebuildOptions();
                    StartCoroutine(PulseContent());
                });
                m_filterObjects.Add(filter.gameObject);
            }
        }

        private void RebuildOptions()
        {
            DestroyObjects(m_optionObjects);
            IReadOnlyList<DialogueShopOption> options = m_categories[m_selectedCategory].Options;
            if (options == null) return;
            bool deliveries = string.Equals(m_categories[m_selectedCategory].Title, "Deliveries", StringComparison.OrdinalIgnoreCase);
            int visibleIndex = 0;
            foreach (DialogueShopOption option in options)
            {
                if (m_selectedFilter != "All" && !string.Equals(option.Group, m_selectedFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddOption(option, deliveries, visibleIndex++);
            }
        }

        private void AddOption(DialogueShopOption option, bool delivery, int visibleIndex)
        {
            GameObject card = Panel(option.Title, m_optionsRoot, Sprite("CardBlank"), Color.white);
            Image cardImage = card.GetComponent<Image>();
            cardImage.type = Image.Type.Sliced;
            cardImage.color = delivery
                ? Color.Lerp(new Color(0.09f, 0.14f, 0.21f, 0.98f), option.Colour, 0.18f)
                : new Color(0.08f, 0.14f, 0.22f, 0.98f);
            card.AddComponent<LayoutElement>().preferredHeight = delivery ? 96f : 112f;
            m_optionObjects.Add(card);

            Sprite featureSprite = option.Icon;
            if (featureSprite == null && delivery)
                featureSprite = Sprite(visibleIndex switch { 0 => "DeliveryEasy", 1 => "DeliveryMedium", _ => "DeliveryHard" });
            if (featureSprite == null && !delivery)
                featureSprite = Sprite(option.Group == "Weapons" ? "WeaponsIcon" : option.Group == "Upgrades" ? "UpgradeCrate" : "VanIcon");

            GameObject iconObject = Panel("Artwork", card.transform, featureSprite, Color.white);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0f);
            iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(12f, 0f);
            iconRect.sizeDelta = new Vector2(delivery ? 190f : 118f, delivery ? -10f : -12f);
            iconObject.GetComponent<Image>().preserveAspect = true;

            float textLeft = delivery ? 214f : 145f;
            TMP_Text title = Label("Title", card.transform, option.Title, delivery ? 23f : 25f,
                FontStyles.Bold, Color.white);
            SetRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.57f, 1f),
                new Vector2(textLeft, 0f), new Vector2(-textLeft, -4f));
            title.alignment = TextAlignmentOptions.BottomLeft;

            TMP_Text description = Label("Description", card.transform, option.Description, 17f,
                FontStyles.Normal, new Color32(224, 236, 246, 255));
            SetRect(description.rectTransform, new Vector2(0f, 0f), new Vector2(0.57f, 0.55f),
                new Vector2(textLeft, 5f), new Vector2(-textLeft, -2f));
            description.alignment = TextAlignmentOptions.TopLeft;

            string value = string.IsNullOrWhiteSpace(option.Reward)
                ? option.Price
                : $"{option.Reward} PAYOUT\n<size=70%>Cost: {option.Price}</size>";
            TMP_Text economy = Label("Economy", card.transform, value, 21f, FontStyles.Bold,
                new Color32(255, 231, 120, 255));
            SetRect(economy.rectTransform, new Vector2(0.58f, 0f), new Vector2(0.78f, 1f),
                new Vector2(5f, 4f), new Vector2(-10f, -8f));
            economy.alignment = TextAlignmentOptions.MidlineLeft;

            string actionText = delivery ? "SELECT" : "BUY";
            Color actionColour = new Color(1f, 0.48f, 0.06f, 1f);
            switch (option.VisualState)
            {
                case DialogueShopVisualState.Unaffordable: actionText = "NEED CASH"; actionColour = new Color(0.32f, 0.35f, 0.42f); break;
                case DialogueShopVisualState.Owned: actionText = "OWNED"; actionColour = new Color(0.12f, 0.48f, 0.75f); break;
                case DialogueShopVisualState.Equipped: actionText = "EQUIPPED"; actionColour = new Color(0.15f, 0.62f, 0.42f); break;
                case DialogueShopVisualState.Locked: actionText = "LOCKED"; actionColour = new Color(0.28f, 0.3f, 0.36f); break;
            }

            Button action = TextButton("Action", card.transform, actionText, actionColour);
            RectTransform actionRect = action.GetComponent<RectTransform>();
            actionRect.anchorMin = new Vector2(0.79f, 0.18f);
            actionRect.anchorMax = new Vector2(0.985f, 0.82f);
            actionRect.offsetMin = actionRect.offsetMax = Vector2.zero;
            action.interactable = option.Available;
            if (option.Available) action.onClick.AddListener(() => option.Selected?.Invoke());

            if (!option.Available)
            {
                TMP_Text reason = Label("UnavailableReason", card.transform, option.UnavailableReason, 13f,
                    FontStyles.Bold, new Color32(255, 190, 175, 255));
                SetRect(reason.rectTransform, new Vector2(0.58f, 0f), new Vector2(0.985f, 0f),
                    new Vector2(5f, 2f), new Vector2(-10f, 18f));
                reason.alignment = TextAlignmentOptions.BottomRight;
            }
        }

        private IEnumerator PulseContent()
        {
            if (m_optionsRoot == null) yield break;
            m_optionsRoot.localScale = new Vector3(0.985f, 0.985f, 1f);
            CanvasGroup group = m_optionsRoot.GetComponent<CanvasGroup>() ?? m_optionsRoot.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.25f;
            float elapsed = 0f;
            while (elapsed < 0.16f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.16f);
                m_optionsRoot.localScale = Vector3.one * Mathf.Lerp(0.985f, 1f, t);
                group.alpha = Mathf.SmoothStep(0.25f, 1f, t);
                yield return null;
            }
            m_optionsRoot.localScale = Vector3.one;
            group.alpha = 1f;
        }

        private Sprite Sprite(string name) => m_style != null ? m_style.Get(name) : null;

        private static GameObject Panel(string name, Transform parent, Sprite sprite, Color colour)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false;
            return panel;
        }

        private static Button InvisibleButton(string name, Transform parent)
        {
            GameObject root = Panel(name, parent, null, new Color(1f, 1f, 1f, 0.01f));
            root.GetComponent<Image>().raycastTarget = true;
            Button button = root.AddComponent<Button>();
            root.AddComponent<CouchDispatchUIFeedback>();
            return button;
        }

        private Button TextButton(string name, Transform parent, string label, Color colour)
        {
            GameObject root = Panel(name, parent, Sprite("CardBlank"), colour);
            Image image = root.GetComponent<Image>();
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            Button button = root.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = colour;
            colors.highlightedColor = Color.Lerp(colour, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(colour, Color.black, 0.18f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(colour.r * 0.6f, colour.g * 0.6f, colour.b * 0.6f, 0.85f);
            button.colors = colors;
            TMP_Text text = Label("Label", root.transform, label, 20f, FontStyles.Bold, Color.white);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            root.AddComponent<CouchDispatchUIFeedback>();
            return button;
        }

        private static TMP_Text Label(string name, Transform parent, string value, float size,
            FontStyles style, Color colour)
        {
            GameObject root = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            root.transform.SetParent(parent, false);
            TextMeshProUGUI text = root.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = colour;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void DestroyObjects(List<GameObject> objects)
        {
            foreach (GameObject item in objects)
                if (item != null) Destroy(item);
            objects.Clear();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

#if UNITY_EDITOR
        public void RebuildEditorPrefab(CouchDispatchUIStyle style)
        {
            m_style = style;
            while (transform.childCount > 0) DestroyImmediate(transform.GetChild(0).gameObject);
            m_canvasRoot = null;
            m_dialogueEventSystemObject = null;
            m_dialogueEventSystem = null;
            Build();
        }
#endif
    }
}
