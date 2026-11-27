using System.Collections;
using System.Collections.Generic;
using CouchGuys.Networking;
using Steamworks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    /// <summary>Colourful main-menu front end for the persistent Steam lobby service.</summary>
    [DisallowMultipleComponent]
    public sealed class MultiplayerMenuController : MonoBehaviour
    {
        private SteamLobbyController m_lobby;
        [SerializeField] private CanvasGroup m_panel;
        [SerializeField] private Vector2 m_slideOffset = new Vector2(-220f, 0f);
        [SerializeField, Min(0f)] private float m_slideDuration = 0.4f;
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
        private MainMenuIntro m_menuIntro;
        private GameObject m_currentPage;
        private Vector2 m_panelPosition;
        private bool m_transitioning;
        private bool m_lobbyOpenPending;

        private void Awake()
        {
            if (m_panel != null) m_panelPosition = ((RectTransform)m_panel.transform).anchoredPosition;
        }

        private void Start() => StartCoroutine(BindLobby());

        public void Initialise(MainMenuIntro menuIntro, params Button[] mainButtons)
        {
            m_menuIntro = menuIntro;
            m_mainButtons = mainButtons;
        }

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
            if (mainButtons != null && mainButtons.Length > 0) m_mainButtons = mainButtons;
            TryOpen(m_homePage);
        }

        private bool TryOpen(GameObject page)
        {
            if (m_panel == null || page == null || m_transitioning || m_panel.gameObject.activeSelf) return false;
            if (m_menuIntro != null && !m_menuIntro.TryBeginMultiplayer()) return false;
            m_lobbyOpenPending = false;
            StartCoroutine(OpenPanel(page));
            return true;
        }

        private IEnumerator OpenPanel(GameObject page)
        {
            m_transitioning = true;
            EventSystem.current?.SetSelectedGameObject(null);
            if (m_menuIntro != null) yield return m_menuIntro.SlideMainMenu(false);
            else SetLegacyMainButtonsVisible(false);

            SetPanelInput(false);
            SamplePanel(0f);
            ShowPage(m_lobby != null && m_lobby.IsInLobby ? m_lobbyPage : page);
            m_panel.gameObject.SetActive(true);
            if (m_currentPage == m_browserPage) Refresh();
            yield return SlidePanel(true);
            SetPanelInput(true);
            m_transitioning = false;
            SelectCurrentPage();
        }

        private void Update()
        {
            if (LoadingScreenController.BlocksGameplayInput || m_panel == null || m_transitioning) return;
            if (!m_panel.gameObject.activeSelf)
            {
                if (m_lobbyOpenPending && (m_lobby == null || !m_lobby.IsInLobby || TryOpen(m_lobbyPage)))
                    m_lobbyOpenPending = false;
                return;
            }
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
            if (m_lobby == null || m_panel == null) return;
            m_status.text = m_lobby.Status;
            m_lobbyOpenPending = m_lobby.IsInLobby && !m_panel.gameObject.activeSelf && !m_transitioning;
            if (m_lobby.IsInLobby && m_panel.gameObject.activeSelf) ShowPage(m_lobbyPage);
        }

        public void OpenHost() => TryOpen(m_hostPage);
        public void OpenJoin() => TryOpen(m_browserPage);
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
            if (m_transitioning || m_panel == null || !m_panel.gameObject.activeSelf) return;
            StartCoroutine(ClosePanel());
        }

        private IEnumerator ClosePanel()
        {
            m_transitioning = true;
            SetPanelInput(false);
            EventSystem.current?.SetSelectedGameObject(null);
            yield return SlidePanel(false);
            m_panel.gameObject.SetActive(false);

            if (m_menuIntro != null)
            {
                yield return m_menuIntro.SlideMainMenu(true);
                m_menuIntro.FinishMultiplayer();
            }
            else
            {
                SetLegacyMainButtonsVisible(true);
                EventSystem.current?.SetSelectedGameObject(m_mainButtons is { Length: > 0 } ? m_mainButtons[0]?.gameObject : null);
            }
            m_transitioning = false;
        }

        private IEnumerator SlidePanel(bool visible)
        {
            for (float elapsed = 0f; elapsed < m_slideDuration; elapsed += Time.unscaledDeltaTime)
            {
                float progress = elapsed / m_slideDuration;
                SamplePanel(visible ? progress : 1f - progress);
                yield return null;
            }
            SamplePanel(visible ? 1f : 0f);
        }

        private void SamplePanel(float progress)
        {
            float blend = Mathf.SmoothStep(0f, 1f, progress);
            ((RectTransform)m_panel.transform).anchoredPosition = m_panelPosition + m_slideOffset * (1f - blend);
            m_panel.alpha = blend;
        }

        private void SetPanelInput(bool enabled)
        {
            m_panel.interactable = enabled;
            m_panel.blocksRaycasts = enabled;
        }

        private void SetLegacyMainButtonsVisible(bool visible)
        {
            if (m_mainButtons == null) return;
            foreach (Button button in m_mainButtons)
                if (button != null) button.gameObject.SetActive(visible);
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
            if (page == null || page == m_currentPage) return;
            m_currentPage = page;
            if (m_homePage != null) m_homePage.SetActive(page == m_homePage);
            if (m_hostPage != null) m_hostPage.SetActive(page == m_hostPage);
            if (m_browserPage != null) m_browserPage.SetActive(page == m_browserPage);
            if (m_lobbyPage != null) m_lobbyPage.SetActive(page == m_lobbyPage);
            if (!m_transitioning) SelectCurrentPage();
        }

        private void SelectCurrentPage()
        {
            var first = m_currentPage.GetComponentInChildren<Selectable>();
            EventSystem.current?.SetSelectedGameObject(first != null ? first.gameObject : null);
        }
    }
}
