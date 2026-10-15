using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    [CreateAssetMenu(fileName = "CarSettings", menuName = "Couch Guys/Traffic/Car Settings")]
    public sealed class CarSettings : ScriptableObject
    {
        [Header("Driving")]
        [SerializeField, Min(1f)] private float m_maximumHealth = 120f;
        [SerializeField, Min(1f), Tooltip("Metres per second.")] private float m_maximumDrivingSpeed = 13f;
        [SerializeField, Min(0f)] private float m_acceleration = 8f;
        [SerializeField, Min(0f)] private float m_brakingStrength = 18f;
        [SerializeField, Min(0f), Tooltip("Braking deceleration used specifically when a player is detected ahead, in metres per second squared.")]
        private float m_playerBrakingStrength = 18f;
        [SerializeField, Min(1f)] private float m_turningSpeed = 95f;
        [SerializeField, Min(1f)] private float m_safeFollowingDistance = 9f;

        [Header("Impact")]
        [SerializeField, Min(0f)] private float m_collisionDamage = 28f;
        [SerializeField, Min(0f)] private float m_collisionKnockbackStrength = 13f;
        [SerializeField, Min(0f)] private float m_minimumCollisionSpeed = 3.5f;
        [SerializeField, Min(0.05f)] private float m_collisionDamageCooldown = 0.65f;

        [Header("Explosion")]
        [SerializeField, Min(0f)] private float m_explosionDamage = 38f;
        [SerializeField, Min(0f)] private float m_explosionKnockback = 18f;
        [SerializeField, Min(0f)] private float m_explosionRadius = 6f;

        public float MaximumHealth => m_maximumHealth;
        public float MaximumDrivingSpeed => m_maximumDrivingSpeed;
        public float Acceleration => m_acceleration;
        public float BrakingStrength => m_brakingStrength;
        public float PlayerBrakingStrength => m_playerBrakingStrength;
        public float TurningSpeed => m_turningSpeed;
        public float SafeFollowingDistance => m_safeFollowingDistance;
        public float CollisionDamage => m_collisionDamage;
        public float CollisionKnockbackStrength => m_collisionKnockbackStrength;
        public float MinimumCollisionSpeed => m_minimumCollisionSpeed;
        public float CollisionDamageCooldown => m_collisionDamageCooldown;
        public float ExplosionDamage => m_explosionDamage;
        public float ExplosionKnockback => m_explosionKnockback;
        public float ExplosionRadius => m_explosionRadius;

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_maximumHealth = Mathf.Max(1f, m_maximumHealth);
            m_maximumDrivingSpeed = Mathf.Max(1f, m_maximumDrivingSpeed);
            m_playerBrakingStrength = Mathf.Max(0f, m_playerBrakingStrength);
            m_collisionDamageCooldown = Mathf.Max(0.05f, m_collisionDamageCooldown);
            m_explosionRadius = Mathf.Max(0f, m_explosionRadius);
        }
#endif
    }
}
