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
        private const float RingBobScale = 0.28f;
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
        private static RobotHoverVFXSettings s_fallbackSettings;
        private static Mesh s_groundGlowMesh;
        private static Material s_groundGlowMaterial;

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
        private Vector3 m_robotBaseLocalPosition;
        private Vector3 m_ringPivotBaseLocalPosition;
        private Quaternion m_ringBaseLocalRotation;
        private float m_ringAngle;
        private float m_phase;
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

        private void Awake()
        {
            CacheReferences();
            EnsureRuntimePresentation();
            CacheReferences();
            if (m_robotVisual != null) m_robotBaseLocalPosition = m_robotVisual.localPosition;
            if (m_ringPivot != null) m_ringPivotBaseLocalPosition = m_ringPivot.localPosition;

            if (m_hoverRing != null && m_ringPivot != null && m_hoverRing.parent != m_ringPivot)
                m_hoverRing.SetParent(m_ringPivot, true);
            if (m_hoverRing != null) m_ringBaseLocalRotation = m_hoverRing.localRotation;

            CacheRingMaterials();
            m_groundBlock = new MaterialPropertyBlock();
            m_phase = (GetInstanceID() & 1023) * (Mathf.PI * 2f / 1024f);
            ApplyParticleRates(0);
        }

        private void OnEnable()
        {
            m_nextGroundCastTime = 0f;
            m_detailLevel = -1;
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
            if (m_hoverRing != null) m_hoverRing.localRotation = m_ringBaseLocalRotation;
            if (m_groundGlow != null) m_groundGlow.enabled = false;
        }

        private void AnimateHover()
        {
            float bob = Mathf.Sin(Time.time * m_settings.HoverBobFrequency + m_phase) *
                m_settings.HoverBobAmplitude;
            if (m_robotVisual != null)
            {
                m_robotVisual.localPosition = m_robotBaseLocalPosition + Vector3.up * bob;
                VisualWorldOffset = m_robotVisual.parent.TransformVector(Vector3.up * bob);
            }

            if (m_ringPivot != null)
            {
                float ringBob = Mathf.Sin(Time.time * m_settings.HoverBobFrequency + m_phase + 0.22f) *
                    m_settings.HoverBobAmplitude * RingBobScale;
                m_ringPivot.localPosition = m_ringPivotBaseLocalPosition + Vector3.up * ringBob;
            }
        }

        private void AnimateRing()
        {
            if (m_hoverRing == null) return;
            m_ringAngle = Mathf.Repeat(
                m_ringAngle + m_settings.RingRotationSpeed * Time.deltaTime,
                360f);
            m_hoverRing.localRotation = m_ringBaseLocalRotation *
                Quaternion.AngleAxis(m_ringAngle, Vector3.up);

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
                    ringRenderer.GetPropertyBlock(block, materialIndex);
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
            m_groundGlow.GetPropertyBlock(m_groundBlock);
            m_groundBlock.SetColor(ColourId, colour);
            m_groundBlock.SetColor(BaseColourId, colour);
            m_groundGlow.SetPropertyBlock(m_groundBlock);
        }

        private void FindGround()
        {
            Vector3 origin = transform.position + Vector3.up * GroundCastHeight;
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
            m_robotVisual ??= transform.Find("Visual/CouchGuy");
            m_hoverRing ??= FindDescendant(transform, "HoverRing");
            m_ringPivot ??= transform.Find("Visual/HoverVFX/HoverRingPivot");
            m_ringEffects ??= transform.Find("Visual/HoverVFX/HoverRingPivot/RingEffects");
            m_energyRipples ??= FindParticleSystem("HoverEnergyRipples");
            m_floatingParticles ??= FindParticleSystem("HoverEnergyWisps");
            Transform glow = transform.Find("Visual/HoverVFX/GroundGlow");
            m_groundGlow ??= glow != null ? glow.GetComponent<Renderer>() : null;
        }

        private void EnsureRuntimePresentation()
        {
            if (m_settings == null)
            {
                if (s_fallbackSettings == null)
                {
                    s_fallbackSettings = ScriptableObject.CreateInstance<RobotHoverVFXSettings>();
                    s_fallbackSettings.hideFlags = HideFlags.HideAndDontSave;
                }
                m_settings = s_fallbackSettings;
            }

            Transform visual = transform.Find("Visual");
            if (visual == null || m_hoverRing == null) return;

            Transform hoverVfx = visual.Find("HoverVFX");
            if (hoverVfx == null)
            {
                hoverVfx = new GameObject("HoverVFX").transform;
                hoverVfx.SetParent(visual, false);
                Renderer ringRenderer = m_hoverRing.GetComponentInChildren<Renderer>();
                hoverVfx.position = ringRenderer != null ? ringRenderer.bounds.center : m_hoverRing.position;
            }

            if (m_ringPivot == null)
            {
                m_ringPivot = new GameObject("HoverRingPivot").transform;
                m_ringPivot.SetParent(hoverVfx, false);
            }
            if (m_ringEffects == null)
            {
                m_ringEffects = new GameObject("RingEffects").transform;
                m_ringEffects.SetParent(m_ringPivot, false);
                if (m_energyRipples != null) m_energyRipples.transform.SetParent(m_ringEffects, true);
                if (m_floatingParticles != null) m_floatingParticles.transform.SetParent(m_ringEffects, true);
            }
            if (m_groundGlow == null)
                m_groundGlow = CreateRuntimeGroundGlow(hoverVfx);
        }

        private Renderer CreateRuntimeGroundGlow(Transform parent)
        {
            EnsureGroundGlowResources();
            GameObject glow = new("GroundGlow");
            glow.transform.SetParent(parent, false);
            MeshFilter filter = glow.AddComponent<MeshFilter>();
            filter.sharedMesh = s_groundGlowMesh;
            MeshRenderer renderer = glow.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = s_groundGlowMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.enabled = false;

            Renderer ringRenderer = m_hoverRing.GetComponentInChildren<Renderer>();
            float radius = ringRenderer != null
                ? Mathf.Max(ringRenderer.bounds.extents.x, ringRenderer.bounds.extents.z)
                : 0.34f;
            glow.transform.localScale = Vector3.one * (radius * 2.45f);
            return renderer;
        }

        private static void EnsureGroundGlowResources()
        {
            if (s_groundGlowMesh == null)
            {
                s_groundGlowMesh = new Mesh { name = "Runtime Hover Ground Glow Quad" };
                s_groundGlowMesh.hideFlags = HideFlags.HideAndDontSave;
                s_groundGlowMesh.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
                };
                s_groundGlowMesh.uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f)
                };
                s_groundGlowMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                s_groundGlowMesh.RecalculateNormals();
                s_groundGlowMesh.RecalculateBounds();
            }

            if (s_groundGlowMaterial != null) return;
            Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                : Shader.Find("Particles/Standard Unlit");
            shader ??= Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null) return;

            const int size = 32;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime Hover Ground Glow",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[size * size];
            Vector2 centre = new((size - 1) * 0.5f, (size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), centre) / centre.x;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 2.15f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);

            s_groundGlowMaterial = new Material(shader)
            {
                name = "Runtime Hover Ground Glow",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = 3000
            };
            if (s_groundGlowMaterial.HasProperty("_MainTex"))
                s_groundGlowMaterial.SetTexture("_MainTex", texture);
            if (s_groundGlowMaterial.HasProperty("_BaseMap"))
                s_groundGlowMaterial.SetTexture("_BaseMap", texture);
            if (s_groundGlowMaterial.HasProperty("_Surface")) s_groundGlowMaterial.SetFloat("_Surface", 1f);
            if (s_groundGlowMaterial.HasProperty("_Blend")) s_groundGlowMaterial.SetFloat("_Blend", 0f);
            if (s_groundGlowMaterial.HasProperty("_Mode")) s_groundGlowMaterial.SetFloat("_Mode", 2f);
            if (s_groundGlowMaterial.HasProperty("_SrcBlend")) s_groundGlowMaterial.SetFloat("_SrcBlend", 5f);
            if (s_groundGlowMaterial.HasProperty("_DstBlend")) s_groundGlowMaterial.SetFloat("_DstBlend", 10f);
            if (s_groundGlowMaterial.HasProperty("_ZWrite")) s_groundGlowMaterial.SetFloat("_ZWrite", 0f);
            s_groundGlowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
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
                    m_ringBlocks[rendererIndex][materialIndex] = new MaterialPropertyBlock();
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
