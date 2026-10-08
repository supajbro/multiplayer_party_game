using CouchGuys.Gameplay.Delivery;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Owner-only Couch Guys HUD, built at runtime from reusable uGUI primitives.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(PlayerWeaponController), typeof(PlayerHealth))]
    [RequireComponent(typeof(PlayerStamina))]
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponHud : MonoBehaviour
    {
        private static readonly Color Charcoal = new(0.055f, 0.07f, 0.09f, 0.94f);
        private static readonly Color CharcoalSoft = new(0.08f, 0.1f, 0.13f, 0.88f);
        private static readonly Color Gold = new(1f, 0.66f, 0.08f, 1f);
        private static readonly Color Inactive = new(0.36f, 0.4f, 0.45f, 1f);
        private static readonly Color Green = new(0.25f, 0.92f, 0.35f, 1f);
        private static readonly Color Yellow = new(1f, 0.7f, 0.08f, 1f);
        private static readonly Color Red = new(1f, 0.2f, 0.15f, 1f);

        private NetworkObject m_networkObject;
        private PlayerWeaponController m_weapons;
        private PlayerHealth m_health;
        private PlayerStamina m_stamina;
        private DeliveryManager m_delivery;
        private SuburbsChapterManager m_chapter;
        private GameObject m_canvasRoot;
        private Text m_healthText;
        private Text m_deliveryTitle;
        private Text m_deliveryDetail;
        private Text m_deliveryDistance;
        private Text m_currencyText;
        private Text m_ammoText;
        private Image m_healthFill;
        private Image m_staminaFill;
        private Image[] m_slotBackgrounds;
        private Outline[] m_slotOutlines;
        private GameObject m_ammoPanel;
        private float m_healthTarget = 1f;
        private float m_healthDisplay = 1f;
        private float m_staminaTarget = 1f;
        private float m_staminaDisplay = 1f;
        private float m_nextSlowRefresh;
        private int m_lastSelectedSlot = -1;
        private RenderTexture m_portraitTexture;
        private Camera m_portraitCamera;
        private Sprite m_roundedSprite;
        private Sprite m_circleSprite;

        private void Awake()
        {
            m_networkObject = GetComponent<NetworkObject>();
            m_weapons = GetComponent<PlayerWeaponController>();
            m_health = GetComponent<PlayerHealth>();
            m_stamina = GetComponent<PlayerStamina>();
        }

        private void Update()
        {
            if (m_networkObject == null || !m_networkObject.IsOwner) return;
            if (m_canvasRoot == null)
            {
                BuildHud();
                BindEvents();
            }

            m_healthDisplay = Mathf.MoveTowards(m_healthDisplay, m_healthTarget, Time.unscaledDeltaTime * 2.8f);
            m_staminaDisplay = Mathf.MoveTowards(m_staminaDisplay, m_staminaTarget, Time.unscaledDeltaTime * 3.5f);
            SetBar(m_healthFill, m_healthDisplay);
            SetBar(m_staminaFill, m_staminaDisplay);
            m_healthFill.color = MeterColour(m_healthDisplay);
            m_staminaFill.color = MeterColour(m_staminaDisplay);

            RefreshWeapons();
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
            if (m_delivery == null) m_delivery = FindFirstObjectByType<DeliveryManager>();
            if (m_chapter == null) m_chapter = FindFirstObjectByType<SuburbsChapterManager>();
        }

        private void OnHealthChanged(float current, float maximum)
        {
            m_healthTarget = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            if (m_healthText != null) m_healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maximum)}";
        }

        private void OnStaminaChanged(float current, float maximum) =>
            m_staminaTarget = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;

        private void RefreshWeapons()
        {
            int selected = m_weapons.SelectedHotbarSlot;
            if (selected != m_lastSelectedSlot)
            {
                for (int i = 0; i < m_slotBackgrounds.Length; i++)
                {
                    bool active = i == selected;
                    m_slotBackgrounds[i].color = active ? new Color(0.13f, 0.14f, 0.15f, 0.98f) : CharcoalSoft;
                    m_slotOutlines[i].effectColor = active ? Gold : Inactive;
                    m_slotOutlines[i].effectDistance = active ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
                }
                m_lastSelectedSlot = selected;
            }

            bool showAmmo = m_weapons.HasEquippedWeapon;
            if (m_ammoPanel.activeSelf != showAmmo) m_ammoPanel.SetActive(showAmmo);
            if (showAmmo)
            {
                m_ammoText.text = m_weapons.IsReloading
                    ? $"{m_weapons.EquippedWeaponName}\n<size=24>RELOADING  {m_weapons.CurrentMagazine} / {m_weapons.CurrentReserve}</size>"
                    : $"{m_weapons.EquippedWeaponName}\n<size=30>{m_weapons.CurrentMagazine} / {m_weapons.CurrentReserve}</size>";
            }
        }

        private void RefreshDeliveryAndCurrency()
        {
            m_currencyText.text = $"$ {(m_delivery != null ? m_delivery.CurrentCurrency : 0):N0}";
            int completed = m_chapter != null ? m_chapter.CompletedDeliveryCount : 0;
            if (m_delivery != null && m_delivery.HasActiveDelivery && m_delivery.ActiveDestination != null)
            {
                DeliveryDestination destination = m_delivery.ActiveDestination;
                int number = m_delivery.ActiveStageIndex >= 0 ? m_delivery.ActiveStageIndex + 1 : completed + 1;
                m_deliveryTitle.text = $"Delivery {number}";
                m_deliveryDetail.text = $"Take couch to {destination.DisplayName}";
                Vector3 delta = destination.DropPosition.position - transform.position;
                delta.y = 0f;
                m_deliveryDistance.text = $"●  {Mathf.RoundToInt(delta.magnitude)} m";
            }
            else
            {
                m_deliveryTitle.text = completed > 0 ? $"Delivery {completed}" : "Deliveries";
                m_deliveryDetail.text = m_chapter != null && m_chapter.ChapterCompleted
                    ? "Region complete!"
                    : "Visit the dispatcher for a couch";
                m_deliveryDistance.text = string.Empty;
            }
        }

        private void BuildHud()
        {
            BuildSprites();
            m_canvasRoot = new GameObject("CouchGuysHud", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = m_canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = m_canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            BuildHealthPanel();
            BuildDeliveryPanel();
            BuildCurrencyPanel();
            BuildStaminaPanel();
            BuildHotbar();
            BuildAmmoPanel();
        }

        private void BuildHealthPanel()
        {
            RectTransform panel = Panel("HealthPanel", m_canvasRoot.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(455f, 122f), Gold);
            RectTransform portraitFrame = Panel("PortraitFrame", panel, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(98f, 98f), Gold);
            portraitFrame.pivot = new Vector2(0f, 0.5f);
            Image maskImage = portraitFrame.GetComponent<Image>();
            maskImage.sprite = m_circleSprite;
            portraitFrame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            GameObject portrait = new("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            portrait.transform.SetParent(portraitFrame, false);
            Stretch(portrait.GetComponent<RectTransform>(), 5f);
            BuildPortrait(portrait.GetComponent<RawImage>());

            Text name = Label("Name", panel, "You", 25, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(126f, -14f), new Vector2(290f, 34f), new Vector2(0f, 1f));
            RectTransform bar = Panel("HealthBar", panel, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(126f, -4f), new Vector2(307f, 31f), Inactive, false);
            bar.pivot = new Vector2(0f, 0.5f);
            m_healthFill = Fill(bar, "HealthFill");
            m_healthText = Label("HealthText", panel, "100 / 100", 20, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(m_healthText.rectTransform, Vector2.zero, Vector2.zero, new Vector2(126f, 11f), new Vector2(280f, 30f), Vector2.zero);
        }

        private void BuildDeliveryPanel()
        {
            RectTransform panel = Panel("DeliveryPanel", m_canvasRoot.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(480f, 145f), Gold);
            m_deliveryTitle = Label("DeliveryTitle", panel, "Deliveries", 32, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(m_deliveryTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(420f, 42f), new Vector2(0f, 1f));
            m_deliveryDetail = Label("DeliveryDetail", panel, "Visit the dispatcher for a couch", 23, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(m_deliveryDetail.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -58f), new Vector2(432f, 35f), new Vector2(0f, 1f));
            m_deliveryDistance = Label("DeliveryDistance", panel, string.Empty, 24, TextAnchor.MiddleLeft, FontStyle.Bold, Yellow);
            SetRect(m_deliveryDistance.rectTransform, Vector2.zero, Vector2.zero, new Vector2(24f, 13f), new Vector2(220f, 35f), Vector2.zero);
        }

        private void BuildCurrencyPanel()
        {
            RectTransform panel = Panel("CurrencyPanel", m_canvasRoot.transform, Vector2.one, Vector2.one, new Vector2(-24f, -24f), new Vector2(260f, 76f), new Color(0.55f, 0.82f, 1f), true, new Vector2(1f, 1f));
            Text icon = Label("CurrencyIcon", panel, "$", 31, TextAnchor.MiddleCenter, FontStyle.Bold, Color.white);
            SetRect(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(15f, 0f), new Vector2(52f, 52f), new Vector2(0f, 0.5f));
            icon.transform.parent.GetComponent<Image>();
            Image iconBackground = icon.gameObject.AddComponent<Image>();
            iconBackground.sprite = m_circleSprite;
            iconBackground.color = new Color(0.12f, 0.75f, 0.28f);
            icon.transform.SetAsFirstSibling();
            m_currencyText = Label("CurrencyText", panel, "$ 0", 34, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(m_currencyText.rectTransform, Vector2.zero, Vector2.one, new Vector2(82f, 8f), new Vector2(-10f, -8f));
        }

        private void BuildStaminaPanel()
        {
            RectTransform panel = Panel("StaminaPanel", m_canvasRoot.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(410f, 42f), Gold);
            Text bolt = Label("Bolt", panel, "⚡", 22, TextAnchor.MiddleCenter, FontStyle.Bold, Yellow);
            SetRect(bolt.rectTransform, Vector2.zero, new Vector2(0f, 1f), new Vector2(8f, 5f), new Vector2(40f, -5f));
            RectTransform bar = Panel("StaminaTrack", panel, Vector2.zero, Vector2.one, new Vector2(47f, 7f), new Vector2(-10f, -7f), Inactive, false);
            m_staminaFill = Fill(bar, "StaminaFill");
        }

        private void BuildHotbar()
        {
            RectTransform root = Rect("Hotbar", m_canvasRoot.transform);
            SetRect(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(548f, 108f), new Vector2(0.5f, 0f));
            m_slotBackgrounds = new Image[5];
            m_slotOutlines = new Outline[5];
            string[] icons = { "P", "AR", "SG", "—", "—" };
            for (int i = 0; i < 5; i++)
            {
                RectTransform slot = Panel($"Slot{i + 1}", root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * 110f, 0f), new Vector2(98f, 98f), Inactive);
                slot.pivot = new Vector2(0f, 0.5f);
                m_slotBackgrounds[i] = slot.GetComponent<Image>();
                m_slotOutlines[i] = slot.GetComponent<Outline>();
                Text number = Label("Number", slot, (i + 1).ToString(), 18, TextAnchor.MiddleCenter, FontStyle.Bold);
                SetRect(number.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 7f), new Vector2(30f, 27f), new Vector2(0.5f, 1f));
                number.gameObject.AddComponent<Outline>().effectColor = Color.black;
                Text icon = Label("Icon", slot, icons[i], i == 1 ? 25 : 34, TextAnchor.MiddleCenter, FontStyle.Bold, i < 3 ? new Color(1f, 0.55f, 0.12f) : Inactive);
                Stretch(icon.rectTransform, 10f);
            }
        }

        private void BuildAmmoPanel()
        {
            RectTransform panel = Panel("AmmoPanel", m_canvasRoot.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(270f, 94f), Inactive, true, new Vector2(1f, 0f));
            m_ammoPanel = panel.gameObject;
            m_ammoText = Label("AmmoText", panel, string.Empty, 22, TextAnchor.MiddleRight, FontStyle.Bold);
            Stretch(m_ammoText.rectTransform, 14f);
        }

        private void BuildPortrait(RawImage image)
        {
            m_portraitTexture = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32) { name = "LocalPlayerPortrait" };
            m_portraitTexture.Create();
            image.texture = m_portraitTexture;
            image.color = Color.white;
            image.raycastTarget = false;

            GameObject cameraObject = new("PortraitCamera", typeof(Camera), typeof(Light));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 2.0f, 2.4f);
            cameraObject.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.08f, -1f));
            m_portraitCamera = cameraObject.GetComponent<Camera>();
            m_portraitCamera.targetTexture = m_portraitTexture;
            m_portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            m_portraitCamera.backgroundColor = new Color(0.75f, 0.9f, 1f, 0f);
            m_portraitCamera.fieldOfView = 29f;
            m_portraitCamera.nearClipPlane = 0.1f;
            m_portraitCamera.farClipPlane = 5f;
            const int portraitLayer = 30;
            m_portraitCamera.cullingMask = 1 << portraitLayer;
            Transform visual = transform.Find("Visual");
            if (visual != null)
                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                    renderer.gameObject.layer = portraitLayer;
            Light fill = cameraObject.GetComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.65f;
            fill.cullingMask = 1 << portraitLayer;
        }

        private RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color outline, bool shadow = true, Vector2? pivot = null)
        {
            RectTransform rect = Rect(name, parent);
            SetRect(rect, anchorMin, anchorMax, position, size, pivot ?? new Vector2(anchorMin.x, anchorMin.y));
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = m_roundedSprite;
            image.type = Image.Type.Sliced;
            image.color = Charcoal;
            Outline border = rect.gameObject.AddComponent<Outline>();
            border.effectColor = outline;
            border.effectDistance = new Vector2(3f, -3f);
            if (shadow)
            {
                Shadow panelShadow = rect.gameObject.AddComponent<Shadow>();
                panelShadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
                panelShadow.effectDistance = new Vector2(7f, -7f);
            }
            return rect;
        }

        private RectTransform Rect(string name, Transform parent)
        {
            GameObject gameObject = new(name, typeof(RectTransform), typeof(CanvasRenderer));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        private Text Label(string name, Transform parent, string value, int size, TextAnchor alignment, FontStyle style, Color? colour = null)
        {
            RectTransform rect = Rect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = colour ?? Color.white;
            text.raycastTarget = false;
            return text;
        }

        private Image Fill(Transform parent, string name)
        {
            RectTransform rect = Rect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 4f);
            rect.offsetMax = new Vector2(-4f, -4f);
            rect.pivot = new Vector2(0f, 0.5f);
            Image fill = rect.gameObject.AddComponent<Image>();
            fill.sprite = m_roundedSprite;
            fill.type = Image.Type.Sliced;
            fill.color = Green;
            return fill;
        }

        private void BuildSprites()
        {
            m_roundedSprite = MakeRoundedSprite(32, 8f);
            m_circleSprite = MakeRoundedSprite(32, 16f);
        }

        private static Sprite MakeRoundedSprite(int size, float radius)
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false) { name = "HudRoundedShape" };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x - 0.5f, 0f, x + 0.5f - (size - radius));
                float dy = Mathf.Max(radius - y - 0.5f, 0f, y + 0.5f - (size - radius));
                float alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        private static Color MeterColour(float value) => value > 0.55f ? Green : value > 0.25f ? Yellow : Red;

        private static void SetBar(Image fill, float value)
        {
            RectTransform rect = fill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void OnDestroy()
        {
            if (m_health != null) m_health.HealthChanged -= OnHealthChanged;
            if (m_stamina != null) m_stamina.StaminaChanged -= OnStaminaChanged;
            if (m_canvasRoot != null) Destroy(m_canvasRoot);
            if (m_portraitTexture != null)
            {
                m_portraitTexture.Release();
                Destroy(m_portraitTexture);
            }
            if (m_roundedSprite != null)
            {
                Destroy(m_roundedSprite.texture);
                Destroy(m_roundedSprite);
            }
            if (m_circleSprite != null)
            {
                Destroy(m_circleSprite.texture);
                Destroy(m_circleSprite);
            }
        }
    }
}