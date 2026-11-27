using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using CouchGuys.Player;

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

        [Header("Customise")]
        [SerializeField] private Camera m_menuCamera;
        [SerializeField] private CanvasGroup m_customiseArrows;
        [SerializeField] private RectTransform m_settingsButton;
        [SerializeField] private Vector2 m_settingsExitOffset = new Vector2(0f, 160f);
        [SerializeField] private Vector3 m_playerFocusOffset = new Vector3(0f, 2.2f, 0f);
        [SerializeField, Range(10f, 90f)] private float m_customiseFieldOfView = 42f;
        [SerializeField, Min(0.01f)] private float m_cameraTurnDuration = 1.14f;
        [SerializeField, Min(0f)] private float m_arrowFadeDuration = 0.3f;
        [SerializeField] private PlayerCharacterCatalog m_characterCatalog;
        [SerializeField] private Button m_previousCharacterButton;
        [SerializeField] private Button m_nextCharacterButton;

        private IDisposable m_inputSubscription;
        private bool m_waiting;
        private bool m_menuReady;
        private bool m_customising;
        private bool m_transitioning;
        private Quaternion m_originalCameraRotation;
        private float m_originalFieldOfView;
        private Vector2 m_settingsPosition;
        private GameObject m_originalRobot;
        private Animator m_originalRobotAnimator;
        private GameObject m_characterPreview;

        private void Awake()
        {
            m_enterScreen.gameObject.SetActive(true);
            m_enterScreen.alpha = 1f;
            m_menu.alpha = 0f;
            m_menu.interactable = false;
            m_menu.blocksRaycasts = false;
            m_menuAnimator.enabled = false;
            m_robotAnchor.gameObject.SetActive(false);
            m_originalRobotAnimator = m_robotAnimator;
            m_originalRobot = m_robotAnimator != null ? m_robotAnimator.gameObject : null;
            m_previousCharacterButton?.onClick.AddListener(SelectPreviousCharacter);
            m_nextCharacterButton?.onClick.AddListener(SelectNextCharacter);
            ShowSelectedCharacter();
            if (m_customiseArrows != null)
            {
                m_customiseArrows.alpha = 0f;
                m_customiseArrows.interactable = false;
                m_customiseArrows.blocksRaycasts = false;
            }
            if (m_settingsButton != null) m_settingsPosition = m_settingsButton.anchoredPosition;
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
            if (m_robotAnimator != null)
            {
                m_robotAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
                m_robotAnimator.Play("Walk", 0, 0f);
            }
            float duration = Mathf.Max(0.01f, m_robotEntranceDuration);
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                m_robotAnchor.position = Vector3.Lerp(m_robotStart.position, m_robotEnd.position, elapsed / duration);
                yield return null;
            }
            m_robotAnchor.position = m_robotEnd.position;
            if (m_robotAnimator != null) m_robotAnimator.CrossFadeInFixedTime("Idle", 0.2f);
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
            m_menuReady = true;
            EventSystem.current?.SetSelectedGameObject(m_firstSelectedButton);
        }

        private void Update()
        {
            if (m_customising && !m_transitioning &&
                ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) ||
                 (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false)))
                StartCoroutine(TransitionCustomise(false));
        }

        public void OpenCustomise()
        {
            if (!m_menuReady || m_customising || m_transitioning || m_menuCamera == null) return;
            m_originalCameraRotation = m_menuCamera.transform.rotation;
            m_originalFieldOfView = m_menuCamera.fieldOfView;
            StartCoroutine(TransitionCustomise(true));
        }

        internal bool TryBeginMultiplayer()
        {
            if (!m_menuReady || m_customising || m_transitioning) return false;
            m_transitioning = true;
            m_menu.interactable = false;
            m_menu.blocksRaycasts = false;
            EventSystem.current?.SetSelectedGameObject(null);
            return true;
        }

        internal IEnumerator SlideMainMenu(bool visible)
        {
            float duration = m_menuEntrance.length;
            m_menuAnimator.speed = 0f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float progress = elapsed / duration;
                SampleMainMenu(visible ? progress : 1f - progress);
                yield return null;
            }
            SampleMainMenu(visible ? 1f : 0f);
        }

        internal void FinishMultiplayer()
        {
            m_menuAnimator.speed = 1f;
            m_menu.interactable = true;
            m_menu.blocksRaycasts = true;
            m_transitioning = false;
            EventSystem.current?.SetSelectedGameObject(m_firstSelectedButton);
        }

        public void SelectPreviousCharacter() => ChangeCharacter(-1);
        public void SelectNextCharacter() => ChangeCharacter(1);

        private void ChangeCharacter(int direction)
        {
            if (!m_customising || m_transitioning || m_characterCatalog == null || m_characterCatalog.Count < 2) return;
            m_characterCatalog.Select(m_characterCatalog.SelectedIndex + direction);
            ShowSelectedCharacter();
        }

        private void ShowSelectedCharacter()
        {
            if (m_characterCatalog == null || m_characterCatalog.Count == 0) return;
            if (m_characterPreview != null)
            {
                m_characterPreview.SetActive(false);
                Destroy(m_characterPreview);
            }
            var character = m_characterCatalog[m_characterCatalog.SelectedIndex];
            bool original = m_characterCatalog.SelectedIndex == 0;
            if (m_originalRobot != null) m_originalRobot.SetActive(original);
            m_robotAnimator = original ? m_originalRobotAnimator : null;
            if (!original && character.MenuVisualPrefab != null)
            {
                m_characterPreview = Instantiate(character.MenuVisualPrefab, m_robotAnchor, false);
                m_robotAnimator = m_characterPreview.GetComponentInChildren<Animator>(true);
            }
        }

        private void SetArrowInput(bool enabled)
        {
            if (m_customiseArrows == null) return;
            bool available = enabled && m_characterCatalog != null && m_characterCatalog.Count > 1;
            // The main menu remains blocked while this independent group accepts input.
            m_customiseArrows.ignoreParentGroups = true;
            m_customiseArrows.interactable = available;
            m_customiseArrows.blocksRaycasts = available;
        }

        private IEnumerator TransitionCustomise(bool opening)
        {
            m_transitioning = true;
            m_customising = opening;
            m_menu.interactable = false;
            m_menu.blocksRaycasts = false;
            EventSystem.current?.SetSelectedGameObject(null);
            SetArrowInput(false);

            if (!opening) yield return FadeArrows(false);

            Quaternion startRotation = m_menuCamera.transform.rotation;
            Quaternion endRotation = opening
                ? Quaternion.LookRotation(m_robotAnchor.position + m_playerFocusOffset - m_menuCamera.transform.position, Vector3.up)
                : m_originalCameraRotation;
            float startFieldOfView = m_menuCamera.fieldOfView;
            float endFieldOfView = opening ? m_customiseFieldOfView : m_originalFieldOfView;
            float menuDuration = Mathf.Max(0.01f, m_menuEntrance.length);
            float cameraDuration = Mathf.Max(0.01f, m_cameraTurnDuration);
            float duration = Mathf.Max(menuDuration, cameraDuration);

            // Sample the saved entrance state backwards so the same curves and stagger
            // drive the exit, including any future Inspector edits to the clip.
            m_menuAnimator.speed = 0f;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                ApplyTransition(elapsed / menuDuration, elapsed / cameraDuration);
                yield return null;
            }
            ApplyTransition(1f, 1f);
            // Hold the reversed clip at its hidden pose while customising.
            m_menuAnimator.speed = opening ? 0f : 1f;

            if (opening) yield return FadeArrows(true);
            else
            {
                m_menu.interactable = true;
                m_menu.blocksRaycasts = true;
                EventSystem.current?.SetSelectedGameObject(m_firstSelectedButton);
            }
            m_transitioning = false;
            if (opening)
            {
                SetArrowInput(true);
                EventSystem.current?.SetSelectedGameObject(m_nextCharacterButton?.gameObject);
            }

            void ApplyTransition(float menuProgress, float cameraProgress)
            {
                float menuTime = opening ? 1f - Mathf.Clamp01(menuProgress) : Mathf.Clamp01(menuProgress);
                SampleMainMenu(menuTime);
                float cameraBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(cameraProgress));
                m_menuCamera.transform.rotation = Quaternion.Slerp(startRotation, endRotation, cameraBlend);
                m_menuCamera.fieldOfView = Mathf.Lerp(startFieldOfView, endFieldOfView, cameraBlend);
            }
        }

        private void SampleMainMenu(float time)
        {
            m_menuAnimator.Play("Slide In", 0, time);
            m_menuAnimator.Update(0f);
            // The settings cog is outside the entrance clip; slide it through the top edge.
            if (m_settingsButton != null)
                m_settingsButton.anchoredPosition = m_settingsPosition +
                    m_settingsExitOffset * Mathf.SmoothStep(1f, 0f, time);
        }

        private IEnumerator FadeArrows(bool visible)
        {
            if (m_customiseArrows == null) yield break;
            float start = m_customiseArrows.alpha;
            float end = visible ? 1f : 0f;
            for (float elapsed = 0f; elapsed < m_arrowFadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                m_customiseArrows.alpha = Mathf.Lerp(start, end, elapsed / m_arrowFadeDuration);
                yield return null;
            }
            m_customiseArrows.alpha = end;
        }

        private void OnDestroy()
        {
            m_inputSubscription?.Dispose();
            m_previousCharacterButton?.onClick.RemoveListener(SelectPreviousCharacter);
            m_nextCharacterButton?.onClick.RemoveListener(SelectNextCharacter);
        }
    }
}
