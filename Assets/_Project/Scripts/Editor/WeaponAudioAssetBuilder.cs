using System;
using CouchGuys.Gameplay.Weapons;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class WeaponAudioAssetBuilder
    {
        private const string ResourcesFolder = "Assets/_Project/Resources";
        private const string AudioSettingsFolder = ResourcesFolder + "/WeaponAudio";
        private const string SettingsPath = AudioSettingsFolder + "/WeaponAudioSettings.asset";

        private static readonly string[] PistolClips =
        {
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/Handgun/Gunshots/sfx_wpn_hgn_gunshot_01.wav",
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/Handgun/Gunshots/sfx_wpn_hgn_gunshot_02.wav",
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/Handgun/Gunshots/sfx_wpn_hgn_gunshot_03.wav"
        };

        private static readonly string[] RifleClips =
        {
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/AssaultRifle/Gunshots/sfx_wpn_ar_gunshot_01.wav",
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/AssaultRifle/Gunshots/sfx_wpn_ar_gunshot_02.wav",
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/AssaultRifle/Gunshots/sfx_wpn_ar_gunshot_03.wav"
        };

        private static readonly string[] ShotgunClips =
        {
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/Shotgun/Gunshots/sfx_wpn_sgn_gunshot_01.wav",
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/Shotgun/Gunshots/sfx_wpn_sgn_gunshot_02.wav",
            "Assets/ThirdParty/FreeWeaponSFX_v1.1/Shotgun/Gunshots/sfx_wpn_sgn_gunshot_03.wav"
        };

        [MenuItem("Couch Guys/Weapons/Build Audio Settings")]
        public static void Build()
        {
            EnsureFolder(ResourcesFolder, "WeaponAudio");
            WeaponAudioSettings settings = AssetDatabase.LoadAssetAtPath<WeaponAudioSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<WeaponAudioSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            SerializedObject serialised = new(settings);
            SerializedProperty profiles = serialised.FindProperty("m_profiles");
            profiles.arraySize = 5;
            serialised.FindProperty("m_voicePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioSource>("Assets/_Project/Prefabs/Weapons/WeaponAudioVoice.prefab");
            ConfigureProfile(profiles.GetArrayElementAtIndex(0), PlayerWeaponType.Pistol,
                PistolClips, 1f, 3f, 35f, 1f, 0.97f, 1.03f, 5, null, 0f, null, 0f, 0f);
            ConfigureProfile(profiles.GetArrayElementAtIndex(1), PlayerWeaponType.AssaultRifle,
                RifleClips, 0.9f, 4f, 35f, 1f, 0.97f, 1.03f, 8,
                "Assets/_Project/Audio/AssaultRifle/CouchGuys_AR_FiringStop_MechanicalClack.wav",
                0.8f, null, 0f, 0f);
            ConfigureProfile(profiles.GetArrayElementAtIndex(2), PlayerWeaponType.Shotgun,
                ShotgunClips, 1f, 5f, 35f, 1f, 0.97f, 1.03f, 4, null, 0f,
                "Assets/_Project/Audio/Shotgun/CouchGuys_Shotgun_Pump_Separate.wav", 0.85f, 0.42f);
            string[] grappleClips = Array.ConvertAll(
                AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Audio/GrappleGun" }),
                AssetDatabase.GUIDToAssetPath);
            Array.Sort(grappleClips, StringComparer.Ordinal);
            ConfigureProfile(profiles.GetArrayElementAtIndex(3), PlayerWeaponType.GrappleGun,
                grappleClips, 1f, 3f, 35f, 1f, 0.97f, 1.03f, 6, null, 0f, null, 0f, 0f);
            ConfigureProfile(profiles.GetArrayElementAtIndex(4), PlayerWeaponType.RocketLauncher,
                new[] { "Assets/ThirdParty/FreeWeaponSFX_v1.1/GrenadeLauncher/sfx_wpn_gl_fire.wav" },
                1f, 3f, 35f, 1f, 0.97f, 1.03f, 6, null, 0f, null, 0f, 0f);
            profiles.GetArrayElementAtIndex(4).FindPropertyRelative("m_explosionClip").objectReferenceValue =
                LoadRequired("Assets/ThirdParty/FreeWeaponSFX_v1.1/GrenadeLauncher/sfx_wpn_gl_explosion.wav");
            profiles.GetArrayElementAtIndex(4).FindPropertyRelative("m_explosionVolume").floatValue = 1f;
            serialised.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Built weapon audio settings at {SettingsPath}.");
        }

        private static void ConfigureProfile(SerializedProperty profile, PlayerWeaponType weaponType,
            string[] gunshotPaths, float volume, float minimumDistance, float maximumDistance,
            float spatialBlend, float minimumPitch, float maximumPitch, int maximumVoices,
            string stopPath, float stopVolume, string pumpPath, float pumpVolume, float pumpDelay)
        {
            profile.FindPropertyRelative("m_weaponType").enumValueIndex = (int)weaponType;
            SerializedProperty gunshots = profile.FindPropertyRelative("m_gunshots");
            gunshots.arraySize = gunshotPaths.Length;
            for (int index = 0; index < gunshotPaths.Length; index++)
            {
                gunshots.GetArrayElementAtIndex(index).objectReferenceValue = LoadRequired(gunshotPaths[index]);
            }

            profile.FindPropertyRelative("m_gunshotVolume").floatValue = volume;
            profile.FindPropertyRelative("m_minimumDistance").floatValue = minimumDistance;
            profile.FindPropertyRelative("m_maximumDistance").floatValue = maximumDistance;
            profile.FindPropertyRelative("m_spatialBlend").floatValue = spatialBlend;
            profile.FindPropertyRelative("m_pitchRange").vector2Value = new Vector2(minimumPitch, maximumPitch);
            profile.FindPropertyRelative("m_maximumGunshotVoices").intValue = maximumVoices;
            profile.FindPropertyRelative("m_firingStopClip").objectReferenceValue = LoadOptional(stopPath);
            profile.FindPropertyRelative("m_firingStopVolume").floatValue = stopVolume;
            profile.FindPropertyRelative("m_pumpClip").objectReferenceValue = LoadOptional(pumpPath);
            profile.FindPropertyRelative("m_pumpVolume").floatValue = pumpVolume;
            profile.FindPropertyRelative("m_pumpDelay").floatValue = pumpDelay;
        }

        private static AudioClip LoadRequired(string path)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                throw new InvalidOperationException($"Required weapon audio clip is missing: {path}");
            }
            return clip;
        }

        private static AudioClip LoadOptional(string path) =>
            string.IsNullOrEmpty(path) ? null : LoadRequired(path);

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
