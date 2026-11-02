using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Saved depot-local safe points and hard boundaries. No runtime navigation build.</summary>
    [DisallowMultipleComponent]
    public sealed class DispatcherRoamingArea : MonoBehaviour
    {
        [SerializeField] private Transform[] m_waypoints = System.Array.Empty<Transform>();
        [SerializeField] private Bounds m_interior;
        [SerializeField, Tooltip("Truck bay, door lanes and other forbidden depot-local volumes.")]
        private Bounds[] m_exclusions = System.Array.Empty<Bounds>();

        public int WaypointCount => m_waypoints.Length;
        public Transform GetWaypoint(int index) => m_waypoints[index];

        public bool AllowsSegment(Vector3 from, Vector3 to, float clearance)
        {
            from = transform.InverseTransformPoint(from);
            to = transform.InverseTransformPoint(to);
            float padding = clearance / Mathf.Max(.001f, Mathf.Min(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)));
            var interior = m_interior;
            interior.Expand(new Vector3(-2f * padding, 0f, -2f * padding));
            if (interior.size.x <= 0f || interior.size.z <= 0f || !interior.Contains(from) || !interior.Contains(to)) return false;
            Vector3 delta = to - from;
            foreach (var exclusion in m_exclusions)
            {
                var expanded = exclusion;
                expanded.Expand(new Vector3(2f * padding, 0f, 2f * padding));
                if (expanded.Contains(from) || expanded.Contains(to)) return false;
                if (delta.sqrMagnitude > .000001f && expanded.IntersectRay(new Ray(from, delta.normalized), out float distance) &&
                    distance <= delta.magnitude) return false;
            }
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.cyan; Gizmos.DrawWireCube(m_interior.center, m_interior.size);
            Gizmos.color = Color.red;
            foreach (var b in m_exclusions) Gizmos.DrawWireCube(b.center, b.size);
            Gizmos.matrix = Matrix4x4.identity; Gizmos.color = Color.green;
            foreach (var point in m_waypoints) if (point != null) Gizmos.DrawWireSphere(point.position, .2f);
        }
    }
}
