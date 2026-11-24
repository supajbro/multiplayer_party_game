using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Builds seeded border scenery and a collision perimeter with intentional traffic gateways.
    /// Optional region prefabs replace the deliberately cheap fallback art.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodBorderGenerator : MonoBehaviour
    {
        private readonly List<Material> m_ownedMaterials = new List<Material>();
        private Material m_trunkMaterial;
        private Material m_leafMaterial;
        private Material m_rockMaterial;

        public int PerimeterColliderCount { get; private set; }
        public int ProtectedPerimeterEdgeCount { get; private set; }
        public int SceneryItemCount { get; private set; }
        public int RoadClosureCount { get; private set; }
        public int TunnelCount { get; private set; }

        private struct Edge
        {
            public Vector3 Start;
            public Vector3 Tangent;
            public Vector3 Outward;
            public float Length;
        }

        public void Generate(
            NeighbourhoodGenerator generator,
            Rect playableBounds,
            float clearWidth,
            NeighbourhoodBorderSettings settings,
            System.Random random)
        {
            settings.Validate();
            // Keep containment and hill scenery beyond the playable terrain. HillDepth is
            // the outward transition distance, never an inward playable-area influence.
            float outerOffset = Mathf.Max(
                clearWidth + settings.BorderWidth,
                settings.HillDepth);
            Edge[] edges = CreateEdges(playableBounds, outerOffset);
            CreateCollisionPerimeter(generator, edges, settings);
            CreateUphillCorridorContainment(generator, playableBounds, settings);
            CreateTunnels(generator, settings);
            CreateScenery(generator, edges, settings, random);
            CreateRearHillForest(generator, playableBounds, outerOffset, settings, random);

            float recoveryMaximumZ = playableBounds.yMax + outerOffset - settings.ColliderThickness;
            if (generator.HasSummitExit)
            {
                Vector3 summitLocal = transform.InverseTransformPoint(
                    generator.SummitExitPortal.WorldPosition);
                recoveryMaximumZ = Mathf.Max(recoveryMaximumZ,
                    summitLocal.z + generator.TileSize);
            }
            Rect recoveryBounds = Rect.MinMaxRect(
                playableBounds.xMin - outerOffset + settings.ColliderThickness,
                playableBounds.yMin - outerOffset + settings.ColliderThickness,
                playableBounds.xMax + outerOffset - settings.ColliderThickness,
                recoveryMaximumZ);
            NeighbourhoodOutOfBoundsRecovery recovery =
                gameObject.AddComponent<NeighbourhoodOutOfBoundsRecovery>();
            recovery.Initialise(generator, recoveryBounds, settings.FallRecoveryDepth);
        }

        private static Edge[] CreateEdges(Rect bounds, float offset)
        {
            float minimumX = bounds.xMin - offset;
            float maximumX = bounds.xMax + offset;
            float minimumZ = bounds.yMin - offset;
            float maximumZ = bounds.yMax + offset;
            float width = maximumX - minimumX;
            float depth = maximumZ - minimumZ;
            return new[]
            {
                new Edge { Start = new Vector3(minimumX, 0f, minimumZ), Tangent = Vector3.right, Outward = Vector3.back, Length = width },
                new Edge { Start = new Vector3(maximumX, 0f, minimumZ), Tangent = Vector3.forward, Outward = Vector3.right, Length = depth },
                new Edge { Start = new Vector3(maximumX, 0f, maximumZ), Tangent = Vector3.left, Outward = Vector3.forward, Length = width },
                new Edge { Start = new Vector3(minimumX, 0f, maximumZ), Tangent = Vector3.back, Outward = Vector3.left, Length = depth }
            };
        }

        private void CreateCollisionPerimeter(
            NeighbourhoodGenerator generator,
            Edge[] edges,
            NeighbourhoodBorderSettings settings)
        {
            Renderer terrainRenderer = generator.GeneratedTerrain != null
                ? generator.GeneratedTerrain.GetComponent<Renderer>()
                : null;
            float terrainMinimum = terrainRenderer != null
                ? terrainRenderer.bounds.min.y
                : generator.transform.position.y;
            float terrainMaximum = terrainRenderer != null
                ? terrainRenderer.bounds.max.y
                : generator.transform.position.y;
            float bottom = terrainMinimum - settings.ColliderDepthBelowTerrain;
            float top = terrainMaximum + settings.ColliderHeightAboveTerrain;
            float height = top - bottom;
            Vector3 worldMidpoint = new Vector3(0f, (bottom + top) * 0.5f, 0f);

            Transform colliderRoot = new GameObject("PhysicalPerimeter").transform;
            colliderRoot.SetParent(transform, false);
            for (int index = 0; index < edges.Length; index++)
            {
                Edge edge = edges[index];
                int colliderCountBeforeEdge = PerimeterColliderCount;
                List<float> gaps = GetPortalDistances(generator, edge);
                float cursor = 0f;
                // Keep the invisible containment wall out of the complete tunnel shell,
                // including fallback pillars. A tile-based gap could cut into wider tunnels.
                float pillarWidth = Mathf.Max(1.2f, settings.TunnelWidth * 0.16f);
                float halfGap = Mathf.Max(
                    generator.TileSize * 0.7f,
                    settings.TunnelWidth * 0.5f + pillarWidth +
                    settings.ColliderThickness * 0.5f);
                for (int gapIndex = 0; gapIndex < gaps.Count; gapIndex++)
                {
                    float gapStart = Mathf.Clamp(gaps[gapIndex] - halfGap, 0f, edge.Length);
                    float gapEnd = Mathf.Clamp(gaps[gapIndex] + halfGap, 0f, edge.Length);
                    CreateWallSegment(colliderRoot, edge, index, gapIndex, cursor, gapStart,
                        settings, height, worldMidpoint.y);
                    cursor = Mathf.Max(cursor, gapEnd);
                }

                CreateWallSegment(colliderRoot, edge, index, gaps.Count, cursor, edge.Length,
                    settings, height, worldMidpoint.y);
                if (PerimeterColliderCount > colliderCountBeforeEdge)
                {
                    ProtectedPerimeterEdgeCount++;
                }
            }
        }

        private void CreateWallSegment(
            Transform colliderRoot,
            Edge edge,
            int edgeIndex,
            int segmentIndex,
            float start,
            float end,
            NeighbourhoodBorderSettings settings,
            float height,
            float worldMidpointY)
        {
            float length = end - start;
            if (length <= settings.ColliderThickness)
            {
                return;
            }

            GameObject wall = new GameObject($"BoundaryWall_{edgeIndex + 1:00}_{segmentIndex + 1:00}");
            wall.transform.SetParent(colliderRoot, false);
            wall.transform.localPosition = edge.Start + edge.Tangent * (start + length * 0.5f);
            Vector3 wallWorld = wall.transform.position;
            wallWorld.y = worldMidpointY;
            wall.transform.position = wallWorld;
            wall.transform.localRotation = Quaternion.LookRotation(edge.Outward, Vector3.up);
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(
                length,
                height / Mathf.Max(0.001f, wall.transform.lossyScale.y),
                settings.ColliderThickness);
            PerimeterColliderCount++;
        }

        private void CreateUphillCorridorContainment(
            NeighbourhoodGenerator generator,
            Rect playableBounds,
            NeighbourhoodBorderSettings settings)
        {
            if (!generator.HasSummitExit) return;

            Vector3 summitLocal = transform.InverseTransformPoint(
                generator.SummitExitPortal.WorldPosition);
            float start = playableBounds.yMax;
            float end = summitLocal.z + settings.TunnelDepth * 0.5f;
            if (end <= start) return;

            Transform root = new GameObject("UphillEmbankmentContainment").transform;
            root.SetParent(transform, false);
            float segmentLength = generator.TileSize * 4f;
            float sideClearance = Mathf.Max(settings.TunnelClearance,
                settings.TunnelWidth * 0.65f);
            for (int side = -1; side <= 1; side += 2)
            {
                for (float cursor = start; cursor < end; cursor += segmentLength)
                {
                    float segmentEnd = Mathf.Min(end, cursor + segmentLength);
                    float midpoint = (cursor + segmentEnd) * 0.5f;
                    Vector3 localBottom = new Vector3(side * sideClearance, 0f, midpoint);
                    Vector3 worldBottom = transform.TransformPoint(localBottom);
                    worldBottom.y = generator.SampleTerrainHeight(worldBottom) -
                                    settings.ColliderDepthBelowTerrain;
                    float height = settings.ColliderHeightAboveTerrain +
                                   settings.ColliderDepthBelowTerrain + settings.HillHeight;
                    GameObject wall = new GameObject(
                        $"UphillHillCollider_{side}_{Mathf.RoundToInt(cursor):000}");
                    wall.transform.SetParent(root, true);
                    wall.transform.SetPositionAndRotation(
                        worldBottom + Vector3.up * (height * 0.5f),
                        transform.rotation);
                    BoxCollider collider = wall.AddComponent<BoxCollider>();
                    collider.size = new Vector3(settings.ColliderThickness, height,
                        segmentEnd - cursor + 0.1f);
                    PerimeterColliderCount++;
                }
            }
        }

        private List<float> GetPortalDistances(NeighbourhoodGenerator generator, Edge edge)
        {
            List<float> distances = new List<float>(2);
            for (int index = 0; index < generator.TrafficRoadPortals.Count; index++)
            {
                TrafficRoadPortal portal = generator.TrafficRoadPortals[index];
                Vector3 localOutward = transform.InverseTransformDirection(portal.WorldOutward);
                if (Vector3.Dot(localOutward, edge.Outward) < 0.8f)
                {
                    continue;
                }

                Vector3 local = transform.InverseTransformPoint(portal.WorldPosition);
                float distance = Vector3.Dot(local - edge.Start, edge.Tangent);
                if (distance >= 0f && distance <= edge.Length)
                {
                    distances.Add(distance);
                }
            }

            if (generator.HasSummitExit)
            {
                TrafficRoadPortal portal = generator.SummitExitPortal;
                Vector3 localOutward = transform.InverseTransformDirection(portal.WorldOutward);
                if (Vector3.Dot(localOutward, edge.Outward) >= 0.8f)
                {
                    Vector3 local = transform.InverseTransformPoint(portal.WorldPosition);
                    float distance = Vector3.Dot(local - edge.Start, edge.Tangent);
                    if (distance >= 0f && distance <= edge.Length)
                    {
                        distances.Add(distance);
                    }
                }
            }

            distances.Sort();
            return distances;
        }

        private void CreateScenery(
            NeighbourhoodGenerator generator,
            Edge[] edges,
            NeighbourhoodBorderSettings settings,
            System.Random random)
        {
            Transform sceneryRoot = new GameObject("Scenery").transform;
            sceneryRoot.SetParent(transform, false);
            float spacing = Mathf.Lerp(
                settings.MaximumTreeSpacing,
                settings.MinimumTreeSpacing,
                settings.TreeDensity);
            for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                if (generator.HasSummitExit && edgeIndex == 2) continue;
                Edge edge = edges[edgeIndex];
                List<float> portalDistances = GetPortalDistances(generator, edge);
                int depthRows = Mathf.Max(2, Mathf.CeilToInt(settings.BorderWidth / spacing));
                for (int row = 0; row < depthRows; row++)
                {
                    float distance = spacing * (0.35f + (float)random.NextDouble() * 0.5f);
                    while (distance < edge.Length - spacing * 0.25f)
                    {
                        float jitter = RandomRange(random, -spacing * 0.35f, spacing * 0.35f);
                        bool blocksPortal = false;
                        for (int portalIndex = 0; portalIndex < portalDistances.Count; portalIndex++)
                        {
                            if (Mathf.Abs(distance + jitter - portalDistances[portalIndex]) <
                                Mathf.Max(settings.TunnelClearance, generator.TileSize))
                            {
                                blocksPortal = true;
                                break;
                            }
                        }
                        if (!blocksPortal && random.NextDouble() <= settings.TreeDensity)
                        {
                            float rowDepth = (row + 0.35f + (float)random.NextDouble() * 0.3f) /
                                             depthRows * settings.BorderWidth;
                            Vector3 localPosition = edge.Start + edge.Tangent * (distance + jitter) -
                                                    edge.Outward * rowDepth;
                            if (generator.HasSummitExit && localPosition.z >
                                edges[2].Start.z - Mathf.Max(settings.HillDepth, settings.BorderWidth))
                            {
                                distance += spacing;
                                continue;
                            }
                            Quaternion rotation = Quaternion.Euler(
                                0f, RandomRange(random, 0f, 360f), 0f);
                            if (PlaceScenery(generator, sceneryRoot,
                                    NeighbourhoodBorderType.Forest, localPosition,
                                    rotation, settings, random))
                                SceneryItemCount++;
                        }
                        distance += spacing;
                    }
                }
            }
        }

        private void CreateRearHillForest(
            NeighbourhoodGenerator generator,
            Rect bounds,
            float outerOffset,
            NeighbourhoodBorderSettings settings,
            System.Random random)
        {
            if (!generator.HasSummitExit || settings.TreeDensity <= 0f) return;
            Transform root = new GameObject("RearHillForest").transform;
            root.SetParent(transform, false);
            float spacing = Mathf.Lerp(settings.MaximumTreeSpacing,
                settings.MinimumTreeSpacing, settings.TreeDensity);
            float summitZ = transform.InverseTransformPoint(generator.SummitExitPortal.WorldPosition).z;
            float minimumX = bounds.xMin - outerOffset;
            float maximumX = bounds.xMax + outerOffset;
            float maximumZ = summitZ + outerOffset;
            for (float z = bounds.yMax + spacing * 0.5f; z < maximumZ - spacing * 0.5f; z += spacing)
            {
                for (float x = minimumX + spacing * 0.5f; x < maximumX - spacing * 0.5f; x += spacing)
                {
                    Vector3 local = new Vector3(
                        x + RandomRange(random, -spacing * 0.3f, spacing * 0.3f), 0f,
                        z + RandomRange(random, -spacing * 0.3f, spacing * 0.3f));
                    if (random.NextDouble() > settings.TreeDensity ||
                        !generator.IsHillTreePositionClear(transform.TransformPoint(local))) continue;
                    if (PlaceScenery(generator, root, NeighbourhoodBorderType.Forest, local,
                            Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 0f), settings, random))
                        SceneryItemCount++;
                }
            }
        }

        private bool PlaceScenery(
            NeighbourhoodGenerator generator,
            Transform parent,
            NeighbourhoodBorderType type,
            Vector3 localPosition,
            Quaternion rotation,
            NeighbourhoodBorderSettings settings,
            System.Random random)
        {
            Vector3 world = transform.TransformPoint(localPosition);
            world.y = generator.SampleTerrainHeight(world);
            localPosition = transform.InverseTransformPoint(world);
            Vector3 normal = SampleTerrainNormal(generator, world, 0.75f);
            if (Vector3.Angle(normal, Vector3.up) > settings.MaximumTreeSlope)
                return false;
            GameObject[] prefabs = type switch
            {
                NeighbourhoodBorderType.Forest => settings.TreePrefabs != null &&
                                                  settings.TreePrefabs.Length > 0
                    ? settings.TreePrefabs
                    : settings.ForestPrefabs,
                NeighbourhoodBorderType.Mountain => settings.MountainPrefabs,
                _ => settings.ConstructionPrefabs
            };
            GameObject prefab = SelectPrefab(prefabs, random);
            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab, parent);
                instance.name = $"{type}_{prefab.name}";
                instance.transform.localPosition = localPosition;
                Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, normal);
                instance.transform.rotation = slopeRotation * transform.rotation * rotation;
                float scale = RandomRange(random, settings.TreeScaleRange.x,
                    settings.TreeScaleRange.y);
                instance.transform.localScale *= scale;
                ConfigureInstancedTree(instance);
                return true;
            }

            GameObject fallback;
            switch (type)
            {
                case NeighbourhoodBorderType.Mountain:
                    fallback = CreateFallbackRock(parent, localPosition, rotation, random, true);
                    break;
                case NeighbourhoodBorderType.Construction:
                    return false;
                default:
                    fallback = CreateFallbackTree(parent, localPosition, rotation, random);
                    break;
            }
            fallback.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) *
                                          fallback.transform.rotation;
            float fallbackScale = RandomRange(random, settings.TreeScaleRange.x,
                settings.TreeScaleRange.y);
            fallback.transform.localScale *= fallbackScale;
            return true;
        }

        private void CreateTunnels(
            NeighbourhoodGenerator generator,
            NeighbourhoodBorderSettings settings)
        {
            Transform tunnelRoot = new GameObject("RoadTunnels").transform;
            tunnelRoot.SetParent(transform, false);
            for (int index = 0; index < generator.TrafficRoadPortals.Count; index++)
            {
                if (!generator.IsTrafficPortalClearOfSummit(
                        generator.TrafficRoadPortals[index]))
                {
                    Debug.LogError(
                        "Skipped a boundary tunnel whose reserved footprint overlaps the summit corridor.",
                        generator);
                    continue;
                }

                CreateTunnel(generator, tunnelRoot, generator.TrafficRoadPortals[index],
                    settings.StandardTunnelPrefab, settings, false, index);
            }

            if (generator.HasSummitExit)
            {
                CreateTunnel(generator, tunnelRoot, generator.SummitExitPortal,
                    settings.SummitGatewayPrefab, settings, true,
                    generator.TrafficRoadPortals.Count);
            }
        }

        private void CreateTunnel(
            NeighbourhoodGenerator generator,
            Transform parent,
            TrafficRoadPortal portal,
            GameObject prefab,
            NeighbourhoodBorderSettings settings,
            bool summit,
            int index)
        {
            Vector3 position = portal.WorldPosition;
            position.y = generator.SampleTerrainHeight(position);
            Vector3 previous = portal.WorldPosition - portal.WorldOutward * generator.TileSize;
            previous.y = generator.SampleTerrainHeight(previous);
            Vector3 roadDirection = position - previous;
            if (roadDirection.sqrMagnitude < 0.01f) roadDirection = portal.WorldOutward;
            Quaternion rotation = Quaternion.LookRotation(roadDirection.normalized, Vector3.up);
            if (prefab == null)
            {
                Debug.LogError("Assign a saved tunnel prefab in the region's Road Tunnels settings.", generator);
                return;
            }
            GameObject tunnel = Instantiate(prefab, position, rotation, parent);
            tunnel.name = summit ? "SummitRegionGateway" : $"BoundaryTunnel_{index + 1:00}";

            if (tunnel.TryGetComponent(out TunnelGeometry geometry))
            {
                geometry.Configure(settings, summit, generator.LandmarkRoads.TransitionTriggerDepth,
                    generator.LandmarkRoads.NextRegionId);
            }
            else if (summit)
            {
                RegionTransitionTrigger trigger = tunnel.GetComponentInChildren<RegionTransitionTrigger>(true);
                if (trigger == null)
                {
                    Debug.LogError("Summit tunnel prefab requires a saved RegionTransitionTrigger.", tunnel);
                }
                else
                {
                    trigger.gameObject.SetActive(true);
                    trigger.Initialise(generator.LandmarkRoads.NextRegionId);
                }
            }

            TunnelCount++;
        }

        private static Vector3 SampleTerrainNormal(
            NeighbourhoodGenerator generator,
            Vector3 world,
            float distance)
        {
            Vector3 left = world - Vector3.right * distance;
            Vector3 right = world + Vector3.right * distance;
            Vector3 back = world - Vector3.forward * distance;
            Vector3 forward = world + Vector3.forward * distance;
            left.y = generator.SampleTerrainHeight(left);
            right.y = generator.SampleTerrainHeight(right);
            back.y = generator.SampleTerrainHeight(back);
            forward.y = generator.SampleTerrainHeight(forward);
            return Vector3.Cross(forward - back, right - left).normalized;
        }

        private static void ConfigureInstancedTree(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Material[] materials = renderers[rendererIndex].sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    if (materials[materialIndex] != null)
                        materials[materialIndex].enableInstancing = true;
                }
            }

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                colliders[colliderIndex].enabled = false;
        }

        private GameObject CreateFallbackTree(
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            System.Random random)
        {
            GameObject root = new GameObject("ForestCluster");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = rotation;
            float height = RandomRange(random, 4.5f, 8f);
            CreatePrimitive(root.transform, PrimitiveType.Cylinder, "Trunk",
                new Vector3(0f, height * 0.28f, 0f), new Vector3(0.55f, height * 0.28f, 0.55f),
                GetMaterial(ref m_trunkMaterial, "Border Trunks", new Color(0.28f, 0.14f, 0.06f)));
            CreatePrimitive(root.transform, PrimitiveType.Sphere, "Canopy",
                new Vector3(0f, height * 0.72f, 0f), new Vector3(2.8f, height * 0.5f, 2.8f),
                GetMaterial(ref m_leafMaterial, "Border Leaves", new Color(0.16f, 0.48f, 0.12f)));
            if (random.NextDouble() < 0.35)
            {
                CreateFallbackRock(root.transform, new Vector3(1.8f, 0.25f, 0.6f),
                    Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 12f), random, false);
            }
            return root;
        }

        private GameObject CreateFallbackRock(
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            System.Random random,
            bool cliff)
        {
            GameObject root = new GameObject(cliff ? "Cliff" : "Rock");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = rotation * Quaternion.Euler(
                RandomRange(random, -8f, 8f), 0f, RandomRange(random, -8f, 8f));
            float height = cliff ? RandomRange(random, 6f, 13f) : RandomRange(random, 0.6f, 1.5f);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Stone",
                new Vector3(0f, height * 0.5f, 0f),
                cliff
                    ? new Vector3(RandomRange(random, 5f, 9f), height, RandomRange(random, 4f, 7f))
                    : new Vector3(1.8f, height, 1.5f),
                GetMaterial(ref m_rockMaterial, "Border Stone", new Color(0.38f, 0.36f, 0.31f)));
            return root;
        }

        private static GameObject CreatePrimitive(
            Transform parent,
            PrimitiveType type,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool keepCollider = false)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null && !keepCollider)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }
            return primitive;
        }

        private Material GetMaterial(ref Material cached, string name, Color colour)
        {
            if (cached != null) return cached;
            Shader shader = Shader.Find("Couch Guys/Toon Lit") ??
                            Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard");
            if (shader == null) return null;
            cached = new Material(shader) { name = name, color = colour };
            m_ownedMaterials.Add(cached);
            return cached;
        }

        private static GameObject SelectPrefab(GameObject[] prefabs, System.Random random)
        {
            if (prefabs == null || prefabs.Length == 0) return null;
            int first = random.Next(prefabs.Length);
            for (int offset = 0; offset < prefabs.Length; offset++)
            {
                GameObject prefab = prefabs[(first + offset) % prefabs.Length];
                if (prefab != null) return prefab;
            }
            return null;
        }

        private static NeighbourhoodBorderType SelectType(
            NeighbourhoodBorderType enabled,
            System.Random random)
        {
            NeighbourhoodBorderType[] candidates =
            {
                NeighbourhoodBorderType.Forest,
                NeighbourhoodBorderType.Mountain,
                NeighbourhoodBorderType.Construction
            };
            int count = 0;
            for (int index = 0; index < candidates.Length; index++)
                if ((enabled & candidates[index]) != 0) count++;
            if (count == 0) return NeighbourhoodBorderType.Forest;
            int selected = random.Next(count);
            for (int index = 0; index < candidates.Length; index++)
            {
                if ((enabled & candidates[index]) == 0) continue;
                if (selected-- == 0) return candidates[index];
            }
            return NeighbourhoodBorderType.Forest;
        }

        private static float RandomRange(System.Random random, float minimum, float maximum) =>
            minimum + (float)random.NextDouble() * (maximum - minimum);

        private void OnDestroy()
        {
            for (int index = 0; index < m_ownedMaterials.Count; index++)
            {
                Material material = m_ownedMaterials[index];
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            m_ownedMaterials.Clear();
        }
    }
}
