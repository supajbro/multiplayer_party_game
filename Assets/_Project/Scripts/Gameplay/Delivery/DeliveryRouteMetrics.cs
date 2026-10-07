using System;
using CouchGuys.ProceduralGeneration;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Cached, deterministic measurements of the route from the depot to a destination.</summary>
    [Serializable]
    public sealed class DeliveryRouteMetrics
    {
        [SerializeField] private bool m_isReachable;
        [SerializeField] private GridCoordinate m_roadCoordinate;
        [SerializeField] private float m_routeLength;
        [SerializeField] private float m_straightLineDistance;
        [SerializeField] private float m_cumulativeElevationGain;
        [SerializeField] private float m_maximumRoadGrade;
        [SerializeField] private int m_turnCount;
        [SerializeField] private int m_intersectionCount;
        [SerializeField] private float m_finalCarryDistance;
        [SerializeField] private float m_finalCarryElevationGain;
        [SerializeField] private float m_difficultyScore;
        [SerializeField] private DeliveryDifficultyZone m_zone;
        [SerializeField] private SuburbZone m_progressionZone;
        [SerializeField] private float m_destinationElevation;
        [SerializeField] private bool m_vanAccessible;

        public bool IsReachable => m_isReachable;
        public GridCoordinate RoadCoordinate => m_roadCoordinate;
        public float RouteLength => m_routeLength;
        public float StraightLineDistance => m_straightLineDistance;
        public float CumulativeElevationGain => m_cumulativeElevationGain;
        public float MaximumRoadGrade => m_maximumRoadGrade;
        public int TurnCount => m_turnCount;
        public int IntersectionCount => m_intersectionCount;
        public float FinalCarryDistance => m_finalCarryDistance;
        public float FinalCarryElevationGain => m_finalCarryElevationGain;
        public float DifficultyScore => m_difficultyScore;
        public DeliveryDifficultyZone Zone => m_zone;
        public SuburbZone ProgressionZone => m_progressionZone;
        public float DestinationElevation => m_destinationElevation;
        public bool VanAccessible => m_vanAccessible;

        public DeliveryRouteMetrics(
            bool isReachable,
            GridCoordinate roadCoordinate,
            float routeLength,
            float straightLineDistance,
            float cumulativeElevationGain,
            float maximumRoadGrade,
            int turnCount,
            int intersectionCount,
            float finalCarryDistance,
            float finalCarryElevationGain,
            float difficultyScore,
            DeliveryDifficultyZone zone,
            SuburbZone progressionZone = SuburbZone.Easy,
            float destinationElevation = 0f,
            bool vanAccessible = true)
        {
            m_isReachable = isReachable;
            m_roadCoordinate = roadCoordinate;
            m_routeLength = Mathf.Max(0f, routeLength);
            m_straightLineDistance = Mathf.Max(0f, straightLineDistance);
            m_cumulativeElevationGain = Mathf.Max(0f, cumulativeElevationGain);
            m_maximumRoadGrade = Mathf.Max(0f, maximumRoadGrade);
            m_turnCount = Mathf.Max(0, turnCount);
            m_intersectionCount = Mathf.Max(0, intersectionCount);
            m_finalCarryDistance = Mathf.Max(0f, finalCarryDistance);
            m_finalCarryElevationGain = Mathf.Max(0f, finalCarryElevationGain);
            m_difficultyScore = Mathf.Clamp(difficultyScore, 0f, 100f);
            m_zone = zone;
            m_progressionZone = progressionZone;
            m_destinationElevation = destinationElevation;
            m_vanAccessible = vanAccessible;
        }
    }
}
