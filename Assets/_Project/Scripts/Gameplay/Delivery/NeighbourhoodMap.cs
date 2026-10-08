using System.Collections.Generic;
using CouchGuys.Input;
using CouchGuys.Networking;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Owner-only, data-driven full-neighbourhood map.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerInputReader))]
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodMap : MonoBehaviour
    {
        private const float MapSize = 720f;
        private const float MapPadding = 24f;

        private readonly Dictionary<NetworkPlayerOwnership, RectTransform> m_playerMarkers = new();
        private NetworkObject m_networkObject;
        private PlayerInputReader m_input;
        private NeighbourhoodGenerator m_generator;
        private DeliveryManager m_deliveryManager;
        private GameObject m_canvasRoot;
        private RectTransform m_mapPanel;
        private RectTransform m_waypointMarker;
        private float m_worldToMapScale;
        private float m_nextPlayerRefreshTime;

        public bool IsOpen => m_canvasRoot != null && m_canvasRoot.activeSelf;

        private void Awake()
        {
            m_networkObject = GetComponent<NetworkObject>();
            m_input = GetComponent<PlayerInputReader>();
        }

        private void Update()
        {
            if (m_networkObject == null || !m_networkObject.IsOwner)
            {
                return;
            }

            if (m_input != null && m_input.MapPressedThisFrame)
            {
                ToggleMap();
            }

            if (m_canvasRoot == null || !m_canvasRoot.activeSelf)
            {
                return;
            }

            if (Time.unscaledTime >= m_nextPlayerRefreshTime)
            {
                RefreshPlayers();
                m_nextPlayerRefreshTime = Time.unscaledTime + 0.5f;
            }

            UpdateDynamicMarkers();
        }

        private void OnDestroy()
        {
            if (m_canvasRoot != null)
            {
                Destroy(m_canvasRoot);
            }
        }

        private void ToggleMap()
        {
            if (m_canvasRoot == null && !TryBuildMap())
            {
                return;
            }

            m_canvasRoot.SetActive(!m_canvasRoot.activeSelf);
            if (m_canvasRoot.activeSelf)
            {
                RefreshPlayers();
                UpdateDynamicMarkers();
            }
        }

        private bool TryBuildMap()
        {
            m_generator = FindFirstObjectByType<NeighbourhoodGenerator>();
            m_deliveryManager = FindFirstObjectByType<DeliveryManager>();
            if (m_generator == null || !m_generator.HasGeneratedNeighbourhood)
            {
                return false;
            }

            m_canvasRoot = new GameObject("NeighbourhoodMapCanvas", typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = m_canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = m_canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image panelImage = CreateImage("Map", m_canvasRoot.transform, new Color(0.035f, 0.045f, 0.055f, 0.96f));
            m_mapPanel = panelImage.rectTransform;
            m_mapPanel.anchorMin = m_mapPanel.anchorMax = new Vector2(0.5f, 0.5f);
            m_mapPanel.pivot = new Vector2(0.5f, 0.5f);
            m_mapPanel.sizeDelta = new Vector2(MapSize, MapSize);

            Bounds bounds = m_generator.GeneratedWorldBounds;
            float largestWorldSize = Mathf.Max(bounds.size.x, bounds.size.z, 1f);
            m_worldToMapScale = (MapSize - MapPadding * 2f) / largestWorldSize;

            float roadSize = Mathf.Max(2f, m_generator.TileSize * m_worldToMapScale * 0.72f);
            foreach (GeneratedRoad road in m_generator.GeneratedRoads)
            {
                RectTransform marker = CreateMarker("Road", new Color(0.26f, 0.29f, 0.32f, 1f), roadSize);
                marker.anchoredPosition = WorldToMap(road.transform.position);
            }

            foreach (GeneratedProperty property in m_generator.GeneratedProperties)
            {
                RectTransform marker = CreateMarker("House", new Color(0.24f, 0.48f, 0.28f, 1f), 7f);
                marker.anchoredPosition = WorldToMap(property.transform.position);
            }

            if (m_generator.GeneratedStartingArea != null)
            {
                RectTransform start = CreateMarker("Start", new Color(0.95f, 0.95f, 0.95f, 1f), 16f);
                start.anchoredPosition = WorldToMap(m_generator.GeneratedStartingArea.transform.position);
            }

            m_waypointMarker = CreateMarker("DeliveryWaypoint", new Color(1f, 0.55f, 0.05f, 1f), 22f);
            m_waypointMarker.localRotation = Quaternion.Euler(0f, 0f, 45f);
            m_canvasRoot.SetActive(false);
            return true;
        }

        private void RefreshPlayers()
        {
            NetworkPlayerOwnership[] players = FindObjectsByType<NetworkPlayerOwnership>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            HashSet<NetworkPlayerOwnership> currentPlayers = new();
            foreach (NetworkPlayerOwnership player in players)
            {
                if (player == null || !player.Owner.IsValid)
                {
                    continue;
                }

                currentPlayers.Add(player);
                if (!m_playerMarkers.ContainsKey(player))
                {
                    bool isLocal = player.IsOwner;
                    m_playerMarkers.Add(
                        player,
                        CreateMarker(
                            isLocal ? "LocalPlayer" : "Teammate",
                            isLocal ? new Color(0.1f, 1f, 1f, 1f) : new Color(0.2f, 0.55f, 1f, 1f),
                            isLocal ? 18f : 13f));
                }
            }

            List<NetworkPlayerOwnership> removed = null;
            foreach (KeyValuePair<NetworkPlayerOwnership, RectTransform> entry in m_playerMarkers)
            {
                if (entry.Key != null && currentPlayers.Contains(entry.Key))
                {
                    continue;
                }

                if (entry.Value != null)
                {
                    Destroy(entry.Value.gameObject);
                }

                removed ??= new List<NetworkPlayerOwnership>();
                removed.Add(entry.Key);
            }

            if (removed == null)
            {
                return;
            }

            foreach (NetworkPlayerOwnership player in removed)
            {
                m_playerMarkers.Remove(player);
            }
        }

        private void UpdateDynamicMarkers()
        {
            foreach (KeyValuePair<NetworkPlayerOwnership, RectTransform> entry in m_playerMarkers)
            {
                if (entry.Key != null && entry.Value != null)
                {
                    entry.Value.anchoredPosition = WorldToMap(entry.Key.transform.position);
                }
            }

            DeliveryDestination destination = m_deliveryManager != null
                ? m_deliveryManager.ActiveDestination
                : null;
            bool showWaypoint = destination != null && destination.CanReceiveDelivery;
            m_waypointMarker.gameObject.SetActive(showWaypoint);
            if (showWaypoint)
            {
                m_waypointMarker.anchoredPosition = WorldToMap(destination.DropPosition.position);
            }
        }

        private Vector2 WorldToMap(Vector3 worldPosition)
        {
            Vector3 offset = worldPosition - m_generator.GeneratedWorldBounds.center;
            return new Vector2(offset.x, offset.z) * m_worldToMapScale;
        }

        private RectTransform CreateMarker(string name, Color colour, float size)
        {
            Image image = CreateImage(name, m_mapPanel, colour);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, Color colour)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }
    }
}
