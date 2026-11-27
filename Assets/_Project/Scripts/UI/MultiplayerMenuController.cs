using System.Collections;
using System.Collections.Generic;
using CouchGuys.Networking;
using Steamworks;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    /// <summary>Colourful main-menu front end for the persistent Steam lobby service.</summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerMenuController : MonoBehaviour
    {
        private SteamLobbyController m_lobby;
        [SerializeField] private GameObject m_root;
        [SerializeField] private GameObject m_homePage;
        [SerializeField] private GameObject m_hostPage;
        [SerializeField] private GameObject m_browserPage;
        [SerializeField] private GameObject m_lobbyPage;
        [SerializeField] private Transform m_browserRows;
        [SerializeField] private Text m_lobbyPlayers;
        [SerializeField] private Text m_status;
        [SerializeField] private Toggle m_publicToggle;
        [SerializeField] private Button m_playerLimitButton;
        [SerializeField] private Text m_playerLimitLabel;
        private int m_maximumPlayers = 4;
        [SerializeField] private Button m_startButton;
        private Button[] m_mainButtons;
        [SerializeField] private Button m_browserRowPrefab;
        [SerializeField] private Text m_emptyBrowserLabel;
        private float m_nextRefresh;

        private void Start() => StartCoroutine(BindLobby());

        public void InitialiseMainButtons(params Button[] mainButtons) => m_mainButtons = mainButtons;

        private IEnumerator BindLobby()
        {
            while (m_lobby == null)
            {
                m_lobby = FindFirstObjectByType<SteamLobbyController>();
                yield return null;
            }
            m_lobby.StateChanged += OnLobbyStateChanged;
            m_lobby.LobbiesChanged += RebuildBrowser;
            OnLobbyStateChanged();
        }

        private void OnDestroy()
        {
            if (m_lobby == null) return;
            m_lobby.StateChanged -= OnLobbyStateChanged;
            m_lobby.LobbiesChanged -= RebuildBrowser;
        }

        public void Open(params Button[] mainButtons)
        {
            if (m_root == null) { Debug.LogError("Assign the saved multiplayer menu view.", this); return; }
            if (mainButtons != null && mainButtons.Length > 0) m_mainButtons = mainButtons;
            if (m_mainButtons != null) foreach (Button button in m_mainButtons) if (button != null) button.gameObject.SetActive(false);
            m_root.SetActive(true);
            ShowPage(m_lobby != null && m_lobby.IsInLobby ? m_lobbyPage : m_homePage);
        }

        private void Update()
        {
            if (LoadingScreenController.BlocksGameplayInput || m_root == null || !m_root.activeSelf) return;
            if ((UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) ||
                (UnityEngine.InputSystem.Gamepad.current?.buttonEast.wasPressedThisFrame ?? false))
            {
                if (m_lobbyPage.activeSelf) LeaveLobby(); else Close();
                return;
            }
            if (m_lobby == null || Time.unscaledTime < m_nextRefresh) return;
            m_nextRefresh = Time.unscaledTime + .35f;
            m_status.text = m_lobby.Status;
            if (m_lobby.IsInLobby)
            {
                ShowPage(m_lobbyPage);
                IReadOnlyList<string> names = m_lobby.GetLobbyMemberNames();
                m_lobbyPlayers.text = "PLAYERS\n\n" + string.Join("\n", names);
                m_startButton.gameObject.SetActive(m_lobby.IsLocalLobbyOwner);
                m_startButton.interactable = names.Count > 0;
            }
        }

        private void OnLobbyStateChanged()
        {
            if (m_lobby == null) return;
            if (m_root == null) return;
            if (!m_root.activeSelf && m_lobby.IsInLobby) Open();
            m_status.text = m_lobby.Status;
            if (m_lobby.IsInLobby) ShowPage(m_lobbyPage);
        }

        public void OpenHost() { Open(); if (m_lobby == null || !m_lobby.IsInLobby) ShowPage(m_hostPage); }
        public void OpenJoin() { Open(); if (m_lobby == null || !m_lobby.IsInLobby) OpenBrowser(); }
        public void ShowHome() => ShowPage(m_homePage);
        public void ShowHost() => ShowPage(m_hostPage);
        public void InviteFriends() => m_lobby?.InviteFriends();
        public void StartGame() => m_lobby?.StartGame();

        public void Host() => m_lobby?.HostLobby(m_publicToggle.isOn, m_maximumPlayers);
        public void CyclePlayerLimit()
        {
            m_maximumPlayers = m_maximumPlayers >= 4 ? 2 : m_maximumPlayers + 1;
            m_playerLimitLabel.text = $"Maximum Players: {m_maximumPlayers}";
        }
        public void OpenBrowser() { ShowPage(m_browserPage); Refresh(); }
        public void Refresh() => m_lobby?.RefreshLobbies();
        public void LeaveLobby() { m_lobby?.LeaveLobby(); ShowPage(m_homePage); }

        public void Close()
        {
            m_root.SetActive(false);
            if (m_mainButtons != null) foreach (Button button in m_mainButtons) if (button != null) button.gameObject.SetActive(true);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(m_mainButtons != null && m_mainButtons.Length > 0 ? m_mainButtons[0]?.gameObject : null);
        }

        private void RebuildBrowser()
        {
            if (m_browserRows == null || m_lobby == null) return;
            for (int index = m_browserRows.childCount - 1; index >= 0; index--) Destroy(m_browserRows.GetChild(index).gameObject);
            foreach (CSteamID lobbyId in m_lobby.DiscoveredLobbies)
            {
                int current = m_lobby.GetLobbyMemberCount(lobbyId);
                int maximum = m_lobby.GetLobbyMemberLimit(lobbyId);
                string label = $"{m_lobby.GetLobbyHostName(lobbyId)}     {current}/{maximum}     JOIN";
                Button row = Instantiate(m_browserRowPrefab, m_browserRows);
                row.gameObject.SetActive(true);
                row.GetComponentInChildren<Text>().text = label;
                row.onClick.AddListener(() => m_lobby.JoinLobby(lobbyId));
                row.GetComponent<RectTransform>().sizeDelta = new Vector2(760f, 72f);
                row.interactable = maximum > 0 && current < maximum;
            }
            if (m_emptyBrowserLabel != null) m_emptyBrowserLabel.gameObject.SetActive(m_lobby.DiscoveredLobbies.Count == 0);
        }

        private void ShowPage(GameObject page)
        {
            if (page == null) return;
            if (page.activeSelf) return;
            if (m_homePage != null) m_homePage.SetActive(page == m_homePage);
            if (m_hostPage != null) m_hostPage.SetActive(page == m_hostPage);
            if (m_browserPage != null) m_browserPage.SetActive(page == m_browserPage);
            if (m_lobbyPage != null) m_lobbyPage.SetActive(page == m_lobbyPage);
            var first = page.GetComponentInChildren<Selectable>();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(first != null ? first.gameObject : null);
        }

    }
}
