using System;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
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
