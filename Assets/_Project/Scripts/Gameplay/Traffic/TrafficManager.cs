using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Networking;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    /// <summary>Spawns only on the server and reuses routes built once per generated region.</summary>
    [DisallowMultipleComponent]
    public sealed partial class TrafficManager : MonoBehaviour
    {
        private const string TrafficSettingsResource = "Traffic/TrafficSettings";
        private readonly List<NetworkObject> m_activeVehicles = new List<NetworkObject>(12);
        private readonly Collider[] m_spawnHits = new Collider[32];
        private readonly TrafficLaneGraph m_laneGraph = new TrafficLaneGraph();
        private readonly List<int> m_routeOrder = new List<int>(64);
        private readonly System.Random m_routeRandom = new System.Random();
        private readonly List<TrafficObserver> m_observers = new(8);
        private readonly Dictionary<PlayerHealth, Vector3> m_previousPlayerPositions = new();
        private readonly List<PlayerHealth> m_removedPlayers = new();
        private float m_lastObserverUpdate;

        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private TrafficSettings m_settings;
        [SerializeField] private NetworkObject m_carPrefab;
        [SerializeField, TextArea] private string m_trafficStatus = "Waiting for neighbourhood generation.";

        private float m_nextSpawnTime;
        private float m_nextPruneTime;
        private bool m_regionReady;
        private int m_routeCursor;

        public int ActiveVehicleCount => m_activeVehicles.Count;

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
            m_settings ??= Resources.Load<TrafficSettings>(TrafficSettingsResource);
            if (m_carPrefab == null)
            {
                GameObject prefab = m_settings != null ? m_settings.CarPrefab : null;
                if (prefab != null) m_carPrefab = prefab.GetComponent<NetworkObject>();
            }
        }

        private void OnEnable()
        {
            NetworkPlayerOwnership.ServerPlayerStarted += OnTrafficPlayerChanged;
            NetworkPlayerOwnership.ServerPlayerStopped += OnTrafficPlayerChanged;
            if (m_generator == null) return;
            m_generator.RegionClearing += OnRegionClearing;
            m_generator.RegionGenerated += OnRegionGenerated;
            if (m_generator.IsGenerationReady) OnRegionGenerated(m_generator);
        }

        private void OnDisable()
        {
            NetworkPlayerOwnership.ServerPlayerStarted -= OnTrafficPlayerChanged;
            NetworkPlayerOwnership.ServerPlayerStopped -= OnTrafficPlayerChanged;
            ClearRogueCars();
            if (m_generator == null) return;
            m_generator.RegionClearing -= OnRegionClearing;
            m_generator.RegionGenerated -= OnRegionGenerated;
        }

        private void OnTrafficPlayerChanged(NetworkPlayerOwnership player) => m_nextPruneTime = 0f;

        private void Update()
        {
            UpdateRogueSpawning();
            if (!m_regionReady || m_settings == null || !IsServerStarted()) return;
            if (Time.time >= m_nextPruneTime)
            {
                RefreshObservers();
                m_nextPruneTime = Time.time + m_settings.SimulationCheckInterval;
                PruneVehicles();
            }
            if (Time.time < m_nextSpawnTime ||
                m_activeVehicles.Count >= m_settings.MaximumActiveVehicles || m_observers.Count == 0) return;

            TrySpawnVehicle();
            m_nextSpawnTime = Time.time + m_settings.SpawnInterval;
        }

        internal void DespawnVehicle(TrafficVehicleController vehicle)
        {
            if (vehicle == null || !IsServerStarted()) return;
            NetworkObject networkObject = vehicle.NetworkObject;
            m_activeVehicles.Remove(networkObject);
            if (networkObject != null && networkObject.IsSpawned)
                InstanceFinder.ServerManager.Despawn(networkObject,
                    vehicle.CanPool ? DespawnType.Pool : DespawnType.Destroy);
        }

        private void OnRegionGenerated(NeighbourhoodGenerator generator)
        {
            m_laneGraph.Build(generator, m_settings);
            m_regionReady = m_laneGraph.Routes.Count > 0 && m_carPrefab != null;
            m_trafficStatus = m_regionReady
                ? $"Ready: {m_laneGraph.Routes.Count} routes through {generator.TrafficRoadPortals.Count} tunnel portals."
                : m_laneGraph.BuildFailureReason ?? "Traffic car prefab is missing.";
            if (!m_regionReady)
                Debug.LogWarning($"Traffic disabled for seed {generator.CurrentSeed}: {m_trafficStatus}", this);
            RebuildRouteOrder();
            m_nextSpawnTime = Time.time + (m_settings != null ? m_settings.SpawnInterval : 5f);
        }

        private void OnRegionClearing(NeighbourhoodGenerator generator)
        {
            ClearRogueCars();
            m_regionReady = false;
            m_trafficStatus = "Waiting for neighbourhood generation.";
            m_laneGraph.Clear();
            m_routeOrder.Clear();
            m_routeCursor = 0;
            DespawnAllVehicles();
        }

        private void TrySpawnVehicle()
        {
            if (m_carPrefab == null || m_laneGraph.Routes.Count == 0) return;
            for (int offset = 0; offset < m_laneGraph.Routes.Count; offset++)
            {
                if (m_routeCursor >= m_routeOrder.Count) ShuffleRouteOrder();
                int routeIndex = m_routeOrder[m_routeCursor++];
                TrafficRoute route = m_laneGraph.Routes[routeIndex];
                if (route.Waypoints.Length < 2) continue;
                if (m_laneGraph.TryCreateRandomRoute(route, m_settings, m_routeRandom, out var individualRoute))
                    route = individualRoute;

                int start = FindSpawnWaypoint(route);
                if (start < 0) continue;
                Vector3 direction = Vector3.ProjectOnPlane(
                    route.Waypoints[start + 1] - route.Waypoints[start], Vector3.up).normalized;
                Vector3 position = route.Waypoints[start] + Vector3.up * 0.7f;
                NetworkObject instance = InstanceFinder.NetworkManager.GetPooledInstantiated(
                    m_carPrefab, position, Quaternion.LookRotation(direction, Vector3.up), asServer: true);
                if (instance == null) return;
                TrafficVehicleController vehicle = instance.GetComponent<TrafficVehicleController>();
                if (vehicle == null)
                {
                    Destroy(instance.gameObject);
                    return;
                }

                InstanceFinder.ServerManager.Spawn(instance);
                // Assign after FishNet has completed its spawn callbacks. The route is
                // server-only runtime data and must not be cleared during initialization.
                int visualVariant = vehicle.VisualVariantCount > 0
                    ? Random.Range(0, vehicle.VisualVariantCount)
                    : 0;
                vehicle.InitialiseServer(route, this, visualVariant, start);
                vehicle.RefreshSimulationServer(m_observers, m_settings);
                m_activeVehicles.Add(instance);
                return;
            }
        }

        private void RebuildRouteOrder()
        {
            m_routeOrder.Clear();
            for (int index = 0; index < m_laneGraph.Routes.Count; index++)
                m_routeOrder.Add(index);
            ShuffleRouteOrder();
        }

        private int FindSpawnWaypoint(TrafficRoute route)
        {
            // If a tunnel is beyond every player's simulation area, enter the route
            // at a distant waypoint instead of repeatedly spawning and culling there.
            for (int i = 0; i < route.Waypoints.Length - 1; i++)
            {
                Vector3 position = route.Waypoints[i];
                float nearestSquared = float.PositiveInfinity;
                foreach (var observer in m_observers)
                    nearestSquared = Mathf.Min(nearestSquared, (observer.Position - position).sqrMagnitude);
                float minimum = Mathf.Max(m_settings.MinimumPlayerSpawnDistance,
                    m_settings.FullPhysicsReleaseDistance + 30f);
                float maximum = m_settings.DespawnDistance - 20f;
                if (nearestSquared < minimum * minimum || nearestSquared > maximum * maximum) continue;
                if (IsSpawnClear(position)) return i;
            }
            return -1;
        }

        private void ShuffleRouteOrder()
        {
            for (int index = m_routeOrder.Count - 1; index > 0; index--)
            {
                int swapIndex = Random.Range(0, index + 1);
                (m_routeOrder[index], m_routeOrder[swapIndex]) =
                    (m_routeOrder[swapIndex], m_routeOrder[index]);
            }
            m_routeCursor = 0;
        }

        private bool IsSpawnClear(Vector3 position)
        {
            float radius = Mathf.Max(m_settings.MinimumPlayerSpawnDistance,
                m_settings.MinimumVehicleSpawnDistance);
            int count = Physics.OverlapSphereNonAlloc(position, radius, m_spawnHits, ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider hit = m_spawnHits[index];
                if (hit == null) continue;
                if (hit.GetComponentInParent<PlayerHealth>() != null &&
                    (hit.transform.position - position).sqrMagnitude <
                    m_settings.MinimumPlayerSpawnDistance * m_settings.MinimumPlayerSpawnDistance)
                    return false;
                if ((hit.GetComponentInParent<TrafficVehicleController>() != null ||
                     hit.GetComponentInParent<ThiefHealth>() != null) &&
                    (hit.transform.position - position).sqrMagnitude <
                    m_settings.MinimumVehicleSpawnDistance * m_settings.MinimumVehicleSpawnDistance)
                    return false;
            }
            return true;
        }

        private void PruneVehicles()
        {
            for (int index = m_activeVehicles.Count - 1; index >= 0; index--)
            {
                NetworkObject vehicle = m_activeVehicles[index];
                if (vehicle == null || !vehicle.IsSpawned)
                {
                    m_activeVehicles.RemoveAt(index);
                    continue;
                }
                var controller = vehicle.GetComponent<TrafficVehicleController>();
                if (controller == null || controller.IsDestroyed || controller.IsBatLaunched || controller.HasRecentInteraction) continue;
                if (TrafficSimulationPolicy.CanDespawn(vehicle.transform.position, controller.DrivingSpeed,
                        m_observers, m_settings))
                {
                    DespawnVehicle(controller);
                }
                else controller.RefreshSimulationServer(m_observers, m_settings);
            }
        }

        private void DespawnAllVehicles()
        {
            if (IsServerStarted())
            {
                for (int index = m_activeVehicles.Count - 1; index >= 0; index--)
                {
                    NetworkObject vehicle = m_activeVehicles[index];
                    if (vehicle != null && vehicle.IsSpawned)
                    {
                        var controller = vehicle.GetComponent<TrafficVehicleController>();
                        InstanceFinder.ServerManager.Despawn(vehicle,
                            controller != null && controller.CanPool ? DespawnType.Pool : DespawnType.Destroy);
                    }
                }
            }
            m_activeVehicles.Clear();
            m_observers.Clear();
            m_previousPlayerPositions.Clear();
            m_nextPruneTime = 0f;
        }

        private void RefreshObservers()
        {
            float elapsed = Time.time - m_lastObserverUpdate;
            m_lastObserverUpdate = Time.time;
            m_observers.Clear();
            foreach (var player in PlayerHealth.Instances)
            {
                // Downed/dead players still have a view of the world. AI teammates
                // also retain physical traffic; unowned hostile bots are excluded.
                if (!IsTrafficObserver(player)) continue;
                Vector3 position = player.transform.position;
                float speed = elapsed > 0f && m_previousPlayerPositions.TryGetValue(player, out var previous)
                    ? Vector3.Distance(position, previous) / elapsed : 0f;
                m_observers.Add(new TrafficObserver(position, speed));
                m_previousPlayerPositions[player] = position;
            }
            m_removedPlayers.Clear();
            foreach (var entry in m_previousPlayerPositions)
                if (!IsTrafficObserver(entry.Key)) m_removedPlayers.Add(entry.Key);
            foreach (var player in m_removedPlayers) m_previousPlayerPositions.Remove(player);
        }

        private static bool IsTrafficObserver(PlayerHealth player) => player != null &&
            player.isActiveAndEnabled && player.IsServerInitialized && player.IsSpawned &&
            (player.IsAiTeammate || (player.Owner.IsValid && player.Owner.IsActive));

        private static bool IsServerStarted() =>
            InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.ServerManager.Started;
    }
}
