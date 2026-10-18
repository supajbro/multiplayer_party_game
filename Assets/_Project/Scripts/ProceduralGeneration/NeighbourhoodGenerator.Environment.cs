using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        private RegionEnvironmentSettings m_environmentSettings = RegionEnvironmentSettings.SuburbsDefault;
        private readonly List<OrientedFootprint> m_environmentFootprints = new List<OrientedFootprint>();
        private readonly List<GeneratedParkRecord> m_generatedParks = new List<GeneratedParkRecord>();
        private readonly HashSet<Vector2Int> m_parkBlocks = new HashSet<Vector2Int>();
        private Transform m_streetlightsRoot;
        private Transform m_parksRoot;
        private Transform m_residentialDecorationsRoot;
        private Transform m_roadsideDecorationsRoot;

        public int GeneratedParkCount => m_generatedParks.Count;
        public int GeneratedParkBlockCount => m_parkBlocks.Count;
        public int GeneratedStreetlightCount => m_streetlightsRoot != null ? m_streetlightsRoot.childCount : 0;
        public int GeneratedResidentialDecorationCount =>
            m_residentialDecorationsRoot != null ? m_residentialDecorationsRoot.childCount : 0;
        public int GeneratedRoadsideDecorationCount =>
            m_roadsideDecorationsRoot != null ? m_roadsideDecorationsRoot.childCount : 0;

        public bool IsCoordinateInParkBlock(GridCoordinate coordinate)
        {
            Vector2Int local = GridToLocal(coordinate);
            int horizontalSpan = m_blockWidth + 1;
            int verticalSpan = m_blockHeight + 1;
            int relativeX = local.x - m_minimumLocalX;
            if (relativeX < 0 || local.y < 0)
            {
                return false;
            }

            int blockX = relativeX / horizontalSpan;
            int blockY = local.y / verticalSpan;
            return blockX >= 0 && blockX < m_gridWidth &&
                   blockY >= 0 && blockY < m_gridHeight &&
                   m_parkBlocks.Contains(new Vector2Int(blockX, blockY));
        }

        private sealed class GeneratedParkRecord
        {
            public Transform Root;
            public ParkSize Size;
            public Vector3 Centre;
            public Vector3 Right;
            public Vector3 Forward;
            public Vector2 WorldSize;
            public Vector3 Entrance;
        }

        private void ApplyEnvironmentAtmosphere()
        {
            CartoonLightingSettings settings = m_environmentSettings.Lighting;
            settings.Validate();
            if (!settings.Enabled) return;

            Light sun = null;
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int index = 0; index < lights.Length; index++)
            {
                if (lights[index].type == LightType.Directional)
                {
                    sun = lights[index];
                    break;
                }
            }

            if (sun == null)
            {
                GameObject sunObject = new GameObject("Cartoon Sun", typeof(Light));
                sunObject.transform.SetParent(transform, false);
                sun = sunObject.GetComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
            }

            sun.transform.rotation = Quaternion.Euler(settings.SunEulerAngles);
            sun.color = settings.SunColour;
            sun.intensity = settings.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = settings.AmbientSkyColour;
            RenderSettings.ambientEquatorColor = settings.AmbientEquatorColour;
            RenderSettings.ambientGroundColor = settings.AmbientGroundColour;
            RenderSettings.ambientIntensity = settings.AmbientIntensity;
            RenderSettings.fog = settings.FogEnabled;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = settings.FogColour;
            RenderSettings.fogStartDistance = settings.FogStartDistance;
            RenderSettings.fogEndDistance = settings.FogEndDistance;
            if (settings.SkyboxMaterial != null) RenderSettings.skybox = settings.SkyboxMaterial;
            if (settings.PostProcessingProfile != null)
            {
                Transform existing = transform.Find("Neighbourhood Post Processing");
                Volume volume;
                if (existing == null)
                {
                    GameObject volumeObject = new GameObject("Neighbourhood Post Processing", typeof(Volume));
                    volumeObject.transform.SetParent(transform, false);
                    volume = volumeObject.GetComponent<Volume>();
                }
                else
                {
                    volume = existing.GetComponent<Volume>();
                    if (volume == null) volume = existing.gameObject.AddComponent<Volume>();
                }
                volume.isGlobal = true;
                volume.priority = 10f;
                volume.sharedProfile = settings.PostProcessingProfile;
            }
        }

        private void GenerateParksAndMajorLandmarks(System.Random random)
        {
            ParkGenerationSettings settings = m_environmentSettings.Parks;
            settings.Validate();
            if (!settings.Enabled || settings.MaximumParks <= 0 || m_parksRoot == null) return;

            int target = random.Next(settings.MinimumParks, settings.MaximumParks + 1);
            List<Vector2Int> blocks = new List<Vector2Int>(m_gridWidth * m_gridHeight);
            for (int y = 0; y < m_gridHeight; y++)
                for (int x = 0; x < m_gridWidth; x++)
                    blocks.Add(new Vector2Int(x, y));
            Shuffle(blocks, random);

            for (int index = 0; index < blocks.Count && m_generatedParks.Count < target; index++)
            {
                ParkSize requestedSize = SelectParkSize(random, settings);
                if (TryCreatePark(blocks[index], requestedSize, settings, random)) continue;
                if (requestedSize == ParkSize.Large && TryCreatePark(blocks[index], ParkSize.Medium, settings, random)) continue;
                if (requestedSize != ParkSize.Small) TryCreatePark(blocks[index], ParkSize.Small, settings, random);
            }

            // A park is a hard generation rule. If terrain-slope filtering rejected
            // every normal candidate, reserve the first otherwise valid block and
            // conform a small park to its sampled average height.
            for (int index = 0;
                 index < blocks.Count && m_generatedParks.Count < settings.MinimumParks;
                 index++)
            {
                if (!m_parkBlocks.Contains(blocks[index]))
                    TryCreatePark(blocks[index], ParkSize.Small, settings, random, false);
            }

            GenerateConfiguredLandmarkZones(blocks, random);
        }

        private void GenerateConfiguredLandmarkZones(List<Vector2Int> blocks, System.Random random)
        {
            LandmarkGenerationSettings settings = m_environmentSettings.Landmarks;
            settings.Validate();
            if (!settings.Enabled || settings.MaximumCount <= 0 || settings.Prefabs.Length == 0) return;
            int target = random.Next(settings.MinimumCount, settings.MaximumCount + 1);
            int placed = 0;
            List<Vector3> centres = new List<Vector3>();
            for (int blockIndex = 0; blockIndex < blocks.Count && placed < target; blockIndex++)
            {
                GameObject prefab = SelectConfiguredLandmark(random, settings.Prefabs);
                if (prefab == null) break;
                EnvironmentPlacementMetadata metadata = prefab.GetComponent<EnvironmentPlacementMetadata>();
                Vector2 worldSize = metadata != null ? metadata.Footprint : Vector2.one * m_roadTileSize;
                Vector2Int cellsSize = new Vector2Int(
                    Mathf.Clamp(Mathf.CeilToInt((worldSize.x + settings.Clearance * 2f) / m_roadTileSize), 1, m_blockWidth),
                    Mathf.Clamp(Mathf.CeilToInt((worldSize.y + settings.Clearance * 2f) / m_roadTileSize), 1, m_blockHeight));
                Vector2Int block = blocks[blockIndex];
                int horizontalSpan = m_blockWidth + 1;
                int verticalSpan = m_blockHeight + 1;
                int blockMinimumX = m_minimumLocalX + block.x * horizontalSpan + 1;
                int blockMaximumX = blockMinimumX + m_blockWidth - 1;
                int blockMinimumY = block.y * verticalSpan + 1;
                int blockMaximumY = blockMinimumY + m_blockHeight - 1;
                int startX = blockMinimumX + (m_blockWidth - cellsSize.x) / 2;
                int startY = blockMinimumY + (m_blockHeight - cellsSize.y) / 2;
                List<GridCoordinate> cells = new List<GridCoordinate>(cellsSize.x * cellsSize.y);
                for (int y = 0; y < cellsSize.y; y++)
                    for (int x = 0; x < cellsSize.x; x++)
                        cells.Add(LocalToGrid(startX + x, startY + y));
                if (!CanPlaceProperty(cells, blockMinimumX, blockMaximumX, blockMinimumY, blockMaximumY)) continue;
                Vector3 centre = CalculateCellCentre(cells);
                bool spaced = true;
                for (int i = 0; i < centres.Count; i++)
                    if (HorizontalSqrDistance(centre, centres[i]) < settings.MinimumSpacing * settings.MinimumSpacing)
                    { spaced = false; break; }
                if (!spaced || !IsAreaSlopeValid(centre, worldSize, settings.MaximumTerrainSlope, out float height)) continue;
                centre.y = height;
                if (!TryPlaceEnvironmentPrefab(prefab, centre, RandomYaw(random), m_landmarksRoot, true, out GameObject instance)) continue;
                instance.name = $"Landmark_Configured_{placed:00}_{prefab.name}";
                for (int i = 0; i < cells.Count; i++) m_propertyCells.Add(cells[i]);
                centres.Add(centre);
                placed++;
                GeneratedLandmarkCount++;
            }
        }

        private static GameObject SelectConfiguredLandmark(System.Random random, WeightedEnvironmentPrefab[] entries)
        {
            float total = 0f;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Prefab != null) total += Mathf.Max(0.01f, entries[i].Weight);
            if (total <= 0f) return null;
            float value = (float)random.NextDouble() * total;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Prefab == null) continue;
                value -= Mathf.Max(0.01f, entries[i].Weight);
                if (value <= 0f) return entries[i].Prefab;
            }
            return null;
        }

        private static ParkSize SelectParkSize(System.Random random, ParkGenerationSettings settings)
        {
            float total = settings.SmallProbability + settings.MediumProbability + settings.LargeProbability;
            if (total <= 0.001f) return ParkSize.Small;
            float value = (float)random.NextDouble() * total;
            if (value < settings.LargeProbability) return ParkSize.Large;
            value -= settings.LargeProbability;
            return value < settings.MediumProbability ? ParkSize.Medium : ParkSize.Small;
        }

        private bool TryCreatePark(
            Vector2Int block,
            ParkSize parkSize,
            ParkGenerationSettings settings,
            System.Random random,
            bool enforceTerrainSlope = true)
        {
            Vector2Int footprint = parkSize switch
            {
                ParkSize.Large => settings.LargeFootprint,
                ParkSize.Medium => settings.MediumFootprint,
                _ => settings.SmallFootprint
            };
            footprint.x = Mathf.Clamp(footprint.x, 1, m_blockWidth);
            footprint.y = Mathf.Clamp(footprint.y, 1, m_blockHeight);

            int horizontalSpan = m_blockWidth + 1;
            int verticalSpan = m_blockHeight + 1;
            int blockMinimumX = m_minimumLocalX + block.x * horizontalSpan + 1;
            int blockMaximumX = blockMinimumX + m_blockWidth - 1;
            int blockMinimumY = block.y * verticalSpan + 1;
            int blockMaximumY = blockMinimumY + m_blockHeight - 1;
            int startX = blockMinimumX + (m_blockWidth - footprint.x) / 2;
            int startY = blockMinimumY + (m_blockHeight - footprint.y) / 2;
            List<GridCoordinate> blockCells = new List<GridCoordinate>(m_blockWidth * m_blockHeight);
            for (int y = blockMinimumY; y <= blockMaximumY; y++)
                for (int x = blockMinimumX; x <= blockMaximumX; x++)
                    blockCells.Add(LocalToGrid(x, y));
            if (!CanPlaceProperty(
                    blockCells,
                    blockMinimumX,
                    blockMaximumX,
                    blockMinimumY,
                    blockMaximumY))
                return false;

            List<GridCoordinate> cells = new List<GridCoordinate>(footprint.x * footprint.y);
            for (int y = 0; y < footprint.y; y++)
                for (int x = 0; x < footprint.x; x++)
                    cells.Add(LocalToGrid(startX + x, startY + y));
            Vector3 centre = CalculateCellCentre(cells);
            Vector2 worldSize = new Vector2(footprint.x * m_roadTileSize, footprint.y * m_roadTileSize);
            bool slopeIsValid = IsAreaSlopeValid(
                centre,
                worldSize,
                settings.MaximumTerrainSlope,
                out float centreHeight);
            if (enforceTerrainSlope && !slopeIsValid) return false;
            centre.y = centreHeight + 0.025f;
            GameObject prefab = parkSize switch
            {
                ParkSize.Large => m_environmentSettings.Prefabs.LargePark,
                ParkSize.Medium => m_environmentSettings.Prefabs.MediumPark,
                _ => m_environmentSettings.Prefabs.SmallPark
            };
            GameObject root = prefab != null
                ? Instantiate(prefab, centre, Quaternion.identity, m_parksRoot)
                : new GameObject($"{parkSize}_Park");
            if (root.transform.parent != m_parksRoot) root.transform.SetParent(m_parksRoot, true);
            root.name = $"Park_{m_generatedParks.Count:00}_{parkSize}";

            Vector3 right = DirectionVector(TurnRight(m_startDirection));
            Vector3 forward = DirectionVector(m_startDirection);
            List<PropertyCandidate> entrances = CreatePropertyCandidates(
                blockMinimumX, blockMaximumX, blockMinimumY, blockMaximumY);
            Vector3 entrance = entrances.Count > 0 ? GridToWorld(entrances[random.Next(entrances.Count)].Anchor) : centre;
            entrance.y = SamplePlacementTerrainHeight(entrance) + 0.04f;
            m_generatedParks.Add(new GeneratedParkRecord
            {
                Root = root.transform,
                Size = parkSize,
                Centre = centre,
                Right = right,
                Forward = forward,
                WorldSize = worldSize,
                Entrance = entrance
            });
            m_parkBlocks.Add(block);
            for (int cellIndex = 0; cellIndex < blockCells.Count; cellIndex++)
                m_propertyCells.Add(blockCells[cellIndex]);
            GeneratedLandmarkCount++;
            return true;
        }

        private bool IsAreaSlopeValid(Vector3 centre, Vector2 size, float maximumSlope, out float averageHeight)
        {
            Vector3[] samples =
            {
                centre + new Vector3(-size.x * 0.45f, 0f, -size.y * 0.45f),
                centre + new Vector3(size.x * 0.45f, 0f, -size.y * 0.45f),
                centre + new Vector3(-size.x * 0.45f, 0f, size.y * 0.45f),
                centre + new Vector3(size.x * 0.45f, 0f, size.y * 0.45f),
                centre
            };
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            float sum = 0f;
            for (int index = 0; index < samples.Length; index++)
            {
                float height = SamplePlacementTerrainHeight(samples[index]);
                minimum = Mathf.Min(minimum, height);
                maximum = Mathf.Max(maximum, height);
                sum += height;
            }
            averageHeight = sum / samples.Length;
            float run = Mathf.Max(0.1f, Mathf.Min(size.x, size.y) * 0.9f);
            return Mathf.Atan2(maximum - minimum, run) * Mathf.Rad2Deg <= maximumSlope;
        }

        private void PopulateGeneratedParks(System.Random random)
        {
            ParkGenerationSettings settings = m_environmentSettings.Parks;
            EnvironmentPrefabLibrary prefabs = m_environmentSettings.Prefabs;
            for (int parkIndex = 0; parkIndex < m_generatedParks.Count; parkIndex++)
            {
                GeneratedParkRecord park = m_generatedParks[parkIndex];
                CreateParkPath(park, prefabs.ParkPath);
                CreateParkBoundaries(park, prefabs.ParkBoundary);
                TryPlaceEnvironmentPrefab(prefabs.ParkSign, park.Entrance, Quaternion.LookRotation(park.Centre - park.Entrance), park.Root, false);

                int treeCount = Mathf.Max(2, Mathf.RoundToInt((park.WorldSize.x + park.WorldSize.y) / 18f * settings.DecorationDensity));
                int benchCount = Mathf.Max(1, Mathf.RoundToInt(treeCount * 0.45f));
                for (int i = 0; i < treeCount; i++)
                {
                    Vector3 local = RandomParkPoint(park, random, 0.38f, 0.46f);
                    TryPlaceEnvironmentPrefab(prefabs.Tree, local, RandomYaw(random), park.Root, true);
                }
                for (int i = 0; i < benchCount; i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    Vector3 position = park.Centre + park.Right * side * park.WorldSize.x * 0.22f +
                                       park.Forward * Mathf.Lerp(-park.WorldSize.y * 0.24f, park.WorldSize.y * 0.24f,
                                           (float)random.NextDouble());
                    TryPlaceEnvironmentPrefab(prefabs.Bench, position,
                        Quaternion.LookRotation(-side * park.Right), park.Root, true);
                }
                TryPlaceEnvironmentPrefab(prefabs.PublicBin,
                    Vector3.Lerp(park.Entrance, park.Centre, 0.18f) + park.Right * 1.6f,
                    Quaternion.LookRotation(park.Centre - park.Entrance), park.Root, true);
                if (random.NextDouble() < 0.85)
                    TryPlaceEnvironmentPrefab(prefabs.FlowerCluster, RandomParkPoint(park, random, 0.18f, 0.36f), RandomYaw(random), park.Root, true);
                if (park.Size != ParkSize.Small)
                {
                    GameObject feature = SelectParkLandmark(random);
                    TryPlaceEnvironmentPrefab(feature, park.Centre, RandomYaw(random), park.Root, true);
                }
                if (park.Size == ParkSize.Large && random.NextDouble() < 0.75)
                    TryPlaceEnvironmentPrefab(prefabs.Playground,
                        park.Centre + park.Right * park.WorldSize.x * 0.24f,
                        RandomYaw(random), park.Root, true);
            }
        }

        private Vector3 RandomParkPoint(GeneratedParkRecord park, System.Random random, float minimumRadius, float maximumRadius)
        {
            float x = Mathf.Lerp(minimumRadius, maximumRadius, (float)random.NextDouble()) *
                      (random.Next(0, 2) == 0 ? -1f : 1f) * park.WorldSize.x;
            float z = Mathf.Lerp(minimumRadius, maximumRadius, (float)random.NextDouble()) *
                      (random.Next(0, 2) == 0 ? -1f : 1f) * park.WorldSize.y;
            return park.Centre + park.Right * x + park.Forward * z;
        }

        private void CreateParkPath(GeneratedParkRecord park, GameObject pathPrefab)
        {
            if (pathPrefab == null) return;
            Vector3 direction = Vector3.ProjectOnPlane(park.Centre - park.Entrance, Vector3.up);
            float length = direction.magnitude;
            if (length < 0.5f) return;
            GameObject path = Instantiate(pathPrefab,
                Vector3.Lerp(park.Entrance, park.Centre, 0.5f), Quaternion.LookRotation(direction), park.Root);
            path.name = "WalkingPath";
            EnvironmentPlacementMetadata metadata = path.GetComponent<EnvironmentPlacementMetadata>();
            float baseLength = metadata != null ? metadata.Footprint.y : 1f;
            Vector3 scale = path.transform.localScale;
            scale.z *= length / Mathf.Max(0.1f, baseLength);
            path.transform.localScale = scale;
            SnapToTerrain(path.transform, 0.035f);
        }

        private void CreateParkBoundaries(GeneratedParkRecord park, GameObject boundaryPrefab)
        {
            if (boundaryPrefab == null) return;
            Vector3[] positions =
            {
                park.Centre + park.Right * park.WorldSize.x * 0.48f,
                park.Centre - park.Right * park.WorldSize.x * 0.48f,
                park.Centre + park.Forward * park.WorldSize.y * 0.48f,
                park.Centre - park.Forward * park.WorldSize.y * 0.48f
            };
            for (int i = 0; i < positions.Length; i++)
            {
                bool alongForward = i < 2;
                GameObject boundary = Instantiate(boundaryPrefab, positions[i],
                    Quaternion.LookRotation(alongForward ? park.Forward : park.Right), park.Root);
                boundary.name = $"Boundary_{i}";
                EnvironmentPlacementMetadata metadata = boundary.GetComponent<EnvironmentPlacementMetadata>();
                float sourceLength = metadata != null ? metadata.Footprint.y : 1f;
                float targetLength = alongForward ? park.WorldSize.y : park.WorldSize.x;
                Vector3 scale = boundary.transform.localScale;
                scale.z *= targetLength / Mathf.Max(0.1f, sourceLength);
                boundary.transform.localScale = scale;
                SnapToTerrain(boundary.transform, 0.05f);
            }
        }

        private GameObject SelectParkLandmark(System.Random random)
        {
            WeightedEnvironmentPrefab[] entries = m_environmentSettings.Landmarks.Prefabs;
            float total = 0f;
            if (entries != null)
                for (int i = 0; i < entries.Length; i++)
                    if (entries[i].AllowedInParks && entries[i].Prefab != null) total += Mathf.Max(0.01f, entries[i].Weight);
            if (total > 0f)
            {
                float value = (float)random.NextDouble() * total;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (!entries[i].AllowedInParks || entries[i].Prefab == null) continue;
                    value -= Mathf.Max(0.01f, entries[i].Weight);
                    if (value <= 0f) return entries[i].Prefab;
                }
            }
            EnvironmentPrefabLibrary prefabs = m_environmentSettings.Prefabs;
            return random.Next(0, 3) switch { 0 => prefabs.Fountain, 1 => prefabs.Statue, _ => prefabs.Gazebo };
        }

        private void GenerateStreetlights(System.Random random)
        {
            StreetlightGenerationSettings settings = m_environmentSettings.Streetlights;
            settings.Validate();
            if (!settings.Enabled || settings.Prefab == null || settings.MaximumCount <= 0) return;
            float nextSpacing = RandomRange(random, settings.AverageSpacing - settings.SpacingVariation,
                settings.AverageSpacing + settings.SpacingVariation);
            float travelled = nextSpacing;
            int count = 0;
            for (int index = 0; index < m_roadOrder.Count && count < settings.MaximumCount; index++)
            {
                GridCoordinate road = m_roadOrder[index];
                if (IsTunnelApproachRoad(road) ||
                    CountConnections(m_logicalRoads[road]) >= 3 ||
                    IsNearIntersection(road, settings.MinimumDistanceFromIntersections)) continue;
                travelled += m_roadTileSize;
                if (travelled < nextSpacing) continue;
                List<CardinalDirection> openSides = GetOpenRoadSides(road);
                if (openSides.Count == 0) continue;
                CardinalDirection side = openSides[(index + random.Next(openSides.Count)) % openSides.Count];
                Vector3 outward = DirectionVector(side);
                Vector3 position = GridToWorld(road) + outward *
                    (m_roadTileSize * 0.5f + m_sidewalkWidth + settings.SidewalkOffset);
                position.y = TrySampleGeneratedTerrain(position, out RaycastHit groundHit)
                    ? groundHit.point.y
                    : SamplePlacementTerrainHeight(position);
                if (IsNearDriveway(position, settings.MinimumClearanceFromDriveways)) continue;
                if (!TryPlaceEnvironmentPrefab(settings.Prefab, position,
                        Quaternion.LookRotation(-outward, Vector3.up), m_streetlightsRoot, true, out GameObject instance)) continue;
                RemoveCollidersFromHierarchy(instance);
                GroundStreetlightToGeneratedTerrain(instance);
                StreetlightPlaceholder light = instance.GetComponent<StreetlightPlaceholder>();
                if (light != null) light.Configure(settings.LightingEnabled, settings.LightColour,
                    settings.LightIntensity, settings.LightRange);
                instance.name = $"StreetLight_{count:000}";
                count++;
                travelled = 0f;
                nextSpacing = RandomRange(random, settings.AverageSpacing - settings.SpacingVariation,
                    settings.AverageSpacing + settings.SpacingVariation);
            }
        }

        private List<CardinalDirection> GetOpenRoadSides(GridCoordinate road)
        {
            List<CardinalDirection> sides = new List<CardinalDirection>(4);
            for (int directionIndex = 0; directionIndex < 4; directionIndex++)
            {
                CardinalDirection direction = (CardinalDirection)directionIndex;
                if (!m_roadCells.Contains(road + DirectionOffset(direction))) sides.Add(direction);
            }
            return sides;
        }

        private bool IsNearIntersection(GridCoordinate road, float distance)
        {
            if (distance <= 0f) return false;
            int radius = Mathf.CeilToInt(distance / m_roadTileSize);
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    GridCoordinate candidate = road + new GridCoordinate(x, y);
                    if (m_logicalRoads.TryGetValue(candidate, out RoadConnections connections) &&
                        CountConnections(connections) >= 3 && Mathf.Sqrt(x * x + y * y) * m_roadTileSize < distance)
                        return true;
                }
            return false;
        }

        private void GenerateResidentialDecorations(System.Random random)
        {
            ResidentialDecorationSettings settings = m_environmentSettings.ResidentialDecorations;
            settings.Validate();
            if (!settings.Enabled) return;
            EnvironmentPrefabLibrary prefabs = m_environmentSettings.Prefabs;
            for (int index = 0; index < m_generatedProperties.Count; index++)
            {
                GeneratedProperty property = m_generatedProperties[index];
                if (property == null || property.GetComponent<GeneratedLot>()?.LotType != LotType.Residential) continue;
                Vector3 road = GridToWorld(property.RoadCoordinate);
                Vector3 front = Vector3.ProjectOnPlane(road - property.transform.position, Vector3.up).normalized;
                if (front.sqrMagnitude < 0.1f) front = property.transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, front).normalized;
                OrientedFootprint house = FindClosestHouseFootprint(property.transform.position);
                float halfWidth = Mathf.Max(1f, house.HalfSize.x);
                float halfDepth = Mathf.Max(1f, house.HalfSize.y);
                float side = random.Next(0, 2) == 0 ? -1f : 1f;
                Vector3 houseCentre = new Vector3(house.Centre.x, property.transform.position.y, house.Centre.y);
                Vector3 frontGarden = houseCentre + front * (ProjectedHalfExtent(house, front) + 0.9f);

                if (random.NextDouble() < settings.MailboxProbability)
                    TryPlaceEnvironmentPrefab(prefabs.Mailbox,
                        property.DrivewayStart + right * side * (m_drivewayWidth * 0.5f + 1.15f),
                        Quaternion.LookRotation(front), m_residentialDecorationsRoot, true);
                if (random.NextDouble() < settings.BinProbability)
                {
                    Vector3 bin = frontGarden + right * side * Mathf.Max(2.2f, halfWidth * 0.42f);
                    TryPlaceEnvironmentPrefab(prefabs.Bin, bin, Quaternion.LookRotation(front), m_residentialDecorationsRoot, true);
                    if (random.NextDouble() < settings.SecondBinProbability)
                        TryPlaceEnvironmentPrefab(prefabs.Bin, bin - right * side * 0.9f,
                            Quaternion.LookRotation(front), m_residentialDecorationsRoot, true);
                }
                int gardenAttempts = Mathf.Max(1, Mathf.RoundToInt(3f * settings.GardenDensity));
                for (int attempt = 0; attempt < gardenAttempts; attempt++)
                {
                    float lateral = RandomRange(random, -halfWidth * 0.38f, halfWidth * 0.38f);
                    Vector3 position = frontGarden + right * lateral + front * RandomRange(random, -0.8f, 0.8f);
                    GameObject decoration = random.NextDouble() < settings.FlowerBedProbability
                        ? prefabs.FlowerCluster
                        : prefabs.Bush;
                    if (decoration == prefabs.Bush && random.NextDouble() > settings.BushProbability) continue;
                    TryPlaceEnvironmentPrefab(decoration, position, RandomYaw(random), m_residentialDecorationsRoot, true);
                }
                if (random.NextDouble() < settings.OrnamentalTreeProbability)
                    TryPlaceEnvironmentPrefab(prefabs.Tree,
                        houseCentre - front * (ProjectedHalfExtent(house, front) + 1.35f) + right * side * halfWidth * 0.32f,
                        RandomYaw(random), m_residentialDecorationsRoot, true);
            }
        }

        private OrientedFootprint FindClosestHouseFootprint(Vector3 position)
        {
            Vector2 point = new Vector2(position.x, position.z);
            OrientedFootprint closest = new OrientedFootprint(point, Vector2.right, Vector2.up, Vector2.one);
            float distance = float.PositiveInfinity;
            for (int i = 0; i < m_placedHouseFootprints.Count; i++)
            {
                float candidate = (m_placedHouseFootprints[i].Centre - point).sqrMagnitude;
                if (candidate >= distance) continue;
                distance = candidate;
                closest = m_placedHouseFootprints[i];
            }
            return closest;
        }

        private static float ProjectedHalfExtent(OrientedFootprint footprint, Vector3 direction)
        {
            Vector2 axis = new Vector2(direction.x, direction.z).normalized;
            return Mathf.Abs(Vector2.Dot(axis, footprint.Right)) * footprint.HalfSize.x +
                   Mathf.Abs(Vector2.Dot(axis, footprint.Forward)) * footprint.HalfSize.y;
        }

        private void GenerateRoadsideDecorations(System.Random random)
        {
            EnvironmentPrefabLibrary prefabs = m_environmentSettings.Prefabs;
            int spawned = 0;
            for (int index = 0; index < m_roadOrder.Count && spawned < 48; index++)
            {
                if (random.NextDouble() > 0.12) continue;
                GridCoordinate road = m_roadOrder[index];
                if (IsTunnelApproachRoad(road) ||
                    CountConnections(m_logicalRoads[road]) >= 3) continue;
                List<CardinalDirection> sides = GetOpenRoadSides(road);
                if (sides.Count == 0) continue;
                Vector3 outward = DirectionVector(sides[random.Next(sides.Count)]);
                Vector3 position = GridToWorld(road) + outward * (m_roadTileSize * 0.5f + m_sidewalkWidth + 1.1f);
                GameObject prefab = random.Next(0, 5) switch
                {
                    0 => prefabs.FireHydrant,
                    1 => prefabs.StreetSign,
                    2 => prefabs.UtilityBox,
                    3 => prefabs.BusStop,
                    _ => prefabs.PlantingBed
                };
                if (TryPlaceEnvironmentPrefab(prefab, position, Quaternion.LookRotation(-outward),
                        m_roadsideDecorationsRoot, true)) spawned++;
            }
        }

        private bool TryPlaceEnvironmentPrefab(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            bool reserve) => TryPlaceEnvironmentPrefab(prefab, position, rotation, parent, reserve, out _);

        private bool TryPlaceEnvironmentPrefab(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            bool reserve,
            out GameObject instance)
        {
            instance = null;
            if (prefab == null || parent == null) return false;
            EnvironmentPlacementMetadata metadata = prefab.GetComponent<EnvironmentPlacementMetadata>();
            Vector2 size = metadata != null ? metadata.Footprint : Vector2.one;
            float clearance = metadata != null ? metadata.Clearance : 0.25f;
            Vector3 forward3 = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up).normalized;
            if (forward3.sqrMagnitude < 0.1f) forward3 = Vector3.forward;
            Vector3 right3 = Vector3.Cross(Vector3.up, forward3).normalized;
            OrientedFootprint footprint = new OrientedFootprint(
                new Vector2(position.x, position.z),
                new Vector2(right3.x, right3.z),
                new Vector2(forward3.x, forward3.z),
                size * 0.5f + Vector2.one * clearance);
            if (reserve && !CanPlaceEnvironmentFootprint(footprint)) return false;
            position.y = SamplePlacementTerrainHeight(position);
            instance = Instantiate(prefab, position, rotation, parent);
            if (reserve && (metadata == null || metadata.BlocksPlacement)) m_environmentFootprints.Add(footprint);
            return true;
        }

        private bool CanPlaceEnvironmentFootprint(OrientedFootprint candidate)
        {
            for (int i = 0; i < m_environmentFootprints.Count; i++)
                if (FootprintsOverlap(candidate, m_environmentFootprints[i], 0f)) return false;
            for (int i = 0; i < m_reservedDrivewayFootprints.Count; i++)
                if (FootprintsOverlap(candidate, m_reservedDrivewayFootprints[i], 0.25f)) return false;
            for (int i = 0; i < m_placedHouseFootprints.Count; i++)
                if (FootprintsOverlap(candidate, m_placedHouseFootprints[i], 0.15f)) return false;
            return true;
        }

        private void GroundStreetlightToGeneratedTerrain(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            Vector3 position = instance.transform.position;
            EnvironmentPlacementMetadata metadata =
                instance.GetComponent<EnvironmentPlacementMetadata>();
            Vector2 footprint = metadata != null ? metadata.Footprint : Vector2.one;
            Vector3 right = instance.transform.right;
            Vector3 forward = instance.transform.forward;
            float groundHeight = float.PositiveInfinity;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 sample = position + right * (x * footprint.x * 0.5f) +
                                     forward * (z * footprint.y * 0.5f);
                    if (TrySampleGeneratedTerrain(sample, out RaycastHit hit))
                    {
                        groundHeight = Mathf.Min(groundHeight, hit.point.y);
                    }
                }
            }

            if (TrySampleGeneratedTerrain(position, out RaycastHit centreHit))
                groundHeight = Mathf.Min(groundHeight, centreHit.point.y);
            if (float.IsPositiveInfinity(groundHeight))
                groundHeight = SamplePlacementTerrainHeight(position);

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (TryCalculateRendererBounds(renderers, out Bounds renderedBounds))
            {
                // Ground the visible model rather than assuming a replacement prefab's
                // root pivot is exactly at the bottom of its pole.
                position.y += groundHeight - renderedBounds.min.y;
            }
            else
            {
                position.y = groundHeight;
            }

            instance.transform.position = position;
        }

        private bool TrySampleGeneratedTerrain(Vector3 position, out RaycastHit hit)
        {
            hit = default;
            if (m_generatedTerrainCollider == null)
            {
                return false;
            }

            Bounds bounds = m_generatedTerrainCollider.bounds;
            Ray ray = new Ray(
                new Vector3(position.x, bounds.max.y + 2f, position.z),
                Vector3.down);
            return m_generatedTerrainCollider.Raycast(ray, out hit, bounds.size.y + 4f);
        }

        private bool IsNearDriveway(Vector3 position, float clearance)
        {
            Vector2 point = new Vector2(position.x, position.z);
            for (int i = 0; i < m_reservedDrivewayFootprints.Count; i++)
            {
                OrientedFootprint footprint = m_reservedDrivewayFootprints[i];
                Vector2 delta = point - footprint.Centre;
                if (Mathf.Abs(Vector2.Dot(delta, footprint.Right)) <= footprint.HalfSize.x + clearance &&
                    Mathf.Abs(Vector2.Dot(delta, footprint.Forward)) <= footprint.HalfSize.y + clearance) return true;
            }
            return false;
        }

        private void SnapToTerrain(Transform target, float offset)
        {
            Vector3 position = target.position;
            position.y = SamplePlacementTerrainHeight(position) + offset;
            target.position = position;
        }

        private static Quaternion RandomYaw(System.Random random) =>
            Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);

        private static float RandomRange(System.Random random, float minimum, float maximum) =>
            Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
    }
}
