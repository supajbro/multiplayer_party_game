using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Fits a saved, floorless tunnel to the region. Never constructs geometry.</summary>
    [DisallowMultipleComponent]
    public sealed class TunnelGeometry : MonoBehaviour
    {
        [SerializeField] private Vector3 m_authoredDimensions = new Vector3(12f, 8f, 12f);
        [SerializeField] private RegionTransitionTrigger m_transition;
        [SerializeField] private BoxCollider m_transitionVolume;

        public void Configure(NeighbourhoodBorderSettings settings, bool summit,
            float transitionDepth, string destination)
        {
            transform.localScale = Vector3.Scale(transform.localScale, new Vector3(
                settings.TunnelWidth / m_authoredDimensions.x,
                settings.TunnelHeight / m_authoredDimensions.y,
                settings.TunnelDepth / m_authoredDimensions.z));
            if (m_transition == null || m_transitionVolume == null) return;
            m_transition.gameObject.SetActive(summit);
            if (!summit) return;
            m_transition.transform.localPosition = new Vector3(0f, 0f, m_authoredDimensions.z * .35f);
            // Preserve the former gateway trigger's world dimensions, including its base-centred height.
            m_transitionVolume.size = new Vector3(m_authoredDimensions.x * .8f,
                m_authoredDimensions.y * .8f, transitionDepth * m_authoredDimensions.z / settings.TunnelDepth);
            m_transition.Initialise(destination);
        }
    }
}
