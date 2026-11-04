using System.Collections;
using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Networking;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Testing
{
    // Scene-only startup. All gameplay remains in the production authority and delivery systems.
    public sealed class AITestController : MonoBehaviour
    {
        [SerializeField] private GameObject m_localBootstrap;
        [SerializeField] private NetworkObject[] m_teammatePrefabs;
        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private DeliveryManager m_deliveryManager;
        [SerializeField] private Camera m_spectatorCamera;
        [SerializeField] private Vector3 m_cameraOffset = new(0f, 18f, -16f);
        [SerializeField, Min(0.1f)] private float m_cameraSmoothing = 4f;
        [SerializeField, Min(0.5f)] private float m_dispatchRetryInterval = 2f;
        private readonly List<NetworkObject> m_team = new();
        private NetworkManager m_network;
        private GameObject m_bootstrap;
        private float m_nextDispatch;

        private IEnumerator Start()
        {
            // No session, lobby, player connection, or UI is needed. A local server provides
            // the same authority used by normal gameplay; a loopback client renders its objects.
            var previous = FishNet.InstanceFinder.NetworkManager;
            if (previous != null)
            {
                if (previous.ServerManager.Started || previous.ClientManager.Started)
                {
                    Debug.LogError("Open AITest outside an existing game session.", this);
                    yield break;
                }
                Destroy(previous.gameObject);
                yield return null;
            }
            m_bootstrap = Instantiate(m_localBootstrap);
            m_network = m_bootstrap.GetComponent<NetworkManager>();
            var spawner = m_bootstrap.GetComponent<NeighbourhoodPlayerSpawner>();
            if (spawner != null) Destroy(spawner);
            yield return null;
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("AITest.AllowRemoteValidation", false))
            {
                m_network.TransportManager.Transport.SetMaximumClients(2);

            }
#endif
            // Without an owned human player, the test must enroll its spectator explicitly.
            // FishNet otherwise hides scene renderers from the local host client.
            m_network.SceneManager.OnClientLoadedStartScenes += AddTestObserver;
            if (AITestRemoteProbe.ClientOnly)
            {
                m_network.ClientManager.StartConnection();
                yield break;
            }
            if (!m_network.ServerManager.StartConnection() || !m_network.ClientManager.StartConnection())
            {
                Debug.LogError("AITest local gameplay authority failed to start.", this);
                yield break;
            }
            var synchroniser = m_generator.GetComponent<NeighbourhoodSeedSynchroniser>();
            if (synchroniser == null && !m_generator.HasGeneratedNeighbourhood && !m_generator.IsGenerating)
                m_generator.Generate();
            while (!m_generator.IsGenerationReady || !m_generator.IsNavMeshReady || !m_deliveryManager.IsServerInitialized)
                yield return null;
            Transform depot = NeighbourhoodPlayerSpawner.SelectSpawnPoint(m_generator.GeneratedStartingArea, 0);
            if (depot == null || m_teammatePrefabs == null || m_teammatePrefabs.Length != 4)
            {
                Debug.LogError("AITest requires a depot spawn and exactly four teammate prefabs.", this);
                yield break;
            }
            for (int i = 0; i < m_teammatePrefabs.Length; i++)
            {
                Vector3 position = depot.position + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.back * 3f;
                if (!NavMesh.SamplePosition(position, out var hit, 8f, NavMesh.AllAreas))
                {
                    Debug.LogError($"AITest has no navigable spawn for teammate {i + 1}.", this);
                    yield break;
                }
                var bot = Instantiate(m_teammatePrefabs[i], hit.position, depot.rotation);
                bot.name = $"AI Teammate {i + 1}";
                var controller = bot.GetComponent<DebugCouchBotController>();
                controller.Initialise(bot.GetComponent<PlayerCouchCarrier>().DebugBotSettings, i, null);
                m_network.ServerManager.Spawn(bot);
                m_team.Add(bot);
            }
            Debug.Log("AITest: four role-configured teammates ready.", this);
        }

        private void Update()
        {
            if (m_network == null || !m_network.ServerManager.Started || m_team.Count != 4 ||
                !m_deliveryManager.IsServerInitialized || Time.time < m_nextDispatch) return;
            m_nextDispatch = Time.time + m_dispatchRetryInterval;
            if (m_deliveryManager.HasActiveDelivery) return;
            foreach (var npc in FindObjectsByType<DeliveryNPC>(FindObjectsSortMode.None))
            {
                for (int tier = 0; m_deliveryManager.GetTierConfig(tier) != null; tier++)
                {
                    if (m_deliveryManager.TryStartDeliveryServer(npc, tier))
                    {
                        Debug.Log($"AITest: started delivery {m_deliveryManager.Deliveries[0].Id}.", this);
                        return;
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (m_spectatorCamera == null || m_team.Count == 0) return;
            Vector3 focus = Vector3.zero;
            int count = 0;
            foreach (var bot in m_team) if (bot != null) { focus += bot.transform.position; count++; }
            if (count == 0) return;
            focus /= count;
            if (m_deliveryManager.ActiveCouch != null) focus = m_deliveryManager.ActiveCouch.transform.position;
            var cameraTransform = m_spectatorCamera.transform;
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, focus + m_cameraOffset,
                1f - Mathf.Exp(-m_cameraSmoothing * Time.deltaTime));
            cameraTransform.LookAt(focus);
        }

        private void AddTestObserver(FishNet.Connection.NetworkConnection connection, bool asServer)
        {
            if (asServer && connection.IsActive)
                m_network.SceneManager.AddConnectionToScene(connection, gameObject.scene);
        }

        private void OnDestroy()
        {
            if (m_network != null) m_network.SceneManager.OnClientLoadedStartScenes -= AddTestObserver;
            if (m_bootstrap != null) Destroy(m_bootstrap);
        }
    }
}
