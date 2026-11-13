using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Saved Particle Systems plus two curved hand streams and a grab-point pulse.</summary>
    [DefaultExecutionOrder(210)]
    public sealed class MagneticCarryEffect : MonoBehaviour
    {
        [SerializeField] private LineRenderer[] m_streams;
        [SerializeField] private LineRenderer m_pulse;
        [SerializeField] private ParticleSystem m_particles;
        [SerializeField] private ParticleSystem m_attachBurst;
        [Header("Aura Appearance")]
        [SerializeField, ColorUsage(true, true)] private Color m_colour = new Color(.04f, .8f, 1f, .85f);
        [SerializeField, Range(0f, 8f)] private float m_intensity = 2f;
        [SerializeField, Range(0, 64)] private int m_particleCount = 18;
        [SerializeField, Min(0f)] private float m_pulseSpeed = 3f;
        [SerializeField] private Vector3 m_attachmentOffset = new Vector3(0f, .04f, 0f);
        [SerializeField, Min(.01f)] private float m_streamWidth = .025f;
        [SerializeField, Min(0f)] private float m_curveHeight = .12f;
        [SerializeField, Min(.01f)] private float m_pulseRadius = .14f;
        [SerializeField, Min(.01f)] private float m_dissipateTime = .18f;
        private Transform m_left, m_right, m_body, m_target;
        private float m_fade = 1f, m_age, m_emit;
        private bool m_released;
        private int m_nextHand;
        private Renderer[] m_renderers;
        private MaterialPropertyBlock m_properties;
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");
        private readonly Vector3[] m_start = new Vector3[2];
        private Vector3 m_end, m_normal;

        private void Awake()
        {
            m_renderers = GetComponentsInChildren<Renderer>();
            m_properties = new MaterialPropertyBlock();
        }

        public void Attach(Transform left, Transform right, Transform body, Transform target)
        {
            m_left = left; m_right = right; m_body = body; m_target = target;
            UpdateEndpoints();
            if (m_particles != null) m_particles.Play();
            if (m_attachBurst != null)
            {
                m_attachBurst.transform.position = m_end;
                var main = m_attachBurst.main;
                main.startColor = Tint(1f);
                m_attachBurst.Play();
            }
        }

        public void Release()
        {
            if (m_released) return;
            m_released = true;
            m_target = null; m_left = null; m_right = null; m_body = null;
            if (m_attachBurst != null) m_attachBurst.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, m_dissipateTime + .4f);
        }

        private void LateUpdate()
        {
            if (!m_released && (m_target == null || !m_target.gameObject.activeInHierarchy || m_body == null)) Release();
            if (!m_released) UpdateEndpoints();
            m_age += Time.deltaTime;
            if (m_released) m_fade = Mathf.MoveTowards(m_fade, 0f, Time.deltaTime / Mathf.Max(.01f, m_dissipateTime));
            float pulse = .8f + .2f * Mathf.Sin(m_age * m_pulseSpeed);
            // Keep HDR intensity out of ParticleSystem's Color32 vertex channel.
            foreach (Renderer renderer in m_renderers)
            {
                renderer.GetPropertyBlock(m_properties);
                m_properties.SetFloat(Intensity, m_intensity);
                renderer.SetPropertyBlock(m_properties);
            }
            for (int hand = 0; hand < m_streams.Length; hand++)
            {
                LineRenderer line = m_streams[hand];
                if (line == null) continue;
                line.startColor = line.endColor = Tint(m_fade * pulse);
                line.widthMultiplier = m_streamWidth * m_fade;
                for (int j = 0; j < line.positionCount; j++)
                    line.SetPosition(j, Stream(hand % 2, j / (float)(line.positionCount - 1)));
            }
            if (m_pulse != null)
            {
                Vector3 tangent = Vector3.Cross(m_normal, Vector3.up).normalized;
                if (tangent.sqrMagnitude < .01f) tangent = Vector3.right;
                Vector3 bitangent = Vector3.Cross(m_normal, tangent).normalized;
                for (int i = 0; i < m_pulse.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2f / m_pulse.positionCount;
                    m_pulse.SetPosition(i, m_end + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * m_pulseRadius * (1f + .12f * pulse));
                }
                m_pulse.startColor = m_pulse.endColor = Tint(m_fade * pulse * .65f);
                m_pulse.widthMultiplier = m_streamWidth * .65f * m_fade;
            }
            if (m_particles == null) return;
            // Bounded, local emissions. Particle travel follows the moving curve without network traffic.
            m_emit += m_released ? 0f : Time.deltaTime * m_particleCount * 2f;
            int count = Mathf.Min(64, Mathf.FloorToInt(m_emit));
            m_emit -= count;
            for (int i = 0; i < count; i++)
            {
                int hand = m_nextHand++ % 2;
                var emit = new ParticleSystem.EmitParams { position = Stream(hand, 0f), startColor = Tint(m_fade),
                    startLifetime = .5f, startSize = .035f, randomSeed = (uint)(1 + hand) };
                m_particles.Emit(emit, 1);
            }
            int alive = m_particles.GetParticles(m_buffer);
            for (int i = 0; i < alive; i++)
            {
                var particle = m_buffer[i];
                float t = 1f - particle.remainingLifetime / particle.startLifetime;
                particle.position = Stream((int)(particle.randomSeed - 1) % 2, t);
                particle.startColor = Tint(m_fade * Mathf.Sin(t * Mathf.PI));
                m_buffer[i] = particle;
            }
            m_particles.SetParticles(m_buffer, alive);
        }
        private readonly ParticleSystem.Particle[] m_buffer = new ParticleSystem.Particle[64];
        private void UpdateEndpoints()
        {
            if (m_target == null) return;
            m_end = m_target.TransformPoint(m_attachmentOffset);
            Vector3 fallback = m_body != null ? m_body.position : m_end;
            m_start[0] = m_left != null ? m_left.position : fallback;
            m_start[1] = m_right != null ? m_right.position : fallback;
            m_normal = (fallback - m_end).normalized;
            if (m_normal.sqrMagnitude < .01f) m_normal = Vector3.up;
        }
        private Vector3 Stream(int hand, float t) => Vector3.Lerp(m_start[hand], m_end, t) +
            Vector3.up * (Mathf.Sin(t * Mathf.PI) * m_curveHeight * (1f + .18f * Mathf.Sin(m_age * m_pulseSpeed + t * 5f + hand)));
        private Color Tint(float alpha) => new Color(m_colour.r, m_colour.g, m_colour.b, m_colour.a * alpha);
    }
}
