using System;
using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.Gameplay.Pedestrians
{
    /// <summary>Convex sidewalk footprints joined only where their surfaces touch.
    /// Driveway aprons retain the pedestrian corridor across the lowered curb.</summary>
    public sealed class SidewalkGraph
    {
        public sealed class Node
        {
            public Vector3 Position;
            public readonly List<int> Neighbours = new List<int>(3);
        }

        public readonly List<Node> Nodes = new List<Node>();
        public readonly List<int> SpawnNodes = new List<int>();
        private readonly Dictionary<Vector2Int, int> m_lookup = new Dictionary<Vector2Int, int>();
        private readonly HashSet<ulong> m_walkable = new HashSet<ulong>();

        public void Build(IReadOnlyList<Rect> patches, Func<Vector3, Vector3> sampleSurface)
        {
            Nodes.Clear();
            SpawnNodes.Clear();
            m_lookup.Clear();
            m_walkable.Clear();
            for (int i = 0; i < patches.Count; i++) AddNode(patches[i].center);
            for (int i = 0; i < patches.Count; i++)
            for (int j = i + 1; j < patches.Count; j++)
            {
                Rect a = patches[i], b = patches[j];
                float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
                float z0 = Mathf.Max(a.yMin, b.yMin), z1 = Mathf.Min(a.yMax, b.yMax);
                // Reject gaps and point-only contacts. Never connect across a road.
                if (x1 < x0 - 0.001f || z1 < z0 - 0.001f ||
                    (x1 - x0 < 0.05f && z1 - z0 < 0.05f)) continue;
                int portal = AddNode(new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f));
                Connect(AddNode(a.center), portal);
                Connect(portal, AddNode(b.center));
            }

            // Removing bridges (including cul-de-sacs and links between loops) ensures
            // every surviving directed arrival has an exit other than its incoming edge.
            int[] visited = new int[Nodes.Count], low = new int[Nodes.Count];
            int clock = 0;
            var bridges = new List<Vector2Int>();
            for (int i = 0; i < Nodes.Count; i++)
                if (visited[i] == 0) FindBridges(i, -1, visited, low, ref clock, bridges);
            foreach (Vector2Int bridge in bridges)
            {
                Nodes[bridge.x].Neighbours.Remove(bridge.y);
                Nodes[bridge.y].Neighbours.Remove(bridge.x);
            }

            // Sample the same terrain/driveway surface as the sidewalk mesh once,
            // rather than raycasting the ground underneath every walker every frame.
            int originalCount = Nodes.Count;
            for (int i = 0; i < originalCount; i++)
                Nodes[i].Position = sampleSurface(Nodes[i].Position);
            var edges = new List<Vector2Int>();
            for (int i = 0; i < originalCount; i++)
                foreach (int j in Nodes[i].Neighbours)
                    if (j > i) edges.Add(new Vector2Int(i, j));
            foreach (Vector2Int edge in edges)
            {
                Vector3 a = Nodes[edge.x].Position, b = Nodes[edge.y].Position;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.75f));
                if (steps == 1) continue;
                Nodes[edge.x].Neighbours.Remove(edge.y);
                Nodes[edge.y].Neighbours.Remove(edge.x);
                int previous = edge.x;
                for (int step = 1; step < steps; step++)
                {
                    int next = Nodes.Count;
                    Nodes.Add(new Node { Position = sampleSurface(Vector3.Lerp(a, b, step / (float)steps)) });
                    Connect(previous, next);
                    previous = next;
                }
                Connect(previous, edge.y);
            }
            for (int i = 0; i < Nodes.Count; i++)
                foreach (int next in Nodes[i].Neighbours) m_walkable.Add(DirectedKey(i, next));
            // An overlapping intersection can produce multiple edges on the same side
            // of a node. Keep only directed arrivals with a continuous forward exit.
            var invalid = new List<ulong>();
            do
            {
                invalid.Clear();
                foreach (ulong edge in m_walkable)
                {
                    int previous = (int)(edge >> 32), current = (int)(uint)edge;
                    bool exit = false;
                    foreach (int next in Nodes[current].Neighbours)
                        if (next != previous && m_walkable.Contains(DirectedKey(current, next)) &&
                            IsForward(previous, current, next)) { exit = true; break; }
                    if (!exit) invalid.Add(edge);
                }
                foreach (ulong edge in invalid) m_walkable.Remove(edge);
            } while (invalid.Count > 0);
            for (int i = 0; i < Nodes.Count; i++)
                foreach (int next in Nodes[i].Neighbours)
                    if (m_walkable.Contains(DirectedKey(i, next))) { SpawnNodes.Add(i); break; }
            m_lookup.Clear();
        }

        public int ChooseNext(int current, int previous)
        {
            List<int> neighbours = Nodes[current].Neighbours;
            int count = 0, selected = -1;
            foreach (int candidate in neighbours)
                if (candidate != previous && m_walkable.Contains(DirectedKey(current, candidate)) &&
                    (previous < 0 || IsForward(previous, current, candidate)) &&
                    UnityEngine.Random.Range(0, ++count) == 0)
                    selected = candidate;
            return selected;
        }

        private static ulong DirectedKey(int from, int to) => ((ulong)(uint)from << 32) | (uint)to;

        private bool IsForward(int previous, int current, int next)
        {
            Vector3 incoming = Vector3.ProjectOnPlane(Nodes[current].Position - Nodes[previous].Position, Vector3.up);
            Vector3 outgoing = Vector3.ProjectOnPlane(Nodes[next].Position - Nodes[current].Position, Vector3.up);
            return Vector3.Dot(incoming.normalized, outgoing.normalized) >= -0.01f;
        }

        private int AddNode(Vector2 point)
        {
            var key = new Vector2Int(Mathf.RoundToInt(point.x * 1000f), Mathf.RoundToInt(point.y * 1000f));
            if (m_lookup.TryGetValue(key, out int index)) return index;
            index = Nodes.Count;
            Nodes.Add(new Node { Position = new Vector3(point.x, 0f, point.y) });
            m_lookup.Add(key, index);
            return index;
        }

        private void Connect(int a, int b)
        {
            if (a == b || Nodes[a].Neighbours.Contains(b)) return;
            Nodes[a].Neighbours.Add(b);
            Nodes[b].Neighbours.Add(a);
        }

        private void FindBridges(int node, int parent, int[] visited, int[] low,
            ref int clock, List<Vector2Int> bridges)
        {
            visited[node] = low[node] = ++clock;
            foreach (int next in Nodes[node].Neighbours)
            {
                if (next == parent) continue;
                if (visited[next] == 0)
                {
                    FindBridges(next, node, visited, low, ref clock, bridges);
                    low[node] = Mathf.Min(low[node], low[next]);
                    if (low[next] > visited[node]) bridges.Add(new Vector2Int(node, next));
                }
                else low[node] = Mathf.Min(low[node], visited[next]);
            }
        }
    }
}
