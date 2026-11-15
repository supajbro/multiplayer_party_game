using System.Collections;
using CouchGuys.Networking;
using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuManager : MonoBehaviour
    {
        private const string GameplaySceneName = "SampleScene";

        [SerializeField, InspectorName("Host Game Button")] private Button m_soloButton;
        [SerializeField, InspectorName("Join Game Button")] private Button m_multiplayerButton;
        [SerializeField] private Button m_settingsButton;
        [SerializeField] private Button m_customiseButton;
        [SerializeField] private GameObject m_settingsPanel;
        [SerializeField] private Slider m_volumeSlider;
        [SerializeField] private Button m_settingsBackButton;
        [SerializeField] private MultiplayerMenuController m_multiplayerMenu;

        private bool m_launching;

        private void Awake()
        {
            GameLaunchContext.EnterMainMenu();
            SteamLobbyDebugInterface.IsVisible = false;
            if (m_multiplayerMenu == null) m_multiplayerMenu = GetComponent<MultiplayerMenuController>();
            m_multiplayerMenu?.InitialiseMainButtons(m_soloButton, m_multiplayerButton, m_customiseButton, m_settingsButton);
            if (m_settingsPanel != null) m_settingsPanel.SetActive(false);
            if (m_volumeSlider != null)
            {
                AudioListener.volume = PlayerPrefs.GetFloat("MainMenu.MasterVolume", 1f);
                m_volumeSlider.SetValueWithoutNotify(AudioListener.volume);
                m_volumeSlider.onValueChanged.AddListener(SetVolume);
            }
            BindButtons();
        }

        private IEnumerator Start()
        {
            // Exit removes the old session (including solo's transport) at end of frame.
            yield return null;
            if (InstanceFinder.NetworkManager == null)
            {
                GameObject bootstrap = Resources.Load<GameObject>("SteamMultiplayerBootstrap");
                if (bootstrap != null) Instantiate(bootstrap);
            }
        }

        private void Update()
        {
            if (m_settingsPanel != null && m_settingsPanel.activeSelf &&
                ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) ||
                 (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false))) CloseSettings();
        }

        public void OpenSettings()
        {
            if (m_settingsPanel == null) return;
            SetButtonsInteractable(false);
            m_settingsPanel.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(m_volumeSlider != null ? m_volumeSlider.gameObject : m_settingsBackButton?.gameObject);
        }

        public void CloseSettings()
        {
            m_settingsPanel?.SetActive(false);
            SetButtonsInteractable(true);
            EventSystem.current?.SetSelectedGameObject(m_settingsButton?.gameObject);
            PlayerPrefs.Save();
        }

        public void SetVolume(float value)
        {
            AudioListener.volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat("MainMenu.MasterVolume", AudioListener.volume);
        }

        public void StartHost() => m_multiplayerMenu?.OpenHost();
        public void JoinGame() => m_multiplayerMenu?.OpenJoin();

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
            m_soloButton?.onClick.AddListener(StartHost);
            m_multiplayerButton?.onClick.AddListener(JoinGame);
            m_settingsButton?.onClick.AddListener(OpenSettings);
            m_settingsBackButton?.onClick.AddListener(CloseSettings);
        }

        private void OnDestroy()
        {
            UnbindButtons();
            if (m_volumeSlider != null) m_volumeSlider.onValueChanged.RemoveListener(SetVolume);
            SceneManager.sceneLoaded -= OnSoloSceneLoaded;
        }

        private void UnbindButtons()
        {
            m_soloButton?.onClick.RemoveListener(StartHost);
            m_multiplayerButton?.onClick.RemoveListener(JoinGame);
            m_settingsButton?.onClick.RemoveListener(OpenSettings);
            m_settingsBackButton?.onClick.RemoveListener(CloseSettings);
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
            m_multiplayerMenu?.Open(m_soloButton, m_multiplayerButton, m_customiseButton, m_settingsButton);
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
            if (m_customiseButton != null) m_customiseButton.interactable = interactable;
        }
    }
}
