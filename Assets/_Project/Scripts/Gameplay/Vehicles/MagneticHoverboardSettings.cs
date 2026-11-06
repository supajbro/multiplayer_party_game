using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    [CreateAssetMenu(menuName = "Couch Guys/Vehicles/Magnetic Hoverboard Settings")]
    public sealed class MagneticHoverboardSettings : ScriptableObject
    {
        [Header("Movement (metres / seconds)")]
        [Min(.1f)] public float MaximumForwardSpeed = 13f;
        [Min(.1f)] public float ReverseSpeed = 4f;
        [Min(.1f)] public float LoadedSpeed = 9f;
        [Min(.1f)] public float Acceleration = 9f;
        [Min(.1f)] public float Deceleration = 5f;
        [Min(.1f)] public float BrakingStrength = 16f;
        [Min(.1f)] public float TurningSpeed = 100f;
        [Min(.1f)] public float TurningResponsiveness = 7f;
        [Min(.1f)] public float LateralGrip = 7f;
        [Min(.1f)] public float InputTimeout = .35f;
        [Header("Hover")]
        [Min(.1f)] public float HoverHeight = .42f;
        [Min(.1f)] public float HoverSpringStrength = 65f;
        [Min(.1f)] public float HoverDamping = 13f;
        [Min(.1f)] public float GroundDetectionDistance = 2.8f;
        [Range(0,70)] public float MaximumSlopeAngle = 38f;
        [Min(.1f)] public float GroundAlignment = 7f;
        [Min(0)] public float BobbingAmplitude = .025f;
        [Min(0)] public float BobbingFrequency = 1.6f;
        [Range(0,25)] public float BankingAngle = 9f;
        [Min(.1f)] public float BankingSmoothing = 8f;
        public LayerMask GroundMask = ~0;
        [Header("Magnet")]
        [Min(.1f)] public float AttachmentRange = 1.7f;
        [Min(.1f)] public float LoadingPointRange = 1.5f;
        public Vector3 AttachmentPositionOffset = new(0f, .84f, -.85f);
        public Vector3 AttachmentRotationOffset = new(0,90,0);
        [Min(.1f)] public float AttachmentTransitionSpeed = 2f;
        [Min(.5f)] public float DetachmentDistance = 3.7f;
        [Min(.1f)] public float ArmAnimationSpeed = 3f;
        [Range(0,45)] public float ArmOpenAngle = 18f;
        [Min(0)] public float MagnetMaximumSpeed = .65f;
        [Header("Vehicle / collisions")]
        [Min(1)] public float Mass = 160f;
        [Min(0)] public float CollisionFriction = .04f;
        [Range(0,1)] public float CollisionRestitution = .03f;
        [Min(0)] public float CollisionBraking = .3f;
        [Header("Health and impact rules")]
        [Min(1)] public float MaximumHealth = 5000f;
        [Tooltip("Reuse the truck's collision thresholds, weapon scaling and character impact rules.")]
        public TruckSettings DamageSettings;
        [Min(0), Tooltip("Damage received relative to the truck. 0.5 takes half as much damage.")]
        public float DamageTakenMultiplier = .5f;

        public float BulletDamage(float rawDamage) => Mathf.Max(0f, rawDamage) *
            (DamageSettings != null ? DamageSettings.BulletDamageMultiplier : 1f) * DamageTakenMultiplier;

        public float CollisionDamage(float speed) => DamageSettings == null ? 0f :
            Mathf.Min(Mathf.Max(0f, speed - DamageSettings.MinimumCollisionDamageSpeed) *
                DamageSettings.CollisionDamageMultiplier, DamageSettings.MaximumCollisionDamage) * DamageTakenMultiplier;
        [Min(.1f)] public float MountRange = 1.8f;
        [Min(.1f)] public float InteractionCooldown = .4f;
        // Player root is below the soles in the existing robot rig.
        public Vector3 DriverPositionOffset = new(0,-.06f,1.02f);
        public Vector3 DriverRotationOffset = Vector3.zero;
        [Min(.5f)] public float DismountDistance = 1.8f;
        [Min(1)] public float CameraDistance = 6f;
        public float CameraHeight = 1.6f;
        [Min(.1f)] public float CameraSmoothing = 8f;
        [Min(.01f)] public float CameraSensitivity = .12f;
    }
}
