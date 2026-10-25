using System.Linq;
using CouchGuys.Networking;
using CouchGuys.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CouchGuys.Editor
{
    public static class SessionMenuPrefabBuilder
    {
        private const string Root = "Assets/Prefabs/UI/";
        private const string Transition = "Assets/Scenes/RetryTransition.unity";

        [InitializeOnLoadMethod]
        private static void ScheduleInstall() => EditorApplication.delayCall += Install;

        [MenuItem("Couch Guys/Build Session Menus")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            System.IO.Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<SessionMenuView>(Root + "PauseMenu.prefab") == null) Build(false);
            if (AssetDatabase.LoadAssetAtPath<SessionMenuView>(Root + "GameOverMenu.prefab") == null) Build(true);
            foreach (string name in new[] { "SoloNetworkBootstrap", "SteamMultiplayerBootstrap" })
            {
                string path = "Assets/_Project/Resources/" + name + ".prefab";
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                GameplaySessionController controller = root.GetComponent<GameplaySessionController>();
                if (controller == null) controller = root.AddComponent<GameplaySessionController>();
                var serialized = new SerializedObject(controller);
                var pause = serialized.FindProperty("m_pauseMenuPrefab");
                var gameOver = serialized.FindProperty("m_gameOverMenuPrefab");
                // Keep existing Inspector assignments and avoid reserializing transports on every reload.
                if (pause.objectReferenceValue == null || gameOver.objectReferenceValue == null)
                {
                    if (pause.objectReferenceValue == null) pause.objectReferenceValue = AssetDatabase.LoadAssetAtPath<SessionMenuView>(Root + "PauseMenu.prefab");
                    if (gameOver.objectReferenceValue == null) gameOver.objectReferenceValue = AssetDatabase.LoadAssetAtPath<SessionMenuView>(Root + "GameOverMenu.prefab");
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            if (!System.IO.File.Exists(Transition))
            {
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene, Transition);
                EditorSceneManager.CloseScene(scene, true);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == Transition))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(Transition, true)).ToArray();
            AssetDatabase.SaveAssets();
        }

        private static void Build(bool gameOver)
        {
            string name = gameOver ? "GameOverMenu" : "PauseMenu";
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(SessionMenuView));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var background = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            RectTransform backdrop = background.GetComponent<RectTransform>();
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            background.GetComponent<Image>().color = new Color(.04f, .06f, .09f, .95f);
            Label(root.transform, "Title", gameOver ? "Game Over" : "Paused", 150, 52);
            SessionMenuView view = root.GetComponent<SessionMenuView>();
            view.PrimaryButton = Button(root.transform, gameOver ? "Retry" : "Resume", 15);
            view.ExitButton = Button(root.transform, "Exit Game", -85);
            if (gameOver)
            {
                view.ProgressLabel = Label(root.transform, "Voting Progress", "0/0 Players Ready", -185, 30);
                view.LocalReadyLabel = Label(root.transform, "Local Ready State", "You are not ready", -235, 24);
            }
            var events = new GameObject("Menu Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(root.transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            events.GetComponent<EventSystem>().firstSelectedGameObject = view.PrimaryButton.gameObject;
            PrefabUtility.SaveAsPrefabAsset(root, Root + name + ".prefab");
            Object.DestroyImmediate(root);
        }

        private static Text Label(Transform parent, string name, string value, float y, int size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, y); rect.sizeDelta = new Vector2(700, 80);
            Text label = go.GetComponent<Text>();
            label.text = value; label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size; label.alignment = TextAnchor.MiddleCenter; label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private static Button Button(Transform parent, string label, float y)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, y); rect.sizeDelta = new Vector2(420, 80);
            go.GetComponent<Image>().color = new Color(.15f, .24f, .34f);
            Button button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            Text text = Label(go.transform, "Label", label, 0, 32);
            text.rectTransform.sizeDelta = rect.sizeDelta;
            return button;
        }
    }
}
