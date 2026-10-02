using Steamworks;
using UnityEngine;

namespace CouchGuys.Networking
{
    /// <summary>
    /// Intentionally small development UI for proving the Steam lobby pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamLobbyDebugInterface : MonoBehaviour
    {
        [SerializeField] private SteamLobbyController m_lobbyController;

        private string m_lobbyIdText = string.Empty;
        private Vector2 m_scrollPosition;

        private void Awake()
        {
            m_lobbyController ??= GetComponent<SteamLobbyController>();
        }

        private void OnGUI()
        {
            if (m_lobbyController == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(16f, 16f, 390f, 430f), GUI.skin.box);
            GUILayout.Label("Couch Guys — Steam Multiplayer (Development)");
            GUILayout.Label($"Session capacity: {m_lobbyController.MaximumPlayersSupported} players");
            GUILayout.Label(m_lobbyController.Status);

            GUI.enabled = m_lobbyController.IsSteamReady && !m_lobbyController.IsInLobby;
            if (GUILayout.Button("Host Game"))
            {
                m_lobbyController.HostLobby();
            }

            if (GUILayout.Button("Find Lobbies"))
            {
                m_lobbyController.RefreshLobbies();
            }

            GUILayout.BeginHorizontal();
            m_lobbyIdText = GUILayout.TextField(m_lobbyIdText, GUILayout.MinWidth(220f));
            if (GUILayout.Button("Join Lobby ID", GUILayout.Width(120f)))
            {
                m_lobbyController.JoinLobby(m_lobbyIdText.Trim());
            }
            GUILayout.EndHorizontal();

            GUI.enabled = m_lobbyController.IsInLobby;
            if (GUILayout.Button("Invite Steam Friend"))
            {
                m_lobbyController.InviteFriends();
            }

            if (GUILayout.Button("Leave Session"))
            {
                m_lobbyController.LeaveLobby();
            }

            GUI.enabled = true;
            if (m_lobbyController.IsInLobby)
            {
                GUILayout.Label($"Lobby ID: {m_lobbyController.CurrentLobby.m_SteamID}");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Discovered lobbies");
            m_scrollPosition = GUILayout.BeginScrollView(m_scrollPosition, GUILayout.Height(150f));
            foreach (CSteamID lobbyId in m_lobbyController.DiscoveredLobbies)
            {
                if (GUILayout.Button($"Join {m_lobbyController.GetLobbyDisplayName(lobbyId)} ({lobbyId.m_SteamID})"))
                {
                    m_lobbyController.JoinLobby(lobbyId);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
