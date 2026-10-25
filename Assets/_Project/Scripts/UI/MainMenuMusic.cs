using System.Collections;
using UnityEngine;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuMusic : MonoBehaviour
    {
        [SerializeField] private AudioClip m_clip;
        [SerializeField, Range(0f, 1f)] private float m_volume = 0.5f;
        [SerializeField, Min(0.01f)] private float m_fadeDuration = 2f;

        private static MainMenuMusic s_instance;
        private AudioSource m_source;
        private Coroutine m_fade;

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                // Returning to the menu during a fade keeps the existing track.
                s_instance.Resume();
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
            m_source = gameObject.AddComponent<AudioSource>();
            m_source.playOnAwake = false;
            m_source.clip = m_clip;
            m_source.loop = true;
            m_source.spatialBlend = 0f;
            Resume();
        }

        private void Resume()
        {
            if (m_fade != null) StopCoroutine(m_fade);
            m_fade = null;
            m_source.volume = m_volume;
            if (!m_source.isPlaying) m_source.Play();
        }

        public static void FadeOutForGameplay()
        {
            if (s_instance != null && s_instance.m_fade == null)
                s_instance.m_fade = s_instance.StartCoroutine(s_instance.FadeOut());
        }

        private IEnumerator FadeOut()
        {
            // Let the loading overlay disappear before lowering the volume.
            yield return null;
            float startVolume = m_source.volume;
            float duration = Mathf.Max(0.01f, m_fadeDuration);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                m_source.volume = startVolume * (1f - Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }
            m_source.volume = 0f;
            m_source.Stop();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }
    }
}
