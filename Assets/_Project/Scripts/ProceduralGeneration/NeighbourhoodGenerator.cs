using System;
using System.Collections.Generic;
using System.Diagnostics;
using CouchGuys.Gameplay.Delivery;
using Unity.AI.Navigation;
using UnityEngine.AI;
using UnityEngine;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Builds a deterministic logical neighbourhood, then represents it with assigned
    /// modular prefabs or lightweight development placeholders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodGenerator : MonoBehaviour
    {
        public event Action<NeighbourhoodGenerator> RegionGenerated;
        public event Action<NeighbourhoodGenerator> RegionClearing;

        [Header("Generation")]
        [SerializeField] private bool m_generateOnStart = true;
        [SerializeField] private bool m_useRandomSeed;
        [SerializeField] private int m_seed = 12345;

        [Header("Region")]
        [Tooltip("The selected region. Leave empty to use the legacy Suburbs fields below.")]
        [SerializeField] private RegionDefinition m_selectedRegion;
        [Tooltip("Stable order used when synchronising region selection in multiplayer.")]
        [SerializeField] private RegionDefinition[] m_availableRegions = Array.Empty<RegionDefinition>();

        [Header("Grid")]
        [SerializeField, Min(1)] private int m_gridWidth = 4;
        [SerializeField, Min(1)] private int m_gridHeight = 4;
        [SerializeField, Min(1)] private int m_blockWidth = 4;
        [SerializeField, Min(1)] private int m_blockHeight = 4;
        [FormerlySerializedAs("m_tileSize")]
        [SerializeField, Min(0.1f)] private float m_roadTileSize = 10f;

        [Header("Roads")]
        [SerializeField] private GameObject[] m_roadPrefabs = Array.Empty<GameObject>();

        [Header("Residential Ground")]
        [Tooltip("One instance is fitted to the complete non-road area inside each residential block.")]
        [SerializeField] private GameObject m_residentialGroundPrefab;
        [Tooltip("Lowers residential ground slightly below the generated block elevation to avoid z-fighting.")]
        [SerializeField, Min(0f)] private float m_residentialGroundVerticalOffset = 0.02f;

        [Header("Houses")]
        [SerializeField] private GameObject[] m_housePrefabs = Array.Empty<GameObject>();
        [SerializeField, Min(0)] private int m_housesPerBlock = 6;
        [SerializeField, Min(0)] private int m_minimumHouseSpacing = 1;
        [SerializeField] private Vector2Int m_standardPropertyFootprint = Vector2Int.one;
        [SerializeField] private Vector3 m_placeholderHouseSize = new Vector3(10f, 6f, 10f);
        [Tooltip("Uniform house visual size relative to its generated property footprint. Values above 1 allow a small, intentional overhang beyond the grid cell.")]
        [SerializeField, Range(0.1f, 1.5f)] private float m_houseFootprintFill = 1.15f;

        [Header("Elevation")]
        [SerializeField] private bool m_elevationEnabled = true;
        [Tooltip("Minimum seeded height difference between elevated neighbourhood sections.")]
        [SerializeField, Min(0.1f)] private float m_minimumElevationStep = 2f;
        [Tooltip("Maximum seeded height difference between elevated neighbourhood sections.")]
        [SerializeField, Min(0.1f)] private float m_elevationStep = 8f;
        [SerializeField, Min(0f)] private float m_maximumElevation = 24f;
        [SerializeField, Range(1f, 60f)] private float m_maximumRoadSlope = 45f;

        [Header("Starting Area")]
        [SerializeField] private GameObject m_startingAreaPrefab;
        [SerializeField] private GameObject m_deliveryNpcPrefab;
        [SerializeField] private Vector2Int m_placeholderStartingAreaFootprint = new Vector2Int(3, 3);

        [Header("Navigation")]
        [Tooltip("Built once after every generated object exists. Optional while no AI navigation is present.")]
        [SerializeField] private NavMeshSurface m_navMeshSurface;

        [Header("Debug")]
        [SerializeField] private bool m_showGrid;
        [SerializeField] private bool m_showConnections = true;
        [SerializeField] private bool m_showOccupiedCells;

        private readonly List<GeneratedRoad> m_generatedRoads = new List<GeneratedRoad>();
        private readonly List<GeneratedProperty> m_generatedProperties = new List<GeneratedProperty>();
        private readonly List<GeneratedLot> m_generatedLots = new List<GeneratedLot>();
        private readonly List<DeliveryDestination> m_deliveryDestinations = new List<DeliveryDestination>();
        private readonly HashSet<GridCoordinate> m_roadCells = new HashSet<GridCoordinate>();
        private readonly HashSet<GridCoordinate> m_propertyCells = new HashSet<GridCoordinate>();
        private readonly HashSet<GridCoordinate> m_startingAreaCells = new HashSet<GridCoordinate>();
        private readonly HashSet<LotDefinition> m_spawnedUniqueLots = new HashSet<LotDefinition>();
        private readonly List<GridCoordinate> m_roadOrder = new List<GridCoordinate>();
        private readonly Dictionary<GridCoordinate, RoadConnections> m_logicalRoads =
            new Dictionary<GridCoordinate, RoadConnections>();
        private readonly Dictionary<GridCoordinate, float> m_roadHeights =
            new Dictionary<GridCoordinate, float>();

        private float[,] m_blockElevations;
        private int m_minimumLocalX;

        private Transform m_generatedRoot;
        private Transform m_roadsRoot;
        private Transform m_residentialGroundRoot;
        private Transform m_housesRoot;
        private Transform m_landmarksRoot;
        private Transform m_propsRoot;
        private Transform m_hazardsRoot;
        private Vector3 m_gridOrigin;
        private CardinalDirection m_startDirection;
        private bool m_warnedAboutRoadPlaceholders;
        private bool m_warnedAboutHousePlaceholders;

        private readonly struct PropertyCandidate
        {
            public readonly GridCoordinate Anchor;
            public readonly GridCoordinate Road;
            public readonly CardinalDirection PropertyToRoad;

            public PropertyCandidate(
                GridCoordinate anchor,
                GridCoordinate road,
                CardinalDirection propertyToRoad)
            {
                Anchor = anchor;
                Road = road;
                PropertyToRoad = propertyToRoad;
            }
        }

        public StartingArea GeneratedStartingArea { get; private set; }
        public IReadOnlyList<GeneratedRoad> GeneratedRoads => m_generatedRoads;
        public IReadOnlyList<GeneratedProperty> GeneratedProperties => m_generatedProperties;
        public IReadOnlyList<GeneratedLot> GeneratedLots => m_generatedLots;
        public IReadOnlyList<DeliveryDestination> DeliveryDestinations => m_deliveryDestinations;
        public RegionDefinition SelectedRegion => m_selectedRegion;
        public string CurrentRegionId => m_selectedRegion != null ? m_selectedRegion.RegionId : "suburbs";
        public string CurrentRegionName => m_selectedRegion != null ? m_selectedRegion.DisplayName : "Suburbs (Legacy)";
        public int SelectedRegionIndex => FindRegionIndex(m_selectedRegion);
        public GameObject DeliveryNpcPrefab => m_deliveryNpcPrefab;
        public int CurrentSeed { get; private set; }
        public int GeneratedLandmarkCount { get; private set; }
        public int GeneratedPropCount { get; private set; }
        public int GeneratedHazardCount { get; private set; }
        public double LastGenerationMilliseconds { get; private set; }
        public float TileSize => m_roadTileSize;
        public bool IsGenerationReady { get; private set; }
        public bool IsNavMeshReady { get; private set; }
        public bool HasGeneratedNeighbourhood =>
            IsGenerationReady && GeneratedStartingArea != null && m_generatedRoads.Count > 0;
        public Bounds GeneratedWorldBounds { get; private set; }

        private void Start()
        {
            if (m_generateOnStart && GetComponent<NeighbourhoodSeedSynchroniser>() == null)
            {
                Generate();
            }
        }

        [ContextMenu("Generate Neighbourhood")]
        public void Generate()
        {
            Generate(SelectSeed());
        }

        public void Generate(int seed)
        {
            Generate(m_selectedRegion, seed);
        }

        public void Generate(RegionDefinition region, int seed)
        {
            ApplyRegionConfiguration(region);
            if (!ValidateConfiguration())
            {
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Clear();
            CurrentSeed = seed;
            System.Random layoutRandom = new System.Random(seed);
            CreateGeneratedHierarchy();
            GeneratedStartingArea = CreateStartingArea();
            if (GeneratedStartingArea == null || GeneratedStartingArea.RoadConnection == null)
            {
                Debug.LogError("Neighbourhood generation stopped because no valid Starting Area RoadConnection could be created.", this);
                Clear();
                return;
            }

            m_gridOrigin = GeneratedStartingArea.RoadConnection.position;
            m_startDirection = ClosestDirection(GeneratedStartingArea.RoadConnection.forward);
            ReserveStartingArea(GeneratedStartingArea);
            AssignBlockElevations(layoutRandom);
            GenerateRoadLayout(new System.Random(DeriveSeed(seed, 0x1874A2B1)));
            CalculateRoadConnections();
            // Visual selection has its own stream so adding art cannot perturb the
            // logical road/property decisions made for a given seed.
            CreateRoadVisuals(new System.Random(DeriveSeed(seed, 0x2D31A7B5)));
            CreateResidentialGround();
            GenerateLandmarks(new System.Random(DeriveSeed(seed, 0x4F1BBCDC)));
            GenerateProperties(new System.Random(DeriveSeed(seed, 0x61C88647)));
            GenerateRegionObjects(
                m_selectedRegion != null ? m_selectedRegion.Props : null,
                m_propsRoot,
                new System.Random(DeriveSeed(seed, 0x15342E19)),
                false);
            GenerateRegionObjects(
                m_selectedRegion != null ? m_selectedRegion.Hazards : null,
                m_hazardsRoot,
                new System.Random(DeriveSeed(seed, 0x72AE91C3)),
                true);
            CreateDeliveryNpc();
            CalculateWorldBounds();
            ValidateLayout();
            BuildNavMeshOnce();
            IsGenerationReady = true;
            stopwatch.Stop();
            LastGenerationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            Debug.Log(
                $"Generated region '{CurrentRegionName}' with seed {CurrentSeed}: " +
                $"{m_generatedRoads.Count} roads, {m_generatedLots.Count} lots, " +
                $"{m_deliveryDestinations.Count} destinations, {GeneratedLandmarkCount} landmarks, " +
                $"{GeneratedHazardCount} hazards in {LastGenerationMilliseconds:F1} ms.",
                this);
            RegionGenerated?.Invoke(this);
        }

        public bool TrySelectRegion(int regionIndex)
        {
            if (regionIndex == -1)
            {
                m_selectedRegion = null;
                return true;
            }

            if (m_availableRegions == null || regionIndex < 0 || regionIndex >= m_availableRegions.Length ||
                m_availableRegions[regionIndex] == null)
            {
                return false;
            }

            m_selectedRegion = m_availableRegions[regionIndex];
            return true;
        }

        public bool TrySelectRegion(string regionId)
        {
            if (string.IsNullOrWhiteSpace(regionId))
            {
                return false;
            }

            for (int index = 0; index < m_availableRegions.Length; index++)
            {
                RegionDefinition region = m_availableRegions[index];
                if (region != null && string.Equals(
                        region.RegionId,
                        regionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    m_selectedRegion = region;
                    return true;
                }
            }

            return false;
        }

        public int SelectSeed()
        {
            return m_useRandomSeed
                ? unchecked(Environment.TickCount ^ Guid.NewGuid().GetHashCode())
                : m_seed;
        }

        private void ApplyRegionConfiguration(RegionDefinition region)
        {
            m_selectedRegion = region;
            if (region == null)
            {
                return;
            }

            RegionGridSettings grid = region.Grid;
            RegionElevationSettings elevation = region.Elevation;
            m_gridWidth = grid.GridWidth;
            m_gridHeight = grid.GridHeight;
            m_blockWidth = grid.BlockWidth;
            m_blockHeight = grid.BlockHeight;
            m_roadTileSize = grid.RoadTileSize;
            m_housesPerBlock = grid.LotsPerBlock;
            m_minimumHouseSpacing = grid.MinimumLotSpacing;
            m_elevationEnabled = elevation.Enabled;
            m_minimumElevationStep = elevation.MinimumStep;
            m_elevationStep = elevation.MaximumStep;
            m_maximumElevation = elevation.MaximumElevation;
            m_maximumRoadSlope = elevation.MaximumRoadSlope;
            m_roadPrefabs = region.RoadPrefabs ?? Array.Empty<GameObject>();
            m_residentialGroundPrefab = region.GroundPrefab;
            m_startingAreaPrefab = region.StartingAreaPrefab;
            m_deliveryNpcPrefab = region.DeliveryNpcPrefab;
        }

        private int FindRegionIndex(RegionDefinition region)
        {
            if (region == null || m_availableRegions == null)
            {
                return -1;
            }

            for (int index = 0; index < m_availableRegions.Length; index++)
            {
                if (m_availableRegions[index] == region)
                {
                    return index;
                }
            }

            return -1;
        }

        public void SetDeliveryNpcPrefab(GameObject deliveryNpcPrefab)
        {
            m_deliveryNpcPrefab = deliveryNpcPrefab;
        }

        public bool TrySetDefaultHousePrefabs(GameObject[] housePrefabs)
        {
            if (m_housePrefabs != null)
            {
                for (int index = 0; index < m_housePrefabs.Length; index++)
                {
                    if (m_housePrefabs[index] != null)
                    {
                        return false;
                    }
                }
            }

            m_housePrefabs = housePrefabs ?? Array.Empty<GameObject>();
            return m_housePrefabs.Length > 0;
        }

        [ContextMenu("Clear Generated Neighbourhood")]
        public void Clear()
        {
            if (IsGenerationReady || m_generatedRoot != null)
            {
                RegionClearing?.Invoke(this);
            }

            if (m_navMeshSurface != null && m_navMeshSurface.navMeshData != null)
            {
                m_navMeshSurface.RemoveData();
            }

            GeneratedNeighbourhoodRoot existingRoot = GetComponentInChildren<GeneratedNeighbourhoodRoot>(true);
            if (existingRoot != null && existingRoot.transform.parent == transform)
            {
                if (Application.isPlaying)
                {
                    Destroy(existingRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(existingRoot.gameObject);
                }
            }

            m_generatedRoot = null;
            m_roadsRoot = null;
            m_residentialGroundRoot = null;
            m_housesRoot = null;
            m_landmarksRoot = null;
            m_propsRoot = null;
            m_hazardsRoot = null;
            GeneratedStartingArea = null;
            IsGenerationReady = false;
            IsNavMeshReady = false;
            m_generatedRoads.Clear();
            m_generatedProperties.Clear();
            m_generatedLots.Clear();
            m_deliveryDestinations.Clear();
            m_roadCells.Clear();
            m_propertyCells.Clear();
            m_startingAreaCells.Clear();
            m_spawnedUniqueLots.Clear();
            m_roadOrder.Clear();
            m_logicalRoads.Clear();
            m_roadHeights.Clear();
            m_blockElevations = null;
            GeneratedWorldBounds = new Bounds(transform.position, Vector3.zero);
            m_warnedAboutRoadPlaceholders = false;
            m_warnedAboutHousePlaceholders = false;
            GeneratedLandmarkCount = 0;
            GeneratedPropCount = 0;
            GeneratedHazardCount = 0;
        }

        public Vector3 GridToWorld(GridCoordinate coordinate)
        {
            float height = GetCellHeight(coordinate);
            return m_gridOrigin + new Vector3(
                coordinate.X * m_roadTileSize,
                height,
                coordinate.Y * m_roadTileSize);
        }

        private float GetCellHeight(GridCoordinate coordinate)
        {
            if (m_roadHeights.TryGetValue(coordinate, out float roadHeight))
            {
                return roadHeight;
            }

            if (m_blockElevations == null)
            {
                return 0f;
            }

            Vector2Int local = GridToLocal(coordinate);
            int horizontalSpan = m_blockWidth + 1;
            int verticalSpan = m_blockHeight + 1;
            int relativeX = local.x - m_minimumLocalX;
            int blockX = Mathf.FloorToInt(relativeX / (float)horizontalSpan);
            int blockY = Mathf.FloorToInt(local.y / (float)verticalSpan);
            return blockX >= 0 && blockX < m_gridWidth && blockY >= 0 && blockY < m_gridHeight
                ? m_blockElevations[blockX, blockY]
                : 0f;
        }

        public GridCoordinate WorldToGrid(Vector3 worldPosition)
        {
            Vector3 offset = worldPosition - m_gridOrigin;
            return new GridCoordinate(
                Mathf.RoundToInt(offset.x / m_roadTileSize),
                Mathf.RoundToInt(offset.z / m_roadTileSize));
        }

        private bool ValidateConfiguration()
        {
            if (m_roadTileSize <= 0f || m_gridWidth <= 0 || m_gridHeight <= 0 ||
                m_blockWidth <= 0 || m_blockHeight <= 0)
            {
                Debug.LogError("Neighbourhood Generator grid sizes and Road Tile Size must be greater than zero.", this);
                return false;
            }

            if (m_minimumElevationStep <= 0f || m_elevationStep <= 0f ||
                m_minimumElevationStep > m_elevationStep ||
                m_maximumElevation < 0f || m_maximumRoadSlope <= 0f)
            {
                Debug.LogError("Neighbourhood Generator elevation settings are invalid.", this);
                return false;
            }

            if (m_standardPropertyFootprint.x <= 0 || m_standardPropertyFootprint.y <= 0 ||
                m_placeholderStartingAreaFootprint.x <= 0 || m_placeholderStartingAreaFootprint.y <= 0)
            {
                Debug.LogError("Neighbourhood Generator footprints must contain at least one grid cell.", this);
                return false;
            }

            return true;
        }

        private void CreateGeneratedHierarchy()
        {
            GameObject root = new GameObject("ProceduralNeighbourhood");
            root.transform.SetParent(transform, false);
            root.AddComponent<GeneratedNeighbourhoodRoot>();
            m_generatedRoot = root.transform;

            m_roadsRoot = new GameObject("Roads").transform;
            m_roadsRoot.SetParent(m_generatedRoot, false);
            m_residentialGroundRoot = new GameObject("ResidentialGround").transform;
            m_residentialGroundRoot.SetParent(m_generatedRoot, false);
            m_housesRoot = new GameObject("Houses").transform;
            m_housesRoot.SetParent(m_generatedRoot, false);
            m_landmarksRoot = new GameObject("Landmarks").transform;
            m_landmarksRoot.SetParent(m_generatedRoot, false);
            m_propsRoot = new GameObject("Props").transform;
            m_propsRoot.SetParent(m_generatedRoot, false);
            m_hazardsRoot = new GameObject("Hazards").transform;
            m_hazardsRoot.SetParent(m_generatedRoot, false);
        }

        private StartingArea CreateStartingArea()
        {
            if (m_startingAreaPrefab != null)
            {
                StartingArea definition = m_startingAreaPrefab.GetComponent<StartingArea>();
                if (definition != null && definition.RoadConnection != null)
                {
                    GameObject instance = Instantiate(m_startingAreaPrefab, transform.position, transform.rotation, m_generatedRoot);
                    instance.name = "StartingArea";
                    return instance.GetComponent<StartingArea>();
                }

                Debug.LogWarning("The assigned Starting Area prefab is missing a StartingArea component or RoadConnection; using the placeholder.", this);
            }
            else
            {
                Debug.LogWarning("No Starting Area prefab is assigned; using the placeholder Starting Area.", this);
            }

            GameObject root = new GameObject("StartingArea");
            root.transform.SetPositionAndRotation(transform.position, transform.rotation);
            root.transform.SetParent(m_generatedRoot, true);

            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Plane);
            placeholder.name = "Placeholder_StartingArea";
            placeholder.transform.SetParent(root.transform, false);
            placeholder.transform.localPosition = Vector3.zero;
            placeholder.transform.localScale = new Vector3(
                m_placeholderStartingAreaFootprint.x * m_roadTileSize / 10f,
                1f,
                m_placeholderStartingAreaFootprint.y * m_roadTileSize / 10f);

            Transform roadConnection = new GameObject("RoadConnection").transform;
            roadConnection.SetParent(root.transform, false);
            roadConnection.localPosition = new Vector3(
                0f,
                0f,
                (m_placeholderStartingAreaFootprint.y * 0.5f + 0.5f) * m_roadTileSize);
            roadConnection.localRotation = Quaternion.identity;

            Transform[] spawnPoints = new Transform[4];
            Transform spawnRoot = new GameObject("PlayerSpawnPoints").transform;
            spawnRoot.SetParent(root.transform, false);
            Vector3[] offsets =
            {
                new Vector3(-2f, 0.1f, -2f), new Vector3(2f, 0.1f, -2f),
                new Vector3(-2f, 0.1f, 2f), new Vector3(2f, 0.1f, 2f)
            };
            for (int index = 0; index < spawnPoints.Length; index++)
            {
                Transform point = new GameObject($"PlayerSpawnPoint{index + 1:00}").transform;
                point.SetParent(spawnRoot, false);
                point.localPosition = offsets[index];
                spawnPoints[index] = point;
            }

            Transform npcSpawnPoint = new GameObject("DeliveryNpcSpawnPoint").transform;
            npcSpawnPoint.SetParent(root.transform, false);
            npcSpawnPoint.localPosition = new Vector3(0f, 0f, 1f);
            npcSpawnPoint.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            StartingArea startingArea = root.AddComponent<StartingArea>();
            startingArea.Initialise(
                roadConnection,
                spawnPoints,
                npcSpawnPoint,
                m_placeholderStartingAreaFootprint);
            return startingArea;
        }

        private void CreateDeliveryNpc()
        {
            if (GeneratedStartingArea == null)
            {
                return;
            }

            Transform spawnPoint = GeneratedStartingArea.DeliveryNpcSpawnPoint;
            if (spawnPoint == null)
            {
                spawnPoint = new GameObject("DeliveryNpcSpawnPoint").transform;
                spawnPoint.SetParent(GeneratedStartingArea.transform, false);
                spawnPoint.localPosition = new Vector3(0f, 0f, 1f);
                spawnPoint.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                GeneratedStartingArea.SetDeliveryNpcSpawnPoint(spawnPoint);
            }

            GameObject npc;
            if (m_deliveryNpcPrefab != null)
            {
                npc = Instantiate(m_deliveryNpcPrefab, spawnPoint.position, spawnPoint.rotation, spawnPoint);
            }
            else
            {
                npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                npc.transform.SetPositionAndRotation(spawnPoint.position + Vector3.up, spawnPoint.rotation);
                npc.transform.SetParent(spawnPoint, true);
            }

            npc.name = m_deliveryNpcPrefab != null ? "DeliveryNPC" : "Placeholder_DeliveryNPC";
            DeliveryNPC deliveryNpc = npc.GetComponent<DeliveryNPC>();
            if (deliveryNpc == null)
            {
                deliveryNpc = npc.AddComponent<DeliveryNPC>();
            }

            deliveryNpc.SetDeliveryManager(GetComponent<DeliveryManager>());
        }

        private void CalculateWorldBounds()
        {
            bool hasPoint = false;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            for (int index = 0; index < m_generatedRoads.Count; index++)
            {
                EncapsulatePoint(ref bounds, ref hasPoint, m_generatedRoads[index].transform.position);
            }

            for (int index = 0; index < m_generatedProperties.Count; index++)
            {
                GeneratedProperty property = m_generatedProperties[index];
                EncapsulatePoint(ref bounds, ref hasPoint, property.transform.position);
                if (property.DeliveryPoint != null)
                {
                    EncapsulatePoint(ref bounds, ref hasPoint, property.DeliveryPoint.position);
                }
            }

            if (GeneratedStartingArea != null)
            {
                EncapsulatePoint(ref bounds, ref hasPoint, GeneratedStartingArea.transform.position);
            }

            bounds.Expand(new Vector3(m_roadTileSize, 0f, m_roadTileSize));
            GeneratedWorldBounds = bounds;
        }

        private static void EncapsulatePoint(ref Bounds bounds, ref bool hasPoint, Vector3 point)
        {
            if (!hasPoint)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasPoint = true;
            }
            else
            {
                bounds.Encapsulate(point);
            }
        }

        private void ReserveStartingArea(StartingArea startingArea)
        {
            Vector2Int footprint = startingArea.GridFootprint;
            int minimumX = -(footprint.x / 2);
            int minimumY = -(footprint.y / 2);
            for (int x = 0; x < footprint.x; x++)
            {
                for (int y = 0; y < footprint.y; y++)
                {
                    Vector3 worldPosition = startingArea.transform.TransformPoint(new Vector3(
                        (minimumX + x) * m_roadTileSize,
                        0f,
                        (minimumY + y) * m_roadTileSize));
                    m_startingAreaCells.Add(WorldToGrid(worldPosition));
                }
            }

            // The explicit connection is the one intentional entry into the exclusion zone.
            m_startingAreaCells.Remove(GridCoordinate.Zero);
        }

        private void AssignBlockElevations(System.Random random)
        {
            m_blockElevations = new float[m_gridWidth, m_gridHeight];
            if (!m_elevationEnabled || m_gridHeight < 2)
            {
                return;
            }

            float maximumRise = Mathf.Tan(m_maximumRoadSlope * Mathf.Deg2Rad) * m_roadTileSize;
            float maximumStep = Mathf.Min(m_elevationStep, maximumRise, m_maximumElevation);
            float minimumStep = Mathf.Min(m_minimumElevationStep, maximumStep);
            float elevationStep = Mathf.Round(
                Mathf.Lerp(minimumStep, maximumStep, (float)random.NextDouble()) * 2f) * 0.5f;
            elevationStep = Mathf.Max(0.1f, elevationStep);
            int maximumLevel = Mathf.FloorToInt(m_maximumElevation / elevationStep);
            if (maximumLevel <= 0)
            {
                Debug.LogWarning(
                    "Elevation is enabled, but Maximum Elevation does not allow one elevation step. " +
                    "The generated grid will remain flat.", this);
                return;
            }

            int maximumLevelChange = Mathf.Max(1, Mathf.FloorToInt(maximumRise / elevationStep));
            int[] rowLevels = new int[m_gridHeight];
            bool hasElevation = false;
            for (int row = 1; row < m_gridHeight; row++)
            {
                int previousLevel = rowLevels[row - 1];
                int nextLevel = previousLevel;
                if (random.Next(100) < 75)
                {
                    bool moveUp = previousLevel <= 0 ||
                                  (previousLevel < maximumLevel && random.Next(100) < 60);
                    int availableLevels = moveUp
                        ? maximumLevel - previousLevel
                        : previousLevel;
                    int changeLimit = Mathf.Min(maximumLevelChange, availableLevels);
                    if (changeLimit > 0)
                    {
                        int change = random.Next(1, changeLimit + 1);
                        nextLevel += moveUp ? change : -change;
                    }
                }

                rowLevels[row] = nextLevel;
                hasElevation |= nextLevel > 0;
            }

            if (!hasElevation)
            {
                int raisedRow = random.Next(1, m_gridHeight);
                for (int row = raisedRow; row < m_gridHeight; row++)
                {
                    rowLevels[row] = 1;
                }
            }

            for (int row = 0; row < m_gridHeight; row++)
            {
                float elevation = rowLevels[row] * elevationStep;
                for (int column = 0; column < m_gridWidth; column++)
                {
                    m_blockElevations[column, row] = elevation;
                }
            }
        }

        private void GenerateRoadLayout(System.Random random)
        {
            if (m_selectedRegion != null && m_selectedRegion.RoadStrategy != null)
            {
                RegionGridSettings grid = m_selectedRegion.Grid;
                m_minimumLocalX = -(grid.GridWidth / 2) * (grid.BlockWidth + 1);
                RegionRoadGenerationContext context = new RegionRoadGenerationContext(
                    grid,
                    (localX, localY) => AddRoad(
                        LocalToGrid(localX, localY),
                        RoadHeightAtLocal(localY)));
                m_selectedRegion.RoadStrategy.Generate(context, random);
                return;
            }

            // Keep one boundary aligned with the Starting Area connection, then build
            // the complete rectangular road lattice forward from it.
            m_minimumLocalX = -(m_gridWidth / 2) * (m_blockWidth + 1);
            int maximumLocalX = m_minimumLocalX + m_gridWidth * (m_blockWidth + 1);
            int maximumLocalY = m_gridHeight * (m_blockHeight + 1);

            for (int boundary = 0; boundary <= m_gridWidth; boundary++)
            {
                int localX = m_minimumLocalX + boundary * (m_blockWidth + 1);
                for (int localY = 0; localY <= maximumLocalY; localY++)
                {
                    AddRoad(LocalToGrid(localX, localY), RoadHeightAtLocal(localY));
                }
            }

            for (int boundary = 0; boundary <= m_gridHeight; boundary++)
            {
                int localY = boundary * (m_blockHeight + 1);
                for (int localX = m_minimumLocalX; localX <= maximumLocalX; localX++)
                {
                    AddRoad(LocalToGrid(localX, localY), RoadHeightAtLocal(localY));
                }
            }
        }

        private float RoadHeightAtLocal(int localY)
        {
            if (m_blockElevations == null || m_gridHeight == 0)
            {
                return 0f;
            }

            int span = m_blockHeight + 1;
            int boundary = localY / span;
            if (localY % span == 0 && boundary > 0 && boundary < m_gridHeight)
            {
                return (m_blockElevations[0, boundary - 1] +
                        m_blockElevations[0, boundary]) * 0.5f;
            }

            int row = Mathf.Clamp(boundary, 0, m_gridHeight - 1);
            return m_blockElevations[0, row];
        }

        private GridCoordinate LocalToGrid(int localX, int localY)
        {
            GridCoordinate right = DirectionOffset(TurnRight(m_startDirection));
            GridCoordinate forward = DirectionOffset(m_startDirection);
            return Multiply(right, localX) + Multiply(forward, localY);
        }

        private void AddRoad(GridCoordinate coordinate, float height)
        {
            if (m_startingAreaCells.Contains(coordinate) && coordinate != GridCoordinate.Zero)
            {
                return;
            }

            if (m_roadCells.Add(coordinate))
            {
                m_roadOrder.Add(coordinate);
                m_roadHeights.Add(coordinate, height);
            }
        }

        private void CalculateRoadConnections()
        {
            for (int index = 0; index < m_roadOrder.Count; index++)
            {
                GridCoordinate coordinate = m_roadOrder[index];
                RoadConnections connections = RoadConnections.None;
                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    CardinalDirection direction = (CardinalDirection)directionIndex;
                    if (m_roadCells.Contains(coordinate + DirectionOffset(direction)))
                    {
                        connections |= DirectionConnection(direction);
                    }
                }

                if (coordinate == GridCoordinate.Zero)
                {
                    connections |= DirectionConnection(Opposite(m_startDirection));
                }

                m_logicalRoads.Add(coordinate, connections);
            }
        }

        private void CreateRoadVisuals(System.Random random)
        {
            for (int index = 0; index < m_roadOrder.Count; index++)
            {
                GridCoordinate coordinate = m_roadOrder[index];
                RoadConnections connections = m_logicalRoads[coordinate];
                Quaternion surfaceRotation = CalculateRoadSurfaceRotation(coordinate);
                GameObject road = TryCreateRoadPrefab(
                    connections, GridToWorld(coordinate), surfaceRotation, random);
                if (road == null)
                {
                    road = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    road.transform.SetPositionAndRotation(GridToWorld(coordinate), surfaceRotation);
                    road.transform.localScale = new Vector3(
                        m_roadTileSize / 10f, 1f, m_roadTileSize / 10f);
                    if (!m_warnedAboutRoadPlaceholders)
                    {
                        Debug.LogWarning("One or more matching Road prefabs are unavailable; Plane placeholders are being used.", this);
                        m_warnedAboutRoadPlaceholders = true;
                    }
                }

                road.name = road.GetComponent<RoadTile>() == null
                    ? $"Placeholder_Road_{index:000}"
                    : $"Road_{index:000}";
                road.transform.SetParent(m_roadsRoot, true);
                FitRoadVisualToTile(road.transform);
                GeneratedRoad generatedRoad = road.AddComponent<GeneratedRoad>();
                generatedRoad.Initialise(coordinate, connections);
                m_generatedRoads.Add(generatedRoad);
            }
        }

        private void FitRoadVisualToTile(Transform road)
        {
            Renderer[] renderers = road.GetComponentsInChildren<Renderer>(true);
            if (!TryCalculateRendererBounds(renderers, out Bounds bounds))
            {
                return;
            }

            Vector3 localXDirection = Vector3.ProjectOnPlane(road.right, Vector3.up).normalized;
            Vector3 localZDirection = Vector3.ProjectOnPlane(road.forward, Vector3.up).normalized;
            float renderedX = ProjectBoundsSizeOntoAxis(bounds.size, localXDirection);
            float renderedZ = ProjectBoundsSizeOntoAxis(bounds.size, localZDirection);
            if (renderedX <= 0.001f || renderedZ <= 0.001f)
            {
                return;
            }

            // Stretch the inclined axis by the slope's secant so every road still has
            // a full tile-sized horizontal footprint. Adjacent tiles then meet at their
            // edges instead of shrinking into gaps or intersecting at the grade.
            Vector3 scale = road.localScale;
            scale.x *= m_roadTileSize / renderedX;
            scale.z *= m_roadTileSize / renderedZ;
            road.localScale = scale;
        }

        private GameObject TryCreateRoadPrefab(
            RoadConnections requiredConnections,
            Vector3 position,
            Quaternion surfaceRotation,
            System.Random random)
        {
            if (m_roadPrefabs == null || m_roadPrefabs.Length == 0)
            {
                return null;
            }

            int firstPrefab = random.Next(0, m_roadPrefabs.Length);
            for (int prefabOffset = 0; prefabOffset < m_roadPrefabs.Length; prefabOffset++)
            {
                GameObject prefab = m_roadPrefabs[(firstPrefab + prefabOffset) % m_roadPrefabs.Length];
                if (prefab == null || !prefab.TryGetComponent(out RoadTile roadTile))
                {
                    continue;
                }

                for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
                {
                    if (RoadTile.RotateClockwise(roadTile.Connections, quarterTurns) == requiredConnections)
                    {
                        return Instantiate(
                            prefab,
                            position,
                            surfaceRotation * Quaternion.Euler(0f, quarterTurns * 90f, 0f),
                            m_roadsRoot);
                    }
                }
            }

            return null;
        }

        private Quaternion CalculateRoadSurfaceRotation(GridCoordinate coordinate)
        {
            int localY = GridToLocal(coordinate).y;
            int span = m_blockHeight + 1;
            if (localY % span != 0)
            {
                return Quaternion.identity;
            }

            int boundary = localY / span;
            if (boundary <= 0 || boundary >= m_gridHeight)
            {
                return Quaternion.identity;
            }

            float rise = m_blockElevations[0, boundary] -
                         m_blockElevations[0, boundary - 1];
            return CalculateSlopeRotation(rise, m_roadTileSize);
        }

        private Quaternion CalculateSlopeRotation(float rise, float run)
        {
            if (Mathf.Abs(rise) <= 0.001f || run <= 0.001f)
            {
                return Quaternion.identity;
            }

            GridCoordinate forwardOffset = DirectionOffset(m_startDirection);
            Vector3 forward = new Vector3(forwardOffset.X, 0f, forwardOffset.Y);
            Vector3 normal = (Vector3.up - forward * (rise / run)).normalized;
            return Quaternion.FromToRotation(Vector3.up, normal);
        }

        private void CreateResidentialGround()
        {
            int groundIndex = 0;
            int horizontalSpan = m_blockWidth + 1;
            int verticalSpan = m_blockHeight + 1;
            GridCoordinate rightOffset = DirectionOffset(TurnRight(m_startDirection));
            GridCoordinate forwardOffset = DirectionOffset(m_startDirection);
            Vector3 right = new Vector3(rightOffset.X, 0f, rightOffset.Y);
            Vector3 forward = new Vector3(forwardOffset.X, 0f, forwardOffset.Y);
            Quaternion directionRotation = Quaternion.LookRotation(forward, Vector3.up);

            for (int blockY = 0; blockY < m_gridHeight; blockY++)
            {
                float localCentreY = blockY * verticalSpan + verticalSpan * 0.5f;
                for (int blockX = 0; blockX < m_gridWidth; blockX++)
                {
                    float localCentreX =
                        m_minimumLocalX + blockX * horizontalSpan + horizontalSpan * 0.5f;
                    Vector3 centre = m_gridOrigin +
                                     right * (localCentreX * m_roadTileSize) +
                                     forward * (localCentreY * m_roadTileSize);
                    centre.y += m_blockElevations[blockX, blockY] -
                                m_residentialGroundVerticalOffset;
                    Quaternion rotation = directionRotation;

                    bool isPlaceholder = m_residentialGroundPrefab == null;
                    GameObject ground = isPlaceholder
                        ? GameObject.CreatePrimitive(PrimitiveType.Plane)
                        : Instantiate(
                            m_residentialGroundPrefab,
                            centre,
                            rotation,
                            m_residentialGroundRoot);
                    ground.name = isPlaceholder
                        ? $"Placeholder_ResidentialGround_{groundIndex:000}"
                        : $"ResidentialGround_{groundIndex:000}";
                    ground.transform.SetPositionAndRotation(centre, rotation);
                    ground.transform.SetParent(m_residentialGroundRoot, true);

                    FitResidentialGroundToBlock(
                        ground.transform,
                        centre,
                        m_blockWidth * m_roadTileSize,
                        m_blockHeight * m_roadTileSize,
                        right,
                        forward);
                    groundIndex++;
                }
            }
        }

        private static void FitResidentialGroundToBlock(
            Transform ground,
            Vector3 blockCentre,
            float blockWidth,
            float blockDepth,
            Vector3 blockRight,
            Vector3 blockForward)
        {
            Renderer[] renderers = ground.GetComponentsInChildren<Renderer>(true);
            if (!TryCalculateRendererBounds(renderers, out Bounds bounds))
            {
                return;
            }

            float renderedWidth = ProjectBoundsSizeOntoAxis(bounds.size, blockRight);
            float renderedDepth = ProjectBoundsSizeOntoAxis(bounds.size, blockForward);
            if (renderedWidth <= 0.001f || renderedDepth <= 0.001f)
            {
                return;
            }

            Vector3 scale = ground.localScale;
            scale.x *= blockWidth / renderedWidth;
            scale.z *= blockDepth / renderedDepth;
            ground.localScale = scale;

            if (!TryCalculateRendererBounds(renderers, out bounds))
            {
                return;
            }

            // Bounds-based centring supports prefabs whose visual is offset from its pivot.
            // Y scale is deliberately untouched; the visual centre sits just below the
            // generated elevation while all child colliders inherit the same X/Z fit.
            ground.position += blockCentre - bounds.center;
        }

        private static float ProjectBoundsSizeOntoAxis(Vector3 boundsSize, Vector3 axis)
        {
            Vector3 absoluteAxis = new Vector3(
                Mathf.Abs(axis.x),
                Mathf.Abs(axis.y),
                Mathf.Abs(axis.z));
            return Vector3.Dot(boundsSize, absoluteAxis);
        }

        private void GenerateProperties(System.Random random)
        {
            int houseIndex = 0;
            int horizontalSpan = m_blockWidth + 1;
            int verticalSpan = m_blockHeight + 1;
            for (int blockY = 0; blockY < m_gridHeight; blockY++)
            {
                int minimumY = blockY * verticalSpan + 1;
                int maximumY = minimumY + m_blockHeight - 1;
                for (int blockX = 0; blockX < m_gridWidth; blockX++)
                {
                    int minimumX = m_minimumLocalX + blockX * horizontalSpan + 1;
                    int maximumX = minimumX + m_blockWidth - 1;
                    List<PropertyCandidate> candidates = CreatePropertyCandidates(
                        minimumX, maximumX, minimumY, maximumY);
                    Shuffle(candidates, random);

                    int housesInBlock = 0;
                    for (int candidateIndex = 0;
                         candidateIndex < candidates.Count && housesInBlock < m_housesPerBlock;
                         candidateIndex++)
                    {
                        if (m_selectedRegion != null && random.NextDouble() > m_selectedRegion.LotDensity)
                        {
                            continue;
                        }

                        PropertyCandidate candidate = candidates[candidateIndex];
                        LotDefinition lotDefinition = SelectLotDefinition(random, false);
                        if (m_selectedRegion != null && lotDefinition == null)
                        {
                            continue;
                        }

                        GameObject prefab = lotDefinition != null
                            ? ValidateLotPrefab(lotDefinition.Prefab)
                            : SelectHousePrefab(random);
                        Vector2Int footprint = GetLotFootprint(lotDefinition, prefab);
                        List<GridCoordinate> cells = GetPropertyCells(
                            candidate.Anchor, candidate.PropertyToRoad, footprint);
                        if (!CanPlaceProperty(cells, minimumX, maximumX, minimumY, maximumY))
                        {
                            continue;
                        }

                        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                        {
                            m_propertyCells.Add(cells[cellIndex]);
                        }

                        Vector3 centre = CalculateCellCentre(cells);
                        SpawnLot(
                            lotDefinition,
                            prefab,
                            candidate,
                            footprint,
                            centre,
                            m_housesRoot,
                            $"Lot_{houseIndex:000}");
                        housesInBlock++;
                        houseIndex++;
                    }
                }
            }
        }

        private void GenerateLandmarks(System.Random random)
        {
            if (m_selectedRegion == null || m_selectedRegion.MaximumLandmarks <= 0)
            {
                return;
            }

            int targetCount = random.Next(
                m_selectedRegion.MinimumLandmarks,
                m_selectedRegion.MaximumLandmarks + 1);
            if (targetCount <= 0)
            {
                return;
            }

            List<PropertyCandidate> candidates = new List<PropertyCandidate>();
            int horizontalSpan = m_blockWidth + 1;
            int verticalSpan = m_blockHeight + 1;
            for (int blockY = 0; blockY < m_gridHeight; blockY++)
            {
                int minimumY = blockY * verticalSpan + 1;
                int maximumY = minimumY + m_blockHeight - 1;
                for (int blockX = 0; blockX < m_gridWidth; blockX++)
                {
                    int minimumX = m_minimumLocalX + blockX * horizontalSpan + 1;
                    int maximumX = minimumX + m_blockWidth - 1;
                    candidates.AddRange(CreatePropertyCandidates(
                        minimumX, maximumX, minimumY, maximumY));
                }
            }

            Shuffle(candidates, random);
            for (int index = 0; index < candidates.Count && GeneratedLandmarkCount < targetCount; index++)
            {
                LotDefinition definition = SelectLotDefinition(random, true);
                if (definition == null)
                {
                    break;
                }

                PropertyCandidate candidate = candidates[index];
                GameObject prefab = ValidateLotPrefab(definition.Prefab);
                Vector2Int footprint = GetLotFootprint(definition, prefab);
                List<GridCoordinate> cells = GetPropertyCells(
                    candidate.Anchor,
                    candidate.PropertyToRoad,
                    footprint);
                Vector2Int local = GridToLocal(candidate.Anchor);
                int blockX = Mathf.Clamp((local.x - m_minimumLocalX) / horizontalSpan, 0, m_gridWidth - 1);
                int blockY = Mathf.Clamp(local.y / verticalSpan, 0, m_gridHeight - 1);
                int minimumX = m_minimumLocalX + blockX * horizontalSpan + 1;
                int maximumX = minimumX + m_blockWidth - 1;
                int minimumY = blockY * verticalSpan + 1;
                int maximumY = minimumY + m_blockHeight - 1;
                if (!CanPlaceProperty(cells, minimumX, maximumX, minimumY, maximumY))
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    m_propertyCells.Add(cells[cellIndex]);
                }

                SpawnLot(
                    definition,
                    prefab,
                    candidate,
                    footprint,
                    CalculateCellCentre(cells),
                    m_landmarksRoot,
                    $"Landmark_{GeneratedLandmarkCount:000}");
                GeneratedLandmarkCount++;
            }
        }

        private void SpawnLot(
            LotDefinition definition,
            GameObject prefab,
            PropertyCandidate candidate,
            Vector2Int footprint,
            Vector3 centre,
            Transform parent,
            string instanceName)
        {
            GameObject property = prefab != null
                ? Instantiate(prefab, centre, Quaternion.identity, parent)
                : CreatePlaceholderHouse(centre, footprint);
            property.name = prefab != null ? instanceName : $"Placeholder_{instanceName}";
            if (property.transform.parent != parent)
            {
                property.transform.SetParent(parent, true);
            }

            GeneratedProperty generatedProperty = property.GetComponent<GeneratedProperty>();
            if (generatedProperty == null)
            {
                generatedProperty = property.AddComponent<GeneratedProperty>();
            }

            Transform deliveryPoint = generatedProperty.DeliveryPoint;
            Transform roadConnection = generatedProperty.RoadConnection;
            if (prefab == null)
            {
                roadConnection = property.transform.Find("RoadConnection");
                deliveryPoint = property.transform.Find("DeliveryPoint");
            }

            generatedProperty.Initialise(candidate.Anchor, footprint, roadConnection, deliveryPoint);
            FacePropertyTowardsRoad(property.transform, candidate.Road);
            FitPropertyVisualToFootprint(generatedProperty, footprint);
            m_generatedProperties.Add(generatedProperty);

            GeneratedLot generatedLot = property.GetComponent<GeneratedLot>();
            if (generatedLot == null)
            {
                generatedLot = property.AddComponent<GeneratedLot>();
            }

            generatedLot.Initialise(definition, candidate.Anchor);
            m_generatedLots.Add(generatedLot);
            if (definition != null && definition.UniquePerMap)
            {
                m_spawnedUniqueLots.Add(definition);
            }

            RegisterDeliveryDestination(property, generatedProperty, definition);
        }

        private void RegisterDeliveryDestination(
            GameObject property,
            GeneratedProperty generatedProperty,
            LotDefinition definition)
        {
            bool canDeliver = definition == null || definition.CanBeDeliveryDestination;
            if (!canDeliver || generatedProperty.DeliveryPoint == null)
            {
                return;
            }

            DeliveryDestination destination = property.GetComponent<DeliveryDestination>();
            if (destination == null)
            {
                destination = property.AddComponent<DeliveryDestination>();
            }

            LotType lotType = definition != null ? definition.LotType : LotType.Residential;
            DeliveryDestinationType destinationType = lotType switch
            {
                LotType.Commercial => DeliveryDestinationType.Commercial,
                LotType.Landmark => DeliveryDestinationType.Landmark,
                LotType.Special => DeliveryDestinationType.Special,
                _ => DeliveryDestinationType.Standard
            };
            destination.Initialise(
                generatedProperty.DeliveryPoint,
                destinationType,
                definition != null ? definition.DestinationDisplayName : "House",
                definition != null ? definition.DeliveryDifficultyModifier : 1f,
                definition != null ? definition.DeliveryRewardModifier : 1f,
                true);
            m_deliveryDestinations.Add(destination);
        }

        private LotDefinition SelectLotDefinition(System.Random random, bool landmarkOnly)
        {
            WeightedLot[] lots = m_selectedRegion != null ? m_selectedRegion.Lots : null;
            if (lots == null || lots.Length == 0)
            {
                return null;
            }

            float totalWeight = 0f;
            for (int index = 0; index < lots.Length; index++)
            {
                LotDefinition definition = lots[index].Definition;
                if (definition == null ||
                    (definition.LotType == LotType.Landmark) != landmarkOnly ||
                    (definition.UniquePerMap && m_spawnedUniqueLots.Contains(definition)))
                {
                    continue;
                }

                totalWeight += Mathf.Max(0.01f, lots[index].Weight);
            }

            if (totalWeight <= 0f)
            {
                return null;
            }

            float selection = (float)random.NextDouble() * totalWeight;
            for (int index = 0; index < lots.Length; index++)
            {
                LotDefinition definition = lots[index].Definition;
                if (definition == null ||
                    (definition.LotType == LotType.Landmark) != landmarkOnly ||
                    (definition.UniquePerMap && m_spawnedUniqueLots.Contains(definition)))
                {
                    continue;
                }

                selection -= Mathf.Max(0.01f, lots[index].Weight);
                if (selection <= 0f)
                {
                    return definition;
                }
            }

            return null;
        }

        private static GameObject ValidateLotPrefab(GameObject prefab)
        {
            if (prefab == null || !prefab.TryGetComponent(out GeneratedProperty property) ||
                property.RoadConnection == null || property.GridFootprint.x <= 0 ||
                property.GridFootprint.y <= 0)
            {
                return null;
            }

            return prefab;
        }

        private Vector2Int GetLotFootprint(LotDefinition definition, GameObject prefab)
        {
            Vector2Int footprint = GetPropertyFootprint(prefab);
            if (definition == null)
            {
                return footprint;
            }

            Vector2Int minimum = definition.MinimumFootprint;
            return new Vector2Int(
                Mathf.Max(footprint.x, minimum.x),
                Mathf.Max(footprint.y, minimum.y));
        }

        private List<PropertyCandidate> CreatePropertyCandidates(
            int minimumX,
            int maximumX,
            int minimumY,
            int maximumY)
        {
            List<PropertyCandidate> candidates = new List<PropertyCandidate>(
                (m_blockWidth + m_blockHeight) * 2);
            for (int localX = minimumX; localX <= maximumX; localX++)
            {
                candidates.Add(new PropertyCandidate(
                    LocalToGrid(localX, minimumY),
                    LocalToGrid(localX, minimumY - 1),
                    Opposite(m_startDirection)));
                candidates.Add(new PropertyCandidate(
                    LocalToGrid(localX, maximumY),
                    LocalToGrid(localX, maximumY + 1),
                    m_startDirection));
            }

            CardinalDirection right = TurnRight(m_startDirection);
            for (int localY = minimumY; localY <= maximumY; localY++)
            {
                candidates.Add(new PropertyCandidate(
                    LocalToGrid(minimumX, localY),
                    LocalToGrid(minimumX - 1, localY),
                    Opposite(right)));
                candidates.Add(new PropertyCandidate(
                    LocalToGrid(maximumX, localY),
                    LocalToGrid(maximumX + 1, localY),
                    right));
            }

            return candidates;
        }

        private void GenerateRegionObjects(
            RegionSpawnRule[] rules,
            Transform parent,
            System.Random random,
            bool hazards)
        {
            if (rules == null || rules.Length == 0 || parent == null || m_roadOrder.Count == 0)
            {
                return;
            }

            int[] spawnedPerRule = new int[rules.Length];
            int additionalCount = 0;
            for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                int minimum = Mathf.Max(0, rules[ruleIndex].MinimumCount);
                int maximum = Mathf.Max(minimum, rules[ruleIndex].MaximumCount);
                if (rules[ruleIndex].Prefab == null)
                {
                    continue;
                }

                for (int instanceIndex = 0; instanceIndex < minimum; instanceIndex++)
                {
                    SpawnRegionObject(rules[ruleIndex], parent, random, hazards);
                    spawnedPerRule[ruleIndex]++;
                }

                additionalCount += maximum > minimum
                    ? random.Next(0, maximum - minimum + 1)
                    : 0;
            }

            for (int index = 0; index < additionalCount; index++)
            {
                int selectedRule = SelectSpawnRule(rules, spawnedPerRule, random);
                if (selectedRule < 0)
                {
                    break;
                }

                SpawnRegionObject(rules[selectedRule], parent, random, hazards);
                spawnedPerRule[selectedRule]++;
            }
        }

        private void SpawnRegionObject(
            RegionSpawnRule rule,
            Transform parent,
            System.Random random,
            bool hazards)
        {
            GridCoordinate road = m_roadOrder[random.Next(m_roadOrder.Count)];
            Vector3 centre = GridToWorld(road);
            double angle = random.NextDouble() * Math.PI * 2.0;
            float placementOffset = rule.Placement switch
            {
                RegionObjectPlacement.Roadside => m_roadTileSize * 0.55f,
                RegionObjectPlacement.OpenLot => m_roadTileSize,
                _ => 0f
            };
            float minimumOffset = Mathf.Max(placementOffset, rule.MinimumRoadOffset);
            float maximumOffset = Mathf.Max(minimumOffset, rule.MaximumRoadOffset);
            float offset = Mathf.Lerp(minimumOffset, maximumOffset, (float)random.NextDouble());
            Vector3 direction = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
            Vector3 position = centre + direction * offset;
            position.y = GetCellHeight(WorldToGrid(position));
            GameObject instance = Instantiate(
                rule.Prefab,
                position,
                Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f),
                parent);
            instance.name = hazards
                ? $"Hazard_{GeneratedHazardCount:000}"
                : $"Prop_{GeneratedPropCount:000}";
            if (hazards)
            {
                GeneratedHazardCount++;
            }
            else
            {
                GeneratedPropCount++;
            }
        }

        private static int SelectSpawnRule(
            RegionSpawnRule[] rules,
            int[] spawnedPerRule,
            System.Random random)
        {
            float totalWeight = 0f;
            for (int index = 0; index < rules.Length; index++)
            {
                int maximum = Mathf.Max(rules[index].MinimumCount, rules[index].MaximumCount);
                if (spawnedPerRule[index] < maximum && rules[index].Prefab != null)
                {
                    totalWeight += Mathf.Max(0.01f, rules[index].Weight);
                }
            }

            if (totalWeight <= 0f)
            {
                return -1;
            }

            float selection = (float)random.NextDouble() * totalWeight;
            for (int index = 0; index < rules.Length; index++)
            {
                int maximum = Mathf.Max(rules[index].MinimumCount, rules[index].MaximumCount);
                if (spawnedPerRule[index] >= maximum || rules[index].Prefab == null)
                {
                    continue;
                }

                selection -= Mathf.Max(0.01f, rules[index].Weight);
                if (selection <= 0f)
                {
                    return index;
                }
            }

            return -1;
        }

        private void BuildNavMeshOnce()
        {
            bool shouldBuild = m_selectedRegion == null || m_selectedRegion.BuildNavMesh;
            if (shouldBuild && m_navMeshSurface != null)
            {
                m_navMeshSurface.BuildNavMesh();
                IsNavMeshReady = m_navMeshSurface.navMeshData != null &&
                                 NavMesh.CalculateTriangulation().vertices.Length > 0;
            }
        }

        private GameObject SelectHousePrefab(System.Random random)
        {
            // Consume one selection value even when no art is assigned. This keeps
            // later property decisions stable as placeholders are replaced.
            int selectionValue = random.Next();
            if (m_housePrefabs == null || m_housePrefabs.Length == 0)
            {
                return null;
            }

            int first = selectionValue % m_housePrefabs.Length;
            for (int offset = 0; offset < m_housePrefabs.Length; offset++)
            {
                GameObject prefab = m_housePrefabs[(first + offset) % m_housePrefabs.Length];
                if (prefab != null && prefab.TryGetComponent(out GeneratedProperty property) &&
                    property.RoadConnection != null && property.DeliveryPoint != null &&
                    property.GridFootprint.x > 0 && property.GridFootprint.y > 0)
                {
                    return prefab;
                }
            }

            return null;
        }

        private Vector2Int GetPropertyFootprint(GameObject prefab)
        {
            return prefab != null
                ? prefab.GetComponent<GeneratedProperty>().GridFootprint
                : m_standardPropertyFootprint;
        }

        private List<GridCoordinate> GetPropertyCells(
            GridCoordinate frontCentre,
            CardinalDirection propertyToRoadDirection,
            Vector2Int footprint)
        {
            List<GridCoordinate> cells = new List<GridCoordinate>(footprint.x * footprint.y);
            CardinalDirection awayFromRoad = Opposite(propertyToRoadDirection);
            CardinalDirection widthDirection = TurnRight(awayFromRoad);
            int minimumWidthOffset = -(footprint.x / 2);
            for (int depth = 0; depth < footprint.y; depth++)
            {
                for (int width = 0; width < footprint.x; width++)
                {
                    cells.Add(frontCentre +
                              Multiply(DirectionOffset(awayFromRoad), depth) +
                              Multiply(DirectionOffset(widthDirection), minimumWidthOffset + width));
                }
            }

            return cells;
        }

        private bool CanPlaceProperty(
            List<GridCoordinate> cells,
            int minimumLocalX,
            int maximumLocalX,
            int minimumLocalY,
            int maximumLocalY)
        {
            for (int index = 0; index < cells.Count; index++)
            {
                GridCoordinate cell = cells[index];
                Vector2Int local = GridToLocal(cell);
                if (local.x < minimumLocalX || local.x > maximumLocalX ||
                    local.y < minimumLocalY || local.y > maximumLocalY)
                {
                    return false;
                }

                if (m_roadCells.Contains(cell) || m_startingAreaCells.Contains(cell) || m_propertyCells.Contains(cell))
                {
                    return false;
                }

                if (m_minimumHouseSpacing <= 0)
                {
                    continue;
                }

                for (int x = -m_minimumHouseSpacing; x <= m_minimumHouseSpacing; x++)
                {
                    for (int y = -m_minimumHouseSpacing; y <= m_minimumHouseSpacing; y++)
                    {
                        if (m_propertyCells.Contains(cell + new GridCoordinate(x, y)))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private Vector2Int GridToLocal(GridCoordinate coordinate)
        {
            GridCoordinate right = DirectionOffset(TurnRight(m_startDirection));
            GridCoordinate forward = DirectionOffset(m_startDirection);
            return new Vector2Int(
                coordinate.X * right.X + coordinate.Y * right.Y,
                coordinate.X * forward.X + coordinate.Y * forward.Y);
        }

        private static void Shuffle<T>(List<T> values, System.Random random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(index + 1);
                (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
            }
        }

        private GameObject CreatePlaceholderHouse(Vector3 centre, Vector2Int footprint)
        {
            if (!m_warnedAboutHousePlaceholders)
            {
                Debug.LogWarning("No valid House prefab is available; Cube placeholder Houses are being used.", this);
                m_warnedAboutHousePlaceholders = true;
            }

            GameObject root = new GameObject("Placeholder_House");
            root.transform.SetPositionAndRotation(centre, Quaternion.identity);
            root.transform.SetParent(m_housesRoot, true);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Visual";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = Vector3.up * (m_placeholderHouseSize.y * 0.5f);
            cube.transform.localScale = new Vector3(
                Mathf.Min(m_placeholderHouseSize.x, footprint.x * m_roadTileSize),
                m_placeholderHouseSize.y,
                Mathf.Min(m_placeholderHouseSize.z, footprint.y * m_roadTileSize));

            Transform roadConnection = new GameObject("RoadConnection").transform;
            roadConnection.SetParent(root.transform, false);
            roadConnection.localPosition = Vector3.forward * (footprint.y * m_roadTileSize * 0.5f);
            Transform deliveryPoint = new GameObject("DeliveryPoint").transform;
            deliveryPoint.SetParent(root.transform, false);
            deliveryPoint.localPosition = Vector3.forward * (footprint.y * m_roadTileSize * 0.5f + 1f);

            GeneratedProperty property = root.AddComponent<GeneratedProperty>();
            property.Initialise(GridCoordinate.Zero, footprint, roadConnection, deliveryPoint);
            return root;
        }

        private void FacePropertyTowardsRoad(Transform property, GridCoordinate roadCoordinate)
        {
            Vector3 directionToRoad = GridToWorld(roadCoordinate) - property.position;
            directionToRoad.y = 0f;
            if (directionToRoad.sqrMagnitude < 0.001f)
            {
                return;
            }

            GeneratedProperty metadata = property.GetComponent<GeneratedProperty>();
            if (metadata != null && metadata.FitVisualToFootprint && metadata.VisualRoot != null)
            {
                // The Blender houses are authored Z-up with their facade along -Y.
                // Correct Z-up to Unity Y-up with -90 X, then turn around the model's
                // original Z axis so the facade points at this property's road.
                Vector3 localDirectionToRoad = Vector3.ProjectOnPlane(
                    property.InverseTransformDirection(directionToRoad.normalized),
                    Vector3.up).normalized;
                float localZAngle = Vector3.SignedAngle(
                    Vector3.forward,
                    localDirectionToRoad,
                    Vector3.up);
                metadata.VisualRoot.localRotation = Quaternion.Euler(-90f, 0f, localZAngle);
                SetPropertyPointDirection(metadata.RoadConnection, localDirectionToRoad);
                SetPropertyPointDirection(metadata.DeliveryPoint, localDirectionToRoad);
                return;
            }

            property.rotation = Quaternion.LookRotation(directionToRoad.normalized, Vector3.up);
        }

        private static void SetPropertyPointDirection(Transform point, Vector3 localDirection)
        {
            if (point == null || localDirection.sqrMagnitude < 0.001f)
            {
                return;
            }

            float distance = Vector3.ProjectOnPlane(point.localPosition, Vector3.up).magnitude;
            point.localPosition = localDirection.normalized * distance;
            point.localRotation = Quaternion.LookRotation(localDirection.normalized, Vector3.up);
        }

        private void FitPropertyVisualToFootprint(GeneratedProperty property, Vector2Int footprint)
        {
            if (property == null || !property.FitVisualToFootprint || property.VisualRoot == null)
            {
                return;
            }

            Renderer[] renderers = property.VisualRoot.GetComponentsInChildren<Renderer>(true);
            if (!TryCalculateRendererBounds(renderers, out Bounds bounds) ||
                bounds.size.x <= 0.001f || bounds.size.z <= 0.001f)
            {
                return;
            }

            float availableWidth = footprint.x * m_roadTileSize * m_houseFootprintFill;
            float availableDepth = footprint.y * m_roadTileSize * m_houseFootprintFill;
            float uniformScale = Mathf.Min(
                availableWidth / bounds.size.x,
                availableDepth / bounds.size.z);
            property.VisualRoot.localScale *= uniformScale;

            renderers = property.VisualRoot.GetComponentsInChildren<Renderer>(true);
            if (!TryCalculateRendererBounds(renderers, out bounds))
            {
                return;
            }

            property.VisualRoot.position += new Vector3(
                property.transform.position.x - bounds.center.x,
                property.transform.position.y - bounds.min.y,
                property.transform.position.z - bounds.center.z);
            TryCalculateRendererBounds(renderers, out bounds);
            if (!property.TryGetComponent(out BoxCollider houseCollider))
            {
                houseCollider = property.gameObject.AddComponent<BoxCollider>();
            }

            houseCollider.center = property.transform.InverseTransformPoint(bounds.center);
            Vector3 localMinimum = property.transform.InverseTransformPoint(bounds.min);
            Vector3 localMaximum = property.transform.InverseTransformPoint(bounds.max);
            houseCollider.size = new Vector3(
                Mathf.Abs(localMaximum.x - localMinimum.x),
                Mathf.Abs(localMaximum.y - localMinimum.y),
                Mathf.Abs(localMaximum.z - localMinimum.z));

            Vector3 frontDirection = property.RoadConnection != null
                ? Vector3.ProjectOnPlane(property.RoadConnection.localPosition, Vector3.up)
                : Vector3.forward;
            frontDirection = frontDirection.sqrMagnitude > 0.001f
                ? frontDirection.normalized
                : Vector3.forward;
            float propertyFront = footprint.y * m_roadTileSize * 0.5f;
            if (property.RoadConnection != null)
            {
                property.RoadConnection.localPosition = frontDirection * propertyFront;
            }

            if (property.DeliveryPoint != null)
            {
                property.DeliveryPoint.localPosition = frontDirection *
                    (propertyFront + m_roadTileSize * 0.05f);
            }
        }

        private static bool TryCalculateRendererBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        private Vector3 CalculateCellCentre(List<GridCoordinate> cells)
        {
            Vector3 centre = Vector3.zero;
            for (int index = 0; index < cells.Count; index++)
            {
                centre += GridToWorld(cells[index]);
            }

            return centre / cells.Count;
        }

        private void ValidateLayout()
        {
            if (m_roadCells.Count != m_logicalRoads.Count || m_roadCells.Count != m_roadHeights.Count)
            {
                Debug.LogError("Generated neighbourhood road data is inconsistent.", this);
                return;
            }

            foreach (GridCoordinate propertyCell in m_propertyCells)
            {
                if (m_roadCells.Contains(propertyCell) || m_startingAreaCells.Contains(propertyCell))
                {
                    Debug.LogError($"Generated property cell {propertyCell} overlaps reserved neighbourhood space.", this);
                    return;
                }
            }

            foreach (KeyValuePair<GridCoordinate, RoadConnections> road in m_logicalRoads)
            {
                for (int directionIndex = 0; directionIndex < 2; directionIndex++)
                {
                    CardinalDirection direction = (CardinalDirection)directionIndex;
                    if ((road.Value & DirectionConnection(direction)) == 0)
                    {
                        continue;
                    }

                    GridCoordinate neighbour = road.Key + DirectionOffset(direction);
                    if (!m_roadHeights.TryGetValue(neighbour, out float neighbourHeight))
                    {
                        continue;
                    }

                    float rise = Mathf.Abs(neighbourHeight - m_roadHeights[road.Key]);
                    float slope = Mathf.Atan2(rise, m_roadTileSize) * Mathf.Rad2Deg;
                    if (slope > m_maximumRoadSlope + 0.01f)
                    {
                        Debug.LogError(
                            $"Generated road slope of {slope:F1} degrees exceeds the configured maximum.",
                            this);
                        return;
                    }
                }
            }
        }

        private static int DeriveSeed(int seed, int salt)
        {
            unchecked
            {
                return (seed * 397) ^ salt;
            }
        }

        private static GridCoordinate DirectionOffset(CardinalDirection direction)
        {
            return direction switch
            {
                CardinalDirection.North => new GridCoordinate(0, 1),
                CardinalDirection.East => new GridCoordinate(1, 0),
                CardinalDirection.South => new GridCoordinate(0, -1),
                _ => new GridCoordinate(-1, 0)
            };
        }

        private static RoadConnections DirectionConnection(CardinalDirection direction)
        {
            return direction switch
            {
                CardinalDirection.North => RoadConnections.North,
                CardinalDirection.East => RoadConnections.East,
                CardinalDirection.South => RoadConnections.South,
                _ => RoadConnections.West
            };
        }

        private static CardinalDirection ClosestDirection(Vector3 direction)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (Mathf.Abs(horizontal.x) > Mathf.Abs(horizontal.z))
            {
                return horizontal.x >= 0f ? CardinalDirection.East : CardinalDirection.West;
            }

            return horizontal.z >= 0f ? CardinalDirection.North : CardinalDirection.South;
        }

        private static CardinalDirection TurnRight(CardinalDirection direction) =>
            (CardinalDirection)(((int)direction + 1) % 4);

        private static CardinalDirection TurnLeft(CardinalDirection direction) =>
            (CardinalDirection)(((int)direction + 3) % 4);

        private static CardinalDirection Opposite(CardinalDirection direction) =>
            (CardinalDirection)(((int)direction + 2) % 4);

        private static GridCoordinate Multiply(GridCoordinate coordinate, int amount) =>
            new GridCoordinate(coordinate.X * amount, coordinate.Y * amount);

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!m_showGrid && !m_showOccupiedCells && !m_showConnections)
            {
                return;
            }

            if (m_showOccupiedCells)
            {
                DrawCells(m_startingAreaCells, new Color(1f, 0.65f, 0.1f, 0.25f));
                DrawCells(m_roadCells, new Color(0.2f, 0.65f, 1f, 0.25f));
                DrawCells(m_propertyCells, new Color(0.25f, 1f, 0.35f, 0.25f));
            }

            if (m_showGrid)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.18f);
                foreach (GridCoordinate coordinate in m_roadCells)
                {
                    Gizmos.DrawWireCube(
                        GridToWorld(coordinate),
                        new Vector3(m_roadTileSize, 0.05f, m_roadTileSize));
                }
            }

            if (m_showConnections)
            {
                Gizmos.color = Color.cyan;
                foreach (KeyValuePair<GridCoordinate, RoadConnections> road in m_logicalRoads)
                {
                    Vector3 centre = GridToWorld(road.Key) + Vector3.up * 0.2f;
                    for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                    {
                        CardinalDirection direction = (CardinalDirection)directionIndex;
                        if ((road.Value & DirectionConnection(direction)) != 0)
                        {
                            GridCoordinate offset = DirectionOffset(direction);
                            Gizmos.DrawLine(
                                centre,
                                centre + new Vector3(offset.X, 0f, offset.Y) * (m_roadTileSize * 0.45f));
                        }
                    }
                }
            }

            Gizmos.color = new Color(1f, 0.3f, 0.85f, 0.9f);
            for (int index = 0; index < m_deliveryDestinations.Count; index++)
            {
                DeliveryDestination destination = m_deliveryDestinations[index];
                if (destination != null && destination.DropPosition != null)
                {
                    Gizmos.DrawWireSphere(destination.DropPosition.position, 0.5f);
                }
            }
        }

        private void DrawCells(HashSet<GridCoordinate> cells, Color colour)
        {
            Gizmos.color = colour;
            foreach (GridCoordinate coordinate in cells)
            {
                Gizmos.DrawCube(
                    GridToWorld(coordinate),
                    new Vector3(m_roadTileSize * 0.95f, 0.08f, m_roadTileSize * 0.95f));
            }
        }

        private void OnValidate()
        {
            m_gridWidth = Mathf.Max(1, m_gridWidth);
            m_gridHeight = Mathf.Max(1, m_gridHeight);
            m_blockWidth = Mathf.Max(1, m_blockWidth);
            m_blockHeight = Mathf.Max(1, m_blockHeight);
            m_roadTileSize = Mathf.Max(0.1f, m_roadTileSize);
            m_housesPerBlock = Mathf.Max(0, m_housesPerBlock);
            m_minimumHouseSpacing = Mathf.Max(0, m_minimumHouseSpacing);
            m_elevationStep = Mathf.Max(0.1f, m_elevationStep);
            m_minimumElevationStep = Mathf.Clamp(
                m_minimumElevationStep,
                0.1f,
                m_elevationStep);
            m_maximumElevation = Mathf.Max(0f, m_maximumElevation);
            m_maximumRoadSlope = Mathf.Clamp(m_maximumRoadSlope, 1f, 60f);
            m_residentialGroundVerticalOffset = Mathf.Max(0f, m_residentialGroundVerticalOffset);
            m_standardPropertyFootprint.x = Mathf.Max(1, m_standardPropertyFootprint.x);
            m_standardPropertyFootprint.y = Mathf.Max(1, m_standardPropertyFootprint.y);
            m_placeholderStartingAreaFootprint.x = Mathf.Max(1, m_placeholderStartingAreaFootprint.x);
            m_placeholderStartingAreaFootprint.y = Mathf.Max(1, m_placeholderStartingAreaFootprint.y);
        }
#endif
    }
}
