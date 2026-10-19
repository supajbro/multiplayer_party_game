using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    public enum VanQuality { Light, Good, Best }

    /// <summary>Single source of truth for a van's configurable behaviour. Runtime state is never stored here.</summary>
    [CreateAssetMenu(fileName = "VanSettings", menuName = "Couch Guys/Vehicles/Van Settings")]
    public sealed class VanSettings : ScriptableObject
    {
        [Header("General")]
        [SerializeField] private VanQuality m_quality;
        [SerializeField] private string m_displayName = "Van";

        [Header("Driving")]
        [SerializeField, Min(1f), Tooltip("Maximum forward speed in kilometres per hour.")] private float m_maxForwardSpeed = 65f;
        [SerializeField, Min(1f), Tooltip("Maximum reverse speed in kilometres per hour.")] private float m_maxReverseSpeed = 22f;
        [SerializeField, Min(0f), Tooltip("Forward acceleration in metres per second squared.")] private float m_acceleration = 8f;
        [SerializeField, Min(0f)] private float m_reverseAcceleration = 5f;
        [SerializeField, Min(0f), Tooltip("Braking deceleration in metres per second squared.")] private float m_brakingForce = 16f;
        [SerializeField, Min(0f)] private float m_naturalDeceleration = 1.2f;
        [SerializeField, Min(0f), Tooltip("Maximum yaw rate in degrees per second.")] private float m_steeringSensitivity = 90f;
        [SerializeField, Min(0f)] private float m_steeringResponseSpeed = 5f;
        [SerializeField, Range(0.05f, 1f)] private float m_highSpeedSteeringMultiplier = 0.4f;
        [SerializeField, Min(1f)] private float m_vehicleMass = 1450f;
        [SerializeField, Range(0f, 1f), Tooltip("Lateral tyre grip. Lower values produce more sliding.")] private float m_traction = 0.78f;
        [SerializeField, Range(0f, 1f)] private float m_uphillPowerMultiplier = 0.65f;
        [SerializeField, Min(0f)] private float m_antiRollStrength = 9f;

        [Header("Fuel")]
        [SerializeField, Min(0.01f)] private float m_maxFuel = 90f;
        [SerializeField, Min(0f), Tooltip("Fuel units consumed per second while the occupied engine is idling.")] private float m_fuelConsumptionIdle = 0.03f;
        [SerializeField, Min(0f), Tooltip("Fuel units consumed per second at full driving speed.")] private float m_fuelConsumptionDriving = 0.25f;
        [SerializeField, Min(0f), Tooltip("Extra fuel units per second at full acceleration input.")] private float m_accelerationFuelMultiplier = 0.3f;
        [SerializeField, Range(0f, 1f), Tooltip("Fuel fraction at which the warning appears.")] private float m_lowFuelWarningThreshold = 0.2f;

        [Header("Health And Damage")]
        [SerializeField, Min(1f)] private float m_maxHealth = 175f;
        [SerializeField, Min(0f), Tooltip("Minimum speed into a solid obstacle in m/s before the van takes damage.")] private float m_minimumCollisionDamageSpeed = 4f;
        [SerializeField, Min(0f), Tooltip("Van damage per m/s above the obstacle collision threshold.")] private float m_collisionDamageMultiplier = 4f;
        [SerializeField, Min(0f)] private float m_maximumCollisionDamage = 45f;
        [SerializeField, Min(0f), Tooltip("Minimum time before the same collision can deal damage again.")] private float m_collisionDamageCooldown = 0.4f;
        [SerializeField, Min(0f), Tooltip("Minimum horizontal van impact speed in m/s before a character takes damage. Slower contact only pushes.")] private float m_minimumCharacterImpactSpeed = 2.5f;
        [SerializeField, Min(0f), Tooltip("Character damage per m/s above the impact threshold.")] private float m_characterImpactDamageMultiplier = 6f;
        [SerializeField, Min(0f), Tooltip("Horizontal knockback strength at a full-speed character impact.")] private float m_characterKnockbackForce = 12f;
        [SerializeField, Min(0f), Tooltip("Upward knockback applied to a character hit by the van.")] private float m_characterKnockbackUpwardForce = 2.5f;
        [SerializeField, Min(0f)] private float m_bulletDamageMultiplier = 1f;
        [SerializeField, Range(0f, 1f)] private float m_criticalHealthThreshold = 0.25f;

        [Header("Interactions")]
        [SerializeField, Min(0f)] private float m_enterHoldDuration = 0.65f;
        [SerializeField, Min(0f)] private float m_exitHoldDuration = 0.45f;
        [SerializeField, Min(0.1f)] private float m_interactionRange = 2.8f;
        [SerializeField, Min(0.1f)] private float m_exitClearanceRadius = 0.45f;
        [SerializeField, Min(0f)] private float m_interactionCooldown = 0.6f;

        [Header("Rear Doors")]
        [SerializeField, Min(0.1f), Tooltip("Maximum player distance from the imported rear-door interaction marker.")] private float m_doorInteractionRange = 2.4f;
        [SerializeField, Range(45f, 135f), Tooltip("Outward swing angle for both rear door hinges.")] private float m_doorOpenAngle = 110f;
        [SerializeField, Min(0.05f), Tooltip("Seconds for the rear doors to fully open or close.")] private float m_doorAnimationDuration = 0.55f;
        [SerializeField, Tooltip("When disabled, engine power is blocked until the rear doors are closed.")] private bool m_allowDrivingWithDoorsOpen;

        [Header("Couch Loading")]
        [SerializeField, Min(1), Tooltip("Maximum couches secured in this van at once.")] private int m_cargoCapacity = 1;
        [SerializeField, Min(0.1f), Tooltip("Maximum couch and player distance from CargoLoadingAnchor.")] private float m_couchLoadingRange = 2.5f;
        [SerializeField, Min(0.05f)] private float m_couchLoadingDuration = 0.8f;
        [SerializeField, Min(0.05f)] private float m_couchUnloadingDuration = 0.65f;
        [SerializeField, Tooltip("Align loaded couches to the prefab's CargoPoint transforms.")] private bool m_autoPositionLoadedCouches = true;

        [Header("Camera")]
        [SerializeField, Min(0.1f)] private float m_cameraDistance = 6.5f;
        [SerializeField] private float m_cameraHeight = 2.4f;
        [SerializeField, Min(0f)] private float m_cameraLookSensitivity = 0.12f;
        [SerializeField, Range(-89f, 0f)] private float m_cameraMinimumPitch = -35f;
        [SerializeField, Range(0f, 89f)] private float m_cameraMaximumPitch = 65f;
        [SerializeField, Min(0.01f)] private float m_cameraFollowSmoothness = 9f;
        [SerializeField, Min(0.01f)] private float m_cameraRotationSmoothness = 7f;
        [SerializeField, Range(30f, 110f)] private float m_cameraFieldOfView = 70f;
        [SerializeField, Min(0.01f)] private float m_cameraCollisionRadius = 0.3f;

        [Header("Visual Behaviour")]
        [SerializeField, Min(0f)] private float m_wheelRotationMultiplier = 1f;
        [SerializeField, Range(0f, 60f)] private float m_maxWheelSteeringAngle = 28f;
        [SerializeField, Range(0f, 20f)] private float m_bodyTiltAmount = 5f;
        [SerializeField, Range(0f, 20f)] private float m_bodyPitchAmount = 3f;
        [SerializeField, Min(0.01f)] private float m_visualSmoothing = 8f;

        public VanQuality Quality => m_quality; public string DisplayName => m_displayName;
        public float MaxForwardSpeed => m_maxForwardSpeed; public float MaxReverseSpeed => m_maxReverseSpeed;
        public float Acceleration => m_acceleration; public float ReverseAcceleration => m_reverseAcceleration;
        public float BrakingForce => m_brakingForce; public float NaturalDeceleration => m_naturalDeceleration;
        public float SteeringSensitivity => m_steeringSensitivity; public float SteeringResponseSpeed => m_steeringResponseSpeed;
        public float HighSpeedSteeringMultiplier => m_highSpeedSteeringMultiplier; public float VehicleMass => m_vehicleMass;
        public float Traction => m_traction; public float UphillPowerMultiplier => m_uphillPowerMultiplier; public float AntiRollStrength => m_antiRollStrength;
        public float MaxFuel => m_maxFuel; public float FuelConsumptionIdle => m_fuelConsumptionIdle;
        public float FuelConsumptionDriving => m_fuelConsumptionDriving; public float AccelerationFuelMultiplier => m_accelerationFuelMultiplier;
        public float LowFuelWarningThreshold => m_lowFuelWarningThreshold; public float MaxHealth => m_maxHealth;
        public float MinimumCollisionDamageSpeed => m_minimumCollisionDamageSpeed; public float CollisionDamageMultiplier => m_collisionDamageMultiplier;
        public float MaximumCollisionDamage => m_maximumCollisionDamage; public float CollisionDamageCooldown => m_collisionDamageCooldown;
        public float MinimumCharacterImpactSpeed => m_minimumCharacterImpactSpeed;
        public float CharacterImpactDamageMultiplier => m_characterImpactDamageMultiplier;
        public float CharacterKnockbackForce => m_characterKnockbackForce;
        public float CharacterKnockbackUpwardForce => m_characterKnockbackUpwardForce;
        public float BulletDamageMultiplier => m_bulletDamageMultiplier; public float CriticalHealthThreshold => m_criticalHealthThreshold;
        public float EnterHoldDuration => m_enterHoldDuration; public float ExitHoldDuration => m_exitHoldDuration;
        public float InteractionRange => m_interactionRange; public float ExitClearanceRadius => m_exitClearanceRadius;
        public float InteractionCooldown => m_interactionCooldown;
        public float DoorInteractionRange => m_doorInteractionRange; public float DoorOpenAngle => m_doorOpenAngle;
        public float DoorAnimationDuration => m_doorAnimationDuration; public bool AllowDrivingWithDoorsOpen => m_allowDrivingWithDoorsOpen;
        public int CargoCapacity => m_cargoCapacity; public float CouchLoadingRange => m_couchLoadingRange;
        public float CouchLoadingDuration => m_couchLoadingDuration; public float CouchUnloadingDuration => m_couchUnloadingDuration;
        public bool AutoPositionLoadedCouches => m_autoPositionLoadedCouches;
        public float CameraDistance => m_cameraDistance; public float CameraHeight => m_cameraHeight;
        public float CameraLookSensitivity => m_cameraLookSensitivity;
        public float CameraMinimumPitch => m_cameraMinimumPitch; public float CameraMaximumPitch => m_cameraMaximumPitch;
        public float CameraFollowSmoothness => m_cameraFollowSmoothness; public float CameraRotationSmoothness => m_cameraRotationSmoothness;
        public float CameraFieldOfView => m_cameraFieldOfView; public float CameraCollisionRadius => m_cameraCollisionRadius;
        public float WheelRotationMultiplier => m_wheelRotationMultiplier; public float MaxWheelSteeringAngle => m_maxWheelSteeringAngle;
        public float BodyTiltAmount => m_bodyTiltAmount; public float BodyPitchAmount => m_bodyPitchAmount; public float VisualSmoothing => m_visualSmoothing;
    }
}
