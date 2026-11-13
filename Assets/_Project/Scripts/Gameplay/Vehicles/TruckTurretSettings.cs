using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    [CreateAssetMenu(menuName = "Couch Guys/Vehicles/Truck Turret Settings")]
    public sealed class TruckTurretSettings : ScriptableObject
    {
        [Min(0f)] public float Damage = 18f;
        [Min(.05f)] public float FireInterval = .18f;
        [Min(1f)] public float ProjectileSpeed = 65f;
        [Min(.1f)] public float Lifetime = 2.5f;
        [Min(.01f)] public float CollisionRadius = .07f;
        [Min(0f)] public float Knockback = 3f;
        [Range(0f, 180f)] public float YawLimit = 180f;
        [Range(-85f, 0f)] public float MinimumPitch = -18f;
        [Range(0f, 85f)] public float MaximumPitch = 65f;
        [Min(1f)] public float TurnSpeed = 180f;
        public LayerMask HitMask = ~0;
        public HoverTruckBolt ProjectilePrefab;
        public GameObject ImpactPrefab;
    }
}
