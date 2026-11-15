using System;
using System.IO;
using System.Linq;
using CouchGuys.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit one-shot migration. Never rebuilds assets on import, reload or entering Play mode.
    public static class MainMenuSceneBuilder
    {
        const string UiPath = "Assets/_Project/Prefabs/UI/MainMenuUI.prefab";
        const string RobotPath = "Assets/_Project/Prefabs/UI/MainMenuRobotVisual.prefab";
        static GameObject s_template;

        [MenuItem("Couch Guys/Main Menu/Install Depot Menu (Once)")]
        public static void BuildMainMenuScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(UiPath) != null) throw new InvalidOperationException("Already installed. Edit the saved scene and prefabs directly.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var roots = scene.GetRootGameObjects();
            var manager = roots.SelectMany(r => r.GetComponentsInChildren<MainMenuManager>(true)).Single();
            var canvas = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).First();
            var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single();
            camera.name = "Main Menu Depot Camera"; camera.fieldOfView = 58; camera.nearClipPlane = .05f; camera.farClipPlane = 180;
            camera.transform.SetPositionAndRotation(new Vector3(0, 2.4f, -7), Quaternion.Euler(-5, 0, 0));
            var data = new SerializedObject(manager);
            foreach (string field in new[] { "m_soloButton", "m_multiplayerButton", "m_settingsButton" })
            { var old = data.FindProperty(field).objectReferenceValue as Button; if (old != null) Object.DestroyImmediate(old.gameObject); }
            foreach (var root in roots.Where(r => r.name == "Decorative Neighbourhood")) Object.DestroyImmediate(root);
            foreach (string name in new[] { "Concept Background", "Left UI Contrast" })
            { var old = canvas.transform.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject); }
            var logo = (RectTransform)canvas.transform.Find("Couch Guys Logo");
            logo.anchoredPosition = new Vector2(68, -38); logo.sizeDelta = new Vector2(530, 285);
            var depot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/World/depot_02.prefab"), scene);
            depot.name = "Main Menu Depot";
            // Disable only this menu instance's gameplay behaviours; preserve its saved geometry and lights.
            foreach (var script in depot.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null && (script.GetType().Namespace?.StartsWith("CouchGuys") == true || script.GetType().Name == "NavMeshLink"))
                { script.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(script); }
            var anchor = new GameObject("Menu Player Anchor").transform; anchor.SetParent(depot.transform, false);
            anchor.localPosition = new Vector3(2.5f, -.1f, -.8f); anchor.localRotation = Quaternion.Euler(0, 195, 0); anchor.localScale = Vector3.one * 2.2f;
            CreateRobot(anchor);
            var dispatch = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/CouchDispatchUI.prefab");
            s_template = dispatch.GetComponentsInChildren<Button>(true).First(b => b.name == "Action").gameObject;
            var ui = Rect("Depot Main Menu", canvas.transform, Vector2.zero, Vector2.zero); Stretch(ui);
            MaskButton(Button(ui, "Host Game", 68, 390, 570, 142));
            MaskButton(Button(ui, "Join Game", 68, 555, 570, 142));
            MaskButton(Button(ui, "Customise", 68, 720, 570, 130));
            var settings = Button(ui, "Settings", -42, 34, 94, 94);
            var sr = (RectTransform)settings.transform; sr.anchorMin = sr.anchorMax = sr.pivot = Vector2.one;
            Object.DestroyImmediate(settings.GetComponentInChildren<TMP_Text>().gameObject);
            var cogRect = Rect("Settings Cog", settings.transform, new Vector2(12, -12), new Vector2(70, 70));
            var cog = cogRect.gameObject.AddComponent<RawImage>();
            cog.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/UI/button_settings.png");
            cog.uvRect = new Rect(.185f, .27f, .145f, .45f); cog.raycastTarget = false;
            var panel = ImageRect("Settings Panel", ui, new Color(.025f, .06f, .1f, .97f)); Stretch(panel.rectTransform);
            Label(panel.transform, "SETTINGS", 630, 240, 660, 90, 58);
            Label(panel.transform, "Master Volume", 630, 380, 660, 70, 36);
            Slider(panel.transform); Button(panel.transform, "Back", 680, 650, 560, 130); panel.gameObject.SetActive(false);
            CreateMultiplayer(ui);
            var prefab = PrefabUtility.SaveAsPrefabAsset(ui.gameObject, UiPath); Object.DestroyImmediate(ui.gameObject);
            var saved = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
            var buttons = saved.GetComponentsInChildren<Button>(true);
            Assign(manager, "m_soloButton", buttons.First(b => b.name == "Host Game"));
            Assign(manager, "m_multiplayerButton", buttons.First(b => b.name == "Join Game"));
            Assign(manager, "m_customiseButton", buttons.First(b => b.name == "Customise"));
            Assign(manager, "m_settingsButton", buttons.First(b => b.name == "Settings"));
            Assign(manager, "m_settingsBackButton", buttons.First(b => b.transform.parent.name == "Settings Panel" && b.name == "Back"));
            Assign(manager, "m_settingsPanel", saved.transform.Find("Settings Panel").gameObject);
            Assign(manager, "m_volumeSlider", saved.GetComponentInChildren<Slider>(true));
            Assign(manager, "m_multiplayerMenu", saved.GetComponentInChildren<MultiplayerMenuController>(true));
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).Single().firstSelectedGameObject = buttons.First(b => b.name == "Host Game").gameObject;
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }

        static void CreateRobot(Transform anchor)
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var source = player.GetComponentInChildren<Animator>(true);
            // Instantiate the robot model alone, excluding the player's weapons, input and network components.
            var visual = Object.Instantiate(PrefabUtility.GetCorrespondingObjectFromSource(source.gameObject)); visual.name = "Menu Robot Visual";
            var materials = source.GetComponentsInChildren<Renderer>(true).ToDictionary(r => AnimationUtility.CalculateTransformPath(r.transform, source.transform), r => r.sharedMaterials);
            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
                if (materials.TryGetValue(AnimationUtility.CalculateTransformPath(r.transform, visual.transform), out var values)) r.sharedMaterials = values;
            foreach (var script in visual.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Weapon_")))
                Object.DestroyImmediate(renderer.gameObject);
            var face = visual.AddComponent<CouchGuys.Player.RobotFaceExpressionController>();
            Assign(face, "m_faceMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Resources/RobotDigitalFace.mat"));
            var idle = source.runtimeAnimatorController.animationClips.First(c => c.name == "CouchGuy_Rig|Idle");
            var controller = AnimatorController.CreateAnimatorControllerAtPath("Assets/_Project/Animations/MainMenuRobotIdle.controller");
            var state = controller.layers[0].stateMachine.AddState("Idle"); state.motion = idle; controller.layers[0].stateMachine.defaultState = state;
            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            visual.transform.localPosition = source.transform.localPosition; visual.transform.localRotation = source.transform.localRotation; visual.transform.localScale = source.transform.localScale;
            var prefab = PrefabUtility.SaveAsPrefabAsset(visual, RobotPath); Object.DestroyImmediate(visual);
            PrefabUtility.InstantiatePrefab(prefab, anchor);
        }

        static void CreateMultiplayer(Transform parent)
        {
            var root = Rect("Multiplayer Navigation", parent, Vector2.zero, Vector2.zero); Stretch(root);
            var c = root.gameObject.AddComponent<MultiplayerMenuController>();
            var shade = ImageRect("Multiplayer Panel", root, new Color(.025f, .06f, .1f, .97f)); Stretch(shade.rectTransform); Assign(c, "m_root", shade.gameObject);
            Label(shade.transform, "MULTIPLAYER", 530, 100, 860, 90, 58);
            Assign(c, "m_status", LegacyLabel(shade.transform, "Starting Steam...", 530, 960, 860, 80, 26));
            var home = Page("Multiplayer Home"); var host = Page("Host Options"); var browser = Page("Lobby Browser"); var lobby = Page("Lobby");
            Assign(c, "m_homePage", home.gameObject); Assign(c, "m_hostPage", host.gameObject); Assign(c, "m_browserPage", browser.gameObject); Assign(c, "m_lobbyPage", lobby.gameObject);
            Action(home, "Host Game", 300, c.ShowHost); Action(home, "Join Game", 460, c.OpenBrowser); Action(home, "Back", 660, c.Close);
            var tr = Rect("Public Lobby", host, new Vector2(660, -260), new Vector2(600, 80)); var toggle = tr.gameObject.AddComponent<Toggle>();
            var box = ImageRect("Box", tr, Color.white); Place(box.rectTransform, Vector2.zero, new Vector2(60, 60));
            var check = ImageRect("Check", box.transform, Color.cyan); Stretch(check.rectTransform, 10);
            toggle.targetGraphic = box; toggle.graphic = check; toggle.isOn = true; Label(tr, "Public Lobby", 90, 0, 420, 70, 36); Assign(c, "m_publicToggle", toggle);
            var limit = Action(host, "Maximum Players: 4", 390, c.CyclePlayerLimit); LegacyButton(limit);
            Assign(c, "m_playerLimitButton", limit); Assign(c, "m_playerLimitLabel", limit.GetComponentInChildren<Text>());
            Action(host, "Create Lobby", 550, c.Host); Action(host, "Back", 730, c.Close);
            Action(browser, "Refresh", 700, c.Refresh); Action(browser, "Back", 840, c.Close);
            var viewport = Rect("Lobby Viewport", browser, new Vector2(530, -240), new Vector2(860, 440)); viewport.gameObject.AddComponent<RectMask2D>();
            var rows = Rect("Lobby Rows", viewport, Vector2.zero, new Vector2(860, 440));
            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 12; layout.childControlHeight = false; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            rows.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = rows; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
            Assign(c, "m_browserRows", rows); Assign(c, "m_emptyBrowserLabel", LegacyLabel(browser, "No public lobbies found.", 530, 340, 860, 90, 30));
            var row = Button(root, "Lobby Row Template", 0, 0, 860, 80); LegacyButton(row);
            var rowAsset = PrefabUtility.SaveAsPrefabAsset(row.gameObject, "Assets/_Project/Prefabs/UI/MainMenuLobbyRow.prefab"); Assign(c, "m_browserRowPrefab", rowAsset.GetComponent<Button>()); Object.DestroyImmediate(row.gameObject);
            Assign(c, "m_lobbyPlayers", LegacyLabel(lobby, "PLAYERS", 580, 230, 760, 310, 36));
            Action(lobby, "Invite Friends", 560, c.InviteFriends); Assign(c, "m_startButton", Action(lobby, "Start Game", 700, c.StartGame)); Action(lobby, "Leave Lobby", 840, c.LeaveLobby);
            foreach (var page in new[] { host, browser, lobby }) page.gameObject.SetActive(false); shade.gameObject.SetActive(false);
            RectTransform Page(string name) { var p = Rect(name, shade.transform, Vector2.zero, Vector2.zero); Stretch(p); return p; }
        }

        static Button Action(Transform parent, string text, float y, UnityEngine.Events.UnityAction callback)
        { var b = Button(parent, text, 660, y, 600, 120); UnityEventTools.AddPersistentListener(b.onClick, callback); return b; }
        static Button Button(Transform parent, string text, float x, float y, float width, float height)
        {
            var copy = Object.Instantiate(s_template, parent, false); copy.name = text; copy.SetActive(true); Place((RectTransform)copy.transform, new Vector2(x, -y), new Vector2(width, height));
            var b = copy.GetComponent<Button>(); b.onClick = new Button.ButtonClickedEvent();
            var colors = b.colors; colors.normalColor = new Color(.85f, .94f, 1f); colors.highlightedColor = Color.white;
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.65f, .8f, .9f); colors.fadeDuration = .12f; b.colors = colors;
            var image = copy.GetComponent<Image>(); image.color = colors.normalColor;
            image.sprite = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/DispatcherDeliveryButton.prefab").GetComponent<Image>().sprite;
            image.type = Image.Type.Simple;
            var label = copy.GetComponentInChildren<TMP_Text>(); label.text = text; label.color = new Color(1, .96f, .84f); label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true; label.fontSizeMin = 26; label.fontSizeMax = 46; Stretch(label.rectTransform, 12); label.raycastTarget = false; return b;
        }
        static void LegacyButton(Button b)
        { var old = b.GetComponentInChildren<TMP_Text>(); string text = old.text; Object.DestroyImmediate(old.gameObject); Stretch(LegacyLabel(b.transform, text, 0, 0, 0, 0, 30).rectTransform, 8); }
        static void MaskButton(Button button)
        {
            // The existing artwork has an opaque black matte. Reuse the terminal's rounded stencil
            // surface to clip its corners without changing or generating any texture.
            var source = button.GetComponent<Image>(); source.enabled = false;
            var mask = Rect("Terminal Rounded Mask", button.transform, Vector2.zero, Vector2.zero); Stretch(mask); mask.SetAsFirstSibling();
            mask.gameObject.AddComponent<CanvasRenderer>();
            var graphic = mask.gameObject.AddComponent<TerminalScreenGraphic>(); graphic.raycastTarget = false;
            var data = new SerializedObject(graphic); data.FindProperty("m_cornerRadius").floatValue = 34; data.ApplyModifiedPropertiesWithoutUndo();
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var artwork = ImageRect("Dispatcher Button Artwork", mask, source.color); Stretch(artwork.rectTransform);
            artwork.sprite = source.sprite; button.targetGraphic = artwork;
        }
        static TMP_Text Label(Transform parent, string text, float x, float y, float w, float h, float size)
        {
            var label = Rect(text, parent, new Vector2(x, -y), new Vector2(w, h)).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/Barlow_Semi_Condensed/BarlowSemiCondensed-BoldItalic SDF.asset");
            label.text = text; label.fontSize = size; label.color = new Color(1, .96f, .84f); label.raycastTarget = false; label.alignment = TextAlignmentOptions.Center; return label;
        }
        static Text LegacyLabel(Transform parent, string text, float x, float y, float w, float h, int size)
        { var t = Rect(text, parent, new Vector2(x, -y), new Vector2(w, h)).gameObject.AddComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.text = text; t.fontSize = size; t.color = new Color(1, .96f, .84f); t.raycastTarget = false; t.alignment = TextAnchor.MiddleCenter; return t; }
        static void Slider(Transform parent)
        {
            var rect = Rect("Master Volume", parent, new Vector2(660, -500), new Vector2(600, 45)); var slider = rect.gameObject.AddComponent<Slider>();
            var track = ImageRect("Track", rect, new Color(.14f, .28f, .37f)); Stretch(track.rectTransform, 8);
            var fill = ImageRect("Fill", track.transform, new Color(.25f, .8f, 1)); Stretch(fill.rectTransform);
            var area = Rect("Handle Area", rect, Vector2.zero, Vector2.zero); Stretch(area, 8);
            var handle = ImageRect("Handle", area, Color.white); handle.rectTransform.sizeDelta = new Vector2(34, 55);
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; slider.value = 1;
        }
        static Image ImageRect(string name, Transform parent, Color color)
        { var r = Rect(name, parent, Vector2.zero, Vector2.zero); var i = r.gameObject.AddComponent<Image>(); i.color = color; return i; }
        static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); Place(r, position, size); return r; }
        static void Place(RectTransform r, Vector2 position, Vector2 size)
        { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = position; r.sizeDelta = size; r.localScale = Vector3.one; }
        static void Stretch(RectTransform r, float inset = 0)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f); r.offsetMin = Vector2.one * inset; r.offsetMax = Vector2.one * -inset; }
        static void Assign(Object target, string field, Object value)
        { var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
