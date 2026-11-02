using TMPro;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Presentation only; the complete editable hierarchy lives in its saved prefab.</summary>
    public sealed class DispatcherBubbleView : MonoBehaviour
    {
        [SerializeField] private TMP_Text m_label;
        [SerializeField] private CanvasGroup m_group;
        [SerializeField] private RectTransform m_content;
        [SerializeField, Min(0f), Tooltip("Minimum bubble height relative to a 1080-pixel viewport. Zero disables distance compensation.")]
        private float m_minimumScreenHeight = 135f;
        [SerializeField, Min(1f)] private float m_maximumScaleMultiplier = 3f;
        private Vector3 m_authoredScale;
        private Vector3 m_authoredRootScale;
        private float m_worldHeight;

        private void Awake()
        {
            m_authoredScale = m_content.localScale;
            m_authoredRootScale = transform.localScale;
            m_worldHeight = Mathf.Max(.001f, ((RectTransform)transform).rect.height * transform.lossyScale.y);
        }

        public void Show(string line)
        {
            m_label.text = line;
            SetProgress(0f);
            gameObject.SetActive(true);
        }

        public void SetProgress(float visibility)
        {
            visibility = Mathf.SmoothStep(0f, 1f, visibility);
            m_group.alpha = visibility;
            m_content.localScale = m_authoredScale * Mathf.Lerp(0.92f, 1f, visibility);
        }

        public void Face(Camera camera)
        {
            transform.rotation = camera.transform.rotation;
            float depth = Mathf.Max(0f, Vector3.Dot(transform.position - camera.transform.position, camera.transform.forward));
            float viewHeight = camera.orthographic ? camera.orthographicSize * 2f :
                2f * depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float multiplier = Mathf.Clamp(viewHeight * m_minimumScreenHeight / (1080f * m_worldHeight),
                1f, Mathf.Max(1f, m_maximumScaleMultiplier));
            transform.localScale = m_authoredRootScale * multiplier;
        }
        public void Hide() => gameObject.SetActive(false);
    }
}
