using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class SplashScreenController : MonoBehaviour
    {
        [SerializeField] private VideoPlayer m_videoPlayer;
        [SerializeField, Min(0f), Tooltip("Seconds to hold the final video frame before loading the main menu. Uses real time.")]
        private float m_postVideoDelay = 1f;
        [SerializeField, Tooltip("Scene name or full scene path included in Build Settings.")]
        private string m_mainMenuScene = "Assets/Scenes/MainMenu.unity";

        private bool m_transitionStarted;

        private void OnEnable()
        {
            CouchGuys.Networking.SteamLobbyDebugInterface.IsVisible = false;
            m_transitionStarted = false;
            if (m_videoPlayer == null) m_videoPlayer = GetComponent<VideoPlayer>();
            m_videoPlayer.prepareCompleted += OnPrepared;
            m_videoPlayer.loopPointReached += OnFinished;
            m_videoPlayer.errorReceived += OnVideoError;
        }

        private void Start()
        {
            m_videoPlayer.playOnAwake = false;
            m_videoPlayer.isLooping = false;
            if (m_videoPlayer.source == VideoSource.VideoClip && m_videoPlayer.clip == null)
            {
                OnVideoError(m_videoPlayer, "No intro video is assigned.");
                return;
            }
            m_videoPlayer.Prepare();
        }

        private void OnPrepared(VideoPlayer player) => player.Play();

        private void OnFinished(VideoPlayer player)
        {
            if (m_transitionStarted) return;
            m_transitionStarted = true;
            // Pause rather than Stop so the last frame stays visible throughout the delay.
            player.Pause();
            StartCoroutine(LoadMainMenu());
        }

        private void OnVideoError(VideoPlayer player, string message)
        {
            Debug.LogWarning($"Splash video could not play: {message}. Continuing to the main menu.", this);
            OnFinished(player);
        }

        private IEnumerator LoadMainMenu()
        {
            if (m_postVideoDelay > 0f)
                yield return new WaitForSecondsRealtime(m_postVideoDelay);

            if (string.IsNullOrWhiteSpace(m_mainMenuScene) || !Application.CanStreamedLevelBeLoaded(m_mainMenuScene))
            {
                Debug.LogError($"Splash destination '{m_mainMenuScene}' is not an enabled build scene.", this);
                yield break;
            }
            SceneManager.LoadSceneAsync(m_mainMenuScene, LoadSceneMode.Single);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (m_videoPlayer == null) return;
            m_videoPlayer.prepareCompleted -= OnPrepared;
            m_videoPlayer.loopPointReached -= OnFinished;
            m_videoPlayer.errorReceived -= OnVideoError;
        }
    }
}
