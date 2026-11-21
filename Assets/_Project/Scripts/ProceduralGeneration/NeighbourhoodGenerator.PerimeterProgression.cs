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

        internal bool IsHillTreePositionClear(Vector3 worldPosition)
        {
            float clearance = Mathf.Max(m_borderSettings.TunnelClearance,
                m_terrainSettings.RoadHalfWidth + m_terrainSettings.RoadShoulderWidth);
            GridCoordinate nearest = WorldToGrid(worldPosition);
            int radius = Mathf.CeilToInt(clearance / m_roadTileSize) + 1;
            for (int y = nearest.Y - radius; y <= nearest.Y + radius; y++)
            {
                for (int x = nearest.X - radius; x <= nearest.X + radius; x++)
                {
                    var coordinate = new GridCoordinate(x, y);
                    if (!m_roadCells.Contains(coordinate)) continue;
                    Vector3 offset = worldPosition - GridToWorld(coordinate);
                    if (Mathf.Abs(offset.x) < clearance + m_roadTileSize * 0.5f &&
                        Mathf.Abs(offset.z) < clearance + m_roadTileSize * 0.5f) return false;
                }
            }
            foreach (TrafficRoadPortal portal in m_trafficRoadPortals)
            {
                Vector3 offset = worldPosition - portal.WorldPosition;
                float along = Vector3.Dot(offset, portal.WorldOutward);
                float across = Mathf.Abs(Vector3.Dot(offset,
                    Vector3.Cross(Vector3.up, portal.WorldOutward)));
                if (Mathf.Abs(along) < m_borderSettings.TunnelDepth * 0.5f + clearance &&
                    across < GetTunnelOuterHalfWidth() + clearance) return false;
            }
            return true;
        }

        private void CreateUphillExitRestriction()
        {
            if (!HasSummitExit) return;
            var configuration = UphillExitRestrictionConfiguration.Load();
            if (configuration == null || configuration.Prefab == null)
            {
                Debug.LogError("The uphill exit restriction prefab is missing.", this);
                return;
            }
            int baseY = m_gridHeight * (m_blockHeight + 1);
            Vector3 start = GridToWorld(LocalToGrid(0, baseY));
            Vector3 summit = SummitExitPortal.WorldPosition;
            start += Vector3.ProjectOnPlane(summit - start, Vector3.up).normalized * (m_roadTileSize * .5f);
            var restriction = Instantiate(configuration.Prefab, m_generatedRoot);
            restriction.name = "UphillExitRestriction";
            restriction.Initialise(this, start, summit,
                m_terrainSettings.RoadHalfWidth + Mathf.Max(m_terrainSettings.RoadShoulderWidth, m_landmarkRoads.EmbankmentBlendWidth),
                m_terrainSettings.RoadHalfWidth);
        }

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

        private float ApplySummitHighlands(float terrainHeight, float localY)
        {
            float boundaryY = m_gridHeight * (m_blockHeight + 1);
            if (!HasSummitExit || localY <= boundaryY)
            {
                return terrainHeight;
            }

            // Continue the climb across the entire rear of the map, rather than
            // leaving the exit road on a narrow ridge. Clamp beyond the summit
            // so the high ground continues as a plateau to the terrain edge.
            float roadY = Mathf.Min(localY, boundaryY + m_landmarkRoads.UphillLengthTiles);
            Vector3 roadPoint = LocalCoordinatesToWorld(0f, roadY, 0f);
            float hillHeight = SampleRoadSurfaceHeight(roadPoint) - m_gridOrigin.y;
            float blend = Mathf.SmoothStep(0f, 1f,
                (localY - boundaryY) * m_roadTileSize /
                Mathf.Max(m_roadTileSize, m_landmarkRoads.EmbankmentBlendWidth));
            return Mathf.Lerp(terrainHeight, hillHeight, blend);
        }

        private float CarveTrafficTunnelPassages(float height, Vector3 worldPosition)
        {
            if (!m_borderSettings.Enabled) return height;
            float halfWidth = m_borderSettings.TunnelWidth * 0.5f;
            float halfDepth = m_borderSettings.TunnelDepth * 0.5f;
            float blendWidth = Mathf.Max(1f, m_terrainSettings.RoadShoulderWidth);
            foreach (TrafficRoadPortal portal in m_trafficRoadPortals)
            {
                Vector3 offset = worldPosition - portal.WorldPosition;
                float along = Vector3.Dot(offset, portal.WorldOutward);
                float across = Mathf.Abs(Vector3.Dot(offset,
                    Vector3.Cross(Vector3.up, portal.WorldOutward)));
                float lateral = 1f - Mathf.SmoothStep(0f, 1f,
                    (across - halfWidth) / blendWidth);
                float longitudinal = 1f - Mathf.SmoothStep(0f, 1f,
                    (Mathf.Abs(along) - halfDepth - m_roadTileSize) / blendWidth);
                float roadHeight = portal.WorldPosition.y - m_gridOrigin.y +
                                   m_terrainSettings.RoadSurfaceOffset;
                height = Mathf.Lerp(height, Mathf.Min(height, roadHeight), lateral * longitudinal);
            }
            return height;
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
