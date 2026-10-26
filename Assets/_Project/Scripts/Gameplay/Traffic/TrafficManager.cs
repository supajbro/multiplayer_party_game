using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
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
        private const string CarPrefabResource = "Traffic/TrafficCar";
        private readonly List<NetworkObject> m_activeVehicles = new List<NetworkObject>(12);
        private readonly Collider[] m_spawnHits = new Collider[32];
        private readonly TrafficLaneGraph m_laneGraph = new TrafficLaneGraph();
        private readonly List<int> m_routeOrder = new List<int>(64);

        [SerializeField] private NeighbourhoodGenerator m_generator;
        [SerializeField] private TrafficSettings m_settings;
        [SerializeField] private NetworkObject m_carPrefab;

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
                GameObject prefab = Resources.Load<GameObject>(CarPrefabResource);
                if (prefab != null) m_carPrefab = prefab.GetComponent<NetworkObject>();
            }
        }

        private void OnEnable()
        {
            if (m_generator == null) return;
            m_generator.RegionClearing += OnRegionClearing;
            m_generator.RegionGenerated += OnRegionGenerated;
            if (m_generator.IsGenerationReady) OnRegionGenerated(m_generator);
        }

        private void OnDisable()
        {
            ClearRogueCars();
            if (m_generator == null) return;
            m_generator.RegionClearing -= OnRegionClearing;
            m_generator.RegionGenerated -= OnRegionGenerated;
        }

        private void Update()
        {
            UpdateRogueSpawning();
            if (!m_regionReady || m_settings == null || !IsServerStarted()) return;
            if (Time.time >= m_nextPruneTime)
            {
                m_nextPruneTime = Time.time + 1f;
                PruneVehicles();
            }
            if (Time.time < m_nextSpawnTime ||
                m_activeVehicles.Count >= m_settings.MaximumActiveVehicles) return;

            TrySpawnVehicle();
            m_nextSpawnTime = Time.time + m_settings.SpawnInterval;
        }

        internal void DespawnVehicle(TrafficVehicleController vehicle)
        {
            if (vehicle == null || !IsServerStarted()) return;
            NetworkObject networkObject = vehicle.NetworkObject;
            m_activeVehicles.Remove(networkObject);
            if (networkObject != null && networkObject.IsSpawned)
                InstanceFinder.ServerManager.Despawn(networkObject);
        }

        private void OnRegionGenerated(NeighbourhoodGenerator generator)
        {
            m_laneGraph.Build(generator, m_settings);
            m_regionReady = m_laneGraph.Routes.Count > 0;
            RebuildRouteOrder();
            m_nextSpawnTime = Time.time + (m_settings != null ? m_settings.SpawnInterval : 5f);
        }

        private void OnRegionClearing(NeighbourhoodGenerator generator)
        {
            ClearRogueCars();
            m_regionReady = false;
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
                if (route.Waypoints.Length < 2 || !IsSpawnClear(route.Waypoints[0])) continue;

                Vector3 direction = Vector3.ProjectOnPlane(
                    route.Waypoints[1] - route.Waypoints[0], Vector3.up).normalized;
                Vector3 position = route.Waypoints[0] + Vector3.up * 0.7f;
                NetworkObject instance = Instantiate(m_carPrefab, position,
                    Quaternion.LookRotation(direction, Vector3.up));
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
                vehicle.InitialiseServer(route, this, visualVariant);
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
            float maximumDistanceSquared = m_settings.DespawnDistance * m_settings.DespawnDistance;
            Vector3 centre = m_generator.GeneratedWorldBounds.center;
            for (int index = m_activeVehicles.Count - 1; index >= 0; index--)
            {
                NetworkObject vehicle = m_activeVehicles[index];
                if (vehicle == null || !vehicle.IsSpawned)
                {
                    m_activeVehicles.RemoveAt(index);
                    continue;
                }
                if ((vehicle.transform.position - centre).sqrMagnitude > maximumDistanceSquared)
                {
                    InstanceFinder.ServerManager.Despawn(vehicle);
                    m_activeVehicles.RemoveAt(index);
                }
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
                        InstanceFinder.ServerManager.Despawn(vehicle);
                }
            }
            m_activeVehicles.Clear();
        }

        private static bool IsServerStarted() =>
            InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.ServerManager.Started;
    }
}
