using System;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Presentation only: reuses a saved material and the existing screen/UVs.</summary>
    [DisallowMultipleComponent]
    public class RobotFaceDisplay : MonoBehaviour
    {
        [Header("CRT Display")]
        [SerializeField] private RobotFaceDisplaySettings m_displaySettings;
        [Tooltip("Existing rectangular screen renderer. Auto-resolves FaceDisplay on CouchGuy.")]
        [SerializeField] protected Renderer m_faceRenderer;
        [SerializeField, Min(0)] protected int m_materialIndex;
        [Tooltip("Optional renderers to suppress while the rectangular display is active.")]
        [SerializeField] private Renderer[] m_legacyGraphics;
        [SerializeField] private bool m_useCRT = true;

        private MaterialPropertyBlock m_properties, m_originalProperties;
        private Material m_originalMaterial;
        private bool[] m_originalHidden;
        private bool m_bound;
        private RobotFaceExpression m_from = RobotFaceExpression.Happy;
        private RobotFaceExpression m_target = RobotFaceExpression.Happy;
        private float m_transitionAt, m_nextBlink, m_blinkAt = float.NegativeInfinity;
        private float m_damageAt = float.NegativeInfinity, m_shutdownAt = float.NegativeInfinity;
        private float m_rebootAt = float.NegativeInfinity, m_phase;
        private uint m_random;

        private static readonly int ColorId = Shader.PropertyToID("_Cyan");
        private static readonly int DisplayId = Shader.PropertyToID("_Display");
        private static readonly int EffectsId = Shader.PropertyToID("_Effects");
        private static readonly int StateId = Shader.PropertyToID("_State");
        private static readonly int MotionId = Shader.PropertyToID("_Motion");
        private static readonly int UVId = Shader.PropertyToID("_ScreenUVRect");
        private static readonly int RotateId = Shader.PropertyToID("_RotateUV");

        public RobotFaceExpression CurrentExpression => m_target;
        protected virtual void Awake()
        {
            m_random = unchecked((uint)GetInstanceID() * 747796405u + 2891336453u);
            m_phase = Random01() * 100f;
            m_displaySettings ??= Resources.Load<RobotFaceDisplaySettings>("RobotFaceDisplaySettings");
            m_properties = new MaterialPropertyBlock();
            m_originalProperties = new MaterialPropertyBlock();
        }

        protected virtual void OnEnable()
        {
            BindScreen();
            ScheduleBlink(Time.time);
        }

        protected virtual void OnDisable() => RestoreOriginalDisplay();

        // No geometry or components are created. The two legacy eye meshes are
        // suppressed per instance, preserving their enabled state and materials.
        private void BindScreen()
        {
            if (m_bound || !m_useCRT || m_displaySettings == null || m_displaySettings.Material == null) return;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            // Migrate the old serialized eye reference to the actual rectangular screen.
            if (m_faceRenderer == null || m_faceRenderer.name.StartsWith("FaceGraphic", StringComparison.Ordinal))
                m_faceRenderer = Array.Find(renderers, r => r.name == "FaceDisplay");
            if (m_faceRenderer == null) return;
            Material[] slots = m_faceRenderer.sharedMaterials;
            if (m_materialIndex < 0 || m_materialIndex >= slots.Length) return;
            m_originalMaterial = slots[m_materialIndex];
            m_faceRenderer.GetPropertyBlock(m_originalProperties, m_materialIndex);
            slots[m_materialIndex] = m_displaySettings.Material;
            m_faceRenderer.sharedMaterials = slots;
            if (m_legacyGraphics == null || m_legacyGraphics.Length == 0)
                m_legacyGraphics = Array.FindAll(renderers, r => r.name == "FaceGraphic_L" || r.name == "FaceGraphic_R");
            m_originalHidden = new bool[m_legacyGraphics.Length];
            for (int i = 0; i < m_legacyGraphics.Length; i++)
            {
                Renderer r = m_legacyGraphics[i];
                if (r == null || r == m_faceRenderer) continue;
                m_originalHidden[i] = r.forceRenderingOff;
                r.forceRenderingOff = true;
            }
            m_bound = true;
        }

        [ContextMenu("Restore Original Display")]
        public void RestoreOriginalDisplay()
        {
            if (!m_bound) return;
            if (m_faceRenderer != null)
            {
                Material[] slots = m_faceRenderer.sharedMaterials;
                if (m_materialIndex < slots.Length)
                {
                    slots[m_materialIndex] = m_originalMaterial;
                    m_faceRenderer.sharedMaterials = slots;
                    m_faceRenderer.SetPropertyBlock(m_originalProperties, m_materialIndex);
                }
            }
            for (int i = 0; i < m_legacyGraphics.Length; i++)
                if (m_legacyGraphics[i] != null && m_legacyGraphics[i] != m_faceRenderer)
                    m_legacyGraphics[i].forceRenderingOff = m_originalHidden[i];
            m_bound = false;
        }

        public void SetExpression(RobotFaceExpression expression)
        {
            if (expression == m_target) return;
            float duration = m_displaySettings != null ? m_displaySettings.TransitionDuration : 0.16f;
            if (Time.time - m_transitionAt > duration * 0.5f) m_from = m_target;
            m_target = expression;
            m_transitionAt = Time.time;
            if (expression == RobotFaceExpression.Downed) m_shutdownAt = Time.time;
        }

        public void PlayDamageGlitch() => m_damageAt = Time.time;
        public void PlayReboot() => m_rebootAt = Time.time;

        protected virtual void LateUpdate()
        {
            if (!m_bound) return;
            if (!m_useCRT) { RestoreOriginalDisplay(); return; }
            var s = m_displaySettings;
            float now = Time.time;
            float blend = Mathf.Clamp01((now - m_transitionAt) * s.AnimationSpeed / s.TransitionDuration);
            float blink = 1f;
            if (now >= m_nextBlink)
            {
                m_blinkAt = now;
                ScheduleBlink(now);
            }
            float blinkAge = (now - m_blinkAt) * s.AnimationSpeed / s.BlinkDuration;
            if (blinkAge < 1f) blink = 1f - Mathf.Sin(blinkAge * Mathf.PI);
            if (m_target == RobotFaceExpression.Downed || m_target == RobotFaceExpression.Reviving ||
                m_target == RobotFaceExpression.Surprised) blink = 1f;
            float squash = 1f - Mathf.Sin(blend * Mathf.PI) * 0.18f;
            float glitch = Mathf.Clamp01(1f - (now - m_damageAt) / 0.28f) * s.GlitchIntensity;
            // Collapse to a CRT line briefly, then leave readable dim X eyes.
            float shutdownAge = now - m_shutdownAt;
            float shutdown = m_target == RobotFaceExpression.Downed && shutdownAge < 0.32f
                ? Mathf.Sin(Mathf.Clamp01(shutdownAge / 0.32f) * Mathf.PI) : 0f;
            float reboot = Mathf.Clamp01((now - m_rebootAt) * s.AnimationSpeed / 0.85f);
            m_faceRenderer.GetPropertyBlock(m_properties, m_materialIndex);
            m_properties.SetColor(ColorId, s.Cyan);
            m_properties.SetVector(DisplayId, new Vector4(s.Brightness, s.ScanlineIntensity, s.PixelDensity, s.Curvature));
            m_properties.SetVector(EffectsId, new Vector4(glitch, s.StaticIntensity, m_target == RobotFaceExpression.Damaged ? 1f : 0f, shutdown));
            m_properties.SetVector(StateId, new Vector4(AtlasIndex(m_from), AtlasIndex(m_target), Mathf.SmoothStep(0, 1, blend), reboot));
            m_properties.SetVector(MotionId, new Vector4(Mathf.Max(0.035f, blink) * squash, now * s.AnimationSpeed + m_phase, m_target == RobotFaceExpression.Reviving ? 1f : 0f, m_target == RobotFaceExpression.Downed ? 0.55f : 1f));
            m_properties.SetVector(UVId, s.ScreenUVRect);
            m_properties.SetFloat(RotateId, s.RotateScreenUV ? 1f : 0f);
            m_faceRenderer.SetPropertyBlock(m_properties, m_materialIndex);
        }

        // Existing enum values are stable for saved emote definitions.
        public static int AtlasIndex(RobotFaceExpression expression) => expression switch
        {
            RobotFaceExpression.Angry => 8,
            RobotFaceExpression.Surprised => 2,
            RobotFaceExpression.Focused => 3,
            RobotFaceExpression.Excited => 1,
            RobotFaceExpression.Confused => 4,
            RobotFaceExpression.Damaged => 5,
            RobotFaceExpression.Downed => 6,
            RobotFaceExpression.Reviving => 7,
            RobotFaceExpression.Celebrating => 9,
            _ => 0
        };

        private void ScheduleBlink(float now)
        {
            Vector2 interval = m_displaySettings != null ? m_displaySettings.BlinkInterval : new Vector2(2.4f, 5.6f);
            m_nextBlink = now + Mathf.Lerp(Mathf.Max(0.4f, interval.x), Mathf.Max(interval.x, interval.y), Random01());
        }
        private float Random01()
        {
            m_random = m_random * 1664525u + 1013904223u;
            return (m_random & 0x00ffffffu) / 16777216f;
        }
        protected float LowHealthFraction => m_displaySettings != null ? m_displaySettings.LowHealthFraction : 0.3f;
        protected float AnimationSpeed => m_displaySettings != null ? m_displaySettings.AnimationSpeed : 1f;
    }
}
