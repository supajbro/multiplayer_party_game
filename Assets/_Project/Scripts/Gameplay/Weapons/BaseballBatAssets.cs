using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [CreateAssetMenu(menuName = "Couch Guys/Weapons/Baseball Bat")]
    public sealed class BaseballBatAssets : ScriptableObject
    {
        public const float Cooldown = 2f;
        public const float ActiveStart = .2f;
        public const float ActiveEnd = .4f;
        public const float SwingDuration = .85f;
        public const string ResourcesPath = "Weapons/BaseballBatAssets";

        [SerializeField] private GameObject m_equippedPrefab;
        [SerializeField] private GameObject m_pickupPrefab;
        [SerializeField, Min(0f)] private float m_damage = 5f;
        [SerializeField, Min(.1f)] private float m_range = 1.8f;
        [SerializeField, Range(.02f, .5f)] private float m_sweepRadius = .18f;
        [SerializeField, Min(0f)] private float m_playerKnockback = 24f;
        [SerializeField, Min(0f)] private float m_npcKnockback = 28f;
        [SerializeField, Min(0f)] private float m_vehicleKnockback = 17f;
        [SerializeField, Min(0f)] private float m_playerLift = 9f;
        [SerializeField, Min(0f)] private float m_npcLift = 11f;
        [SerializeField, Min(0f)] private float m_vehicleLift = 9f;
        [SerializeField, Min(0f)] private float m_vehicleSpin = 4f;
        public GameObject EquippedPrefab => m_equippedPrefab;
        public GameObject PickupPrefab => m_pickupPrefab;
        public float Damage => m_damage;
        public float Range => m_range;
        public float SweepRadius => m_sweepRadius;
        public float PlayerKnockback => m_playerKnockback;
        public float NpcKnockback => m_npcKnockback;
        public float VehicleKnockback => m_vehicleKnockback;
        public float PlayerLift => m_playerLift;
        public float NpcLift => m_npcLift;
        public float VehicleLift => m_vehicleLift;
        public float VehicleSpin => m_vehicleSpin;
    }
}
