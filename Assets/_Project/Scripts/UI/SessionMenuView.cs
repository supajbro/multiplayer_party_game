using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace CouchGuys.UI
{
    /// <summary>Inspector-authored menu; the controller only binds actions and updates text.</summary>
    public sealed class SessionMenuView : MonoBehaviour
    {
        public Button PrimaryButton;
        public Button ExitButton;
        public Text ProgressLabel;
        public Text LocalReadyLabel;
        private readonly List<EventSystem> m_suspendedEvents = new();

        private void OnEnable()
        {
            EventSystem own = GetComponentInChildren<EventSystem>(true);
            foreach (var events in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
            {
                if (events == own || !events.enabled) continue;
                m_suspendedEvents.Add(events);
                events.enabled = false;
            }
        }

        private void OnDisable()
        {
            foreach (var events in m_suspendedEvents)
                if (events != null) events.enabled = true;
            m_suspendedEvents.Clear();
        }
    }
}
