using System.Collections;
using CouchGuys.Networking;
using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuManager : MonoBehaviour
    {
        private const string GameplaySceneName = "SampleScene";

        [SerializeField] private Button m_soloButton;
        [SerializeField] private Button m_multiplayerButton;
        [SerializeField] private Button m_settingsButton;
        [SerializeField] private Sprite m_backgroundSprite;

        private bool m_launching;
        private MultiplayerMenuController m_multiplayerMenu;

        private void Awake()
        {
            InstallStaticBackground();
            GameLaunchContext.EnterMainMenu();
            SteamLobbyDebugInterface.IsVisible = false;
            m_multiplayerMenu = GetComponent<MultiplayerMenuController>();
            if (m_multiplayerMenu == null) m_multiplayerMenu = gameObject.AddComponent<MultiplayerMenuController>();
            m_multiplayerMenu.InitialiseMainButtons(m_soloButton, m_multiplayerButton, m_settingsButton);
            BindButtons();
        }

        private void InstallStaticBackground()
        {
            GameObject decorativeNeighbourhood = GameObject.Find("Decorative Neighbourhood");
            if (decorativeNeighbourhood != null) decorativeNeighbourhood.SetActive(false);
            if (m_backgroundSprite == null) return;

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null || canvas.transform.Find("Concept Background") != null) return;
            GameObject background = new GameObject("Concept Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvas.transform, false);
            background.transform.SetAsFirstSibling();
            RectTransform rect = background.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = background.GetComponent<Image>();
            image.sprite = m_backgroundSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        public void Initialise(Button soloButton, Button multiplayerButton, Button settingsButton)
        {
            UnbindButtons();
            m_soloButton = soloButton;
            m_multiplayerButton = multiplayerButton;
            m_settingsButton = settingsButton;
            BindButtons();
        }

        private void BindButtons()
        {
            m_soloButton?.onClick.AddListener(StartSolo);
            m_multiplayerButton?.onClick.AddListener(StartMultiplayer);
            // Settings is deliberately presented but unconnected for this milestone.
        }

        private void OnDestroy()
        {
            UnbindButtons();
            SceneManager.sceneLoaded -= OnSoloSceneLoaded;
        }

        private void UnbindButtons()
        {
            m_soloButton?.onClick.RemoveListener(StartSolo);
            m_multiplayerButton?.onClick.RemoveListener(StartMultiplayer);
        }

        public void StartSolo()
        {
            if (m_launching)
            {
                return;
            }

            m_launching = true;
            SetButtonsInteractable(false);
            SteamLobbyDebugInterface.IsVisible = false;
            GameLaunchContext.BeginSolo();
            LoadingScreenController.Show("Building Neighbourhood...");
            DontDestroyOnLoad(gameObject);
            StartCoroutine(BeginSoloLaunch());
        }

        public void StartMultiplayer()
        {
            SteamLobbyDebugInterface.IsVisible = false;
            m_multiplayerMenu.Open(m_soloButton, m_multiplayerButton, m_settingsButton);
        }

        private void OnSoloSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.name, GameplaySceneName, System.StringComparison.Ordinal))
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSoloSceneLoaded;
            StartCoroutine(StartLocalSession());
        }

        private IEnumerator BeginSoloLaunch()
        {
            // Replace the persistent Steam transport with the same FishNet setup using
            // its local UDP transport. This keeps solo independent from Steam entirely.
            NetworkManager multiplayerManager = InstanceFinder.NetworkManager;
            if (multiplayerManager != null)
            {
                Destroy(multiplayerManager.gameObject);
                yield return null;
            }

            GameObject soloBootstrap = Resources.Load<GameObject>("SoloNetworkBootstrap");
            if (soloBootstrap == null)
            {
                Debug.LogError("Solo network bootstrap resource is missing.", this);
                m_launching = false;
                SetButtonsInteractable(true);
                yield break;
            }

            Instantiate(soloBootstrap);
            yield return null;
            SceneManager.sceneLoaded += OnSoloSceneLoaded;
            SceneManager.LoadScene(GameplaySceneName, LoadSceneMode.Single);
        }

        private IEnumerator StartLocalSession()
        {
            // Let FishNet register the newly loaded gameplay scene before connecting.
            yield return null;

            NetworkManager networkManager = InstanceFinder.NetworkManager;
            if (networkManager == null)
            {
                Debug.LogError("Solo launch could not find the existing FishNet NetworkManager.", this);
                Destroy(gameObject);
                yield break;
            }

            bool serverStarted = networkManager.ServerManager.Started ||
                                 networkManager.ServerManager.StartConnection();
            bool clientStarted = serverStarted && (networkManager.ClientManager.Started ||
                                                    networkManager.ClientManager.StartConnection());
            if (!serverStarted || !clientStarted)
            {
                Debug.LogError("Solo launch could not start the local FishNet host.", this);
            }

            Destroy(gameObject);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (m_soloButton != null) m_soloButton.interactable = interactable;
            if (m_multiplayerButton != null) m_multiplayerButton.interactable = interactable;
            if (m_settingsButton != null) m_settingsButton.interactable = interactable;
        }
    }
}
