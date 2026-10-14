using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Small, shared configuration surface for generated suburban lawns.</summary>
    [CreateAssetMenu(menuName = "Couch Guys/World/Suburbs Grass Settings", fileName = "SuburbsGrassSettings")]
    public sealed class SuburbsGrassSettings : ScriptableObject
    {
        [Header("Lawn Appearance")]
        [SerializeField] private Color m_baseGrassColour = new Color32(0x55, 0xB9, 0x6A, 0xFF);
        [SerializeField] private Color m_lightStripeColour = new Color32(0x73, 0xCC, 0x79, 0xFF);
        [SerializeField] private Color m_darkStripeColour = new Color32(0x42, 0x9A, 0x55, 0xFF);
        [SerializeField, Min(0.5f)] private float m_stripeWidth = 3.5f;
        [SerializeField, Range(0f, 1f)] private float m_stripeContrast = 0.42f;
        [SerializeField, Range(0f, 1f)] private float m_grassDetailStrength = 0.32f;

        [Header("Wind")]
        [SerializeField] private bool m_windEnabled = true;
        [SerializeField, Range(0f, 1f)] private float m_windStrength = 0.22f;
        [SerializeField, Min(0f)] private float m_windSpeed = 0.65f;

        [Header("Grass Tufts")]
        [SerializeField] private bool m_enableGrassTufts = true;
        [Tooltip("Average decorative tufts per square metre. Tufts are batched into one mesh.")]
        [SerializeField, Range(0f, 0.2f)] private float m_grassTuftDensity = 0.045f;
        [SerializeField, Min(5f)] private float m_grassTuftDrawDistance = 42f;

        public Color BaseGrassColour => m_baseGrassColour;
        public Color LightStripeColour => m_lightStripeColour;
        public Color DarkStripeColour => m_darkStripeColour;
        public float StripeWidth => m_stripeWidth;
        public float StripeContrast => m_stripeContrast;
        public float GrassDetailStrength => m_grassDetailStrength;
        public bool WindEnabled => m_windEnabled;
        public float WindStrength => m_windStrength;
        public float WindSpeed => m_windSpeed;
        public bool EnableGrassTufts => m_enableGrassTufts;
        public float GrassTuftDensity => m_grassTuftDensity;
        public float GrassTuftDrawDistance => m_grassTuftDrawDistance;

        private void OnValidate()
        {
            m_stripeWidth = Mathf.Max(0.5f, m_stripeWidth);
            m_stripeContrast = Mathf.Clamp01(m_stripeContrast);
            m_grassDetailStrength = Mathf.Clamp01(m_grassDetailStrength);
            m_windStrength = Mathf.Clamp01(m_windStrength);
            m_windSpeed = Mathf.Max(0f, m_windSpeed);
            m_grassTuftDensity = Mathf.Clamp(m_grassTuftDensity, 0f, 0.2f);
            m_grassTuftDrawDistance = Mathf.Max(5f, m_grassTuftDrawDistance);
        }
    }
}
