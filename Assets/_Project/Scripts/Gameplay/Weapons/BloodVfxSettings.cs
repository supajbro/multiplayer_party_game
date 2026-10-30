using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [CreateAssetMenu(menuName = "Couch Guys/Blood VFX Settings")]
    public sealed class BloodVfxSettings : ScriptableObject
    {
        [SerializeField] private BloodBurst m_prefab;
        public BloodBurst Prefab => m_prefab;
    }
}
