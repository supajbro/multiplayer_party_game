using UnityEngine;
using UnityEngine.Rendering;

namespace CouchGuys.Gameplay.Delivery
{
    [DisallowMultipleComponent, RequireComponent(typeof(DeliveryNPC))]
    public sealed class DispatcherIdleBanter : MonoBehaviour
    {
        [SerializeField] private DispatcherBubbleView m_bubblePrefab;
        [SerializeField] private DeliveryNPC m_npc;
        [SerializeField] private DispatcherScreen m_screen;
        [SerializeField] private Vector3 m_anchorOffset = new(0f, 2.24f, 0f);
        [SerializeField, Tooltip("Random delay after each bubble or conversation, in seconds.")]
        private Vector2 m_intervalSeconds = new(25f, 50f);
        [SerializeField, Min(0.5f)] private float m_displaySeconds = 3.5f;
        [SerializeField, Min(0.01f)] private float m_fadeSeconds = 0.2f;
        [SerializeField, Min(1f)] private float m_maximumDistance = 12f;
        [SerializeField, TextArea(1, 3)] private string[] m_lines =
        {
            "These couches won't deliver themselves.",
            "When's knock off time.",
            "I hate this job.",
            "I'm on a roll. Legally, that's walking.",
            "Pivot. That's the entire training manual.",
            "Another day, another mystery cushion stain.",
            "Please stop calling it a couch potato. That's management.",
            "My dream job? Standby mode.",
            "The customer said 'just one flight of stairs'. They always do.",
            "I've found more coins in couches than in my pay packet."
        };

        private DispatcherBubbleView m_bubble;
        private DispatcherQuipDeck m_deck;
        private System.Random m_random;
        private Camera m_camera;
        private float m_nextAt, m_hideAt, m_startedAt, m_nextVisibilityCheck;
        private bool m_visible, m_wasEligible, m_nearby;

        private void Awake()
        {
            if (m_npc == null) m_npc = GetComponent<DeliveryNPC>();
            if (m_screen == null) m_screen = GetComponent<DispatcherScreen>();
            m_random = new System.Random();
            m_deck = new DispatcherQuipDeck(m_lines, m_random);
        }

        private void OnEnable()
        {
            m_wasEligible = m_nearby = false;
            m_nextVisibilityCheck = 0f;
            Schedule();
        }

        private void Schedule()
        {
            float minimum = Mathf.Max(1f, m_intervalSeconds.x);
            m_nextAt = Time.time + minimum + (float)m_random.NextDouble() *
                (Mathf.Max(minimum, m_intervalSeconds.y) - minimum);
        }

        private void Update()
        {
            var manager = m_npc != null ? m_npc.Manager : null;
            bool idle = manager != null && manager.IsClientInitialized && !manager.IsDispatcherSpeaking;
            if (Time.time >= m_nextVisibilityCheck)
            {
                m_nextVisibilityCheck = Time.time + 0.5f;
                if (m_camera == null || !m_camera.isActiveAndEnabled) m_camera = Camera.main;
                m_nearby = m_camera != null && m_camera.isActiveAndEnabled &&
                    (m_camera.transform.position - transform.position).sqrMagnitude <= m_maximumDistance * m_maximumDistance;
                if (m_nearby)
                {
                    Vector3 point = m_camera.WorldToViewportPoint(transform.TransformPoint(m_anchorOffset));
                    m_nearby = point.z > 0f && point.x > 0f && point.x < 1f && point.y > 0f && point.y < 1f;
                }
            }
            bool eligible = idle && m_nearby && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!eligible)
            {
                Hide();
                m_wasEligible = false;
                return;
            }
            if (!m_wasEligible) { Schedule(); m_wasEligible = true; }
            if (m_visible)
            {
                if (Time.time >= m_hideAt) { Hide(); Schedule(); }
                return;
            }
            if (Time.time < m_nextAt) return;
            Schedule(); // Empty/invalid lists retry on a cooldown, never every frame.
            if (m_bubblePrefab == null || !m_deck.TryNext(out string line)) return;
            if (m_bubble == null)
            {
                m_bubble = Instantiate(m_bubblePrefab, transform);
                m_bubble.transform.localPosition = m_anchorOffset;
            }
            m_startedAt = Time.time;
            m_hideAt = Time.time + Mathf.Max(0.5f, m_displaySeconds);
            m_visible = true;
            m_bubble.Show(line);
            if (m_screen != null) m_screen.SetAmbientSpeaking(true);
        }

        private void LateUpdate()
        {
            if (!m_visible || m_camera == null) return;
            float fade = Mathf.Min(Mathf.Max(0.01f, m_fadeSeconds), (m_hideAt - m_startedAt) * 0.5f);
            m_bubble.SetProgress(Mathf.Min((Time.time - m_startedAt) / fade, (m_hideAt - Time.time) / fade));
            m_bubble.Face(m_camera);
        }

        private void Hide()
        {
            if (!m_visible) return;
            m_visible = false;
            if (m_bubble != null) m_bubble.Hide();
            if (m_screen != null) m_screen.SetAmbientSpeaking(false);
        }

        private void OnDisable() => Hide();
        private void OnDestroy() { if (m_bubble != null) Destroy(m_bubble.gameObject); }
    }
}
