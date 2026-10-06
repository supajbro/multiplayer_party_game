using System;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [CreateAssetMenu(menuName = "Couch Guys/World/Region Definition", fileName = "Region_")]
    public sealed class RegionDefinition : ScriptableObject
    {
        [Header("Identity / Progression")]
        [SerializeField] private string m_regionId = "suburbs";
        [SerializeField] private string m_displayName = "Suburbs";
        [SerializeField, TextArea] private string m_description;
        [SerializeField, Min(0)] private int m_unlockCost;
        [SerializeField, Range(1, 10)] private int m_difficulty = 1;

        [Header("Layout")]
        [SerializeField] private RegionRoadGenerationStrategy m_roadStrategy;
        [SerializeField] private RegionGridSettings m_grid = RegionGridSettings.SuburbsDefault;
        [SerializeField] private RegionElevationSettings m_elevation = RegionElevationSettings.SuburbsDefault;

        [Header("World Prefabs")]
        [SerializeField] private GameObject[] m_roadPrefabs = Array.Empty<GameObject>();
        [SerializeField] private GameObject m_groundPrefab;
        [SerializeField] private GameObject m_startingAreaPrefab;
        [SerializeField] private GameObject m_deliveryNpcPrefab;

        [Header("Lots")]
        [SerializeField] private WeightedLot[] m_lots = Array.Empty<WeightedLot>();
        [SerializeField, Range(0f, 1f)] private float m_lotDensity = 1f;
        [SerializeField, Min(0)] private int m_minimumLandmarks;
        [SerializeField, Min(0)] private int m_maximumLandmarks;

        [Header("Environment")]
        [SerializeField] private RegionSpawnRule[] m_props = Array.Empty<RegionSpawnRule>();
        [SerializeField] private RegionSpawnRule[] m_hazards = Array.Empty<RegionSpawnRule>();

        [Header("Navigation")]
        [SerializeField] private bool m_buildNavMesh = true;

        public string RegionId => m_regionId;
        public string DisplayName => string.IsNullOrWhiteSpace(m_displayName) ? name : m_displayName;
        public string Description => m_description;
        public int UnlockCost => m_unlockCost;
        public int Difficulty => m_difficulty;
        public RegionRoadGenerationStrategy RoadStrategy => m_roadStrategy;
        public RegionGridSettings Grid => m_grid;
        public RegionElevationSettings Elevation => m_elevation;
        public GameObject[] RoadPrefabs => m_roadPrefabs;
        public GameObject GroundPrefab => m_groundPrefab;
        public GameObject StartingAreaPrefab => m_startingAreaPrefab;
        public GameObject DeliveryNpcPrefab => m_deliveryNpcPrefab;
        public WeightedLot[] Lots => m_lots;
        public float LotDensity => m_lotDensity;
        public int MinimumLandmarks => m_minimumLandmarks;
        public int MaximumLandmarks => Mathf.Max(m_minimumLandmarks, m_maximumLandmarks);
        public RegionSpawnRule[] Props => m_props;
        public RegionSpawnRule[] Hazards => m_hazards;
        public bool BuildNavMesh => m_buildNavMesh;

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_regionId = (m_regionId ?? string.Empty).Trim().ToLowerInvariant();
            m_unlockCost = Mathf.Max(0, m_unlockCost);
            m_difficulty = Mathf.Clamp(m_difficulty, 1, 10);
            m_lotDensity = Mathf.Clamp01(m_lotDensity);
            m_minimumLandmarks = Mathf.Max(0, m_minimumLandmarks);
            m_maximumLandmarks = Mathf.Max(m_minimumLandmarks, m_maximumLandmarks);
            m_grid.Validate();
            m_elevation.Validate();
        }
#endif
    }

    [Serializable]
    public struct RegionGridSettings
    {
        [Min(1)] public int GridWidth;
        [Min(1)] public int GridHeight;
        [Min(1)] public int BlockWidth;
        [Min(1)] public int BlockHeight;
        [Min(0.1f)] public float RoadTileSize;
        [Min(0)] public int LotsPerBlock;
        [Min(0)] public int MinimumLotSpacing;

        public static RegionGridSettings SuburbsDefault => new RegionGridSettings
        {
            GridWidth = 4,
            GridHeight = 4,
            BlockWidth = 4,
            BlockHeight = 4,
            RoadTileSize = 10f,
            LotsPerBlock = 6,
            MinimumLotSpacing = 1
        };

        public void Validate()
        {
            GridWidth = Mathf.Max(1, GridWidth);
            GridHeight = Mathf.Max(1, GridHeight);
            BlockWidth = Mathf.Max(1, BlockWidth);
            BlockHeight = Mathf.Max(1, BlockHeight);
            RoadTileSize = Mathf.Max(0.1f, RoadTileSize);
            LotsPerBlock = Mathf.Max(0, LotsPerBlock);
            MinimumLotSpacing = Mathf.Max(0, MinimumLotSpacing);
        }
    }

    [Serializable]
    public struct RegionElevationSettings
    {
        public bool Enabled;
        [Min(0.1f)] public float MinimumStep;
        [Min(0.1f)] public float MaximumStep;
        [Min(0f)] public float MaximumElevation;
        [Range(1f, 60f)] public float MaximumRoadSlope;

        public static RegionElevationSettings SuburbsDefault => new RegionElevationSettings
        {
            Enabled = true,
            MinimumStep = 2f,
            MaximumStep = 8f,
            MaximumElevation = 24f,
            MaximumRoadSlope = 45f
        };

        public void Validate()
        {
            MaximumStep = Mathf.Max(0.1f, MaximumStep);
            MinimumStep = Mathf.Clamp(MinimumStep, 0.1f, MaximumStep);
            MaximumElevation = Mathf.Max(0f, MaximumElevation);
            MaximumRoadSlope = Mathf.Clamp(MaximumRoadSlope, 1f, 60f);
        }
    }

    [Serializable]
    public struct WeightedLot
    {
        public LotDefinition Definition;
        [Min(0.01f)] public float Weight;
    }

    [Serializable]
    public struct RegionSpawnRule
    {
        public GameObject Prefab;
        [Min(0.01f)] public float Weight;
        [Min(0)] public int MinimumCount;
        [Min(0)] public int MaximumCount;
        public RegionObjectPlacement Placement;
        [Min(0f)] public float MinimumRoadOffset;
        [Min(0f)] public float MaximumRoadOffset;
    }

    public enum RegionObjectPlacement
    {
        Roadside,
        OpenLot,
        AnyGeneratedGround
    }
}
