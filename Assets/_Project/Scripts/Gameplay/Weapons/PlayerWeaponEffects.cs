using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Small preallocated pool for combat tracers, impacts, and muzzle flashes.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponEffects : MonoBehaviour
    {
        private const int PoolSize = 24;
        private readonly LineRenderer[] m_tracers = new LineRenderer[PoolSize];
        private readonly float[] m_tracerExpiry = new float[PoolSize];
        private readonly ParticleSystem[] m_impacts = new ParticleSystem[PoolSize];
        private Material m_tracerMaterial;
        private int m_nextTracer;
        private int m_nextImpact;

        private void Awake()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                m_tracerMaterial = new Material(shader) { color = new Color(1f, 0.75f, 0.18f, 0.9f) };
            }

            for (int index = 0; index < PoolSize; index++)
            {
                GameObject tracerObject = new GameObject($"WeaponTracer_{index}");
                tracerObject.transform.SetParent(transform, false);
                LineRenderer line = tracerObject.AddComponent<LineRenderer>();
                line.enabled = false;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = 0.025f;
                line.endWidth = 0.006f;
                line.sharedMaterial = m_tracerMaterial;
                line.startColor = new Color(1f, 0.9f, 0.35f, 0.95f);
                line.endColor = new Color(1f, 0.35f, 0.08f, 0.2f);
                m_tracers[index] = line;

                GameObject impactObject = new GameObject($"WeaponImpact_{index}");
                impactObject.transform.SetParent(transform, false);
                ParticleSystem particles = impactObject.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = particles.main;
                main.loop = false;
                main.playOnAwake = false;
                main.duration = 0.12f;
                main.startLifetime = 0.18f;
                main.startSpeed = 2.2f;
                main.startSize = 0.06f;
                main.maxParticles = 8;
                main.startColor = new Color(1f, 0.55f, 0.12f, 1f);
                ParticleSystem.EmissionModule emission = particles.emission;
                emission.enabled = false;
                m_impacts[index] = particles;
            }
        }

        private void Update()
        {
            float now = Time.time;
            for (int index = 0; index < PoolSize; index++)
            {
                if (m_tracers[index].enabled && now >= m_tracerExpiry[index])
                {
                    m_tracers[index].enabled = false;
                }
            }
        }

        private void OnDestroy()
        {
            if (m_tracerMaterial != null) Destroy(m_tracerMaterial);
        }

        public void PlayShot(Vector3 muzzle, Vector3[] ends)
        {
            if (ends == null) return;
            ParticleSystem flash = m_impacts[m_nextImpact];
            flash.transform.position = muzzle;
            ParticleSystem.MainModule flashMain = flash.main;
            flashMain.startSize = 0.12f;
            flashMain.startLifetime = 0.08f;
            flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            flash.Emit(8);
            m_nextImpact = (m_nextImpact + 1) % PoolSize;
            for (int index = 0; index < ends.Length; index++)
            {
                LineRenderer tracer = m_tracers[m_nextTracer];
                tracer.SetPosition(0, muzzle);
                tracer.SetPosition(1, ends[index]);
                tracer.enabled = true;
                m_tracerExpiry[m_nextTracer] = Time.time + 0.065f;
                m_nextTracer = (m_nextTracer + 1) % PoolSize;

                ParticleSystem impact = m_impacts[m_nextImpact];
                ParticleSystem.MainModule impactMain = impact.main;
                impactMain.startSize = 0.06f;
                impactMain.startLifetime = 0.18f;
                impact.transform.position = ends[index];
                impact.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                impact.Emit(5);
                m_nextImpact = (m_nextImpact + 1) % PoolSize;
            }
        }
    }
}
