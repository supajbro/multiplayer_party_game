using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace CouchGuys.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuIntro : MonoBehaviour
    {
        [SerializeField] private CanvasGroup m_enterScreen;
        [SerializeField] private CanvasGroup m_menu;
        [SerializeField] private Animator m_menuAnimator;
        [SerializeField] private AnimationClip m_menuEntrance;
        [SerializeField] private Transform m_robotAnchor;
        [SerializeField] private Animator m_robotAnimator;
        [SerializeField] private Transform m_robotStart;
        [SerializeField] private Transform m_robotEnd;
        [SerializeField, Min(0f)] private float m_fadeDuration = 0.45f;
        [SerializeField, Min(0.01f)] private float m_robotEntranceDuration = 2.2f;
        [SerializeField, Min(0f)] private float m_robotTurnDuration = 0.3f;
        [SerializeField] private GameObject m_firstSelectedButton;

        private IDisposable m_inputSubscription;
        private bool m_waiting;

        private void Awake()
        {
            m_enterScreen.gameObject.SetActive(true);
            m_enterScreen.alpha = 1f;
            m_menu.alpha = 0f;
            m_menu.interactable = false;
            m_menu.blocksRaycasts = false;
            m_menuAnimator.enabled = false;
            m_robotAnchor.gameObject.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        private IEnumerator Start()
        {
            // Ignore the input used to enter this scene or start Play mode.
            yield return null;
            m_waiting = true;
            m_inputSubscription = InputSystem.onAnyButtonPress.Call(_ => Begin());
        }

        public void Begin()
        {
            if (!m_waiting) return;
            m_waiting = false;
            StartCoroutine(Reveal());
        }

        private IEnumerator Reveal()
        {
            EventSystem.current?.SetSelectedGameObject(null);
            for (float elapsed = 0f; elapsed < m_fadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                m_enterScreen.alpha = 1f - elapsed / m_fadeDuration;
                yield return null;
            }
            m_enterScreen.alpha = 0f;
            m_enterScreen.gameObject.SetActive(false);

            m_robotAnchor.SetPositionAndRotation(m_robotStart.position, m_robotStart.rotation);
            m_robotAnchor.gameObject.SetActive(true);
            m_robotAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            m_robotAnimator.Play("Walk", 0, 0f);
            float duration = Mathf.Max(0.01f, m_robotEntranceDuration);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                m_robotAnchor.position = Vector3.Lerp(m_robotStart.position, m_robotEnd.position, elapsed / duration);
                yield return null;
            }
            m_robotAnchor.position = m_robotEnd.position;
            m_robotAnimator.CrossFadeInFixedTime("Idle", 0.2f);
            Quaternion rotation = m_robotAnchor.rotation;
            for (float elapsed = 0f; elapsed < m_robotTurnDuration; elapsed += Time.unscaledDeltaTime)
            {
                m_robotAnchor.rotation = Quaternion.Slerp(rotation, m_robotEnd.rotation, elapsed / m_robotTurnDuration);
                yield return null;
            }
            m_robotAnchor.rotation = m_robotEnd.rotation;

            // Reveal the menu only after the robot has arrived and turned into its idle pose.
            m_menuAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            m_menuAnimator.enabled = true;
            m_menuAnimator.Play("Slide In", 0, 0f);
            m_menuAnimator.Update(0f);
            m_menu.alpha = 1f;
            yield return new WaitForSecondsRealtime(m_menuEntrance.length);

            // Finish on a fresh frame so the Enter input cannot activate a menu action.
            yield return null;
            m_menu.interactable = true;
            m_menu.blocksRaycasts = true;
            EventSystem.current?.SetSelectedGameObject(m_firstSelectedButton);
        }

        private void OnDestroy() => m_inputSubscription?.Dispose();
    }
}
