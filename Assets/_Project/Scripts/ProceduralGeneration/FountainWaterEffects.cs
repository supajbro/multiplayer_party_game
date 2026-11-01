using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Local cosmetic effects. Native camera culling; no network state or Update loop.</summary>
    [DisallowMultipleComponent]
    public sealed class FountainWaterEffects : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] m_particles;
        [SerializeField, Tooltip("Optional. Otherwise follows Camera.main, including camera replacement.")]
        private Camera m_camera;
        [SerializeField, Min(1f)] private float m_fullEffectDistance = 28f;
        [SerializeField, Min(1f)] private float m_effectCutoffDistance = 48f;
        [SerializeField, Range(0f, 1f)] private float m_mediumEmission = 0.3f;
        [SerializeField] private Vector3 m_boundsCenter = new Vector3(0f, 1.8f, 0f);
        [SerializeField, Min(.1f)] private float m_boundsRadius = 4.3f;
        private CullingGroup m_culling;
        private BoundingSphere[] m_spheres;
        private float[] m_rates;
        private Camera m_boundCamera;
        private int m_band = -1;

        private void OnEnable()
        {
            m_rates = new float[m_particles.Length];
            for (int i = 0; i < m_particles.Length; i++)
                if (m_particles[i]) m_rates[i] = m_particles[i].emission.rateOverTimeMultiplier;
            m_spheres = new BoundingSphere[1];
            m_culling = new CullingGroup();
            m_culling.SetBoundingSpheres(m_spheres);
            m_culling.SetBoundingSphereCount(1);
            m_culling.SetBoundingDistances(new[] { m_fullEffectDistance, Mathf.Max(m_fullEffectDistance, m_effectCutoffDistance) });
            m_culling.onStateChanged = OnVisibilityChanged;
            SetBand(-1, true);
            // Rebind cameras and moved/scaled instances at low frequency, staggered across props.
            InvokeRepeating(nameof(RefreshCamera), 0f, 1f + (GetInstanceID() & 7) * .03f);
        }

        private void RefreshCamera()
        {
            Vector3 scale = transform.lossyScale;
            m_spheres[0] = new BoundingSphere(transform.TransformPoint(m_boundsCenter),
                m_boundsRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Camera target = m_camera ? m_camera : Camera.main;
            if (target == m_boundCamera && target) return;
            m_boundCamera = target;
            m_culling.targetCamera = target;
            if (target) m_culling.SetDistanceReferencePoint(target.transform);
            // A replacement camera can produce the same native state (no change callback).
            // Retain/query that state instead of leaving effects disabled until it changes.
            SetBand(target && m_culling.IsVisible(0) ? m_culling.GetDistance(0) : -1);
        }

        private void OnVisibilityChanged(CullingGroupEvent state) =>
            SetBand(state.isVisible && m_boundCamera ? state.currentDistance : -1);

        private void SetBand(int band, bool force = false)
        {
            if (band >= 2) band = -1;
            if (!force && band == m_band) return;
            m_band = band;
            for (int i = 0; i < m_particles.Length; i++)
            {
                ParticleSystem ps = m_particles[i];
                if (!ps) continue;
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = m_rates[i] * (band == 1 ? m_mediumEmission : 1f);
                if (band < 0) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                else if (!ps.isPlaying) ps.Play(false);
            }
        }

        private void OnDisable()
        {
            CancelInvoke();
            m_culling?.Dispose();
            m_culling = null;
            SetBand(-1, true);
            // Restore authored rates, including when toggled repeatedly in the Inspector.
            if (m_rates != null)
                for (int i = 0; i < m_particles.Length; i++)
                    if (m_particles[i]) { var e = m_particles[i].emission; e.rateOverTimeMultiplier = m_rates[i]; }
        }
    }
}
