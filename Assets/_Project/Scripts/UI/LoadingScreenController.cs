using System.Collections;
using CouchGuys.Input;
using CouchGuys.Networking;
using CouchGuys.ProceduralGeneration;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    public enum GameLaunchMode { DirectScene, Solo, Multiplayer }

    public static class GameLaunchContext
    {
        public static GameLaunchMode Mode { get; private set; } = GameLaunchMode.DirectScene;
        public static bool UseLoadingFlow => Mode != GameLaunchMode.DirectScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Mode = GameLaunchMode.DirectScene;

        public static void EnterMainMenu() => Mode = GameLaunchMode.DirectScene;
        public static void BeginSolo() => Mode = GameLaunchMode.Solo;
        public static void BeginMultiplayer() => Mode = GameLaunchMode.Multiplayer;
    }

    /// <summary>Persistent, code-authored loading view shared by solo and network launches.</summary>
    [DisallowMultipleComponent]
    public sealed class LoadingScreenController : MonoBehaviour
    {
        private static readonly string[] Tips =
        {
            "Tip: Big couches are easier with a coordinated crew!",
            "Tip: Watch the corners — furniture has feelings too.",
            "Tip: Use the sled to move awkward deliveries faster.",
            "Tip: Thieves hate teamwork almost as much as locked doors.",
            "Tip: The shortest road is not always the safest road."
        };

        private static LoadingScreenController s_instance;
        private Text m_status;
        private Text m_tip;
        private Image m_fill;
        private RectTransform m_spinner;
        private Button m_returnButton;
        private float m_displayedProgress;
        private bool m_finishing;

        public static void Show(string initialStatus)
        {
            if (s_instance == null)
            {
                GameObject root = new GameObject("Couch Guys Loading Screen");
                s_instance = root.AddComponent<LoadingScreenController>();
            }
            s_instance.m_status.text = initialStatus;
            s_instance.gameObject.SetActive(true);
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            DontDestroyOnLoad(gameObject);
            BuildUi();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (s_instance == this) s_instance = null;
        }

        private void Update()
        {
            if (m_spinner != null) m_spinner.Rotate(0f, 0f, -150f * Time.unscaledDeltaTime);
            m_fill.fillAmount = Mathf.MoveTowards(m_fill.fillAmount, m_displayedProgress, Time.unscaledDeltaTime * 0.8f);
            foreach (PlayerInputReader input in FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None))
                input.SetGameplaySuppressed(true);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "SampleScene" || m_finishing) return;
            StartCoroutine(TrackReadiness());
        }

        private IEnumerator TrackReadiness()
        {
            m_displayedProgress = 0.12f;
            float deadline = Time.realtimeSinceStartup + 120f;
            NeighbourhoodGenerator generator = null;
            while (generator == null && Time.realtimeSinceStartup < deadline)
            {
                generator = FindFirstObjectByType<NeighbourhoodGenerator>();
                yield return null;
            }

            if (generator == null) { Fail("Could not find the neighbourhood generator."); yield break; }
            while (!generator.IsGenerationReady && Time.realtimeSinceStartup < deadline)
            {
                m_displayedProgress = 0.12f + generator.GenerationProgress * 0.76f;
                if (!string.IsNullOrWhiteSpace(generator.GenerationStatus)) m_status.text = generator.GenerationStatus;
                yield return null;
            }
            if (!generator.IsGenerationReady) { Fail("Neighbourhood generation timed out."); yield break; }

            m_displayedProgress = 0.9f;
            m_status.text = "Spawning Couches...";
            while (FindFirstObjectByType<PlayerInputReader>() == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (FindFirstObjectByType<PlayerInputReader>() == null) { Fail("Player spawning timed out."); yield break; }

            if (GameLaunchContext.Mode == GameLaunchMode.Multiplayer)
            {
                SteamLobbyController lobby = FindFirstObjectByType<SteamLobbyController>();
                if (lobby == null) { Fail("The multiplayer session was lost while loading."); yield break; }
                lobby.SetLocalPlayerReady();
                m_status.text = "Waiting for other players...";
                while (!lobby.IsGamePlaying && Time.realtimeSinceStartup < deadline)
                {
                    if (lobby.IsLocalLobbyOwner && lobby.AreAllLobbyMembersReady()) lobby.ReleasePlayers();
                    if (!lobby.IsInLobby) { Fail("Disconnected while loading."); yield break; }
                    yield return null;
                }
                if (!lobby.IsGamePlaying)
                {
                    if (lobby.IsLocalLobbyOwner) lobby.ReleasePlayers();
                    else { Fail("The host did not finish loading the session."); yield break; }
                }
            }

            m_displayedProgress = 1f;
            m_status.text = "Ready!";
            yield return null;
            yield return null;
            Finish();
        }

        private void Finish()
        {
            m_finishing = true;
            foreach (PlayerInputReader input in FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None))
                input.SetGameplaySuppressed(false);
            Destroy(gameObject);
        }

        private void Fail(string message)
        {
            m_status.text = message;
            m_returnButton.gameObject.SetActive(true);
        }

        private void ReturnToMenu()
        {
            FindFirstObjectByType<SteamLobbyController>()?.LeaveLobby();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            Destroy(gameObject);
        }

        private void BuildUi()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            gameObject.AddComponent<GraphicRaycaster>();

            Image backdrop = UiImage(transform, "Backdrop", new Color(0.08f, 0.32f, 0.55f, 1f));
            Stretch(backdrop.rectTransform);
            Image sun = UiImage(transform, "Sun", new Color(1f, 0.72f, 0.22f, 1f));
            sun.rectTransform.anchorMin = sun.rectTransform.anchorMax = new Vector2(.78f, .72f);
            sun.rectTransform.sizeDelta = new Vector2(460f, 460f);

            Image existingLogo = GameObject.Find("Couch Guys Logo")?.GetComponent<Image>();
            if (existingLogo != null && existingLogo.sprite != null)
            {
                Image logoImage = UiImage(transform, "Couch Guys Logo", Color.white);
                logoImage.sprite = existingLogo.sprite; logoImage.preserveAspect = true;
                logoImage.rectTransform.anchorMin = logoImage.rectTransform.anchorMax = new Vector2(.5f, .68f);
                logoImage.rectTransform.sizeDelta = new Vector2(900f, 340f);
            }
            else
            {
                Text logo = UiText(transform, "COUCH\nGUYS", 112, FontStyle.Bold, Color.white);
                logo.alignment = TextAnchor.MiddleCenter;
                logo.rectTransform.anchorMin = logo.rectTransform.anchorMax = new Vector2(.5f, .68f);
                logo.rectTransform.sizeDelta = new Vector2(900f, 300f);
            }

            m_spinner = UiImage(transform, "Spinner", new Color(1f, .58f, .12f, 1f)).rectTransform;
            m_spinner.anchorMin = m_spinner.anchorMax = new Vector2(.5f, .42f);
            m_spinner.sizeDelta = new Vector2(54f, 54f);
            Text spinnerMark = UiText(m_spinner, "↻", 54, FontStyle.Bold, Color.white);
            Stretch(spinnerMark.rectTransform);
            spinnerMark.alignment = TextAnchor.MiddleCenter;

            m_status = UiText(transform, "Building Neighbourhood...", 38, FontStyle.Bold, Color.white);
            m_status.alignment = TextAnchor.MiddleCenter;
            m_status.rectTransform.anchorMin = m_status.rectTransform.anchorMax = new Vector2(.5f, .32f);
            m_status.rectTransform.sizeDelta = new Vector2(1000f, 80f);

            Image track = UiImage(transform, "Progress Track", new Color(.05f, .12f, .2f, .75f));
            track.rectTransform.anchorMin = track.rectTransform.anchorMax = new Vector2(.5f, .24f);
            track.rectTransform.sizeDelta = new Vector2(820f, 42f);
            m_fill = UiImage(track.transform, "Actual Progress", new Color(1f, .63f, .12f, 1f));
            Stretch(m_fill.rectTransform, 6f);
            m_fill.type = Image.Type.Filled;
            m_fill.fillMethod = Image.FillMethod.Horizontal;
            m_fill.fillAmount = 0f;

            m_tip = UiText(transform, Tips[Random.Range(0, Tips.Length)], 27, FontStyle.Italic, new Color(1f, .93f, .72f));
            m_tip.alignment = TextAnchor.MiddleCenter;
            m_tip.rectTransform.anchorMin = m_tip.rectTransform.anchorMax = new Vector2(.5f, .13f);
            m_tip.rectTransform.sizeDelta = new Vector2(1200f, 70f);

            m_returnButton = UiButton(transform, "Return to Main Menu", ReturnToMenu);
            m_returnButton.GetComponent<RectTransform>().anchorMin = m_returnButton.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, .12f);
            m_returnButton.GetComponent<RectTransform>().sizeDelta = new Vector2(420f, 80f);
            m_returnButton.gameObject.SetActive(false);
        }

        internal static Image UiImage(Transform parent, string name, Color colour)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>(); image.color = colour; return image;
        }

        internal static Text UiText(Transform parent, string value, int size, FontStyle style, Color colour)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>(); text.text = value; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.fontStyle = style; text.color = colour; text.raycastTarget = false; return text;
        }

        internal static Button UiButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            Image image = UiImage(parent, label, new Color(1f, .58f, .12f, 1f));
            Button button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            Text text = UiText(image.transform, label, 30, FontStyle.Bold, new Color(.2f, .07f, .02f));
            Stretch(text.rectTransform); text.alignment = TextAnchor.MiddleCenter; return button;
        }

        internal static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
