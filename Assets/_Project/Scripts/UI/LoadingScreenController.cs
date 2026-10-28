using System.Collections;
using CouchGuys.Input;
using CouchGuys.Networking;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
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
        public static int AuthoritativeSeed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Mode = GameLaunchMode.DirectScene;
            AuthoritativeSeed = 0;
        }

        public static void EnterMainMenu() => Mode = GameLaunchMode.DirectScene;
        public static void BeginSolo()
        {
            Mode = GameLaunchMode.Solo;
            AuthoritativeSeed = Random.Range(1, int.MaxValue);
        }

        public static void BeginMultiplayer(int seed = 0)
        {
            Mode = GameLaunchMode.Multiplayer;
            if (seed != 0) AuthoritativeSeed = seed;
        }
    }

    /// <summary>Persistent, prefab-authored loading view shared by solo and network launches.</summary>
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

        public static bool BlocksGameplayInput => s_instance != null && s_instance.isActiveAndEnabled;
        private static LoadingScreenController s_instance;
        [SerializeField] private Text m_status;
        [SerializeField] private Text m_tip;
        [SerializeField] private Image m_fill;
        [SerializeField] private Text m_progressPercent;
        [SerializeField] private RectTransform m_spinner;
        [SerializeField] private Button m_returnButton;
        private float m_displayedProgress;
        private float m_visualProgress;
        private bool m_tracking;

        public static void Show(string initialStatus)
        {
            if (s_instance == null)
            {
                var settings = Resources.Load<LoadingScreenSettings>("UI/LoadingScreenSettings");
                LoadingScreenController prefab = settings != null ? settings.Prefab : null;
                if (prefab == null)
                {
                    Debug.LogError("Assign the loading screen prefab in Assets/_Project/Resources/UI/LoadingScreenSettings.asset.");
                    return;
                }
                s_instance = Instantiate(prefab);
            }
            s_instance.m_status.text = initialStatus;
            s_instance.gameObject.SetActive(true);
            if (!s_instance.m_tracking)
            {
                s_instance.m_tracking = true;
                s_instance.StartCoroutine(s_instance.TrackReadiness());
            }
        }

        private void Awake()
        {
            if (!Application.isPlaying) return;
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            DontDestroyOnLoad(gameObject);
            m_tip.text = Tips[Random.Range(0, Tips.Length)];
            m_returnButton.onClick.AddListener(ReturnToMenu);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        private void Update()
        {
            if (m_spinner != null) m_spinner.Rotate(0f, 0f, -150f * Time.unscaledDeltaTime);
            m_visualProgress = Mathf.MoveTowards(m_visualProgress, m_displayedProgress, Time.unscaledDeltaTime * 0.8f);
            if (m_fill != null)
            {
                m_fill.rectTransform.sizeDelta = new Vector2(808f * m_visualProgress, 30f);
                m_progressPercent.text = $"{Mathf.RoundToInt(m_visualProgress * 100f)}%";
            }
        }

        private IEnumerator TrackReadiness()
        {
            // Steam lobby data and FishNet scene messages arrive independently.
            // Poll scene state so showing this UI after sceneLoaded is also supported.
            float deadline = Time.realtimeSinceStartup + 120f;
            while (!SceneManager.GetSceneByName("SampleScene").isLoaded && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!SceneManager.GetSceneByName("SampleScene").isLoaded)
            { Fail("The gameplay scene did not finish loading."); yield break; }

            m_displayedProgress = 0.12f;
            deadline = Time.realtimeSinceStartup + 120f;
            NeighbourhoodGenerator generator = null;
            while (generator == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (NeighbourhoodGenerator candidate in FindObjectsByType<NeighbourhoodGenerator>(FindObjectsSortMode.None))
                    if (candidate.gameObject.scene.name == "SampleScene") { generator = candidate; break; }
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
            deadline = Time.realtimeSinceStartup + 120f;
            while (!HasLocalPlayer() && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!HasLocalPlayer()) { Fail("Player spawning timed out."); yield break; }

            if (GameLaunchContext.Mode == GameLaunchMode.Multiplayer)
            {
                SteamLobbyController lobby = FindFirstObjectByType<SteamLobbyController>();
                if (lobby == null) { Fail("The multiplayer session was lost while loading."); yield break; }
                lobby.SetLocalPlayerReady();
                m_status.text = "Waiting for other players...";
                deadline = Time.realtimeSinceStartup + 120f;
                while (!lobby.IsGamePlaying && Time.realtimeSinceStartup < deadline)
                {
                    if (lobby.IsLocalLobbyOwner && lobby.AreAllLobbyMembersReady()) lobby.ReleasePlayers();
                    if (!lobby.IsInLobby) { Fail("Disconnected while loading."); yield break; }
                    yield return null;
                }
                if (!lobby.IsGamePlaying)
                {
                    Fail("Not all players finished loading the session."); yield break;
                }
            }

            m_displayedProgress = 1f;
            m_status.text = "Ready!";
            while (m_visualProgress < .995f) yield return null;
            yield return new WaitForSecondsRealtime(.35f);
            Finish();
        }

        private void Finish()
        {
            Destroy(gameObject);
            MainMenuMusic.FadeOutForGameplay();
        }

        private static bool HasLocalPlayer()
        {
            foreach (PlayerInputReader input in FindObjectsByType<PlayerInputReader>(FindObjectsSortMode.None))
            {
                NetworkObject networkObject = input.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsOwner) return true;
            }
            return false;
        }

        private void Fail(string message)
        {
            Debug.LogError(message);
            m_status.text = message;
            m_returnButton.gameObject.SetActive(true);
        }

        private void ReturnToMenu()
        {
            FindFirstObjectByType<SteamLobbyController>()?.LeaveLobby();
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            Destroy(gameObject);
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
