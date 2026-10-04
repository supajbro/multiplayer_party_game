using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Delivery;
using UnityEngine;
using UnityEngine.Serialization;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Builds a deterministic logical neighbourhood, then represents it with assigned
    /// modular prefabs or lightweight development placeholders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodGenerator : MonoBehaviour
    {
        [Header("Generation")]
        [SerializeField] private bool m_generateOnStart = true;
        [SerializeField] private bool m_useRandomSeed;
        [SerializeField] private int m_seed = 12345;

        [Header("Grid")]
        [SerializeField, Min(1)] private int m_gridWidth = 4;
        [SerializeField, Min(1)] private int m_gridHeight = 4;
        [SerializeField, Min(1)] private int m_blockWidth = 4;
        [SerializeField, Min(1)] private int m_blockHeight = 4;
        [FormerlySerializedAs("m_tileSize")]
        [SerializeField, Min(0.1f)] private float m_roadTileSize = 10f;

        [Header("Roads")]
        [SerializeField] private GameObject[] m_roadPrefabs = Array.Empty<GameObject>();

        [Header("Houses")]
        [SerializeField] private GameObject[] m_housePrefabs = Array.Empty<GameObject>();
        [SerializeField, Min(0)] private int m_housesPerBlock = 6;
        [SerializeField, Min(0)] private int m_minimumHouseSpacing = 1;
        [SerializeField] private Vector2Int m_standardPropertyFootprint = Vector2Int.one;
        [SerializeField] private Vector3 m_placeholderHouseSize = new Vector3(10f, 6f, 10f);

        [Header("Elevation")]
        [SerializeField] private bool m_elevationEnabled = true;
        [SerializeField, Min(0.1f)] private float m_elevationStep = 3f;
        [SerializeField, Min(0f)] private float m_maximumElevation = 9f;
        [SerializeField, Range(1f, 45f)] private float m_maximumRoadSlope = 12f;

        [Header("Starting Area")]
        [SerializeField] private GameObject m_startingAreaPrefab;
        [SerializeField] private GameObject m_deliveryNpcPrefab;
        [SerializeField] private Vector2Int m_placeholderStartingAreaFootprint = new Vector2Int(3, 3);

        [Header("Debug")]
        [SerializeField] private bool m_showGrid;
        [SerializeField] private bool m_showConnections = true;
        [SerializeField] private bool m_showOccupiedCells;

        private readonly List<GeneratedRoad> m_generatedRoads = new List<GeneratedRoad>();
        private readonly List<GeneratedProperty> m_generatedProperties = new List<GeneratedProperty>();
        private readonly HashSet<GridCoordinate> m_roadCells = new HashSet<GridCoordinate>();
        private readonly HashSet<GridCoordinate> m_propertyCells = new HashSet<GridCoordinate>();
        private readonly HashSet<GridCoordinate> m_startingAreaCells = new HashSet<GridCoordinate>();
        private readonly List<GridCoordinate> m_roadOrder = new List<GridCoordinate>();
        private readonly Dictionary<GridCoordinate, RoadConnections> m_logicalRoads =
            new Dictionary<GridCoordinate, RoadConnections>();
        private readonly Dictionary<GridCoordinate, float> m_roadHeights =
            new Dictionary<GridCoordinate, float>();

        private float[,] m_blockElevations;
        private int m_minimumLocalX;

        private Transform m_generatedRoot;
        private Transform m_roadsRoot;
        private Transform m_housesRoot;
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
        public GameObject DeliveryNpcPrefab => m_deliveryNpcPrefab;
        public int CurrentSeed { get; private set; }
        public float TileSize => m_roadTileSize;
        public bool HasGeneratedNeighbourhood => GeneratedStartingArea != null && m_generatedRoads.Count > 0;
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
            if (!ValidateConfiguration())
            {
                return;
            }

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
            GenerateRoadLayout();
            CalculateRoadConnections();
            // Visual selection has its own stream so adding art cannot perturb the
            // logical road/property decisions made for a given seed.
            CreateRoadVisuals(new System.Random(DeriveSeed(seed, 0x2D31A7B5)));
            GenerateProperties(new System.Random(DeriveSeed(seed, 0x61C88647)));
            CreateDeliveryNpc();
            CalculateWorldBounds();
            ValidateLayout();
        }

        public int SelectSeed()
        {
            return m_useRandomSeed
                ? unchecked(Environment.TickCount ^ Guid.NewGuid().GetHashCode())
                : m_seed;
        }

        public void SetDeliveryNpcPrefab(GameObject deliveryNpcPrefab)
        {
            m_deliveryNpcPrefab = deliveryNpcPrefab;
        }

        [ContextMenu("Clear Generated Neighbourhood")]
        public void Clear()
        {
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
            m_housesRoot = null;
            GeneratedStartingArea = null;
            m_generatedRoads.Clear();
            m_generatedProperties.Clear();
            m_roadCells.Clear();
            m_propertyCells.Clear();
            m_startingAreaCells.Clear();
            m_roadOrder.Clear();
            m_logicalRoads.Clear();
            m_roadHeights.Clear();
            m_blockElevations = null;
            GeneratedWorldBounds = new Bounds(transform.position, Vector3.zero);
            m_warnedAboutRoadPlaceholders = false;
            m_warnedAboutHousePlaceholders = false;
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

            if (m_elevationStep <= 0f || m_maximumElevation < 0f || m_maximumRoadSlope <= 0f)
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
            m_housesRoot = new GameObject("Houses").transform;
            m_housesRoot.SetParent(m_generatedRoot, false);
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

            int maximumLevel = Mathf.FloorToInt(m_maximumElevation / m_elevationStep);
            float transitionLength = (m_blockHeight + 1) * m_roadTileSize;
            float maximumRise = Mathf.Tan(m_maximumRoadSlope * Mathf.Deg2Rad) * transitionLength;
            if (maximumLevel <= 0 || m_elevationStep > maximumRise + 0.001f)
            {
                Debug.LogWarning(
                    "Elevation is enabled, but the configured Elevation Step cannot fit below Maximum Road Slope. " +
                    "The generated grid will remain flat.", this);
                return;
            }

            int sectionSize = m_gridHeight >= 4 ? 2 : 1;
            int sectionCount = Mathf.CeilToInt(m_gridHeight / (float)sectionSize);
            int[] sectionLevels = new int[sectionCount];
            bool hasElevation = false;
            for (int section = 1; section < sectionCount; section++)
            {
                int change = random.Next(0, 5) == 0 ? -1 : random.Next(0, 3) == 0 ? 1 : 0;
                sectionLevels[section] = Mathf.Clamp(sectionLevels[section - 1] + change, 0, maximumLevel);
                hasElevation |= sectionLevels[section] > 0;
            }

            if (!hasElevation && sectionCount > 1)
            {
                sectionLevels[sectionCount - 1] = 1;
            }

            for (int row = 0; row < m_gridHeight; row++)
            {
                float elevation = sectionLevels[row / sectionSize] * m_elevationStep;
                for (int column = 0; column < m_gridWidth; column++)
                {
                    m_blockElevations[column, row] = elevation;
                }
            }
        }

        private void GenerateRoadLayout()
        {
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
            int row = Mathf.Min(localY / span, m_gridHeight - 1);
            int nextRow = Mathf.Min(row + 1, m_gridHeight - 1);
            float progress = (localY % span) / (float)span;
            return Mathf.Lerp(m_blockElevations[0, row], m_blockElevations[0, nextRow], progress);
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
                GeneratedRoad generatedRoad = road.AddComponent<GeneratedRoad>();
                generatedRoad.Initialise(coordinate, connections);
                m_generatedRoads.Add(generatedRoad);
            }
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
            GridCoordinate eastCoordinate = coordinate + DirectionOffset(CardinalDirection.East);
            GridCoordinate westCoordinate = coordinate + DirectionOffset(CardinalDirection.West);
            GridCoordinate northCoordinate = coordinate + DirectionOffset(CardinalDirection.North);
            GridCoordinate southCoordinate = coordinate + DirectionOffset(CardinalDirection.South);

            float east = m_roadHeights.TryGetValue(eastCoordinate, out float eastHeight) ? eastHeight : centre;
            float west = m_roadHeights.TryGetValue(westCoordinate, out float westHeight) ? westHeight : centre;
            float north = m_roadHeights.TryGetValue(northCoordinate, out float northHeight) ? northHeight : centre;
            float south = m_roadHeights.TryGetValue(southCoordinate, out float southHeight) ? southHeight : centre;
            float xSlope = SteepestAxisSlope(east - centre, centre - west);
            float zSlope = SteepestAxisSlope(north - centre, centre - south);
            Vector3 normal = new Vector3(
                -xSlope / m_roadTileSize,
                1f,
                -zSlope / m_roadTileSize).normalized;
            return Quaternion.FromToRotation(Vector3.up, normal);
        }

        private static float SteepestAxisSlope(float positiveDelta, float negativeDelta)
        {
            return Mathf.Abs(positiveDelta) >= Mathf.Abs(negativeDelta)
                ? positiveDelta
                : negativeDelta;
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
                        PropertyCandidate candidate = candidates[candidateIndex];
                        GameObject prefab = SelectHousePrefab(random);
                        Vector2Int footprint = GetPropertyFootprint(prefab);
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
                        GameObject property = prefab != null
                            ? Instantiate(prefab, centre, Quaternion.identity, m_housesRoot)
                            : CreatePlaceholderHouse(centre, footprint);
                        FacePropertyTowardsRoad(property.transform, candidate.Road);
                        property.name = prefab != null
                            ? $"House_{houseIndex:000}"
                            : $"Placeholder_House_{houseIndex:000}";

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
                        m_generatedProperties.Add(generatedProperty);
                        housesInBlock++;
                        houseIndex++;
                    }
                }
            }
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
            property.rotation = Quaternion.LookRotation(directionToRoad.normalized, Vector3.up);
            if (metadata == null || metadata.RoadConnection == null)
            {
                return;
            }

            Vector3 connectionForward = Vector3.ProjectOnPlane(metadata.RoadConnection.forward, Vector3.up);
            if (connectionForward.sqrMagnitude > 0.001f)
            {
                property.rotation = Quaternion.AngleAxis(
                    Vector3.SignedAngle(connectionForward, directionToRoad, Vector3.up),
                    Vector3.up) * property.rotation;
            }
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
            m_maximumElevation = Mathf.Max(0f, m_maximumElevation);
            m_maximumRoadSlope = Mathf.Clamp(m_maximumRoadSlope, 1f, 45f);
            m_standardPropertyFootprint.x = Mathf.Max(1, m_standardPropertyFootprint.x);
            m_standardPropertyFootprint.y = Mathf.Max(1, m_standardPropertyFootprint.y);
            m_placeholderStartingAreaFootprint.x = Mathf.Max(1, m_placeholderStartingAreaFootprint.x);
            m_placeholderStartingAreaFootprint.y = Mathf.Max(1, m_placeholderStartingAreaFootprint.y);
        }
#endif
    }
}
