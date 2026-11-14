using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using CouchGuys.Gameplay.Shop;
using CouchGuys.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    public static class WeaponAdvertisementAuthoring
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/UI/WeaponShopDisplay.prefab";
        public const string ModelPath = "Assets/_Project/Models/WeaponShop/weapon_shop_display.fbx";
        private const string CataloguePath = "Assets/_Project/Resources/Shop/DeliveryManagerShop.asset";
        private const string FontFolder = "Assets/_Project/Art/Fonts/Barlow_Semi_Condensed/";

        [MenuItem("Couch Guys/Weapon Advertising/Create Missing TV Prefab")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
                throw new InvalidOperationException("Prefab already exists; edit it in the Inspector. It will not be overwritten.");
            var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(CataloguePath);
            var first = catalogue.Items.First(i => i != null && i.Category == ShopItemCategory.Weapon && i.PurchaseBehaviour != null);
            var root = new GameObject("WeaponShopDisplay");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
                var screen = model.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "weapon_shop_display_screen");
                var bounds = screen.sharedMesh.bounds;
                var canvasRect = Rect("AdvertisementCanvas", screen.transform, 0, 0, 1280, 720);
                // The FBX screen lies in local X/Z, faces -Y, and has +Z as its top edge.
                canvasRect.localPosition = bounds.center + Vector3.down * 0.003f;
                canvasRect.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                canvasRect.localScale = new Vector3(bounds.size.x / 1280f, bounds.size.z / 720f, 0.002f);
                var canvas = canvasRect.gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 0;
                canvasRect.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
                var group = canvasRect.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                Fill(canvasRect, new Color(0.018f, 0.045f, 0.12f));
                var viewport = Rect("ClippedAdvertisements", canvasRect, 0, 0, 1280, 720);
                viewport.gameObject.AddComponent<RectMask2D>();
                var firstPanel = Panel("AdvertisementA", viewport, first);
                var secondPanel = Panel("AdvertisementB", viewport, first);
                secondPanel.gameObject.SetActive(false);
                Fill(Rect("TopCyanLine", canvasRect, 0, 345, 1200, 4), new Color(0f, 0.85f, 1f));
                Fill(Rect("BottomCyanLine", canvasRect, 0, -345, 1200, 4), new Color(0f, 0.85f, 1f));
                var display = root.AddComponent<WeaponAdvertisementDisplay>();
                var data = new SerializedObject(display);
                data.FindProperty("m_catalogue").objectReferenceValue = catalogue;
                data.FindProperty("m_viewport").objectReferenceValue = viewport;
                WirePanel(data.FindProperty("m_firstPanel"), firstPanel);
                WirePanel(data.FindProperty("m_secondPanel"), secondPanel);
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            Validate();
        }

        private static RectTransform Panel(string name, Transform parent, ShopItemDefinition item)
        {
            var panel = Rect(name, parent, 0, 0, 1280, 720);
            var cyan = new Color(0f, 0.85f, 1f);
            Label("Featured", panel, "FEATURED WEAPON", 32, 0, 285, 1120, 55, cyan);
            Label("WeaponName", panel, item.DisplayName, 76, 0, 197, 1140, 110, Color.white, true);
            var placeholder = Rect("ImagePlaceholder", panel, 0, -2, 660, 245);
            Fill(placeholder, new Color(0.035f, 0.12f, 0.22f));
            Label("PlaceholderText", placeholder, "[ WEAPON IMAGE ]", 40, 0, 0, 620, 180, cyan);
            Label("WeaponPrice", panel, "$" + item.Price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), 86, 0, -221, 1120, 120, new Color(1f, 0.83f, 0.12f), true);
            return panel;
        }

        private static void WirePanel(SerializedProperty property, RectTransform panel)
        {
            property.FindPropertyRelative("m_rect").objectReferenceValue = panel;
            property.FindPropertyRelative("m_name").objectReferenceValue = panel.Find("WeaponName").GetComponent<TMP_Text>();
            property.FindPropertyRelative("m_price").objectReferenceValue = panel.Find("WeaponPrice").GetComponent<TMP_Text>();
        }

        private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }

        private static void Fill(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void Label(string name, Transform parent, string text, float size, float x, float y, float w, float h, Color color, bool bold = false)
        {
            var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontFolder + (bold ? "BarlowSemiCondensed-BoldItalic" : "BarlowSemiCondensed-Regular") + " SDF.asset");
            label.text = text;
            label.fontSize = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = 26;
            label.fontSizeMax = size;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = false;
            label.color = color;
            label.raycastTarget = false;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        [MenuItem("Couch Guys/Weapon Advertising/Validate TV Prefab")]
        public static void Validate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(prefab != null, "Saved prefab missing");
            var scene = EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var report = new StringBuilder();
            try
            {
                var view = instance.GetComponent<WeaponAdvertisementDisplay>();
                var data = new SerializedObject(view);
                var catalogue = (ShopCatalogue)data.FindProperty("m_catalogue").objectReferenceValue;
                var weapons = catalogue.Items.Where(i => i != null && i.Category == ShopItemCategory.Weapon && i.PurchaseBehaviour != null).Distinct().ToArray();
                Require(weapons.Length > 1, "Expected multiple catalogue weapons");
                Require(instance.GetComponentInChildren<Canvas>().renderMode == RenderMode.WorldSpace, "Canvas must be world space");
                Require(instance.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0 && instance.GetComponentsInChildren<Selectable>(true).Length == 0, "Advertising must have no interaction");
                Require(instance.GetComponentsInChildren<MonoBehaviour>(true).All(c => c == null || !c.GetType().Namespace.StartsWith("FishNet")), "Unexpected networking component");
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var start = typeof(WeaponAdvertisementDisplay).GetMethod("OnEnable", flags);
                var advance = typeof(WeaponAdvertisementDisplay).GetMethod("Advance", flags);
                start.Invoke(view, null);
                float interval = data.FindProperty("m_scrollInterval").floatValue;
                float duration = data.FindProperty("m_transitionDuration").floatValue;
                var a = (RectTransform)data.FindProperty("m_firstPanel").FindPropertyRelative("m_rect").objectReferenceValue;
                var b = (RectTransform)data.FindProperty("m_secondPanel").FindPropertyRelative("m_rect").objectReferenceValue;
                Action<int> check = index =>
                {
                    var visible = new[] { a, b }.Single(p => p.gameObject.activeSelf);
                    Require(Mathf.Abs(visible.anchoredPosition.x) < 0.01f, "Panel did not settle exactly");
                    Require(visible.Find("WeaponName").GetComponent<TMP_Text>().text == weapons[index].DisplayName, "Incorrect weapon name");
                    Require(visible.Find("WeaponPrice").GetComponent<TMP_Text>().text == "$" + weapons[index].Price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Incorrect catalogue price");
                };
                check(0);
                for (int i = 1; i <= weapons.Length * 2; i++)
                {
                    advance.Invoke(view, new object[] { i == 1 ? interval : interval - duration });
                    advance.Invoke(view, new object[] { duration * 0.5f });
                    Require(a.gameObject.activeSelf && b.gameObject.activeSelf, "Both panels must be visible during transition");
                    Require(Mathf.Abs(Mathf.Abs(a.anchoredPosition.x - b.anchoredPosition.x) - 1280f) < .1f, "Slide panels have gap/overlap");
                    advance.Invoke(view, new object[] { duration * 0.5f });
                    check(i % weapons.Length);
                }
                report.AppendLine("PASS: two complete catalogue loops; correct names/prices, smooth midpoint and gap-free wrap.");
                start.Invoke(view, null);
                check(0);
                report.AppendLine("PASS: re-enable resets panels; world-space canvas; no interactive or networking components.");
                report.AppendLine("Catalogue weapons: " + string.Join(", ", weapons.Select(w => w.DisplayName + " $" + w.Price)));
                ValidateCatalogueChanges(view, catalogue, weapons[0], start, advance);
                report.AppendLine("PASS: new catalogue weapon and changed price appear automatically; empty/single-weapon catalogues and zero-duration transition are safe.");
                RenderPreview(instance, scene);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            File.WriteAllText("Temp/WeaponAdvertisementValidation.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        private static void ValidateCatalogueChanges(WeaponAdvertisementDisplay view, ShopCatalogue source,
            ShopItemDefinition original, MethodInfo start, MethodInfo advance)
        {
            var catalogue = Object.Instantiate(source);
            var added = Object.Instantiate(original);
            try
            {
                var newItem = new SerializedObject(added);
                newItem.FindProperty("m_displayName").stringValue = "Validation Added Weapon";
                newItem.FindProperty("m_price").intValue = 12345;
                newItem.ApplyModifiedPropertiesWithoutUndo();
                var catalogueData = new SerializedObject(catalogue);
                var items = catalogueData.FindProperty("m_items");
                items.arraySize = 0;
                catalogueData.ApplyModifiedPropertiesWithoutUndo();
                var data = new SerializedObject(view);
                data.FindProperty("m_catalogue").objectReferenceValue = catalogue;
                data.ApplyModifiedPropertiesWithoutUndo();
                var a = (RectTransform)data.FindProperty("m_firstPanel").FindPropertyRelative("m_rect").objectReferenceValue;
                var b = (RectTransform)data.FindProperty("m_secondPanel").FindPropertyRelative("m_rect").objectReferenceValue;
                start.Invoke(view, null);
                advance.Invoke(view, new object[] { 5f });
                Require(!a.gameObject.activeSelf && !b.gameObject.activeSelf, "Empty catalogue must hide both panels");
                items.arraySize = 1;
                items.GetArrayElementAtIndex(0).objectReferenceValue = original;
                catalogueData.ApplyModifiedPropertiesWithoutUndo();
                start.Invoke(view, null);
                advance.Invoke(view, new object[] { 5f });
                Require(a.gameObject.activeSelf && !b.gameObject.activeSelf && a.anchoredPosition.x == 0f, "One weapon must remain stationary");
                // Append after initialization: no rebuild and no copied definitions on the display.
                items.arraySize = 2;
                items.GetArrayElementAtIndex(1).objectReferenceValue = added;
                catalogueData.ApplyModifiedPropertiesWithoutUndo();
                advance.Invoke(view, new object[] { 5f });
                advance.Invoke(view, new object[] { .65f });
                Require(b.gameObject.activeSelf && b.Find("WeaponName").GetComponent<TMP_Text>().text == added.DisplayName,
                    "Newly appended catalogue item was not discovered");
                Require(b.Find("WeaponPrice").GetComponent<TMP_Text>().text == "$12,345", "New price did not come from catalogue");
                newItem.FindProperty("m_price").intValue = 54321;
                newItem.ApplyModifiedPropertiesWithoutUndo();
                data.Update();
                data.FindProperty("m_transitionDuration").floatValue = 0f;
                data.ApplyModifiedPropertiesWithoutUndo();
                start.Invoke(view, null);
                advance.Invoke(view, new object[] { 5f });
                Require(b.gameObject.activeSelf && !a.gameObject.activeSelf && b.anchoredPosition.x == 0f, "Instant transition failed");
                Require(b.Find("WeaponPrice").GetComponent<TMP_Text>().text == "$54,321", "Changed catalogue price was not reflected");
            }
            finally
            {
                var data = new SerializedObject(view);
                data.FindProperty("m_catalogue").objectReferenceValue = source;
                data.FindProperty("m_transitionDuration").floatValue = .65f;
                data.ApplyModifiedPropertiesWithoutUndo();
                start.Invoke(view, null);
                Object.DestroyImmediate(added);
                Object.DestroyImmediate(catalogue);
            }
        }

        private static void RenderPreview(GameObject instance, UnityEngine.SceneManagement.Scene scene)
        {
            var screen = instance.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "weapon_shop_display_screen");
            var canvas = instance.GetComponentInChildren<Canvas>().transform;
            var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            var outward = screen.transform.TransformDirection(Vector3.down);
            camera.transform.position = canvas.position + outward * 6f;
            camera.transform.rotation = Quaternion.LookRotation(-outward, canvas.up);
            camera.orthographic = true;
            camera.orthographicSize = 1.3f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .07f, .1f);
            camera.targetTexture = new RenderTexture(1400, 900, 24);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            var texture = new Texture2D(1400, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1400, 900), 0, 0);
            texture.Apply();
            File.WriteAllBytes("Temp/WeaponAdvertisementPreview.png", texture.EncodeToPNG());
            RenderTexture.active = previous;
            var target = camera.targetTexture;
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(target);
        }

    }
}
