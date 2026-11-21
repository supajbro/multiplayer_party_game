using UnityEngine;

namespace CouchGuys.Gameplay.Pedestrians
{
    [CreateAssetMenu(fileName = "NpcDeathAudioSettings", menuName = "Couch Guys/Audio/NPC Death Settings")]
    public sealed class NpcDeathAudioSettings : ScriptableObject
    {
        public const string ResourcesPath = "Audio/NpcDeathAudioSettings";

        [SerializeField] private AudioSource m_voicePrefab;
        [SerializeField] private AudioClip m_clip;
        [SerializeField, Range(0f, 1f)] private float m_volume = 1f;
        [SerializeField, Min(.1f)] private float m_minimumDistance = 1f;
        [Tooltip("Death audio fades to silence at this distance in metres.")]
        [SerializeField, Min(.2f)] private float m_maximumDistance = 25f;

        // Match the existing weapon voices' smooth attenuation, including silence at max range.
        private static readonly AnimationCurve Rolloff = new(
            new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));

        /// <summary>Called only by the unbuffered death RPC on each observing client, including host.</summary>
        public static void Play(Vector3 worldPosition)
        {
            var settings = Resources.Load<NpcDeathAudioSettings>(ResourcesPath);
            if (settings == null || settings.m_voicePrefab == null || settings.m_clip == null) return;

            // Reuse the saved audio voice prefab, independently of the character's lifetime.
            AudioSource source = Instantiate(settings.m_voicePrefab, worldPosition, Quaternion.identity);
            source.name = "NpcDeathVoice";
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.maxDistance = Mathf.Max(.2f, settings.m_maximumDistance);
            source.minDistance = Mathf.Clamp(settings.m_minimumDistance, .1f, source.maxDistance - .1f);
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            source.pitch = Random.Range(.9f, 1.1f);
            source.PlayOneShot(settings.m_clip, Mathf.Clamp01(settings.m_volume));
            Destroy(source.gameObject, settings.m_clip.length / source.pitch + .1f);
        }
    }
}
