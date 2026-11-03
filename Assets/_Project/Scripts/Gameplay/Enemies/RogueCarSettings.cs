using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    [CreateAssetMenu(fileName = "RogueCarSettings", menuName = "Couch Guys/Enemies/Rogue Car Settings")]
    public sealed class RogueCarSettings : ScriptableObject
    {
        [Header("Movement")]
        [Min(0.1f)] public float MaxSpeed = 15f;
        [Min(0f)] public float Acceleration = 10f;
        [Min(0.1f)] public float ReverseSpeed = 5f;
        [Min(0f), Tooltip("Maximum yaw speed in degrees per second.")] public float TurnSpeed = 95f;
        [Min(0.1f)] public float MinTurningRadius = 4f;
        [Min(0f), Tooltip("Maximum yaw control acceleration. Larger chassis need more steering authority.")]
        public float AngularAcceleration = 8f;
        [Tooltip("Scale steering torque by the chassis inertia tensor. Enable for large compound vehicles.")]
        public bool CompensateSteeringInertia;
        [Min(0.05f)] public float TargetRefreshInterval = 0.5f;
        [Min(0.1f)] public float StuckDetectionTime = 2f;
        [Tooltip("Back toward close targets behind the vehicle instead of circling them. Useful for larger vehicles.")]
        public bool ReverseTowardsNearbyTargets;
        [Header("Forward Navigation Turnaround")]
        [Min(.1f)] public float TurnaroundForwardDuration = 2f;
        [Min(.1f)] public float TurnaroundReverseDuration = 1.4f;
        [Min(1f)] public float TurnaroundTimeout = 12f;

        [Header("Combat")]
        [Min(1f)] public float MaxHealth = 120f;
        [Min(0f)] public float PlayerCollisionDamage = 28f;
        [Min(0f)] public float PlayerKnockbackForce = 13f;
        [Min(0f), Tooltip("Damage per metre/second of impact normal speed.")]
        public float StructureCollisionDamageMultiplier = 5f;
        [Min(0f)] public float MinimumDamagingCollisionSpeed = 3.5f;

        [Header("Spawning")]
        [Min(0f)] public float InitialSpawnDelay = 45f;
        [Min(0.1f)] public float SpawnCheckInterval = 60f;
        [Range(0f, 1f)] public float SpawnChance = 0.15f;
        [Min(0)] public int MaxActiveCars = 1;
        [Min(0)] public int MaxSpawnsPerDelivery = 1;
    }
}
