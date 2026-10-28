using UnityEngine;

namespace CouchGuys.UI
{
    [CreateAssetMenu(menuName = "Couch Guys/UI/Loading Screen Settings")]
    public sealed class LoadingScreenSettings : ScriptableObject
    {
        [SerializeField] private LoadingScreenController m_prefab;

        public LoadingScreenController Prefab => m_prefab;
    }
}
