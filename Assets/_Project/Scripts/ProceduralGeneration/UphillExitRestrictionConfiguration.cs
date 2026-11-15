using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [CreateAssetMenu(menuName = "Couch Guys/World/Uphill Exit Restriction")]
    public sealed class UphillExitRestrictionConfiguration : ScriptableObject
    {
        [SerializeField] private UphillExitRestriction m_prefab;
        public UphillExitRestriction Prefab => m_prefab;

        private static UphillExitRestrictionConfiguration s_instance;
        public static UphillExitRestrictionConfiguration Load()
        {
            if (s_instance == null)
                s_instance = Resources.Load<UphillExitRestrictionConfiguration>("World/UphillExitRestriction");
            return s_instance;
        }
    }
}
