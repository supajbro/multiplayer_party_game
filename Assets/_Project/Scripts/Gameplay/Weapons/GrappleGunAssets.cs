using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [CreateAssetMenu(menuName = "Couch Guys/Weapons/Grapple Gun Assets")]
    public sealed class GrappleGunAssets : ScriptableObject
    {
        [SerializeField] private GameObject m_equippedPrefab;
        [SerializeField] private GameObject m_pickupPrefab;
        public GameObject EquippedPrefab => m_equippedPrefab;
        public GameObject PickupPrefab => m_pickupPrefab;
    }
}
