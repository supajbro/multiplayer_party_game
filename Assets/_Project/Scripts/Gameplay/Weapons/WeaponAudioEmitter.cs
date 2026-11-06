using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Reusable local 3D audio voices driven by replicated weapon events.</summary>
    [DisallowMultipleComponent]
    public sealed class WeaponAudioEmitter : MonoBehaviour
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public double AvailableAt;
        }

        private static readonly AnimationCurve SmoothRolloff = new(
            new Keyframe(0f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, 0f, 0f));

        private readonly Dictionary<PlayerWeaponType, List<Voice>> m_gunshotVoices = new();
        private WeaponAudioSettings m_settings;
        private AudioSource m_rifleStopSource;
        private Coroutine m_pendingPump;
        private bool m_reportedMissingSettings;

        private void Awake()
        {
            m_settings = Resources.Load<WeaponAudioSettings>(WeaponAudioSettings.ResourcesPath);
            if (m_settings == null && !m_reportedMissingSettings)
            {
                Debug.LogError(
                    $"Missing Resources/{WeaponAudioSettings.ResourcesPath}.asset; weapon audio is disabled.",
                    this);
                m_reportedMissingSettings = true;
            }
        }

        private void OnDisable()
        {
            if (m_pendingPump != null)
            {
                StopCoroutine(m_pendingPump);
                m_pendingPump = null;
            }

            foreach (List<Voice> voices in m_gunshotVoices.Values)
            {
                foreach (Voice voice in voices)
                {
                    voice.Source.Stop();
                }
            }

            if (m_rifleStopSource != null)
            {
                m_rifleStopSource.Stop();
            }
        }

        private void OnDestroy()
        {
            foreach (List<Voice> voices in m_gunshotVoices.Values)
                foreach (Voice voice in voices)
                    if (voice.Source != null) Destroy(voice.Source.gameObject);
            if (m_rifleStopSource != null) Destroy(m_rifleStopSource.gameObject);
        }

        // Uses the existing explosion RPC, but a separate saved voice survives projectile despawn.
        public static void PlayRocketExplosion(Vector3 worldPosition)
        {
            var settings = Resources.Load<WeaponAudioSettings>(WeaponAudioSettings.ResourcesPath);
            var profile = settings != null ? settings.GetProfile(PlayerWeaponType.RocketLauncher) : null;
            if (settings == null || settings.VoicePrefab == null || profile?.ExplosionClip == null) return;
            AudioSource source = Instantiate(settings.VoicePrefab, worldPosition, Quaternion.identity);
            Configure(source, profile, worldPosition);
            source.pitch = 1f;
            source.PlayOneShot(profile.ExplosionClip, profile.ExplosionVolume);
            Destroy(source.gameObject, profile.ExplosionClip.length + .1f);
        }

        public void PlayShot(PlayerWeaponType weaponType, Vector3 worldPosition, Transform spatialOrigin = null)
        {
            WeaponAudioSettings.Profile profile = GetProfile(weaponType);
            AudioClip[] clips = profile?.Gunshots;
            if (clips == null || clips.Length == 0 || m_settings.VoicePrefab == null)
            {
                return;
            }

            if (weaponType == PlayerWeaponType.AssaultRifle && m_rifleStopSource != null)
            {
                m_rifleStopSource.Stop();
            }

            int variation = Random.Range(0, clips.Length);
            AudioClip clip = clips[variation];
            if (clip == null)
            {
                return;
            }

            Voice voice = GetVoice(weaponType, profile);
            Configure(voice.Source, profile, worldPosition);
            voice.Source.pitch = Random.Range(profile.MinimumPitch, profile.MaximumPitch);
            voice.Source.PlayOneShot(clip, profile.GunshotVolume);
            voice.AvailableAt = AudioSettings.dspTime + clip.length / Mathf.Max(0.01f, voice.Source.pitch);

            if (weaponType == PlayerWeaponType.Shotgun && profile.PumpClip != null)
            {
                if (m_pendingPump != null)
                {
                    StopCoroutine(m_pendingPump);
                }
                m_pendingPump = StartCoroutine(PlayPumpAfter(profile, spatialOrigin, worldPosition));
            }
        }

        public void PlayRifleStop(Vector3 worldPosition)
        {
            WeaponAudioSettings.Profile profile = GetProfile(PlayerWeaponType.AssaultRifle);
            if (profile?.FiringStopClip == null || m_settings.VoicePrefab == null)
            {
                return;
            }

            m_rifleStopSource ??= CreateSource("AssaultRifleStop");
            Configure(m_rifleStopSource, profile, worldPosition);
            m_rifleStopSource.pitch = 1f;
            m_rifleStopSource.Stop();
            m_rifleStopSource.PlayOneShot(profile.FiringStopClip, profile.FiringStopVolume);
        }

        private IEnumerator PlayPumpAfter(WeaponAudioSettings.Profile profile, Transform spatialOrigin,
            Vector3 fallbackPosition)
        {
            yield return new WaitForSeconds(profile.PumpDelay);
            Vector3 position = spatialOrigin != null ? spatialOrigin.position : fallbackPosition;
            Voice voice = GetVoice(PlayerWeaponType.Shotgun, profile);
            Configure(voice.Source, profile, position);
            voice.Source.pitch = 1f;
            voice.Source.PlayOneShot(profile.PumpClip, profile.PumpVolume);
            voice.AvailableAt = AudioSettings.dspTime + profile.PumpClip.length;
            m_pendingPump = null;
        }

        private WeaponAudioSettings.Profile GetProfile(PlayerWeaponType weaponType) =>
            m_settings != null ? m_settings.GetProfile(weaponType) : null;

        private Voice GetVoice(PlayerWeaponType weaponType, WeaponAudioSettings.Profile profile)
        {
            if (!m_gunshotVoices.TryGetValue(weaponType, out List<Voice> voices))
            {
                voices = new List<Voice>(profile.MaximumGunshotVoices);
                m_gunshotVoices.Add(weaponType, voices);
            }

            foreach (Voice voice in voices)
            {
                if (AudioSettings.dspTime >= voice.AvailableAt)
                {
                    return voice;
                }
            }

            if (voices.Count < profile.MaximumGunshotVoices)
            {
                Voice voice = new() { Source = CreateSource($"{weaponType}Voice{voices.Count}") };
                voices.Add(voice);
                return voice;
            }

            // Keep the retained pool bounded without truncating a still-playing shot.
            // Overflow voices remain at their shot position and expire after the longest clip.
            AudioSource overflow = CreateSource($"{weaponType}Overflow");
            float lifetime = profile.PumpClip != null ? profile.PumpClip.length : 0f;
            foreach (AudioClip clip in profile.Gunshots)
                if (clip != null) lifetime = Mathf.Max(lifetime, clip.length / profile.MinimumPitch);
            Destroy(overflow.gameObject, lifetime + .1f);
            return new Voice { Source = overflow };
        }

        private AudioSource CreateSource(string sourceName)
        {
            // Unparented voices stay at the world position where the shot occurred.
            AudioSource source = Instantiate(m_settings.VoicePrefab);
            source.name = sourceName;
            source.playOnAwake = false;
            source.loop = false;
            source.dopplerLevel = 0f;
            return source;
        }

        private static void Configure(AudioSource source, WeaponAudioSettings.Profile profile,
            Vector3 worldPosition)
        {
            source.transform.position = worldPosition;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = profile.SpatialBlend;
            source.minDistance = profile.MinimumDistance;
            source.maxDistance = profile.MaximumDistance;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, SmoothRolloff);
        }
    }
}
