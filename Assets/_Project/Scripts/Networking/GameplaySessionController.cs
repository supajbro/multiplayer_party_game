using System.Collections;
using System.Collections.Generic;
using CouchGuys.Player;
using CouchGuys.UI;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace CouchGuys.Networking
{
    public struct SessionStateMessage : IBroadcast
    {
        public bool GameOver;
        public bool Restarting;
        public int ReadyCount;
        public int PlayerCount;
        public bool LocalReady;
        public uint Round;
    }

    public struct RetryVoteMessage : IBroadcast { public uint Round; }

    /// <summary>Lives with the existing persistent network bootstrap, including solo hosts.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class GameplaySessionController : MonoBehaviour
    {
        [SerializeField] private SessionMenuView m_pauseMenuPrefab;
        [SerializeField] private SessionMenuView m_gameOverMenuPrefab;
        private NetworkManager m_network;
        private NeighbourhoodPlayerSpawner m_spawner;
        private readonly HashSet<int> m_votes = new();
        private SessionMenuView m_pauseMenu;
        private SessionMenuView m_gameOverMenu;
        private SessionStateMessage m_localState;
        private bool m_paused;
        private bool m_gameOver;
        private bool m_restarting;
        private bool m_exiting;
        private uint m_round;
        private float m_nextPublish;
        private CursorLockMode m_previousCursorLock;
        private bool m_previousCursorVisible;
        private bool m_hadInputBlock;

        public static GameplaySessionController Instance { get; private set; }
        public static bool BlocksLocalInput => Instance != null &&
            (Instance.m_paused || Instance.m_localState.GameOver || Instance.m_localState.Restarting || Instance.m_exiting);
        public static bool IsServerGameOver => Instance != null && (Instance.m_gameOver || Instance.m_restarting);

        private void Awake()
        {
            Instance = this;
            m_network = GetComponent<NetworkManager>();
            m_spawner = GetComponent<NeighbourhoodPlayerSpawner>();
            m_network.ServerManager.RegisterBroadcast<RetryVoteMessage>(OnRetryVote);
            m_network.ClientManager.RegisterBroadcast<SessionStateMessage>(OnState);
            m_network.ClientManager.OnClientConnectionState += OnClientConnection;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Update()
        {
            if (m_network.ServerManager.Started && !m_restarting &&
                SceneManager.GetSceneByName("SampleScene").isLoaded)
            {
                EvaluateGameOverServer();
                m_votes.RemoveWhere(id => !m_network.ServerManager.Clients.TryGetValue(id, out var c) || !c.IsActive);
                if (m_gameOver && ActiveConnectionCount() > 0 &&
                    m_votes.Count == ActiveConnectionCount()) StartCoroutine(RestartServer());
                if (Time.unscaledTime >= m_nextPublish)
                {
                    m_nextPublish = Time.unscaledTime + .2f;
                    PublishState();
                }
            }

            if (HasLocalPlayer() && !m_localState.GameOver && !m_localState.Restarting &&
                (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)) SetPaused(!m_paused);
            UpdateCursor();
        }

        // Called immediately by lethal damage, before any player's respawn update can run.
        public void EvaluateGameOverServer()
        {
            if (!m_network.ServerManager.Started || m_gameOver || m_restarting) return;
            int players = 0;
            foreach (PlayerHealth health in PlayerHealth.Instances)
            {
                if (health == null || !health.IsServerInitialized || health.IsAiTeammate ||
                    !health.Owner.IsValid || !health.Owner.IsActive) continue;
                players++;
                if (health.IsAlive || health.HasPendingRespawn) return;
            }
            // A connected player still loading/spawning is not a dead player.
            if (players == 0 || players < ActiveConnectionCount()) return;
            m_gameOver = true;
            m_votes.Clear();
            PublishState();
        }

        private int ActiveConnectionCount()
        {
            int count = 0;
            foreach (var connection in m_network.ServerManager.Clients.Values)
                if (connection.IsActive && connection.IsAuthenticated) count++;
            return count;
        }

        private void OnRetryVote(NetworkConnection connection, RetryVoteMessage message, Channel channel)
        {
            if (!m_gameOver || m_restarting || message.Round != m_round || !connection.IsActive) return;
            m_votes.Add(connection.ClientId);
            PublishState();
        }

        private void PublishState()
        {
            foreach (NetworkConnection connection in m_network.ServerManager.Clients.Values)
            {
                if (!connection.IsActive || !connection.IsAuthenticated) continue;
                m_network.ServerManager.Broadcast(connection, new SessionStateMessage
                {
                    GameOver = m_gameOver, Restarting = m_restarting, Round = m_round,
                    ReadyCount = m_votes.Count, PlayerCount = ActiveConnectionCount(),
                    LocalReady = m_votes.Contains(connection.ClientId)
                });
            }
        }

        private void OnState(SessionStateMessage state, Channel channel)
        {
            if (state.Restarting && !m_localState.Restarting)
                FindFirstObjectByType<SteamLobbyController>()?.BeginRetryLoading();
            m_round = state.Round;
            m_localState = state;
            if (state.GameOver || state.Restarting) SetPaused(false);
            if (state.GameOver && !state.Restarting && HasLocalPlayer())
            {
                if (m_gameOverMenu == null)
                {
                    m_gameOverMenu = CreateMenu(m_gameOverMenuPrefab);
                    if (m_gameOverMenu == null) return;
                    m_gameOverMenu.PrimaryButton.onClick.AddListener(VoteRetry);
                }
                m_gameOverMenu.gameObject.SetActive(true);
                m_gameOverMenu.PrimaryButton.interactable = !state.LocalReady;
                m_gameOverMenu.ProgressLabel.text = $"{state.ReadyCount}/{state.PlayerCount} Players Ready";
                m_gameOverMenu.LocalReadyLabel.text = state.LocalReady ? "You are ready" : "You are not ready";
            }
            else if (m_gameOverMenu != null) m_gameOverMenu.gameObject.SetActive(false);
        }

        private SessionMenuView CreateMenu(SessionMenuView prefab)
        {
            if (prefab == null) { Debug.LogError("Session menu prefab is not assigned.", this); return null; }
            SessionMenuView menu = Instantiate(prefab, transform);
            menu.ExitButton.onClick.AddListener(ExitGame);
            return menu;
        }

        private void SetPaused(bool paused)
        {
            m_paused = paused;
            if (paused && m_pauseMenu == null)
            {
                m_pauseMenu = CreateMenu(m_pauseMenuPrefab);
                if (m_pauseMenu == null) { m_paused = false; return; }
                m_pauseMenu.PrimaryButton.onClick.AddListener(() => SetPaused(false));
            }
            if (m_pauseMenu != null) m_pauseMenu.gameObject.SetActive(paused);
        }

        private void VoteRetry()
        {
            if (m_localState.GameOver && !m_localState.LocalReady && !m_localState.Restarting)
                m_network.ClientManager.Broadcast(new RetryVoteMessage { Round = m_round });
        }

        private IEnumerator RestartServer()
        {
            m_restarting = true;
            PublishState();
            // FishNet's ReloadScenes option is explicitly unimplemented in this version.
            // Replace through an empty scene so every scene-owned gameplay object is reset.
            m_spawner.ResetForRetry();
            bool transitionLoaded = false;
            void OnLoadEnd(SceneLoadEndEventArgs args)
            {
                if (!args.QueueData.AsServer) return;
                foreach (var scene in args.LoadedScenes)
                    if (scene.name == "RetryTransition") transitionLoaded = true;
            }
            m_network.SceneManager.OnLoadEnd += OnLoadEnd;
            m_network.SceneManager.LoadGlobalScenes(new SceneLoadData("RetryTransition") { ReplaceScenes = ReplaceOption.All });
            while (!transitionLoaded && m_network.ServerManager.Started) yield return null;
            m_network.SceneManager.OnLoadEnd -= OnLoadEnd;
            if (!m_network.ServerManager.Started) yield break;
            m_votes.Clear();
            m_gameOver = false;
            m_restarting = false;
            m_round++;
            FindFirstObjectByType<SteamLobbyController>()?.PrepareRetry();
            PublishState();
            m_network.SceneManager.LoadGlobalScenes(new SceneLoadData("SampleScene") { ReplaceScenes = ReplaceOption.All });
            // The spawner waits for each connection's scene presence and generation readiness.
            foreach (var connection in m_network.ServerManager.Clients.Values) m_spawner.SpawnForRetry(connection);
        }

        private bool HasLocalPlayer()
        {
            foreach (var health in PlayerHealth.Instances)
                if (health != null && health.IsClientInitialized && health.IsOwner && !health.IsAiTeammate) return true;
            return false;
        }

        private void UpdateCursor()
        {
            bool blocked = BlocksLocalInput;
            if (blocked && !m_hadInputBlock)
            {
                PlayerDebugMenu.CloseActive();
                m_previousCursorLock = Cursor.lockState;
                m_previousCursorVisible = Cursor.visible;
            }
            if (blocked) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else if (m_hadInputBlock)
            {
                Cursor.lockState = m_previousCursorLock;
                Cursor.visible = m_previousCursorVisible;
            }
            m_hadInputBlock = blocked;
        }

        private void LateUpdate() { if (BlocksLocalInput) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SetPaused(false);
            if (m_gameOverMenu != null) m_gameOverMenu.gameObject.SetActive(false);
            if (scene.name == "SampleScene" && GameLaunchContext.UseLoadingFlow)
                LoadingScreenController.Show("Building Neighbourhood...");
        }

        private void OnClientConnection(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            SetPaused(false);
            m_localState = default;
            m_gameOver = m_restarting = false;
            m_votes.Clear();
            if (m_gameOverMenu != null) m_gameOverMenu.gameObject.SetActive(false);
            if (!m_exiting && (SceneManager.GetSceneByName("SampleScene").isLoaded ||
                SceneManager.GetSceneByName("RetryTransition").isLoaded)) ExitGame();
        }

        public void ExitGame()
        {
            if (m_exiting) return;
            m_exiting = true;
            StartCoroutine(ExitRoutine());
        }

        private IEnumerator ExitRoutine()
        {
            var lobby = FindFirstObjectByType<SteamLobbyController>();
            if (lobby != null) lobby.LeaveLobby();
            else
            {
                m_network.ClientManager.StopConnection();
                if (m_network.ServerManager.Started) m_network.ServerManager.StopConnection(true);
            }
            var loading = FindFirstObjectByType<LoadingScreenController>();
            if (loading != null) Destroy(loading.gameObject);
            // Remove the persistent bootstrap, including solo's UDP transport. The main menu
            // installs its normal Steam bootstrap on the next frame.
            yield return null;
            Destroy(m_network.gameObject);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (m_network == null) return;
            m_network.ServerManager.UnregisterBroadcast<RetryVoteMessage>(OnRetryVote);
            m_network.ClientManager.UnregisterBroadcast<SessionStateMessage>(OnState);
            m_network.ClientManager.OnClientConnectionState -= OnClientConnection;
        }
    }
}
