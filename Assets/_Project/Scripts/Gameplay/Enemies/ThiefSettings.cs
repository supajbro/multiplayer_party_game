using System;
using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Shared, authored configuration for one thief archetype; runtime state stays on instances.</summary>
    [CreateAssetMenu(fileName = "ThiefSettings", menuName = "Couch Guys/Enemies/Thief Settings")]
    public sealed class ThiefSettings : ScriptableObject
    {
        public MovementSettings Movement = new();
        public NavigationSettings Navigation = new();
        public AlertsSettings Alerts = new();
        public HealthSettings Health = new();
        public CombatSettings Combat = new();
        public AudioSettings Audio = new();
        public AnimationSettings Animation = new();

        [Serializable]
        public sealed class MovementSettings
        {
            [Min(0.1f), Tooltip("Maximum navigation speed in metres per second.")] public float Speed = 4.5f;
            [Min(0.1f)] public float Acceleration = 16f;
            [Min(1f)] public float AngularSpeed = 720f;
            [Min(0f)] public float StoppingDistance = 5f;
            public bool AutoBraking = true;
        }

        [Serializable]
        public sealed class NavigationSettings
        {
            [Min(0.05f)] public float PathRefreshInterval = 0.45f;
            [Min(0.1f)] public float TacticalRepositionInterval = 2.5f;
            [Min(0f)] public float RepositionIntervalVariation = 1f;
            [Min(0.25f)] public float TargetNavMeshSampleRadius = 6f;
            [Min(0.25f)] public float TacticalSampleRadius = 4f;
            [Min(0.1f)] public float TargetMovementThreshold = 2.5f;
            [Min(1)] public int TacticalPositionAttempts = 4;
            [Header("Formation")]
            [Min(0f)] public float PositionVariation = 1.25f;
            [Min(0f)] public float SupportRangeMultiplier = 1.45f;
            [Min(0f)] public float MinimumSlotSeparation = 1.75f;
            [Min(1f)] public float AimTurnSpeed = 260f;
        }

        [Serializable]
        public sealed class AlertsSettings
        {
            [Header("Proximity Alert")]
            [Range(0f, 100f), Tooltip("Percentage chance, rolled once per proximity encounter.")]
            public float AlertChance = 25f;
            [Min(0.1f)] public float AlertDistance = 14f;
            [Min(0.1f)] public float AlertResetDistance = 20f;
            [Min(0.1f)] public float AlertPauseDuration = 1.2f;
            [Min(0f)] public float AlertCooldown = 10f;
        }

        [Serializable]
        public sealed class HealthSettings
        {
            [Min(1f)] public float MaxHealth = 100f;
            [Min(0.05f)] public float KnockbackDuration = 0.22f;
            [Min(0f)] public float KnockbackDamping = 14f;
            [Min(0f)] public float DeathDespawnDelay = 2.5f;
            [Range(0f, 100f)] public float WeaponDropChance = 35f;
        }

        [Serializable]
        public sealed class CombatSettings
        {
            [Header("Weapon")] public EnemyWeaponType WeaponType = EnemyWeaponType.Pistol;
            [Min(0.05f)] public float FireInterval = 0.8f;
            [Min(1f)] public float Range = 30f;
            [Min(1)] public int PelletsPerShot = 1;
            [Min(0f)] public float SpreadAngle = 5f;
            [Min(0f)] public float DamagePerPellet = 20f;
            [Min(0f)] public float KnockbackForce = 5f;
            [Min(0f)] public float PreferredRange = 12f;
            public LayerMask HitMask = ~0;
            [Header("Accuracy and Pacing")]
            [Tooltip("Percentage chance per shot to aim visibly wide of the target. Existing spread still applies.")]
            [Range(0f, 100f)] public float MissChance = 40f;
            [Range(0f, 1f)] public float BaseAccuracy = 0.72f;
            [Range(0f, 1f)] public float MovingAccuracyPenalty = 0.2f;
            [Range(0f, 1f)] public float DistanceAccuracyPenalty = 0.35f;
            [Min(0f)] public float ReactionTime = 0.65f;
            [Min(1f)] public float AimRotationSpeed = 150f;
            [Min(1)] public int MinBurstShots = 1;
            [Min(1)] public int MaxBurstShots = 3;
            [Min(0f)] public float BurstCooldown = 1.1f;
            [Range(0f, 45f)] public float RequiredAimAngle = 16f;
            [Header("Bullet Visuals")]
            [Min(0.01f)] public float TracerDuration = 0.16f;
            [Min(0.005f)] public float TracerWidth = 0.025f;
            public Color TracerColour = new(1f, 0.72f, 0.12f, 1f);
        }

        [Serializable]
        public sealed class AudioSettings
        {
            public AudioClip FootstepLoop = null;
            public AudioClip AlertClip = null;
            [Range(0f, 1f)] public float FootstepVolume = 0.55f;
            [Range(0f, 1f)] public float AlertVolume = 1f;
            [Min(0.01f)] public float MovingThreshold = 0.12f;
            [Min(0.1f)] public float FootstepMinimumDistance = 2f;
            [Min(0.1f)] public float FootstepMaximumDistance = 20f;
            [Min(0.1f)] public float AlertMinimumDistance = 4f;
            [Min(0.1f)] public float AlertMaximumDistance = 30f;
        }

        [Serializable]
        public sealed class AnimationSettings
        {
            [Min(0.01f)] public float MovingThreshold = 0.08f;
            [Min(0f)] public float SpeedDamping = 0.08f;
        }

        private void OnValidate()
        {
            Movement.Speed = Mathf.Max(0.1f, Movement.Speed);
            Movement.Acceleration = Mathf.Max(0.1f, Movement.Acceleration);
            Movement.AngularSpeed = Mathf.Max(1f, Movement.AngularSpeed);
            Movement.StoppingDistance = Mathf.Max(0f, Movement.StoppingDistance);
            Navigation.PathRefreshInterval = Mathf.Max(0.05f, Navigation.PathRefreshInterval);
            Navigation.TacticalRepositionInterval = Mathf.Max(0.1f, Navigation.TacticalRepositionInterval);
            Navigation.RepositionIntervalVariation = Mathf.Clamp(Navigation.RepositionIntervalVariation,
                0f, Navigation.TacticalRepositionInterval * 0.8f);
            Navigation.TargetNavMeshSampleRadius = Mathf.Max(0.25f, Navigation.TargetNavMeshSampleRadius);
            Navigation.TacticalSampleRadius = Mathf.Max(0.25f, Navigation.TacticalSampleRadius);
            Navigation.TacticalPositionAttempts = Mathf.Max(1, Navigation.TacticalPositionAttempts);
            Alerts.AlertChance = Mathf.Clamp(Alerts.AlertChance, 0f, 100f);
            Alerts.AlertDistance = Mathf.Max(0.1f, Alerts.AlertDistance);
            Alerts.AlertResetDistance = Mathf.Max(Alerts.AlertDistance, Alerts.AlertResetDistance);
            Alerts.AlertPauseDuration = Mathf.Max(0.1f, Alerts.AlertPauseDuration);
            Alerts.AlertCooldown = Mathf.Max(0f, Alerts.AlertCooldown);
            Health.MaxHealth = Mathf.Max(1f, Health.MaxHealth);
            Health.KnockbackDuration = Mathf.Max(0.05f, Health.KnockbackDuration);
            Combat.MissChance = Mathf.Clamp(Combat.MissChance, 0f, 100f);
            Combat.FireInterval = Mathf.Max(0.05f, Combat.FireInterval);
            Combat.Range = Mathf.Max(1f, Combat.Range);
            Combat.PelletsPerShot = Mathf.Max(1, Combat.PelletsPerShot);
            Combat.MinBurstShots = Mathf.Max(1, Combat.MinBurstShots);
            Combat.MaxBurstShots = Mathf.Max(Combat.MinBurstShots, Combat.MaxBurstShots);
            Audio.FootstepMaximumDistance = Mathf.Max(Audio.FootstepMinimumDistance, Audio.FootstepMaximumDistance);
            Audio.AlertMaximumDistance = Mathf.Max(Audio.AlertMinimumDistance, Audio.AlertMaximumDistance);
        }
    }
}
