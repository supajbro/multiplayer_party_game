using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Shared prefab presentation; gameplay and smoothing remain in PlayerWeaponHud.</summary>
    public sealed class PlayerStatusBar : MonoBehaviour
    {
        [SerializeField] private Image m_fill;
        [SerializeField] private Image m_expression;
        [SerializeField] private Sprite[] m_happyFrames;
        [Tooltip("Optional; falls back to Happy when empty. Used below 60% health.")]
        [SerializeField] private Sprite[] m_orangeFrames;
        [Tooltip("Optional; falls back to Happy when empty. Used below 30% health.")]
        [SerializeField] private Sprite[] m_redFrames;
        [Header("Face Idle And Occasional Animations")]
        [SerializeField, Min(0)] private int m_idleFrame;
        [Tooltip("Gentle breathing cycles per second, applied only to the face Image.")]
        [SerializeField, Min(0.01f)] private float m_idleAnimationSpeed = 0.3f;
        [SerializeField, Range(0f, 0.03f)] private float m_idleScaleAmount = 0.012f;
        [Tooltip("Frame indices in the selected health expression sheet: half closed, closed, half closed.")]
        [SerializeField] private int[] m_blinkFrames = { 8, 10, 8 };
        [SerializeField] private Vector2 m_blinkInterval = new(3f, 6f);
        [SerializeField] private Vector2 m_blinkDuration = new(0.15f, 0.25f);
        [Tooltip("Frame indices for a smile, bigger smile, then smile.")]
        [SerializeField] private int[] m_bigHappyFrames = { 2, 6, 2 };
        [SerializeField] private Vector2 m_bigHappyInterval = new(8f, 15f);
        [SerializeField] private Vector2 m_bigHappyDuration = new(0.4f, 0.7f);
        [SerializeField] private RawImage m_lightningIcon;
        [Tooltip("Assign the lightning icon texture here when it is available.")]
        public Texture lightningIconTexture;

        private static readonly int s_health = Shader.PropertyToID("_Health");
        private static readonly int s_barRect = Shader.PropertyToID("_BarRect");
        private Material m_fillMaterial;
        private Material m_originalMaterial;

        private Sprite[] m_activeFrames;
        private Sprite[] m_selectedFrames;
        private enum FaceAnimation { Idle, Blink, BigHappy }
        private FaceAnimation m_animation;
        private double m_animationStartedAt;
        private float m_animationDuration;
        private double m_nextBlinkAt, m_nextHappyAt, m_nextFrameAt;
        private RectTransform m_faceRect;
        private Vector3 m_faceRestScale;
        private float m_idlePhase, m_idleBlend;

        private void Awake()
        {
            if (m_expression == null) return;
            m_faceRect = m_expression.rectTransform;
            m_faceRestScale = m_faceRect.localScale;
        }

        // Receives the actual synchronized health, independently of bar smoothing.
        public void SetHealthExpression(float normalizedHealth)
        {
            Sprite[] frames = normalizedHealth >= 0.6f ? m_happyFrames
                : normalizedHealth >= 0.3f ? m_orangeFrames : m_redFrames;
            if (frames == null || frames.Length == 0) frames = m_happyFrames;
            if (m_selectedFrames == frames) return;
            m_selectedFrames = frames;
            // Finish a gesture before applying a different health expression sheet.
            if (m_animation == FaceAnimation.Idle)
            {
                m_activeFrames = frames;
                ShowFrame(m_idleFrame);
            }
        }

        private void Update()
        {
            UpdateExpression();
            UpdateIdleMotion();
        }

        private void UpdateIdleMotion()
        {
            if (m_faceRect == null || !m_expression.isActiveAndEnabled) return;
            bool idle = m_animation == FaceAnimation.Idle;
            float delta = Time.unscaledDeltaTime;
            // Pause the breathing phase during gestures and ease its influence out/back in.
            if (idle) m_idlePhase = Mathf.Repeat(m_idlePhase + delta * m_idleAnimationSpeed, 1f);
            m_idleBlend = Mathf.MoveTowards(m_idleBlend, idle ? 1f : 0f, delta / 0.15f);
            float breath = Mathf.Sin(m_idlePhase * Mathf.PI * 2f) * m_idleScaleAmount *
                Mathf.SmoothStep(0f, 1f, m_idleBlend);
            m_faceRect.localScale = m_faceRestScale * (1f + breath);
        }

        private void OnDisable()
        {
            if (m_faceRect != null) m_faceRect.localScale = m_faceRestScale;
            m_idlePhase = m_idleBlend = 0f;
        }

        private void UpdateExpression()
        {
            double now = Time.unscaledTimeAsDouble;
            if (now < m_nextFrameAt) return;
            if (m_expression == null || !m_expression.isActiveAndEnabled ||
                m_activeFrames == null || m_activeFrames.Length == 0) return;

            if (m_animation == FaceAnimation.Idle)
            {
                if (now < m_nextBlinkAt && now < m_nextHappyAt)
                {
                    m_nextFrameAt = System.Math.Min(m_nextBlinkAt, m_nextHappyAt);
                    return;
                }
                // If both are due, the oldest request goes first. The other waits until idle.
                m_animation = m_nextBlinkAt <= m_nextHappyAt ? FaceAnimation.Blink : FaceAnimation.BigHappy;
                m_animationStartedAt = now;
                m_animationDuration = SampleRange(m_animation == FaceAnimation.Blink ? m_blinkDuration : m_bigHappyDuration);
            }

            int[] sequence = m_animation == FaceAnimation.Blink ? m_blinkFrames : m_bigHappyFrames;
            double elapsed = now - m_animationStartedAt;
            if (sequence == null || sequence.Length == 0 || elapsed >= m_animationDuration)
            {
                if (m_animation == FaceAnimation.Blink) m_nextBlinkAt = now + SampleRange(m_blinkInterval);
                else m_nextHappyAt = now + SampleRange(m_bigHappyInterval);
                m_animation = FaceAnimation.Idle;
                m_activeFrames = m_selectedFrames;
                ShowFrame(m_idleFrame);
                m_nextFrameAt = System.Math.Min(m_nextBlinkAt, m_nextHappyAt);
                return;
            }
            int step = Mathf.Min((int)(elapsed / m_animationDuration * sequence.Length), sequence.Length - 1);
            ShowFrame(sequence[step]);
            m_nextFrameAt = m_animationStartedAt + (step + 1) * (double)m_animationDuration / sequence.Length;
        }

        private void ShowFrame(int index)
        {
            if (m_expression == null || m_activeFrames == null || m_activeFrames.Length == 0) return;
            Sprite sprite = m_activeFrames[Mathf.Clamp(index, 0, m_activeFrames.Length - 1)];
            if (m_expression.sprite != sprite) m_expression.sprite = sprite;
        }

        private static float SampleRange(Vector2 range) =>
            Random.Range(Mathf.Max(0.01f, Mathf.Min(range.x, range.y)), Mathf.Max(0.01f, Mathf.Max(range.x, range.y)));

        public void SetValue(float value)
        {
            m_fill.fillAmount = Mathf.Clamp01(value);
            if (m_fillMaterial == null) return;

            Rect rect = m_fill.rectTransform.rect;
            Vector4 bounds = new Vector4(rect.xMin, rect.yMin, rect.width, rect.height);
            m_fillMaterial.SetFloat(s_health, m_fill.fillAmount);
            m_fillMaterial.SetVector(s_barRect, bounds);
            // A parent UI Mask can supply a stencil-modified copy of this material.
            Material rendered = m_fill.materialForRendering;
            if (rendered != m_fillMaterial)
            {
                rendered.SetFloat(s_health, m_fill.fillAmount);
                rendered.SetVector(s_barRect, bounds);
            }
        }

        private void OnEnable()
        {
            if (m_selectedFrames == null) SetHealthExpression(1f);
            m_animation = FaceAnimation.Idle;
            m_activeFrames = m_selectedFrames;
            ShowFrame(m_idleFrame);
            double now = Time.unscaledTimeAsDouble;
            m_nextBlinkAt = now + SampleRange(m_blinkInterval);
            m_nextHappyAt = now + SampleRange(m_bigHappyInterval);
            m_nextFrameAt = System.Math.Min(m_nextBlinkAt, m_nextHappyAt);
            RefreshIcon();
            if (m_fill == null || m_fillMaterial != null || !m_fill.material.HasProperty(s_health)) return;
            m_originalMaterial = m_fill.material;
            m_fillMaterial = new Material(m_originalMaterial) { name = m_originalMaterial.name + " (Instance)" };
            m_fill.material = m_fillMaterial;
            SetValue(m_fill.fillAmount);
        }

        private void OnDestroy()
        {
            if (m_fillMaterial == null) return;
            if (m_fill != null) m_fill.material = m_originalMaterial;
            Destroy(m_fillMaterial);
        }

        private void RefreshIcon()
        {
            if (m_lightningIcon == null) return;
            m_lightningIcon.texture = lightningIconTexture;
            m_lightningIcon.enabled = lightningIconTexture != null;
        }

#if UNITY_EDITOR
        private void OnValidate() => RefreshIcon();
#endif
    }
}
