using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishySteamworks;
using Steamworks;
using UnityEngine;

namespace CouchGuys.Networking
{
    /// <summary>
    /// Coordinates a Steam lobby with FishNet's host/client lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamLobbyController : MonoBehaviour
    {
        private const int MaximumPlayers = 4;
        private const string GameKey = "couch_guys";
        private const string GameValue = "1";
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
        private CSteamID m_currentLobby;
        private bool m_callbacksRegistered;
        private bool m_isCreatingLobby;
        private string m_status = "Starting Steam...";

        public IReadOnlyList<CSteamID> DiscoveredLobbies => m_discoveredLobbies;
        public CSteamID CurrentLobby => m_currentLobby;
        public string Status => m_status;
        public int MaximumPlayersSupported => MaximumPlayers;
        public bool IsInLobby => m_currentLobby.IsValid();
        public bool IsHosting => m_networkManager != null && m_networkManager.ServerManager.Started;
        public bool IsSteamReady => SteamClientBootstrap.IsInitialised;

        private void Awake()
        {
            m_networkManager ??= GetComponent<NetworkManager>();
            m_transport ??= GetComponent<FishySteamworks.FishySteamworks>();
            m_steamBootstrap ??= GetComponent<SteamClientBootstrap>();
            m_transport?.SetMaximumClients(MaximumPlayers);
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
            if (!CanBeginLobbyOperation() || m_isCreatingLobby)
            {
                return;
            }

            m_isCreatingLobby = true;
            SetStatus("Creating Steam lobby...");
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaximumPlayers);
        }

        public void RefreshLobbies()
        {
            if (!CanBeginLobbyOperation())
            {
                return;
            }

            m_discoveredLobbies.Clear();
            SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameValue, ELobbyComparison.k_ELobbyComparisonEqual);
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
            m_callbacksRegistered = true;
        }

        private void DisposeCallbacks()
        {
            m_lobbyCreated?.Dispose();
            m_lobbyEntered?.Dispose();
            m_lobbyMatchList?.Dispose();
            m_lobbyChatUpdate?.Dispose();
            m_gameLobbyJoinRequested?.Dispose();
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
            SteamMatchmaking.SetLobbyData(m_currentLobby, HostAddressKey, hostAddress);
            SteamMatchmaking.SetLobbyData(m_currentLobby, LobbyNameKey, $"{SteamFriends.GetPersonaName()}'s Couch Guys Lobby");
            SteamMatchmaking.SetLobbyMemberLimit(m_currentLobby, MaximumPlayers);
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
