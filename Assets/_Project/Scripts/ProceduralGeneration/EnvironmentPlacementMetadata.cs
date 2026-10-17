using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Art-independent placement contract for replaceable environment prefabs.</summary>
    [DisallowMultipleComponent]
    public sealed class EnvironmentPlacementMetadata : MonoBehaviour
    {
        [SerializeField] private Vector2 m_footprint = Vector2.one;
        [SerializeField, Min(0f)] private float m_clearance = 0.25f;
        [SerializeField] private bool m_keepUpright = true;
        [SerializeField] private bool m_blocksPlacement = true;

        public Vector2 Footprint => m_footprint;
        public float Clearance => m_clearance;
        public bool KeepUpright => m_keepUpright;
        public bool BlocksPlacement => m_blocksPlacement;

        public void Configure(Vector2 footprint, float clearance, bool keepUpright = true, bool blocksPlacement = true)
        {
            m_footprint = new Vector2(Mathf.Max(0.05f, footprint.x), Mathf.Max(0.05f, footprint.y));
            m_clearance = Mathf.Max(0f, clearance);
            m_keepUpright = keepUpright;
            m_blocksPlacement = blocksPlacement;
        }

#if UNITY_EDITOR
        private void OnValidate() => Configure(m_footprint, m_clearance, m_keepUpright, m_blocksPlacement);
#endif
    }

}
