using System;
using Steamworks;
using UnityEngine;

namespace CouchGuys.Networking
{
    /// <summary>
    /// Owns the Steamworks client lifetime and callback pump.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    [DisallowMultipleComponent]
    public sealed class SteamClientBootstrap : MonoBehaviour
    {
        public static SteamClientBootstrap Instance { get; private set; }
        public static bool IsInitialised { get; private set; }
        public static string InitialisationError { get; private set; }

        public event Action Initialised;
        public event Action<string> InitialisationFailed;

        private bool m_ownsSteamLifetime;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitialiseSteam();
        }

        private void Update()
        {
            if (IsInitialised)
            {
                SteamAPI.RunCallbacks();
            }
        }

        private void OnApplicationQuit()
        {
            ShutdownSteam();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                ShutdownSteam();
            }
        }

        private void InitialiseSteam()
        {
            if (IsInitialised)
            {
                Initialised?.Invoke();
                return;
            }

            try
            {
                ESteamAPIInitResult result = SteamAPI.InitEx(out string errorMessage);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    ReportFailure($"Steam failed to initialise ({result}): {errorMessage}. Ensure Steam is running and the development steam_appid.txt is beside the executable/project root.");
                    return;
                }

                IsInitialised = true;
                InitialisationError = string.Empty;
                m_ownsSteamLifetime = true;
                string personaName = SteamFriends.GetPersonaName();
                CSteamID steamId = SteamUser.GetSteamID();
                Debug.Log($"Steam initialised for {personaName} ({steamId.m_SteamID}).");
                Initialised?.Invoke();
            }
            catch (Exception exception)
            {
                ReportFailure($"Steam could not initialise. Ensure Steam is running and the Steamworks native libraries are available. {exception.GetType().Name}: {exception.Message}");
            }
        }

        private void ShutdownSteam()
        {
            if (!m_ownsSteamLifetime)
            {
                return;
            }

            SteamAPI.Shutdown();
            m_ownsSteamLifetime = false;
            IsInitialised = false;
        }

        private void ReportFailure(string message)
        {
            InitialisationError = message;
            Debug.LogError(message);
            InitialisationFailed?.Invoke(message);
        }
    }
}
