using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class CouchDispatchUIFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField, Range(1f, 1.15f)] private float m_hoverScale = 1.035f;
        [SerializeField, Range(0.85f, 1f)] private float m_pressedScale = 0.96f;
        [SerializeField, Min(1f)] private float m_speed = 18f;

        private RectTransform m_rect;
        private Coroutine m_animation;
        private bool m_hovered;

        private void Awake() => m_rect = transform as RectTransform;
        public void OnPointerEnter(PointerEventData eventData) { m_hovered = true; AnimateTo(m_hoverScale); }
        public void OnPointerExit(PointerEventData eventData) { m_hovered = false; AnimateTo(1f); }
        public void OnPointerDown(PointerEventData eventData) => AnimateTo(m_pressedScale);
        public void OnPointerUp(PointerEventData eventData) => AnimateTo(m_hovered ? m_hoverScale : 1f);
        public void OnSelect(BaseEventData eventData) => AnimateTo(m_hoverScale);
        public void OnDeselect(BaseEventData eventData) => AnimateTo(1f);

        private void AnimateTo(float scale)
        {
            if (!isActiveAndEnabled || m_rect == null) return;
            if (m_animation != null) StopCoroutine(m_animation);
            m_animation = StartCoroutine(AnimateScale(scale));
        }

        private IEnumerator AnimateScale(float target)
        {
            Vector3 destination = Vector3.one * target;
            while ((m_rect.localScale - destination).sqrMagnitude > 0.00001f)
            {
                m_rect.localScale = Vector3.Lerp(m_rect.localScale, destination,
                    1f - Mathf.Exp(-m_speed * Time.unscaledDeltaTime));
                yield return null;
            }
            m_rect.localScale = destination;
            m_animation = null;
        }
    }
}
