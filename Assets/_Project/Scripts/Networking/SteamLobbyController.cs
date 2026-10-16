using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Managing.Scened;
using FishySteamworks;
using Steamworks;
using UnityEngine;
using CouchGuys.UI;

namespace CouchGuys.Networking
{
    /// <summary>
    /// Coordinates a Steam lobby with FishNet's host/client lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamLobbyController : MonoBehaviour
    {
        private const int DefaultMaximumPlayers = 4;
        private const string GameKey = "game";
        private const string GameValue = "couch_guys";
        private const string BuildKey = "build";
        private const string StateKey = "state";
        private const string ReadyKey = "ready";
        private const string WaitingState = "waiting";
        private const string LoadingState = "loading";
        private const string PlayingState = "playing";
        private const string HostAddressKey = "host_address";
        private const string LobbyNameKey = "name";

        [SerializeField] private NetworkManager m_networkManager;
        [SerializeField] private FishySteamworks.FishySteamworks m_transport;
        [SerializeField] private SteamClientBootstrap m_steamBootstrap;

        private readonly List<CSteamID> m_discoveredLobbies = new List<CSteamID>();
        private Callback<LobbyCreated_t> m_lobbyCreated;
        private Callback<LobbyEnter_t> m_lobbyEntered;
        private Callback<LobbyMatchList_t> m_lobbyMatchList;
        private Callback<LobbyChatUpdate_t> m_lobbyChatUpdate;
        private Callback<GameLobbyJoinRequested_t> m_gameLobbyJoinRequested;
        private Callback<LobbyDataUpdate_t> m_lobbyDataUpdated;
        private CSteamID m_currentLobby;
        private bool m_callbacksRegistered;
        private bool m_isCreatingLobby;
        private ELobbyType m_pendingLobbyType = ELobbyType.k_ELobbyTypeFriendsOnly;
        private int m_pendingMaximumPlayers = DefaultMaximumPlayers;
        private string m_status = "Starting Steam...";

        public IReadOnlyList<CSteamID> DiscoveredLobbies => m_discoveredLobbies;
        public CSteamID CurrentLobby => m_currentLobby;
        public string Status => m_status;
        public int MaximumPlayersSupported => DefaultMaximumPlayers;
        public bool IsInLobby => m_currentLobby.IsValid();
        public bool IsHosting => m_networkManager != null && m_networkManager.ServerManager.Started;
        public bool IsSteamReady => SteamClientBootstrap.IsInitialised;
        public bool IsLocalLobbyOwner => IsInLobby && SteamMatchmaking.GetLobbyOwner(m_currentLobby) == SteamUser.GetSteamID();
        public bool IsGameLoading => IsInLobby && SteamMatchmaking.GetLobbyData(m_currentLobby, StateKey) == LoadingState;
        public bool IsGamePlaying => IsInLobby && SteamMatchmaking.GetLobbyData(m_currentLobby, StateKey) == PlayingState;

        public event Action StateChanged;
        public event Action LobbiesChanged;
        public event Action GameStartRequested;

        private void Awake()
        {
            m_networkManager ??= GetComponent<NetworkManager>();
            m_transport ??= GetComponent<FishySteamworks.FishySteamworks>();
            m_steamBootstrap ??= GetComponent<SteamClientBootstrap>();
            m_transport?.SetMaximumClients(DefaultMaximumPlayers);
        }

        private void Start()
        {
            if (m_steamBootstrap == null)
            {
                SetStatus("Steam bootstrap is missing.", true);
                return;
            }

            m_steamBootstrap.Initialised += OnSteamInitialised;
            m_steamBootstrap.InitialisationFailed += OnSteamInitialisationFailed;

            if (SteamClientBootstrap.IsInitialised)
            {
                OnSteamInitialised();
            }
            else if (!string.IsNullOrWhiteSpace(SteamClientBootstrap.InitialisationError))
            {
                OnSteamInitialisationFailed(SteamClientBootstrap.InitialisationError);
            }
        }

        private void OnDestroy()
        {
            if (m_steamBootstrap != null)
            {
                m_steamBootstrap.Initialised -= OnSteamInitialised;
                m_steamBootstrap.InitialisationFailed -= OnSteamInitialisationFailed;
            }

            UnsubscribeNetworkEvents();
            DisposeCallbacks();
        }

        public void HostLobby()
        {
            HostLobby(false, DefaultMaximumPlayers);
        }

        public void HostLobby(bool isPublic, int maximumPlayers)
        {
            if (!CanBeginLobbyOperation() || m_isCreatingLobby)
            {
                return;
            }

            m_isCreatingLobby = true;
            m_pendingLobbyType = isPublic ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly;
            m_pendingMaximumPlayers = Mathf.Clamp(maximumPlayers, 2, DefaultMaximumPlayers);
            SetStatus("Creating Steam lobby...");
            SteamMatchmaking.CreateLobby(m_pendingLobbyType, m_pendingMaximumPlayers);
        }

        public void RefreshLobbies()
        {
            if (!CanBeginLobbyOperation())
            {
                return;
            }

            m_discoveredLobbies.Clear();
            SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameValue, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(BuildKey, Application.version, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(StateKey, WaitingState, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.RequestLobbyList();
            SetStatus("Searching for Couch Guys Steam lobbies...");
        }

        public void JoinLobby(CSteamID lobbyId)
        {
            if (!CanBeginLobbyOperation() || !lobbyId.IsValid())
            {
                SetStatus("Enter or select a valid Steam lobby ID.", true);
                return;
            }

            if (IsInLobby)
            {
                LeaveLobby();
            }

            SetStatus($"Joining Steam lobby {lobbyId.m_SteamID}...");
            SteamMatchmaking.JoinLobby(lobbyId);
        }

        public bool JoinLobby(string lobbyIdText)
        {
            if (!ulong.TryParse(lobbyIdText, out ulong lobbyId))
            {
                SetStatus("Lobby ID must be an unsigned number.", true);
                return false;
            }

            JoinLobby(new CSteamID(lobbyId));
            return true;
        }

        public void InviteFriends()
        {
            if (!IsInLobby)
            {
                SetStatus("Host or join a lobby before inviting friends.", true);
                return;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(m_currentLobby);
            SetStatus("Opened the Steam friend invitation overlay.");
        }

        public void StartGame()
        {
            if (!IsLocalLobbyOwner || m_networkManager == null || !m_networkManager.ServerManager.Started)
            {
                SetStatus("Only the lobby host can start the game.", true);
                return;
            }

            SteamMatchmaking.SetLobbyJoinable(m_currentLobby, false);
            SteamMatchmaking.SetLobbyData(m_currentLobby, StateKey, LoadingState);
            SteamMatchmaking.SetLobbyMemberData(m_currentLobby, ReadyKey, "0");
            SetStatus("Loading the neighbourhood...");
            GameLaunchContext.BeginMultiplayer();
            LoadingScreenController.Show("Building Neighbourhood...");
            GameStartRequested?.Invoke();

            SceneLoadData loadData = new SceneLoadData("SampleScene")
            {
                ReplaceScenes = ReplaceOption.All
            };
            m_networkManager.SceneManager.LoadGlobalScenes(loadData);
        }

        public void SetLocalPlayerReady()
        {
            if (IsInLobby)
            {
                SteamMatchmaking.SetLobbyMemberData(m_currentLobby, ReadyKey, "1");
                StateChanged?.Invoke();
            }
        }

        public bool AreAllLobbyMembersReady()
        {
            if (!IsInLobby) return false;
            int count = SteamMatchmaking.GetNumLobbyMembers(m_currentLobby);
            if (count <= 0) return false;
            for (int index = 0; index < count; index++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(m_currentLobby, index);
                if (SteamMatchmaking.GetLobbyMemberData(m_currentLobby, member, ReadyKey) != "1") return false;
            }
            return true;
        }

        public void ReleasePlayers()
        {
            if (IsLocalLobbyOwner)
            {
                SteamMatchmaking.SetLobbyData(m_currentLobby, StateKey, PlayingState);
                StateChanged?.Invoke();
            }
        }

        public IReadOnlyList<string> GetLobbyMemberNames()
        {
            List<string> names = new List<string>();
            if (!IsInLobby) return names;
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(m_currentLobby);
            int count = SteamMatchmaking.GetNumLobbyMembers(m_currentLobby);
            for (int index = 0; index < count; index++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(m_currentLobby, index);
                string suffix = member == owner ? " (Host)" : string.Empty;
                names.Add(SteamFriends.GetFriendPersonaName(member) + suffix);
            }
            return names;
        }

        public int GetLobbyMemberCount(CSteamID lobbyId) => SteamMatchmaking.GetNumLobbyMembers(lobbyId);
        public int GetLobbyMemberLimit(CSteamID lobbyId) => SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
        public string GetLobbyHostName(CSteamID lobbyId)
        {
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
            return owner.IsValid() ? SteamFriends.GetFriendPersonaName(owner) : "Unknown host";
        }

        public void LeaveLobby()
        {
            if (m_networkManager != null)
            {
                if (m_networkManager.ClientManager.Started)
                {
                    m_networkManager.ClientManager.StopConnection();
                }

                if (m_networkManager.ServerManager.Started)
                {
                    m_networkManager.ServerManager.StopConnection(true);
                }
            }

            if (SteamClientBootstrap.IsInitialised && IsInLobby)
            {
                SteamMatchmaking.LeaveLobby(m_currentLobby);
            }

            m_currentLobby = CSteamID.Nil;
            m_isCreatingLobby = false;
            SetStatus("Left multiplayer session.");
        }

        public string GetLobbyDisplayName(CSteamID lobbyId)
        {
            if (!SteamClientBootstrap.IsInitialised || !lobbyId.IsValid())
            {
                return "Unknown lobby";
            }

            string lobbyName = SteamMatchmaking.GetLobbyData(lobbyId, LobbyNameKey);
            return string.IsNullOrWhiteSpace(lobbyName) ? $"Lobby {lobbyId.m_SteamID}" : lobbyName;
        }

        private void OnSteamInitialised()
        {
            RegisterCallbacks();
            SubscribeNetworkEvents();
            SetStatus($"Steam ready: {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID().m_SteamID})");
        }

        private void OnSteamInitialisationFailed(string message)
        {
            SetStatus(message, true);
        }

        private void RegisterCallbacks()
        {
            if (m_callbacksRegistered)
            {
                return;
            }

            m_lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            m_lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            m_lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
            m_lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            m_gameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            m_lobbyDataUpdated = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdated);
            m_callbacksRegistered = true;
        }

        private void DisposeCallbacks()
        {
            m_lobbyCreated?.Dispose();
            m_lobbyEntered?.Dispose();
            m_lobbyMatchList?.Dispose();
            m_lobbyChatUpdate?.Dispose();
            m_gameLobbyJoinRequested?.Dispose();
            m_lobbyDataUpdated?.Dispose();
            m_callbacksRegistered = false;
        }

        private void SubscribeNetworkEvents()
        {
            if (m_networkManager == null)
            {
                SetStatus("FishNet NetworkManager is missing.", true);
                return;
            }

            UnsubscribeNetworkEvents();
            m_networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            m_networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            m_networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void UnsubscribeNetworkEvents()
        {
            if (m_networkManager == null || m_networkManager.ClientManager == null || m_networkManager.ServerManager == null)
            {
                return;
            }

            m_networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            m_networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            m_networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        private void OnLobbyCreated(LobbyCreated_t result)
        {
            m_isCreatingLobby = false;
            if (result.m_eResult != EResult.k_EResultOK)
            {
                SetStatus($"Steam lobby creation failed: {result.m_eResult}.", true);
                return;
            }

            m_currentLobby = new CSteamID(result.m_ulSteamIDLobby);
            string hostAddress = SteamUser.GetSteamID().m_SteamID.ToString();
            SteamMatchmaking.SetLobbyData(m_currentLobby, GameKey, GameValue);
            SteamMatchmaking.SetLobbyData(m_currentLobby, BuildKey, Application.version);
            SteamMatchmaking.SetLobbyData(m_currentLobby, StateKey, WaitingState);
            SteamMatchmaking.SetLobbyData(m_currentLobby, HostAddressKey, hostAddress);
            SteamMatchmaking.SetLobbyData(m_currentLobby, LobbyNameKey, $"{SteamFriends.GetPersonaName()}'s Couch Guys Lobby");
            SteamMatchmaking.SetLobbyMemberLimit(m_currentLobby, m_pendingMaximumPlayers);
            SteamMatchmaking.SetLobbyJoinable(m_currentLobby, true);

            bool serverStarted = m_networkManager.ServerManager.StartConnection();
            bool clientStarted = serverStarted && m_networkManager.ClientManager.StartConnection();
            if (!serverStarted || !clientStarted)
            {
                SetStatus("Lobby was created, but the FishNet host failed to start.", true);
                return;
            }

            SetStatus($"Hosting Steam lobby {m_currentLobby.m_SteamID}. Invite a friend or share this lobby ID.");
            Debug.Log($"Steam lobby created successfully. Lobby ID: {m_currentLobby.m_SteamID}; host Steam ID: {hostAddress}.");
        }

        private void OnLobbyEntered(LobbyEnter_t result)
        {
            EChatRoomEnterResponse response = (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse;
            if (response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                SetStatus($"Could not enter Steam lobby: {response}.", true);
                return;
            }

            m_currentLobby = new CSteamID(result.m_ulSteamIDLobby);
            if (SteamMatchmaking.GetLobbyData(m_currentLobby, GameKey) != GameValue ||
                SteamMatchmaking.GetLobbyData(m_currentLobby, BuildKey) != Application.version ||
                SteamMatchmaking.GetLobbyData(m_currentLobby, StateKey) != WaitingState)
            {
                SetStatus("This lobby is incompatible or has already started.", true);
                SteamMatchmaking.LeaveLobby(m_currentLobby);
                m_currentLobby = CSteamID.Nil;
                return;
            }
            CSteamID lobbyOwner = SteamMatchmaking.GetLobbyOwner(m_currentLobby);
            if (lobbyOwner == SteamUser.GetSteamID())
            {
                return;
            }

            string hostAddress = SteamMatchmaking.GetLobbyData(m_currentLobby, HostAddressKey);
            if (string.IsNullOrWhiteSpace(hostAddress))
            {
                SetStatus("Joined the lobby, but its host address is missing.", true);
                return;
            }

            bool connectionStarted = m_networkManager.ClientManager.StartConnection(hostAddress);
            if (!connectionStarted)
            {
                SetStatus($"Joined lobby {m_currentLobby.m_SteamID}, but the network connection could not start.", true);
                return;
            }

            SetStatus($"Joined Steam lobby {m_currentLobby.m_SteamID}; connecting to host {hostAddress}...");
            Debug.Log($"Entered Steam lobby {m_currentLobby.m_SteamID}; connecting to host Steam ID {hostAddress}.");
        }

        private void OnLobbyMatchList(LobbyMatchList_t result)
        {
            m_discoveredLobbies.Clear();
            for (int index = 0; index < result.m_nLobbiesMatching; index++)
            {
                m_discoveredLobbies.Add(SteamMatchmaking.GetLobbyByIndex(index));
            }

            SetStatus($"Found {m_discoveredLobbies.Count} available Couch Guys lobby/lobbies.");
            LobbiesChanged?.Invoke();
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t update)
        {
            EChatMemberStateChange change = (EChatMemberStateChange)update.m_rgfChatMemberStateChange;
            string memberName = SteamFriends.GetFriendPersonaName(new CSteamID(update.m_ulSteamIDUserChanged));
            if ((change & EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
            {
                Debug.Log($"Steam lobby player joined: {memberName} ({update.m_ulSteamIDUserChanged}).");
            }
            else
            {
                Debug.Log($"Steam lobby player left or disconnected: {memberName} ({update.m_ulSteamIDUserChanged}), state {change}.");
            }
            StateChanged?.Invoke();
        }

        private void OnLobbyDataUpdated(LobbyDataUpdate_t update)
        {
            if (!IsInLobby || update.m_ulSteamIDLobby != m_currentLobby.m_SteamID) return;
            string state = SteamMatchmaking.GetLobbyData(m_currentLobby, StateKey);
            if (state == LoadingState)
            {
                GameLaunchContext.BeginMultiplayer();
                LoadingScreenController.Show("Joining the neighbourhood...");
                GameStartRequested?.Invoke();
            }
            StateChanged?.Invoke();
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t request)
        {
            Debug.Log($"Steam friend join accepted for lobby {request.m_steamIDLobby.m_SteamID}.");
            JoinLobby(request.m_steamIDLobby);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            Debug.Log($"Local multiplayer client state: {args.ConnectionState}.");
            if (args.ConnectionState == LocalConnectionState.Stopped && IsInLobby && !m_networkManager.ServerManager.Started)
            {
                SetStatus("Disconnected from the host. The Steam lobby session has ended.", true);
                SteamMatchmaking.LeaveLobby(m_currentLobby);
                m_currentLobby = CSteamID.Nil;
            }
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            Debug.Log($"Multiplayer host state: {args.ConnectionState}.");
            if (args.ConnectionState == LocalConnectionState.Stopped && IsInLobby && SteamMatchmaking.GetLobbyOwner(m_currentLobby) == SteamUser.GetSteamID())
            {
                SetStatus("The host stopped; the Steam lobby session has ended.");
                SteamMatchmaking.LeaveLobby(m_currentLobby);
                m_currentLobby = CSteamID.Nil;
            }
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            Debug.Log($"Network player {connection.ClientId} {args.ConnectionState.ToString().ToLowerInvariant()}.");
        }

        private bool CanBeginLobbyOperation()
        {
            if (!SteamClientBootstrap.IsInitialised)
            {
                SetStatus("Steam is not initialised. Ensure the Steam client is running.", true);
                return false;
            }

            if (m_networkManager == null || m_transport == null)
            {
                SetStatus("Multiplayer bootstrap is incomplete.", true);
                return false;
            }

            return true;
        }

        private void SetStatus(string status, bool isError = false)
        {
            m_status = status;
            StateChanged?.Invoke();
            if (isError)
            {
                Debug.LogError(status);
            }
            else
            {
                Debug.Log(status);
            }
        }
    }
}
