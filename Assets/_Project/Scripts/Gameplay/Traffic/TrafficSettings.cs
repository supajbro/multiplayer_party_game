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
        [SerializeField] private TrafficDrivingSide m_drivingSide = TrafficDrivingSide.AustralianLeft;
        [SerializeField, Min(0)] private int m_maximumActiveVehicles = 8;
        [SerializeField, Min(0.25f)] private float m_spawnInterval = 5f;
        [SerializeField, Min(0f)] private float m_minimumPlayerSpawnDistance = 24f;
        [SerializeField, Min(10f)] private float m_despawnDistance = 450f;
        [SerializeField, Min(1f)] private float m_minimumVehicleSpawnDistance = 14f;
        [SerializeField, Range(0.12f, 0.35f)] private float m_laneOffsetFraction = 0.22f;

        public TrafficDrivingSide DrivingSide => m_drivingSide;
        public int MaximumActiveVehicles => m_maximumActiveVehicles;
        public float SpawnInterval => m_spawnInterval;
        public float MinimumPlayerSpawnDistance => m_minimumPlayerSpawnDistance;
        public float DespawnDistance => m_despawnDistance;
        public float MinimumVehicleSpawnDistance => m_minimumVehicleSpawnDistance;
        public float LaneOffsetFraction => m_laneOffsetFraction;

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_maximumActiveVehicles = Mathf.Max(0, m_maximumActiveVehicles);
            m_spawnInterval = Mathf.Max(0.25f, m_spawnInterval);
            m_minimumPlayerSpawnDistance = Mathf.Max(0f, m_minimumPlayerSpawnDistance);
            m_despawnDistance = Mathf.Max(10f, m_despawnDistance);
            m_minimumVehicleSpawnDistance = Mathf.Max(1f, m_minimumVehicleSpawnDistance);
        }
#endif
    }
}
