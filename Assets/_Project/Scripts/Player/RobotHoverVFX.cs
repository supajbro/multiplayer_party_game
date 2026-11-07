using System;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>
    /// Locally animates cosmetic hover presentation. It never moves the player root,
    /// CharacterController, NetworkTransform or gameplay sockets.
    /// </summary>
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    public sealed class RobotHoverVFX : MonoBehaviour
    {
        private const float GroundCastHeight = 0.45f;
        private const float GroundCastDistance = 4f;
        private const float NearDetailDistance = 24f;
        private const float FarDetailDistance = 42f;
        private const float GroundOffset = 0.018f;
        private const int GroundHitCapacity = 8;

        private static readonly int EmissionColourId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissiveColourId = Shader.PropertyToID("_EmissiveColor");
        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColourId = Shader.PropertyToID("_Color");

        [Header("Configuration")]
        [SerializeField] private RobotHoverVFXSettings m_settings;

        [Header("Visual-only References")]
        [SerializeField] private Transform m_robotVisual;
        [SerializeField] private Transform m_hoverRing;
        [SerializeField] private Transform m_ringPivot;
        [SerializeField] private Transform m_ringEffects;
        [SerializeField] private ParticleSystem m_energyRipples;
        [SerializeField] private ParticleSystem m_floatingParticles;
        [SerializeField] private Renderer m_groundGlow;

        private readonly RaycastHit[] m_groundHits = new RaycastHit[GroundHitCapacity];
        private Renderer[] m_ringRenderers = Array.Empty<Renderer>();
        private Material[][] m_ringMaterials = Array.Empty<Material[]>();
        private MaterialPropertyBlock[][] m_ringBlocks = Array.Empty<MaterialPropertyBlock[]>();
        private MaterialPropertyBlock m_groundBlock;
        private Camera m_viewCamera;
        private CouchGuyAnimationDriver m_animationDriver;
        private Vector3 m_robotBaseLocalPosition;
        private Vector3 m_ringPivotBaseLocalPosition;
        private Quaternion m_ringBaseLocalRotation;
        private Vector3 m_ringFollowLocalPosition;
        private PlayerHealth m_health;
        private float m_ringAngle;
        private float m_phase;
        private float m_ringMovementBlend;
        private bool m_ringDetached;
        private Vector3 m_detachedRingPosition;
        private float m_groundAlpha;
        private float m_nextGroundCastTime;
        private int m_detailLevel = -1;
        private bool m_groundFound;
        private Vector3 m_groundPoint;
        private Vector3 m_groundNormal = Vector3.up;

        /// <summary>
        /// World-space displacement applied to the rendered robot. Gameplay code can
        /// subtract this from visual sockets to retain an unbobbed authoritative origin.
        /// </summary>
        public Vector3 VisualWorldOffset { get; private set; }
        public Vector3 RingWorldPosition => m_hoverRing != null ? m_hoverRing.position : transform.position;

        private void Awake()
        {
            CacheReferences();
            m_health = GetComponent<PlayerHealth>();
            if (m_robotVisual != null) m_robotBaseLocalPosition = m_robotVisual.localPosition;
            if (m_ringPivot != null) m_ringPivotBaseLocalPosition = m_ringPivot.localPosition;

            if (m_hoverRing != null)
            {
                m_ringBaseLocalRotation = Quaternion.Inverse(transform.rotation) * m_hoverRing.rotation;
                m_ringFollowLocalPosition = transform.InverseTransformPoint(m_hoverRing.position);
            }

            CacheRingMaterials();
            m_groundBlock = new MaterialPropertyBlock();
            if (m_groundGlow != null) m_groundGlow.GetPropertyBlock(m_groundBlock);
            m_phase = (GetInstanceID() & 1023) * (Mathf.PI * 2f / 1024f);
            ApplyParticleRates(0);
        }

        private void OnEnable()
        {
            m_nextGroundCastTime = 0f;
            m_detailLevel = -1;
            foreach (Renderer ringRenderer in m_ringRenderers)
                if (ringRenderer != null) ringRenderer.enabled = true;
            if (m_floatingParticles != null && !m_floatingParticles.isPlaying) m_floatingParticles.Play();
            if (m_energyRipples != null && !m_energyRipples.isPlaying) m_energyRipples.Play();
        }

        private void LateUpdate()
        {
            if (m_settings == null) return;

            ResolveCamera();
            UpdateDetailLevel();
            AnimateHover();
            AnimateRing();
            AnimateEmission();
            UpdateGroundProjection();
        }

        private void OnDisable()
        {
            VisualWorldOffset = Vector3.zero;
            if (m_robotVisual != null) m_robotVisual.localPosition = m_robotBaseLocalPosition;
            if (m_ringPivot != null) m_ringPivot.localPosition = m_ringPivotBaseLocalPosition;
            if (m_hoverRing != null)
                m_hoverRing.SetPositionAndRotation(transform.TransformPoint(m_ringFollowLocalPosition),
                    transform.rotation * m_ringBaseLocalRotation);
            foreach (Renderer ringRenderer in m_ringRenderers)
                if (ringRenderer != null) ringRenderer.enabled = false;
            if (m_floatingParticles != null) m_floatingParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (m_energyRipples != null) m_energyRipples.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (m_groundGlow != null) m_groundGlow.enabled = false;
        }

        private void AnimateHover()
        {
            float bob = m_health != null && m_health.IsKnockedDown ? 0f : Mathf.Sin(Time.time * m_settings.HoverBobFrequency + m_phase) *
                m_settings.HoverBobAmplitude;
            if (m_robotVisual != null)
            {
                m_robotVisual.localPosition = m_robotBaseLocalPosition + Vector3.up * bob;
                VisualWorldOffset = m_robotVisual.parent.TransformVector(Vector3.up * bob);
            }

            if (m_ringPivot == null) return;

            float movementSpeed = m_animationDriver != null
                ? m_animationDriver.NormalizedMovementSpeed
                : 0f;
            float targetMovementBlend = Mathf.Clamp01(movementSpeed);
            m_ringMovementBlend = Mathf.MoveTowards(
                m_ringMovementBlend,
                targetMovementBlend,
                m_settings.RingMotionResponse * Time.deltaTime);
            float sprintBlend = Mathf.InverseLerp(1f, 1.65f, movementSpeed);
            float amplitude = m_settings.RingBobAmplitude *
                (1f + m_ringMovementBlend * m_settings.RingMovementIntensity) *
                Mathf.Lerp(1f, m_settings.RingSprintIntensity, sprintBlend);
            float frequency = m_settings.RingBobFrequency * Mathf.Lerp(1f, 1.18f, m_ringMovementBlend);
            float ringBob = Mathf.Sin(Time.time * frequency + m_phase + 1.17f) * amplitude;
            bool detached = m_health != null && m_health.IsKnockedDown;
            if (detached)
            {
                if (!m_ringDetached) m_detachedRingPosition = m_health.RingAnchor;
                m_detachedRingPosition = Vector3.MoveTowards(m_detachedRingPosition, m_health.RingAnchor, 15f * Time.deltaTime);
            }
            m_ringDetached = detached;
            Vector3 position = detached ? m_detachedRingPosition :
                transform.TransformPoint(m_ringFollowLocalPosition) + Vector3.up * ringBob;
            m_ringPivot.SetPositionAndRotation(position, Quaternion.identity);
            if (m_hoverRing != null) m_hoverRing.position = position;
        }

        private float m_punchWobbleStartedAt = float.NegativeInfinity;
        private float m_punchWobbleStrength;

        public void NotifyPunch(bool cross, float age)
        {
            m_punchWobbleStartedAt = Time.time - age;
            m_punchWobbleStrength = cross ? -5f : 3f;
        }

        private void AnimateRing()
        {
            if (m_hoverRing == null) return;
            m_ringAngle = Mathf.Repeat(
                m_ringAngle + m_settings.RingRotationSpeed * Time.deltaTime,
                360f);
            m_hoverRing.rotation = Quaternion.AngleAxis(m_ringAngle, Vector3.up) *
                m_ringBaseLocalRotation;
            float punchAge = Time.time - m_punchWobbleStartedAt;
            if (!m_ringDetached && punchAge >= 0f && punchAge < .46f)
            {
                float wobble = Mathf.Sin(punchAge / .46f * Mathf.PI * 2f) *
                    (1f - punchAge / .46f) * m_punchWobbleStrength;
                m_hoverRing.rotation = Quaternion.AngleAxis(wobble, transform.forward) * m_hoverRing.rotation;
            }

            if (m_ringEffects != null)
                m_ringEffects.localRotation = Quaternion.AngleAxis(-m_ringAngle * 0.18f, Vector3.up);
        }

        private void AnimateEmission()
        {
            float pulse = 0.88f + Mathf.Sin(
                Time.time * m_settings.EmissionPulseSpeed + m_phase) * 0.12f;
            Color emission = new Color(0.02f, 0.58f, 1f, 1f) *
                (m_settings.EmissionBrightness * pulse);

            for (int rendererIndex = 0; rendererIndex < m_ringRenderers.Length; rendererIndex++)
            {
                Renderer ringRenderer = m_ringRenderers[rendererIndex];
                MaterialPropertyBlock[] blocks = m_ringBlocks[rendererIndex];
                Material[] materials = m_ringMaterials[rendererIndex];
                for (int materialIndex = 0; materialIndex < blocks.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    MaterialPropertyBlock block = blocks[materialIndex];
                    if (material == null) continue;
                    if (material.HasProperty(EmissionColourId)) block.SetColor(EmissionColourId, emission);
                    if (material.HasProperty(EmissiveColourId)) block.SetColor(EmissiveColourId, emission);
                    ringRenderer.SetPropertyBlock(block, materialIndex);
                }
            }
        }

        private void UpdateGroundProjection()
        {
            if (m_groundGlow == null) return;

            float interval = m_detailLevel == 0 ? 0.05f : 0.16f;
            if (m_detailLevel >= 2)
            {
                m_groundFound = false;
            }
            else if (Time.time >= m_nextGroundCastTime)
            {
                m_nextGroundCastTime = Time.time + interval;
                FindGround();
            }

            float targetAlpha = m_groundFound ? m_settings.GroundGlowIntensity : 0f;
            m_groundAlpha = Mathf.MoveTowards(m_groundAlpha, targetAlpha, Time.deltaTime * 2.5f);
            bool visible = m_groundAlpha > 0.005f;
            m_groundGlow.enabled = visible;
            if (!visible) return;

            Transform glowTransform = m_groundGlow.transform;
            glowTransform.SetPositionAndRotation(
                m_groundPoint + m_groundNormal * GroundOffset,
                Quaternion.FromToRotation(Vector3.forward, m_groundNormal));

            Color colour = new(0.02f, 0.62f, 1f, m_groundAlpha);
            m_groundBlock.SetColor(ColourId, colour);
            m_groundBlock.SetColor(BaseColourId, colour);
            m_groundGlow.SetPropertyBlock(m_groundBlock);
        }

        private void FindGround()
        {
            Vector3 origin = RingWorldPosition + Vector3.up * GroundCastHeight;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                m_groundHits,
                GroundCastDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            m_groundFound = false;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = m_groundHits[index];
                if (hit.collider == null || hit.distance >= nearestDistance ||
                    hit.collider.transform.IsChildOf(transform))
                    continue;

                nearestDistance = hit.distance;
                m_groundPoint = hit.point;
                m_groundNormal = hit.normal.sqrMagnitude > 0.5f ? hit.normal.normalized : Vector3.up;
                m_groundFound = true;
            }
        }

        private void UpdateDetailLevel()
        {
            float squaredDistance = m_viewCamera != null
                ? (m_viewCamera.transform.position - transform.position).sqrMagnitude
                : 0f;
            int detail = squaredDistance > FarDetailDistance * FarDetailDistance
                ? 2
                : squaredDistance > NearDetailDistance * NearDetailDistance ? 1 : 0;
            if (detail == m_detailLevel) return;
            m_detailLevel = detail;
            ApplyParticleRates(detail);
        }

        private void ApplyParticleRates(int detail)
        {
            if (m_settings == null) return;
            float multiplier = detail == 0 ? 1f : detail == 1 ? 0.45f : 0f;
            if (m_floatingParticles != null)
            {
                ParticleSystem.EmissionModule emission = m_floatingParticles.emission;
                emission.rateOverTime = m_settings.ParticleEmissionRate * multiplier;
            }
            if (m_energyRipples != null)
            {
                ParticleSystem.EmissionModule emission = m_energyRipples.emission;
                emission.rateOverTime = m_settings.RippleSpawnRate * multiplier;
            }
        }

        private void ResolveCamera()
        {
            if (m_viewCamera == null || !m_viewCamera.isActiveAndEnabled)
                m_viewCamera = Camera.main;
        }

        private void CacheReferences()
        {
            m_settings ??= Resources.Load<RobotHoverVFXSettings>("RobotHoverVFXSettings");
            m_animationDriver ??= GetComponent<CouchGuyAnimationDriver>();
            m_robotVisual ??= transform.Find("Visual/CouchGuy");
            m_hoverRing ??= FindDescendant(transform, "HoverAssembly");
            m_ringPivot ??= transform.Find("Visual/HoverVFX/HoverRingPivot");
            m_ringEffects ??= transform.Find("Visual/HoverVFX/HoverRingPivot/RingEffects");
            m_energyRipples ??= FindParticleSystem("HoverEnergyRipples");
            m_floatingParticles ??= FindParticleSystem("HoverEnergyWisps");
            Transform glow = transform.Find("Visual/HoverVFX/GroundGlow");
            m_groundGlow ??= glow != null ? glow.GetComponent<Renderer>() : null;
        }

        private void CacheRingMaterials()
        {
            if (m_hoverRing == null) return;
            m_ringRenderers = m_hoverRing.GetComponentsInChildren<Renderer>(true);
            m_ringMaterials = new Material[m_ringRenderers.Length][];
            m_ringBlocks = new MaterialPropertyBlock[m_ringRenderers.Length][];
            for (int rendererIndex = 0; rendererIndex < m_ringRenderers.Length; rendererIndex++)
            {
                m_ringMaterials[rendererIndex] = m_ringRenderers[rendererIndex].sharedMaterials;
                int materialCount = m_ringMaterials[rendererIndex].Length;
                m_ringBlocks[rendererIndex] = new MaterialPropertyBlock[materialCount];
                for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    m_ringRenderers[rendererIndex].GetPropertyBlock(block, materialIndex);
                    m_ringBlocks[rendererIndex][materialIndex] = block;
                }
            }
        }

        private ParticleSystem FindParticleSystem(string objectName)
        {
            Transform found = FindDescendant(transform, objectName);
            return found != null ? found.GetComponent<ParticleSystem>() : null;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            foreach (Transform child in root)
            {
                if (child.name == objectName) return child;
                Transform found = FindDescendant(child, objectName);
                if (found != null) return found;
            }
            return null;
        }

#if UNITY_EDITOR
        private void OnValidate() => CacheReferences();
#endif
    }
}
