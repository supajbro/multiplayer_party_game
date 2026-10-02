using UnityEngine;

namespace CouchGuys.Gameplay.Couch
{
    /// <summary>
    /// Identifies one physical attachment position on a couch.
    /// Occupancy is owned and synchronised by the parent CouchCarryController.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CouchCarryPoint : MonoBehaviour
    {
        [SerializeField, Range(0, CouchCarryController.MaximumCarryPoints - 1)]
        private int m_pointIndex;

        private CouchCarryController m_couch;

        public int PointIndex => m_pointIndex;
        public CouchCarryController Couch => m_couch != null ? m_couch : m_couch = GetComponentInParent<CouchCarryController>();
        public bool IsAvailable => Couch != null && !Couch.IsPointOccupied(m_pointIndex);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = IsAvailable ? new Color(0.2f, 1f, 0.35f, 0.9f) : new Color(1f, 0.2f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);
            Gizmos.DrawLine(transform.position, transform.position + transform.up * 0.25f);
        }
#endif
    }
}
