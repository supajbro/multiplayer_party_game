using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    public readonly struct DialogueShopOption
    {
        public readonly string Title;
        public readonly string Description;
        public readonly string Price;
        public readonly string Reward;
        public readonly Color Colour;
        public readonly bool Available;
        public readonly string UnavailableReason;
        public readonly Action Selected;

        public DialogueShopOption(string title, string description, string price, string reward,
            Color colour, bool available, string unavailableReason, Action selected)
        {
            Title = title;
            Description = description;
            Price = price;
            Reward = reward;
            Colour = colour;
            Available = available;
            UnavailableReason = unavailableReason;
            Selected = selected;
        }
    }

    /// <summary>Reusable local-only dialogue/shop presenter. Providers supply data and actions.</summary>
    public sealed class DialogueShopUI : MonoBehaviour
    {
        private static DialogueShopUI s_instance;
        private GameObject m_canvasRoot;
        private RectTransform m_optionsRoot;
        private Text m_greeting;
        private Action m_closed;
        private GameObject m_dialogueEventSystemObject;
        private EventSystem m_dialogueEventSystem;
        private readonly List<GameObject> m_optionObjects = new();
        private readonly List<EventSystem> m_suspendedEventSystems = new();

        public static DialogueShopUI Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = new GameObject("DialogueShopUI").AddComponent<DialogueShopUI>();
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
        }

        public void Open(string greeting, IReadOnlyList<DialogueShopOption> options, Action closed)
        {
            Build();
            ActivateDialogueInput();
            CancelInvoke(nameof(HideNotice));
            m_closed = closed;
            m_greeting.text = greeting;
            foreach (GameObject optionObject in m_optionObjects) Destroy(optionObject);
            m_optionObjects.Clear();
            for (int i = 0; i < options.Count; i++) AddOption(options[i]);
            m_canvasRoot.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close(bool notifyProvider = true)
        {
            if (!IsOpen) return;
            m_canvasRoot.SetActive(false);
            DeactivateDialogueInput();
            Action closed = m_closed;
            m_closed = null;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (notifyProvider) closed?.Invoke();
        }

        public void ShowNotice(string message)
        {
            Build();
            m_greeting.text = message;
            m_canvasRoot.SetActive(true);
            CancelInvoke(nameof(HideNotice));
            Invoke(nameof(HideNotice), 2.5f);
        }

        private void HideNotice()
        {
            if (m_closed == null && m_canvasRoot != null) m_canvasRoot.SetActive(false);
        }

        private void Build()
        {
            if (m_canvasRoot != null) return;
            BuildDialogueEventSystem();
            m_canvasRoot = new GameObject("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            m_canvasRoot.transform.SetParent(transform, false);
            Canvas canvas = m_canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = m_canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject shade = Panel("Shade", m_canvasRoot.transform, new Color(0.035f, 0.025f, 0.06f, 0.82f));
            Stretch(shade.GetComponent<RectTransform>());
            GameObject card = Panel("DialogueCard", shade.transform, new Color(0.12f, 0.16f, 0.24f, 0.98f));
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(900f, 760f);
            Outline outline = card.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.66f, 0.12f, 1f);
            outline.effectDistance = new Vector2(6f, -6f);

            Text title = Label("Title", card.transform, "DELIVERY DISPATCH", 42, FontStyle.Bold);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -80f), new Vector2(-64f, 58f));
            title.alignment = TextAnchor.MiddleCenter;
            title.color = new Color(1f, 0.78f, 0.2f);
            m_greeting = Label("Greeting", card.transform, string.Empty, 25, FontStyle.Normal);
            SetRect(m_greeting.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(46f, -142f), new Vector2(-92f, 54f));
            m_greeting.alignment = TextAnchor.MiddleCenter;

            GameObject options = new GameObject("Options", typeof(RectTransform), typeof(VerticalLayoutGroup));
            options.transform.SetParent(card.transform, false);
            m_optionsRoot = options.GetComponent<RectTransform>();
            m_optionsRoot.anchorMin = new Vector2(0f, 0f);
            m_optionsRoot.anchorMax = new Vector2(1f, 1f);
            m_optionsRoot.offsetMin = new Vector2(46f, 96f);
            m_optionsRoot.offsetMax = new Vector2(-46f, -176f);
            VerticalLayoutGroup layout = options.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            Button close = Button("Close", card.transform, "CLOSE", new Color(0.75f, 0.19f, 0.22f));
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0.5f, 0f);
            closeRect.anchoredPosition = new Vector2(0f, 24f);
            closeRect.sizeDelta = new Vector2(240f, 58f);
            close.onClick.AddListener(() => Close());
            m_canvasRoot.SetActive(false);
        }

        private void BuildDialogueEventSystem()
        {
            if (m_dialogueEventSystemObject != null) return;
            m_dialogueEventSystemObject = new GameObject("DialogueEventSystem");
            m_dialogueEventSystemObject.SetActive(false);
            m_dialogueEventSystemObject.transform.SetParent(transform, false);
            m_dialogueEventSystem = m_dialogueEventSystemObject.AddComponent<EventSystem>();
            InputSystemUIInputModule inputModule =
                m_dialogueEventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }

        private void ActivateDialogueInput()
        {
            BuildDialogueEventSystem();
            m_suspendedEventSystems.Clear();
            EventSystem[] eventSystems = FindObjectsByType<EventSystem>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int index = 0; index < eventSystems.Length; index++)
            {
                EventSystem eventSystem = eventSystems[index];
                if (eventSystem == null || eventSystem == m_dialogueEventSystem || !eventSystem.enabled)
                    continue;
                m_suspendedEventSystems.Add(eventSystem);
                eventSystem.enabled = false;
            }

            m_dialogueEventSystemObject.SetActive(true);
            m_dialogueEventSystem.enabled = true;
        }

        private void DeactivateDialogueInput()
        {
            if (m_dialogueEventSystemObject != null)
                m_dialogueEventSystemObject.SetActive(false);
            for (int index = 0; index < m_suspendedEventSystems.Count; index++)
            {
                EventSystem eventSystem = m_suspendedEventSystems[index];
                if (eventSystem != null) eventSystem.enabled = true;
            }
            m_suspendedEventSystems.Clear();
        }

        private void AddOption(DialogueShopOption option)
        {
            Color background = option.Available ? option.Colour : new Color(0.24f, 0.25f, 0.29f);
            Button button = Button(option.Title, m_optionsRoot, string.Empty, background);
            m_optionObjects.Add(button.gameObject);
            Text text = button.GetComponentInChildren<Text>();
            text.fontSize = 23;
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(26f, 10f);
            text.rectTransform.offsetMax = new Vector2(-26f, -10f);
            string status = option.Available ? $"Cost: {option.Price}     Payout: {option.Reward}" : option.UnavailableReason;
            text.text = $"{option.Title}\n<size=19>{option.Description}\n{status}</size>";
            button.interactable = option.Available;
            if (option.Available) button.onClick.AddListener(() => option.Selected?.Invoke());
        }

        private static GameObject Panel(string name, Transform parent, Color colour)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = colour;
            return panel;
        }

        private static Button Button(string name, Transform parent, string label, Color colour)
        {
            GameObject root = Panel(name, parent, colour);
            Button button = root.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(colour, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(colour, Color.black, 0.15f);
            colors.disabledColor = new Color(0.25f, 0.25f, 0.27f, 0.8f);
            button.colors = colors;
            Text text = Label("Label", root.transform, label, 24, FontStyle.Bold);
            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private static Text Label(string name, Transform parent, string value, int size, FontStyle style)
        {
            GameObject root = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            root.transform.SetParent(parent, false);
            Text text = root.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.supportRichText = true;
            return text;
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
    }
}
