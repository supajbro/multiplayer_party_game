using System;
using System.Collections.Generic;
using UnityEngine;

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
        [SerializeField, Min(1)] private int m_maximumRoadTiles = 30;
        [SerializeField, Min(0.1f)] private float m_tileSize = 10f;

        [Header("Roads")]
        [SerializeField] private GameObject[] m_roadPrefabs = Array.Empty<GameObject>();
        [SerializeField, Min(1)] private int m_minimumRoadLength = 8;
        [SerializeField, Min(1)] private int m_maximumRoadLength = 15;
        [SerializeField, Min(1)] private int m_minimumSegmentLength = 3;
        [SerializeField, Min(1)] private int m_maximumSegmentLength = 6;
        [SerializeField, Range(0f, 1f)] private float m_turnChance = 0.45f;
        [SerializeField, Range(0f, 1f)] private float m_branchChance = 0.2f;
        [SerializeField, Min(0)] private int m_maximumBranches = 3;
        [SerializeField, Min(1)] private int m_minimumBranchLength = 2;
        [SerializeField, Min(1)] private int m_maximumBranchLength = 5;

        [Header("Houses")]
        [SerializeField] private GameObject[] m_housePrefabs = Array.Empty<GameObject>();
        [SerializeField, Range(0f, 1f)] private float m_houseSpawnChance = 0.42f;
        [SerializeField, Min(0)] private int m_minimumHouseSpacing = 1;
        [SerializeField] private Vector2Int m_standardPropertyFootprint = Vector2Int.one;
        [SerializeField] private Vector3 m_placeholderHouseSize = new Vector3(10f, 6f, 10f);

        [Header("Starting Area")]
        [SerializeField] private GameObject m_startingAreaPrefab;
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

        private Transform m_generatedRoot;
        private Transform m_roadsRoot;
        private Transform m_housesRoot;
        private Vector3 m_gridOrigin;
        private CardinalDirection m_startDirection;
        private bool m_warnedAboutRoadPlaceholders;
        private bool m_warnedAboutHousePlaceholders;

        public StartingArea GeneratedStartingArea { get; private set; }
        public IReadOnlyList<GeneratedRoad> GeneratedRoads => m_generatedRoads;
        public IReadOnlyList<GeneratedProperty> GeneratedProperties => m_generatedProperties;
        public int CurrentSeed { get; private set; }
        public float TileSize => m_tileSize;

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
            GenerateRoadLayout(layoutRandom);
            CalculateRoadConnections();
            // Visual selection has its own stream so adding art cannot perturb the
            // logical road/property decisions made for a given seed.
            CreateRoadVisuals(new System.Random(DeriveSeed(seed, 0x2D31A7B5)));
            GenerateProperties(new System.Random(DeriveSeed(seed, 0x61C88647)));
            ValidateLayout();
        }

        public int SelectSeed()
        {
            return m_useRandomSeed
                ? unchecked(Environment.TickCount ^ Guid.NewGuid().GetHashCode())
                : m_seed;
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
            m_warnedAboutRoadPlaceholders = false;
            m_warnedAboutHousePlaceholders = false;
        }

        public Vector3 GridToWorld(GridCoordinate coordinate)
        {
            return m_gridOrigin + new Vector3(coordinate.X * m_tileSize, 0f, coordinate.Y * m_tileSize);
        }

        public GridCoordinate WorldToGrid(Vector3 worldPosition)
        {
            Vector3 offset = worldPosition - m_gridOrigin;
            return new GridCoordinate(
                Mathf.RoundToInt(offset.x / m_tileSize),
                Mathf.RoundToInt(offset.z / m_tileSize));
        }

        private bool ValidateConfiguration()
        {
            if (m_tileSize <= 0f || m_maximumRoadTiles <= 0)
            {
                Debug.LogError("Neighbourhood Generator requires Tile Size and Maximum Road Tiles greater than zero.", this);
                return false;
            }

            if (m_minimumRoadLength <= 0 || m_maximumRoadLength < m_minimumRoadLength ||
                m_minimumSegmentLength <= 0 || m_maximumSegmentLength < m_minimumSegmentLength ||
                m_minimumBranchLength <= 0 || m_maximumBranchLength < m_minimumBranchLength)
            {
                Debug.LogError("Neighbourhood Generator road length ranges are invalid.", this);
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

            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = "Placeholder_StartingArea";
            placeholder.transform.SetParent(root.transform, false);
            placeholder.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            placeholder.transform.localScale = new Vector3(
                m_placeholderStartingAreaFootprint.x * m_tileSize,
                0.5f,
                m_placeholderStartingAreaFootprint.y * m_tileSize);

            Transform roadConnection = new GameObject("RoadConnection").transform;
            roadConnection.SetParent(root.transform, false);
            roadConnection.localPosition = new Vector3(
                0f,
                0f,
                (m_placeholderStartingAreaFootprint.y * 0.5f + 0.5f) * m_tileSize);
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

            StartingArea startingArea = root.AddComponent<StartingArea>();
            startingArea.Initialise(roadConnection, spawnPoints, m_placeholderStartingAreaFootprint);
            return startingArea;
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
                        (minimumX + x) * m_tileSize,
                        0f,
                        (minimumY + y) * m_tileSize));
                    m_startingAreaCells.Add(WorldToGrid(worldPosition));
                }
            }

            // The explicit connection is the one intentional entry into the exclusion zone.
            m_startingAreaCells.Remove(GridCoordinate.Zero);
        }

        private void GenerateRoadLayout(System.Random random)
        {
            AddRoad(GridCoordinate.Zero);
            int requestedMainLength = NextInclusive(random, m_minimumRoadLength, m_maximumRoadLength);
            int mainLength = Mathf.Min(requestedMainLength, m_maximumRoadTiles);
            CardinalDirection direction = m_startDirection;
            GridCoordinate current = GridCoordinate.Zero;
            int segmentRemaining = NextInclusive(random, m_minimumSegmentLength, m_maximumSegmentLength);

            while (m_roadCells.Count < mainLength)
            {
                if (segmentRemaining <= 0)
                {
                    if (random.NextDouble() < m_turnChance)
                    {
                        direction = random.Next(0, 2) == 0 ? TurnLeft(direction) : TurnRight(direction);
                    }

                    segmentRemaining = NextInclusive(random, m_minimumSegmentLength, m_maximumSegmentLength);
                }

                if (!TryAdvanceRoad(current, direction, out GridCoordinate next))
                {
                    CardinalDirection firstAlternative = random.Next(0, 2) == 0 ? TurnLeft(direction) : TurnRight(direction);
                    CardinalDirection secondAlternative = firstAlternative == TurnLeft(direction)
                        ? TurnRight(direction)
                        : TurnLeft(direction);
                    if (TryAdvanceRoad(current, firstAlternative, out next))
                    {
                        direction = firstAlternative;
                    }
                    else if (TryAdvanceRoad(current, secondAlternative, out next))
                    {
                        direction = secondAlternative;
                    }
                    else
                    {
                        break;
                    }
                }

                current = next;
                segmentRemaining--;
            }

            int mainRoadCount = m_roadOrder.Count;
            int branches = 0;
            for (int roadIndex = 0;
                 roadIndex < mainRoadCount && branches < m_maximumBranches && m_roadCells.Count < m_maximumRoadTiles;
                 roadIndex++)
            {
                if (random.NextDouble() >= m_branchChance)
                {
                    continue;
                }

                GridCoordinate branchOrigin = m_roadOrder[roadIndex];
                CardinalDirection branchDirection = (CardinalDirection)random.Next(0, 4);
                if (!CanPlaceRoad(branchOrigin + DirectionOffset(branchDirection)))
                {
                    branchDirection = TurnRight(branchDirection);
                }

                int branchLength = NextInclusive(random, m_minimumBranchLength, m_maximumBranchLength);
                GridCoordinate branchCurrent = branchOrigin;
                bool placedAny = false;
                for (int branchIndex = 0;
                     branchIndex < branchLength && m_roadCells.Count < m_maximumRoadTiles;
                     branchIndex++)
                {
                    if (!TryAdvanceRoad(branchCurrent, branchDirection, out GridCoordinate next))
                    {
                        CardinalDirection turned = random.Next(0, 2) == 0
                            ? TurnLeft(branchDirection)
                            : TurnRight(branchDirection);
                        if (!TryAdvanceRoad(branchCurrent, turned, out next))
                        {
                            break;
                        }

                        branchDirection = turned;
                    }

                    branchCurrent = next;
                    placedAny = true;
                }

                if (placedAny)
                {
                    branches++;
                }
            }
        }

        private bool TryAdvanceRoad(
            GridCoordinate current,
            CardinalDirection direction,
            out GridCoordinate next)
        {
            next = current + DirectionOffset(direction);
            if (!CanPlaceRoad(next))
            {
                return false;
            }

            AddRoad(next);
            return true;
        }

        private bool CanPlaceRoad(GridCoordinate coordinate)
        {
            return m_roadCells.Count < m_maximumRoadTiles &&
                   !m_roadCells.Contains(coordinate) &&
                   !m_startingAreaCells.Contains(coordinate);
        }

        private void AddRoad(GridCoordinate coordinate)
        {
            m_roadCells.Add(coordinate);
            m_roadOrder.Add(coordinate);
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
                GameObject road = TryCreateRoadPrefab(connections, GridToWorld(coordinate), random);
                if (road == null)
                {
                    road = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    road.transform.SetPositionAndRotation(GridToWorld(coordinate), Quaternion.identity);
                    road.transform.localScale = new Vector3(m_tileSize / 10f, 1f, m_tileSize / 10f);
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
                            Quaternion.Euler(0f, quarterTurns * 90f, 0f),
                            m_roadsRoot);
                    }
                }
            }

            return null;
        }

        private void GenerateProperties(System.Random random)
        {
            HashSet<GridCoordinate> consideredAnchors = new HashSet<GridCoordinate>();
            int houseIndex = 0;
            for (int roadIndex = 0; roadIndex < m_roadOrder.Count; roadIndex++)
            {
                GridCoordinate roadCoordinate = m_roadOrder[roadIndex];
                RoadConnections roadConnections = m_logicalRoads[roadCoordinate];
                for (int directionIndex = 0; directionIndex < 4; directionIndex++)
                {
                    CardinalDirection propertyToRoadDirection = Opposite((CardinalDirection)directionIndex);
                    CardinalDirection roadToPropertyDirection = (CardinalDirection)directionIndex;
                    if ((roadConnections & DirectionConnection(roadToPropertyDirection)) != 0)
                    {
                        continue;
                    }

                    GridCoordinate anchor = roadCoordinate + DirectionOffset(roadToPropertyDirection);
                    if (!consideredAnchors.Add(anchor) || random.NextDouble() >= m_houseSpawnChance)
                    {
                        continue;
                    }

                    GameObject prefab = SelectHousePrefab(random);
                    Vector2Int footprint = GetPropertyFootprint(prefab);
                    List<GridCoordinate> cells = GetPropertyCells(anchor, propertyToRoadDirection, footprint);
                    if (!CanPlaceProperty(cells))
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
                    FacePropertyTowardsRoad(property.transform, roadCoordinate);
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

                    generatedProperty.Initialise(anchor, footprint, roadConnection, deliveryPoint);
                    m_generatedProperties.Add(generatedProperty);
                    houseIndex++;
                }
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

        private bool CanPlaceProperty(List<GridCoordinate> cells)
        {
            for (int index = 0; index < cells.Count; index++)
            {
                GridCoordinate cell = cells[index];
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
                Mathf.Min(m_placeholderHouseSize.x, footprint.x * m_tileSize),
                m_placeholderHouseSize.y,
                Mathf.Min(m_placeholderHouseSize.z, footprint.y * m_tileSize));

            Transform roadConnection = new GameObject("RoadConnection").transform;
            roadConnection.SetParent(root.transform, false);
            roadConnection.localPosition = Vector3.forward * (footprint.y * m_tileSize * 0.5f);
            Transform deliveryPoint = new GameObject("DeliveryPoint").transform;
            deliveryPoint.SetParent(root.transform, false);
            deliveryPoint.localPosition = Vector3.forward * (footprint.y * m_tileSize * 0.5f + 1f);

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
            if (m_roadCells.Count > m_maximumRoadTiles || m_roadCells.Count != m_logicalRoads.Count)
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
        }

        private static int NextInclusive(System.Random random, int minimum, int maximum)
        {
            return random.Next(minimum, maximum + 1);
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
                    Gizmos.DrawWireCube(GridToWorld(coordinate), new Vector3(m_tileSize, 0.05f, m_tileSize));
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
                                centre + new Vector3(offset.X, 0f, offset.Y) * (m_tileSize * 0.45f));
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
                Gizmos.DrawCube(GridToWorld(coordinate), new Vector3(m_tileSize * 0.95f, 0.08f, m_tileSize * 0.95f));
            }
        }

        private void OnValidate()
        {
            m_maximumRoadTiles = Mathf.Max(1, m_maximumRoadTiles);
            m_minimumRoadLength = Mathf.Max(1, m_minimumRoadLength);
            m_maximumRoadLength = Mathf.Max(m_minimumRoadLength, m_maximumRoadLength);
            m_minimumSegmentLength = Mathf.Max(1, m_minimumSegmentLength);
            m_maximumSegmentLength = Mathf.Max(m_minimumSegmentLength, m_maximumSegmentLength);
            m_minimumBranchLength = Mathf.Max(1, m_minimumBranchLength);
            m_maximumBranchLength = Mathf.Max(m_minimumBranchLength, m_maximumBranchLength);
            m_standardPropertyFootprint.x = Mathf.Max(1, m_standardPropertyFootprint.x);
            m_standardPropertyFootprint.y = Mathf.Max(1, m_standardPropertyFootprint.y);
            m_placeholderStartingAreaFootprint.x = Mathf.Max(1, m_placeholderStartingAreaFootprint.x);
            m_placeholderStartingAreaFootprint.y = Mathf.Max(1, m_placeholderStartingAreaFootprint.y);
        }
#endif
    }
}
