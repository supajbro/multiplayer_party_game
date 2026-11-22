using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    public enum TrafficDrivingSide
    {
        AustralianLeft,
        AmericanRight
    }

    [CreateAssetMenu(fileName = "TrafficSettings", menuName = "Couch Guys/Traffic/Traffic Settings")]
    public sealed class TrafficSettings : ScriptableObject
    {
        [SerializeField] private GameObject m_carPrefab;
        [SerializeField] private TrafficDrivingSide m_drivingSide = TrafficDrivingSide.AustralianLeft;
        [SerializeField, Min(0)] private int m_maximumActiveVehicles = 8;
        [SerializeField, Min(0.25f)] private float m_spawnInterval = 5f;
        [SerializeField, Min(0f)] private float m_minimumPlayerSpawnDistance = 24f;
        [SerializeField, Min(10f)] private float m_despawnDistance = 450f;
        [SerializeField, Min(1f)] private float m_minimumVehicleSpawnDistance = 14f;
        [SerializeField, Range(0.12f, 0.35f)] private float m_laneOffsetFraction = 0.22f;
        [Header("Individual Routes")]
        [Tooltip("Cars explore this many road tiles with randomized junction choices before heading to an exit tunnel.")]
        [SerializeField, Min(2)] private int m_minimumRandomRouteTiles = 40;
        [SerializeField, Min(2)] private int m_maximumRandomRouteTiles = 140;

        [Header("Server Simulation Distance")]
        [SerializeField, Min(10f)] private float m_fullPhysicsDistance = 90f;
        [SerializeField, Min(10f)] private float m_fullPhysicsReleaseDistance = 130f;
        [SerializeField, Min(0f), Tooltip("Extra approach time for vehicle speed, player speed and network delay.")]
        private float m_approachBufferSeconds = 1f;
        [SerializeField, Min(0.05f)] private float m_simulationCheckInterval = 0.2f;
        [SerializeField, Min(0.05f)] private float m_distantUpdateInterval = 0.2f;
        [SerializeField, Range(1, 30), Tooltip("Network ticks between distant car pose updates. Nearby cars use the prefab's original interval.")]
        private byte m_distantNetworkInterval = 6;
        [SerializeField, Range(1, 30)] private byte m_nearbyNetworkInterval = 1;

        public GameObject CarPrefab => m_carPrefab;
        public TrafficDrivingSide DrivingSide => m_drivingSide;
        public int MaximumActiveVehicles => m_maximumActiveVehicles;
        public float SpawnInterval => m_spawnInterval;
        public float MinimumPlayerSpawnDistance => m_minimumPlayerSpawnDistance;
        public float DespawnDistance => Mathf.Max(m_despawnDistance, FullPhysicsReleaseDistance + 60f);
        public float FullPhysicsDistance => Mathf.Max(10f, m_fullPhysicsDistance);
        public float FullPhysicsReleaseDistance => Mathf.Max(FullPhysicsDistance + 5f, m_fullPhysicsReleaseDistance);
        public float ApproachBufferSeconds => Mathf.Max(0f, m_approachBufferSeconds);
        public float SimulationCheckInterval => Mathf.Max(0.05f, m_simulationCheckInterval);
        public float DistantUpdateInterval => Mathf.Max(0.05f, m_distantUpdateInterval);
        public byte DistantNetworkInterval => (byte)Mathf.Clamp(m_distantNetworkInterval, NearbyNetworkInterval, 30);
        public byte NearbyNetworkInterval => (byte)Mathf.Clamp(m_nearbyNetworkInterval, 1, 30);
        public float MinimumVehicleSpawnDistance => m_minimumVehicleSpawnDistance;
        public float LaneOffsetFraction => m_laneOffsetFraction;
        public int MinimumRandomRouteTiles => m_minimumRandomRouteTiles;
        public int MaximumRandomRouteTiles => m_maximumRandomRouteTiles;

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_maximumActiveVehicles = Mathf.Max(0, m_maximumActiveVehicles);
            m_spawnInterval = Mathf.Max(0.25f, m_spawnInterval);
            m_minimumPlayerSpawnDistance = Mathf.Max(0f, m_minimumPlayerSpawnDistance);
            m_despawnDistance = Mathf.Max(10f, m_despawnDistance);
            m_minimumVehicleSpawnDistance = Mathf.Max(1f, m_minimumVehicleSpawnDistance);
            m_minimumRandomRouteTiles = Mathf.Max(2, m_minimumRandomRouteTiles);
            m_maximumRandomRouteTiles = Mathf.Max(m_minimumRandomRouteTiles, m_maximumRandomRouteTiles);
            m_fullPhysicsDistance = FullPhysicsDistance;
            m_fullPhysicsReleaseDistance = FullPhysicsReleaseDistance;
            m_despawnDistance = DespawnDistance;
            m_approachBufferSeconds = ApproachBufferSeconds;
            m_simulationCheckInterval = SimulationCheckInterval;
            m_distantUpdateInterval = DistantUpdateInterval;
            m_nearbyNetworkInterval = NearbyNetworkInterval;
            m_distantNetworkInterval = DistantNetworkInterval;
        }
#endif
    }
}
