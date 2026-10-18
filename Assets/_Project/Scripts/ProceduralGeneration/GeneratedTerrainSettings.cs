using System;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [Flags]
    public enum NeighbourhoodBorderType
    {
        None = 0,
        Forest = 1 << 0,
        Mountain = 1 << 1,
        Construction = 1 << 2,
        All = Forest | Mountain | Construction
    }

    [Serializable]
    public struct NeighbourhoodBorderSettings
    {
        public bool Enabled;
        [Min(4f)] public float BorderWidth;
        public NeighbourhoodBorderType EnabledTypes;
        [Range(0.1f, 1f)] public float SceneryDensity;
        [Min(2f)] public float MinimumSectionLength;
        [Min(2f)] public float MaximumSectionLength;

        [Header("Hilly Perimeter")]
        [Min(1f)] public float HillHeight;
        [Min(4f)] public float HillDepth;
        [Range(0.25f, 4f)] public float SlopeSmoothness;
        [Min(0f)] public float HillNoiseAmount;
        [Min(0.001f)] public float HillNoiseFrequency;
        [Range(0f, 1f)] public float HillVariation;

        [Header("Hill Trees")]
        [Range(0f, 1f)] public float TreeDensity;
        [Min(1f)] public float MinimumTreeSpacing;
        [Min(1f)] public float MaximumTreeSpacing;
        [Min(0f)] public float TunnelClearance;
        [Range(0f, 45f)] public float MaximumTreeSlope;
        public Vector2 TreeScaleRange;
        public GameObject[] TreePrefabs;

        [Header("Road Tunnels")]
        public GameObject StandardTunnelPrefab;
        public GameObject SummitGatewayPrefab;
        [Min(3f)] public float TunnelWidth;
        [Min(3f)] public float TunnelHeight;
        [Min(2f)] public float TunnelDepth;

        [Header("Optional Art Prefabs")]
        public GameObject[] ForestPrefabs;
        public GameObject[] MountainPrefabs;
        public GameObject[] ConstructionPrefabs;
        public GameObject[] RoadClosurePrefabs;

        [Header("Containment")]
        [Min(2f)] public float ColliderHeightAboveTerrain;
        [Min(1f)] public float ColliderDepthBelowTerrain;
        [Min(0.2f)] public float ColliderThickness;
        [Min(1f)] public float FallRecoveryDepth;

        public static NeighbourhoodBorderSettings SuburbsDefault =>
            new NeighbourhoodBorderSettings
            {
                Enabled = true,
                BorderWidth = 18f,
                EnabledTypes = NeighbourhoodBorderType.All,
                SceneryDensity = 0.65f,
                MinimumSectionLength = 28f,
                MaximumSectionLength = 65f,
                HillHeight = 24f,
                HillDepth = 80f,
                SlopeSmoothness = 1.35f,
                HillNoiseAmount = 5f,
                HillNoiseFrequency = 0.018f,
                HillVariation = 0.55f,
                TreeDensity = 0.62f,
                MinimumTreeSpacing = 8f,
                MaximumTreeSpacing = 15f,
                TunnelClearance = 8f,
                MaximumTreeSlope = 38f,
                TreeScaleRange = new Vector2(0.8f, 1.35f),
                TreePrefabs = Array.Empty<GameObject>(),
                StandardTunnelPrefab = null,
                SummitGatewayPrefab = null,
                TunnelWidth = 12f,
                TunnelHeight = 8f,
                TunnelDepth = 12f,
                ForestPrefabs = Array.Empty<GameObject>(),
                MountainPrefabs = Array.Empty<GameObject>(),
                ConstructionPrefabs = Array.Empty<GameObject>(),
                RoadClosurePrefabs = Array.Empty<GameObject>(),
                ColliderHeightAboveTerrain = 18f,
                ColliderDepthBelowTerrain = 12f,
                ColliderThickness = 2f,
                FallRecoveryDepth = 18f
            };

        public void Validate()
        {
            BorderWidth = Mathf.Max(4f, BorderWidth);
            SceneryDensity = Mathf.Clamp(SceneryDensity, 0.1f, 1f);
            MinimumSectionLength = Mathf.Max(2f, MinimumSectionLength);
            MaximumSectionLength = Mathf.Max(MinimumSectionLength, MaximumSectionLength);
            HillHeight = Mathf.Max(1f, HillHeight);
            HillDepth = Mathf.Max(4f, HillDepth);
            SlopeSmoothness = Mathf.Clamp(SlopeSmoothness, 0.25f, 4f);
            HillNoiseAmount = Mathf.Max(0f, HillNoiseAmount);
            HillNoiseFrequency = Mathf.Max(0.001f, HillNoiseFrequency);
            HillVariation = Mathf.Clamp01(HillVariation);
            TreeDensity = Mathf.Clamp01(TreeDensity);
            MinimumTreeSpacing = Mathf.Max(1f, MinimumTreeSpacing);
            MaximumTreeSpacing = Mathf.Max(MinimumTreeSpacing, MaximumTreeSpacing);
            TunnelClearance = Mathf.Max(0f, TunnelClearance);
            MaximumTreeSlope = Mathf.Clamp(MaximumTreeSlope, 0f, 45f);
            if (TreeScaleRange.x <= 0f) TreeScaleRange.x = 0.8f;
            if (TreeScaleRange.y <= 0f) TreeScaleRange.y = 1.35f;
            TreeScaleRange.y = Mathf.Max(TreeScaleRange.x, TreeScaleRange.y);
            TunnelWidth = Mathf.Max(3f, TunnelWidth);
            TunnelHeight = Mathf.Max(3f, TunnelHeight);
            TunnelDepth = Mathf.Max(2f, TunnelDepth);
            ColliderHeightAboveTerrain = Mathf.Max(2f, ColliderHeightAboveTerrain);
            ColliderDepthBelowTerrain = Mathf.Max(1f, ColliderDepthBelowTerrain);
            ColliderThickness = Mathf.Max(0.2f, ColliderThickness);
            FallRecoveryDepth = Mathf.Max(1f, FallRecoveryDepth);
            ForestPrefabs ??= Array.Empty<GameObject>();
            MountainPrefabs ??= Array.Empty<GameObject>();
            ConstructionPrefabs ??= Array.Empty<GameObject>();
            RoadClosurePrefabs ??= Array.Empty<GameObject>();
            TreePrefabs ??= Array.Empty<GameObject>();
        }
    }

    [Serializable]
    public struct LandmarkRoadSettings
    {
        public bool Enabled;
        [Min(6)] public int UphillLengthTiles;
        [Min(5f)] public float SummitHeight;
        [Range(1f, 30f)] public float MaximumSlope;
        [Range(0.5f, 3f)] public float ElevationCurveExponent;
        [Min(1f)] public float EmbankmentBlendWidth;
        public bool GenerateGuardRails;
        [Min(0.2f)] public float GuardRailHeight;
        [Min(1)] public int GuardRailPostIntervalTiles;
        [Min(2f)] public float TransitionTriggerDepth;
        public string NextRegionId;

        public static LandmarkRoadSettings SuburbsDefault => new LandmarkRoadSettings
        {
            Enabled = true,
            UphillLengthTiles = 24,
            SummitHeight = 100f,
            MaximumSlope = 30f,
            ElevationCurveExponent = 1.35f,
            EmbankmentBlendWidth = 22f,
            GenerateGuardRails = true,
            GuardRailHeight = 1.1f,
            GuardRailPostIntervalTiles = 2,
            TransitionTriggerDepth = 6f,
            NextRegionId = "countryside"
        };

        public void Validate()
        {
            UphillLengthTiles = Mathf.Max(6, UphillLengthTiles);
            SummitHeight = Mathf.Max(5f, SummitHeight);
            MaximumSlope = Mathf.Clamp(MaximumSlope, 1f, 30f);
            ElevationCurveExponent = Mathf.Clamp(ElevationCurveExponent, 0.5f, 3f);
            EmbankmentBlendWidth = Mathf.Max(1f, EmbankmentBlendWidth);
            GuardRailHeight = Mathf.Max(0.2f, GuardRailHeight);
            GuardRailPostIntervalTiles = Mathf.Max(1, GuardRailPostIntervalTiles);
            TransitionTriggerDepth = Mathf.Max(2f, TransitionTriggerDepth);
            NextRegionId = string.IsNullOrWhiteSpace(NextRegionId)
                ? "next-region"
                : NextRegionId.Trim().ToLowerInvariant();
        }
    }

    [Serializable]
    public struct GeneratedTerrainSettings
    {
        [Min(0.5f)] public float VertexSpacing;
        [Min(0f)] public float MapBorder;
        [Min(1f)] public float RoadHalfWidth;
        [Min(0f)] public float RoadShoulderWidth;
        [Min(0f)] public float DepotBlendWidth;
        [Tooltip("Vertical gap between the depot floor and terrain beneath it.")]
        [Min(0f)] public float DepotSurfaceClearance;
        [Min(0f)] public float HousePadBlendWidth;
        [Min(0f)] public float RoadSurfaceOffset;
        [Min(0.1f)] public float TextureScale;
        public Material SurfaceMaterial;

        public static GeneratedTerrainSettings SuburbsDefault => new GeneratedTerrainSettings
        {
            VertexSpacing = 2f,
            MapBorder = 8f,
            RoadHalfWidth = 4f,
            RoadShoulderWidth = 4f,
            DepotBlendWidth = 4f,
            DepotSurfaceClearance = 0.2f,
            HousePadBlendWidth = 3f,
            RoadSurfaceOffset = 0.035f,
            TextureScale = 16f,
            SurfaceMaterial = null
        };

        public void Validate()
        {
            VertexSpacing = Mathf.Max(0.5f, VertexSpacing);
            MapBorder = Mathf.Max(0f, MapBorder);
            RoadHalfWidth = Mathf.Max(1f, RoadHalfWidth);
            RoadShoulderWidth = Mathf.Max(0f, RoadShoulderWidth);
            DepotBlendWidth = Mathf.Max(0f, DepotBlendWidth);
            DepotSurfaceClearance = Mathf.Max(0f, DepotSurfaceClearance);
            HousePadBlendWidth = Mathf.Max(0f, HousePadBlendWidth);
            RoadSurfaceOffset = Mathf.Max(0f, RoadSurfaceOffset);
            TextureScale = Mathf.Max(0.1f, TextureScale);
        }
    }
}
