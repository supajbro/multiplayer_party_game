using System.IO;
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
        private const string BackgroundPath = "Assets/_Project/Concepts/Couch_Guys_Concept_01.png";
        private const string SoloButtonPath = "Assets/_Project/UI/button_solo.png";
        private const string MultiplayerButtonPath = "Assets/_Project/UI/button_multiplayer.png";
        private const string SettingsButtonPath = "Assets/_Project/UI/button_settings.png";
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
            ConfigureSingleSprite(BackgroundPath);
            ConfigureSingleSprite(SoloButtonPath);
            ConfigureSingleSprite(MultiplayerButtonPath);
            ConfigureSingleSprite(SettingsButtonPath);

            Scene menuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(menuScene);

            CreateCamera(menuScene);
            CreateLighting(menuScene);
            CreateMenuUi(menuScene);
            CreateEventSystem(menuScene);
            CreatePostProcessing(menuScene);
            CreateMusic(menuScene);

            EditorSceneManager.SaveScene(menuScene, MainMenuScenePath);
            ConfigureBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateScene(menuScene);
            Debug.Log("Couch Guys MainMenu scene built successfully.");
        }

        private static void CreateMusic(Scene scene)
        {
            GameObject musicObject = new GameObject("Main Menu Music", typeof(MainMenuMusic));
            SceneManager.MoveGameObjectToScene(musicObject, scene);
            SerializedObject serialized = new SerializedObject(musicObject.GetComponent<MainMenuMusic>());
            serialized.FindProperty("m_clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/_Project/Audio/Jingles/Delivery_Full_Preview_Loop.wav");
            serialized.ApplyModifiedPropertiesWithoutUndo();
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

            CreateBackground(canvas.transform);

            CreateLeftScrim(canvas.transform);
            CreateLogo(canvas.transform);

            Sprite solo = AssetDatabase.LoadAssetAtPath<Sprite>(SoloButtonPath);
            Sprite multiplayer = AssetDatabase.LoadAssetAtPath<Sprite>(MultiplayerButtonPath);
            Sprite settings = AssetDatabase.LoadAssetAtPath<Sprite>(SettingsButtonPath);

            Button soloButton = CreateButton(canvas.transform, "Solo Button", -405f, solo);
            Button multiplayerButton = CreateButton(canvas.transform, "Multiplayer Button", -600f, multiplayer);
            Button settingsButton = CreateButton(canvas.transform, "Settings Button", -795f, settings);

            GameObject managerObject = new GameObject("MainMenuManager", typeof(MainMenuManager));
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            MainMenuManager manager = managerObject.GetComponent<MainMenuManager>();
            SerializedObject serialized = new SerializedObject(manager);
            serialized.FindProperty("m_soloButton").objectReferenceValue = soloButton;
            serialized.FindProperty("m_multiplayerButton").objectReferenceValue = multiplayerButton;
            serialized.FindProperty("m_settingsButton").objectReferenceValue = settingsButton;
            serialized.FindProperty("m_backgroundSprite").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateBackground(Transform parent)
        {
            GameObject backgroundObject = new GameObject("Concept Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)backgroundObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = backgroundObject.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            image.preserveAspect = true;
            image.raycastTarget = false;
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
            float anchoredY,
            Sprite sprite)
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
            backgroundImage.sprite = sprite;
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
                FindComponentInScene<Canvas>(scene) == null ||
                FindComponentInScene<EventSystem>(scene) == null ||
                FindComponentInScene<Camera>(scene) == null)
            {
                throw new UnityException("MainMenu scene validation failed.");
            }
        }
    }
}
