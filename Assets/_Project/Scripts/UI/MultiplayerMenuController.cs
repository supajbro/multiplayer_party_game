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
        private GameObject m_root;
        private GameObject m_homePage;
        private GameObject m_hostPage;
        private GameObject m_browserPage;
        private GameObject m_lobbyPage;
        private Transform m_browserRows;
        private Text m_lobbyPlayers;
        private Text m_status;
        private Toggle m_publicToggle;
        private Button m_playerLimitButton;
        private Text m_playerLimitLabel;
        private int m_maximumPlayers = 4;
        private Button m_startButton;
        private Button[] m_mainButtons;
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
            m_lobby.GameStartRequested += OnGameStartRequested;
            OnLobbyStateChanged();
        }

        private void OnDestroy()
        {
            if (m_lobby == null) return;
            m_lobby.StateChanged -= OnLobbyStateChanged;
            m_lobby.LobbiesChanged -= RebuildBrowser;
            m_lobby.GameStartRequested -= OnGameStartRequested;
        }

        public void Open(params Button[] mainButtons)
        {
            if (mainButtons != null && mainButtons.Length > 0) m_mainButtons = mainButtons;
            foreach (Button button in m_mainButtons) if (button != null) button.gameObject.SetActive(false);
            if (m_root == null) BuildUi();
            m_root.SetActive(true);
            ShowPage(m_lobby != null && m_lobby.IsInLobby ? m_lobbyPage : m_homePage);
        }

        private void Update()
        {
            if (m_root == null || !m_root.activeSelf || m_lobby == null || Time.unscaledTime < m_nextRefresh) return;
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
            if (m_root == null && m_lobby.IsInLobby) Open();
            if (m_root == null) return;
            m_status.text = m_lobby.Status;
            if (m_lobby.IsInLobby) ShowPage(m_lobbyPage);
        }

        private void OnGameStartRequested()
        {
            GameLaunchContext.BeginMultiplayer();
            LoadingScreenController.Show("Building Neighbourhood...");
        }

        private void BuildUi()
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            Transform parent = canvas != null ? canvas.transform : transform;
            Image rootImage = LoadingScreenController.UiImage(parent, "Custom Multiplayer Menu", new Color(.04f, .16f, .25f, .88f));
            LoadingScreenController.Stretch(rootImage.rectTransform);
            m_root = rootImage.gameObject;

            Image card = LoadingScreenController.UiImage(m_root.transform, "Multiplayer Card", new Color(.12f, .44f, .62f, .98f));
            card.rectTransform.anchorMin = new Vector2(.5f, .5f); card.rectTransform.anchorMax = new Vector2(.5f, .5f);
            card.rectTransform.sizeDelta = new Vector2(960f, 850f);
            Text title = LoadingScreenController.UiText(card.transform, "MULTIPLAYER", 62, FontStyle.Bold, Color.white);
            title.alignment = TextAnchor.MiddleCenter; title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(.5f, .91f);
            title.rectTransform.sizeDelta = new Vector2(850f, 100f);
            m_status = LoadingScreenController.UiText(card.transform, "Starting Steam...", 23, FontStyle.Normal, new Color(1f, .9f, .62f));
            m_status.alignment = TextAnchor.MiddleCenter; m_status.rectTransform.anchorMin = m_status.rectTransform.anchorMax = new Vector2(.5f, .08f);
            m_status.rectTransform.sizeDelta = new Vector2(850f, 80f);

            m_homePage = Page(card.transform, "Multiplayer Home");
            PlaceButton(m_homePage.transform, "Host Game", .67f, () => ShowPage(m_hostPage));
            PlaceButton(m_homePage.transform, "Join Game", .47f, OpenBrowser);
            PlaceButton(m_homePage.transform, "Back", .27f, Close);

            m_hostPage = Page(card.transform, "Host Options");
            m_publicToggle = CreateToggle(m_hostPage.transform, "Public Lobby", .72f);
            Text maxLabel = Label(m_hostPage.transform, "Maximum Players", .57f);
            m_playerLimitButton = PlaceButton(m_hostPage.transform, "Maximum Players: 4", .47f, CyclePlayerLimit);
            m_playerLimitLabel = m_playerLimitButton.GetComponentInChildren<Text>();
            PlaceButton(m_hostPage.transform, "Create Lobby", .29f, Host);
            PlaceButton(m_hostPage.transform, "Back", .14f, () => ShowPage(m_homePage));

            m_browserPage = Page(card.transform, "Lobby Browser");
            m_browserRows = new GameObject("Lobby Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).transform;
            m_browserRows.SetParent(m_browserPage.transform, false);
            RectTransform rowsRect = (RectTransform)m_browserRows;
            rowsRect.anchorMin = new Vector2(.08f, .25f); rowsRect.anchorMax = new Vector2(.92f, .82f); rowsRect.offsetMin = rowsRect.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = m_browserRows.GetComponent<VerticalLayoutGroup>(); layout.spacing = 12f; layout.childControlHeight = false;
            PlaceButton(m_browserPage.transform, "Refresh", .17f, Refresh);
            PlaceButton(m_browserPage.transform, "Back", .06f, () => ShowPage(m_homePage));

            m_lobbyPage = Page(card.transform, "Lobby");
            m_lobbyPlayers = LoadingScreenController.UiText(m_lobbyPage.transform, "PLAYERS", 34, FontStyle.Bold, Color.white);
            m_lobbyPlayers.alignment = TextAnchor.UpperLeft; m_lobbyPlayers.rectTransform.anchorMin = new Vector2(.12f, .35f);
            m_lobbyPlayers.rectTransform.anchorMax = new Vector2(.88f, .82f); m_lobbyPlayers.rectTransform.offsetMin = m_lobbyPlayers.rectTransform.offsetMax = Vector2.zero;
            PlaceButton(m_lobbyPage.transform, "Invite Friends", .25f, () => m_lobby.InviteFriends());
            m_startButton = PlaceButton(m_lobbyPage.transform, "Start Game", .14f, () => m_lobby.StartGame());
            PlaceButton(m_lobbyPage.transform, "Leave Lobby", .03f, LeaveLobby);

            ShowPage(m_homePage);
        }

        private void Host() => m_lobby?.HostLobby(m_publicToggle.isOn, m_maximumPlayers);
        private void CyclePlayerLimit()
        {
            m_maximumPlayers = m_maximumPlayers >= 4 ? 2 : m_maximumPlayers + 1;
            m_playerLimitLabel.text = $"Maximum Players: {m_maximumPlayers}";
        }
        private void OpenBrowser() { ShowPage(m_browserPage); Refresh(); }
        private void Refresh() => m_lobby?.RefreshLobbies();
        private void LeaveLobby() { m_lobby?.LeaveLobby(); ShowPage(m_homePage); }

        private void Close()
        {
            m_root.SetActive(false);
            if (m_mainButtons != null) foreach (Button button in m_mainButtons) if (button != null) button.gameObject.SetActive(true);
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
                Button row = LoadingScreenController.UiButton(m_browserRows, label, () => m_lobby.JoinLobby(lobbyId));
                row.GetComponent<RectTransform>().sizeDelta = new Vector2(760f, 72f);
                row.interactable = maximum > 0 && current < maximum;
            }
            if (m_lobby.DiscoveredLobbies.Count == 0)
            {
                Text empty = LoadingScreenController.UiText(m_browserRows, "No public lobbies found.", 28, FontStyle.Italic, Color.white);
                empty.rectTransform.sizeDelta = new Vector2(760f, 72f); empty.alignment = TextAnchor.MiddleCenter;
            }
        }

        private static GameObject Page(Transform parent, string name)
        {
            GameObject page = new GameObject(name, typeof(RectTransform)); page.transform.SetParent(parent, false);
            RectTransform rect = page.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(.05f, .14f); rect.anchorMax = new Vector2(.95f, .84f);
            rect.offsetMin = rect.offsetMax = Vector2.zero; return page;
        }

        private void ShowPage(GameObject page)
        {
            if (page == null) return;
            if (m_homePage != null) m_homePage.SetActive(page == m_homePage);
            if (m_hostPage != null) m_hostPage.SetActive(page == m_hostPage);
            if (m_browserPage != null) m_browserPage.SetActive(page == m_browserPage);
            if (m_lobbyPage != null) m_lobbyPage.SetActive(page == m_lobbyPage);
        }

        private static Button PlaceButton(Transform parent, string text, float anchorY, UnityEngine.Events.UnityAction action)
        {
            Button button = LoadingScreenController.UiButton(parent, text, action);
            RectTransform rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, anchorY);
            rect.sizeDelta = new Vector2(600f, 88f); return button;
        }

        private static Text Label(Transform parent, string text, float anchorY)
        {
            Text label = LoadingScreenController.UiText(parent, text, 28, FontStyle.Bold, Color.white);
            label.alignment = TextAnchor.MiddleCenter; label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, anchorY);
            label.rectTransform.sizeDelta = new Vector2(600f, 60f); return label;
        }

        private static Toggle CreateToggle(Transform parent, string text, float anchorY)
        {
            GameObject go = new GameObject(text, typeof(RectTransform), typeof(Toggle)); go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, anchorY); rect.sizeDelta = new Vector2(600f, 70f);
            Image box = LoadingScreenController.UiImage(go.transform, "Box", Color.white); box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(.08f, .5f); box.rectTransform.sizeDelta = new Vector2(48f, 48f);
            Image check = LoadingScreenController.UiImage(box.transform, "Check", new Color(1f, .55f, .1f)); LoadingScreenController.Stretch(check.rectTransform, 8f);
            Toggle toggle = go.GetComponent<Toggle>(); toggle.targetGraphic = box; toggle.graphic = check; toggle.isOn = true;
            Text label = LoadingScreenController.UiText(go.transform, text, 31, FontStyle.Bold, Color.white); label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.anchorMin = new Vector2(.17f, 0f); label.rectTransform.anchorMax = Vector2.one; label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return toggle;
        }

    }
}
