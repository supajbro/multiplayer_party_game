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
        [Tooltip("Optional large home used when distance progression assigns one lot to a block.")]
        [SerializeField] private GameObject m_mansionPrefab;
        [SerializeField, Min(0)] private int m_minimumHousesPerBlock = 1;
        [SerializeField, Min(0)] private int m_housesPerBlock = 3;
        [SerializeField, Min(0)] private int m_minimumHouseSpacing = 1;
        [Tooltip("Minimum world-space clearance kept around the rendered footprint of every house.")]
        [SerializeField, Min(0f)] private float m_minimumHouseClearance = 1f;
        [SerializeField] private Vector2Int m_standardPropertyFootprint = Vector2Int.one;
        [SerializeField] private Vector3 m_placeholderHouseSize = new Vector3(10f, 6f, 10f);
        [Tooltip("Uniform house visual size relative to its generated property footprint. Values above 1 allow a small, intentional overhang beyond the grid cell.")]
        [SerializeField, Range(0.1f, 1.5f)] private float m_houseFootprintFill = 1.15f;
        [Tooltip("Lots steeper than this are skipped. Accepted houses use the sampled surface normal for pitch and roll.")]
        [SerializeField, Range(0f, 45f)] private float m_maximumHouseSlope = 18f;

        [Header("Sidewalks & Frontage")]
        [SerializeField] private GameObject m_sidewalkPrefab;
        [SerializeField] private Material m_sidewalkMaterial;
        [SerializeField, Min(0f)] private float m_sidewalkWidth = 1.5f;
        [SerializeField, Min(0f)] private float m_sidewalkSurfaceOffset = 0.06f;
        [SerializeField, Min(0f)] private float m_houseSetbackFromSidewalk = 3f;
        [SerializeField, Min(0f)] private float m_houseSetbackVariation = 1.5f;
        [SerializeField, Min(0.5f)] private float m_drivewayWidth = 3.5f;
        [SerializeField, Min(0f)] private float m_drivewayClearance = 0.75f;
        [SerializeField, Range(1f, 30f)] private float m_maximumDrivewaySlope = 16f;

        [Header("Elevation")]
        [SerializeField] private bool m_elevationEnabled = true;
        [Tooltip("Minimum seeded height difference between elevated neighbourhood sections.")]
        [SerializeField, Min(0.1f)] private float m_minimumElevationStep = 2f;
        [Tooltip("Maximum seeded height difference between elevated neighbourhood sections.")]
        [SerializeField, Min(0.1f)] private float m_elevationStep = 8f;
        [SerializeField, Min(0f)] private float m_maximumElevation = 24f;
        [SerializeField, Range(1f, 60f)] private float m_maximumRoadSlope = 45f;

        [Header("Suburbs Progression")]
        [SerializeField] private RegionProgressionSettings m_progression = RegionProgressionSettings.SuburbsDefault;

        [Header("Continuous Terrain")]
        [SerializeField] private GeneratedTerrainSettings m_terrainSettings =
            GeneratedTerrainSettings.SuburbsDefault;

        [Header("Delivery Difficulty")]
        [SerializeField] private DeliveryDifficultySettings m_deliveryDifficulty =
            DeliveryDifficultySettings.SuburbsDefault;

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
        [SerializeField] private bool m_showDeliveryDifficulty = true;
        [SerializeField] private bool m_showProgressionZones = true;
        [SerializeField] private bool m_showPrimaryRoute = true;
        [SerializeField] private bool m_showElevation;

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
        private readonly Dictionary<GridCoordinate, SuburbZone> m_roadZones =
            new Dictionary<GridCoordinate, SuburbZone>();
        private readonly HashSet<GridCoordinate> m_primaryRoadCells = new HashSet<GridCoordinate>();
        private readonly List<OrientedFootprint> m_placedHouseFootprints =
            new List<OrientedFootprint>();
        private readonly List<OrientedFootprint> m_reservedDrivewayFootprints =
            new List<OrientedFootprint>();

        private float[,] m_blockElevations;
        private int m_minimumLocalX;

        private Transform m_generatedRoot;
        private Transform m_roadsRoot;
        private Transform m_sidewalksRoot;
        private Transform m_residentialGroundRoot;
        private Transform m_housesRoot;
        private Transform m_landmarksRoot;
        private Transform m_propsRoot;
        private Transform m_hazardsRoot;
        private Vector3 m_gridOrigin;
        private CardinalDirection m_startDirection;
        private bool m_warnedAboutRoadPlaceholders;
        private bool m_warnedAboutHousePlaceholders;
        private float m_elevationNoiseOffsetX;
        private float m_elevationNoiseOffsetY;

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

        private readonly struct OrientedFootprint
        {
            public readonly Vector2 Centre;
            public readonly Vector2 Right;
            public readonly Vector2 Forward;
            public readonly Vector2 HalfSize;

            public OrientedFootprint(
                Vector2 centre,
                Vector2 right,
                Vector2 forward,
                Vector2 halfSize)
            {
                Centre = centre;
                Right = right;
                Forward = forward;
                HalfSize = halfSize;
            }
        }

        public StartingArea GeneratedStartingArea { get; private set; }
        public GeneratedTerrainMesh GeneratedTerrain { get; private set; }
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
        public bool IsDeliveryDifficultyReady { get; private set; }
        public bool LastValidationSucceeded { get; private set; }
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
            GenerateInternal(region, seed, 0);
        }

        private void EnsureDifficultySettings()
        {
            if (m_deliveryDifficulty.TotalWeight <= 0.001f ||
                m_deliveryDifficulty.LongRouteDistance <= 0f ||
                m_deliveryDifficulty.LongDirectDistance <= 0f ||
                m_deliveryDifficulty.HighElevationGain <= 0f ||
                m_deliveryDifficulty.SteepRoadGrade <= 0f ||
                m_deliveryDifficulty.ManyTurns <= 0 ||
                m_deliveryDifficulty.ManyIntersections <= 0 ||
                m_deliveryDifficulty.LongFinalCarry <= 0f ||
                m_deliveryDifficulty.HighFinalCarryElevation <= 0f)
            {
                m_deliveryDifficulty = DeliveryDifficultySettings.SuburbsDefault;
            }
        }

        private void GenerateInternal(RegionDefinition region, int seed, int attemptIndex)
        {
            ApplyRegionConfiguration(region);
            m_progression.Validate();
            m_terrainSettings.Validate();
            EnsureDifficultySettings();
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
            m_minimumLocalX = -(m_gridWidth / 2) * (m_blockWidth + 1);
            AssignBlockElevations(layoutRandom);
            GenerateRoadLayout(new System.Random(DeriveSeed(seed, 0x1874A2B1)));
            CalculateRoadConnections();
            ResolveRoadElevations();
            RefreshBlockElevationsFromRoads();
            // Visual selection has its own stream so adding art cannot perturb the
            // logical road/property decisions made for a given seed.
            CreateRoadVisuals(new System.Random(DeriveSeed(seed, 0x2D31A7B5)));
            GenerateLandmarks(new System.Random(DeriveSeed(seed, 0x4F1BBCDC)));
            GenerateProperties(new System.Random(DeriveSeed(seed, 0x61C88647)));
            CreateContinuousTerrain();
            CreateSidewalks();
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
            AnalyseDeliveryDestinations();
            CreateDeliveryNpc();
            CalculateWorldBounds();
            LastValidationSucceeded = ValidateLayout();
            if (!LastValidationSucceeded)
            {
                stopwatch.Stop();
                LastGenerationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                int maximumAttempts = Mathf.Max(1, m_progression.MaximumGenerationAttempts);
                if (attemptIndex + 1 < maximumAttempts)
                {
                    int retrySeed = DeriveSeed(seed, 0x51ED270B + attemptIndex);
                    Debug.LogWarning(
                        $"Generated Suburbs seed {CurrentSeed} failed validation; " +
                        $"retrying with deterministic repair seed {retrySeed} " +
                        $"({attemptIndex + 2}/{maximumAttempts}).",
                        this);
                    GenerateInternal(region, retrySeed, attemptIndex + 1);
                    return;
                }

                Debug.LogError(
                    $"Generated Suburbs seed {CurrentSeed} failed validation after " +
                    $"{maximumAttempts} attempts and will not be exposed to gameplay.",
                    this);
                return;
            }
            BuildNavMeshOnce();
            IsGenerationReady = true;
            stopwatch.Stop();
            LastGenerationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            Debug.Log(
                $"Generated region '{CurrentRegionName}' with seed {CurrentSeed}: " +
                $"{m_generatedRoads.Count} roads, {m_generatedLots.Count} lots, " +
                $"{m_deliveryDestinations.Count} destinations, {GeneratedLandmarkCount} landmarks, " +
                $"{GeneratedHazardCount} hazards, " +
                $"{GeneratedTerrain?.VertexCount ?? 0} terrain vertices, difficulty pools " +
                $"{(IsDeliveryDifficultyReady ? "ready" : "incomplete")} " +
                $"[{GetStrictDestinationCountForStage(0)}/" +
                $"{GetStrictDestinationCountForStage(1)}/" +
                $"{GetStrictDestinationCountForStage(2)}/" +
                $"{GetStrictDestinationCountForStage(3)}/" +
                $"{GetStrictDestinationCountForStage(4)}/" +
                $"{GetStrictDestinationCountForStage(5)}/" +
                $"{GetStrictDestinationCountForStage(6)}] in " +
                $"{LastGenerationMilliseconds:F1} ms.",
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
            m_minimumHousesPerBlock = grid.MinimumLotsPerBlock;
            m_housesPerBlock = grid.LotsPerBlock;
            m_minimumHouseSpacing = grid.MinimumLotSpacing;
            m_elevationEnabled = elevation.Enabled;
            m_minimumElevationStep = elevation.MinimumStep;
            m_elevationStep = elevation.MaximumStep;
            m_maximumElevation = elevation.MaximumElevation;
            m_maximumRoadSlope = elevation.MaximumRoadSlope;
            m_progression = region.Progression;
            m_terrainSettings = region.Terrain;
            m_roadPrefabs = region.RoadPrefabs ?? Array.Empty<GameObject>();
            m_residentialGroundPrefab = region.GroundPrefab;
            m_startingAreaPrefab = region.StartingAreaPrefab;
            m_deliveryNpcPrefab = region.DeliveryNpcPrefab;
            m_mansionPrefab = region.MansionPrefab;
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
            m_sidewalksRoot = null;
            m_residentialGroundRoot = null;
            m_housesRoot = null;
            m_landmarksRoot = null;
            m_propsRoot = null;
            m_hazardsRoot = null;
            GeneratedStartingArea = null;
            GeneratedTerrain = null;
            IsGenerationReady = false;
            IsNavMeshReady = false;
            IsDeliveryDifficultyReady = false;
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
            m_roadZones.Clear();
            m_primaryRoadCells.Clear();
            m_placedHouseFootprints.Clear();
            m_reservedDrivewayFootprints.Clear();
            m_blockElevations = null;
            GeneratedWorldBounds = new Bounds(transform.position, Vector3.zero);
            m_warnedAboutRoadPlaceholders = false;
            m_warnedAboutHousePlaceholders = false;
            GeneratedLandmarkCount = 0;
            GeneratedPropCount = 0;
            GeneratedHazardCount = 0;
            LastValidationSucceeded = false;
        }

        public Vector3 GridToWorld(GridCoordinate coordinate)
        {
            float height = GetCellHeight(coordinate);
            return m_gridOrigin + new Vector3(
                coordinate.X * m_roadTileSize,
                height,
                coordinate.Y * m_roadTileSize);
        }

        public bool TryGetRoadHeight(GridCoordinate coordinate, out float height) =>
            m_roadHeights.TryGetValue(coordinate, out height);

        public bool TryGetRoadConnections(GridCoordinate coordinate, out RoadConnections connections) =>
            m_logicalRoads.TryGetValue(coordinate, out connections);

        public bool TryGetRoadZone(GridCoordinate coordinate, out SuburbZone zone) =>
            m_roadZones.TryGetValue(coordinate, out zone);

        public bool IsPrimaryRoad(GridCoordinate coordinate) => m_primaryRoadCells.Contains(coordinate);

        public SuburbZone GetProgressionZone(Vector3 worldPosition) =>
            CalculateZone(GridToLocal(WorldToGrid(worldPosition)).y);

        public float GetEnemySpawnOpportunity(Vector3 worldPosition)
        {
            return GetProgressionZone(worldPosition) switch
            {
                SuburbZone.Start => 0f,
                SuburbZone.Easy => 0.15f,
                SuburbZone.Medium => 0.55f,
                SuburbZone.Hilly => 0.85f,
                _ => 1f
            };
        }

        public int GetStrictDestinationCountForStage(int stageIndex)
        {
            DeliveryDestinationQuery query = DeliveryDestinationQuery.ForSuburbsStage(stageIndex);
            int count = 0;
            for (int index = 0; index < m_deliveryDestinations.Count; index++)
            {
                DeliveryDestination destination = m_deliveryDestinations[index];
                if (destination != null && destination.CanReceiveDelivery &&
                    query.Matches(destination.RouteMetrics))
                {
                    count++;
                }
            }

            return count;
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
            return CalculateProgressiveElevation(local.x, local.y);
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
            m_sidewalksRoot = new GameObject("Sidewalks").transform;
            m_sidewalksRoot.SetParent(m_generatedRoot, false);
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
            if (GeneratedTerrain != null &&
                GeneratedTerrain.TryGetComponent(out Renderer terrainRenderer))
            {
                bounds = terrainRenderer.bounds;
                hasPoint = true;
            }

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
            m_elevationNoiseOffsetX = (float)random.NextDouble() * 1000f;
            m_elevationNoiseOffsetY = (float)random.NextDouble() * 1000f;
            if (!m_elevationEnabled)
            {
                return;
            }

            int spanX = m_blockWidth + 1;
            int spanY = m_blockHeight + 1;
            for (int blockY = 0; blockY < m_gridHeight; blockY++)
            {
                for (int blockX = 0; blockX < m_gridWidth; blockX++)
                {
                    float localX = m_minimumLocalX + blockX * spanX + spanX * 0.5f;
                    float localY = blockY * spanY + spanY * 0.5f;
                    m_blockElevations[blockX, blockY] = CalculateProgressiveElevation(localX, localY);
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
                        CalculateProgressiveElevation(localX, localY)));
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
                    AddRoad(LocalToGrid(localX, localY), CalculateProgressiveElevation(localX, localY));
                }
            }

            for (int boundary = 0; boundary <= m_gridHeight; boundary++)
            {
                int localY = boundary * (m_blockHeight + 1);
                for (int localX = m_minimumLocalX; localX <= maximumLocalX; localX++)
                {
                    AddRoad(LocalToGrid(localX, localY), CalculateProgressiveElevation(localX, localY));
                }
            }
        }

        private float CalculateProgressiveElevation(float localX, float localY)
        {
            if (!m_elevationEnabled || m_gridHeight <= 0)
            {
                return 0f;
            }

            int maximumLocalY = m_gridHeight * (m_blockHeight + 1);
            float flatRadius = Mathf.Max(1, m_progression.DepotFlatRadiusTiles);
            if (localY <= flatRadius)
            {
                return 0f;
            }

            float usableProgress = Mathf.InverseLerp(flatRadius, maximumLocalY, localY);
            float trend = Mathf.Pow(
                Mathf.Clamp01(usableProgress),
                Mathf.Max(0.5f, m_progression.ElevationTrendExponent));
            float maximum = m_maximumElevation * Mathf.Lerp(
                1f,
                Mathf.Max(1f, m_progression.OuterElevationMultiplier),
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    m_progression.HillyEndNormalized,
                    1f,
                    usableProgress)));
            float baseElevation = maximum * trend * 0.82f;

            float frequency = Mathf.Max(0.005f, m_progression.ElevationNoiseFrequency);
            float firstNoise = Mathf.PerlinNoise(
                m_elevationNoiseOffsetX + localX * frequency,
                m_elevationNoiseOffsetY + localY * frequency);
            float secondNoise = Mathf.PerlinNoise(
                m_elevationNoiseOffsetX * 0.37f + localX * frequency * 2.1f,
                m_elevationNoiseOffsetY * 0.37f + localY * frequency * 2.1f);
            float rollingHills = Mathf.Sin(
                (localY * frequency + m_elevationNoiseOffsetY * 0.01f) * Mathf.PI * 2f) * 0.4f;
            float signedNoise = (firstNoise - 0.5f) * 2f +
                                (secondNoise - 0.5f) * 0.55f +
                                rollingHills;
            float variation = m_progression.ElevationNoiseAmplitude *
                Mathf.SmoothStep(0f, 1f, usableProgress) * signedNoise;
            return Mathf.Clamp(baseElevation + variation, 0f, maximum);
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

        private void ResolveRoadElevations()
        {
            for (int index = 0; index < m_roadOrder.Count; index++)
            {
                GridCoordinate coordinate = m_roadOrder[index];
                Vector2Int local = GridToLocal(coordinate);
                m_roadZones[coordinate] = CalculateZone(local.y);
                if (local.x == 0)
                {
                    m_primaryRoadCells.Add(coordinate);
                }

                if (local.y <= m_progression.DepotFlatRadiusTiles)
                {
                    m_roadHeights[coordinate] = 0f;
                }
            }

            if (!m_roadCells.Contains(GridCoordinate.Zero))
            {
                return;
            }

            // First constrain the connected graph outward from the depot. This gives
            // every road a valid route even before cycle edges are relaxed below.
            Queue<GridCoordinate> frontier = new Queue<GridCoordinate>();
            HashSet<GridCoordinate> visited = new HashSet<GridCoordinate> { GridCoordinate.Zero };
            frontier.Enqueue(GridCoordinate.Zero);
            while (frontier.Count > 0)
            {
                GridCoordinate current = frontier.Dequeue();
                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    CardinalDirection direction = (CardinalDirection)directionIndex;
                    if ((m_logicalRoads[current] & DirectionConnection(direction)) == 0)
                    {
                        continue;
                    }

                    GridCoordinate neighbour = current + DirectionOffset(direction);
                    if (!m_roadCells.Contains(neighbour) || !visited.Add(neighbour))
                    {
                        continue;
                    }

                    float maximumRise = MaximumRoadRise(current, neighbour);
                    m_roadHeights[neighbour] = Mathf.Clamp(
                        m_roadHeights[neighbour],
                        m_roadHeights[current] - maximumRise,
                        m_roadHeights[current] + maximumRise);
                    frontier.Enqueue(neighbour);
                }
            }

            // Resolve cycle edges too. Lowering the higher endpoint preserves the
            // flat depot and avoids introducing artificial pits beside roads.
            for (int iteration = 0; iteration < 32; iteration++)
            {
                bool changed = false;
                for (int roadIndex = 0; roadIndex < m_roadOrder.Count; roadIndex++)
                {
                    GridCoordinate roadCoordinate = m_roadOrder[roadIndex];
                    RoadConnections roadConnections = m_logicalRoads[roadCoordinate];
                    for (int directionIndex = 0; directionIndex < 2; directionIndex++)
                    {
                        CardinalDirection direction = (CardinalDirection)directionIndex;
                        if ((roadConnections & DirectionConnection(direction)) == 0)
                        {
                            continue;
                        }

                        GridCoordinate neighbour = roadCoordinate + DirectionOffset(direction);
                        if (!m_roadHeights.ContainsKey(neighbour))
                        {
                            continue;
                        }

                        float maximumRise = MaximumRoadRise(roadCoordinate, neighbour);
                        float first = m_roadHeights[roadCoordinate];
                        float second = m_roadHeights[neighbour];
                        if (Mathf.Abs(first - second) <= maximumRise + 0.001f)
                        {
                            continue;
                        }

                        if (first > second)
                        {
                            m_roadHeights[roadCoordinate] = second + maximumRise;
                        }
                        else
                        {
                            m_roadHeights[neighbour] = first + maximumRise;
                        }

                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        private float MaximumRoadRise(GridCoordinate first, GridCoordinate second)
        {
            bool primaryConnection = m_primaryRoadCells.Contains(first) &&
                                     m_primaryRoadCells.Contains(second);
            float configuredSlope = primaryConnection
                ? m_progression.PrimaryRoadMaximumSlope
                : m_progression.ResidentialRoadMaximumSlope;
            float slope = Mathf.Min(m_maximumRoadSlope, Mathf.Max(1f, configuredSlope));
            return Mathf.Tan(slope * Mathf.Deg2Rad) * m_roadTileSize;
        }

        private void RefreshBlockElevationsFromRoads()
        {
            if (m_blockElevations == null)
            {
                return;
            }

            int spanX = m_blockWidth + 1;
            int spanY = m_blockHeight + 1;
            for (int blockY = 0; blockY < m_gridHeight; blockY++)
            {
                for (int blockX = 0; blockX < m_gridWidth; blockX++)
                {
                    float localX = m_minimumLocalX + blockX * spanX + spanX * 0.5f;
                    float localY = blockY * spanY + spanY * 0.5f;
                    m_blockElevations[blockX, blockY] = CalculateProgressiveElevation(localX, localY);
                }
            }
        }

        private SuburbZone CalculateZone(int localY)
        {
            if (localY <= m_progression.DepotFlatRadiusTiles)
            {
                return SuburbZone.Start;
            }

            float maximumLocalY = Mathf.Max(1, m_gridHeight * (m_blockHeight + 1));
            float progress = Mathf.Clamp01(localY / maximumLocalY);
            if (progress <= m_progression.EasyEndNormalized)
            {
                return SuburbZone.Easy;
            }

            if (progress <= m_progression.MediumEndNormalized)
            {
                return SuburbZone.Medium;
            }

            return progress <= m_progression.HillyEndNormalized
                ? SuburbZone.Hilly
                : SuburbZone.Outer;
        }

        private float GetMaximumConnectedGrade(GridCoordinate coordinate)
        {
            float maximum = 0f;
            if (!m_logicalRoads.TryGetValue(coordinate, out RoadConnections connections))
            {
                return maximum;
            }

            for (int directionIndex = 0; directionIndex < 4; directionIndex++)
            {
                CardinalDirection direction = (CardinalDirection)directionIndex;
                GridCoordinate neighbour = coordinate + DirectionOffset(direction);
                if ((connections & DirectionConnection(direction)) == 0 ||
                    !m_roadHeights.TryGetValue(neighbour, out float neighbourHeight))
                {
                    continue;
                }

                float rise = Mathf.Abs(neighbourHeight - m_roadHeights[coordinate]);
                maximum = Mathf.Max(maximum, Mathf.Atan2(rise, m_roadTileSize) * Mathf.Rad2Deg);
            }

            return maximum;
        }

        private void CreateRoadVisuals(System.Random random)
        {
            for (int index = 0; index < m_roadOrder.Count; index++)
            {
                GridCoordinate coordinate = m_roadOrder[index];
                RoadConnections connections = m_logicalRoads[coordinate];
                Quaternion surfaceRotation = CalculateRoadSurfaceRotation(coordinate);
                Vector3 roadPosition = GridToWorld(coordinate) +
                                       Vector3.up * m_terrainSettings.RoadSurfaceOffset;
                GameObject road = TryCreateRoadPrefab(
                    connections, roadPosition, surfaceRotation, random);
                if (road == null)
                {
                    road = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    road.transform.SetPositionAndRotation(roadPosition, surfaceRotation);
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
                generatedRoad.Initialise(
                    coordinate,
                    connections,
                    m_roadZones[coordinate],
                    m_primaryRoadCells.Contains(coordinate)
                        ? GeneratedRoadRole.Primary
                        : GeneratedRoadRole.Residential,
                    m_roadHeights[coordinate],
                    GetMaximumConnectedGrade(coordinate));
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
            float centre = m_roadHeights[coordinate];
            float west = m_roadHeights.TryGetValue(
                coordinate + new GridCoordinate(-1, 0), out float westHeight)
                ? westHeight
                : centre;
            float east = m_roadHeights.TryGetValue(
                coordinate + new GridCoordinate(1, 0), out float eastHeight)
                ? eastHeight
                : centre;
            float south = m_roadHeights.TryGetValue(
                coordinate + new GridCoordinate(0, -1), out float southHeight)
                ? southHeight
                : centre;
            float north = m_roadHeights.TryGetValue(
                coordinate + new GridCoordinate(0, 1), out float northHeight)
                ? northHeight
                : centre;
            float gradientX = (east - west) / (2f * m_roadTileSize);
            float gradientZ = (north - south) / (2f * m_roadTileSize);
            Vector3 normal = new Vector3(-gradientX, 1f, -gradientZ).normalized;
            return Quaternion.FromToRotation(Vector3.up, normal);
        }

        private void CreateContinuousTerrain()
        {
            float borderTiles = m_terrainSettings.MapBorder / m_roadTileSize;
            float minimumLocalX = m_minimumLocalX - borderTiles;
            float maximumLocalX = m_minimumLocalX +
                                  m_gridWidth * (m_blockWidth + 1) + borderTiles;
            float depotDepth = GeneratedStartingArea != null
                ? GeneratedStartingArea.GridFootprint.y
                : m_placeholderStartingAreaFootprint.y;
            float minimumLocalY = -(depotDepth + borderTiles);
            float maximumLocalY = m_gridHeight * (m_blockHeight + 1) + borderTiles;
            float widthMetres = (maximumLocalX - minimumLocalX) * m_roadTileSize;
            float depthMetres = (maximumLocalY - minimumLocalY) * m_roadTileSize;
            int vertexCountX = Mathf.Max(
                2,
                Mathf.CeilToInt(widthMetres / m_terrainSettings.VertexSpacing) + 1);
            int vertexCountZ = Mathf.Max(
                2,
                Mathf.CeilToInt(depthMetres / m_terrainSettings.VertexSpacing) + 1);
            float stepX = (maximumLocalX - minimumLocalX) / (vertexCountX - 1);
            float stepZ = (maximumLocalY - minimumLocalY) / (vertexCountZ - 1);

            GameObject terrainObject = new GameObject(
                "ContinuousTerrain",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(MeshCollider),
                typeof(GeneratedTerrainMesh));
            terrainObject.transform.SetParent(m_residentialGroundRoot, false);
            Vector3[] vertices = new Vector3[vertexCountX * vertexCountZ];
            Vector2[] uvs = new Vector2[vertices.Length];
            for (int z = 0; z < vertexCountZ; z++)
            {
                float localY = minimumLocalY + z * stepZ;
                for (int x = 0; x < vertexCountX; x++)
                {
                    float localX = minimumLocalX + x * stepX;
                    Vector3 flatWorldPosition = LocalCoordinatesToWorld(localX, localY, 0f);
                    float height = SampleContinuousTerrainHeight(
                        localX,
                        localY,
                        flatWorldPosition);
                    Vector3 worldPosition = LocalCoordinatesToWorld(localX, localY, height);
                    int vertexIndex = z * vertexCountX + x;
                    vertices[vertexIndex] = terrainObject.transform.InverseTransformPoint(worldPosition);
                    uvs[vertexIndex] = new Vector2(
                        localX * m_roadTileSize / m_terrainSettings.TextureScale,
                        localY * m_roadTileSize / m_terrainSettings.TextureScale);
                }
            }

            int[] triangles = new int[(vertexCountX - 1) * (vertexCountZ - 1) * 6];
            int triangleIndex = 0;
            for (int z = 0; z < vertexCountZ - 1; z++)
            {
                for (int x = 0; x < vertexCountX - 1; x++)
                {
                    int bottomLeft = z * vertexCountX + x;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertexCountX;
                    int topRight = topLeft + 1;
                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = topLeft;
                    triangles[triangleIndex++] = topRight;
                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = topRight;
                    triangles[triangleIndex++] = bottomRight;
                }
            }

            Mesh mesh = new Mesh
            {
                name = $"GeneratedTerrain_{CurrentSeed}",
                indexFormat = vertices.Length > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            MeshFilter meshFilter = terrainObject.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = terrainObject.GetComponent<MeshRenderer>();
            MeshCollider meshCollider = terrainObject.GetComponent<MeshCollider>();
            meshFilter.sharedMesh = mesh;
            Material runtimeMaterial = null;
            Material surfaceMaterial = m_terrainSettings.SurfaceMaterial;
            if (surfaceMaterial == null && m_residentialGroundPrefab != null)
            {
                Renderer prefabRenderer =
                    m_residentialGroundPrefab.GetComponentInChildren<Renderer>(true);
                if (prefabRenderer != null)
                {
                    surfaceMaterial = prefabRenderer.sharedMaterial;
                }
            }

            if (surfaceMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                                Shader.Find("Standard");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Generated Suburbs Terrain Material",
                        color = new Color(0.32f, 0.56f, 0.24f)
                    };
                    surfaceMaterial = runtimeMaterial;
                }
            }

            meshRenderer.sharedMaterial = surfaceMaterial;
            meshCollider.sharedMesh = mesh;
            GeneratedTerrain = terrainObject.GetComponent<GeneratedTerrainMesh>();
            GeneratedTerrain.Initialise(mesh, runtimeMaterial);
        }

        private void CreateSidewalks()
        {
            if (m_sidewalkWidth <= 0.001f)
            {
                return;
            }

            List<Vector3> vertices = m_sidewalkPrefab == null
                ? new List<Vector3>(m_roadCells.Count * 16)
                : null;
            List<int> triangles = m_sidewalkPrefab == null
                ? new List<int>(m_roadCells.Count * 24)
                : null;
            List<Vector2> uvs = m_sidewalkPrefab == null
                ? new List<Vector2>(m_roadCells.Count * 16)
                : null;
            int pieceIndex = 0;
            foreach (GridCoordinate roadCell in m_roadOrder)
            {
                Vector3 centre = GridToWorld(roadCell);
                int boundaryMask = 0;
                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    CardinalDirection direction = (CardinalDirection)directionIndex;
                    Vector3 outward = DirectionVector(direction);
                    GridCoordinate neighbour = roadCell + DirectionOffset(direction);
                    if (m_roadCells.Contains(neighbour))
                    {
                        continue;
                    }

                    boundaryMask |= 1 << directionIndex;

                    Vector3 tangent = Vector3.Cross(outward, Vector3.up);
                    float inner = m_roadTileSize * 0.5f;
                    float outer = inner + m_sidewalkWidth;
                    float along = m_roadTileSize * 0.5f;
                    AddSidewalkPiece(
                        centre + outward * inner - tangent * along,
                        centre + outward * inner + tangent * along,
                        centre + outward * outer + tangent * along,
                        centre + outward * outer - tangent * along,
                        vertices,
                        triangles,
                        uvs,
                        ref pieceIndex);
                }

                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    int nextDirection = (directionIndex + 1) % 4;
                    if ((boundaryMask & (1 << directionIndex)) == 0 ||
                        (boundaryMask & (1 << nextDirection)) == 0)
                    {
                        continue;
                    }

                    Vector3 first = DirectionVector((CardinalDirection)directionIndex);
                    Vector3 second = DirectionVector((CardinalDirection)nextDirection);
                    float inner = m_roadTileSize * 0.5f;
                    float outer = inner + m_sidewalkWidth;
                    AddSidewalkPiece(
                        centre + first * inner + second * inner,
                        centre + first * outer + second * inner,
                        centre + first * outer + second * outer,
                        centre + first * inner + second * outer,
                        vertices,
                        triangles,
                        uvs,
                        ref pieceIndex);
                }
            }

            if (m_sidewalkPrefab != null || vertices == null || vertices.Count == 0)
            {
                return;
            }

            GameObject sidewalkObject = new GameObject(
                "GeneratedSidewalks",
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(MeshCollider),
                typeof(GeneratedTerrainMesh));
            sidewalkObject.transform.SetParent(m_sidewalksRoot, false);
            Mesh mesh = new Mesh
            {
                name = $"GeneratedSidewalks_{CurrentSeed}",
                indexFormat = vertices.Count > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            sidewalkObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            sidewalkObject.GetComponent<MeshCollider>().sharedMesh = mesh;
            Material runtimeMaterial = null;
            Material material = m_sidewalkMaterial;
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                                Shader.Find("Standard");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader)
                    {
                        name = "Generated Sidewalk Material",
                        color = new Color(0.56f, 0.57f, 0.55f)
                    };
                    material = runtimeMaterial;
                }
            }

            sidewalkObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            sidewalkObject.GetComponent<GeneratedTerrainMesh>().Initialise(mesh, runtimeMaterial);
        }

        private void AddSidewalkPiece(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector2> uvs,
            ref int pieceIndex)
        {
            SetSidewalkHeight(ref first);
            SetSidewalkHeight(ref second);
            SetSidewalkHeight(ref third);
            SetSidewalkHeight(ref fourth);
            if (Vector3.Cross(second - first, third - first).y < 0f)
            {
                (second, fourth) = (fourth, second);
            }

            if (m_sidewalkPrefab != null)
            {
                CreateSidewalkPrefabPiece(first, second, third, fourth, pieceIndex++);
                return;
            }

            int vertex = vertices.Count;
            vertices.Add(m_sidewalksRoot.InverseTransformPoint(first));
            vertices.Add(m_sidewalksRoot.InverseTransformPoint(second));
            vertices.Add(m_sidewalksRoot.InverseTransformPoint(third));
            vertices.Add(m_sidewalksRoot.InverseTransformPoint(fourth));
            triangles.Add(vertex);
            triangles.Add(vertex + 1);
            triangles.Add(vertex + 2);
            triangles.Add(vertex);
            triangles.Add(vertex + 2);
            triangles.Add(vertex + 3);
            float uvScale = 1f / Mathf.Max(0.1f, m_sidewalkWidth);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, uvScale));
            uvs.Add(new Vector2(0f, uvScale));
            pieceIndex++;
        }

        private void CreateSidewalkPrefabPiece(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            int pieceIndex)
        {
            Vector3 centre = (first + second + third + fourth) * 0.25f;
            Vector3 lengthDirection = ((second + third) - (first + fourth)).normalized;
            Vector3 widthDirection = ((third + fourth) - (first + second)).normalized;
            Vector3 normal = Vector3.Cross(widthDirection, lengthDirection).normalized;
            if (normal.y < 0f)
            {
                normal = -normal;
            }

            GameObject piece = Instantiate(
                m_sidewalkPrefab,
                centre,
                Quaternion.LookRotation(Vector3.ProjectOnPlane(widthDirection, normal), normal),
                m_sidewalksRoot);
            piece.name = $"Sidewalk_{pieceIndex:000}";
            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
            if (!TryCalculateRendererBounds(renderers, out Bounds bounds))
            {
                return;
            }

            float targetX = 0.5f * (
                Vector3.Distance(first, second) + Vector3.Distance(fourth, third));
            float targetZ = 0.5f * (
                Vector3.Distance(first, fourth) + Vector3.Distance(second, third));
            float renderedX = ProjectBoundsSizeOntoAxis(bounds.size, piece.transform.right);
            float renderedZ = ProjectBoundsSizeOntoAxis(bounds.size, piece.transform.forward);
            Vector3 scale = piece.transform.localScale;
            if (renderedX > 0.001f)
            {
                scale.x *= targetX / renderedX;
            }

            if (renderedZ > 0.001f)
            {
                scale.z *= targetZ / renderedZ;
            }

            piece.transform.localScale = scale;
        }

        private void SetSidewalkHeight(ref Vector3 position)
        {
            position.y = SamplePlacementTerrainHeight(position) + m_sidewalkSurfaceOffset;
        }

        private static Vector3 DirectionVector(CardinalDirection direction)
        {
            GridCoordinate offset = DirectionOffset(direction);
            return new Vector3(offset.X, 0f, offset.Y);
        }

        private float SampleContinuousTerrainHeight(
            float localX,
            float localY,
            Vector3 flatWorldPosition)
        {
            float height = CalculateProgressiveElevation(localX, localY);
            height = BlendHousePads(height, flatWorldPosition);
            return ApplyDepotAndRoadHeights(height, localX, localY, flatWorldPosition);
        }

        private float SampleTerrainHeightWithoutHousePads(
            float localX,
            float localY,
            Vector3 flatWorldPosition)
        {
            float height = CalculateProgressiveElevation(localX, localY);
            return ApplyDepotAndRoadHeights(height, localX, localY, flatWorldPosition);
        }

        private float ApplyDepotAndRoadHeights(
            float height,
            float localX,
            float localY,
            Vector3 flatWorldPosition)
        {
            height = BlendDepotPad(height, flatWorldPosition);
            if (TrySampleRoadCorridor(
                    localX,
                    localY,
                    flatWorldPosition,
                    out float roadHeight,
                    out float distanceToRoad))
            {
                float roadEdge = m_terrainSettings.RoadHalfWidth;
                float shoulderEdge = roadEdge + m_terrainSettings.RoadShoulderWidth;
                if (distanceToRoad <= roadEdge)
                {
                    height = roadHeight;
                }
                else if (distanceToRoad < shoulderEdge && shoulderEdge > roadEdge)
                {
                    float blend = Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(roadEdge, shoulderEdge, distanceToRoad));
                    height = Mathf.Lerp(roadHeight, height, blend);
                }
            }

            return height;
        }

        private float BlendHousePads(float terrainHeight, Vector3 flatWorldPosition)
        {
            float result = terrainHeight;
            for (int index = 0; index < m_generatedProperties.Count; index++)
            {
                GeneratedProperty property = m_generatedProperties[index];
                if (property == null)
                {
                    continue;
                }

                Vector3 right = Vector3.ProjectOnPlane(property.transform.right, Vector3.up).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(property.transform.forward, Vector3.up).normalized;
                Vector3 offset = flatWorldPosition - property.transform.position;
                float halfWidth = property.GridFootprint.x * m_roadTileSize * 0.5f;
                float halfDepth = property.GridFootprint.y * m_roadTileSize * 0.5f;
                float outsideX = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(offset, right)) - halfWidth);
                float outsideZ = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(offset, forward)) - halfDepth);
                float distance = Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
                if (distance > m_terrainSettings.HousePadBlendWidth)
                {
                    continue;
                }

                float influence = m_terrainSettings.HousePadBlendWidth <= 0.001f
                    ? 1f
                    : 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        distance / m_terrainSettings.HousePadBlendWidth);
                Vector3 normal = property.transform.up;
                float propertyWorldHeight = property.transform.position.y;
                if (Mathf.Abs(normal.y) > 0.001f)
                {
                    propertyWorldHeight -=
                        (normal.x * offset.x + normal.z * offset.z) / normal.y;
                }

                float propertyHeight = propertyWorldHeight - m_gridOrigin.y;
                result = Mathf.Lerp(result, propertyHeight, influence);
            }

            return result;
        }

        private float BlendDepotPad(float terrainHeight, Vector3 flatWorldPosition)
        {
            if (GeneratedStartingArea == null)
            {
                return terrainHeight;
            }

            Vector3 sample = flatWorldPosition;
            sample.y = GeneratedStartingArea.transform.position.y;
            Vector3 local = GeneratedStartingArea.transform.InverseTransformPoint(sample);
            float halfWidth = GeneratedStartingArea.GridFootprint.x * m_roadTileSize * 0.5f;
            float halfDepth = GeneratedStartingArea.GridFootprint.y * m_roadTileSize * 0.5f;
            float outsideX = Mathf.Max(0f, Mathf.Abs(local.x) - halfWidth);
            float outsideZ = Mathf.Max(0f, Mathf.Abs(local.z) - halfDepth);
            float distance = Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
            if (distance > m_terrainSettings.DepotBlendWidth)
            {
                return terrainHeight;
            }

            float influence = m_terrainSettings.DepotBlendWidth <= 0.001f
                ? 1f
                : 1f - Mathf.SmoothStep(
                    0f,
                    1f,
                    distance / m_terrainSettings.DepotBlendWidth);
            float depotHeight = GeneratedStartingArea.transform.position.y - m_gridOrigin.y;
            return Mathf.Lerp(terrainHeight, depotHeight, influence);
        }

        private bool TrySampleRoadCorridor(
            float localX,
            float localY,
            Vector3 flatWorldPosition,
            out float roadHeight,
            out float distanceToRoad)
        {
            roadHeight = 0f;
            distanceToRoad = float.PositiveInfinity;
            float influenceDistance = m_terrainSettings.RoadHalfWidth +
                                      m_terrainSettings.RoadShoulderWidth;
            int searchRadius = Mathf.CeilToInt(influenceDistance / m_roadTileSize) + 1;
            int centreX = Mathf.RoundToInt(localX);
            int centreY = Mathf.RoundToInt(localY);
            for (int y = centreY - searchRadius; y <= centreY + searchRadius; y++)
            {
                for (int x = centreX - searchRadius; x <= centreX + searchRadius; x++)
                {
                    GridCoordinate coordinate = LocalToGrid(x, y);
                    if (!m_logicalRoads.TryGetValue(coordinate, out RoadConnections connections))
                    {
                        continue;
                    }

                    Vector3 first = GridToWorld(coordinate);
                    TryUseRoadSample(flatWorldPosition, first, first, ref roadHeight, ref distanceToRoad);
                    for (int directionIndex = 0; directionIndex < 2; directionIndex++)
                    {
                        CardinalDirection direction = (CardinalDirection)directionIndex;
                        if ((connections & DirectionConnection(direction)) == 0)
                        {
                            continue;
                        }

                        GridCoordinate neighbour = coordinate + DirectionOffset(direction);
                        if (m_roadHeights.ContainsKey(neighbour))
                        {
                            TryUseRoadSample(
                                flatWorldPosition,
                                first,
                                GridToWorld(neighbour),
                                ref roadHeight,
                                ref distanceToRoad);
                        }
                    }
                }
            }

            return distanceToRoad <= influenceDistance;
        }

        private void TryUseRoadSample(
            Vector3 sample,
            Vector3 first,
            Vector3 second,
            ref float selectedHeight,
            ref float selectedDistance)
        {
            Vector3 firstFlat = first;
            Vector3 secondFlat = second;
            firstFlat.y = 0f;
            secondFlat.y = 0f;
            sample.y = 0f;
            Vector3 segment = secondFlat - firstFlat;
            float t = segment.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(sample - firstFlat, segment) / segment.sqrMagnitude)
                : 0f;
            Vector3 closest = Vector3.Lerp(firstFlat, secondFlat, t);
            float distance = Vector3.Distance(sample, closest);
            if (distance >= selectedDistance)
            {
                return;
            }

            selectedDistance = distance;
            selectedHeight = Mathf.Lerp(first.y, second.y, t) - m_gridOrigin.y;
        }

        private Vector3 LocalCoordinatesToWorld(float localX, float localY, float height)
        {
            GridCoordinate rightOffset = DirectionOffset(TurnRight(m_startDirection));
            GridCoordinate forwardOffset = DirectionOffset(m_startDirection);
            Vector3 right = new Vector3(rightOffset.X, 0f, rightOffset.Y);
            Vector3 forward = new Vector3(forwardOffset.X, 0f, forwardOffset.Y);
            return m_gridOrigin +
                   right * (localX * m_roadTileSize) +
                   forward * (localY * m_roadTileSize) +
                   Vector3.up * height;
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
                    float halfWidth = m_blockWidth * 0.5f;
                    float halfDepth = m_blockHeight * 0.5f;
                    float leftHeight = CalculateProgressiveElevation(localCentreX - halfWidth, localCentreY);
                    float rightHeight = CalculateProgressiveElevation(localCentreX + halfWidth, localCentreY);
                    float backHeight = CalculateProgressiveElevation(localCentreX, localCentreY - halfDepth);
                    float frontHeight = CalculateProgressiveElevation(localCentreX, localCentreY + halfDepth);
                    Vector3 rightTangent = right * (m_blockWidth * m_roadTileSize) +
                                           Vector3.up * (rightHeight - leftHeight);
                    Vector3 forwardTangent = forward * (m_blockHeight * m_roadTileSize) +
                                             Vector3.up * (frontHeight - backHeight);
                    Vector3 normal = Vector3.Cross(forwardTangent, rightTangent).normalized;
                    Quaternion rotation = Quaternion.LookRotation(
                        Vector3.ProjectOnPlane(forwardTangent, normal).normalized,
                        normal);

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
                    int targetLots = CalculateLotsForBlock(
                        blockX,
                        blockY,
                        horizontalSpan,
                        verticalSpan);
                    if (targetLots <= 0)
                    {
                        continue;
                    }

                    if (targetLots == 1)
                    {
                        if (TryGenerateCentredSingleLot(
                                random,
                                minimumX,
                                maximumX,
                                minimumY,
                                maximumY,
                                houseIndex))
                        {
                            houseIndex++;
                        }

                        continue;
                    }

                    List<PropertyCandidate> candidates = CreatePropertyCandidates(
                        minimumX, maximumX, minimumY, maximumY);
                    Shuffle(candidates, random);

                    int housesInBlock = 0;
                    for (int candidateIndex = 0;
                         candidateIndex < candidates.Count && housesInBlock < targetLots;
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

                        Vector3 centre = CalculateCellCentre(cells);
                        if (!TrySpawnLot(
                                lotDefinition,
                                prefab,
                                candidate,
                                footprint,
                                centre,
                                m_housesRoot,
                                $"Lot_{houseIndex:000}"))
                        {
                            continue;
                        }

                        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                        {
                            m_propertyCells.Add(cells[cellIndex]);
                        }

                        housesInBlock++;
                        houseIndex++;
                    }
                }
            }
        }

        private int CalculateLotsForBlock(
            int blockX,
            int blockY,
            int horizontalSpan,
            int verticalSpan)
        {
            int minimumLots = Mathf.Clamp(m_minimumHousesPerBlock, 0, m_housesPerBlock);
            if (minimumLots == m_housesPerBlock)
            {
                return minimumLots;
            }

            float centreX = m_minimumLocalX + blockX * horizontalSpan +
                            1f + (m_blockWidth - 1f) * 0.5f;
            float centreY = blockY * verticalSpan +
                            1f + (m_blockHeight - 1f) * 0.5f;
            float firstCentreX = m_minimumLocalX + 1f + (m_blockWidth - 1f) * 0.5f;
            float lastCentreX = m_minimumLocalX + (m_gridWidth - 1) * horizontalSpan +
                                1f + (m_blockWidth - 1f) * 0.5f;
            float furthestX = Mathf.Max(Mathf.Abs(firstCentreX), Mathf.Abs(lastCentreX));
            float furthestY = (m_gridHeight - 1) * verticalSpan +
                              1f + (m_blockHeight - 1f) * 0.5f;
            float maximumDistance = Mathf.Sqrt(furthestX * furthestX + furthestY * furthestY);
            float distance = Mathf.Sqrt(centreX * centreX + centreY * centreY);
            float progression = maximumDistance > 0.001f
                ? Mathf.Clamp01(distance / maximumDistance)
                : 0f;
            return Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Lerp(m_housesPerBlock, minimumLots, progression)),
                minimumLots,
                m_housesPerBlock);
        }

        private bool TryGenerateCentredSingleLot(
            System.Random random,
            int minimumX,
            int maximumX,
            int minimumY,
            int maximumY,
            int houseIndex)
        {
            if (m_selectedRegion != null && random.NextDouble() > m_selectedRegion.LotDensity)
            {
                return false;
            }

            List<PropertyCandidate> candidates = CreatePropertyCandidates(
                minimumX, maximumX, minimumY, maximumY);
            if (candidates.Count == 0)
            {
                return false;
            }

            PropertyCandidate candidate = candidates[0];
            float nearestRoadDistance = HorizontalSqrDistance(GridToWorld(candidate.Road), m_gridOrigin);
            for (int index = 1; index < candidates.Count; index++)
            {
                float roadDistance = HorizontalSqrDistance(GridToWorld(candidates[index].Road), m_gridOrigin);
                if (roadDistance < nearestRoadDistance)
                {
                    candidate = candidates[index];
                    nearestRoadDistance = roadDistance;
                }
            }

            List<GridCoordinate> cells = new List<GridCoordinate>(m_blockWidth * m_blockHeight);
            for (int localY = minimumY; localY <= maximumY; localY++)
            {
                for (int localX = minimumX; localX <= maximumX; localX++)
                {
                    cells.Add(LocalToGrid(localX, localY));
                }
            }

            if (!CanPlaceProperty(cells, minimumX, maximumX, minimumY, maximumY))
            {
                return false;
            }

            LotDefinition lotDefinition = SelectLotDefinition(random, false);
            if (m_selectedRegion != null && lotDefinition == null)
            {
                return false;
            }

            GameObject fallbackPrefab = lotDefinition != null
                ? ValidateLotPrefab(lotDefinition.Prefab)
                : SelectHousePrefab(random);
            GameObject mansionPrefab = ValidateLotPrefab(m_mansionPrefab);
            GameObject prefab = mansionPrefab != null ? mansionPrefab : fallbackPrefab;
            bool roadOnFrontOrBack =
                candidate.PropertyToRoad == m_startDirection ||
                candidate.PropertyToRoad == Opposite(m_startDirection);
            Vector2Int footprint = roadOnFrontOrBack
                ? new Vector2Int(m_blockWidth, m_blockHeight)
                : new Vector2Int(m_blockHeight, m_blockWidth);
            if (!TrySpawnLot(
                    lotDefinition,
                    prefab,
                    candidate,
                    footprint,
                    CalculateCellCentre(cells),
                    m_housesRoot,
                    $"Lot_{houseIndex:000}",
                    true))
            {
                return false;
            }

            for (int index = 0; index < cells.Count; index++)
            {
                m_propertyCells.Add(cells[index]);
            }

            return true;
        }

        private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return x * x + z * z;
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

                if (!TrySpawnLot(
                        definition,
                        prefab,
                        candidate,
                        footprint,
                        CalculateCellCentre(cells),
                        m_landmarksRoot,
                        $"Landmark_{GeneratedLandmarkCount:000}"))
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    m_propertyCells.Add(cells[cellIndex]);
                }

                GeneratedLandmarkCount++;
            }
        }

        private bool TrySpawnLot(
            LotDefinition definition,
            GameObject prefab,
            PropertyCandidate candidate,
            Vector2Int footprint,
            Vector3 centre,
            Transform parent,
            string instanceName,
            bool centreInLot = false)
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

            SuburbZone zone = m_roadZones.TryGetValue(candidate.Road, out SuburbZone roadZone)
                ? roadZone
                : CalculateZone(GridToLocal(candidate.Road).y);
            float roadElevation = m_roadHeights.TryGetValue(
                candidate.Road,
                out float sampledRoadElevation)
                ? sampledRoadElevation
                : 0f;
            generatedProperty.Initialise(
                candidate.Anchor,
                candidate.Road,
                footprint,
                roadConnection,
                deliveryPoint,
                zone,
                roadElevation);
            FacePropertyTowardsRoad(property.transform, candidate.Road);
            Renderer[] renderers = property.GetComponentsInChildren<Renderer>(true);
            Vector2Int visualFootprint = centreInLot && prefab != m_mansionPrefab
                ? GetLotFootprint(definition, prefab)
                : footprint;
            FitPropertyVisualToFootprint(generatedProperty, visualFootprint, renderers);
            if (!TryApplyHouseSetback(
                    generatedProperty,
                    footprint,
                    candidate,
                    renderers,
                    centreInLot))
            {
                DiscardRejectedProperty(property);
                return false;
            }

            if (!TryAlignPropertyToTerrain(
                    generatedProperty,
                    footprint,
                    candidate.Road,
                    renderers) ||
                !TryCreateRenderedFootprint(
                    generatedProperty,
                    renderers,
                    out OrientedFootprint renderedFootprint) ||
                !CanPlaceRenderedFootprint(renderedFootprint) ||
                !TryCreateDrivewayReservation(
                    generatedProperty,
                    renderedFootprint,
                    out OrientedFootprint drivewayFootprint,
                    out Vector3 drivewayStart,
                    out Vector3 drivewayEnd))
            {
                DiscardRejectedProperty(property);
                return false;
            }

            m_placedHouseFootprints.Add(renderedFootprint);
            m_reservedDrivewayFootprints.Add(drivewayFootprint);
            generatedProperty.Initialise(
                candidate.Anchor,
                candidate.Road,
                footprint,
                roadConnection,
                deliveryPoint,
                zone,
                roadElevation);
            ConfigurePropertyConnectionPoints(
                generatedProperty,
                drivewayStart,
                drivewayEnd,
                renderedFootprint.Forward);
            generatedProperty.SetDrivewayReservation(
                new Vector3(
                    renderedFootprint.Forward.x,
                    0f,
                    renderedFootprint.Forward.y),
                drivewayStart,
                drivewayEnd,
                m_drivewayWidth,
                m_drivewayClearance);
            generatedProperty.SetAccessMetadata(GridToWorld(candidate.Road));
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

            // A centred, single-property block is the generator's logical mansion lot.
            // It remains a mansion destination while placeholder/standard art is used,
            // and automatically picks up dedicated mansion art once assigned.
            RegisterDeliveryDestination(property, generatedProperty, definition,
                centreInLot || (m_mansionPrefab != null && prefab == m_mansionPrefab));
            return true;
        }

        private void RegisterDeliveryDestination(
            GameObject property,
            GeneratedProperty generatedProperty,
            LotDefinition definition,
            bool isMansion)
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
            DeliveryDestinationType destinationType = isMansion
                ? DeliveryDestinationType.Mansion
                : lotType switch
            {
                LotType.Commercial => DeliveryDestinationType.Commercial,
                LotType.Landmark => DeliveryDestinationType.Landmark,
                LotType.Special => DeliveryDestinationType.Special,
                _ => DeliveryDestinationType.Standard
            };
            destination.Initialise(
                generatedProperty.DeliveryPoint,
                destinationType,
                isMansion
                    ? "Mansion"
                    : definition != null ? definition.DestinationDisplayName : "House",
                definition != null ? definition.DeliveryDifficultyModifier : 1f,
                definition != null ? definition.DeliveryRewardModifier : 1f,
                true);
            m_deliveryDestinations.Add(destination);
        }

        private void AnalyseDeliveryDestinations()
        {
            IsDeliveryDifficultyReady = false;
            Dictionary<GridCoordinate, GridCoordinate> previous =
                new Dictionary<GridCoordinate, GridCoordinate>();
            Dictionary<GridCoordinate, int> distanceInTiles =
                new Dictionary<GridCoordinate, int>();
            Queue<GridCoordinate> frontier = new Queue<GridCoordinate>();

            if (!m_logicalRoads.ContainsKey(GridCoordinate.Zero))
            {
                Debug.LogWarning("Delivery difficulty could not find the depot road tile.", this);
                return;
            }

            distanceInTiles[GridCoordinate.Zero] = 0;
            frontier.Enqueue(GridCoordinate.Zero);
            while (frontier.Count > 0)
            {
                GridCoordinate current = frontier.Dequeue();
                RoadConnections connections = m_logicalRoads[current];
                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    CardinalDirection direction = (CardinalDirection)directionIndex;
                    if ((connections & DirectionConnection(direction)) == 0)
                    {
                        continue;
                    }

                    GridCoordinate neighbour = current + DirectionOffset(direction);
                    if (!m_logicalRoads.ContainsKey(neighbour) || distanceInTiles.ContainsKey(neighbour))
                    {
                        continue;
                    }

                    distanceInTiles[neighbour] = distanceInTiles[current] + 1;
                    previous[neighbour] = current;
                    frontier.Enqueue(neighbour);
                }
            }

            int reachableCount = 0;
            int tutorialCount = 0;
            int stageTwoCount = 0;
            int stageThreeCount = 0;
            for (int index = 0; index < m_deliveryDestinations.Count; index++)
            {
                DeliveryDestination destination = m_deliveryDestinations[index];
                GeneratedProperty property = destination != null
                    ? destination.GetComponent<GeneratedProperty>()
                    : null;
                if (destination == null || property == null ||
                    !distanceInTiles.ContainsKey(property.RoadCoordinate))
                {
                    destination?.SetRouteMetrics(CreateUnreachableMetrics(
                        property != null ? property.RoadCoordinate : GridCoordinate.Zero));
                    continue;
                }

                List<GridCoordinate> path = ReconstructRoadPath(property.RoadCoordinate, previous);
                DeliveryRouteMetrics metrics = CalculateDeliveryRouteMetrics(destination, property, path);
                destination.SetRouteMetrics(metrics);
                reachableCount++;
                tutorialCount += DeliveryDestinationQuery.ForSuburbsStage(0).Matches(metrics) ? 1 : 0;
                stageTwoCount += DeliveryDestinationQuery.ForSuburbsStage(1).Matches(metrics) ? 1 : 0;
                stageThreeCount += DeliveryDestinationQuery.ForSuburbsStage(2).Matches(metrics) ? 1 : 0;
            }

            IsDeliveryDifficultyReady = reachableCount > 0;
            if (tutorialCount == 0 || stageTwoCount == 0 || stageThreeCount == 0)
            {
                Debug.LogWarning(
                    $"Seed {CurrentSeed} has sparse strict delivery pools: tutorial={tutorialCount}, " +
                    $"stage2={stageTwoCount}, stage3={stageThreeCount}. " +
                    "Deterministic relaxed selection will be used where required.",
                    this);
            }
        }

        private static List<GridCoordinate> ReconstructRoadPath(
            GridCoordinate destination,
            Dictionary<GridCoordinate, GridCoordinate> previous)
        {
            List<GridCoordinate> path = new List<GridCoordinate> { destination };
            GridCoordinate current = destination;
            while (current != GridCoordinate.Zero && previous.TryGetValue(current, out GridCoordinate parent))
            {
                current = parent;
                path.Add(current);
            }

            path.Reverse();
            return path;
        }

        private DeliveryRouteMetrics CalculateDeliveryRouteMetrics(
            DeliveryDestination destination,
            GeneratedProperty property,
            List<GridCoordinate> path)
        {
            float routeLength = Mathf.Max(0, path.Count - 1) * m_roadTileSize;
            float elevationGain = 0f;
            float maximumGrade = 0f;
            int turnCount = 0;
            int intersectionCount = 0;
            GridCoordinate previousDirection = GridCoordinate.Zero;

            for (int index = 0; index < path.Count; index++)
            {
                if (CountConnections(m_logicalRoads[path[index]]) >= 3)
                {
                    intersectionCount++;
                }

                if (index == 0)
                {
                    continue;
                }

                float rise = m_roadHeights[path[index]] - m_roadHeights[path[index - 1]];
                elevationGain += Mathf.Max(0f, rise);
                maximumGrade = Mathf.Max(
                    maximumGrade,
                    Mathf.Atan2(Mathf.Abs(rise), m_roadTileSize) * Mathf.Rad2Deg);
                GridCoordinate direction = path[index] - path[index - 1];
                if (index > 1 && direction != previousDirection)
                {
                    turnCount++;
                }

                previousDirection = direction;
            }

            Vector3 depotPosition = GeneratedStartingArea.RoadConnection.position;
            Vector3 destinationPosition = destination.DropPosition.position;
            float straightLineDistance = HorizontalDistance(depotPosition, destinationPosition);
            Vector3 roadConnectionPosition = GridToWorld(property.RoadCoordinate);
            float finalCarryDistance = HorizontalDistance(roadConnectionPosition, destinationPosition);
            float finalCarryElevationGain = Mathf.Max(0f, destinationPosition.y - roadConnectionPosition.y);

            float weightedDifficulty =
                Mathf.Clamp01(routeLength / m_deliveryDifficulty.LongRouteDistance) *
                    m_deliveryDifficulty.RouteDistanceWeight +
                Mathf.Clamp01(straightLineDistance / m_deliveryDifficulty.LongDirectDistance) *
                    m_deliveryDifficulty.DirectDistanceWeight +
                Mathf.Clamp01(elevationGain / m_deliveryDifficulty.HighElevationGain) *
                    m_deliveryDifficulty.ElevationGainWeight +
                Mathf.Clamp01(maximumGrade / m_deliveryDifficulty.SteepRoadGrade) *
                    m_deliveryDifficulty.MaximumGradeWeight +
                Mathf.Clamp01(turnCount / (float)m_deliveryDifficulty.ManyTurns) *
                    m_deliveryDifficulty.TurnWeight +
                Mathf.Clamp01(intersectionCount / (float)m_deliveryDifficulty.ManyIntersections) *
                    m_deliveryDifficulty.IntersectionWeight +
                Mathf.Clamp01(finalCarryDistance / m_deliveryDifficulty.LongFinalCarry) *
                    m_deliveryDifficulty.FinalCarryWeight +
                Mathf.Clamp01(finalCarryElevationGain / m_deliveryDifficulty.HighFinalCarryElevation) *
                    m_deliveryDifficulty.FinalCarryElevationWeight +
                ((int)property.Zone / (float)(int)SuburbZone.Outer) *
                    m_deliveryDifficulty.ProgressionZoneWeight;
            float score = 100f * destination.DifficultyModifier *
                          weightedDifficulty / m_deliveryDifficulty.TotalWeight;
            score = Mathf.Clamp(score, 0f, 100f);

            DeliveryDifficultyZone zone = ClassifyDifficultyZone(
                routeLength,
                elevationGain,
                maximumGrade,
                finalCarryDistance,
                score);
            return new DeliveryRouteMetrics(
                true,
                property.RoadCoordinate,
                routeLength,
                straightLineDistance,
                elevationGain,
                maximumGrade,
                turnCount,
                intersectionCount,
                finalCarryDistance,
                finalCarryElevationGain,
                score,
                zone,
                property.Zone,
                destinationPosition.y - m_gridOrigin.y,
                property.VanAccessible);
        }

        private static DeliveryRouteMetrics CreateUnreachableMetrics(GridCoordinate roadCoordinate) =>
            new DeliveryRouteMetrics(
                false, roadCoordinate, 0f, 0f, 0f, 0f, 0, 0, 0f, 0f, 100f,
                DeliveryDifficultyZone.Final, SuburbZone.Outer, 0f, false);

        private DeliveryDifficultyZone ClassifyDifficultyZone(
            float routeLength,
            float elevationGain,
            float maximumGrade,
            float finalCarryDistance,
            float score)
        {
            if (routeLength <= m_roadTileSize)
            {
                return DeliveryDifficultyZone.Depot;
            }

            if (routeLength <= 96f && elevationGain <= 4f &&
                maximumGrade <= 18f && finalCarryDistance <= 32f)
            {
                return DeliveryDifficultyZone.Easy;
            }

            if (elevationGain >= 10f || maximumGrade >= 22f)
            {
                return score >= 72f ? DeliveryDifficultyZone.Difficult : DeliveryDifficultyZone.Hilly;
            }

            if (score < 42f)
            {
                return DeliveryDifficultyZone.Medium;
            }

            if (score < 72f)
            {
                return DeliveryDifficultyZone.Difficult;
            }

            return DeliveryDifficultyZone.Final;
        }

        private static int CountConnections(RoadConnections connections)
        {
            int count = 0;
            for (int bit = 0; bit < 4; bit++)
            {
                count += ((int)connections & (1 << bit)) != 0 ? 1 : 0;
            }

            return count;
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
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
                AddPropertyCandidateIfRoad(candidates, new PropertyCandidate(
                    LocalToGrid(localX, minimumY),
                    LocalToGrid(localX, minimumY - 1),
                    Opposite(m_startDirection)));
                AddPropertyCandidateIfRoad(candidates, new PropertyCandidate(
                    LocalToGrid(localX, maximumY),
                    LocalToGrid(localX, maximumY + 1),
                    m_startDirection));
            }

            CardinalDirection right = TurnRight(m_startDirection);
            for (int localY = minimumY; localY <= maximumY; localY++)
            {
                AddPropertyCandidateIfRoad(candidates, new PropertyCandidate(
                    LocalToGrid(minimumX, localY),
                    LocalToGrid(minimumX - 1, localY),
                    Opposite(right)));
                AddPropertyCandidateIfRoad(candidates, new PropertyCandidate(
                    LocalToGrid(maximumX, localY),
                    LocalToGrid(maximumX + 1, localY),
                    right));
            }

            return candidates;
        }

        private void AddPropertyCandidateIfRoad(
            List<PropertyCandidate> candidates,
            PropertyCandidate candidate)
        {
            if (m_roadCells.Contains(candidate.Road))
            {
                candidates.Add(candidate);
            }
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
                // Runtime render-mesh collection requires every imported mesh to have
                // Read/Write enabled. The generated roads, ground and building bounds
                // already have colliders, so collider geometry is both build-safe and
                // considerably cheaper than reading decorative house meshes.
                m_navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
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

            property.rotation = Quaternion.LookRotation(directionToRoad.normalized, Vector3.up);
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
            }
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

        private void FitPropertyVisualToFootprint(
            GeneratedProperty property,
            Vector2Int footprint,
            Renderer[] renderers)
        {
            if (property == null || !property.FitVisualToFootprint || property.VisualRoot == null)
            {
                return;
            }

            Vector3 right = Vector3.ProjectOnPlane(property.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(property.transform.forward, Vector3.up).normalized;
            if (!TryCalculateProjectedRendererFootprint(
                    renderers,
                    right,
                    forward,
                    out _,
                    out Vector2 renderedHalfSize))
            {
                return;
            }

            float availableWidth = Mathf.Min(
                footprint.x * m_roadTileSize * m_houseFootprintFill,
                Mathf.Max(0.1f, footprint.x * m_roadTileSize - m_minimumHouseClearance * 2f));
            float availableDepth = Mathf.Min(
                footprint.y * m_roadTileSize * m_houseFootprintFill,
                Mathf.Max(
                    0.1f,
                    footprint.y * m_roadTileSize -
                    m_sidewalkWidth -
                    m_houseSetbackFromSidewalk -
                    m_minimumHouseClearance));
            float uniformScale = Mathf.Min(
                availableWidth / (renderedHalfSize.x * 2f),
                availableDepth / (renderedHalfSize.y * 2f));
            property.VisualRoot.localScale *= uniformScale;

            if (!TryCalculateRendererBounds(renderers, out Bounds bounds) ||
                !TryCalculateProjectedRendererFootprint(
                    renderers,
                    right,
                    forward,
                    out Vector2 renderedCentre,
                    out _))
            {
                return;
            }

            property.VisualRoot.position += new Vector3(
                property.transform.position.x - renderedCentre.x,
                property.transform.position.y - bounds.min.y,
                property.transform.position.z - renderedCentre.y);
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

        private bool TryApplyHouseSetback(
            GeneratedProperty property,
            Vector2Int gridFootprint,
            PropertyCandidate candidate,
            Renderer[] renderers,
            bool centreInLot)
        {
            Vector3 roadPosition = GridToWorld(candidate.Road);
            Vector3 awayFromRoad = property.transform.position - roadPosition;
            awayFromRoad.y = 0f;
            if (awayFromRoad.sqrMagnitude < 0.001f)
            {
                return false;
            }

            awayFromRoad.Normalize();
            Vector3 towardRoad = -awayFromRoad;
            Vector3 right = Vector3.Cross(Vector3.up, towardRoad).normalized;
            if (!TryCalculateProjectedRendererFootprint(
                    renderers,
                    right,
                    towardRoad,
                    out _,
                    out Vector2 renderedHalfSize))
            {
                return false;
            }

            float minimumDistance =
                m_roadTileSize * 0.5f +
                m_sidewalkWidth +
                m_houseSetbackFromSidewalk +
                renderedHalfSize.y;
            float maximumDistance =
                m_roadTileSize * 0.5f +
                gridFootprint.y * m_roadTileSize -
                m_minimumHouseClearance -
                renderedHalfSize.y;
            if (minimumDistance > maximumDistance + 0.001f)
            {
                return false;
            }

            float distance;
            if (centreInLot)
            {
                distance = Vector3.Dot(property.transform.position - roadPosition, awayFromRoad);
                if (distance < minimumDistance - 0.001f || distance > maximumDistance + 0.001f)
                {
                    return false;
                }

                distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
            }
            else
            {
                int hash = DeriveSeed(
                    CurrentSeed,
                    candidate.Anchor.X * 73856093 ^ candidate.Anchor.Y * 19349663);
                float variation = (hash & 0x7fffffff) / (float)int.MaxValue;
                distance = minimumDistance + Mathf.Min(
                    m_houseSetbackVariation * variation,
                    maximumDistance - minimumDistance);
            }
            Vector3 position = roadPosition + awayFromRoad * distance;
            position.y = property.transform.position.y;
            property.transform.position = position;
            return true;
        }

        private bool TryCreateDrivewayReservation(
            GeneratedProperty property,
            OrientedFootprint houseFootprint,
            out OrientedFootprint drivewayFootprint,
            out Vector3 drivewayStart,
            out Vector3 drivewayEnd)
        {
            Vector3 roadPosition = GridToWorld(property.RoadCoordinate);
            Vector2 towardRoad = houseFootprint.Forward;
            Vector2 awayFromRoad = -towardRoad;
            Vector2 roadCentre = new Vector2(roadPosition.x, roadPosition.z);
            Vector2 start = roadCentre + awayFromRoad *
                (m_roadTileSize * 0.5f + m_sidewalkWidth);
            Vector2 end = houseFootprint.Centre + towardRoad * houseFootprint.HalfSize.y;
            Vector2 segment = end - start;
            float length = segment.magnitude;
            if (length < 0.1f)
            {
                drivewayFootprint = default;
                drivewayStart = default;
                drivewayEnd = default;
                return false;
            }

            Vector2 forward = segment / length;
            Vector2 right = new Vector2(forward.y, -forward.x);
            drivewayFootprint = new OrientedFootprint(
                (start + end) * 0.5f,
                right,
                forward,
                new Vector2(
                    m_drivewayWidth * 0.5f + m_drivewayClearance,
                    length * 0.5f));
            for (int index = 0; index < m_placedHouseFootprints.Count; index++)
            {
                if (FootprintsOverlap(
                        drivewayFootprint,
                        m_placedHouseFootprints[index],
                        0f))
                {
                    drivewayStart = default;
                    drivewayEnd = default;
                    return false;
                }
            }

            for (int index = 0; index < m_reservedDrivewayFootprints.Count; index++)
            {
                if (FootprintsOverlap(
                        drivewayFootprint,
                        m_reservedDrivewayFootprints[index],
                        0f))
                {
                    drivewayStart = default;
                    drivewayEnd = default;
                    return false;
                }
            }

            drivewayStart = new Vector3(
                start.x,
                SamplePlacementTerrainHeight(new Vector3(start.x, 0f, start.y)),
                start.y);
            drivewayEnd = new Vector3(
                end.x,
                property.transform.position.y,
                end.y);
            Vector3 propertyNormal = property.transform.up;
            Vector3 propertyOffset = drivewayEnd - property.transform.position;
            if (Mathf.Abs(propertyNormal.y) > 0.001f)
            {
                drivewayEnd.y = property.transform.position.y -
                    (propertyNormal.x * propertyOffset.x +
                     propertyNormal.z * propertyOffset.z) / propertyNormal.y;
            }

            float grade = Mathf.Atan2(
                Mathf.Abs(drivewayEnd.y - drivewayStart.y),
                length) * Mathf.Rad2Deg;
            return grade <= m_maximumDrivewaySlope;
        }

        private static void ConfigurePropertyConnectionPoints(
            GeneratedProperty property,
            Vector3 drivewayStart,
            Vector3 drivewayEnd,
            Vector2 towardRoad)
        {
            Vector3 forward = new Vector3(towardRoad.x, 0f, towardRoad.y);
            if (property.RoadConnection != null)
            {
                property.RoadConnection.SetPositionAndRotation(
                    drivewayStart,
                    Quaternion.LookRotation(-forward, Vector3.up));
            }

            if (property.DeliveryPoint != null)
            {
                property.DeliveryPoint.SetPositionAndRotation(
                    drivewayEnd,
                    Quaternion.LookRotation(forward, Vector3.up));
            }
        }

        private bool TryAlignPropertyToTerrain(
            GeneratedProperty property,
            Vector2Int gridFootprint,
            GridCoordinate roadCoordinate,
            Renderer[] renderers)
        {
            Transform propertyTransform = property.transform;
            Vector3 forward = GridToWorld(roadCoordinate) - propertyTransform.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                return false;
            }

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float halfWidth = gridFootprint.x * m_roadTileSize * 0.5f;
            float halfDepth = gridFootprint.y * m_roadTileSize * 0.5f;
            if (TryCalculateProjectedRendererFootprint(
                    renderers,
                    right,
                    forward,
                    out _,
                    out Vector2 renderedHalfSize))
            {
                halfWidth = Mathf.Max(0.05f, renderedHalfSize.x);
                halfDepth = Mathf.Max(0.05f, renderedHalfSize.y);
            }

            Vector3 centre = propertyTransform.position;
            float leftBack = SamplePlacementTerrainHeight(
                centre - right * halfWidth - forward * halfDepth);
            float leftFront = SamplePlacementTerrainHeight(
                centre - right * halfWidth + forward * halfDepth);
            float rightBack = SamplePlacementTerrainHeight(
                centre + right * halfWidth - forward * halfDepth);
            float rightFront = SamplePlacementTerrainHeight(
                centre + right * halfWidth + forward * halfDepth);
            float leftHeight = 0.5f * (leftBack + leftFront);
            float rightHeight = 0.5f * (rightBack + rightFront);
            float backHeight = 0.5f * (leftBack + rightBack);
            float frontHeight = 0.5f * (leftFront + rightFront);

            float gradientRight = (rightHeight - leftHeight) / Mathf.Max(0.1f, halfWidth * 2f);
            float gradientForward = (frontHeight - backHeight) / Mathf.Max(0.1f, halfDepth * 2f);
            Vector3 normal = (Vector3.up - right * gradientRight - forward * gradientForward).normalized;
            if (Vector3.Angle(Vector3.up, normal) > m_maximumHouseSlope)
            {
                return false;
            }

            centre.y = 0.25f * (leftHeight + rightHeight + backHeight + frontHeight);
            propertyTransform.SetPositionAndRotation(
                centre,
                Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, normal).normalized, normal));
            return true;
        }

        private float SamplePlacementTerrainHeight(Vector3 flatWorldPosition)
        {
            GridCoordinate rightOffset = DirectionOffset(TurnRight(m_startDirection));
            GridCoordinate forwardOffset = DirectionOffset(m_startDirection);
            Vector3 gridRight = new Vector3(rightOffset.X, 0f, rightOffset.Y);
            Vector3 gridForward = new Vector3(forwardOffset.X, 0f, forwardOffset.Y);
            Vector3 offset = flatWorldPosition - m_gridOrigin;
            float localX = Vector3.Dot(offset, gridRight) / m_roadTileSize;
            float localY = Vector3.Dot(offset, gridForward) / m_roadTileSize;
            return m_gridOrigin.y + SampleTerrainHeightWithoutHousePads(
                localX,
                localY,
                flatWorldPosition);
        }

        private bool TryCreateRenderedFootprint(
            GeneratedProperty property,
            Renderer[] renderers,
            out OrientedFootprint footprint)
        {
            Vector3 forward3 = GridToWorld(property.RoadCoordinate) - property.transform.position;
            forward3.y = 0f;
            if (forward3.sqrMagnitude < 0.001f)
            {
                footprint = default;
                return false;
            }

            forward3.Normalize();
            Vector3 right3 = Vector3.Cross(Vector3.up, forward3).normalized;
            if (!TryCalculateProjectedRendererFootprint(
                    renderers,
                    right3,
                    forward3,
                    out Vector2 centre,
                    out Vector2 halfSize))
            {
                footprint = default;
                return false;
            }

            footprint = new OrientedFootprint(
                centre,
                new Vector2(right3.x, right3.z),
                new Vector2(forward3.x, forward3.z),
                halfSize);
            return footprint.HalfSize.x > 0.001f && footprint.HalfSize.y > 0.001f;
        }

        private bool CanPlaceRenderedFootprint(OrientedFootprint candidate)
        {
            for (int index = 0; index < m_placedHouseFootprints.Count; index++)
            {
                if (FootprintsOverlap(
                        candidate,
                        m_placedHouseFootprints[index],
                        m_minimumHouseClearance))
                {
                    return false;
                }
            }

            for (int index = 0; index < m_reservedDrivewayFootprints.Count; index++)
            {
                if (FootprintsOverlap(candidate, m_reservedDrivewayFootprints[index], 0f))
                {
                    return false;
                }
            }

            float searchDistance = candidate.HalfSize.magnitude + m_roadTileSize * 0.5f;
            int searchRadius = Mathf.CeilToInt(searchDistance / m_roadTileSize) + 1;
            GridCoordinate centreCell = WorldToGrid(
                new Vector3(candidate.Centre.x, m_gridOrigin.y, candidate.Centre.y));
            for (int y = -searchRadius; y <= searchRadius; y++)
            {
                for (int x = -searchRadius; x <= searchRadius; x++)
                {
                    GridCoordinate cell = centreCell + new GridCoordinate(x, y);
                    if (m_roadCells.Contains(cell) &&
                        FootprintsOverlap(
                            candidate,
                            CreateCellFootprint(cell, m_sidewalkWidth),
                            0f))
                    {
                        return false;
                    }

                    if (m_startingAreaCells.Contains(cell) &&
                        FootprintsOverlap(candidate, CreateCellFootprint(cell, 0f), 0f))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private OrientedFootprint CreateCellFootprint(
            GridCoordinate coordinate,
            float padding)
        {
            Vector3 centre = GridToWorld(coordinate);
            return new OrientedFootprint(
                new Vector2(centre.x, centre.z),
                Vector2.right,
                Vector2.up,
                Vector2.one * (m_roadTileSize * 0.5f + padding));
        }

        private static bool FootprintsOverlap(
            OrientedFootprint first,
            OrientedFootprint second,
            float minimumClearance)
        {
            Vector2 centreOffset = second.Centre - first.Centre;
            return OverlapsOnAxis(first, second, centreOffset, first.Right, minimumClearance) &&
                   OverlapsOnAxis(first, second, centreOffset, first.Forward, minimumClearance) &&
                   OverlapsOnAxis(first, second, centreOffset, second.Right, minimumClearance) &&
                   OverlapsOnAxis(first, second, centreOffset, second.Forward, minimumClearance);
        }

        private static bool OverlapsOnAxis(
            OrientedFootprint first,
            OrientedFootprint second,
            Vector2 centreOffset,
            Vector2 axis,
            float minimumClearance)
        {
            float firstRadius =
                Mathf.Abs(Vector2.Dot(axis, first.Right)) * first.HalfSize.x +
                Mathf.Abs(Vector2.Dot(axis, first.Forward)) * first.HalfSize.y;
            float secondRadius =
                Mathf.Abs(Vector2.Dot(axis, second.Right)) * second.HalfSize.x +
                Mathf.Abs(Vector2.Dot(axis, second.Forward)) * second.HalfSize.y;
            return Mathf.Abs(Vector2.Dot(centreOffset, axis)) <
                   firstRadius + secondRadius + minimumClearance - 0.001f;
        }

        private static void DiscardRejectedProperty(GameObject property)
        {
            property.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(property);
            }
            else
            {
                DestroyImmediate(property);
            }
        }

        private static bool TryCalculateProjectedRendererFootprint(
            Renderer[] renderers,
            Vector3 right,
            Vector3 forward,
            out Vector2 centre,
            out Vector2 halfSize)
        {
            float minimumRight = float.PositiveInfinity;
            float maximumRight = float.NegativeInfinity;
            float minimumForward = float.PositiveInfinity;
            float maximumForward = float.NegativeInfinity;
            bool found = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Bounds localBounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 localCorner = localBounds.center + Vector3.Scale(
                        localBounds.extents,
                        new Vector3(
                            (corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f));
                    Vector3 worldCorner = renderer.transform.TransformPoint(localCorner);
                    float rightDistance = Vector3.Dot(worldCorner, right);
                    float forwardDistance = Vector3.Dot(worldCorner, forward);
                    minimumRight = Mathf.Min(minimumRight, rightDistance);
                    maximumRight = Mathf.Max(maximumRight, rightDistance);
                    minimumForward = Mathf.Min(minimumForward, forwardDistance);
                    maximumForward = Mathf.Max(maximumForward, forwardDistance);
                    found = true;
                }
            }

            if (!found)
            {
                centre = default;
                halfSize = default;
                return false;
            }

            float centreRight = (minimumRight + maximumRight) * 0.5f;
            float centreForward = (minimumForward + maximumForward) * 0.5f;
            Vector3 worldCentre = right * centreRight + forward * centreForward;
            centre = new Vector2(worldCentre.x, worldCentre.z);
            halfSize = new Vector2(
                (maximumRight - minimumRight) * 0.5f,
                (maximumForward - minimumForward) * 0.5f);
            return halfSize.x > 0.001f && halfSize.y > 0.001f;
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

        private bool ValidateLayout()
        {
            if (GeneratedTerrain == null ||
                GeneratedTerrain.VertexCount < 4 ||
                GeneratedTerrain.TriangleCount < 2 ||
                !GeneratedTerrain.TryGetComponent(out MeshCollider terrainCollider) ||
                terrainCollider.sharedMesh == null)
            {
                Debug.LogError("Generated neighbourhood has no valid continuous terrain mesh.", this);
                return false;
            }

            if (m_roadCells.Count != m_logicalRoads.Count || m_roadCells.Count != m_roadHeights.Count)
            {
                Debug.LogError("Generated neighbourhood road data is inconsistent.", this);
                return false;
            }

            if (GeneratedStartingArea == null || !m_roadCells.Contains(GridCoordinate.Zero))
            {
                Debug.LogError("Generated neighbourhood has no connected depot road.", this);
                return false;
            }

            foreach (GridCoordinate propertyCell in m_propertyCells)
            {
                if (m_roadCells.Contains(propertyCell) || m_startingAreaCells.Contains(propertyCell))
                {
                    Debug.LogError($"Generated property cell {propertyCell} overlaps reserved neighbourhood space.", this);
                    return false;
                }
            }

            HashSet<GridCoordinate> reachable = new HashSet<GridCoordinate> { GridCoordinate.Zero };
            Queue<GridCoordinate> frontier = new Queue<GridCoordinate>();
            frontier.Enqueue(GridCoordinate.Zero);
            while (frontier.Count > 0)
            {
                GridCoordinate current = frontier.Dequeue();
                RoadConnections connections = m_logicalRoads[current];
                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    CardinalDirection direction = (CardinalDirection)directionIndex;
                    GridCoordinate neighbour = current + DirectionOffset(direction);
                    if ((connections & DirectionConnection(direction)) != 0 &&
                        m_roadCells.Contains(neighbour) && reachable.Add(neighbour))
                    {
                        frontier.Enqueue(neighbour);
                    }
                }
            }

            if (reachable.Count != m_roadCells.Count)
            {
                Debug.LogError(
                    $"Generated road graph is disconnected ({reachable.Count}/{m_roadCells.Count} reachable).",
                    this);
                return false;
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
                    float allowedSlope = Mathf.Atan2(
                        MaximumRoadRise(road.Key, neighbour),
                        m_roadTileSize) * Mathf.Rad2Deg;
                    if (slope > allowedSlope + 0.05f)
                    {
                        Debug.LogError(
                            $"Generated road slope of {slope:F1} degrees exceeds its " +
                            $"{allowedSlope:F1} degree role limit.",
                            this);
                        return false;
                    }
                }
            }

            int[] roadsPerZone = new int[5];
            float[] elevationPerZone = new float[5];
            foreach (KeyValuePair<GridCoordinate, SuburbZone> roadZone in m_roadZones)
            {
                int zoneIndex = (int)roadZone.Value;
                roadsPerZone[zoneIndex]++;
                elevationPerZone[zoneIndex] += m_roadHeights[roadZone.Key];
            }

            int[] destinationsPerZone = new int[5];
            int unreachableDestinations = 0;
            for (int index = 0; index < m_deliveryDestinations.Count; index++)
            {
                DeliveryRouteMetrics metrics = m_deliveryDestinations[index]?.RouteMetrics;
                if (metrics == null || !metrics.IsReachable)
                {
                    unreachableDestinations++;
                    continue;
                }

                destinationsPerZone[(int)metrics.ProgressionZone]++;
            }

            for (int zoneIndex = (int)SuburbZone.Easy; zoneIndex <= (int)SuburbZone.Outer; zoneIndex++)
            {
                if (roadsPerZone[zoneIndex] == 0 ||
                    destinationsPerZone[zoneIndex] < m_progression.MinimumDestinationsPerZone)
                {
                    Debug.LogError(
                        $"Generated zone {(SuburbZone)zoneIndex} is missing roads or has only " +
                        $"{destinationsPerZone[zoneIndex]} delivery destinations.",
                        this);
                    return false;
                }
            }

            if (unreachableDestinations > 0)
            {
                Debug.LogError(
                    $"Generated neighbourhood contains {unreachableDestinations} unreachable destinations.",
                    this);
                return false;
            }

            int maximumLocalY = m_gridHeight * (m_blockHeight + 1);
            GridCoordinate outerSpine = LocalToGrid(0, maximumLocalY);
            if (!reachable.Contains(outerSpine) || !m_primaryRoadCells.Contains(outerSpine))
            {
                Debug.LogError("Primary road does not reach the outer Suburbs boundary.", this);
                return false;
            }

            float easyAverage = elevationPerZone[(int)SuburbZone.Easy] /
                                Mathf.Max(1, roadsPerZone[(int)SuburbZone.Easy]);
            float outerAverage = elevationPerZone[(int)SuburbZone.Outer] /
                                 Mathf.Max(1, roadsPerZone[(int)SuburbZone.Outer]);
            if (outerAverage < easyAverage + Mathf.Min(5f, m_maximumElevation * 0.2f))
            {
                Debug.LogError(
                    $"Outer Suburbs elevation ({outerAverage:F1}m) does not sufficiently exceed " +
                    $"Easy Suburbs elevation ({easyAverage:F1}m).",
                    this);
                return false;
            }

            int descentCount = 0;
            float previousHeight = m_roadHeights[GridCoordinate.Zero];
            int descentStart = Mathf.RoundToInt(maximumLocalY * m_progression.MediumEndNormalized);
            for (int localY = 1; localY <= maximumLocalY; localY++)
            {
                GridCoordinate coordinate = LocalToGrid(0, localY);
                if (!m_roadHeights.TryGetValue(coordinate, out float height))
                {
                    continue;
                }

                if (localY >= descentStart && height < previousHeight - 0.05f)
                {
                    descentCount++;
                }

                previousHeight = height;
            }

            if (descentCount == 0)
            {
                Debug.LogError("Primary route contains no late-zone descent or valley.", this);
                return false;
            }

            return true;
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
            if (!m_showGrid && !m_showOccupiedCells && !m_showConnections &&
                !m_showDeliveryDifficulty && !m_showProgressionZones &&
                !m_showPrimaryRoute && !m_showElevation)
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

            if (m_showProgressionZones)
            {
                foreach (KeyValuePair<GridCoordinate, SuburbZone> road in m_roadZones)
                {
                    Gizmos.color = ProgressionZoneColour(road.Value);
                    Gizmos.DrawCube(
                        GridToWorld(road.Key) + Vector3.up * 0.12f,
                        new Vector3(m_roadTileSize * 0.72f, 0.08f, m_roadTileSize * 0.72f));
                }
            }

            if (m_showPrimaryRoute)
            {
                Gizmos.color = new Color(0.1f, 1f, 1f, 0.95f);
                foreach (GridCoordinate coordinate in m_primaryRoadCells)
                {
                    Gizmos.DrawWireCube(
                        GridToWorld(coordinate) + Vector3.up * 0.35f,
                        new Vector3(m_roadTileSize * 0.82f, 0.25f, m_roadTileSize * 0.82f));
                }
            }

            if (m_showElevation)
            {
                foreach (GridCoordinate coordinate in m_roadCells)
                {
                    Vector3 top = GridToWorld(coordinate);
                    Vector3 basePoint = new Vector3(top.x, m_gridOrigin.y, top.z);
                    Gizmos.color = ProgressionZoneColour(m_roadZones[coordinate]);
                    Gizmos.DrawLine(basePoint, top);
                }
            }

            if (!m_showDeliveryDifficulty)
            {
                return;
            }

            for (int index = 0; index < m_deliveryDestinations.Count; index++)
            {
                DeliveryDestination destination = m_deliveryDestinations[index];
                if (destination != null && destination.DropPosition != null)
                {
                    Gizmos.color = DifficultyZoneColour(destination.RouteMetrics);
                    Gizmos.DrawWireSphere(destination.DropPosition.position, 0.65f);
                    if (destination.RouteMetrics != null)
                    {
                        Gizmos.DrawLine(
                            destination.DropPosition.position,
                            GridToWorld(destination.RouteMetrics.RoadCoordinate) + Vector3.up * 0.2f);
                    }
                }
            }
        }

        private static Color ProgressionZoneColour(SuburbZone zone)
        {
            return zone switch
            {
                SuburbZone.Start => new Color(0.2f, 1f, 0.35f, 0.32f),
                SuburbZone.Easy => new Color(0.15f, 0.6f, 1f, 0.32f),
                SuburbZone.Medium => new Color(1f, 0.85f, 0.1f, 0.32f),
                SuburbZone.Hilly => new Color(1f, 0.45f, 0.05f, 0.32f),
                _ => new Color(1f, 0.12f, 0.12f, 0.32f)
            };
        }

        private static Color DifficultyZoneColour(DeliveryRouteMetrics metrics)
        {
            if (metrics == null || !metrics.IsReachable)
            {
                return Color.magenta;
            }

            return metrics.Zone switch
            {
                DeliveryDifficultyZone.Depot => Color.white,
                DeliveryDifficultyZone.Easy => Color.green,
                DeliveryDifficultyZone.Medium => Color.yellow,
                DeliveryDifficultyZone.Hilly => new Color(1f, 0.55f, 0.1f),
                DeliveryDifficultyZone.Difficult => Color.red,
                _ => new Color(0.65f, 0.1f, 1f)
            };
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
            m_minimumHousesPerBlock = Mathf.Clamp(
                m_minimumHousesPerBlock,
                0,
                m_housesPerBlock);
            m_minimumHouseSpacing = Mathf.Max(0, m_minimumHouseSpacing);
            m_minimumHouseClearance = Mathf.Max(0f, m_minimumHouseClearance);
            m_maximumHouseSlope = Mathf.Clamp(m_maximumHouseSlope, 0f, 45f);
            m_sidewalkWidth = Mathf.Max(0f, m_sidewalkWidth);
            m_sidewalkSurfaceOffset = Mathf.Max(0f, m_sidewalkSurfaceOffset);
            m_houseSetbackFromSidewalk = Mathf.Max(0f, m_houseSetbackFromSidewalk);
            m_houseSetbackVariation = Mathf.Max(0f, m_houseSetbackVariation);
            m_drivewayWidth = Mathf.Max(0.5f, m_drivewayWidth);
            m_drivewayClearance = Mathf.Max(0f, m_drivewayClearance);
            m_maximumDrivewaySlope = Mathf.Clamp(m_maximumDrivewaySlope, 1f, 30f);
            m_elevationStep = Mathf.Max(0.1f, m_elevationStep);
            m_minimumElevationStep = Mathf.Clamp(
                m_minimumElevationStep,
                0.1f,
                m_elevationStep);
            m_maximumElevation = Mathf.Max(0f, m_maximumElevation);
            m_maximumRoadSlope = Mathf.Clamp(m_maximumRoadSlope, 1f, 60f);
            m_progression.Validate();
            m_terrainSettings.Validate();
            EnsureDifficultySettings();
            m_residentialGroundVerticalOffset = Mathf.Max(0f, m_residentialGroundVerticalOffset);
            m_standardPropertyFootprint.x = Mathf.Max(1, m_standardPropertyFootprint.x);
            m_standardPropertyFootprint.y = Mathf.Max(1, m_standardPropertyFootprint.y);
            m_placeholderStartingAreaFootprint.x = Mathf.Max(1, m_placeholderStartingAreaFootprint.x);
            m_placeholderStartingAreaFootprint.y = Mathf.Max(1, m_placeholderStartingAreaFootprint.y);
        }
#endif
    }
}
