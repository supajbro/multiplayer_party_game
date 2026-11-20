using System.Collections.Generic;
using CouchGuys.Player;
using Unity.AI.Navigation;
using UnityEngine;

namespace CouchGuys.Gameplay.Depot
{
    /// <summary>Prefab-authored boundary shared by server combat and navigation, and local presentation.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshModifierVolume))]
    public sealed class DepotSafeZone : MonoBehaviour
    {
        public const int NavigationArea = 3;
        public const int ThiefAreaMask = ~(1 << NavigationArea);
        private static readonly List<DepotSafeZone> Zones = new();
        [Tooltip("Edit the centre and size on this volume to adjust both protection and navigation together.")]
        [SerializeField] private NavMeshModifierVolume m_boundary;
        [SerializeField] private GameObject m_localIndicator;
        private Bounds LocalBounds
        {
            get
            {
                if (m_boundary == null) m_boundary = GetComponent<NavMeshModifierVolume>();
                return new Bounds(m_boundary.center, m_boundary.size);
            }
        }

        private void OnEnable() { if (!Zones.Contains(this)) Zones.Add(this); }
        private void OnDisable()
        {
            Zones.Remove(this);
            if (m_localIndicator != null) m_localIndicator.SetActive(false);
        }

        public static bool Contains(Vector3 position)
        {
            foreach (var zone in Zones)
                if (zone != null && zone.LocalBounds.Contains(zone.transform.InverseTransformPoint(position))) return true;
            return false;
        }

        // Segment tests also prevent shooting across the depot at a target outside its far side.
        public static bool Intersects(Vector3 from, Vector3 to)
        {
            foreach (var zone in Zones)
            {
                if (zone == null) continue;
                Vector3 start = zone.transform.InverseTransformPoint(from);
                Vector3 delta = zone.transform.InverseTransformPoint(to) - start;
                if (zone.LocalBounds.Contains(start)) return true;
                if (delta.sqrMagnitude > .000001f && zone.LocalBounds.IntersectRay(
                        new Ray(start, delta.normalized), out float distance) && distance <= delta.magnitude) return true;
            }
            return false;
        }

        public static bool TryGetExterior(Vector3 position, float clearance, out Vector3 exterior)
        {
            exterior = position;
            foreach (var zone in Zones)
            {
                if (zone == null) continue;
                Vector3 local = zone.transform.InverseTransformPoint(exterior);
                if (!zone.LocalBounds.Contains(local)) continue;
                var bounds = zone.LocalBounds;
                float left = local.x - bounds.min.x, right = bounds.max.x - local.x;
                float back = local.z - bounds.min.z, front = bounds.max.z - local.z;
                float nearest = Mathf.Min(left, right, back, front);
                if (nearest == left) local.x = bounds.min.x - clearance;
                else if (nearest == right) local.x = bounds.max.x + clearance;
                else if (nearest == back) local.z = bounds.min.z - clearance;
                else local.z = bounds.max.z + clearance;
                exterior = zone.transform.TransformPoint(local);
            }
            return exterior != position;
        }

        private void Update()
        {
            if (m_localIndicator == null) return;
            bool inside = false;
            foreach (var player in PlayerHealth.Instances)
                if (player != null && player.IsOwner && player.IsSpawned &&
                    LocalBounds.Contains(transform.InverseTransformPoint(player.transform.position))) { inside = true; break; }
            if (m_localIndicator.activeSelf != inside) m_localIndicator.SetActive(inside);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.15f, 1, .45f, .5f);
            Gizmos.DrawWireCube(LocalBounds.center, LocalBounds.size);
        }
    }
}
