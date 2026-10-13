using System.Collections;
using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Networking;
using CouchGuys.Player;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CouchGuys.Testing
{
    /// <summary>
    /// Self-contained host and geometry harness for the CouchPhysicsTest scene.
    /// It deliberately reuses the production Player, Couch, and AI implementations.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CouchPhysicsTestController : MonoBehaviour
    {
        public const string SceneName = "CouchPhysicsTest";

        [SerializeField] private GameObject m_soloNetworkBootstrap;
        [SerializeField] private NetworkObject m_playerPrefab;
        [SerializeField] private NetworkObject m_couchPrefab;
        [SerializeField] private NetworkObject m_sledPrefab;

        private readonly List<NetworkObject> m_spawnedTeammates = new();
        private NetworkManager m_networkManager;
        private NetworkObject m_player;
        private GameObject m_createdBootstrap;
        private Transform m_environmentRoot;
        private int m_nextTeammateIndex;

        private static readonly Vector3 PlayerStart = new(0f, 0.08f, -5f);

        private void Awake()
        {
            if (!string.Equals(SceneManager.GetActiveScene().name, SceneName,
                    System.StringComparison.Ordinal))
            {
                enabled = false;
                return;
            }

            BuildEnvironment();
        }

        private IEnumerator Start()
        {
            if (!enabled)
            {
                yield break;
            }

            yield return null;
            BuildNavMesh();

            m_networkManager = FishNet.InstanceFinder.NetworkManager;
            if (m_networkManager == null && m_soloNetworkBootstrap != null)
            {
                m_createdBootstrap = Instantiate(m_soloNetworkBootstrap);
                yield return null;
                m_networkManager = FishNet.InstanceFinder.NetworkManager;
            }

            if (m_networkManager == null || m_playerPrefab == null || m_couchPrefab == null ||
                m_sledPrefab == null)
            {
                Debug.LogError("Couch physics test scene is missing its network or prefab references.", this);
                yield break;
            }

            // The normal spawner waits for a generated neighbourhood. This scene owns its
            // compact spawn flow, so unsubscribe that component before starting the host.
            NeighbourhoodPlayerSpawner normalSpawner =
                m_networkManager.GetComponent<NeighbourhoodPlayerSpawner>();
            if (normalSpawner != null)
            {
                Destroy(normalSpawner);
                yield return null;
            }

            bool serverStarted = m_networkManager.ServerManager.Started ||
                                 m_networkManager.ServerManager.StartConnection();
            bool clientStarted = serverStarted && (m_networkManager.ClientManager.Started ||
                                                    m_networkManager.ClientManager.StartConnection());
            if (!serverStarted || !clientStarted)
            {
                Debug.LogError("Couch physics test could not start its local FishNet host.", this);
                yield break;
            }

            float timeout = Time.realtimeSinceStartup + 10f;
            while (m_networkManager.ServerManager.Clients.Count == 0 &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            foreach (NetworkConnection connection in m_networkManager.ServerManager.Clients.Values)
            {
                m_player = Instantiate(m_playerPrefab, PlayerStart, Quaternion.identity);
                m_networkManager.ServerManager.Spawn(m_player, connection);
                NetworkObject sled = Instantiate(m_sledPrefab, new Vector3(4f, 0.3f, -2f),
                    Quaternion.identity);
                m_networkManager.ServerManager.Spawn(sled);
                NetworkObject couch = Instantiate(m_couchPrefab, new Vector3(-2f, 0.52f, -2f),
                    Quaternion.identity);
                m_networkManager.ServerManager.Spawn(couch);
                couch.GetComponent<CouchCarryController>().SelectRandomModelVariantServer();
                yield break;
            }

            Debug.LogError("Couch physics test timed out waiting for the local client.", this);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || m_networkManager == null ||
                !m_networkManager.ServerManager.Started || m_player == null)
            {
                return;
            }

            if (Pressed(keyboard.digit0Key, keyboard.numpad0Key))
            {
                SpawnCouch();
            }
            if (Pressed(keyboard.digit9Key, keyboard.numpad9Key))
            {
                SpawnTeammate();
            }
            if (Pressed(keyboard.digit8Key, keyboard.numpad8Key))
            {
                RemoveTeammate();
            }
        }

        private void OnDestroy()
        {
            // The solo bootstrap is normally persistent between scene loads. This test owns
            // its instance, so remove it when leaving the playground.
            if (m_createdBootstrap != null)
            {
                Destroy(m_createdBootstrap);
            }
        }

        private static bool Pressed(ButtonControl mainKey, ButtonControl numpadKey) =>
            mainKey.wasPressedThisFrame || numpadKey.wasPressedThisFrame;

        private void SpawnCouch()
        {
            Vector3 position = FindClearGroundPosition(2.2f, 0.52f, new Vector3(1.05f, 0.45f, 0.45f));
            NetworkObject couch = Instantiate(m_couchPrefab, position, m_player.transform.rotation);
            m_networkManager.ServerManager.Spawn(couch);
            couch.GetComponent<CouchCarryController>().SelectRandomModelVariantServer();
        }

        private void SpawnTeammate()
        {
            if (!m_player.TryGetComponent(out PlayerHealth leaderHealth) ||
                !m_player.TryGetComponent(out PlayerCouchCarrier leaderCarrier))
            {
                return;
            }

            Vector3 desired = FindClearGroundPosition(2.8f, 0.05f, new Vector3(0.35f, 0.9f, 0.35f));
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                desired = hit.position;
            }

            NetworkObject teammate = Instantiate(m_playerPrefab, desired, m_player.transform.rotation);
            if (!teammate.TryGetComponent(out DebugCouchBotController bot))
            {
                Debug.LogError("Player prefab is missing DebugCouchBotController.", teammate);
                Destroy(teammate.gameObject);
                return;
            }

            bot.Initialise(leaderCarrier.DebugBotSettings, m_nextTeammateIndex++, leaderHealth);
            m_networkManager.ServerManager.Spawn(teammate);
            m_spawnedTeammates.Add(teammate);
        }

        private void RemoveTeammate()
        {
            while (m_spawnedTeammates.Count > 0)
            {
                int last = m_spawnedTeammates.Count - 1;
                NetworkObject teammate = m_spawnedTeammates[last];
                m_spawnedTeammates.RemoveAt(last);
                if (teammate != null && teammate.IsSpawned)
                {
                    m_networkManager.ServerManager.Despawn(teammate);
                    return;
                }
            }
        }

        private Vector3 FindClearGroundPosition(float distance, float pivotHeight, Vector3 halfExtents)
        {
            Vector3 forward = Vector3.ProjectOnPlane(m_player.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f)
            {
                forward = Vector3.forward;
            }

            for (int attempt = 0; attempt < 12; attempt++)
            {
                float angle = attempt == 0 ? 0f : ((attempt + 1) / 2) * 30f * (attempt % 2 == 0 ? 1f : -1f);
                Vector3 candidate = m_player.transform.position +
                                    Quaternion.Euler(0f, angle, 0f) * forward * distance;
                if (!Physics.Raycast(candidate + Vector3.up * 8f, Vector3.down,
                        out RaycastHit hit, 20f, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                Vector3 position = hit.point + Vector3.up * pivotHeight;
                Vector3 boxCentre = position + Vector3.up * (halfExtents.y - pivotHeight);
                if (!Physics.CheckBox(boxCentre, halfExtents * 0.92f, Quaternion.identity,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    return position;
                }
            }

            return m_player.transform.position + forward * distance + Vector3.up * pivotHeight;
        }

        private void BuildEnvironment()
        {
            m_environmentRoot = new GameObject("Physics Test Environment").transform;

            PhysicsMaterial normal = CreatePhysicsMaterial("Normal", 0.55f, 0.55f);
            PhysicsMaterial slippery = CreatePhysicsMaterial("Slippery", 0.02f, 0.02f);
            PhysicsMaterial grippy = CreatePhysicsMaterial("High Grip", 1f, 1f);
            Material concrete = CreateMaterial("Concrete", new Color(0.42f, 0.45f, 0.48f));
            Material grass = CreateMaterial("Grass", new Color(0.20f, 0.48f, 0.20f));
            Material ice = CreateMaterial("Low Friction", new Color(0.25f, 0.72f, 0.86f));
            Material grip = CreateMaterial("High Friction", new Color(0.72f, 0.30f, 0.16f));
            Material marker = CreateMaterial("Slope Marker", new Color(0.95f, 0.72f, 0.12f));

            CreateBox("Central Flat Ground", new Vector3(0f, -0.3f, 0f), new Vector3(64f, 0.6f, 52f),
                Vector3.zero, concrete, normal);
            CreateBox("Low Friction Pad", new Vector3(-9f, 0.03f, -5f), new Vector3(7f, 0.06f, 7f),
                Vector3.zero, ice, slippery);
            CreateBox("High Friction Pad", new Vector3(9f, 0.03f, -5f), new Vector3(7f, 0.06f, 7f),
                Vector3.zero, grip, grippy);

            // Five adjacent lanes span gentle through deliberately unwalkable rollover angles.
            CreateRamp("Gentle Ramp 8deg", new Vector3(-12f, 0f, 15f), 8f, 11f, 4f, concrete, normal);
            CreateRamp("Medium Ramp 20deg", new Vector3(-6f, 0f, 15f), 20f, 11f, 4f, grass, normal);
            CreateRamp("Steep Ramp 35deg", new Vector3(0f, 0f, 15f), 35f, 11f, 4f, marker, normal);
            CreateRamp("Very Steep Ramp 48deg", new Vector3(6f, 0f, 15f), 48f, 11f, 4f, grip, grippy);
            CreateRamp("Slide And Roll Ramp 60deg", new Vector3(12f, 0f, 15f), 60f, 11f, 4f, ice, slippery);
            CreateRamp("Sideways Camber 25deg", new Vector3(-19f, 0f, 1f), 25f, 12f, 5f,
                concrete, normal, 90f);

            CreateBox("Sidewalk", new Vector3(0f, 0.12f, -9f), new Vector3(20f, 0.24f, 2.4f),
                Vector3.zero, concrete, normal);
            CreateBox("Kerb", new Vector3(0f, 0.19f, -7.72f), new Vector3(20f, 0.38f, 0.18f),
                Vector3.zero, marker, normal);
            for (int index = 0; index < 4; index++)
            {
                float height = 0.12f * (index + 1);
                CreateBox($"Step {index + 1}", new Vector3(-12f + index * 1.2f, height * 0.5f, -11.5f),
                    new Vector3(1.2f, height, 4f), Vector3.zero, concrete, normal);
            }

            CreateHill("Small Hill", new Vector3(20f, -2.5f, -7f), new Vector3(8f, 6f, 8f), grass, normal);
            CreateHill("Large Hill", new Vector3(22f, -5f, 8f), new Vector3(15f, 11f, 15f), grass, normal);

            for (int x = 0; x < 5; x++)
            {
                for (int z = 0; z < 5; z++)
                {
                    float height = 0.08f + ((x * 7 + z * 3) % 5) * 0.09f;
                    CreateBox($"Uneven Tile {x}-{z}",
                        new Vector3(-23f + x * 1.5f, height * 0.5f, -11f + z * 1.5f),
                        new Vector3(1.48f, height, 1.48f),
                        new Vector3((z - 2) * 2f, 0f, (x - 2) * 2f), grass, normal);
                }
            }

            GameObject lightObject = new("Test Sun", typeof(Light));
            lightObject.transform.SetParent(m_environmentRoot);
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.63f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.38f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.18f, 0.20f);
        }

        private void BuildNavMesh()
        {
            NavMeshSurface surface = m_environmentRoot.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        private void CreateRamp(string name, Vector3 lowerCentre, float angle, float length, float width,
            Material material, PhysicsMaterial physicsMaterial, float yaw = 0f)
        {
            float radians = angle * Mathf.Deg2Rad;
            Vector3 localCentre = new(0f, Mathf.Sin(radians) * length * 0.5f + 0.12f,
                Mathf.Cos(radians) * length * 0.5f);
            Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 centre = lowerCentre + yawRotation * localCentre;
            CreateBox(name, centre, new Vector3(width, 0.24f, length),
                new Vector3(-angle, yaw, 0f), material, physicsMaterial);
        }

        private void CreateHill(string name, Vector3 position, Vector3 scale, Material material,
            PhysicsMaterial physicsMaterial)
        {
            GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hill.name = name;
            hill.transform.SetParent(m_environmentRoot);
            hill.transform.position = position;
            hill.transform.localScale = scale;
            hill.GetComponent<Renderer>().sharedMaterial = material;
            hill.GetComponent<Collider>().sharedMaterial = physicsMaterial;
        }

        private void CreateBox(string name, Vector3 position, Vector3 scale, Vector3 rotation,
            Material material, PhysicsMaterial physicsMaterial)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(m_environmentRoot);
            box.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            box.GetComponent<Collider>().sharedMaterial = physicsMaterial;
        }

        private static Material CreateMaterial(string name, Color colour)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new(shader) { name = name, color = colour };
            material.hideFlags = HideFlags.DontSave;
            return material;
        }

        private static PhysicsMaterial CreatePhysicsMaterial(string name, float dynamicFriction,
            float staticFriction)
        {
            return new PhysicsMaterial(name)
            {
                hideFlags = HideFlags.DontSave,
                dynamicFriction = dynamicFriction,
                staticFriction = staticFriction,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
    }
}
