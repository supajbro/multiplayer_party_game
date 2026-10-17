using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        private const int RoadRenderChunkSpan = 6;

        private readonly struct RoadBatchKey : IEquatable<RoadBatchKey>
        {
            public readonly int ChunkX;
            public readonly int ChunkY;
            public readonly Material Material;
            public readonly bool Markings;

            public RoadBatchKey(int chunkX, int chunkY, Material material, bool markings)
            {
                ChunkX = chunkX;
                ChunkY = chunkY;
                Material = material;
                Markings = markings;
            }

            public bool Equals(RoadBatchKey other) =>
                ChunkX == other.ChunkX && ChunkY == other.ChunkY &&
                Material == other.Material && Markings == other.Markings;

            public override bool Equals(object obj) => obj is RoadBatchKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                ChunkX,
                ChunkY,
                Material != null ? Material.GetInstanceID() : 0,
                Markings);
        }

        private sealed class RoadBatchSources
        {
            public readonly List<CombineInstance> Instances = new List<CombineInstance>(48);
            public ShadowCastingMode ShadowCastingMode = ShadowCastingMode.On;
            public bool ReceiveShadows = true;
        }

        private void CreateRoadRenderBatches()
        {
            if (m_roadsRoot == null || m_generatedRoads.Count == 0) return;

            GameObject batchRootObject = new GameObject("RoadRenderBatches");
            batchRootObject.transform.SetParent(m_roadsRoot, false);
            Transform batchRoot = batchRootObject.transform;
            Matrix4x4 worldToBatch = batchRoot.worldToLocalMatrix;
            Dictionary<RoadBatchKey, RoadBatchSources> batches =
                new Dictionary<RoadBatchKey, RoadBatchSources>();

            for (int roadIndex = 0; roadIndex < m_generatedRoads.Count; roadIndex++)
            {
                GeneratedRoad road = m_generatedRoads[roadIndex];
                if (road == null) continue;
                int chunkX = FloorDivide(road.Coordinate.X, RoadRenderChunkSpan);
                int chunkY = FloorDivide(road.Coordinate.Y, RoadRenderChunkSpan);

                AddRoadBatchSource(
                    road.GetComponent<MeshFilter>(),
                    road.GetComponent<MeshRenderer>(),
                    chunkX,
                    chunkY,
                    false,
                    worldToBatch,
                    batches);

                Transform markings = road.transform.Find("WhiteMarkings");
                if (markings != null)
                {
                    AddRoadBatchSource(
                        markings.GetComponent<MeshFilter>(),
                        markings.GetComponent<MeshRenderer>(),
                        chunkX,
                        chunkY,
                        true,
                        worldToBatch,
                        batches);
                }
            }

            List<RoadBatchKey> keys = new List<RoadBatchKey>(batches.Keys);
            keys.Sort((first, second) =>
            {
                int yComparison = first.ChunkY.CompareTo(second.ChunkY);
                if (yComparison != 0) return yComparison;
                int xComparison = first.ChunkX.CompareTo(second.ChunkX);
                if (xComparison != 0) return xComparison;
                int markingComparison = first.Markings.CompareTo(second.Markings);
                if (markingComparison != 0) return markingComparison;
                int firstMaterial = first.Material != null ? first.Material.GetInstanceID() : 0;
                int secondMaterial = second.Material != null ? second.Material.GetInstanceID() : 0;
                return firstMaterial.CompareTo(secondMaterial);
            });

            for (int index = 0; index < keys.Count; index++)
            {
                RoadBatchKey key = keys[index];
                RoadBatchSources sources = batches[key];
                if (key.Material == null || sources.Instances.Count == 0) continue;

                Mesh mesh = new Mesh
                {
                    name = $"Road{(key.Markings ? "Markings" : "Surface")}_" +
                           $"{key.ChunkX}_{key.ChunkY}",
                    indexFormat = IndexFormat.UInt32
                };
                mesh.CombineMeshes(sources.Instances.ToArray(), true, true, false);
                mesh.RecalculateBounds();

                GameObject batchObject = new GameObject(
                    mesh.name,
                    typeof(MeshFilter),
                    typeof(MeshRenderer),
                    typeof(GeneratedTerrainMesh));
                batchObject.transform.SetParent(batchRoot, false);
                batchObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = batchObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = key.Material;
                renderer.shadowCastingMode = sources.ShadowCastingMode;
                renderer.receiveShadows = sources.ReceiveShadows;
                GeneratedTerrainMesh ownedMesh = batchObject.GetComponent<GeneratedTerrainMesh>();
                ownedMesh.Initialise(mesh, null);
                m_generatedRoadRenderBatches.Add(ownedMesh);
            }

            // The combined collider and render chunks now own all geometry needed at runtime.
            // Retain lightweight road objects for routing and gameplay metadata, but discard
            // their hundreds of individual render components, marking children, and meshes.
            for (int roadIndex = 0; roadIndex < m_generatedRoads.Count; roadIndex++)
            {
                GeneratedRoad road = m_generatedRoads[roadIndex];
                if (road == null) continue;
                MeshFilter filter = road.GetComponent<MeshFilter>();
                MeshRenderer renderer = road.GetComponent<MeshRenderer>();
                if (filter != null) filter.sharedMesh = null;
                if (renderer != null) renderer.enabled = false;
                Transform markings = road.transform.Find("WhiteMarkings");
                if (markings != null)
                {
                    MeshFilter markingFilter = markings.GetComponent<MeshFilter>();
                    MeshRenderer markingRenderer = markings.GetComponent<MeshRenderer>();
                    if (markingFilter != null) markingFilter.sharedMesh = null;
                    if (markingRenderer != null) markingRenderer.enabled = false;
                }
                road.ReleaseOwnedRenderMeshes();
                DestroyGeneratedComponent(renderer);
                DestroyGeneratedComponent(filter);
                if (markings != null) DestroyGeneratedObject(markings.gameObject);
            }
        }

        private static void AddRoadBatchSource(
            MeshFilter filter,
            MeshRenderer renderer,
            int chunkX,
            int chunkY,
            bool markings,
            Matrix4x4 worldToBatch,
            Dictionary<RoadBatchKey, RoadBatchSources> batches)
        {
            if (filter == null || renderer == null || filter.sharedMesh == null ||
                renderer.sharedMaterial == null)
                return;

            RoadBatchKey key = new RoadBatchKey(
                chunkX,
                chunkY,
                renderer.sharedMaterial,
                markings);
            if (!batches.TryGetValue(key, out RoadBatchSources sources))
            {
                sources = new RoadBatchSources
                {
                    ShadowCastingMode = renderer.shadowCastingMode,
                    ReceiveShadows = renderer.receiveShadows
                };
                batches.Add(key, sources);
            }
            sources.Instances.Add(new CombineInstance
            {
                mesh = filter.sharedMesh,
                transform = worldToBatch * filter.transform.localToWorldMatrix
            });
        }

        private static int FloorDivide(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static void DestroyGeneratedComponent(Component component)
        {
            if (component == null) return;
            if (Application.isPlaying) Destroy(component);
            else DestroyImmediate(component);
        }

        private static void DestroyGeneratedObject(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
