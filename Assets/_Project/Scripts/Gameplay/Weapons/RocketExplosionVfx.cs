using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Local pooled presentation only. All meshes, particles and materials live in the saved prefab.</summary>
    public sealed class RocketExplosionVfx : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] m_particles;
        [SerializeField] private Transform m_shockwave;
        [SerializeField] private Renderer m_ringRenderer;
        [SerializeField, Min(.1f)] private float m_lifetime = 1f;
        [SerializeField, Min(.05f)] private float m_ringDuration = .4f;
        private static readonly Dictionary<RocketExplosionVfx, Queue<RocketExplosionVfx>> Pools = new();
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private RocketExplosionVfx m_source;
        private MaterialPropertyBlock m_properties;
        private float m_startedAt;
        private float m_radius;

        public static void Play(RocketExplosionVfx prefab, Vector3 position, Vector3 ground, float radius)
        {
            if (!Pools.TryGetValue(prefab, out var pool))
            {
                pool = new Queue<RocketExplosionVfx>();
                Pools[prefab] = pool;
            }
            RocketExplosionVfx effect = null;
            while (pool.Count > 0 && effect == null) effect = pool.Dequeue();
            if (effect == null) effect = Instantiate(prefab);
            effect.m_source = prefab;
            effect.transform.position = position;
            // Authoring uses a 5 m reference radius; ring uses the exact supplied gameplay radius.
            effect.transform.localScale = Vector3.one * (radius / 5f);
            effect.m_radius = radius;
            effect.m_startedAt = Time.time;
            effect.m_shockwave.position = ground + Vector3.up * .035f;
            effect.m_shockwave.localScale = Vector3.zero;
            effect.gameObject.SetActive(true);
            foreach (ParticleSystem particles in effect.m_particles)
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.Play(false);
            }
        }

        private void Update()
        {
            float elapsed = Time.time - m_startedAt;
            float t = Mathf.Clamp01(elapsed / m_ringDuration);
            float radius = m_radius * (1f - Mathf.Pow(1f - t, 3f));
            float parentScale = Mathf.Max(.0001f, transform.lossyScale.x);
            m_shockwave.localScale = Vector3.one * (radius / parentScale);
            m_properties ??= new MaterialPropertyBlock();
            m_properties.SetFloat(Opacity, 1f - t * t);
            m_ringRenderer.SetPropertyBlock(m_properties);
            if (elapsed < m_lifetime || m_source == null) return;
            foreach (ParticleSystem particles in m_particles)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false);
            Pools[m_source].Enqueue(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPools() => Pools.Clear();
    }
}
