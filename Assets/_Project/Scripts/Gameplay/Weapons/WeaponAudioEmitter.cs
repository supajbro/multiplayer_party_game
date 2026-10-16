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
        private readonly int[] m_lastVariation = { -1, -1, -1 };
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

        public void PlayShot(PlayerWeaponType weaponType, Vector3 worldPosition, Transform spatialOrigin = null)
        {
            WeaponAudioSettings.Profile profile = GetProfile(weaponType);
            AudioClip[] clips = profile?.Gunshots;
            if (clips == null || clips.Length == 0)
            {
                return;
            }

            if (weaponType == PlayerWeaponType.AssaultRifle && m_rifleStopSource != null)
            {
                m_rifleStopSource.Stop();
            }

            int variation = SelectVariation(weaponType, clips);
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
            if (profile?.FiringStopClip == null)
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

        private int SelectVariation(PlayerWeaponType weaponType, AudioClip[] clips)
        {
            int weaponIndex = (int)weaponType;
            int selected = Random.Range(0, clips.Length);
            if (clips.Length > 1 && selected == m_lastVariation[weaponIndex])
            {
                selected = (selected + Random.Range(1, clips.Length)) % clips.Length;
            }

            m_lastVariation[weaponIndex] = selected;
            return selected;
        }

        private Voice GetVoice(PlayerWeaponType weaponType, WeaponAudioSettings.Profile profile)
        {
            if (!m_gunshotVoices.TryGetValue(weaponType, out List<Voice> voices))
            {
                voices = new List<Voice>(profile.MaximumGunshotVoices);
                m_gunshotVoices.Add(weaponType, voices);
            }

            Voice oldest = null;
            foreach (Voice voice in voices)
            {
                if (AudioSettings.dspTime >= voice.AvailableAt)
                {
                    return voice;
                }

                if (oldest == null || voice.AvailableAt < oldest.AvailableAt)
                {
                    oldest = voice;
                }
            }

            if (voices.Count < profile.MaximumGunshotVoices)
            {
                Voice voice = new() { Source = CreateSource($"{weaponType}Voice{voices.Count}") };
                voices.Add(voice);
                return voice;
            }

            oldest.Source.Stop();
            return oldest;
        }

        private AudioSource CreateSource(string sourceName)
        {
            GameObject sourceObject = new(sourceName);
            sourceObject.transform.SetParent(transform, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.dopplerLevel = 0f;
            return source;
        }

        private static void Configure(AudioSource source, WeaponAudioSettings.Profile profile,
            Vector3 worldPosition)
        {
            source.transform.position = worldPosition;
            source.spatialBlend = profile.SpatialBlend;
            source.minDistance = profile.MinimumDistance;
            source.maxDistance = profile.MaximumDistance;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, SmoothRolloff);
        }
    }
}
