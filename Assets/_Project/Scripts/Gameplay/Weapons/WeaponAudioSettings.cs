using System;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [CreateAssetMenu(fileName = "WeaponAudioSettings", menuName = "Couch Guys/Weapons/Audio Settings")]
    public sealed class WeaponAudioSettings : ScriptableObject
    {
        public const string ResourcesPath = "WeaponAudio/WeaponAudioSettings";

        [Serializable]
        public sealed class Profile
        {
            [SerializeField] private PlayerWeaponType m_weaponType;
            [SerializeField] private AudioClip[] m_gunshots;
            [SerializeField, Range(0f, 1f)] private float m_gunshotVolume = 1f;
            [SerializeField, Min(0f)] private float m_minimumDistance = 3f;
            [SerializeField, Min(0.1f)] private float m_maximumDistance = 55f;
            [SerializeField, Range(0f, 1f)] private float m_spatialBlend = 1f;
            [SerializeField] private Vector2 m_pitchRange = new(0.97f, 1.03f);
            [SerializeField, Range(1, 16)] private int m_maximumGunshotVoices = 6;
            [SerializeField] private AudioClip m_firingStopClip;
            [SerializeField, Range(0f, 1f)] private float m_firingStopVolume = 0.8f;
            [SerializeField] private AudioClip m_pumpClip;
            [SerializeField, Range(0f, 1f)] private float m_pumpVolume = 0.85f;
            [SerializeField, Min(0f)] private float m_pumpDelay = 0.42f;

            public PlayerWeaponType WeaponType => m_weaponType;
            public AudioClip[] Gunshots => m_gunshots;
            public float GunshotVolume => m_gunshotVolume;
            public float MinimumDistance => m_minimumDistance;
            public float MaximumDistance => m_maximumDistance;
            public float SpatialBlend => m_spatialBlend;
            public float MinimumPitch => Mathf.Min(m_pitchRange.x, m_pitchRange.y);
            public float MaximumPitch => Mathf.Max(m_pitchRange.x, m_pitchRange.y);
            public int MaximumGunshotVoices => m_maximumGunshotVoices;
            public AudioClip FiringStopClip => m_firingStopClip;
            public float FiringStopVolume => m_firingStopVolume;
            public AudioClip PumpClip => m_pumpClip;
            public float PumpVolume => m_pumpVolume;
            public float PumpDelay => m_pumpDelay;

            public void Validate()
            {
                m_gunshotVolume = Mathf.Clamp01(m_gunshotVolume);
                m_minimumDistance = Mathf.Max(0f, m_minimumDistance);
                m_maximumDistance = Mathf.Max(m_minimumDistance + 0.1f, m_maximumDistance);
                m_spatialBlend = Mathf.Clamp01(m_spatialBlend);
                m_pitchRange.x = Mathf.Clamp(m_pitchRange.x, 0.5f, 2f);
                m_pitchRange.y = Mathf.Clamp(m_pitchRange.y, 0.5f, 2f);
                m_maximumGunshotVoices = Mathf.Clamp(m_maximumGunshotVoices, 1, 16);
                m_firingStopVolume = Mathf.Clamp01(m_firingStopVolume);
                m_pumpVolume = Mathf.Clamp01(m_pumpVolume);
                m_pumpDelay = Mathf.Max(0f, m_pumpDelay);
            }
        }

        [SerializeField] private Profile[] m_profiles = new Profile[3];

        public Profile GetProfile(PlayerWeaponType weaponType)
        {
            if (m_profiles == null)
            {
                return null;
            }

            for (int index = 0; index < m_profiles.Length; index++)
            {
                Profile profile = m_profiles[index];
                if (profile != null && profile.WeaponType == weaponType)
                {
                    return profile;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_profiles == null)
            {
                return;
            }

            foreach (Profile profile in m_profiles)
            {
                profile?.Validate();
            }
        }
#endif
    }
}
