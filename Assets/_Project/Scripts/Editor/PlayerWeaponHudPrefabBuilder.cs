using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    public sealed class PlayerWeaponHudPrefabBuilder
    {
        private static readonly Color Charcoal = new(0.055f, 0.07f, 0.09f, 0.94f);
        private static readonly Color CharcoalSoft = new(0.08f, 0.1f, 0.13f, 0.88f);
        private static readonly Color Gold = new(1f, 0.66f, 0.08f, 1f);
        private static readonly Color Inactive = new(0.36f, 0.4f, 0.45f, 1f);
        private static readonly Color Green = new(0.25f, 0.92f, 0.35f, 1f);
        private static readonly Color Yellow = new(1f, 0.7f, 0.08f, 1f);
        private static readonly Color Red = new(1f, 0.2f, 0.15f, 1f);

        private InteractionPromptView m_interactionPrompt;
        private Text m_healthText;
        private Text m_downedText;
        private Text m_deliveryTitle;
        private Text m_deliveryDetail;
        private Text m_deliveryDistance;
        private Text m_currencyText;
        private Text m_timerText;
        private Text m_failureText;
        private Text m_ammoText;
        private PlayerStatusBar m_healthBar;
        private PlayerStatusBar m_staminaBar;
        private Image m_timerFill;
        private GameObject m_timerPanel;
        private Image[] m_slotBackgrounds;
        private Outline[] m_slotOutlines;
        private GameObject m_ammoPanel;

        private RectTransform m_minimapRoot;
        private GameObject m_canvasRoot;
        private Sprite m_roundedSprite;
        private Sprite m_circleSprite;
        private PlayerStatusBar m_statusPrefab;
        private const string UiRoot = "Assets/_Project/Prefabs/UI/";
        private const string ArtRoot = "Assets/_Project/Art/UI/";
        private const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";

        [InitializeOnLoadMethod]
        private static void InstallMissing()
        {
            EditorApplication.update -= InstallOnUpdate;
            EditorApplication.update += InstallOnUpdate;
        }

        private static void InstallOnUpdate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= InstallOnUpdate;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(UiRoot + "PlayerWeaponHud.prefab") == null) Build();
            else
            {
                GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
                PlayerWeaponHud hud = player != null ? player.GetComponent<PlayerWeaponHud>() : null;
                if (hud != null && new SerializedObject(hud).FindProperty("m_hudPrefab").objectReferenceValue == null)
                    AssignToPlayer();
            }
        }

        [MenuItem("Couch Guys/UI/Rebuild Player HUD")]
        public static void Build()
        {
            new PlayerWeaponHudPrefabBuilder().BuildAssets();
        }

        private void BuildAssets()
        {
            BuildSprites();
            m_statusPrefab = BuildStatusPrefab();
            BuildHud();
            try
            {
                PlayerWeaponHudView view = m_canvasRoot.AddComponent<PlayerWeaponHudView>();
                SerializedObject serialized = new(view);
                foreach (var field in GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    SerializedProperty property = serialized.FindProperty(field.Name);
                    if (property == null) continue;
                    if (field.GetValue(this) is Object value) property.objectReferenceValue = value;
                    else if (field.GetValue(this) is Object[] values)
                    {
                        property.arraySize = values.Length;
                        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
                    }
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PlayerWeaponHudView saved = PrefabUtility.SaveAsPrefabAsset(m_canvasRoot, UiRoot + "PlayerWeaponHud.prefab").GetComponent<PlayerWeaponHudView>();
                ConfigurePlayer(saved);
                AssetDatabase.SaveAssets();
                Verify();
            }
            finally { Object.DestroyImmediate(m_canvasRoot); }
        }

        private static Sprite LoadShape(string name, int size, float radius)
        {
            string path = ArtRoot + name + ".asset";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            sprite = MakeRoundedSprite(size, radius);
            sprite.name = name;
            AssetDatabase.CreateAsset(sprite, path);
            AssetDatabase.AddObjectToAsset(sprite.texture, sprite);
            return sprite;
        }

        private static Sprite ImportSprite(string name, bool holder)
        {
            string path = ArtRoot + name + ".png";
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spriteBorder = holder ? new Vector4(525f, 0f, 180f, 0f) : Vector4.zero;
            importer.SaveAndReimport();
            
            string spritePath = ArtRoot + name + "_Cropped.asset";
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (existing != null) return existing;
            // Trim transparent margins without altering the imported artwork.
            Texture2D readable = new Texture2D(2, 2);
            readable.LoadImage(File.ReadAllBytes(path));
            Color32[] pixels = readable.GetPixels32();
            int minX = readable.width, minY = readable.height, maxX = 0, maxY = 0;
            for (int y = 0; y < readable.height; y++)
            for (int x = 0; x < readable.width; x++)
            {
                if (pixels[y * readable.width + x].a < 8) continue;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            Object.DestroyImmediate(readable);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Sprite cropped = Sprite.Create(texture, new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                holder ? new Vector4(500f, 0f, 180f, 0f) : Vector4.zero);
            cropped.name = name;
            AssetDatabase.CreateAsset(cropped, spritePath);
            Debug.Log(name + " trimmed rect " + cropped.rect);
            return cropped;
        }

        private PlayerStatusBar BuildStatusPrefab()
        {
            Sprite holder = ImportSprite("UI_StatusBar_Background", true);
            Sprite fill = ImportSprite("UI_StatusBar_Fill", false);
            GameObject root = new("PlayerStatusBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            try
            {
                RectTransform rect = root.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(455f, 122f);
                Image background = root.GetComponent<Image>();
                background.sprite = holder;
                background.type = Image.Type.Sliced;
                background.pixelsPerUnitMultiplier = 3.7f;
                background.raycastTarget = false;
                RectTransform fillRect = Rect("Fill", rect);
                SetRect(fillRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                fillRect.offsetMin = new Vector2(148f, 42f);
                fillRect.offsetMax = new Vector2(-23f, -42f);
                Image fillImage = fillRect.gameObject.AddComponent<Image>();
                fillImage.sprite = fill;
                fillImage.type = Image.Type.Filled;
                fillImage.fillMethod = Image.FillMethod.Horizontal;
                fillImage.fillOrigin = 0;
                fillImage.raycastTarget = false;
                RectTransform badge = Rect("BadgeMask", rect);
                SetRect(badge, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(82f, 82f), new Vector2(0f, 0.5f));
                Image mask = badge.gameObject.AddComponent<Image>();
                mask.sprite = m_circleSprite;
                mask.raycastTarget = false;
                badge.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                RectTransform portrait = Rect("Portrait", badge);
                Stretch(portrait, 0f);
                RawImage portraitImage = portrait.gameObject.AddComponent<RawImage>();
                portraitImage.raycastTarget = false;
                portraitImage.texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(ArtRoot + "LocalPlayerPortrait.renderTexture");
                RectTransform icon = Rect("LightningIcon", badge);
                Stretch(icon, 5f);
                RawImage iconImage = icon.gameObject.AddComponent<RawImage>();
                iconImage.raycastTarget = false;
                iconImage.enabled = false;
                PlayerStatusBar status = root.AddComponent<PlayerStatusBar>();
                SerializedObject s = new(status);
                s.FindProperty("m_fill").objectReferenceValue = fillImage;
                s.FindProperty("m_portrait").objectReferenceValue = portraitImage;
                s.FindProperty("m_lightningIcon").objectReferenceValue = iconImage;
                s.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, UiRoot + "PlayerStatusBar.prefab").GetComponent<PlayerStatusBar>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private PlayerStatusBar CreateStatus(RectTransform panel, bool stamina)
        {
            PlayerStatusBar instance = (PlayerStatusBar)PrefabUtility.InstantiatePrefab(m_statusPrefab, panel);
            Stretch((RectTransform)instance.transform, 0f);
            Image holder = instance.GetComponent<Image>();
            holder.pixelsPerUnitMultiplier = stamina ? 10.74f : 3.7f;
            RectTransform fill = (RectTransform)instance.transform.Find("Fill");
            fill.offsetMin = stamina ? new Vector2(52f, 14f) : new Vector2(148f, 42f);
            fill.offsetMax = stamina ? new Vector2(-10f, -14f) : new Vector2(-23f, -42f);
            RectTransform badge = (RectTransform)instance.transform.Find("BadgeMask");
            badge.anchoredPosition = new Vector2(stamina ? 10f : 30f, 0f);
            badge.sizeDelta = Vector2.one * (stamina ? 28f : 82f);
            badge.Find("Portrait").gameObject.SetActive(!stamina);
            badge.Find("LightningIcon").gameObject.SetActive(stamina);
            if (stamina) fill.GetComponent<Image>().material = CreateStaminaMaterial();
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(fill);
            PrefabUtility.RecordPrefabInstancePropertyModifications(badge);
            return instance;
        }

        private static Material CreateStaminaMaterial()
        {
            string path = ArtRoot + "StaminaFill.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("CouchGuys/UI/CyanStatusFill"));
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        public static void AssignToPlayer()
        {
            PlayerWeaponHudView view = AssetDatabase.LoadAssetAtPath<PlayerWeaponHudView>(UiRoot + "PlayerWeaponHud.prefab");
            if (view != null) ConfigurePlayer(view);
        }

        private static void ConfigurePlayer(PlayerWeaponHudView view)
        {
            string rtPath = ArtRoot + "LocalPlayerPortrait.renderTexture";
            RenderTexture target = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
            if (target == null)
            {
                target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { name = "LocalPlayerPortrait" };
                AssetDatabase.CreateAsset(target, rtPath);
            }
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                PlayerWeaponHud hud = player.GetComponent<PlayerWeaponHud>();
                Transform cameraTransform = player.transform.Find("PortraitCamera");
                if (cameraTransform == null)
                {
                    cameraTransform = new GameObject("PortraitCamera", typeof(Camera)).transform;
                    cameraTransform.SetParent(player.transform, false);
                }
                Camera camera = cameraTransform.GetComponent<Camera>();
                camera.enabled = false;
                camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.07f, 0.04f, 1f);
                camera.orthographic = true;
                camera.orthographicSize = 0.32f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 1.1f;
                camera.cullingMask = ~(1 << 5);
                camera.allowHDR = true;
                camera.depth = -10;
                Transform focus = player.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name.Equals("Head", StringComparison.OrdinalIgnoreCase) || t.name.EndsWith(":Head", StringComparison.OrdinalIgnoreCase));
                if (focus == null)
                {
                    Transform visual = player.transform.Find("Visual");
                    focus = new GameObject("PortraitFocus").transform;
                    focus.SetParent(player.transform, false);
                    focus.localPosition = new Vector3(0f, 1.5f, 0f);
                }
                Renderer head = player.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "Head");
                Vector3 focusPosition = head != null ? head.bounds.center : focus.position;
                Vector3 focusOffset = focus.InverseTransformPoint(focusPosition);
                cameraTransform.position = focusPosition + player.transform.forward * 0.75f;
                cameraTransform.rotation = Quaternion.LookRotation(-player.transform.forward);
                SerializedObject s = new(hud);
                s.FindProperty("m_hudPrefab").objectReferenceValue = view;
                s.FindProperty("m_portraitCamera").objectReferenceValue = camera;
                s.FindProperty("m_portraitFocus").objectReferenceValue = focus;
                s.FindProperty("m_portraitFocusOffset").vector3Value = focusOffset;
                s.FindProperty("m_portraitHalfHeight").floatValue = 0.32f;
                s.FindProperty("m_portraitTextureTemplate").objectReferenceValue = target;
                s.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
                Debug.Log("HUD portrait focus: " + focus.name + " at " + focus.position);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        [MenuItem("Couch Guys/UI/Verify Player HUD")]
        public static void Verify()
        {
            PlayerWeaponHudView view = AssetDatabase.LoadAssetAtPath<GameObject>(UiRoot + "PlayerWeaponHud.prefab").GetComponent<PlayerWeaponHudView>();
            if (view.m_healthBar == null || view.m_staminaBar == null || view.m_slotBackgrounds.Length != 5 ||
                view.m_slotOutlines.Any(o => o == null) || view.m_ammoText == null || view.m_minimapRoot == null)
                throw new UnityException("HUD references are incomplete.");
            if (view.m_minimapRoot.Find("MapMask/StaticMap").GetComponent<RawImage>() == null ||
                view.m_minimapRoot.Find("MapMask").GetComponent<Mask>() == null ||
                view.m_minimapRoot.Find("MapMask/DynamicMarkers/LocalPlayer").GetComponent<Image>() == null ||
                view.m_minimapRoot.Find("MapMask/DynamicMarkers/DeliveryWaypoint").GetComponent<Image>() == null ||
                view.m_minimapRoot.Find("DeliveryDistance").GetComponent<Text>() == null)
                throw new UnityException("Minimap references are incomplete.");
            foreach (PlayerStatusBar bar in new[] { view.m_healthBar, view.m_staminaBar })
            {
                if (bar.Portrait.GetComponentInParent<Mask>(true) == null) throw new UnityException("Portrait mask is missing.");
                if (bar.transform.Find("Fill").GetComponent<Image>().type != Image.Type.Filled) throw new UnityException("Bar must use filled Image.");
            }
            foreach (var field in typeof(PlayerWeaponHudView).GetFields())
            {
                object value = field.GetValue(view);
                if (typeof(Object).IsAssignableFrom(field.FieldType) && (Object)value == null) throw new UnityException("Missing HUD component " + field.Name);
                if (value is Object[] references && references.Any(r => r == null)) throw new UnityException("Missing HUD array element " + field.Name);
            }
            PlayerWeaponHud hud = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponent<PlayerWeaponHud>();
            SerializedObject s = new(hud);
            foreach (string field in new[] { "m_hudPrefab", "m_portraitCamera", "m_portraitFocus", "m_portraitTextureTemplate" })
                if (s.FindProperty(field).objectReferenceValue == null) throw new UnityException("Missing " + field);
            Camera portraitCamera = (Camera)s.FindProperty("m_portraitCamera").objectReferenceValue;
            if (portraitCamera.enabled || !portraitCamera.orthographic || portraitCamera.targetTexture == null)
                throw new UnityException("Portrait camera must be saved disabled with an orthographic RenderTexture target.");
            CheckRect(view.transform.Find("HealthPanel"), Vector2.zero, new Vector2(24f, 24f), new Vector2(455f, 122f));
            CheckRect(view.transform.Find("StaminaPanel"), new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(410f, 42f));
            CheckRect(view.transform.Find("Hotbar"), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(548f, 108f));
            RenderPreview(view);
            File.WriteAllText("Temp/PlayerHudVerification.txt", "PASS: HUD prefab references, five slots, filled bars, circle masks, player portrait camera/target/focus. Owner lifecycle requires play-mode check.");
            Debug.Log("Player HUD prefab verification passed.");
        }



        private static void VerifyBehaviour(PlayerWeaponHud hud, PlayerWeaponHudView view)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            var type = typeof(PlayerWeaponHud);
            void Call(string method, params object[] values) => type.GetMethod(method, flags).Invoke(hud, values);
            Call("Awake");
            Call("Update");
            if (type.GetField("m_view", flags).GetValue(hud) != null)
                throw new UnityException("A non-owner created a HUD.");
            type.GetField("m_view", flags).SetValue(hud, view);
            Call("OnHealthChanged", 30f, 100f);
            Call("OnStaminaChanged", 25f, 100f);
            if (view.m_healthText.text != "30 / 100" ||
                !Mathf.Approximately((float)type.GetField("m_healthTarget", flags).GetValue(hud), 0.3f) ||
                !Mathf.Approximately((float)type.GetField("m_staminaTarget", flags).GetValue(hud), 0.25f))
                throw new UnityException("Health/stamina bindings failed.");
            var weapons = hud.GetComponent<PlayerWeaponController>();
            var weaponType = typeof(PlayerWeaponController);
            var selected = (FishNet.Object.Synchronizing.SyncVar<int>)weaponType.GetField("m_selectedHotbarSlot", flags).GetValue(weapons);
            for (int slot = 0; slot < 5; slot++)
            {
                selected.Value = slot;
                Call("RefreshWeapons");
                if (view.m_slotOutlines[slot].effectColor != Gold ||
                    view.m_slotOutlines.Where((outline, index) => index != slot).Any(outline => outline.effectColor != Inactive))
                    throw new UnityException("Weapon slot selection failed.");
            }
            var equipped = (FishNet.Object.Synchronizing.SyncVar<int>)weaponType.GetField("m_equippedIndex", flags).GetValue(weapons);
            equipped.Value = 0;
            ((int[])weaponType.GetField("m_magazine", flags).GetValue(weapons))[0] = 7;
            ((int[])weaponType.GetField("m_reserve", flags).GetValue(weapons))[0] = 19;
            Call("RefreshWeapons");
            if (!view.m_ammoPanel.activeSelf || !view.m_ammoText.text.Contains("7 / 19"))
                throw new UnityException("Ammo HUD failed.");
            weaponType.GetField("m_localReloading", flags).SetValue(weapons, true);
            Call("RefreshWeapons");
            if (!view.m_ammoText.text.Contains("RELOADING")) throw new UnityException("Reload HUD failed.");
            equipped.Value = -1;
            Call("RefreshWeapons");
            if (view.m_ammoPanel.activeSelf) throw new UnityException("Empty ammo panel must hide.");
            view.m_healthText.text = "100 / 100";
            selected.Value = 0;
            Call("RefreshWeapons");
        }

        private static void CheckRect(Transform transform, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = (RectTransform)transform;
            if (rect.anchorMin != anchor || rect.anchorMax != anchor || rect.anchoredPosition != position || rect.sizeDelta != size)
                throw new UnityException("HUD layout changed: " + rect.name);
        }

        private static void RenderPreview(PlayerWeaponHudView prefab)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            GameObject player = null;
            GameObject canvas = null;
            RenderTexture output = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), scene);
                var face = player.GetComponent<CouchGuys.Player.RobotFaceExpressionController>();
                var privateInstance = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(CouchGuys.Player.RobotFaceExpressionController).GetMethod("Awake", privateInstance).Invoke(face, null);
                typeof(CouchGuys.Player.RobotFaceExpressionController).GetMethod("LateUpdate", privateInstance).Invoke(face, null);
                PlayerWeaponHud hud = player.GetComponent<PlayerWeaponHud>();
                SerializedObject s = new(hud);
                Camera portrait = (Camera)s.FindProperty("m_portraitCamera").objectReferenceValue;
                output = new RenderTexture(256, 256, 24);
                portrait.targetTexture = output;
                portrait.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(scene);
                portrait.aspect = 1f;
                portrait.ResetProjectionMatrix();
                GameObject lightObject = new GameObject("PreviewLight", typeof(Light));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
                Light light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 2f;
                light.transform.rotation = Quaternion.Euler(25f, 180f, 0f);
                portrait.Render();
                SaveRender(output, "Temp/PlayerHudPortrait.png");
                canvas = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                PlayerWeaponHudView view = canvas.GetComponent<PlayerWeaponHudView>();
                view.m_healthBar.Portrait.texture = output;
                VerifyBehaviour(hud, view);
                GameObject cameraObject = new GameObject("HudPreviewCamera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.15f, 0.19f);
                camera.orthographic = true;
                camera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(scene);
                camera.cullingMask = 1 << 5;
                foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
                Canvas c = canvas.GetComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = camera;
                c.planeDistance = 1f;
                RenderTexture hudTarget = new RenderTexture(1920, 1080, 24);
                camera.targetTexture = hudTarget;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                SaveRender(hudTarget, "Temp/PlayerHudPreview.png");
                view.m_healthBar.SetValue(-1f);
                view.m_staminaBar.SetValue(2f);
                if (view.m_healthBar.transform.Find("Fill").GetComponent<Image>().fillAmount != 0f ||
                    view.m_staminaBar.transform.Find("Fill").GetComponent<Image>().fillAmount != 1f)
                    throw new UnityException("Status fill must clamp to capacity.");
                view.m_staminaBar.SetValue(0.5f);
                camera.Render();
                SaveRender(hudTarget, "Temp/PlayerHudPartialPreview.png");
                File.WriteAllText("Temp/PlayerHudBehaviourVerified.txt", "PASS: bar values clamp, health text/targets, stamina targets, all five selections, ammo/reload/empty state, non-owner startup. Full/partial renders saved.");
                File.WriteAllText("Temp/PlayerHudFramingVerified.txt", "Rendered full, empty and partial bars for visual inspection.");
                hudTarget.Release();
                Object.DestroyImmediate(hudTarget);
            }
            finally
            {
                RenderTexture.active = previous;
                if (output != null) { output.Release(); Object.DestroyImmediate(output); }
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void SaveRender(RenderTexture target, string path)
        {
            RenderTexture.active = target;
            Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
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

            // Preserve the Inspector-authored prompt when rebuilding the other HUD elements.
            PlayerWeaponHudView existing = AssetDatabase.LoadAssetAtPath<PlayerWeaponHudView>(UiRoot + "PlayerWeaponHud.prefab");
            if (existing != null && existing.m_interactionPrompt != null)
                m_interactionPrompt = Object.Instantiate(existing.m_interactionPrompt, m_canvasRoot.transform, false);

            BuildHealthPanel();
            BuildDeliveryPanel();
            BuildDeliveryTimerPanel();
            BuildCurrencyPanel();
            BuildStaminaPanel();
            BuildHotbar();
            BuildAmmoPanel();
            BuildMinimap();
            m_downedText = Label("DownedCountdown", m_canvasRoot.transform, "DOWNED", 32,
                TextAnchor.MiddleCenter, FontStyle.Bold, Red);
            SetRect(m_downedText.rectTransform, new Vector2(0.5f, 0.65f), new Vector2(0.5f, 0.65f),
                Vector2.zero, new Vector2(720f, 70f));
            m_downedText.gameObject.SetActive(false);
        }

        private void BuildMinimap()
        {
            Image frame = MinimapImage("MinimapFrame", m_canvasRoot.transform,
                new Color(0.04f, 0.055f, 0.075f, 0.96f), m_circleSprite);
            RectTransform frameRect = frame.rectTransform;
            frameRect.anchorMin = frameRect.anchorMax = Vector2.one;
            frameRect.pivot = Vector2.one;
            frameRect.anchoredPosition = new Vector2(-24f, -112f);
            frameRect.sizeDelta = Vector2.one * 270f;

            Image maskImage = MinimapImage("MapMask", frameRect, Color.white, m_circleSprite);
            Stretch(maskImage.rectTransform, 7f);
            Mask mask = maskImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject mapObject = new("StaticMap", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            mapObject.transform.SetParent(maskImage.transform, false);
            RawImage m_mapImage = mapObject.GetComponent<RawImage>();
            m_mapImage.raycastTarget = false;
            Stretch(m_mapImage.rectTransform, 0f);

            GameObject markerObject = new("DynamicMarkers", typeof(RectTransform));
            markerObject.transform.SetParent(maskImage.transform, false);
            RectTransform m_markerLayer = markerObject.GetComponent<RectTransform>();
            Stretch(m_markerLayer, 0f);

            Compass("N", frameRect, new Vector2(0f, -10f), new Vector2(0.5f, 1f));
            Compass("E", frameRect, new Vector2(-10f, 0f), new Vector2(1f, 0.5f));
            Compass("S", frameRect, new Vector2(0f, 10f), new Vector2(0.5f, 0f));
            Compass("W", frameRect, new Vector2(10f, 0f), new Vector2(0f, 0.5f));

            UnityEngine.UI.Image local = MinimapImage("LocalPlayer", m_markerLayer,
                new Color(0.12f, 0.95f, 1f, 1f), null);
            local.rectTransform.anchorMin = local.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            local.rectTransform.sizeDelta = new Vector2(25f, 30f);

            Image m_waypointImage = MinimapImage("DeliveryWaypoint", m_markerLayer,
                new Color(1f, 0.76f, 0.08f, 1f), null);
            RectTransform m_waypointRect = m_waypointImage.rectTransform;
            m_waypointRect.anchorMin = m_waypointRect.anchorMax = new Vector2(0.5f, 0.5f);
            m_waypointRect.sizeDelta = new Vector2(34f, 34f);
            m_waypointRect.gameObject.SetActive(false);

            Text m_distanceText = MinimapText("DeliveryDistance", frameRect, string.Empty, 22);
            RectTransform distanceRect = m_distanceText.rectTransform;
            distanceRect.anchorMin = distanceRect.anchorMax = new Vector2(0.5f, 0f);
            distanceRect.pivot = new Vector2(0.5f, 1f);
            distanceRect.anchoredPosition = new Vector2(0f, -5f);
            distanceRect.sizeDelta = new Vector2(270f, 30f);
            m_distanceText.gameObject.SetActive(false);

            m_minimapRoot = frameRect;
            frame.gameObject.SetActive(false);
        }

        private void Compass(string value, Transform parent, Vector2 position, Vector2 anchor)
        {
            UnityEngine.UI.Text label = MinimapText("Compass " + value, parent, value, 19);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(28f, 28f);
        }

        private static UnityEngine.UI.Text MinimapText(string name, Transform parent, string value, int fontSize)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Text));
            go.transform.SetParent(parent, false);
            UnityEngine.UI.Text text = go.GetComponent<UnityEngine.UI.Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            go.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            return text;
        }

        private static UnityEngine.UI.Image MinimapImage(string name, Transform parent, Color colour, Sprite sprite)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            UnityEngine.UI.Image image = go.GetComponent<UnityEngine.UI.Image>();
            image.color = colour;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private void BuildHealthPanel()
        {
            RectTransform panel = Rect("HealthPanel", m_canvasRoot.transform);
            SetRect(panel, Vector2.zero, Vector2.zero, new Vector2(24f, 24f), new Vector2(455f, 122f), Vector2.zero);
            m_healthBar = CreateStatus(panel, false);
            Text name = Label("Name", panel, "You", 25, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(126f, -14f), new Vector2(290f, 34f), new Vector2(0f, 1f));
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

        private void BuildDeliveryTimerPanel()
        {
            RectTransform panel = Panel("DeliveryTimerPanel", m_canvasRoot.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -181f),
                new Vector2(480f, 92f), Gold);
            m_timerPanel = panel.gameObject;
            Text label = Label("TimerLabel", panel, "DELIVERY TIME", 19,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -12f), new Vector2(260f, 34f), new Vector2(0f, 1f));
            m_timerText = Label("TimerValue", panel, "00:00", 32,
                TextAnchor.MiddleRight, FontStyle.Bold, Gold);
            SetRect(m_timerText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-20f, -10f), new Vector2(150f, 38f), new Vector2(1f, 1f));
            RectTransform track = Panel("TimerTrack", panel, Vector2.zero, Vector2.zero,
                new Vector2(22f, 15f), new Vector2(436f, 14f), Inactive, false);
            track.pivot = Vector2.zero;
            m_timerFill = Fill(track, "TimerFill");
            m_timerPanel.SetActive(false);

            m_failureText = Label("DeliveryFailed", m_canvasRoot.transform, "DELIVERY FAILED!",
                42, TextAnchor.MiddleCenter, FontStyle.Bold, Red);
            SetRect(m_failureText.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f),
                Vector2.zero, new Vector2(520f, 70f));
            Outline outline = m_failureText.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(3f, -3f);
            m_failureText.gameObject.SetActive(false);
        }

        private void BuildCurrencyPanel()
        {
            RectTransform panel = Panel("CurrencyPanel", m_canvasRoot.transform, Vector2.one, Vector2.one, new Vector2(-24f, -24f), new Vector2(260f, 76f), new Color(0.55f, 0.82f, 1f), true, new Vector2(1f, 1f));
            RectTransform iconBackground = Rect("CurrencyIconBackground", panel);
            SetRect(iconBackground, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(15f, 0f), new Vector2(52f, 52f), new Vector2(0f, 0.5f));
            Image backgroundImage = iconBackground.gameObject.AddComponent<Image>();
            backgroundImage.sprite = m_circleSprite;
            backgroundImage.color = new Color(0.12f, 0.75f, 0.28f);
            backgroundImage.raycastTarget = false;
            Text icon = Label("CurrencyIcon", iconBackground, "$", 31, TextAnchor.MiddleCenter, FontStyle.Bold, Color.white);
            Stretch(icon.rectTransform, 0f);
            m_currencyText = Label("CurrencyText", panel, "$ 0", 34, TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(m_currencyText.rectTransform, Vector2.zero, Vector2.one, new Vector2(82f, 8f), new Vector2(-10f, -8f));
        }

        private void BuildStaminaPanel()
        {
            RectTransform panel = Rect("StaminaPanel", m_canvasRoot.transform);
            SetRect(panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -25f), new Vector2(410f, 42f), new Vector2(0.5f, 1f));
            m_staminaBar = CreateStatus(panel, true);
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
            m_roundedSprite = LoadShape("HudRoundedShape", 32, 8f);
            m_circleSprite = LoadShape("HudCircleShape", 128, 64f);
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

    }
}
