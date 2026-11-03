using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [CreateAssetMenu(menuName = "Couch Guys/Weapons/Rocket Launcher Assets")]
    public sealed class RocketLauncherAssets : ScriptableObject
    {
        [SerializeField] private GameObject m_equippedPrefab;
        [SerializeField] private GameObject m_pickupPrefab;
        public GameObject EquippedPrefab => m_equippedPrefab;
        public GameObject PickupPrefab => m_pickupPrefab;
    }
}
