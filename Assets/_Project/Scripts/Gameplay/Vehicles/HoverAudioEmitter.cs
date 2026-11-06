using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    /// <summary>Plays one saved voice locally for each network instance, regardless of ownership.</summary>
    [DisallowMultipleComponent]
    public sealed class HoverAudioEmitter : MonoBehaviour
    {
        [Tooltip("Saved AudioSource at the hover ring or board. Configure the clip and 3D settings there.")]
        [SerializeField] private AudioSource m_source;
        [SerializeField] private NetworkObject m_networkObject;
        [Tooltip("Optional board whose existing destroyed/active state controls this voice.")]
        [SerializeField] private MagneticHoverboardController m_board;
        [Tooltip("Optional robot rider; reduces its hum when combined with a board engine.")]
        [SerializeField] private PlayerHoverboardInteractor m_rider;
        [SerializeField, Range(0f, 1f)] private float m_mountedVolumeMultiplier = 0.5f;

        private float m_volume;

        private void Awake()
        {
            if (m_source == null) return;
            m_volume = m_source.volume;
            // Network instances start after client initialization, including late joins.
            // Do not let Play On Awake sound during scene loading or on a dedicated server.
            m_source.Stop();
        }

        private void LateUpdate()
        {
            if (m_source == null) return;
            bool running = m_networkObject != null && m_networkObject.IsClientInitialized &&
                (m_board == null || (m_board.isActiveAndEnabled && !m_board.IsDestroyed));
            if (!running)
            {
                if (m_source.isPlaying) m_source.Stop();
                return;
            }

            float volume = m_volume * (m_rider != null && m_rider.IsMounted ? m_mountedVolumeMultiplier : 1f);
            m_source.volume = Mathf.MoveTowards(m_source.volume, volume, Time.unscaledDeltaTime * m_volume * 4f);
            if (m_source.isActiveAndEnabled && !m_source.isPlaying) m_source.Play();
        }

        private void OnDisable()
        {
            if (m_source != null) m_source.Stop();
        }
    }
}
