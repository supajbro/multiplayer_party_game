using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CouchGuys.UI
{
    /// <summary>Local button feedback, shared by every scene and runtime-created uGUI button.</summary>
    [DefaultExecutionOrder(-1001)] // Refresh before EventSystem (-1000) processes pointer/submit events.
    [DisallowMultipleComponent]
    public sealed class UIButtonAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource m_source;
        [SerializeField] private AudioClip m_click;
        [SerializeField, Range(0f, 1f)] private float m_clickVolume = 1f;
        [Tooltip("Random pitch offset around normal speed for each click. Zero disables variation.")]
        [SerializeField, Range(0f, 0.1f)] private float m_pitchVariation = 0.04f;

        [Header("Delivery Results")]
        [SerializeField] private AudioSource m_deliverySource;
        [SerializeField] private AudioClip m_deliverySuccess;
        [SerializeField] private AudioClip m_deliveryFailure;
        [SerializeField, Range(0f, 1f)] private float m_deliveryVolume = 1f;

        [Header("Dispatcher and Shop Interaction")]
        [SerializeField] private AudioClip m_interaction;
        [SerializeField, Range(0f, 1f)] private float m_interactionVolume = 1f;

        private static UIButtonAudio s_instance;
        private Selectable[] m_selectables = new Selectable[64];
        private UnityAction m_playClick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialise()
        {
            if (s_instance != null) return;
            var settings = Resources.Load<UIButtonAudioSettings>("UI/UIButtonAudioSettings");
            if (settings == null || settings.Prefab == null)
            {
                Debug.LogError("Missing UI/UIButtonAudioSettings or its button audio prefab.");
                return;
            }

            // Instantiate the saved, Inspector-configurable audio service; no per-button components.
            Instantiate(settings.Prefab);
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
            m_playClick = PlayClick;
            m_source.playOnAwake = false;
            m_source.spatialBlend = 0f;
            m_source.dopplerLevel = 0f;
            m_source.ignoreListenerPause = true;
            // Keep ignoreListenerVolume false: Master volume is controlled by AudioListener.volume.
            m_source.ignoreListenerVolume = false;
            if (m_deliverySource != null)
            {
                m_deliverySource.playOnAwake = false;
                m_deliverySource.spatialBlend = 0f;
                m_deliverySource.dopplerLevel = 0f;
                m_deliverySource.ignoreListenerPause = true;
                m_deliverySource.ignoreListenerVolume = false;
            }
        }

        public static void PlayDeliveryResult(bool success)
        {
            if (s_instance == null) Initialise();
            if (s_instance == null || s_instance.m_deliverySource == null) return;
            AudioClip clip = success ? s_instance.m_deliverySuccess : s_instance.m_deliveryFailure;
            // One-shots overlap, including several deliveries resolved in the same network tick.
            // A separate UI source keeps button pitch variation from affecting result sounds.
            if (clip != null) s_instance.m_deliverySource.PlayOneShot(clip, s_instance.m_deliveryVolume);
        }

        public static void PlayInteraction()
        {
            if (s_instance == null) Initialise();
            if (s_instance == null || s_instance.m_deliverySource == null || s_instance.m_interaction == null) return;
            // Reuse the normal-pitch UI source so button click pitch does not affect this sound.
            s_instance.m_deliverySource.PlayOneShot(s_instance.m_interaction, s_instance.m_interactionVolume);
        }

        private void Update()
        {
            int required = Selectable.allSelectableCount;
            if (required > m_selectables.Length)
                Array.Resize(ref m_selectables, Mathf.NextPowerOfTwo(required));

            int count = Selectable.AllSelectablesNoAlloc(m_selectables);
            for (int i = 0; i < count; i++)
            {
                if (!(m_selectables[i] is Button button)) continue;
                // Rebind to survive pooled cards calling RemoveAllListeners or replacing onClick.
                // Remove only our callback, preserving all gameplay listeners and their order.
                button.onClick.RemoveListener(m_playClick);
                button.onClick.AddListener(m_playClick);
            }
            Array.Clear(m_selectables, 0, count);
        }

        private void PlayClick()
        {
            // Button itself gates onClick on active/interactable state, for both pointer and submit.
            // Do not recheck here: an earlier gameplay listener can close or disable the clicked UI.
            if (m_source != null && m_click != null)
            {
                float variation = Mathf.Clamp(m_pitchVariation, 0f, 0.1f);
                m_source.pitch = UnityEngine.Random.Range(1f - variation, 1f + variation);
                m_source.PlayOneShot(m_click, m_clickVolume);
            }
        }

        private void OnDestroy()
        {
            if (s_instance != this) return;
            int count = Selectable.AllSelectablesNoAlloc(m_selectables);
            for (int i = 0; i < count; i++)
                if (m_selectables[i] is Button button) button.onClick.RemoveListener(m_playClick);
            s_instance = null;
        }
    }
}
