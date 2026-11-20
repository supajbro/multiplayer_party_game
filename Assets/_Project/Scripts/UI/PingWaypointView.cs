using CouchGuys.Networking;
using TMPro;
using UnityEngine;

namespace CouchGuys.UI
{
    [DefaultExecutionOrder(300)]
    public sealed class PingWaypointView : MonoBehaviour
    {
        [SerializeField] private bool m_worldSpace;
        [SerializeField] private TMP_Text m_name;
        [SerializeField] private TMP_Text m_distance;
        [SerializeField] private PingMarkerGraphic m_marker;
        [SerializeField, Min(.01f)] private float m_appearDuration = .3f;
        [SerializeField, Min(.001f)] private float m_minimumWorldScale = .012f;
        private NetworkPlayerOwnership m_player;
        private Vector3 m_baseScale;
        private float m_startedAt;
        private int m_lastDistance = -1;
        private string m_lastName;

        private void Awake() => m_baseScale = transform.localScale;
        public void Bind(NetworkPlayerOwnership player) { m_player = player; Replay(); }
        public void Replay() => m_startedAt = Time.unscaledTime;

        private void LateUpdate()
        {
            var viewer = NetworkPlayerOwnership.LocalPlayer;
            if (m_player == null || viewer == null) return;
            float age = Mathf.Clamp01((Time.unscaledTime - m_startedAt) / Mathf.Max(.01f, m_appearDuration));
            float pulse = 1f + Mathf.Sin(age * Mathf.PI) * .18f;
            if (m_worldSpace)
            {
                Camera camera = viewer.PlayerCamera;
                if (camera == null) return;
                transform.rotation = camera.transform.rotation;
                float depth = Mathf.Max(0f, Vector3.Dot(transform.position - camera.transform.position, camera.transform.forward));
                float height = camera.orthographic ? camera.orthographicSize * 2f :
                    2f * depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
                transform.localScale = Vector3.one * (Mathf.Max(m_minimumWorldScale, height / 1080f) * pulse);
            }
            else transform.localScale = m_baseScale * pulse;

            string name = m_player.DisplayName;
            if (m_lastName != name)
            {
                m_lastName = name;
                if (m_name != null) m_name.text = name;
            }
            Color colour = m_player.IdentityColour;
            if (m_name != null) m_name.color = Color.Lerp(colour, Color.white, .45f);
            if (m_marker != null) m_marker.Accent = colour;
            int metres = Mathf.RoundToInt(Vector3.Distance(viewer.transform.position, m_player.Ping.Position));
            if (m_lastDistance != metres)
            {
                m_lastDistance = metres;
                if (m_distance != null) m_distance.SetText("{0} m", metres);
            }
        }
    }
}
