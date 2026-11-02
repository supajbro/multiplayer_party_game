using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Local-only screen animation; no audio buffers, textures or network animation ticks.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(DeliveryNPC))]
    public sealed class DispatcherScreen : MonoBehaviour
    {
        [SerializeField] private DeliveryNPC m_npc;
        [SerializeField, Tooltip("Existing screen renderers, one for each LOD.")]
        private Renderer[] m_screens;
        [SerializeField, Min(0.01f)] private float m_transitionSeconds = 0.22f;
        [SerializeField, Min(0.01f)] private float m_defaultEmphasisSeconds = 0.8f;
        [SerializeField, Range(1f, 3f)] private float m_emphasisSpeed = 1.6f;
        private static readonly int SpeechId = Shader.PropertyToID("_Speech");
        private static readonly int EmphasisId = Shader.PropertyToID("_Emphasis");
        private static readonly int TimeId = Shader.PropertyToID("_AnimationTime");
        private MaterialPropertyBlock m_block;
        private float m_speech, m_emphasis, m_phase, m_emphasisUntil;
        private bool m_ambientSpeaking;

        public void SetAmbientSpeaking(bool speaking) => m_ambientSpeaking = speaking;

        private void Awake()
        {
            if (m_npc == null) m_npc = GetComponent<DeliveryNPC>();
            m_block = new MaterialPropertyBlock();
        }

        /// <summary>Local expressive accent, callable by future dialogue cues or UnityEvents.</summary>
        public void Emphasize() => EmphasizeFor(m_defaultEmphasisSeconds);
        public void EmphasizeFor(float seconds) => m_emphasisUntil = Time.time + Mathf.Max(0f, seconds);

        private void Update()
        {
            var manager = m_npc != null ? m_npc.Manager : null;
            bool talking = m_ambientSpeaking || (manager != null && manager.IsDispatcherSpeaking);
            bool emphasis = Time.time < m_emphasisUntil;
            float step = Time.deltaTime / Mathf.Max(0.01f, m_transitionSeconds);
            m_speech = Mathf.MoveTowards(m_speech, talking || emphasis ? 1f : 0f, step);
            m_emphasis = Mathf.MoveTowards(m_emphasis, emphasis ? 1f : 0f, step);
            m_phase += Time.deltaTime * Mathf.Lerp(1f, m_emphasisSpeed, m_emphasis);
            Apply();
        }

        private void Apply()
        {
            if (m_screens == null) return;
            foreach (var screen in m_screens)
            {
                if (screen == null) continue;
                screen.GetPropertyBlock(m_block);
                m_block.SetFloat(SpeechId, m_speech);
                m_block.SetFloat(EmphasisId, m_emphasis);
                m_block.SetFloat(TimeId, m_phase);
                screen.SetPropertyBlock(m_block);
            }
        }

        private void OnDisable()
        {
            m_ambientSpeaking = false;
            m_speech = m_emphasis = m_emphasisUntil = 0f;
            if (m_block != null) Apply();
        }
    }
}
