using System;
using System.Collections.Generic;
using CouchGuys.Player;
using CouchGuys.Gameplay.Couch;
using UnityEngine.InputSystem;
using CouchGuys.Input;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Owner-only circular minimap, rasterised once from generated world data.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerHealth))]
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodMap : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField, Min(5f)] private float m_visibleRadiusMetres = 65f;
        [SerializeField, Range(128, 1024)] private int m_textureResolution = 512;
        [SerializeField, Min(0.05f)] private float m_actorRefreshInterval = 0.5f;

        [Header("Navigation")]
        [SerializeField] private RectTransform m_deliveryMarkerPrefab;
        [SerializeField] private InputAction m_cycleDestination = new("Cycle Delivery Destination",
            InputActionType.Button, "<Keyboard>/n");
        [SerializeField, Range(0.3f, 0.9f)] private float m_secondaryDestinationScale = 0.65f;
        private readonly List<int> m_navigationIds = new();
        private readonly List<Vector2> m_destinationMarkerPositions = new();
        private int m_primaryDeliveryId = -1;
        private PlayerTruckInteractor m_truckInteractor;
        private PlayerHoverboardInteractor m_hoverboardInteractor;

        private static readonly Color32 Grass = new(74, 160, 82, 255);
        private static readonly Color32 Park = new(91, 190, 96, 255);
        private static readonly Color32 Road = new(91, 102, 113, 255);
        private static readonly Color32 RoadEdge = new(196, 188, 151, 255);
        private static readonly Color32 House = new(229, 157, 93, 255);
        private static readonly Color32 Landmark = new(72, 177, 204, 255);
        private static readonly Color32 Depot = new(247, 206, 83, 255);

        private sealed class ActorMarker
        {
            public RectTransform Rect;
            public RectTransform ExpandedRect;
            public Text Label;
            public Text ExpandedLabel;
        }

        private readonly Dictionary<PlayerHealth, ActorMarker> m_actorMarkers = new();
        private readonly Dictionary<int, ActorMarker> m_couchMarkers = new();
        private readonly List<int> m_removedCouches = new();
        private PlayerCouchCarrier m_carrier;
        private readonly List<PlayerHealth> m_removedActors = new(8);
        private PlayerWeaponHud m_hud;
        private PlayerInputReader m_input;
        private RectTransform m_expandedRoot;
        private RawImage m_expandedImage;
        private RectTransform m_expandedMarkers;
        private RectTransform m_expandedWaypoint;
        private NetworkObject m_networkObject;
        private PlayerHealth m_localHealth;
        private NeighbourhoodGenerator m_generator;
        private DeliveryManager m_deliveryManager;
        private GameObject m_canvasRoot;
        private RectTransform m_markerLayer;
        private RawImage m_mapImage;
        private RectTransform m_waypointRect;
        private Image m_waypointImage;
        private Text m_distanceText;
        private Texture2D m_mapTexture;
        private Sprite m_arrowSprite;
        private Sprite m_aiSprite;
        private Sprite m_waypointSprite;
        private Bounds m_mapBounds;
        private float m_markerRadius;
        private float m_nextActorRefreshTime;
        private int m_lastDistanceMetres = -1;
        private bool m_generatorSubscribed;
        private DeliveryDestination m_cachedDestination;
        private GeneratedProperty m_cachedDestinationProperty;

        private void Awake()
        {
            m_hud = GetComponent<PlayerWeaponHud>();
            m_carrier = GetComponent<PlayerCouchCarrier>();
            m_truckInteractor = GetComponent<PlayerTruckInteractor>();
            m_hoverboardInteractor = GetComponent<PlayerHoverboardInteractor>();
            m_input = GetComponent<PlayerInputReader>();
            m_networkObject = GetComponent<NetworkObject>();
            m_localHealth = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            if (m_networkObject == null || !m_networkObject.IsOwner || m_localHealth.IsAiTeammate ||
                m_hud == null || m_hud.View == null)
            {
                if (m_mapTexture != null) DestroyMinimap();
                return;
            }
            if (m_generator == null || m_deliveryManager == null) ResolveWorldReferences();
            if (m_canvasRoot == null && m_generator != null && m_generator.HasGeneratedNeighbourhood)
                BuildMinimap();
            if (m_canvasRoot == null) return;

            if (Time.unscaledTime >= m_nextActorRefreshTime)
            {
                RefreshActors();
                m_nextActorRefreshTime = Time.unscaledTime + m_actorRefreshInterval;
            }
            UpdateMapPosition();
            UpdateActorMarkers();
            RefreshNavigation();
            UpdateWaypoint();
            UpdateCouchMarkers();
        }

        private void LateUpdate()
        {
            if (m_expandedRoot == null || m_canvasRoot == null) return;
            // Menus resolve in Update, so they always take priority over this passive overlay.
            if (m_input == null || !m_input.CanShowMap || !m_canvasRoot.activeInHierarchy)
            {
                if (m_expandedRoot.gameObject.activeSelf) m_expandedRoot.gameObject.SetActive(false);
                return;
            }
            if (m_input.MapPressedThisFrame)
                m_expandedRoot.gameObject.SetActive(!m_expandedRoot.gameObject.activeSelf);
            if (!m_expandedRoot.gameObject.activeInHierarchy) return;

            RectTransform mapRect = m_expandedImage.rectTransform;
            Vector2 available = ((RectTransform)mapRect.parent).rect.size;
            // Inset the map enough to keep edge markers inside the available area.
            float scale = Mathf.Min(Mathf.Max(1f, available.x - 36f) / m_mapBounds.size.x,
                Mathf.Max(1f, available.y - 36f) / m_mapBounds.size.z);
            mapRect.sizeDelta = new Vector2(m_mapBounds.size.x, m_mapBounds.size.z) * scale;

            foreach (var entry in m_actorMarkers)
            {
                PlayerHealth actor = entry.Key;
                if (actor == null) continue;
                // Unowned hostile bots are not teammates. Only consume replicated player/AI data.
                bool teammate = actor == m_localHealth || actor.IsAiTeammate || actor.Owner.IsValid;
                if (entry.Value.ExpandedRect == null && teammate)
                {
                    entry.Value.ExpandedRect = Instantiate(entry.Value.Rect, m_expandedMarkers, false);
                    entry.Value.ExpandedRect.GetComponent<Image>().raycastTarget = false;
                }
                RectTransform marker = entry.Value.ExpandedRect;
                if (marker == null) continue;
                marker.gameObject.SetActive(teammate && actor.isActiveAndEnabled && actor.IsAlive);
                if (!marker.gameObject.activeSelf) continue;
                marker.anchoredPosition = ExpandedPosition(actor.transform.position, scale);
                Vector3 forward = actor.transform.forward;
                marker.localRotation = Quaternion.Euler(0f, 0f,
                    -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
            }

            if (m_expandedWaypoint == null)
            {
                m_expandedWaypoint = Instantiate(m_waypointRect, m_expandedMarkers, false);
                m_expandedWaypoint.GetComponent<Image>().sprite = m_waypointSprite;
                m_expandedWaypoint.GetComponent<Image>().raycastTarget = false;
                m_expandedWaypoint.sizeDelta = Vector2.one * 34f;
                m_expandedWaypoint.localRotation = Quaternion.identity;
            }
            DeliveryDestination destination = PrimaryDestination;
            bool active = destination != null && destination.CanReceiveDelivery;
            m_expandedWaypoint.gameObject.SetActive(active);
            if (active) m_expandedWaypoint.anchoredPosition = ExpandedPosition(destination.transform.position, scale);
            UpdateExpandedCouches(scale);
        }

        private Vector2 ExpandedPosition(Vector3 world, float scale) =>
            new Vector2(world.x - m_mapBounds.center.x, world.z - m_mapBounds.center.z) * scale;

        private void ResolveWorldReferences()
        {
            m_generator = FindFirstObjectByType<NeighbourhoodGenerator>();
            m_deliveryManager = FindFirstObjectByType<DeliveryManager>();
            if (m_generator != null && !m_generatorSubscribed)
            {
                m_generator.RegionGenerated += OnRegionGenerated;
                m_generator.RegionClearing += OnRegionClearing;
                m_generatorSubscribed = true;
            }
        }

        private void OnRegionGenerated(NeighbourhoodGenerator generator)
        {
            if (m_networkObject != null && m_networkObject.IsOwner) RebuildMinimap();
        }

        private void OnRegionClearing(NeighbourhoodGenerator generator) => DestroyMinimap();

        private void RebuildMinimap()
        {
            DestroyMinimap();
            if (m_generator != null && m_generator.HasGeneratedNeighbourhood) BuildMinimap();
        }

        private void BuildMinimap()
        {
            if (m_hud == null || m_hud.View == null || m_hud.View.m_minimapRoot == null) return;
            RectTransform root = m_hud.View.m_minimapRoot;
            m_canvasRoot = root.gameObject;
            m_mapBounds = m_generator.GeneratedWorldBounds;
            m_mapBounds.size = new Vector3(Mathf.Max(1f, m_mapBounds.size.x),
                m_mapBounds.size.y, Mathf.Max(1f, m_mapBounds.size.z));
            BuildStaticTexture();
            BuildSprites();

            m_mapImage = root.Find("MapMask/StaticMap").GetComponent<RawImage>();
            m_mapImage.texture = m_mapTexture;
            m_markerLayer = (RectTransform)root.Find("MapMask/DynamicMarkers");
            m_canvasRoot.SetActive(true);
            Canvas.ForceUpdateCanvases();
            m_markerRadius = Mathf.Min(m_markerLayer.rect.width, m_markerLayer.rect.height) * 0.5f;
            Image local = m_markerLayer.Find("LocalPlayer").GetComponent<Image>();
            local.sprite = m_arrowSprite;
            m_actorMarkers[m_localHealth] = new ActorMarker { Rect = local.rectTransform };
            m_waypointImage = m_markerLayer.Find("DeliveryWaypoint").GetComponent<Image>();
            m_waypointImage.sprite = m_waypointSprite;
            m_waypointRect = m_waypointImage.rectTransform;
            m_distanceText = root.Find("DeliveryDistance").GetComponent<Text>();

            m_expandedRoot = m_hud.View.m_expandedMapRoot;
            if (m_expandedRoot != null)
            {
                m_expandedRoot.gameObject.SetActive(false);
                m_expandedImage = m_expandedRoot.Find("Viewport/StaticMap").GetComponent<RawImage>();
                m_expandedImage.texture = m_mapTexture;
                m_expandedMarkers = (RectTransform)m_expandedImage.transform.Find("DynamicMarkers");
            }

            RefreshActors();
            UpdateMapPosition();
            UpdateActorMarkers();
            UpdateWaypoint();
        }

        private void BuildStaticTexture()
        {
            int resolution = Mathf.Clamp(m_textureResolution, 128, 1024);
            Color32[] pixels = new Color32[resolution * resolution];
            Array.Fill(pixels, Grass);
            Transform[] generatedTransforms = m_generator.GetComponentsInChildren<Transform>(true);
            DrawNamedLandmarks(pixels, resolution, generatedTransforms, true);

            IReadOnlyList<GeneratedRoad> roads = m_generator.GeneratedRoads;
            for (int i = 0; i < roads.Count; i++)
            {
                GeneratedRoad road = roads[i];
                if (road == null) continue;
                DrawRect(pixels, resolution, road.transform.position, road.transform.right,
                    road.transform.forward, Vector2.one * m_generator.TileSize * 0.9f, RoadEdge);
                DrawRect(pixels, resolution, road.transform.position, road.transform.right,
                    road.transform.forward, Vector2.one * m_generator.TileSize * 0.72f, Road);
            }

            IReadOnlyList<GeneratedProperty> properties = m_generator.GeneratedProperties;
            for (int i = 0; i < properties.Count; i++)
            {
                GeneratedProperty property = properties[i];
                if (property == null) continue;
                Vector2 size = new(property.GridFootprint.x * m_generator.TileSize * 0.66f,
                    property.GridFootprint.y * m_generator.TileSize * 0.66f);
                GeneratedLot lot = property.GetComponent<GeneratedLot>();
                Color32 colour = lot != null && lot.LotType != LotType.Residential ? Landmark : House;
                DrawRect(pixels, resolution, property.transform.position, property.transform.right,
                    property.transform.forward, size, colour);
            }

            StartingArea start = m_generator.GeneratedStartingArea;
            if (start != null)
            {
                Vector2 size = new(start.GridFootprint.x * m_generator.TileSize * 0.8f,
                    start.GridFootprint.y * m_generator.TileSize * 0.8f);
                DrawRect(pixels, resolution, start.transform.position, start.transform.right,
                    start.transform.forward, size, Depot);
            }
            DrawNamedLandmarks(pixels, resolution, generatedTransforms, false);

            m_mapTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "Generated Neighbourhood Minimap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_mapTexture.SetPixels32(pixels);
            m_mapTexture.Apply(false, true);
        }

        private void DrawNamedLandmarks(
            Color32[] pixels,
            int resolution,
            Transform[] items,
            bool parksOnly)
        {
            for (int i = 0; i < items.Length; i++)
            {
                Transform item = items[i];
                string itemName = item.name;
                bool isPark = itemName.StartsWith("Park_", StringComparison.OrdinalIgnoreCase);
                if (parksOnly != isPark) continue;
                bool isLandmark = itemName.StartsWith("Landmark_", StringComparison.OrdinalIgnoreCase) ||
                    itemName.IndexOf("Fountain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    itemName.IndexOf("Gazebo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    itemName.IndexOf("Statue", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isPark && !isLandmark) continue;
                EnvironmentPlacementMetadata metadata = item.GetComponent<EnvironmentPlacementMetadata>();
                Vector2 size = metadata != null ? metadata.Footprint :
                    Vector2.one * m_generator.TileSize * (isPark ? 2.5f : 0.7f);
                DrawRect(pixels, resolution, item.position, item.right, item.forward,
                    size, isPark ? Park : Landmark);
            }
        }

        private void DrawRect(Color32[] pixels, int resolution, Vector3 centre, Vector3 worldRight,
            Vector3 worldForward, Vector2 size, Color32 colour)
        {
            Vector2 right = new(worldRight.x, worldRight.z);
            Vector2 forward = new(worldForward.x, worldForward.z);
            if (right.sqrMagnitude < 0.001f) right = Vector2.right;
            if (forward.sqrMagnitude < 0.001f) forward = Vector2.up;
            right.Normalize();
            forward.Normalize();
            Vector2 half = size * 0.5f;
            float extentX = Mathf.Abs(right.x) * half.x + Mathf.Abs(forward.x) * half.y;
            float extentZ = Mathf.Abs(right.y) * half.x + Mathf.Abs(forward.y) * half.y;
            int minX = WorldToPixel(centre.x - extentX, m_mapBounds.min.x, m_mapBounds.size.x, resolution);
            int maxX = WorldToPixel(centre.x + extentX, m_mapBounds.min.x, m_mapBounds.size.x, resolution);
            int minY = WorldToPixel(centre.z - extentZ, m_mapBounds.min.z, m_mapBounds.size.z, resolution);
            int maxY = WorldToPixel(centre.z + extentZ, m_mapBounds.min.z, m_mapBounds.size.z, resolution);
            for (int y = minY; y <= maxY; y++)
            {
                float worldZ = PixelToWorld(y, m_mapBounds.min.z, m_mapBounds.size.z, resolution);
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 offset = new(PixelToWorld(x, m_mapBounds.min.x, m_mapBounds.size.x, resolution) - centre.x,
                        worldZ - centre.z);
                    if (Mathf.Abs(Vector2.Dot(offset, right)) <= half.x &&
                        Mathf.Abs(Vector2.Dot(offset, forward)) <= half.y)
                        pixels[y * resolution + x] = colour;
                }
            }
        }

        private static int WorldToPixel(float value, float minimum, float size, int resolution) =>
            Mathf.Clamp(Mathf.RoundToInt((value - minimum) / size * (resolution - 1)), 0, resolution - 1);

        private static float PixelToWorld(int pixel, float minimum, float size, int resolution) =>
            minimum + pixel / (float)(resolution - 1) * size;

        private void BuildSprites()
        {
            m_arrowSprite = ProceduralSprite("Player Arrow", 64, (x, y) =>
                y > -0.9f && y < 0.95f && Mathf.Abs(x) < (0.95f - y) * 0.48f + 0.08f);
            m_aiSprite = ProceduralSprite("AI Diamond", 64, (x, y) =>
                Mathf.Abs(x) + Mathf.Abs(y * 0.78f) < 0.85f);
            m_waypointSprite = ProceduralSprite("Waypoint Ring", 64, (x, y) =>
            {
                float radius = Mathf.Sqrt(x * x + y * y);
                return (radius > 0.55f && radius < 0.92f) || (y > 0.55f && Mathf.Abs(x) < 0.12f);
            });
        }

        private static Sprite ProceduralSprite(string name, int size, Func<float, float, bool> fill)
        {
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
            { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                pixels[y * size + x] = fill(nx, ny) ? new Color32(255, 255, 255, 255) :
                    new Color32(255, 255, 255, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private void RefreshActors()
        {
            IReadOnlyList<PlayerHealth> actors = PlayerHealth.Instances;
            for (int i = 0; i < actors.Count; i++)
            {
                PlayerHealth actor = actors[i];
                if (actor == null || !actor.isActiveAndEnabled || actor == m_localHealth ||
                    m_actorMarkers.ContainsKey(actor)) continue;
                bool ai = actor.IsAiTeammate;
                UnityEngine.UI.Image image = Image(ai ? "AI Teammate" : "Player Teammate", m_markerLayer,
                    ai ? new Color(0.35f, 1f, 0.42f, 1f) : new Color(0.25f, 0.58f, 1f, 1f),
                    ai ? m_aiSprite : m_arrowSprite);
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                image.rectTransform.sizeDelta = ai ? new Vector2(17f, 17f) : new Vector2(18f, 22f);
                m_actorMarkers.Add(actor, new ActorMarker { Rect = image.rectTransform });
            }

            m_removedActors.Clear();
            foreach (KeyValuePair<PlayerHealth, ActorMarker> entry in m_actorMarkers)
            {
                if (entry.Key == m_localHealth || (entry.Key != null && entry.Key.isActiveAndEnabled)) continue;
                if (entry.Value.Rect != null) Destroy(entry.Value.Rect.gameObject);
                if (entry.Value.ExpandedRect != null) Destroy(entry.Value.ExpandedRect.gameObject);
                m_removedActors.Add(entry.Key);
            }
            for (int i = 0; i < m_removedActors.Count; i++) m_actorMarkers.Remove(m_removedActors[i]);
        }

        private void UpdateMapPosition()
        {
            if (m_mapImage == null || m_localHealth == null) return;
            Vector3 position = m_localHealth.transform.position;
            float diameter = m_visibleRadiusMetres * 2f;
            float width = diameter / m_mapBounds.size.x;
            float height = diameter / m_mapBounds.size.z;
            float centreX = (position.x - m_mapBounds.min.x) / m_mapBounds.size.x;
            float centreY = (position.z - m_mapBounds.min.z) / m_mapBounds.size.z;
            m_mapImage.uvRect = new Rect(centreX - width * 0.5f, centreY - height * 0.5f, width, height);
        }

        private void UpdateActorMarkers()
        {
            if (m_localHealth == null) return;
            Vector3 localPosition = m_localHealth.transform.position;
            foreach (KeyValuePair<PlayerHealth, ActorMarker> entry in m_actorMarkers)
            {
                PlayerHealth actor = entry.Key;
                RectTransform marker = entry.Value.Rect;
                if (actor == null || marker == null) continue;
                Vector2 offset = Offset(localPosition, actor.transform.position);
                bool visible = offset.sqrMagnitude <= m_visibleRadiusMetres * m_visibleRadiusMetres;
                marker.gameObject.SetActive(visible && actor.IsAlive);
                if (!visible || !actor.IsAlive) continue;
                marker.anchoredPosition = offset / m_visibleRadiusMetres * m_markerRadius;
                Vector3 forward = actor.transform.forward;
                marker.localRotation = Quaternion.Euler(0f, 0f,
                    -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
            }
        }

        private void UpdateWaypoint()
        {
            DeliveryDestination destination = PrimaryDestination;
            bool active = destination != null && destination.CanReceiveDelivery && m_localHealth != null;
            m_waypointRect.gameObject.SetActive(active);
            m_distanceText.gameObject.SetActive(active);
            if (!active) { m_lastDistanceMetres = -1; return; }

            if (destination != m_cachedDestination)
            {
                m_cachedDestination = destination;
                m_cachedDestinationProperty = destination.GetComponent<GeneratedProperty>();
            }

            Vector3 playerPosition = m_localHealth.transform.position;
            Vector2 deliveryOffset = Offset(playerPosition, destination.DropPosition.position);
            Vector2 markerOffset = Offset(playerPosition, destination.transform.position);
            float distance = deliveryOffset.magnitude;
            bool offscreen = distance > m_visibleRadiusMetres;
            Vector2 direction = distance > 0.01f ? deliveryOffset / distance : Vector2.up;
            m_waypointRect.anchoredPosition = offscreen ? direction * (m_markerRadius - 18f) :
                markerOffset / m_visibleRadiusMetres * m_markerRadius;
            m_waypointRect.localRotation = offscreen ? Quaternion.Euler(0f, 0f,
                -Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg) : Quaternion.identity;
            m_waypointImage.sprite = offscreen ? m_arrowSprite : m_waypointSprite;
            float highlightSize = 34f;
            if (!offscreen && m_cachedDestinationProperty != null)
            {
                Vector2Int footprint = m_cachedDestinationProperty.GridFootprint;
                highlightSize = Mathf.Clamp(
                    Mathf.Max(footprint.x, footprint.y) * m_generator.TileSize /
                    m_visibleRadiusMetres * m_markerRadius,
                    30f,
                    62f);
            }
            m_waypointRect.sizeDelta = offscreen ? new Vector2(24f, 30f) :
                Vector2.one * highlightSize;

            int metres = Mathf.RoundToInt(distance);
            if (metres != m_lastDistanceMetres)
            {
                m_lastDistanceMetres = metres;
                m_distanceText.text = $"DELIVERY  {metres} m";
            }
        }

        // Consume replicated attachment/occupancy, never hierarchy guesses or server-only cargo lists.
        private bool IsTransportedByLocalPlayer(CouchCarryController couch)
        {
            if (couch == null || !couch.IsSpawned) return false;
            if (m_carrier != null && m_carrier.CarriedCouch == couch) return true;
            if (couch.LoadedTruckObject != null && m_truckInteractor != null &&
                m_truckInteractor.CurrentTruck != null &&
                couch.LoadedTruckObject == m_truckInteractor.CurrentTruck.NetworkObject) return true;
            if (couch.MountedSled != null && m_carrier != null &&
                couch.MountedSled == m_carrier.CarriedSled) return true;
            return couch.MountedHoverboardObject != null && m_hoverboardInteractor != null &&
                m_hoverboardInteractor.CurrentBoard != null &&
                couch.MountedHoverboardObject == m_hoverboardInteractor.CurrentBoard.NetworkObject;
        }

        private DeliveryDestination PrimaryDestination => m_deliveryManager != null &&
            m_deliveryManager.TryGetDelivery(m_primaryDeliveryId, out var job)
                ? m_deliveryManager.GetDestination(job.DestinationIndex) : null;

        private bool TryGetNavigationDestination(DeliveryInstance job, out DeliveryDestination destination)
        {
            destination = null;
            if (job.Status != DeliveryState.Transport || job.Couch == null ||
                !job.Couch.TryGetComponent(out CouchCarryController couch) ||
                !IsTransportedByLocalPlayer(couch)) return false;
            destination = m_deliveryManager.GetDestinationForCouch(couch);
            return destination != null && destination.CanReceiveDelivery;
        }

        private void RefreshNavigation()
        {
            m_navigationIds.Clear();
            int earliestId = -1;
            float earliestTime = float.PositiveInfinity;
            if (m_deliveryManager != null)
                foreach (var job in m_deliveryManager.Deliveries)
                {
                    if (!TryGetNavigationDestination(job, out _)) continue;
                    m_navigationIds.Add(job.Id);
                    float remaining = m_deliveryManager.GetRemainingTime(job);
                    if (remaining < earliestTime || (remaining == earliestTime && job.Id < earliestId))
                    {
                        earliestId = job.Id;
                        earliestTime = remaining;
                    }
                }
            // Stable numbering; retain the local choice until it leaves the valid set.
            m_navigationIds.Sort();
            SelectPrimaryDestination(earliestId, m_input != null && m_input.CanNavigateDeliveries &&
                m_cycleDestination != null && m_cycleDestination.WasPressedThisFrame());
        }

        private void SelectPrimaryDestination(int earliestId, bool cycle)
        {
            if (!m_navigationIds.Contains(m_primaryDeliveryId)) m_primaryDeliveryId = earliestId;
            if (m_navigationIds.Count > 1 && cycle)
                m_primaryDeliveryId = m_navigationIds[(m_navigationIds.IndexOf(m_primaryDeliveryId) + 1) % m_navigationIds.Count];
        }

        private void UpdateCouchMarkers()
        {
            if (m_deliveryManager == null || m_waypointRect == null || m_localHealth == null ||
                m_deliveryMarkerPrefab == null) return;
            m_destinationMarkerPositions.Clear();
            if (m_primaryDeliveryId >= 0) m_destinationMarkerPositions.Add(m_waypointRect.anchoredPosition);
            foreach (var job in m_deliveryManager.Deliveries)
            {
                if (!m_couchMarkers.TryGetValue(job.Id, out var marker))
                {
                    var rect = Instantiate(m_deliveryMarkerPrefab, m_markerLayer, false);
                    rect.name = $"DeliveryCouch_{job.Id}";
                    marker = new ActorMarker { Rect = rect, Label = rect.GetComponentInChildren<Text>(true) };
                    m_couchMarkers.Add(job.Id, marker);
                }
                bool destinationMarker = TryGetNavigationDestination(job, out var destination);
                bool visible = job.Status == DeliveryState.Transport && job.Couch != null && job.Couch.IsSpawned;
                marker.Rect.gameObject.SetActive(visible);
                if (!visible) continue;
                bool primary = destinationMarker && job.Id == m_primaryDeliveryId;
                StyleDeliveryMarker(marker.Rect, marker.Label, job.Id, destinationMarker, primary);
                Vector3 position = destinationMarker ? destination.transform.position : job.Couch.transform.position;
                Vector2 offset = Offset(m_localHealth.transform.position, position);
                marker.Rect.anchoredPosition = primary ? m_waypointRect.anchoredPosition :
                    Vector2.ClampMagnitude(offset / m_visibleRadiusMetres, 0.92f) * m_markerRadius;
                if (destinationMarker && !primary)
                    marker.Rect.anchoredPosition = SeparateDestinationMarker(marker.Rect.anchoredPosition,
                        m_markerRadius * 0.92f);
                marker.Rect.SetAsLastSibling();
            }
            m_waypointRect.SetAsLastSibling();
            if (m_couchMarkers.TryGetValue(m_primaryDeliveryId, out var primaryMarker))
                primaryMarker.Rect.SetAsLastSibling();
            m_removedCouches.Clear();
            foreach (var entry in m_couchMarkers)
                if (!m_deliveryManager.TryGetDelivery(entry.Key, out _))
                {
                    Destroy(entry.Value.Rect.gameObject);
                    if (entry.Value.ExpandedRect != null) Destroy(entry.Value.ExpandedRect.gameObject);
                    m_removedCouches.Add(entry.Key);
                }
            foreach (int id in m_removedCouches) m_couchMarkers.Remove(id);
        }

        private Vector2 SeparateDestinationMarker(Vector2 desired, float radius)
        {
            float spacing = 34f * m_secondaryDestinationScale + 2f;
            Vector2 candidate = desired;
            // Nearby houses can project to the same edge point. Keep their numbers readable.
            for (int attempt = 0; attempt < 64; attempt++)
            {
                bool overlaps = false;
                foreach (Vector2 placed in m_destinationMarkerPositions)
                    if ((candidate - placed).sqrMagnitude < spacing * spacing) { overlaps = true; break; }
                if (!overlaps) break;
                float angle = attempt * 2.4f;
                candidate = Vector2.ClampMagnitude(desired + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) *
                    spacing * Mathf.Sqrt(attempt + 1), radius);
            }
            m_destinationMarkerPositions.Add(candidate);
            return candidate;
        }

        private void StyleDeliveryMarker(RectTransform rect, Text label, int id, bool destination, bool primary)
        {
            var image = rect.GetComponent<Image>();
            // Existing waypoint supplies the primary highlight/edge arrow; this marker adds its number.
            image.enabled = !primary;
            image.sprite = destination ? m_waypointSprite : m_hud.View.m_deliveryCouchSprite;
            image.color = destination ? new Color(1f, 0.85f, 0.25f, 0.75f) : new Color(1f, 0.72f, 0.12f);
            image.preserveAspect = true;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = destination ? Vector2.one * (primary ? 34f : 34f * m_secondaryDestinationScale) :
                new Vector2(28f, 22f);
            if (label == null) return;
            label.gameObject.SetActive(destination);
            if (destination)
            {
                label.text = (m_navigationIds.IndexOf(id) + 1).ToString();
                label.color = primary ? Color.white : new Color(1f, 1f, 1f, 0.85f);
                label.fontSize = primary ? 18 : 14;
            }
        }

        private void UpdateExpandedCouches(float scale)
        {
            if (m_deliveryManager == null) return;
            m_destinationMarkerPositions.Clear();
            if (m_primaryDeliveryId >= 0) m_destinationMarkerPositions.Add(m_expandedWaypoint.anchoredPosition);
            foreach (var job in m_deliveryManager.Deliveries)
            {
                if (!m_couchMarkers.TryGetValue(job.Id, out var marker)) continue;
                if (marker.ExpandedRect == null)
                {
                    marker.ExpandedRect = Instantiate(m_deliveryMarkerPrefab, m_expandedMarkers, false);
                    marker.ExpandedLabel = marker.ExpandedRect.GetComponentInChildren<Text>(true);
                }
                bool active = job.Status == DeliveryState.Transport && job.Couch != null && job.Couch.IsSpawned;
                marker.ExpandedRect.gameObject.SetActive(active);
                if (!active) continue;
                bool destinationMarker = TryGetNavigationDestination(job, out var destination);
                StyleDeliveryMarker(marker.ExpandedRect, marker.ExpandedLabel, job.Id,
                    destinationMarker, destinationMarker && job.Id == m_primaryDeliveryId);
                marker.ExpandedRect.anchoredPosition = ExpandedPosition(destinationMarker ?
                    destination.transform.position : job.Couch.transform.position, scale);
                if (destinationMarker && job.Id != m_primaryDeliveryId)
                    marker.ExpandedRect.anchoredPosition = SeparateDestinationMarker(
                        marker.ExpandedRect.anchoredPosition, float.PositiveInfinity);
                marker.ExpandedRect.SetAsLastSibling();
            }
            m_expandedWaypoint.SetAsLastSibling();
            if (m_couchMarkers.TryGetValue(m_primaryDeliveryId, out var primaryMarker) && primaryMarker.ExpandedRect != null)
                primaryMarker.ExpandedRect.SetAsLastSibling();
        }

        private static Vector2 Offset(Vector3 from, Vector3 to) => new(to.x - from.x, to.z - from.z);

        private void DestroyMinimap()
        {
            foreach (var marker in m_couchMarkers.Values)
            {
                if (marker.Rect != null) Destroy(marker.Rect.gameObject);
                if (marker.ExpandedRect != null) Destroy(marker.ExpandedRect.gameObject);
            }
            m_couchMarkers.Clear();
            m_navigationIds.Clear();
            m_primaryDeliveryId = -1;
            foreach (var marker in m_actorMarkers)
            {
                if (marker.Key != m_localHealth && marker.Value.Rect != null) Destroy(marker.Value.Rect.gameObject);
                if (marker.Value.ExpandedRect != null) Destroy(marker.Value.ExpandedRect.gameObject);
            }
            if (m_expandedWaypoint != null) Destroy(m_expandedWaypoint.gameObject);
            if (m_expandedImage != null) m_expandedImage.texture = null;
            if (m_expandedRoot != null) m_expandedRoot.gameObject.SetActive(false);
            m_expandedRoot = null;
            m_expandedImage = null;
            m_expandedMarkers = null;
            m_expandedWaypoint = null;
            m_actorMarkers.Clear();
            if (m_mapImage != null) m_mapImage.texture = null;
            if (m_canvasRoot != null) m_canvasRoot.SetActive(false);
            m_lastDistanceMetres = -1;
            m_nextActorRefreshTime = 0f;
            m_canvasRoot = null;
            m_markerLayer = null;
            m_mapImage = null;
            m_waypointRect = null;
            m_waypointImage = null;
            m_distanceText = null;
            m_cachedDestination = null;
            m_cachedDestinationProperty = null;
            DestroyTexture(ref m_mapTexture);
            DestroySprite(ref m_arrowSprite);
            DestroySprite(ref m_aiSprite);
            DestroySprite(ref m_waypointSprite);
        }

        private void OnEnable() => m_cycleDestination?.Enable();

        private void OnDisable()
        {
            m_cycleDestination?.Disable();
            DestroyMinimap();
        }

        private void OnDestroy()
        {
            if (m_generatorSubscribed && m_generator != null)
            {
                m_generator.RegionGenerated -= OnRegionGenerated;
                m_generator.RegionClearing -= OnRegionClearing;
            }
            DestroyMinimap();
            m_cycleDestination?.Dispose();
        }

        private static void DestroySprite(ref Sprite sprite)
        {
            if (sprite == null) return;
            Texture2D texture = sprite.texture;
            Destroy(sprite);
            if (texture != null) Destroy(texture);
            sprite = null;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null) Destroy(texture);
            texture = null;
        }

        private static UnityEngine.UI.Image Image(string name, Transform parent, Color colour, Sprite sprite)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            UnityEngine.UI.Image image = go.GetComponent<UnityEngine.UI.Image>();
            image.color = colour;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

    }
}
