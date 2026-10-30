using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [CreateAssetMenu(menuName = "Couch Guys/Decorative Surroundings")]
    public sealed class DistantSuburbSettings : ScriptableObject
    {
        [Tooltip("Render-only chunk prefab. Must contain no colliders or gameplay components.")]
        public GeneratedTerrainMesh ChunkPrefab;
        [Min(0f)] public float ExtensionDistance = 600f;
        [Range(0f, 1f)] public float BuildingDensity = 0.6f;
        [Range(0f, 1f)] public float BuildingVariety = 1f;
        [Min(0f)] public float HillHeight = 35f;
        public Color[] BuildingColours = {
            new Color(0.86f, 0.65f, 0.43f), new Color(0.67f, 0.79f, 0.83f),
            new Color(0.87f, 0.78f, 0.61f), new Color(0.77f, 0.53f, 0.48f),
            new Color(0.64f, 0.73f, 0.58f) };
    }
}
