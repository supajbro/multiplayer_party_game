using UnityEngine;

namespace CouchGuys.Networking
{
    /// <summary>
    /// Loads the persistent multiplayer services without requiring scene-by-scene setup.
    /// </summary>
    public static class SteamMultiplayerAutoLoader
    {
        private const string ResourceName = "SteamMultiplayerBootstrap";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadBootstrap()
        {
            if (Object.FindFirstObjectByType<SteamLobbyController>() != null)
            {
                return;
            }

            GameObject prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab == null)
            {
                Debug.LogError($"Steam multiplayer bootstrap resource '{ResourceName}' is missing. Run Couch Guys > Build Steam Multiplayer Assets.");
                return;
            }

            Object.Instantiate(prefab);
        }
    }
}
