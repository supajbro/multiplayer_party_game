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

        public static void EnterMainMenu() => Reset();
        public static void BeginSolo()
        {
            Mode = GameLaunchMode.Solo;
            AuthoritativeSeed = Random.Range(1, int.MaxValue);
        }

        public static void BeginMultiplayer(int seed)
        {
            Mode = GameLaunchMode.Multiplayer;
            if (seed != 0) AuthoritativeSeed = seed;
        }
    }

    /// <summary>Controls the scene presented while the neighbourhood and local player get ready.</summary>
    [DisallowMultipleComponent]
    public sealed class LoadingScreenController : MonoBehaviour
    {
        private const string SceneName = "LoadingScreen";
        private const string MenuSceneName = "MainMenu";
        private const string GameplaySceneName = "SampleScene";
        private const float ReadinessTimeout = 120f;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int BarSizeId = Shader.PropertyToID("_BarSize");
        private static readonly int AnimationTimeId = Shader.PropertyToID("_AnimationTime");

        private static LoadingScreenController s_instance;
        private static AsyncOperation s_opening;

        [SerializeField] private Camera m_loadingCamera;
        [SerializeField] private CanvasGroup m_background;
        [SerializeField] private FuturisticMonowheelRobotSpriteSequence m_robot;
        [SerializeField] private CanvasGroup m_progressTrack;
        [SerializeField] private Image m_fill;
        [SerializeField, Min(0f)] private float m_menuFadeDuration = .45f;
        [SerializeField, Min(0f)] private float m_progressFadeDuration = .3f;
        [SerializeField, Min(0f)] private float m_completedHoldDuration = .35f;

        private Material m_fillMaterial;
        private float m_targetProgress;
        private float m_visualProgress;
        private bool m_entranceComplete;

        public static bool BlocksGameplayInput => s_instance != null || (s_opening != null && !s_opening.isDone);

        public static void Show()
        {
            if (BlocksGameplayInput) return;
            if (!Application.CanStreamedLevelBeLoaded(SceneName))
            {
                Debug.LogError("Enable the LoadingScreen scene in Build Settings.");
                return;
            }

            s_opening = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
        }

        public static IEnumerator WaitForEntrance()
        {
            Show();
            while (BlocksGameplayInput && (s_instance == null || !s_instance.m_entranceComplete))
                yield return null;
        }

        private void Awake()
        {
            s_instance = this;
            m_loadingCamera.enabled = !SceneManager.GetSceneByName(MenuSceneName).isLoaded;
            m_background.alpha = m_loadingCamera.enabled ? 1f : 0f;
            m_progressTrack.alpha = 0f;
            m_robot.PrepareEntrance();
            m_fillMaterial = new Material(m_fill.material);
            m_fill.material = m_fillMaterial;
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (m_fillMaterial != null) Destroy(m_fillMaterial);
        }

        private IEnumerator Start()
        {
            Scene menu = SceneManager.GetSceneByName(MenuSceneName);
            if (menu.isLoaded)
            {
                var events = UnityEngine.EventSystems.EventSystem.current;
                if (events != null)
                {
                    events.SetSelectedGameObject(null);
                    events.sendNavigationEvents = false;
                }

                for (float elapsed = 0f; elapsed < m_menuFadeDuration; elapsed += Time.unscaledDeltaTime)
                {
                    m_background.alpha = Mathf.SmoothStep(0f, 1f, elapsed / m_menuFadeDuration);
                    yield return null;
                }

                m_background.alpha = 1f;
                m_loadingCamera.enabled = true;
                if (SceneManager.GetActiveScene() == menu)
                    SceneManager.SetActiveScene(gameObject.scene);
                if (menu.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(menu);
            }

            yield return m_robot.Enter();
            for (float elapsed = 0f; elapsed < m_progressFadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                m_progressTrack.alpha = Mathf.SmoothStep(0f, 1f, elapsed / m_progressFadeDuration);
                yield return null;
            }

            m_progressTrack.alpha = 1f;
            m_entranceComplete = true;
            if (GameLaunchContext.UseLoadingFlow)
                yield return TrackReadiness();
        }

        private void Update()
        {
            m_visualProgress = Mathf.MoveTowards(m_visualProgress, m_targetProgress, Time.unscaledDeltaTime * .8f);
            Rect rect = m_fill.rectTransform.rect;
            m_fillMaterial.SetFloat(ProgressId, m_visualProgress);
            m_fillMaterial.SetVector(BarSizeId, new Vector4(rect.width, rect.height, 0f, 0f));
            m_fillMaterial.SetFloat(AnimationTimeId, Time.unscaledTime);
        }

        private IEnumerator TrackReadiness()
        {
            float deadline = Time.realtimeSinceStartup + ReadinessTimeout;
            Scene gameplay = SceneManager.GetSceneByName(GameplaySceneName);
            while (!gameplay.isLoaded && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                gameplay = SceneManager.GetSceneByName(GameplaySceneName);
            }
            if (!gameplay.isLoaded) { Fail("The gameplay scene did not finish loading."); yield break; }

            m_targetProgress = .12f;
            deadline = Time.realtimeSinceStartup + ReadinessTimeout;
            NeighbourhoodGenerator generator = null;
            while (generator == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (NeighbourhoodGenerator candidate in FindObjectsByType<NeighbourhoodGenerator>(FindObjectsSortMode.None))
                {
                    if (candidate.gameObject.scene != gameplay) continue;
                    generator = candidate;
                    break;
                }
                if (generator == null) yield return null;
            }

            if (generator == null) { Fail("Could not find the neighbourhood generator."); yield break; }
            while (!generator.IsGenerationReady && Time.realtimeSinceStartup < deadline)
            {
                m_targetProgress = .12f + Mathf.Clamp01(generator.GenerationProgress) * .76f;
                yield return null;
            }
            if (!generator.IsGenerationReady) { Fail("Neighbourhood generation timed out."); yield break; }

            m_targetProgress = .9f;
            deadline = Time.realtimeSinceStartup + ReadinessTimeout;
            while (!HasLocalPlayer() && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!HasLocalPlayer()) { Fail("Player spawning timed out."); yield break; }

            if (GameLaunchContext.Mode == GameLaunchMode.Multiplayer)
            {
                SteamLobbyController lobby = FindFirstObjectByType<SteamLobbyController>();
                if (lobby == null) { Fail("The multiplayer session was lost while loading."); yield break; }
                lobby.SetLocalPlayerReady();
                deadline = Time.realtimeSinceStartup + ReadinessTimeout;
                while (lobby != null && lobby.IsInLobby && !lobby.IsGamePlaying && Time.realtimeSinceStartup < deadline)
                {
                    if (lobby.IsLocalLobbyOwner && lobby.AreAllLobbyMembersReady()) lobby.ReleasePlayers();
                    yield return null;
                }
                if (lobby == null || !lobby.IsInLobby) { Fail("Disconnected while loading."); yield break; }
                if (!lobby.IsGamePlaying) { Fail("Not all players finished loading the session."); yield break; }
            }

            m_targetProgress = 1f;
            while (m_visualProgress < 1f) yield return null;
            yield return new WaitForSecondsRealtime(m_completedHoldDuration);
            yield return m_robot.Exit();

            MainMenuMusic.FadeOutForGameplay();
            SceneManager.UnloadSceneAsync(gameObject.scene);
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

        private static void Fail(string message)
        {
            Debug.LogError(message);
            if (GameplaySessionController.Instance != null)
                GameplaySessionController.Instance.ExitGame();
            else
                SceneManager.LoadSceneAsync(MenuSceneName, LoadSceneMode.Single);
        }
    }
}
