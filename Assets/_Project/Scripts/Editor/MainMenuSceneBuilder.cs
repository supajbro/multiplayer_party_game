using System.IO;
using CouchGuys.ProceduralGeneration;
using CouchGuys.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CouchGuys.Editor
{
    public static class MainMenuSceneBuilder
    {
        private const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string LogoPath = "Assets/_Project/Concepts/logo.png";
        private const string ButtonBackgroundPath = "Assets/_Project/UI/Button_Background.png";
        private const string SoloOverlayPath = "Assets/_Project/UI/Button_Overlay_white.png";
        private const string MultiplayerOverlayPath = "Assets/_Project/UI/Button_Overlay_yellow.png";
        private const string SettingsOverlayPath = "Assets/_Project/UI/Button_Overlay_orange.png";
        private const string VolumeProfilePath = "Assets/_Project/Settings/MainMenuVolumeProfile.asset";

        [InitializeOnLoadMethod]
        private static void BuildMissingSceneAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(MainMenuScenePath) &&
                    !EditorApplication.isPlayingOrWillChangePlaymode &&
                    !EditorApplication.isCompiling)
                {
                    BuildMainMenuScene();
                }
            };
        }

        [MenuItem("Couch Guys/Build Main Menu Scene")]
        public static void BuildMainMenuScene()
        {
            ConfigureSingleSprite(LogoPath);
            ConfigureSingleSprite(ButtonBackgroundPath);
            ConfigureSingleSprite(SoloOverlayPath);
            ConfigureSingleSprite(MultiplayerOverlayPath);
            ConfigureSingleSprite(SettingsOverlayPath);

            Scene gameplayScene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            NeighbourhoodGenerator sourceGenerator = FindComponentInScene<NeighbourhoodGenerator>(gameplayScene);
            if (sourceGenerator == null)
            {
                throw new UnityException("SampleScene has no NeighbourhoodGenerator to reuse for MainMenu.");
            }

            Scene menuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(menuScene);

            Camera menuCamera = CreateCamera(menuScene);
            CreateLighting(menuScene);
            CreateDecorativeNeighbourhood(menuScene, sourceGenerator, menuCamera);
            CreateMenuUi(menuScene);
            CreateEventSystem(menuScene);
            CreatePostProcessing(menuScene);

            EditorSceneManager.SaveScene(menuScene, MainMenuScenePath);
            EditorSceneManager.CloseScene(gameplayScene, true);
            ConfigureBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateScene(menuScene);
            Debug.Log("Couch Guys MainMenu scene built successfully.");
        }

        private static void ConfigureSingleSprite(string path)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        private static Camera CreateCamera(Scene scene)
        {
            GameObject cameraObject = new GameObject("Menu Camera", typeof(Camera), typeof(AudioListener),
                typeof(UniversalAdditionalCameraData));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.transform.SetPositionAndRotation(new Vector3(-80f, 90f, -110f), Quaternion.Euler(28f, 35f, 0f));
            return camera;
        }

        private static void CreateLighting(Scene scene)
        {
            GameObject lightObject = new GameObject("Warm Sun", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(1f, 0.86f, 0.68f);
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.48f, 0.68f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.62f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.24f, 0.2f, 0.16f);
        }

        private static void CreateDecorativeNeighbourhood(
            Scene scene,
            NeighbourhoodGenerator source,
            Camera menuCamera)
        {
            GameObject root = new GameObject("Decorative Neighbourhood");
            SceneManager.MoveGameObjectToScene(root, scene);
            NeighbourhoodGenerator generator = root.AddComponent<NeighbourhoodGenerator>();
            EditorUtility.CopySerialized(source, generator);

            SerializedObject serialized = new SerializedObject(generator);
            serialized.FindProperty("m_generateOnStart").boolValue = true;
            serialized.FindProperty("m_useRandomSeed").boolValue = true;
            serialized.FindProperty("m_createDeliveryNpc").boolValue = false;
            serialized.FindProperty("m_navMeshSurface").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            MainMenuEnvironment environment = root.AddComponent<MainMenuEnvironment>();
            SerializedObject environmentSerialized = new SerializedObject(environment);
            environmentSerialized.FindProperty("m_generator").objectReferenceValue = generator;
            environmentSerialized.FindProperty("m_menuCamera").objectReferenceValue = menuCamera;
            environmentSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateMenuUi(Scene scene)
        {
            GameObject canvasObject = new GameObject("Main Menu Canvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            CreateLeftScrim(canvas.transform);
            CreateLogo(canvas.transform);

            Sprite background = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonBackgroundPath);
            Sprite solo = AssetDatabase.LoadAssetAtPath<Sprite>(SoloOverlayPath);
            Sprite multiplayer = AssetDatabase.LoadAssetAtPath<Sprite>(MultiplayerOverlayPath);
            Sprite settings = AssetDatabase.LoadAssetAtPath<Sprite>(SettingsOverlayPath);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Button soloButton = CreateButton(canvas.transform, "Solo Button", "Solo", -405f, background, solo, font);
            Button multiplayerButton = CreateButton(canvas.transform, "Multiplayer Button", "Multiplayer", -600f,
                background, multiplayer, font);
            Button settingsButton = CreateButton(canvas.transform, "Settings Button", "Settings", -795f, background,
                settings, font);

            GameObject managerObject = new GameObject("MainMenuManager", typeof(MainMenuManager));
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            MainMenuManager manager = managerObject.GetComponent<MainMenuManager>();
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("m_soloButton").objectReferenceValue = soloButton;
            serialized.FindProperty("m_multiplayerButton").objectReferenceValue = multiplayerButton;
            serialized.FindProperty("m_settingsButton").objectReferenceValue = settingsButton;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateLeftScrim(Transform parent)
        {
            GameObject scrimObject = new GameObject("Left UI Contrast", typeof(RectTransform), typeof(Image));
            scrimObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)scrimObject.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 0f);
            Image image = scrimObject.GetComponent<Image>();
            image.color = new Color(0.13f, 0.055f, 0.02f, 0.16f);
            image.raycastTarget = false;
        }

        private static void CreateLogo(Transform parent)
        {
            GameObject logoObject = new GameObject("Couch Guys Logo", typeof(RectTransform), typeof(Image));
            logoObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)logoObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(54f, -35f);
            rect.sizeDelta = new Vector2(665f, 330f);
            Image image = logoObject.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static Button CreateButton(
            Transform parent,
            string objectName,
            string label,
            float anchoredY,
            Sprite background,
            Sprite overlay,
            Font font)
        {
            GameObject root = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(MainMenuButtonFeedback));
            root.transform.SetParent(parent, false);
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 0.5f);
            rootRect.anchoredPosition = new Vector2(82f, anchoredY);
            rootRect.sizeDelta = new Vector2(570f, 180f);

            Image backgroundImage = root.GetComponent<Image>();
            backgroundImage.sprite = background;
            backgroundImage.preserveAspect = true;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = backgroundImage;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            GameObject overlayObject = new GameObject("Colour Overlay", typeof(RectTransform), typeof(Image));
            overlayObject.transform.SetParent(root.transform, false);
            RectTransform overlayRect = (RectTransform)overlayObject.transform;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = new Vector2(24f, 22f);
            overlayRect.offsetMax = new Vector2(-24f, -22f);
            Image overlayImage = overlayObject.GetComponent<Image>();
            overlayImage.sprite = overlay;
            overlayImage.preserveAspect = true;
            overlayImage.raycastTarget = false;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Shadow));
            labelObject.transform.SetParent(root.transform, false);
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(32f, 18f);
            labelRect.offsetMax = new Vector2(-32f, -18f);
            Text text = labelObject.GetComponent<Text>();
            text.text = label;
            text.font = font;
            text.fontSize = label == "Multiplayer" ? 49 : 56;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(0.19f, 0.065f, 0.025f);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            Shadow shadow = labelObject.GetComponent<Shadow>();
            shadow.effectColor = new Color(1f, 0.78f, 0.33f, 0.42f);
            shadow.effectDistance = new Vector2(2f, -3f);
            return button;
        }

        private static void CreateEventSystem(Scene scene)
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(eventSystem, scene);
        }

        private static void CreatePostProcessing(Scene scene)
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            profile.components.Clear();
            Bloom bloom = profile.Add<Bloom>();
            bloom.active = true;
            bloom.intensity.Override(0.28f);
            bloom.threshold.Override(1.05f);
            ColorAdjustments colour = profile.Add<ColorAdjustments>();
            colour.active = true;
            colour.postExposure.Override(0.18f);
            colour.contrast.Override(9f);
            colour.saturation.Override(12f);
            Vignette vignette = profile.Add<Vignette>();
            vignette.active = true;
            vignette.intensity.Override(0.17f);
            vignette.smoothness.Override(0.56f);
            DepthOfField depthOfField = profile.Add<DepthOfField>();
            depthOfField.active = true;
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(115f);
            depthOfField.gaussianEnd.Override(300f);
            depthOfField.gaussianMaxRadius.Override(0.75f);
            EditorUtility.SetDirty(profile);

            GameObject volumeObject = new GameObject("Menu Post Processing", typeof(Volume));
            SceneManager.MoveGameObjectToScene(volumeObject, scene);
            Volume volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        private static void ConfigureBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(GameplayScenePath, true)
            };
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static void ValidateScene(Scene scene)
        {
            if (FindComponentInScene<MainMenuManager>(scene) == null ||
                FindComponentInScene<NeighbourhoodGenerator>(scene) == null ||
                FindComponentInScene<Canvas>(scene) == null ||
                FindComponentInScene<EventSystem>(scene) == null ||
                FindComponentInScene<Camera>(scene) == null)
            {
                throw new UnityException("MainMenu scene validation failed.");
            }
        }
    }
}
