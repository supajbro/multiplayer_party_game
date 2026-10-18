using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        public bool HasSummitExit => m_landmarkRoads.Enabled && m_uphillRoadCells.Count > 0;
        public LandmarkRoadSettings LandmarkRoads => m_landmarkRoads;
        public int UphillRoadCount => m_uphillRoadCells.Count;

        public bool IsUphillRoad(GridCoordinate coordinate) =>
            m_uphillRoadCells.Contains(coordinate);

        public bool IsTrafficPortalClearOfSummit(TrafficRoadPortal portal)
        {
            Vector2Int local = GridToLocal(portal.Coordinate);
            return HasStandardTunnelClearanceFromSummit(local.x);
        }

        public float SummitTunnelExclusionHalfWidth => GetSummitTunnelExclusionHalfWidth();

        public TrafficRoadPortal SummitExitPortal
        {
            get
            {
                GridCoordinate coordinate = GetSummitRoadCoordinate();
                GridCoordinate outwardOffset = DirectionOffset(m_startDirection);
                return new TrafficRoadPortal(
                    coordinate,
                    m_startDirection,
                    GridToWorld(coordinate),
                    new Vector3(outwardOffset.X, 0f, outwardOffset.Y).normalized);
            }
        }

        private GridCoordinate GetSummitRoadCoordinate()
        {
            int localY = m_gridHeight * (m_blockHeight + 1) +
                         (m_landmarkRoads.Enabled ? m_landmarkRoads.UphillLengthTiles : 0);
            return LocalToGrid(0, localY);
        }

        private float ApplyHillyPerimeter(float terrainHeight, float localX, float localY)
        {
            if (!m_borderSettings.Enabled || m_borderSettings.HillHeight <= 0f)
            {
                return terrainHeight;
            }

            float depotDepth = GeneratedStartingArea != null
                ? GeneratedStartingArea.GridFootprint.y
                : m_placeholderStartingAreaFootprint.y;
            float minimumX = m_minimumLocalX * m_roadTileSize;
            float maximumX = (m_minimumLocalX + m_gridWidth * (m_blockWidth + 1)) *
                             m_roadTileSize;
            float minimumY = -depotDepth * m_roadTileSize;
            float maximumY = m_gridHeight * (m_blockHeight + 1) * m_roadTileSize;
            float worldX = localX * m_roadTileSize;
            float worldY = localY * m_roadTileSize;
            // Perimeter terrain must never raise the playable suburb. Measure only the
            // distance outside its bounds, so the grass beside boundary roads remains at
            // its original generated height and the embankment grows outward from there.
            float outsideX = Mathf.Max(minimumX - worldX, worldX - maximumX, 0f);
            float outsideY = Mathf.Max(minimumY - worldY, worldY - maximumY, 0f);
            float outsideDistance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
            if (outsideDistance <= 0.0001f)
            {
                return terrainHeight;
            }

            float depth = Mathf.Max(4f, m_borderSettings.HillDepth);
            float normalized = Mathf.Clamp01(outsideDistance / depth);
            float hillMask = Mathf.Pow(
                Mathf.SmoothStep(0f, 1f, normalized),
                m_borderSettings.SlopeSmoothness);
            hillMask *= CalculatePerimeterOpeningMultiplier(localX, localY);
            if (hillMask <= 0.0001f)
            {
                return terrainHeight;
            }

            float frequency = m_borderSettings.HillNoiseFrequency * m_roadTileSize;
            float broadNoise = Mathf.PerlinNoise(
                m_elevationNoiseOffsetX + localX * frequency,
                m_elevationNoiseOffsetY + localY * frequency);
            float detailNoise = Mathf.PerlinNoise(
                m_elevationNoiseOffsetX * 0.31f + localX * frequency * 2.7f,
                m_elevationNoiseOffsetY * 0.31f + localY * frequency * 2.7f);
            float signedNoise = ((broadNoise - 0.5f) * 2f +
                                 (detailNoise - 0.5f) * m_borderSettings.HillVariation) *
                                m_borderSettings.HillNoiseAmount;
            return terrainHeight + Mathf.Max(0f,
                (m_borderSettings.HillHeight + signedNoise) * hillMask);
        }

        private float CalculatePerimeterOpeningMultiplier(float localX, float localY)
        {
            float boundaryY = m_gridHeight * (m_blockHeight + 1);
            float approachDepth = Mathf.Max(
                m_borderSettings.HillDepth,
                m_borderSettings.TunnelDepth * 2f) / m_roadTileSize;
            if (localY < boundaryY - approachDepth)
            {
                return 1f;
            }

            float halfClearance = Mathf.Max(
                m_borderSettings.TunnelClearance,
                m_borderSettings.TunnelWidth * 0.65f) / m_roadTileSize;
            float approach = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(boundaryY - approachDepth, boundaryY, localY));
            float strongestOpening = m_landmarkRoads.Enabled
                ? CalculateOpeningStrength(Mathf.Abs(localX), halfClearance) * approach
                : 0f;

            for (int index = 0; index < m_trafficPortalCoordinates.Count; index++)
            {
                Vector2Int portal = GridToLocal(m_trafficPortalCoordinates[index]);
                strongestOpening = Mathf.Max(strongestOpening,
                    CalculateOpeningStrength(Mathf.Abs(localX - portal.x), halfClearance) *
                    approach);
            }

            return 1f - Mathf.Clamp01(strongestOpening);
        }

        private static float CalculateOpeningStrength(float lateralDistance, float halfClearance) =>
            1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(halfClearance, halfClearance * 1.65f, lateralDistance));

        private void CreateUphillGuardRails(Transform parent)
        {
            if (!m_landmarkRoads.Enabled || !m_landmarkRoads.GenerateGuardRails || parent == null)
            {
                return;
            }

            Transform root = new GameObject("UphillGuardRails").transform;
            root.SetParent(parent, false);
            int baseLocalY = m_gridHeight * (m_blockHeight + 1);
            int interval = Mathf.Max(1, m_landmarkRoads.GuardRailPostIntervalTiles);
            float sideOffset = m_terrainSettings.RoadHalfWidth + 0.55f;
            Material material = GetFallbackRoadMaterial(true);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 previousRailPoint = Vector3.zero;
                bool hasPreviousRailPoint = false;
                for (int step = 1; step <= m_landmarkRoads.UphillLengthTiles; step += interval)
                {
                    GridCoordinate coordinate = LocalToGrid(0, baseLocalY + step);
                    Vector3 centre = GridToWorld(coordinate);
                    Vector3 right = MapLocalToWorld(Vector3.right) - MapLocalToWorld(Vector3.zero);
                    right = Vector3.ProjectOnPlane(right, Vector3.up).normalized;
                    GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    post.name = $"GuardRailPost_{side}_{step:00}";
                    post.transform.SetParent(root, true);
                    post.transform.position = centre + right * (side * sideOffset) +
                                              Vector3.up * (m_landmarkRoads.GuardRailHeight * 0.5f);
                    post.transform.localScale = new Vector3(0.18f,
                        m_landmarkRoads.GuardRailHeight, 0.18f);
                    post.GetComponent<Renderer>().sharedMaterial = material;

                    Vector3 railPoint = centre + right * (side * sideOffset) +
                                        Vector3.up * (m_landmarkRoads.GuardRailHeight * 0.72f);
                    if (hasPreviousRailPoint)
                    {
                        Vector3 segment = railPoint - previousRailPoint;
                        GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        rail.name = $"GuardRail_{side}_{step:00}";
                        rail.transform.SetParent(root, true);
                        rail.transform.SetPositionAndRotation(
                            (previousRailPoint + railPoint) * 0.5f,
                            Quaternion.LookRotation(segment.normalized, Vector3.up));
                        rail.transform.localScale = new Vector3(0.16f, 0.22f,
                            segment.magnitude);
                        rail.GetComponent<Renderer>().sharedMaterial = material;
                    }
                    previousRailPoint = railPoint;
                    hasPreviousRailPoint = true;
                }
            }
        }
    }
}
