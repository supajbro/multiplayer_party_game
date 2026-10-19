using System;
using System.Collections.Generic;
using CouchGuys.Player;
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
        [SerializeField, Min(160f)] private float m_minimapSize = 270f;
        [SerializeField, Min(5f)] private float m_visibleRadiusMetres = 65f;
        [SerializeField, Min(0f)] private float m_screenMargin = 24f;
        [SerializeField, Range(128, 1024)] private int m_textureResolution = 512;
        [SerializeField, Min(0.05f)] private float m_actorRefreshInterval = 0.5f;

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
        }

        private readonly Dictionary<PlayerHealth, ActorMarker> m_actorMarkers = new();
        private readonly List<PlayerHealth> m_removedActors = new(8);
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
        private Sprite m_circleSprite;
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
            m_networkObject = GetComponent<NetworkObject>();
            m_localHealth = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            if (m_networkObject == null || !m_networkObject.IsOwner) return;
            if (m_generator == null) ResolveWorldReferences();
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
            UpdateWaypoint();
        }

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
            m_mapBounds = m_generator.GeneratedWorldBounds;
            m_mapBounds.size = new Vector3(Mathf.Max(1f, m_mapBounds.size.x),
                m_mapBounds.size.y, Mathf.Max(1f, m_mapBounds.size.z));
            BuildStaticTexture();
            BuildSprites();

            m_canvasRoot = new GameObject("CircularMinimapCanvas", typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = m_canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            CanvasScaler scaler = m_canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image frame = Image("MinimapFrame", m_canvasRoot.transform,
                new Color(0.04f, 0.055f, 0.075f, 0.96f), m_circleSprite);
            RectTransform frameRect = frame.rectTransform;
            frameRect.anchorMin = frameRect.anchorMax = Vector2.one;
            frameRect.pivot = Vector2.one;
            frameRect.anchoredPosition = new Vector2(-m_screenMargin, -m_screenMargin);
            frameRect.sizeDelta = Vector2.one * m_minimapSize;

            Image maskImage = Image("MapMask", frameRect, Color.white, m_circleSprite);
            Stretch(maskImage.rectTransform, 7f);
            Mask mask = maskImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject mapObject = new("StaticMap", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            mapObject.transform.SetParent(maskImage.transform, false);
            m_mapImage = mapObject.GetComponent<RawImage>();
            m_mapImage.texture = m_mapTexture;
            m_mapImage.raycastTarget = false;
            Stretch(m_mapImage.rectTransform, 0f);

            GameObject markerObject = new("DynamicMarkers", typeof(RectTransform));
            markerObject.transform.SetParent(maskImage.transform, false);
            m_markerLayer = markerObject.GetComponent<RectTransform>();
            Stretch(m_markerLayer, 0f);
            m_markerRadius = (m_minimapSize - 14f) * 0.5f;

            Compass("N", frameRect, new Vector2(0f, -10f), new Vector2(0.5f, 1f));
            Compass("E", frameRect, new Vector2(-10f, 0f), new Vector2(1f, 0.5f));
            Compass("S", frameRect, new Vector2(0f, 10f), new Vector2(0.5f, 0f));
            Compass("W", frameRect, new Vector2(10f, 0f), new Vector2(0f, 0.5f));

            UnityEngine.UI.Image local = Image("LocalPlayer", m_markerLayer,
                new Color(0.12f, 0.95f, 1f, 1f), m_arrowSprite);
            local.rectTransform.anchorMin = local.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            local.rectTransform.sizeDelta = new Vector2(25f, 30f);
            m_actorMarkers[m_localHealth] = new ActorMarker { Rect = local.rectTransform };

            m_waypointImage = Image("DeliveryWaypoint", m_markerLayer,
                new Color(1f, 0.76f, 0.08f, 1f), m_waypointSprite);
            m_waypointRect = m_waypointImage.rectTransform;
            m_waypointRect.anchorMin = m_waypointRect.anchorMax = new Vector2(0.5f, 0.5f);
            m_waypointRect.sizeDelta = new Vector2(34f, 34f);
            m_waypointRect.gameObject.SetActive(false);

            m_distanceText = Text("DeliveryDistance", frameRect, string.Empty, 22);
            RectTransform distanceRect = m_distanceText.rectTransform;
            distanceRect.anchorMin = distanceRect.anchorMax = new Vector2(0.5f, 0f);
            distanceRect.pivot = new Vector2(0.5f, 1f);
            distanceRect.anchoredPosition = new Vector2(0f, -5f);
            distanceRect.sizeDelta = new Vector2(m_minimapSize, 30f);
            m_distanceText.gameObject.SetActive(false);

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
            m_circleSprite = ProceduralSprite("Minimap Circle", 128, (x, y) => x * x + y * y <= 0.98f);
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
            DeliveryDestination destination = m_deliveryManager != null && m_deliveryManager.HasActiveDelivery
                ? m_deliveryManager.ActiveDestination : null;
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

        private static Vector2 Offset(Vector3 from, Vector3 to) => new(to.x - from.x, to.z - from.z);

        private void DestroyMinimap()
        {
            m_actorMarkers.Clear();
            if (m_canvasRoot != null) Destroy(m_canvasRoot);
            m_canvasRoot = null;
            m_markerLayer = null;
            m_mapImage = null;
            m_waypointRect = null;
            m_waypointImage = null;
            m_distanceText = null;
            m_cachedDestination = null;
            m_cachedDestinationProperty = null;
            DestroyTexture(ref m_mapTexture);
            DestroySprite(ref m_circleSprite);
            DestroySprite(ref m_arrowSprite);
            DestroySprite(ref m_aiSprite);
            DestroySprite(ref m_waypointSprite);
        }

        private void OnDestroy()
        {
            if (m_generatorSubscribed && m_generator != null)
            {
                m_generator.RegionGenerated -= OnRegionGenerated;
                m_generator.RegionClearing -= OnRegionClearing;
            }
            DestroyMinimap();
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

        private void Compass(string value, Transform parent, Vector2 position, Vector2 anchor)
        {
            UnityEngine.UI.Text label = Text("Compass " + value, parent, value, 19);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(28f, 28f);
        }

        private static UnityEngine.UI.Text Text(string name, Transform parent, string value, int fontSize)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Text));
            go.transform.SetParent(parent, false);
            UnityEngine.UI.Text text = go.GetComponent<UnityEngine.UI.Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            go.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            return text;
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

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
        }
    }
}
