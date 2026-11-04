using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Vehicles
{
    // A bounded visibility graph over the existing NavMesh. Physical hull sweeps
    // supply clearance the pedestrian bake cannot provide for a couch and grips.
    internal sealed class CouchClearancePath
    {
        private readonly NavMeshPath m_path = new();
        private readonly List<Vector3> m_nodes = new();
        private readonly HashSet<Collider> m_obstacles = new();
        private readonly Dictionary<(int, int), bool> m_visibility = new();
        private readonly Dictionary<Collider, bool> m_ignore = new();
        private CouchCarryController m_couch;
        private Vector3 m_extents;
        private Vector3 m_offset;
        private Quaternion m_rotation;
        private float m_height;
        private GetawayTruckSettings m_settings;
        private Vector3 m_start;
        internal Transform LoadingTruck { get; set; }

        internal bool TryBuild(CouchCarryController couch, Vector3 target, GetawayTruckSettings settings,
            float targetRadius, bool allowPartial, out Vector3[] corners, out bool complete)
        {
            corners = null;
            complete = false;
            if (!NavMesh.SamplePosition(couch.transform.position, out var start, 2f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(target, out var end, targetRadius, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, m_path) ||
                m_path.status == NavMeshPathStatus.PathInvalid) return false;
            bool navComplete = m_path.status == NavMeshPathStatus.PathComplete;
            if (!allowPartial && !navComplete) return false;
            Vector3[] basePath = m_path.corners;
            if (basePath.Length == 0) return false;
            m_couch = couch;
            m_settings = settings;
            m_rotation = couch.transform.rotation;
            Bounds bounds = couch.CargoBounds;
            if (bounds.size.sqrMagnitude < .01f) bounds = new Bounds(Vector3.up * .45f, new Vector3(2.1f, .9f, .9f));
            Vector3 scale = couch.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            m_extents = Vector3.Scale(bounds.extents, scale) + new Vector3(settings.CarryClearancePadding, 0f, settings.CarryClearancePadding);
            m_offset = m_rotation * Vector3.Scale(bounds.center, scale);
            m_height = couch.transform.position.y - start.position.y;
            m_start = start.position;
            m_nodes.Clear();
            m_obstacles.Clear();
            m_visibility.Clear();
            m_ignore.Clear();
            m_nodes.Add(start.position);
            m_nodes.Add(basePath[basePath.Length - 1]);
            for (int i = 1; i < basePath.Length - 1; i++) AddNode(basePath[i]);
            // Seed detours from obstacles at the pedestrian corners and along its path.
            for (int i = 0; i < basePath.Length; i++)
            {
                foreach (Collider hit in Physics.OverlapBox(Centre(basePath[i]), m_extents, m_rotation, ~0, QueryTriggerInteraction.Ignore))
                    AddObstacle(hit);
                if (i > 0) SegmentClear(basePath[i - 1], basePath[i], true);
            }
            for (int pass = 0; pass <= settings.MaximumCarryDetourObstacles; pass++)
            {
                int count = m_nodes.Count;
                var costs = new float[count];
                var previous = new int[count];
                var closed = new bool[count];
                for (int i = 0; i < count; i++) { costs[i] = float.PositiveInfinity; previous[i] = -1; }
                costs[0] = 0f;
                for (int step = 0; step < count; step++)
                {
                    int current = -1;
                    for (int i = 0; i < count; i++)
                        if (!closed[i] && !float.IsPositiveInfinity(costs[i]) && (current < 0 || costs[i] < costs[current])) current = i;
                    if (current < 0) break;
                    closed[current] = true;
                    if (current == 1) break;
                    for (int next = 1; next < count; next++)
                    {
                        if (closed[next]) continue;
                        float cost = costs[current] + Vector3.Distance(m_nodes[current], m_nodes[next]);
                        if (cost >= costs[next]) continue;
                        if (!Visible(current, next)) continue;
                        costs[next] = cost;
                        previous[next] = current;
                    }
                }
                if (!float.IsPositiveInfinity(costs[1]))
                {
                    var result = new List<Vector3>();
                    for (int i = 1; i >= 0; i = previous[i]) result.Add(m_nodes[i]);
                    result.Reverse();
                    corners = result.ToArray();
                    complete = navComplete;
                    return true;
                }
                if (m_nodes.Count == count)
                {
                    if (!allowPartial) return false;
                    int best = 0;
                    for (int i = 1; i < count; i++)
                        if (!float.IsPositiveInfinity(costs[i]) &&
                            (m_nodes[i] - target).sqrMagnitude < (m_nodes[best] - target).sqrMagnitude) best = i;
                    if (best == 0) return false;
                    var result = new List<Vector3>();
                    for (int i = best; i >= 0; i = previous[i]) result.Add(m_nodes[i]);
                    result.Reverse();
                    corners = result.ToArray();
                    return true;
                }
            }
            return false;
        }

        internal bool LoadingEntranceClear(CouchCarryController couch, Vector3 root, Vector3 entrance)
        {
            m_couch = couch;
            m_ignore.Clear();
            Bounds bounds = couch.CargoBounds;
            if (bounds.size.sqrMagnitude < .01f) bounds = new Bounds(Vector3.up * .45f, new Vector3(2.1f, .9f, .9f));
            Vector3 scale = couch.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 offset = entrance - root;
            float length = offset.magnitude;
            if (length < .01f) return true;
            // Only the couch enters the cargo bay; the carrier padding applies to
            // walking paths, not the existing hands-off loading transition.
            foreach (var hit in Physics.BoxCastAll(root + couch.transform.rotation * Vector3.Scale(bounds.center, scale),
                Vector3.Scale(bounds.extents, scale), offset / length, couch.transform.rotation,
                length, ~0, QueryTriggerInteraction.Ignore))
                // Cargo loading already disables couch/truck collisions while lifting
                // into the bay. The destination chassis is not an external obstacle.
                if (!Ignore(hit.collider) &&
                    (LoadingTruck == null || !hit.collider.transform.IsChildOf(LoadingTruck))) return false;
            return true;
        }

        private Vector3 Centre(Vector3 position) => position + Vector3.up * m_height + m_offset;

        private bool Ignore(Collider hit)
        {
            if (hit == null) return true;
            if (m_ignore.TryGetValue(hit, out bool ignored)) return ignored;
            ignored = hit.transform.IsChildOf(m_couch.transform) || TruckController.IsGroundSurface(hit) ||
                hit.GetComponentInParent<ThiefHealth>() != null || hit.GetComponentInParent<PlayerHealth>() != null;
            m_ignore[hit] = ignored;
            return ignored;
        }

        private bool Visible(int from, int to)
        {
            var key = (Mathf.Min(from, to), Mathf.Max(from, to));
            if (m_visibility.TryGetValue(key, out bool visible)) return visible;
            visible = SegmentClear(m_nodes[from], m_nodes[to], true) &&
                !NavMesh.Raycast(m_nodes[from], m_nodes[to], out _, NavMesh.AllAreas);
            m_visibility[key] = visible;
            return visible;
        }

        private bool Fits(Vector3 position)
        {
            foreach (Collider hit in Physics.OverlapBox(Centre(position), m_extents, m_rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignore(hit)) return false;
            return true;
        }

        private bool SegmentClear(Vector3 from, Vector3 to, bool expand)
        {
            Vector3 offset = to - from;
            float length = offset.magnitude;
            if (length < .01f) return Fits(to);
            bool clear = true;
            foreach (var hit in Physics.BoxCastAll(Centre(from), m_extents, offset / length, m_rotation,
                length, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Ignore(hit.collider)) continue;
                // Allow the starting grip to move out of its extra safety margin,
                // but never allow a segment to enter or cross an obstruction.
                if (hit.distance < .01f && (from - m_start).sqrMagnitude < .01f)
                {
                    bool supportsClosestPoint = hit.collider is BoxCollider || hit.collider is SphereCollider ||
                        hit.collider is CapsuleCollider || (hit.collider is MeshCollider mesh && mesh.convex);
                    Vector3 closest = supportsClosestPoint ? hit.collider.ClosestPoint(Centre(from)) :
                        hit.collider.bounds.ClosestPoint(Centre(from));
                    Vector3 outward = Centre(from) - closest;
                    float advance = Mathf.Min(length, m_settings.CarryClearancePadding * 2f + .1f);
                    if (Vector3.Dot(outward.normalized, offset.normalized) > .1f &&
                        Fits(from + offset.normalized * advance) &&
                        SegmentClear(from + offset.normalized * advance, to, false)) continue;
                }
                clear = false;
                if (expand)
                {
                    AddObstacle(hit.collider);
                    if (hit.distance > .2f) AddNode(from + offset.normalized * (hit.distance - .15f));
                }
            }
            return clear;
        }

        private void AddNode(Vector3 candidate)
        {
            if (m_nodes.Count >= 2 + m_settings.MaximumCarryDetourObstacles * 4 + 16 ||
                !NavMesh.SamplePosition(candidate, out var hit, 1f, NavMesh.AllAreas) || !Fits(hit.position)) return;
            foreach (Vector3 node in m_nodes) if ((node - hit.position).sqrMagnitude < .04f) return;
            m_nodes.Add(hit.position);
        }

        private void AddObstacle(Collider collider)
        {
            if (Ignore(collider) || m_obstacles.Count >= m_settings.MaximumCarryDetourObstacles || !m_obstacles.Add(collider)) return;
            Bounds bounds = collider.bounds;
            Vector3 right = m_rotation * Vector3.right;
            Vector3 forward = m_rotation * Vector3.forward;
            float x = Mathf.Abs(right.x) * m_extents.x + Mathf.Abs(forward.x) * m_extents.z + .15f;
            float z = Mathf.Abs(right.z) * m_extents.x + Mathf.Abs(forward.z) * m_extents.z + .15f;
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                    AddNode(new Vector3(ix < 0 ? bounds.min.x - x : bounds.max.x + x,
                        m_start.y, iz < 0 ? bounds.min.z - z : bounds.max.z + z));
        }
    }
}
