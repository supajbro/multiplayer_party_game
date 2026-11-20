using UnityEngine;

namespace CouchGuys.UI
{
    [CreateAssetMenu(menuName = "Couch Guys/UI/Button Audio Settings")]
    public sealed class UIButtonAudioSettings : ScriptableObject
    {
        [SerializeField] private UIButtonAudio m_prefab;
        public UIButtonAudio Prefab => m_prefab;
    }
}
