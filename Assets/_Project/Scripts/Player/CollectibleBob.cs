using UnityEngine;

namespace CouchGuys.Player
{
    [DisallowMultipleComponent]
    public sealed class CollectibleBob : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float m_amplitude = .19f;
        [SerializeField, Min(0f)] private float m_frequencyHz = .85f;

        private Vector3 m_restPosition;
        private float m_startTime;

        private void OnEnable()
        {
            m_restPosition = transform.localPosition;
            m_startTime = Time.time;
        }

        private void Update()
        {
            float phase = (Time.time - m_startTime) * m_frequencyHz * (2f * Mathf.PI);
            transform.localPosition = m_restPosition + Vector3.up * (m_amplitude * Mathf.Sin(phase));
        }

        private void OnDisable() => transform.localPosition = m_restPosition;
    }
}
