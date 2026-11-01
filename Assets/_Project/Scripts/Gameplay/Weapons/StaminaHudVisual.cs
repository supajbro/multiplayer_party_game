using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Presentation only. Both displays receive the same owner HUD stamina value.</summary>
    public sealed class StaminaHudVisual : MonoBehaviour
    {
        [Header("Holder and normalized artwork layout")]
        [SerializeField] private Image m_holder;
        [SerializeField] private RectTransform m_holderRect;
        [SerializeField] private Image m_energy;
        [Tooltip("Normalized artwork coordinates: left, bottom, right, top.")]
        [SerializeField] private Vector4 m_energyWindow = new(0.147f, 0.404f, 0.853f, 0.603f);
        [Header("Energy")]
        [SerializeField] private Material m_energyMaterial;
        [SerializeField] private float m_waveSpeed = 0.35f;
        [SerializeField, Min(0f)] private float m_waveFrequency = 2f;
        [SerializeField, Range(0f, 0.4f)] private float m_waveAmplitude = 0.24f;
        [SerializeField, Min(0f)] private float m_glowIntensity = 1.4f;
        [SerializeField] private Color m_cyan = new(0f, 0.88f, 1f, 1f);
        [SerializeField] private Color m_orange = new(1f, 0.38f, 0.025f, 1f);
        [SerializeField] private Color m_red = new(1f, 0.035f, 0.025f, 1f);
        [SerializeField, Range(0f, 1f)] private float m_lowThreshold = 0.25f;
        [SerializeField, Range(0f, 1f)] private float m_highThreshold = 0.6f;
        [SerializeField, Range(0.001f, 0.1f)] private float m_colourBlendWidth = 0.02f;
        [Header("Eight segments, in left to right order")]
        [SerializeField] private Image[] m_segments;
        [SerializeField] private Image[] m_emptySegments;
        [SerializeField] private Vector2 m_segmentCentre = new(0.525f, 0.735f);
        [SerializeField] private Vector2 m_segmentSize = new(0.036f, 0.069f);
        [SerializeField, Min(0f)] private float m_segmentSpacing = 0.009f;
        [SerializeField] private Color m_emptyColour = new(0.008f, 0.035f, 0.055f, 1f);
        [SerializeField, Range(0f, 2f)] private float m_segmentBrightness = 1f;

        private static readonly int Fill = Shader.PropertyToID("_Fill");
        private static readonly int Wave = Shader.PropertyToID("_Wave");
        private static readonly int Glow = Shader.PropertyToID("_Glow");
        private static readonly int Aspect = Shader.PropertyToID("_Aspect");
        private Material m_instance;
        private float m_value = 1f;
        public float NormalizedValue => m_value;

        private void OnEnable()
        {
            if (m_energy != null && m_energyMaterial != null && m_instance == null)
            {
                m_instance = new Material(m_energyMaterial) { name = "Stamina Energy (HUD Instance)" };
                m_energy.material = m_instance;
            }
            ApplyLayout();
            SetValue(m_value);
        }

        public void SetValue(float value)
        {
            m_value = Mathf.Clamp01(value);
            Color colour = Color.Lerp(m_red, m_orange, Blend(m_lowThreshold));
            colour = Color.Lerp(colour, m_cyan, Blend(m_highThreshold));
            if (m_energy != null)
            {
                m_energy.color = colour;
                // Keep the geometry full width: the shader clips the fill without stretching the wave.
                m_energy.enabled = m_value > 0f;
                if (m_instance != null)
                {
                    Configure(m_instance);
                    Material rendered = m_energy.materialForRendering;
                    if (rendered != m_instance) Configure(rendered);
                }
            }
            if (m_segments == null) return;
            Color segmentColour = colour * m_segmentBrightness;
            segmentColour.a = colour.a;
            for (int i = 0; i < m_segments.Length; i++)
            {
                if (m_segments[i] == null) continue;
                m_segments[i].fillAmount = Mathf.Clamp01(m_value * 8f - i);
                m_segments[i].color = segmentColour;
            }
        }

        private float Blend(float threshold) => Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(threshold, threshold + Mathf.Max(0.001f, m_colourBlendWidth), m_value));

        private void Configure(Material material)
        {
            material.SetFloat(Fill, m_value);
            material.SetVector(Wave, new Vector4(m_waveSpeed, m_waveFrequency, m_waveAmplitude, 0f));
            material.SetFloat(Glow, m_glowIntensity);
            Rect rect = m_energy.rectTransform.rect;
            material.SetFloat(Aspect, rect.width / Mathf.Max(0.001f, rect.height));
        }

        private void OnRectTransformDimensionsChange() => ApplyLayout();

        [ContextMenu("Apply Stamina Layout")]
        private void ApplyLayout()
        {
            if (m_holderRect == null || m_holder == null || m_holder.sprite == null) return;
            Rect parent = ((RectTransform)transform).rect;
            Rect sprite = m_holder.sprite.rect;
            float scale = Mathf.Min(parent.width / sprite.width, parent.height / sprite.height);
            m_holderRect.anchorMin = m_holderRect.anchorMax = new Vector2(0.5f, 0.5f);
            m_holderRect.sizeDelta = sprite.size * scale;
            m_holderRect.anchoredPosition = Vector2.zero;
            if (m_energy != null)
                Place(m_energy.rectTransform, new Vector2(m_energyWindow.x, m_energyWindow.y),
                    new Vector2(m_energyWindow.z, m_energyWindow.w));
            for (int i = 0; i < 8; i++)
            {
                Vector2 centre = m_segmentCentre + Vector2.right * ((i - 3.5f) * (m_segmentSize.x + m_segmentSpacing));
                Vector2 min = centre - m_segmentSize * 0.5f, max = centre + m_segmentSize * 0.5f;
                if (m_segments != null && i < m_segments.Length && m_segments[i] != null)
                    Place(m_segments[i].rectTransform, min, max);
                if (m_emptySegments != null && i < m_emptySegments.Length && m_emptySegments[i] != null)
                {
                    Place(m_emptySegments[i].rectTransform, min, max);
                    m_emptySegments[i].color = m_emptyColour;
                }
            }
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (m_instance == null) return;
            if (m_energy != null) m_energy.material = m_energyMaterial;
            Destroy(m_instance);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplyLayout();
            if (Application.isPlaying) SetValue(m_value);
        }
#endif
    }
}
