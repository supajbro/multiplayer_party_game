using CouchGuys.Gameplay.Weapons;
using UnityEngine;

namespace CouchGuys.Player
{
    public enum RobotFaceExpression
    {
        Normal = 0,
        Happy = 1,
        Angry = 2,
        Surprised = 3,
        Focused = 4
    }

    /// <summary>
    /// Drives the robot's existing face-screen renderer with per-instance shader data.
    /// Authoritative gameplay state is already replicated by the owning systems; this
    /// component only converts those observer-visible events into lightweight visuals.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotFaceExpressionController : MonoBehaviour
    {
        private static readonly int EyeColourId = Shader.PropertyToID("_EyeColor");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int FromExpressionId = Shader.PropertyToID("_ExpressionFrom");
        private static readonly int ToExpressionId = Shader.PropertyToID("_ExpressionTo");
        private static readonly int ExpressionBlendId = Shader.PropertyToID("_ExpressionBlend");
        private static readonly int BlinkId = Shader.PropertyToID("_Blink");
        private static readonly int LookOffsetId = Shader.PropertyToID("_LookOffset");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int SingleEyeId = Shader.PropertyToID("_SingleEye");
        private static readonly int EyeSideId = Shader.PropertyToID("_EyeSide");

        [Header("Face Screen")]
        [SerializeField] private Renderer m_faceRenderer;
        [SerializeField, Min(0)] private int m_materialIndex;
        [SerializeField] private Material m_faceMaterial;

        [Header("Display")]
        [SerializeField, ColorUsage(true, true)] private Color m_eyeColour =
            new(0.03f, 0.72f, 1f, 1f);
        [SerializeField, Range(0f, 12f)] private float m_glowIntensity = 5.5f;
        [SerializeField, Min(0.01f)] private float m_transitionDuration = 0.14f;

        [Header("Expression Timing")]
        [SerializeField, Min(0.05f)] private float m_surprisedDuration = 0.85f;
        [SerializeField, Min(0.05f)] private float m_angryHoldDuration = 0.32f;
        [SerializeField] private Vector2 m_happyInterval = new(4.5f, 9f);
        [SerializeField] private Vector2 m_happyDuration = new(0.8f, 1.8f);

        [Header("Natural Motion")]
        [Tooltip("Random time range between cosmetic blinks, in seconds.")]
        [SerializeField] private Vector2 m_blinkInterval = new(2.2f, 5.2f);
        [SerializeField, Range(0.04f, 0.3f)] private float m_blinkDuration = 0.12f;
        [SerializeField, Range(0f, 0.08f)] private float m_eyeDrift = 0.018f;

        private PlayerCouchCarrier m_carrier;
        private MaterialPropertyBlock m_propertyBlock;
        private Renderer[] m_activeRenderers = System.Array.Empty<Renderer>();
        private int[] m_activeMaterialIndices = System.Array.Empty<int>();
        private float[] m_activeEyeSides = System.Array.Empty<float>();
        private RobotFaceExpression m_fromExpression = RobotFaceExpression.Normal;
        private RobotFaceExpression m_toExpression = RobotFaceExpression.Normal;
        private float m_transitionStartedAt;
        private float m_surprisedUntil = float.NegativeInfinity;
        private float m_angryUntil = float.NegativeInfinity;
        private float m_happyUntil = float.NegativeInfinity;
        private float m_nextHappyAt;
        private float m_blinkStartedAt = float.NegativeInfinity;
        private float m_nextBlinkAt;
        private float m_nextResolveAt;
        private float m_phase;
        private uint m_randomState;
        private bool m_ready;

        public RobotFaceExpression CurrentExpression => m_toExpression;

        private void Awake()
        {
            m_carrier = GetComponent<PlayerCouchCarrier>();
            m_propertyBlock = new MaterialPropertyBlock();
            m_randomState = unchecked((uint)GetInstanceID() * 747796405u + 2891336453u);
            m_phase = Next01() * Mathf.PI * 2f;
            ResolveFaceScreen();
            ScheduleHappy(Time.time);
            ScheduleBlink(Time.time);
        }

        private void OnEnable()
        {
            if (m_nextHappyAt <= 0f) ScheduleHappy(Time.time);
            if (m_nextBlinkAt <= 0f) ScheduleBlink(Time.time);
        }

        private void LateUpdate()
        {
            if (!m_ready)
            {
                if (Time.unscaledTime < m_nextResolveAt) return;
                m_nextResolveAt = Time.unscaledTime + 1f;
                ResolveFaceScreen();
                if (!m_ready) return;
            }

            float now = Time.time;
            UpdateAmbientExpression(now);
            SetTargetExpression(ResolveExpression(now), now);
            ApplyShaderProperties(now);
        }

        /// <summary>Called for every accepted, observer-visible weapon shot.</summary>
        public void NotifyShot(PlayerWeaponType weapon)
        {
            float weaponHold = weapon == PlayerWeaponType.AssaultRifle
                ? m_angryHoldDuration * 1.2f
                : m_angryHoldDuration;
            m_angryUntil = Mathf.Max(m_angryUntil, Time.time + weaponHold);
        }

        /// <summary>Called by the health observer RPC so damage overrides every other face.</summary>
        public void NotifyDamage()
        {
            m_surprisedUntil = Time.time + m_surprisedDuration;
        }

        private RobotFaceExpression ResolveExpression(float now)
        {
            if (now < m_surprisedUntil) return RobotFaceExpression.Surprised;
            if (now < m_angryUntil) return RobotFaceExpression.Angry;
            if (m_carrier != null && m_carrier.IsCarrying) return RobotFaceExpression.Focused;
            return now < m_happyUntil ? RobotFaceExpression.Happy : RobotFaceExpression.Normal;
        }

        private void UpdateAmbientExpression(float now)
        {
            if (now < m_happyUntil || now < m_nextHappyAt) return;
            m_happyUntil = now + Range(m_happyDuration);
            ScheduleHappy(m_happyUntil);
        }

        private void SetTargetExpression(RobotFaceExpression expression, float now)
        {
            if (expression == m_toExpression) return;

            float previousBlend = Mathf.Clamp01(
                (now - m_transitionStartedAt) / Mathf.Max(0.01f, m_transitionDuration));
            m_fromExpression = previousBlend >= 0.5f ? m_toExpression : m_fromExpression;
            m_toExpression = expression;
            m_transitionStartedAt = now;
        }

        private void ApplyShaderProperties(float now)
        {
            float transition = Mathf.Clamp01(
                (now - m_transitionStartedAt) / Mathf.Max(0.01f, m_transitionDuration));
            transition = transition * transition * (3f - 2f * transition);

            float blink = CalculateBlink(now);
            if (m_toExpression == RobotFaceExpression.Surprised) blink = 1f;

            float slowTime = now + m_phase;
            Vector2 lookOffset = new(
                Mathf.Sin(slowTime * 0.73f) * m_eyeDrift,
                Mathf.Sin(slowTime * 0.51f + 1.7f) * m_eyeDrift * 0.55f);
            float pulse = 0.94f + Mathf.Sin(slowTime * 2.1f) * 0.06f;

            bool singleEye = m_activeRenderers.Length == 2;
            for (int index = 0; index < m_activeRenderers.Length; index++)
            {
                Renderer faceRenderer = m_activeRenderers[index];
                if (faceRenderer == null) continue;

                int materialIndex = m_activeMaterialIndices[index];
                faceRenderer.GetPropertyBlock(m_propertyBlock, materialIndex);
                m_propertyBlock.SetColor(EyeColourId, m_eyeColour);
                m_propertyBlock.SetFloat(GlowIntensityId, m_glowIntensity);
                m_propertyBlock.SetFloat(FromExpressionId, (float)m_fromExpression);
                m_propertyBlock.SetFloat(ToExpressionId, (float)m_toExpression);
                m_propertyBlock.SetFloat(ExpressionBlendId, transition);
                m_propertyBlock.SetFloat(BlinkId, blink);
                m_propertyBlock.SetVector(LookOffsetId, lookOffset);
                m_propertyBlock.SetFloat(PulseId, pulse);
                m_propertyBlock.SetFloat(SingleEyeId, singleEye ? 1f : 0f);
                m_propertyBlock.SetFloat(EyeSideId, m_activeEyeSides[index]);
                faceRenderer.SetPropertyBlock(m_propertyBlock, materialIndex);
            }
        }

        private float CalculateBlink(float now)
        {
            if (now >= m_nextBlinkAt && m_blinkStartedAt < 0f)
                m_blinkStartedAt = now;

            if (m_blinkStartedAt < 0f) return 1f;

            float progress = (now - m_blinkStartedAt) / Mathf.Max(0.04f, m_blinkDuration);
            if (progress >= 1f)
            {
                m_blinkStartedAt = float.NegativeInfinity;
                ScheduleBlink(now);
                return 1f;
            }

            return 1f - Mathf.Sin(progress * Mathf.PI);
        }

        private void ResolveFaceScreen()
        {
            if (m_faceMaterial == null)
                m_faceMaterial = Resources.Load<Material>("RobotDigitalFace");

            if (m_faceMaterial == null)
            {
                m_ready = false;
                return;
            }

            // The current CouchGuy FBX uses two independent emissive meshes for its
            // face graphics. Drive both of them; selecting only the first one leaves
            // half of the face blank and maps a two-eye shader into a single-eye UV.
            Renderer leftEye = null;
            Renderer rightEye = null;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer candidate = renderers[index];
                if (candidate is ParticleSystemRenderer) continue;

                string rendererName = candidate.name;
                if (rendererName.IndexOf("FaceGraphic_L", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    leftEye = candidate;
                else if (rendererName.IndexOf("FaceGraphic_R", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    rightEye = candidate;
            }

            if (leftEye != null && rightEye != null)
            {
                m_activeRenderers = new[] { leftEye, rightEye };
                m_activeMaterialIndices = new[]
                {
                    FindFaceMaterialIndex(leftEye), FindFaceMaterialIndex(rightEye)
                };
                m_activeEyeSides = new[] { -1f, 1f };
                m_faceRenderer = leftEye;
                m_materialIndex = m_activeMaterialIndices[0];
            }
            else
            {
                if (m_faceRenderer == null)
                    FindBestFaceRenderer(out m_faceRenderer, out m_materialIndex);

                if (m_faceRenderer == null)
                {
                    m_ready = false;
                    return;
                }

                m_activeRenderers = new[] { m_faceRenderer };
                m_activeMaterialIndices = new[] { m_materialIndex };
                m_activeEyeSides = new[] { 0f };
            }

            m_ready = true;
            for (int index = 0; index < m_activeRenderers.Length; index++)
                m_ready &= AssignFaceMaterial(m_activeRenderers[index], ref m_activeMaterialIndices[index]);
        }

        private int FindFaceMaterialIndex(Renderer faceRenderer)
        {
            Material[] materials = faceRenderer.sharedMaterials;
            int bestIndex = 0;
            int bestScore = int.MinValue;
            for (int index = 0; index < materials.Length; index++)
            {
                int score = NameScore(materials[index]?.name);
                if (score <= bestScore) continue;
                bestScore = score;
                bestIndex = index;
            }

            return bestIndex;
        }

        private bool AssignFaceMaterial(Renderer faceRenderer, ref int materialIndex)
        {
            Material[] materials = faceRenderer.sharedMaterials;
            if (materials.Length == 0) return false;

            materialIndex = Mathf.Clamp(materialIndex, 0, materials.Length - 1);
            if (materials[materialIndex] != m_faceMaterial)
            {
                materials[materialIndex] = m_faceMaterial;
                faceRenderer.sharedMaterials = materials;
            }

            return true;
        }

        private void FindBestFaceRenderer(out Renderer bestRenderer, out int bestMaterialIndex)
        {
            bestRenderer = null;
            bestMaterialIndex = 0;
            int bestScore = int.MinValue;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer candidate = renderers[rendererIndex];
                if (candidate is ParticleSystemRenderer) continue;

                int rendererScore = NameScore(candidate.name);
                Material[] materials = candidate.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    int score = rendererScore + NameScore(materials[materialIndex]?.name);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestRenderer = candidate;
                    bestMaterialIndex = materialIndex;
                }
            }

            if (bestScore <= 0) bestRenderer = null;
        }

        private static int NameScore(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            string lower = value.ToLowerInvariant();
            int score = 0;
            if (lower == "face") score += 100;
            if (lower.Contains("face")) score += 45;
            if (lower.Contains("screen")) score += 35;
            if (lower.Contains("display")) score += 30;
            if (lower.Contains("visor")) score += 20;
            return score;
        }

        private void ScheduleHappy(float now) => m_nextHappyAt = now + Range(m_happyInterval);
        private void ScheduleBlink(float now) => m_nextBlinkAt = now + Range(m_blinkInterval);

        private float Range(Vector2 range)
        {
            float minimum = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
            float maximum = Mathf.Max(minimum, Mathf.Max(range.x, range.y));
            return Mathf.Lerp(minimum, maximum, Next01());
        }

        // Per-face PRNG avoids perturbing UnityEngine.Random and gameplay randomness.
        private float Next01()
        {
            m_randomState = m_randomState * 1664525u + 1013904223u;
            return (m_randomState & 0x00ffffffu) / 16777216f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_transitionDuration = Mathf.Max(0.01f, m_transitionDuration);
            m_surprisedDuration = Mathf.Max(0.05f, m_surprisedDuration);
            m_angryHoldDuration = Mathf.Max(0.05f, m_angryHoldDuration);
            m_blinkDuration = Mathf.Clamp(m_blinkDuration, 0.04f, 0.3f);
            m_materialIndex = Mathf.Max(0, m_materialIndex);
        }
#endif
    }
}
