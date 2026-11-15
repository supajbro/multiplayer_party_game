using System;
using System.IO;
using System.Linq;
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
    /// <summary>Explicit authoring only; never regenerates Inspector customizations.</summary>
    public static class WeaponTerminalAuthoring
    {
        public const string ModelPath = "Assets/_Project/Models/WeaponShop/weapon_shop_terminal.fbx";
        public const string PrefabPath = "Assets/_Project/Prefabs/UI/WeaponShopTerminal.prefab";
        public const string MenuPath = "Assets/_Project/Prefabs/UI/WeaponShopTerminalUI.prefab";
        public static void RunPlay() => WeaponTerminalPlayValidation.Run();

        [MenuItem("Couch Guys/Weapon Terminal/Fit Rounded Screen Borders")]
        public static void FitScreenBorders()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before saving prefab changes.");

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var mesh = model.GetComponentsInChildren<MeshFilter>().Single(filter => filter.name == "weapon_shop_terminal_screen").sharedMesh;
            var normal = mesh.normals[0].normalized;
            var up = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
            var right = Vector3.Cross(-normal, up).normalized;
            var points = mesh.vertices.Select(vertex => new Vector2(Vector3.Dot(vertex, right), Vector3.Dot(vertex, up))).ToArray();
            float left = points.Min(point => point.x);
            float rightEdge = points.Max(point => point.x);
            float top = points.Max(point => point.y);
            float bottom = points.Min(point => point.y);
            float topRight = points.Where(point => Mathf.Abs(point.y - top) < .00001f).Max(point => point.x);
            float cornerRadius = rightEdge - topRight;
            // Keep the flat canvas just within the glass rather than covering its bevel.
            const float inset = .0015f;
            float width = rightEdge - left - inset * 2f;
            float height = top - bottom - inset * 2f;

            var menu = PrefabUtility.LoadPrefabContents(MenuPath);
            try
            {
                FitCanvas(menu.GetComponent<RectTransform>(), width, height, cornerRadius - inset, inset);
                var menuData = new SerializedObject(menu.GetComponent<DispatcherMenuUI>());
                foreach (string field in new[] { "m_funds", "m_feedback" })
                {
                    var label = (TMP_Text)menuData.FindProperty(field).objectReferenceValue;
                    label.rectTransform.anchoredPosition = new Vector2(120, label.rectTransform.anchoredPosition.y);
                    label.rectTransform.sizeDelta = new Vector2(860, label.rectTransform.sizeDelta.y);
                }
                PrefabUtility.SaveAsPrefabAsset(menu, MenuPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(menu); }

            var terminal = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var data = new SerializedObject(terminal.GetComponent<DispatcherMenuController>());
                var idle = (GameObject)data.FindProperty("m_idleScreen").objectReferenceValue;
                FitCanvas(idle.GetComponent<RectTransform>(), width, height, cornerRadius - inset, inset);
                PrefabUtility.SaveAsPrefabAsset(terminal, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(terminal); }

            Preview();
            File.WriteAllText("Temp/WeaponTerminalScreenFit.txt", $"Saved rounded screen masks. Width={width:F6}, height={height:F6}, corner radius={cornerRadius:F6}, inset={inset:F6}\n");
        }

        private static void FitCanvas(RectTransform rect, float width, float height, float radius, float inset)
        {
            var graphic = rect.GetComponent<TerminalScreenGraphic>();
            var image = rect.GetComponent<Image>();
            Color backgroundColor = image != null ? image.color : graphic.color;
            if (image != null) Object.DestroyImmediate(image);
            if (graphic == null) graphic = rect.gameObject.AddComponent<TerminalScreenGraphic>();
            graphic.color = backgroundColor;
            float radiusInCanvasUnits = Mathf.Max(1f, radius * rect.rect.width / width);
            var data = new SerializedObject(graphic);
            data.FindProperty("m_cornerRadius").floatValue = radiusInCanvasUnits;
            data.ApplyModifiedPropertiesWithoutUndo();
            graphic.raycastTarget = false;
            graphic.SetAllDirty();
            var mask = rect.GetComponent<Mask>();
            if (mask == null) mask = rect.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            rect.localPosition = new Vector3(inset, -inset, rect.localPosition.z);
            rect.localScale = new Vector3(width / rect.rect.width, height / rect.rect.height, width / rect.rect.width);
        }
        [MenuItem("Couch Guys/Weapon Terminal/Create Missing Prefabs And Place In Depot 02")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath) == null) CreateMenu();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) CreateTerminal();
            var depot = PrefabUtility.LoadPrefabContents(Depot02PrefabBuilder.PrefabPath);
            try
            {
                if (depot.GetComponentInChildren<WeaponShopTerminal>(true) == null)
                {
                    var display = depot.GetComponentInChildren<WeaponAdvertisementDisplay>(true);
                    if (display == null) throw new Exception("Place a WeaponShopDisplay in depot_02 first.");
                    var screen = display.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "weapon_shop_display_screen");
                    Vector3 forward = Vector3.ProjectOnPlane(screen.transform.TransformDirection(Vector3.down), Vector3.up).normalized;
                    var terminal = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), depot.transform);
                    var position = display.transform.position + forward * 1.05f;
                    // The model's base is at ground zero. Keep the user's existing TV position/rotation.
                    position.y = depot.transform.position.y + .015f;
                    terminal.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
                    PrefabUtility.SaveAsPrefabAsset(depot, Depot02PrefabBuilder.PrefabPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(depot); }
            Validate();
        }

        private static void CreateMenu()
        {
            var root = PrefabUtility.LoadPrefabContents(DispatcherMenuAuthoring.MenuPath);
            try
            {
                root.name = "WeaponShopTerminalUI";
                var view = root.GetComponent<DispatcherMenuUI>();
                var data = new SerializedObject(view);
                var menuRect = root.GetComponent<RectTransform>();
                menuRect.sizeDelta = new Vector2(1100, 1100);
                menuRect.pivot = new Vector2(0, 1);
                root.transform.localScale = new Vector3(.655f / 1100f, .65166f / 1100f, .655f / 1100f);
                var panel = ((GameObject)data.FindProperty("m_shopPanel").objectReferenceValue).GetComponent<RectTransform>();
                panel.sizeDelta = new Vector2(1100, 970);
                var scroll = (ScrollRect)data.FindProperty("m_shopScroll").objectReferenceValue;
                scroll.viewport.sizeDelta = new Vector2(1100, 970);
                var funds = (TMP_Text)data.FindProperty("m_funds").objectReferenceValue;
                funds.rectTransform.anchoredPosition = new Vector2(12, -1002);
                var feedback = (TMP_Text)data.FindProperty("m_feedback").objectReferenceValue;
                feedback.rectTransform.anchoredPosition = new Vector2(12, -1042);
                feedback.text = "WEAPON SHOP  |  E / ESC to return";
                feedback.fontSize = 25;
                var background = root.AddComponent<Image>();
                background.color = new Color(.018f, .045f, .12f);
                background.raycastTarget = false;
                view.ShowCategory(1);
                var confirmation = (TMP_Text)data.FindProperty("m_confirmationText").objectReferenceValue;
                confirmation.rectTransform.anchoredPosition = new Vector2(50, -450);
                PrefabUtility.SaveAsPrefabAsset(root, MenuPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CreateTerminal()
        {
            var root = new GameObject("WeaponShopTerminal");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
                var screen = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "weapon_shop_terminal_screen");
                var mesh = screen.sharedMesh;
                Vector3 normal = mesh.normals[0].normalized;
                Vector3 up = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
                Quaternion rotation = Quaternion.LookRotation(-normal, up);
                Vector3 centre = screen.transform.TransformPoint(mesh.bounds.center + normal * .003f);
                Vector3 outward = screen.transform.TransformDirection(normal).normalized;
                Vector3 worldUp = screen.transform.TransformDirection(up).normalized;
                float width = mesh.bounds.size.x;
                float height = Vector3.Dot(mesh.bounds.size, new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z)));
                var anchor = Marker("ScreenCanvasAnchor", screen.transform, mesh.bounds.center + normal * .003f);
                anchor.localRotation = rotation;
                anchor.position += anchor.up * (height * .5f) - anchor.right * (width * .5f);
                var focus = Marker("CameraFocusPoint", root.transform, Vector3.zero);
                focus.SetPositionAndRotation(centre + outward * .94f, Quaternion.LookRotation(-outward, worldUp));
                var reach = Marker("InteractionPoint", root.transform, Vector3.zero);
                reach.position = new Vector3(centre.x, .25f, centre.z) + Vector3.ProjectOnPlane(outward, Vector3.up).normalized * .25f;
                var idle = IdleScreen(anchor, width / 1100f, height / 1100f);
                var controller = root.AddComponent<DispatcherMenuController>();
                var data = new SerializedObject(controller);
                data.FindProperty("m_menuPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<DispatcherMenuUI>(MenuPath);
                data.FindProperty("m_cameraFocusPoint").objectReferenceValue = focus;
                data.FindProperty("m_screenCanvasAnchor").objectReferenceValue = anchor;
                data.FindProperty("m_shopOnly").boolValue = true;
                data.FindProperty("m_hideLocalPlayerWhileOpen").boolValue = true;
                data.FindProperty("m_idleScreen").objectReferenceValue = idle;
                data.FindProperty("m_transitionSeconds").floatValue = .6f;
                data.FindProperty("m_focusFieldOfView").floatValue = 48f;
                foreach (string field in new[] { "m_screens", "m_deliveriesButtons", "m_shopButtons" }) data.FindProperty(field).arraySize = 0;
                data.ApplyModifiedPropertiesWithoutUndo();
                var marker = root.AddComponent<WeaponShopTerminal>();
                data = new SerializedObject(marker);
                data.FindProperty("m_interactionPoint").objectReferenceValue = reach;
                data.ApplyModifiedPropertiesWithoutUndo();
                var body = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "weapon_shop_terminal_body");
                var collider = body.gameObject.AddComponent<BoxCollider>();
                collider.center = body.sharedMesh.bounds.center;
                collider.size = body.sharedMesh.bounds.size;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Transform Marker(string name, Transform parent, Vector3 local)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false); marker.localPosition = local; return marker;
        }

        private static GameObject IdleScreen(Transform anchor, float scaleX, float scaleY)
        {
            var rect = new GameObject("IdleScreen", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
            rect.SetParent(anchor, false); rect.pivot = new Vector2(0, 1); rect.sizeDelta = new Vector2(1100, 1100);
            rect.localScale = new Vector3(scaleX, scaleY, scaleX);
            rect.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(.018f, .045f, .12f); background.raycastTarget = false;
            IdleLabel(rect, "Title", "WEAPON SHOP", -360, 85, Color.white);
            IdleLabel(rect, "Hint", "PRESS E TO BROWSE", -600, 46, Color.cyan);
            return rect.gameObject;
        }

        private static void IdleLabel(Transform parent, string name, string text, float y, float size, Color color)
        {
            var label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 1);
            label.rectTransform.anchoredPosition = new Vector2(550, y); label.rectTransform.sizeDelta = new Vector2(1050, 180);
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/Barlow_Semi_Condensed/BarlowSemiCondensed-BoldItalic SDF.asset");
            label.text = text; label.fontSize = size; label.alignment = TextAlignmentOptions.Center;
            label.color = color; label.raycastTarget = false;
        }

        [MenuItem("Couch Guys/Weapon Terminal/Validate Prefabs")]
        public static void Validate()
        {
            var terminal = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (terminal == null || terminal.GetComponent<WeaponShopTerminal>() == null) throw new Exception("Terminal prefab missing");
            var data = new SerializedObject(terminal.GetComponent<DispatcherMenuController>());
            foreach (string field in new[] { "m_menuPrefab", "m_cameraFocusPoint", "m_screenCanvasAnchor", "m_idleScreen" })
                if (data.FindProperty(field).objectReferenceValue == null) throw new Exception("Missing " + field);
            if (!data.FindProperty("m_shopOnly").boolValue) throw new Exception("Terminal must open shop only");
            var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
            foreach (var item in catalogue.Items.Where(i => i != null))
                if (CouchGuys.Gameplay.Delivery.DeliveryManager.IsShopItemAllowed(item, true) ==
                    CouchGuys.Gameplay.Delivery.DeliveryManager.IsShopItemAllowed(item, false)) throw new Exception("Shop filtering failed");
            var depot = AssetDatabase.LoadAssetAtPath<GameObject>(Depot02PrefabBuilder.PrefabPath);
            if (depot.GetComponentsInChildren<WeaponShopTerminal>(true).Length != 1) throw new Exception("Expected exactly one depot terminal");
            var placedTerminal = depot.GetComponentInChildren<WeaponShopTerminal>(true);
            var display = depot.GetComponentInChildren<WeaponAdvertisementDisplay>(true);
            var displayScreen = display.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "weapon_shop_display_screen");
            var forward = Vector3.ProjectOnPlane(displayScreen.transform.TransformDirection(Vector3.down), Vector3.up).normalized;
            if (Vector3.Dot(placedTerminal.transform.position - display.transform.position, forward) < .5f)
                throw new Exception("Terminal must stand in front of the existing display");
            File.WriteAllText("Temp/WeaponTerminalValidation.txt", "PASS: saved editable terminal/menu prefabs, wired camera focus and shop-only UI, exclusive weapon filtering, exactly one terminal in depot_02.\n");
        }

        public static void Preview() => PreviewTerminal(PrefabPath, "WeaponTerminal");

        public static void PreviewTerminal(string prefabPath, string outputName)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
                var data = new SerializedObject(instance.GetComponent<DispatcherMenuController>());
                var focus = (Transform)data.FindProperty("m_cameraFocusPoint").objectReferenceValue;
                var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene;
                camera.transform.SetPositionAndRotation(focus.position, focus.rotation);
                camera.fieldOfView = 48f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray * .15f;
                var target = new RenderTexture(1200, 900, 24); camera.targetTexture = target;
                SaveRender(camera, "Temp/" + outputName + "Idle.png");
                var anchor = (Transform)data.FindProperty("m_screenCanvasAnchor").objectReferenceValue;
                ((GameObject)data.FindProperty("m_idleScreen").objectReferenceValue).SetActive(false);
                var ui = Object.Instantiate((DispatcherMenuUI)data.FindProperty("m_menuPrefab").objectReferenceValue, anchor, false);
                ui.ConfigureCamera(camera);
                var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
                var category = instance.GetComponent<WeaponShopTerminal>().Category;
                var options = catalogue.Items.Where(i => i != null && i.Category == category)
                    .Select(i => new DialogueShopOption(i.DisplayName, i.Description, "$" + i.Price.ToString("N0"), "", Color.cyan, true, "", () => { }, i.Icon,
                        category == ShopItemCategory.Vehicle ? "Vehicles" : "Weapons")).ToArray();
                ui.Bind(new[] { new DialogueShopCategory("Deliveries", Array.Empty<DialogueShopOption>()), new DialogueShopCategory("Shop", options) }, 10000, category.ToString().ToUpperInvariant() + " SHOP | E / ESC to return");
                ui.ShowCategory(1);
                SaveRender(camera, "Temp/" + outputName + "Menu.png");
                camera.targetTexture = null; Object.DestroyImmediate(target);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void SaveRender(Camera camera, string path)
        {
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = camera.targetTexture;
            var texture = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture); RenderTexture.active = previous;
        }
    }
}
