using CouchGuys.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Player
{
    /// <summary>Client-only presentation of identity already replicated by player ownership.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(300)]
    public sealed class PlayerNametag : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkPlayerOwnership m_identity;
        [SerializeField] private Transform m_visualRoot;
        [Tooltip("Optional override. Otherwise resolves Head under CouchGuy_Rig once on spawn.")]
        [SerializeField] private Transform m_head;
        [SerializeField] private RectTransform m_nametagPrefab;

        [Header("Appearance")]
        [Tooltip("World-space height above the animated head pivot, including its head mesh.")]
        [SerializeField, Min(0f)] private float m_heightOffset = 0.65f;
        [Tooltip("Text size in points at a reference screen height of 1080 pixels.")]
        [SerializeField, Min(1f)] private float m_fontSize = 18f;
        [SerializeField, Min(0f)] private float m_maximumVisibilityDistance = 25f;

        [Header("Occlusion")]
        [SerializeField] private LayerMask m_obstructionLayers = ~0;
        [SerializeField, Min(0.02f)] private float m_occlusionInterval = 0.1f;

        private readonly RaycastHit[] m_hits = new RaycastHit[32];
        private RectTransform m_view;
        private TMP_Text m_text;
        private Camera m_lastCamera;
        private float m_nextOcclusionCheck;
        private bool m_unobstructed;
        private bool m_visible;

        private void Awake()
        {
            m_identity ??= GetComponent<NetworkPlayerOwnership>();
            if (m_head == null && m_visualRoot != null)
            {
                // Mesh pivots also use "Head"; only the skeleton follows the animated pose.
                Transform skeleton = FindDescendant(m_visualRoot, "CouchGuy_Rig");
                if (skeleton != null) m_head = FindDescendant(skeleton, "Head");
            }
        }

        private static Transform FindDescendant(Transform root, string boneName)
        {
            if (root.name == boneName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), boneName);
                if (found != null) return found;
            }
            return null;
        }

        private void LateUpdate()
        {
            NetworkPlayerOwnership viewer = NetworkPlayerOwnership.LocalPlayer;
            Camera camera = viewer != null ? viewer.PlayerCamera : null;
            if (m_identity == null || !m_identity.IsClientInitialized || m_identity.IsOwner ||
                string.IsNullOrEmpty(m_identity.DisplayName) || m_head == null ||
                camera == null || !camera.isActiveAndEnabled)
            {
                SetVisible(false);
                return;
            }

            Vector3 headPosition = m_head.position;
            Vector3 position = headPosition + Vector3.up * m_heightOffset;
            Vector3 cameraPosition = camera.transform.position;
            Vector3 viewport = camera.WorldToViewportPoint(position);
            if ((headPosition - cameraPosition).sqrMagnitude >
                m_maximumVisibilityDistance * m_maximumVisibilityDistance ||
                viewport.z <= camera.nearClipPlane || viewport.x < -0.1f || viewport.x > 1.1f ||
                viewport.y < -0.1f || viewport.y > 1.1f)
            {
                SetVisible(false);
                m_nextOcclusionCheck = 0f;
                return;
            }

            if (camera != m_lastCamera || Time.unscaledTime >= m_nextOcclusionCheck)
            {
                m_lastCamera = camera;
                m_unobstructed = HasLineOfSight(cameraPosition, headPosition, viewer.transform);
                // Offset checks across players without touching Unity's global random state.
                m_nextOcclusionCheck = Time.unscaledTime + m_occlusionInterval *
                    (1f + (uint)GetInstanceID() % 7 * 0.03f);
            }
            if (!m_unobstructed)
            {
                SetVisible(false);
                return;
            }

            if (m_view == null)
            {
                if (m_nametagPrefab == null) return;
                m_view = Instantiate(m_nametagPrefab, transform);
                m_text = m_view.GetComponentInChildren<TMP_Text>(true);
                m_text.richText = false;
                m_text.parseCtrlCharacters = false;
                m_text.textWrappingMode = TextWrappingModes.NoWrap;
            }
            if (m_text.text != m_identity.DisplayName) m_text.text = m_identity.DisplayName;
            m_text.fontSize = m_fontSize;
            m_view.SetPositionAndRotation(position, camera.transform.rotation);
            // Match apparent size across distance and FOV. Canvas units are TMP points.
            float screenHeight = camera.orthographic ? 2f * camera.orthographicSize :
                2f * viewport.z *
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            m_view.localScale = Vector3.one * (screenHeight / 1080f);
            SetVisible(true);
        }

        private bool HasLineOfSight(Vector3 origin, Vector3 headPosition, Transform viewer)
        {
            Vector3 direction = headPosition - origin;
            float distance = direction.magnitude;
            if (distance < 0.001f) return true;
            int count = gameObject.scene.GetPhysicsScene().Raycast(origin, direction / distance,
                m_hits, distance, m_obstructionLayers, QueryTriggerInteraction.Ignore);
            // Overflow is conservatively hidden rather than leaking names through crowded geometry.
            if (count == m_hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = m_hits[i].collider.transform;
                if (!hit.IsChildOf(transform) && !hit.IsChildOf(viewer)) return false;
            }
            return true;
        }

        private void SetVisible(bool visible)
        {
            if (m_view == null || m_visible == visible) return;
            m_visible = visible;
            m_view.gameObject.SetActive(visible);
        }

        private void OnDisable()
        {
            SetVisible(false);
            m_nextOcclusionCheck = 0f;
            m_lastCamera = null;
        }
    }
}
