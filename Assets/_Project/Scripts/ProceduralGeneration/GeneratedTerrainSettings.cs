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
            ColliderHeightAboveTerrain = Mathf.Max(2f, ColliderHeightAboveTerrain);
            ColliderDepthBelowTerrain = Mathf.Max(1f, ColliderDepthBelowTerrain);
            ColliderThickness = Mathf.Max(0.2f, ColliderThickness);
            FallRecoveryDepth = Mathf.Max(1f, FallRecoveryDepth);
            ForestPrefabs ??= Array.Empty<GameObject>();
            MountainPrefabs ??= Array.Empty<GameObject>();
            ConstructionPrefabs ??= Array.Empty<GameObject>();
            RoadClosurePrefabs ??= Array.Empty<GameObject>();
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
            HousePadBlendWidth = Mathf.Max(0f, HousePadBlendWidth);
            RoadSurfaceOffset = Mathf.Max(0f, RoadSurfaceOffset);
            TextureScale = Mathf.Max(0.1f, TextureScale);
        }
    }
}
