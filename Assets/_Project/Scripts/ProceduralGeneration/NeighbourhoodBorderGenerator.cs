using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Builds seeded border scenery and a simple, gap-free collision perimeter.
    /// Optional region prefabs replace the deliberately cheap fallback art.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodBorderGenerator : MonoBehaviour
    {
        private readonly List<Material> m_ownedMaterials = new List<Material>();
        private Material m_trunkMaterial;
        private Material m_leafMaterial;
        private Material m_rockMaterial;
        private Material m_barrierMaterial;
        private Material m_warningMaterial;

        public int PerimeterColliderCount { get; private set; }
        public int SceneryItemCount { get; private set; }
        public int RoadClosureCount { get; private set; }

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
            float outerOffset = clearWidth + settings.BorderWidth;
            Edge[] edges = CreateEdges(playableBounds, outerOffset);
            CreateCollisionPerimeter(generator, edges, settings);
            CreateScenery(generator, edges, settings, random);
            CreateRoadClosures(generator, playableBounds, settings, random);

            Rect recoveryBounds = Rect.MinMaxRect(
                playableBounds.xMin - outerOffset + settings.ColliderThickness,
                playableBounds.yMin - outerOffset + settings.ColliderThickness,
                playableBounds.xMax + outerOffset - settings.ColliderThickness,
                playableBounds.yMax + outerOffset - settings.ColliderThickness);
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
                GameObject wall = new GameObject($"BoundaryWall_{index + 1:00}");
                wall.transform.SetParent(colliderRoot, false);
                wall.transform.localPosition = edge.Start + edge.Tangent * (edge.Length * 0.5f);
                Vector3 wallWorld = wall.transform.position;
                wallWorld.y = worldMidpoint.y;
                wall.transform.position = wallWorld;
                wall.transform.localRotation = Quaternion.LookRotation(edge.Outward, Vector3.up);
                BoxCollider collider = wall.AddComponent<BoxCollider>();
                collider.size = new Vector3(
                    edge.Length + settings.ColliderThickness * 2f,
                    height / Mathf.Max(0.001f, wall.transform.lossyScale.y),
                    settings.ColliderThickness);
                PerimeterColliderCount++;
            }
        }

        private void CreateScenery(
            NeighbourhoodGenerator generator,
            Edge[] edges,
            NeighbourhoodBorderSettings settings,
            System.Random random)
        {
            Transform sceneryRoot = new GameObject("Scenery").transform;
            sceneryRoot.SetParent(transform, false);
            float spacing = Mathf.Lerp(16f, 7f, settings.SceneryDensity);
            for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                Edge edge = edges[edgeIndex];
                float distance = spacing * 0.5f;
                float sectionRemaining = 0f;
                NeighbourhoodBorderType type = NeighbourhoodBorderType.None;
                while (distance < edge.Length - spacing * 0.25f)
                {
                    if (sectionRemaining <= 0f)
                    {
                        type = SelectType(settings.EnabledTypes, random);
                        sectionRemaining = RandomRange(
                            random,
                            settings.MinimumSectionLength,
                            settings.MaximumSectionLength);
                    }

                    float jitter = RandomRange(random, -spacing * 0.25f, spacing * 0.25f);
                    float inset = RandomRange(random, 0.5f, settings.BorderWidth * 0.55f);
                    Vector3 localPosition = edge.Start + edge.Tangent * (distance + jitter) -
                                            edge.Outward * inset;
                    Quaternion rotation = Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 0f);
                    PlaceScenery(generator, sceneryRoot, type, localPosition, rotation, settings, random);
                    SceneryItemCount++;
                    distance += spacing;
                    sectionRemaining -= spacing;
                }
            }
        }

        private void PlaceScenery(
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
            GameObject[] prefabs = type switch
            {
                NeighbourhoodBorderType.Forest => settings.ForestPrefabs,
                NeighbourhoodBorderType.Mountain => settings.MountainPrefabs,
                _ => settings.ConstructionPrefabs
            };
            GameObject prefab = SelectPrefab(prefabs, random);
            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab, parent);
                instance.name = $"{type}_{prefab.name}";
                instance.transform.localPosition = localPosition;
                instance.transform.localRotation = rotation;
                return;
            }

            switch (type)
            {
                case NeighbourhoodBorderType.Mountain:
                    CreateFallbackRock(parent, localPosition, rotation, random, true);
                    break;
                case NeighbourhoodBorderType.Construction:
                    CreateFallbackBarrier(parent, localPosition, rotation, random, false);
                    break;
                default:
                    CreateFallbackTree(parent, localPosition, rotation, random);
                    break;
            }
        }

        private void CreateRoadClosures(
            NeighbourhoodGenerator generator,
            Rect playableBounds,
            NeighbourhoodBorderSettings settings,
            System.Random random)
        {
            Transform closureRoot = new GameObject("RoadClosures").transform;
            closureRoot.SetParent(transform, false);
            float edgeTolerance = generator.TileSize * 0.65f;
            for (int index = 0; index < generator.GeneratedRoads.Count; index++)
            {
                GeneratedRoad road = generator.GeneratedRoads[index];
                Vector3 local = transform.InverseTransformPoint(road.transform.position);
                Vector3 outward;
                if (Mathf.Abs(local.x - playableBounds.xMin) <= edgeTolerance) outward = Vector3.left;
                else if (Mathf.Abs(local.x - playableBounds.xMax) <= edgeTolerance) outward = Vector3.right;
                else if (Mathf.Abs(local.z - playableBounds.yMin) <= edgeTolerance) outward = Vector3.back;
                else if (Mathf.Abs(local.z - playableBounds.yMax) <= edgeTolerance) outward = Vector3.forward;
                else continue;

                Vector3 barrierLocal = local + outward * (generator.TileSize * 0.48f);
                Vector3 world = transform.TransformPoint(barrierLocal);
                world.y = generator.SampleTerrainHeight(world) + 0.65f;
                barrierLocal = transform.InverseTransformPoint(world);
                Quaternion rotation = Quaternion.LookRotation(outward, Vector3.up);
                GameObject prefab = SelectPrefab(settings.RoadClosurePrefabs, random);
                GameObject closure;
                if (prefab != null)
                {
                    closure = Instantiate(prefab, closureRoot);
                    closure.name = $"RoadClosed_{road.Coordinate.X}_{road.Coordinate.Y}";
                    closure.transform.localPosition = barrierLocal;
                    closure.transform.localRotation = rotation;
                }
                else
                {
                    closure = CreateFallbackBarrier(
                        closureRoot,
                        barrierLocal,
                        rotation,
                        random,
                        true);
                }

                BoxCollider collider = closure.GetComponent<BoxCollider>();
                if (collider == null) collider = closure.AddComponent<BoxCollider>();
                collider.size = new Vector3(generator.TileSize * 0.9f, 2.2f, 0.8f);
                RoadClosureCount++;
            }
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

        private GameObject CreateFallbackBarrier(
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            System.Random random,
            bool roadClosure)
        {
            GameObject root = new GameObject(roadClosure ? "RoadClosedBarrier" : "ConstructionBarrier");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = rotation;
            float width = roadClosure ? 7.5f : RandomRange(random, 3f, 5f);
            CreatePrimitive(root.transform, PrimitiveType.Cube, "Barrier",
                Vector3.zero, new Vector3(width, 0.8f, 0.65f),
                GetMaterial(ref m_barrierMaterial, "Border Barriers", new Color(0.85f, 0.86f, 0.79f)));
            for (int stripe = -2; stripe <= 2; stripe++)
            {
                CreatePrimitive(root.transform, PrimitiveType.Cube, "WarningStripe",
                    new Vector3(stripe * width * 0.18f, 0f, -0.34f),
                    new Vector3(width * 0.09f, 0.82f, 0.04f),
                    GetMaterial(ref m_warningMaterial, "Border Warning Orange", new Color(1f, 0.28f, 0.03f)));
            }
            return root;
        }

        private static GameObject CreatePrimitive(
            Transform parent,
            PrimitiveType type,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
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
