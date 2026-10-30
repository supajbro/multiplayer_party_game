using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Local presentation only. Networked victims replicate the impact event.</summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class BloodBurst : MonoBehaviour
    {
        [System.Serializable]
        private struct Intensity
        {
            [Min(1), Tooltip("Number of droplets emitted per impact.")] public int Count;
            [Tooltip("Minimum (X) and maximum (Y) droplet lifetime in seconds.")] public Vector2 Lifetime;
            [Tooltip("Minimum (X) and maximum (Y) spray speed in metres per second.")] public Vector2 Speed;
            [Tooltip("Minimum (X) and maximum (Y) droplet size in metres.")] public Vector2 Size;
        }

        [SerializeField] private Intensity m_bullet = new()
        { Count = 12, Lifetime = new(0.2f, 0.5f), Speed = new(1.5f, 3.5f), Size = new(0.06f, 0.14f) };
        [SerializeField] private Intensity m_vehicle = new()
        { Count = 160, Lifetime = new(0.7f, 1.4f), Speed = new(8f, 14f), Size = new(0.25f, 0.55f) };
        private static readonly Queue<BloodBurst> Pool = new();
        private static BloodVfxSettings s_settings;
        private ParticleSystem m_particles;
        private bool m_playing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPool()
        {
            Pool.Clear();
            s_settings = null;
        }

        public static void Play(Vector3 position, Vector3 direction, bool vehicle)
        {
            s_settings ??= Resources.Load<BloodVfxSettings>("BloodVfxSettings");
            if (s_settings == null || s_settings.Prefab == null) return;
            BloodBurst effect = null;
            while (Pool.Count > 0 && effect == null) effect = Pool.Dequeue();
            if (effect == null) effect = Instantiate(s_settings.Prefab);
            effect.m_particles ??= effect.GetComponent<ParticleSystem>();
            effect.transform.SetPositionAndRotation(position,
                Quaternion.FromToRotation(Vector3.forward,
                    direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.up));
            effect.gameObject.SetActive(true);
            Intensity intensity = vehicle ? effect.m_vehicle : effect.m_bullet;
            var main = effect.m_particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(intensity.Lifetime.x, intensity.Lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(intensity.Speed.x, intensity.Speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(intensity.Size.x, intensity.Size.y);
            main.maxParticles = Mathf.Max(main.maxParticles, intensity.Count);
            main.stopAction = ParticleSystemStopAction.Callback;
            var emission = effect.m_particles.emission;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)Mathf.Clamp(intensity.Count, 1, short.MaxValue)));
            effect.m_playing = true;
            effect.m_particles.Play(true);
        }

        private void OnParticleSystemStopped()
        {
            if (!m_playing) return;
            m_playing = false;
            gameObject.SetActive(false);
            if (Pool.Count < 32) Pool.Enqueue(this);
            else Destroy(gameObject);
        }
    }
}
