using UnityEngine;
using UnityEngine.EventSystems;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuButtonFeedback : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        [SerializeField, Min(1f)] private float m_hoverScale = 1.045f;
        [SerializeField, Range(0.8f, 1f)] private float m_pressedScale = 0.965f;
        [SerializeField, Min(1f)] private float m_response = 18f;

        private bool m_hovered;
        private bool m_pressed;
        private Vector3 m_targetScale = Vector3.one;

        private void OnEnable()
        {
            transform.localScale = Vector3.one;
            RefreshTarget();
        }

        private void Update()
        {
            float blend = 1f - Mathf.Exp(-m_response * Time.unscaledDeltaTime);
            transform.localScale = Vector3.Lerp(transform.localScale, m_targetScale, blend);
        }

        public void OnPointerEnter(PointerEventData eventData) { m_hovered = true; RefreshTarget(); }
        public void OnPointerExit(PointerEventData eventData) { m_hovered = false; m_pressed = false; RefreshTarget(); }
        public void OnPointerDown(PointerEventData eventData) { m_pressed = true; RefreshTarget(); }
        public void OnPointerUp(PointerEventData eventData) { m_pressed = false; RefreshTarget(); }
        public void OnSelect(BaseEventData eventData) { m_hovered = true; RefreshTarget(); }
        public void OnDeselect(BaseEventData eventData) { m_hovered = false; m_pressed = false; RefreshTarget(); }

        private void RefreshTarget()
        {
            float scale = m_pressed ? m_pressedScale : (m_hovered ? m_hoverScale : 1f);
            m_targetScale = Vector3.one * scale;
        }
    }
}
