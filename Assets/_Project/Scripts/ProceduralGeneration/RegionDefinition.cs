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
        [SerializeField] private RegionProgressionSettings m_progression = RegionProgressionSettings.SuburbsDefault;
        [SerializeField] private GeneratedTerrainSettings m_terrain = GeneratedTerrainSettings.SuburbsDefault;

        [Header("World Prefabs")]
        [SerializeField] private GameObject[] m_roadPrefabs = Array.Empty<GameObject>();
        [SerializeField] private GameObject m_groundPrefab;
        [SerializeField] private GameObject m_startingAreaPrefab;
        [SerializeField] private GameObject m_deliveryNpcPrefab;
        [Tooltip("Optional large home used for one-lot blocks. Empty uses the normal house selection.")]
        [SerializeField] private GameObject m_mansionPrefab;

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
        public RegionProgressionSettings Progression => m_progression;
        public GeneratedTerrainSettings Terrain => m_terrain;
        public GameObject[] RoadPrefabs => m_roadPrefabs;
        public GameObject GroundPrefab => m_groundPrefab;
        public GameObject StartingAreaPrefab => m_startingAreaPrefab;
        public GameObject DeliveryNpcPrefab => m_deliveryNpcPrefab;
        public GameObject MansionPrefab => m_mansionPrefab;
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
            m_progression.Validate();
            m_terrain.Validate();
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
        [InspectorName("Minimum Lots Per Block"), Min(0)] public int MinimumLotsPerBlock;
        [InspectorName("Maximum Lots Per Block"), Min(0)] public int LotsPerBlock;
        [Min(0)] public int MinimumLotSpacing;

        public static RegionGridSettings SuburbsDefault => new RegionGridSettings
        {
            GridWidth = 4,
            GridHeight = 4,
            BlockWidth = 4,
            BlockHeight = 4,
            RoadTileSize = 10f,
            MinimumLotsPerBlock = 1,
            LotsPerBlock = 3,
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
            MinimumLotsPerBlock = Mathf.Clamp(MinimumLotsPerBlock, 0, LotsPerBlock);
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
    public struct RegionProgressionSettings
    {
        public bool Enabled;
        [Range(0.05f, 0.45f)] public float EasyEndNormalized;
        [Range(0.2f, 0.7f)] public float MediumEndNormalized;
        [Range(0.5f, 0.95f)] public float HillyEndNormalized;
        [Min(1)] public int DepotFlatRadiusTiles;

        [Header("Elevation Shape")]
        [Range(0.5f, 3f)] public float ElevationTrendExponent;
        [Min(0f)] public float ElevationNoiseAmplitude;
        [Range(0.005f, 0.5f)] public float ElevationNoiseFrequency;
        [Range(1f, 2f)] public float OuterElevationMultiplier;

        [Header("Vehicle Safety")]
        [Range(1f, 30f)] public float PrimaryRoadMaximumSlope;
        [Range(1f, 45f)] public float ResidentialRoadMaximumSlope;

        [Header("Validation")]
        [Min(1)] public int MinimumDestinationsPerZone;
        [Range(1, 5)] public int MaximumGenerationAttempts;

        public static RegionProgressionSettings SuburbsDefault => new RegionProgressionSettings
        {
            Enabled = true,
            EasyEndNormalized = 0.28f,
            MediumEndNormalized = 0.55f,
            HillyEndNormalized = 0.8f,
            DepotFlatRadiusTiles = 4,
            ElevationTrendExponent = 1.15f,
            ElevationNoiseAmplitude = 7f,
            ElevationNoiseFrequency = 0.075f,
            OuterElevationMultiplier = 1.2f,
            PrimaryRoadMaximumSlope = 10f,
            ResidentialRoadMaximumSlope = 18f,
            MinimumDestinationsPerZone = 2,
            MaximumGenerationAttempts = 3
        };

        public void Validate()
        {
            EasyEndNormalized = Mathf.Clamp(EasyEndNormalized, 0.05f, 0.45f);
            MediumEndNormalized = Mathf.Clamp(MediumEndNormalized, EasyEndNormalized + 0.05f, 0.75f);
            HillyEndNormalized = Mathf.Clamp(HillyEndNormalized, MediumEndNormalized + 0.05f, 0.95f);
            DepotFlatRadiusTiles = Mathf.Max(1, DepotFlatRadiusTiles);
            ElevationTrendExponent = Mathf.Clamp(ElevationTrendExponent, 0.5f, 3f);
            ElevationNoiseAmplitude = Mathf.Max(0f, ElevationNoiseAmplitude);
            ElevationNoiseFrequency = Mathf.Clamp(ElevationNoiseFrequency, 0.005f, 0.5f);
            OuterElevationMultiplier = Mathf.Clamp(OuterElevationMultiplier, 1f, 2f);
            PrimaryRoadMaximumSlope = Mathf.Clamp(PrimaryRoadMaximumSlope, 1f, 30f);
            ResidentialRoadMaximumSlope = Mathf.Clamp(
                ResidentialRoadMaximumSlope,
                PrimaryRoadMaximumSlope,
                45f);
            MinimumDestinationsPerZone = Mathf.Max(1, MinimumDestinationsPerZone);
            MaximumGenerationAttempts = Mathf.Clamp(MaximumGenerationAttempts, 1, 5);
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
