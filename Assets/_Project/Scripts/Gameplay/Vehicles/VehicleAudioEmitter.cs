using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    /// <summary>Local, reusable voices. Network events are owned by the vehicle controllers.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleAudioEmitter : MonoBehaviour
    {
        [SerializeField] private AudioClip m_engineClip;
        [SerializeField] private AudioClip m_splatClip;
        [SerializeField] private AudioClip m_hornClip;
        [SerializeField, Range(0f, 1f)] private float m_engineVolume = 0.65f;
        [SerializeField, Min(0.1f)] private float m_minDistance = 3f;
        [SerializeField, Min(0.1f)] private float m_maxDistance = 30f;

        private AudioSource m_engine;
        private AudioSource m_horn;
        private readonly AudioSource[] m_impacts = new AudioSource[4];
        private readonly Vector3[] m_impactPositions = new Vector3[4];
        private int m_nextImpact;

        public void SetEngineRunning(bool running)
        {
            if (!running)
            {
                if (m_engine != null) m_engine.Stop();
                return;
            }
            if (m_engineClip == null) return;
            if (m_engine == null)
            {
                m_engine = CreateSource("Engine");
                m_engine.clip = m_engineClip;
                m_engine.loop = true;
                m_engine.volume = m_engineVolume;
            }
            if (!m_engine.isPlaying) m_engine.Play();
        }

        public void PlayHorn()
        {
            if (m_hornClip == null) return;
            if (m_horn == null) m_horn = CreateSource("Horn");
            m_horn.Stop();
            m_horn.PlayOneShot(m_hornClip);
        }

        public void PlaySplat(Vector3 position)
        {
            if (m_splatClip == null) return;
            int index = m_nextImpact;
            m_nextImpact = (m_nextImpact + 1) % m_impacts.Length;
            if (m_impacts[index] == null) m_impacts[index] = CreateSource("Splat");
            AudioSource source = m_impacts[index];
            source.Stop();
            m_impactPositions[index] = position;
            source.transform.position = position;
            source.PlayOneShot(m_splatClip);
        }

        private void LateUpdate()
        {
            // Impacts stay at the contact point even while their owning car moves.
            for (int i = 0; i < m_impacts.Length; i++)
                if (m_impacts[i] != null && m_impacts[i].isPlaying)
                    m_impacts[i].transform.position = m_impactPositions[i];
        }

        public void StopLocal()
        {
            SetEngineRunning(false);
            if (m_horn != null) m_horn.Stop();
            for (int i = 0; i < m_impacts.Length; i++)
                if (m_impacts[i] != null) m_impacts[i].Stop();
        }

        private void OnDisable() => StopLocal();

        private AudioSource CreateSource(string sourceName)
        {
            var voice = new GameObject(sourceName);
            voice.transform.SetParent(transform, false);
            AudioSource source = voice.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = m_minDistance;
            source.maxDistance = Mathf.Max(m_minDistance, m_maxDistance);
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
