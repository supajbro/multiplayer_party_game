using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.UI;
using TMPro;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace CouchGuys.Editor
{
    /// <summary>Non-destructively imports, slices and packages the authored Couch Dispatch sprite sheet.</summary>
    public static class CouchDispatchUIBuilder
    {
        private const string SheetPath = "Assets/_Project/UI/Couch Dispatch UI Sprite Kit.png";
        private const string ResourceFolder = "Assets/_Project/Resources/UI";
        private const string StylePath = ResourceFolder + "/CouchDispatchUIStyle.asset";
        private const string PrefabFolder = "Assets/_Project/Prefabs/UI";
        private const string PrefabPath = PrefabFolder + "/CouchDispatchUI.prefab";
        private const string AtlasPath = "Assets/_Project/UI/CouchDispatchUI.spriteatlas";
        private const int SheetHeight = 1536;

        private readonly struct Slice
        {
            public readonly string Name;
            public readonly Rect Rect;
            public readonly Vector4 Border;

            public Slice(string name, int x, int top, int width, int height, Vector4 border = default)
            {
                Name = name;
                Rect = new Rect(x, SheetHeight - top - height, width, height);
                Border = border;
            }
        }

        // Coordinates are measured against the original 1024 x 1536 source, top-left origin.
        // They intentionally hug each authored element so neighbouring art never bleeds into a slice.
        private static readonly Slice[] Slices =
        {
            new("WindowFull", 14, 10, 995, 598),
            new("WindowHeader", 237, 10, 553, 92, new Vector4(42, 25, 42, 25)),
            new("CloseButton", 921, 25, 85, 74, new Vector4(20, 20, 20, 20)),
            new("TabDeliveries", 106, 116, 405, 63, new Vector4(24, 20, 24, 20)),
            new("TabShop", 515, 116, 406, 63, new Vector4(24, 20, 24, 20)),
            new("ContentPanel", 65, 198, 895, 271, new Vector4(30, 30, 30, 30)),
            new("ShopPrompt", 214, 495, 599, 72, new Vector4(24, 20, 24, 20)),

            new("SectionWindowPanels", 18, 634, 324, 60, new Vector4(22, 18, 22, 18)),
            new("SectionButtonsTabs", 355, 634, 325, 60, new Vector4(22, 18, 22, 18)),
            new("CardBlank", 698, 633, 308, 152, new Vector4(30, 30, 30, 30)),
            new("CardLocked", 698, 793, 308, 105, new Vector4(26, 24, 26, 24)),

            new("ButtonSelectPrimary", 18, 709, 208, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonBuyPrimary", 245, 709, 204, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonEquip", 468, 709, 214, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonSelectPressed", 18, 784, 208, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonBuyPressed", 246, 784, 204, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonBackLocked", 470, 784, 214, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonBack", 18, 859, 208, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonOkay", 246, 859, 214, 63, new Vector4(25, 20, 25, 20)),
            new("ButtonLocked", 471, 859, 214, 63, new Vector4(25, 20, 25, 20)),

            new("DeliveryEasy", 18, 939, 310, 154, new Vector4(25, 25, 25, 25)),
            new("DeliveryMedium", 348, 939, 314, 154, new Vector4(25, 25, 25, 25)),
            new("DeliveryHard", 691, 939, 316, 154, new Vector4(25, 25, 25, 25)),
            new("CategoryVans", 18, 1105, 226, 63, new Vector4(25, 20, 25, 20)),
            new("CategoryWeapons", 269, 1105, 233, 63, new Vector4(25, 20, 25, 20)),
            new("CategoryUpgrades", 519, 1105, 239, 63, new Vector4(25, 20, 25, 20)),
            new("PageDots", 773, 1105, 235, 63, new Vector4(25, 20, 25, 20)),

            new("VanIcon", 23, 1193, 213, 116),
            new("WeaponsIcon", 252, 1184, 250, 136),
            new("UpgradeCrate", 506, 1182, 174, 133),
            new("CurrencyCoin", 687, 1184, 104, 108),
            new("CurrencyCoins", 803, 1185, 205, 113),
            new("ClipboardIcon", 31, 1328, 137, 124),
            new("ArrowIcon", 186, 1333, 148, 111),
            new("CashIcon", 349, 1322, 154, 123),
            new("EnergySmall", 505, 1310, 101, 137),
            new("EnergyLarge", 620, 1306, 107, 144),
            new("CashReward", 738, 1309, 165, 135),
            new("Sparkles", 906, 1305, 102, 143)
        };

        [MenuItem("Couch Guys/UI/Rebuild Couch Dispatch UI (Overwrites Prefab)")]
        public static void BuildAll()
        {
            if (!File.Exists(SheetPath))
                throw new FileNotFoundException("Couch Dispatch sprite sheet was not found.", SheetPath);

            ConfigureAndSliceSheet();
            EnsureFolder(ResourceFolder);
            EnsureFolder(PrefabFolder);
            CouchDispatchUIStyle style = BuildStyleAsset();
            BuildAtlas();
            BuildPrefab(style);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Couch Dispatch UI rebuilt: {Slices.Length} sprites, style, atlas and prefab.");
        }

        public static void BuildAllBatch() => BuildAll();

        private static void ConfigureAndSliceSheet()
        {
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(SheetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException($"No TextureImporter for {SheetPath}");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;

#pragma warning disable 0618
            importer.spritesheet = Slices.Select(slice => new SpriteMetaData
            {
                name = slice.Name,
                rect = slice.Rect,
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = slice.Border
            }).ToArray();
#pragma warning restore 0618
            importer.SaveAndReimport();
        }

        private static CouchDispatchUIStyle BuildStyleAsset()
        {
            CouchDispatchUIStyle style = AssetDatabase.LoadAssetAtPath<CouchDispatchUIStyle>(StylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<CouchDispatchUIStyle>();
                AssetDatabase.CreateAsset(style, StylePath);
            }

            Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath)
                .OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, StringComparer.Ordinal);
            CouchDispatchUIStyle.SpriteEntry[] entries = Slices
                .Where(slice => sprites.ContainsKey(slice.Name))
                .Select(slice => new CouchDispatchUIStyle.SpriteEntry
                {
                    Name = slice.Name,
                    Sprite = sprites[slice.Name]
                }).ToArray();
            style.SetSprites(entries);
            EditorUtility.SetDirty(style);
            return style;
        }

        private static void BuildAtlas()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                AssetDatabase.CreateAsset(atlas, AtlasPath);
            }

            SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.padding = 4;
            atlas.SetPackingSettings(packing);
            SpriteAtlasTextureSettings texture = atlas.GetTextureSettings();
            texture.generateMipMaps = false;
            texture.filterMode = FilterMode.Bilinear;
            atlas.SetTextureSettings(texture);

            UnityEngine.Object[] current = atlas.GetPackables();
            if (current.Length > 0) atlas.Remove(current);
            Texture2D sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
            atlas.Add(new UnityEngine.Object[] { sheet });
            EditorUtility.SetDirty(atlas);
        }

        private static void BuildPrefab(CouchDispatchUIStyle style)
        {
            GameObject root = new("CouchDispatchUI");
            DialogueShopUI presenter = root.AddComponent<DialogueShopUI>();

            GameObject eventSystemObject = new("DialogueEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystemObject.transform.SetParent(root.transform, false);
            EventSystem eventSystem = eventSystemObject.GetComponent<EventSystem>();
            eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            eventSystemObject.SetActive(false);

            GameObject canvasRoot = new("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(root.transform, false);
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject shade = Panel("BackdropShade", canvasRoot.transform, null,
                new Color(0.015f, 0.035f, 0.08f, 0.64f));
            Stretch(shade.GetComponent<RectTransform>());

            GameObject windowObject = Panel("CouchDispatchWindow", shade.transform, style.Get("WindowFull"), Color.white);
            RectTransform window = windowObject.GetComponent<RectTransform>();
            window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
            window.pivot = new Vector2(0.5f, 0.5f);
            window.sizeDelta = new Vector2(1220f, 730f);
            windowObject.GetComponent<Image>().preserveAspect = true;
            CanvasGroup windowGroup = windowObject.AddComponent<CanvasGroup>();

            TMP_Text greeting = Label("FundsAndNotice", window, "Team funds: $0", 24f, FontStyles.Bold,
                new Color32(255, 240, 194, 255));
            SetRect(greeting.rectTransform, new Vector2(0.52f, 1f), new Vector2(0.9f, 1f),
                new Vector2(0f, -177f), new Vector2(0f, 38f));
            greeting.alignment = TextAlignmentOptions.MidlineRight;

            Button deliveriesTab = InvisibleButton("DeliveriesTab", window);
            SetRect(deliveriesTab.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(126f, -142f), new Vector2(480f, 72f));
            Image deliveriesHighlight = deliveriesTab.GetComponent<Image>();
            Button shopTab = InvisibleButton("ShopTab", window);
            SetRect(shopTab.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(614f, -142f), new Vector2(480f, 72f));
            Image shopHighlight = shopTab.GetComponent<Image>();

            RectTransform filtersRoot = new GameObject("ShopCategoryFilters", typeof(RectTransform),
                typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            filtersRoot.SetParent(window, false);
            SetRect(filtersRoot, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(88f, -238f), new Vector2(-176f, 46f));
            HorizontalLayoutGroup filtersLayout = filtersRoot.GetComponent<HorizontalLayoutGroup>();
            filtersLayout.spacing = 12f;
            filtersLayout.childControlWidth = true;
            filtersLayout.childForceExpandWidth = true;
            filtersLayout.childControlHeight = true;
            filtersLayout.childForceExpandHeight = true;
            string[] filterNames = { "All", "Vans", "Weapons", "Upgrades", "Other" };
            CouchDispatchFilterButtonView[] filters = filterNames
                .Select(group => CreateFilter(filtersRoot, style, group)).ToArray();

            GameObject viewportObject = Panel("ScrollableContent", window, null, Color.clear);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(82f, 157f);
            viewport.offsetMax = new Vector2(-82f, -244f);
            viewportObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewportObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.viewport = viewport;

            RectTransform deliveryCardsRoot = CreateCardsRoot("DeliveryCards", viewport);
            CouchDispatchOptionCardView[] deliveryCards =
            {
                CreateCard(deliveryCardsRoot, style, "EasyDeliveryCard", true, style.Get("DeliveryEasy")),
                CreateCard(deliveryCardsRoot, style, "MediumDeliveryCard", true, style.Get("DeliveryMedium")),
                CreateCard(deliveryCardsRoot, style, "HardDeliveryCard", true, style.Get("DeliveryHard"))
            };
            PopulatePreview(deliveryCards[0], "Easy Delivery", "A nearby, mostly flat run in the early neighbourhood.", "FREE", "$100", new Color(0.24f, 0.76f, 0.34f), true, true, style.Get("DeliveryEasy"));
            PopulatePreview(deliveryCards[1], "Medium Delivery", "A longer route through rolling streets and hills.", "$100", "$200", new Color(1f, 0.56f, 0.12f), false, true, style.Get("DeliveryMedium"));
            PopulatePreview(deliveryCards[2], "Hard Delivery", "Haul the couch to a distant mansion.", "$250", "$350", new Color(0.9f, 0.2f, 0.18f), false, true, style.Get("DeliveryHard"));

            RectTransform shopCardsRoot = CreateCardsRoot("ShopCards", viewport);
            CouchDispatchOptionCardView shopTemplate = CreateCard(shopCardsRoot, style, "ShopCardTemplate", false,
                style.Get("VanIcon"));
            PopulatePreview(shopTemplate, "Shop Item", "Description supplied by the shop catalogue.", "$250", string.Empty,
                new Color(0.18f, 0.52f, 0.72f), true, false, style.Get("VanIcon"));
            shopTemplate.gameObject.SetActive(false);
            shopCardsRoot.gameObject.SetActive(false);
            scroll.content = deliveryCardsRoot;

            Button closeButton = InvisibleButton("CloseButton", window);
            SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-124f, -28f), new Vector2(98f, 88f));

            presenter.EditorConfigure(canvasRoot, window, windowGroup, greeting, closeButton,
                deliveriesTab, deliveriesHighlight, shopTab, shopHighlight, viewport, scroll,
                deliveryCardsRoot, deliveryCards, shopCardsRoot, shopTemplate, filtersRoot.gameObject,
                filters, style, eventSystemObject, eventSystem);
            canvasRoot.SetActive(false);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            style.SetPrefab(prefab);
            EditorUtility.SetDirty(style);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static RectTransform CreateCardsRoot(string name, Transform parent)
        {
            GameObject root = new(name, typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter), typeof(CanvasGroup));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 7, 7);
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static CouchDispatchOptionCardView CreateCard(Transform parent, CouchDispatchUIStyle style,
            string name, bool delivery, Sprite artworkSprite)
        {
            GameObject card = Panel(name, parent, style.Get("CardBlank"), Color.white);
            Image background = card.GetComponent<Image>();
            background.type = Image.Type.Sliced;
            card.AddComponent<LayoutElement>().preferredHeight = delivery ? 96f : 112f;
            CouchDispatchOptionCardView view = card.AddComponent<CouchDispatchOptionCardView>();

            GameObject artworkObject = Panel("Artwork", card.transform, artworkSprite, Color.white);
            Image artwork = artworkObject.GetComponent<Image>();
            artwork.preserveAspect = true;
            RectTransform artworkRect = artwork.rectTransform;
            artworkRect.anchorMin = new Vector2(0f, 0f);
            artworkRect.anchorMax = new Vector2(0f, 1f);
            artworkRect.pivot = new Vector2(0f, 0.5f);
            artworkRect.anchoredPosition = new Vector2(12f, 0f);
            artworkRect.sizeDelta = new Vector2(delivery ? 190f : 118f, delivery ? -10f : -12f);

            float textLeft = delivery ? 214f : 145f;
            TMP_Text title = Label("Title", card.transform, delivery ? "Delivery" : "Shop Item",
                delivery ? 23f : 25f, FontStyles.Bold, Color.white);
            SetRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.57f, 1f),
                new Vector2(textLeft, 0f), new Vector2(-textLeft, -4f));
            title.alignment = TextAlignmentOptions.BottomLeft;

            TMP_Text description = Label("Description", card.transform, "Description", 17f,
                FontStyles.Normal, new Color32(224, 236, 246, 255));
            SetRect(description.rectTransform, new Vector2(0f, 0f), new Vector2(0.57f, 0.55f),
                new Vector2(textLeft, 5f), new Vector2(-textLeft, -2f));
            description.alignment = TextAlignmentOptions.TopLeft;

            TMP_Text economy = Label("Economy", card.transform, "$100 PAYOUT", 21f, FontStyles.Bold,
                new Color32(255, 231, 120, 255));
            SetRect(economy.rectTransform, new Vector2(0.58f, 0f), new Vector2(0.78f, 1f),
                new Vector2(5f, 4f), new Vector2(-10f, -8f));
            economy.alignment = TextAlignmentOptions.MidlineLeft;

            Button action = TextButton("Action", card.transform, style.Get("CardBlank"),
                delivery ? "SELECT" : "BUY", new Color(1f, 0.48f, 0.06f, 1f));
            RectTransform actionRect = action.GetComponent<RectTransform>();
            actionRect.anchorMin = new Vector2(0.79f, 0.18f);
            actionRect.anchorMax = new Vector2(0.985f, 0.82f);
            actionRect.offsetMin = actionRect.offsetMax = Vector2.zero;
            TMP_Text actionLabel = action.GetComponentInChildren<TMP_Text>();

            TMP_Text reason = Label("UnavailableReason", card.transform, "Need more cash", 13f,
                FontStyles.Bold, new Color32(255, 190, 175, 255));
            SetRect(reason.rectTransform, new Vector2(0.58f, 0f), new Vector2(0.985f, 0f),
                new Vector2(5f, 2f), new Vector2(-10f, 18f));
            reason.alignment = TextAlignmentOptions.BottomRight;
            reason.gameObject.SetActive(false);

            view.EditorConfigure(background, artwork, title, description, economy, reason, action,
                action.GetComponent<Image>(), actionLabel);
            return view;
        }

        private static CouchDispatchFilterButtonView CreateFilter(Transform parent, CouchDispatchUIStyle style,
            string group)
        {
            Button button = TextButton($"Filter_{group}", parent, style.Get("CardBlank"), group.ToUpperInvariant(),
                new Color(0.08f, 0.3f, 0.55f, 0.95f));
            CouchDispatchFilterButtonView view = button.gameObject.AddComponent<CouchDispatchFilterButtonView>();
            view.EditorConfigure(group, button, button.GetComponent<Image>(), button.GetComponentInChildren<TMP_Text>());
            return view;
        }

        private static void PopulatePreview(CouchDispatchOptionCardView card, string title, string description,
            string price, string reward, Color colour, bool available, bool delivery, Sprite artwork)
        {
            card.Bind(new DialogueShopOption(title, description, price, reward, colour, available,
                available ? string.Empty : "Need more cash", null), delivery, artwork);
        }

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

        private static Button TextButton(string name, Transform parent, Sprite sprite, string label, Color colour)
        {
            GameObject root = Panel(name, parent, sprite, colour);
            Image image = root.GetComponent<Image>();
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            Button button = root.AddComponent<Button>();
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

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
