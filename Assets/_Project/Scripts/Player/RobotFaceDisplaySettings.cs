using UnityEngine;

namespace CouchGuys.Player
{
    [CreateAssetMenu(fileName = "RobotFaceDisplaySettings", menuName = "Couch Guys/Robot Face Display Settings")]
    public sealed class RobotFaceDisplaySettings : ScriptableObject
    {
        [SerializeField] private Material m_material;
        [SerializeField, ColorUsage(true, true)] private Color m_cyan = new(0.04f, 0.85f, 1f, 1f);
        [SerializeField, Range(0f, 8f)] private float m_brightness = 2.6f;
        [SerializeField, Range(0f, 1f)] private float m_scanlineIntensity = 0.16f;
        [SerializeField, Range(32f, 256f)] private float m_pixelDensity = 128f;
        [SerializeField, Range(0.1f, 3f)] private float m_animationSpeed = 1f;
        [SerializeField, Range(0f, 1f)] private float m_glitchIntensity = 0.45f;
        [SerializeField, Range(0f, 0.1f)] private float m_curvature = 0.035f;
        [SerializeField, Range(0f, 0.1f)] private float m_staticIntensity = 0.0015f;
        [SerializeField, Range(0.01f, 1f)] private float m_lowHealthFraction = 0.3f;
        [SerializeField, Min(0.02f)] private float m_transitionDuration = 0.16f;
        [SerializeField] private Vector2 m_blinkInterval = new(2.4f, 5.6f);
        [SerializeField, Range(0.05f, 0.3f)] private float m_blinkDuration = 0.12f;
        [Tooltip("FBX front-face UV island: (minimum U, minimum V, width, height).")]
        [SerializeField] private Vector4 m_screenUVRect = new(0.38040865f, 0.7526041f, 0.23918271f, 0.24479163f);
        [Tooltip("The CouchGuy screen UV island is rotated: U points up, V points left.")]
        [SerializeField] private bool m_rotateScreenUV = true;

        public Material Material => m_material;
        public Color Cyan => m_cyan;
        public float Brightness => m_brightness;
        public float ScanlineIntensity => m_scanlineIntensity;
        public float PixelDensity => m_pixelDensity;
        public float AnimationSpeed => Mathf.Max(0.1f, m_animationSpeed);
        public float GlitchIntensity => m_glitchIntensity;
        public float Curvature => m_curvature;
        public float StaticIntensity => m_staticIntensity;
        public float LowHealthFraction => m_lowHealthFraction;
        public float TransitionDuration => Mathf.Max(0.02f, m_transitionDuration);
        public Vector2 BlinkInterval => m_blinkInterval;
        public float BlinkDuration => Mathf.Max(0.05f, m_blinkDuration);
        public Vector4 ScreenUVRect => m_screenUVRect;
        public bool RotateScreenUV => m_rotateScreenUV;
    }
}
