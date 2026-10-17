using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [DisallowMultipleComponent]
    public sealed class StreetlightPlaceholder : MonoBehaviour
    {
        [SerializeField] private Light m_light;

        public void Configure(bool enabled, Color colour, float intensity, float range)
        {
            if (m_light == null) m_light = GetComponentInChildren<Light>(true);
            if (m_light == null) return;
            m_light.enabled = enabled;
            m_light.color = colour;
            m_light.intensity = Mathf.Max(0f, intensity);
            m_light.range = Mathf.Max(0.1f, range);
        }
    }
}
