using System;
using CouchGuys.Gameplay.Weapons;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Player
{
    [CreateAssetMenu(menuName = "Couch Guys/AI/Wheelie")]
    public sealed class WheelieSettings : ScriptableObject
    {
        public const int FixedSquadSize = 4;
        [Serializable]
        public struct StartingWeapon
        {
            public PlayerWeaponType Weapon;
            [Min(0f)] public float Probability;
        }

        [Header("Saved Assets")]
        public NetworkObject Prefab;
        [SerializeField, Range(4, 4)] private int m_squadSize = FixedSquadSize;
        public int SquadSize => Mathf.Clamp(m_squadSize, FixedSquadSize, FixedSquadSize);
        [Header("Health and Movement")]
        [Min(1f)] public float MaximumHealth = 80f;
        [Min(.1f)] public float MovementSpeed = 5.5f;
        [Min(.1f)] public float Acceleration = 14f;
        [Min(.1f)] public float Deceleration = 20f;
        [Min(1f)] public float TurningSpeed = 400f;
        [Min(.1f)] public float AgentRadius = .29f;
        [Min(.5f)] public float AgentHeight = 1.15f;
        [Header("Formation and Navigation")]
        [Min(.5f)] public float FollowingDistance = 2.75f;
        [Min(.1f)] public float SquadMemberSpacing = 1.2f;
        [Min(.05f)] public float DestinationUpdateInterval = .25f;
        [Min(.05f)] public float ArrivalTolerance = .35f;
        [Min(.1f)] public float NavigationSampleRadius = 2f;
        [Min(.1f)] public float MaximumNavigationHeightDifference = 1.5f;
        [Min(.1f)] public float SpawnRadius = 2.75f;
        [Min(.1f)] public float SpawnSearchRadius = 9f;
        [Min(4)] public int SpawnPositionAttempts = 96;
        [Min(.01f)] public float GroundClearance = .06f;
        [Header("Combat")]
        [Min(1f)] public float EnemyDetectionRange = 24f;
        [Min(1f)] public float MaximumEngagementDistance = 18f;
        [Min(1f)] public float MaximumDistanceFromLeader = 24f;
        [Min(.05f)] public float EnemyScanInterval = .35f;
        [Range(0f, 1f)] public float CombatAccuracy = .78f;
        [Min(0f)] public float MaximumAimErrorDegrees = 7f;
        [Min(0f)] public float ReactionTime = .35f;
        [Range(1f, 90f)] public float AimAlignmentDegrees = 20f;
        [Min(0f)] public float DamageMultiplier = .65f;
        [Min(.05f)] public float FireRateMultiplier = .85f;
        [Min(.05f)] public float ReloadSpeedMultiplier = 1f;
        [Range(.1f, 1f)] public float PreferredWeaponRangeFraction = .65f;
        [Min(0f)] public float ThreatPriorityBonus = 30f;
        [Min(.1f)] public float HostileAimHeight = .9f;
        public bool UnlimitedReserveAmmo = true;
        public StartingWeapon[] StartingWeapons = {
            new StartingWeapon { Weapon = PlayerWeaponType.Pistol, Probability = .35f },
            new StartingWeapon { Weapon = PlayerWeaponType.AssaultRifle, Probability = .45f },
            new StartingWeapon { Weapon = PlayerWeaponType.Shotgun, Probability = .20f }
        };
        [Header("Existing weapon definitions and effects")]
        [SerializeField] private WeaponDefinition[] m_weapons = WeaponDefinition.CreateDefaults();
        [SerializeField, HideInInspector] private bool m_weaponDefinitionsConfigured;
        public bool HasConfiguredWeaponDefinitions => m_weaponDefinitionsConfigured;
        public WeaponDefinition GetWeapon(int index) => m_weapons != null && index >= 0 && index < m_weapons.Length ? m_weapons[index] : null;
        [Header("Replication and Lifetime")]
        [Min(.05f)] public float CombatStateInterval = .1f;
        [Min(1f)] public float CorpseLifetime = 15f;
        [Min(.01f)] public float ImpulseDamping = 10f;
        [Header("Presentation")]
        [Min(.01f)] public float WheelRadius = .2025f;
        [Min(0f)] public float AccelerationLean = .6f;
        [Min(0f)] public float TurningLean = .035f;
        [Range(0f, 20f)] public float MaximumLean = 8f;
        [Min(.1f)] public float PoseBlendSpeed = 9f;
        [Range(0f, 1f)] public float HeadAimWeight = .45f;
        [Range(0f, 30f)] public float RecoilAngle = 5f;
        [Min(.1f)] public float RecoilRecovery = 14f;

        public static bool IsStartingWeapon(PlayerWeaponType weapon) =>
            weapon == PlayerWeaponType.Pistol || weapon == PlayerWeaponType.AssaultRifle || weapon == PlayerWeaponType.Shotgun;

        public bool TrySelectWeapon(float sample, out PlayerWeaponType weapon)
        {
            weapon = PlayerWeaponType.Pistol;
            float total = 0f;
            if (StartingWeapons == null) return false;
            foreach (var choice in StartingWeapons)
                if (IsStartingWeapon(choice.Weapon) && float.IsFinite(choice.Probability)) total += Mathf.Max(0f, choice.Probability);
            if (total <= 0f) return false;
            float remaining = Mathf.Clamp01(sample) * total;
            foreach (var choice in StartingWeapons)
            {
                if (!IsStartingWeapon(choice.Weapon) || !float.IsFinite(choice.Probability) || choice.Probability <= 0f) continue;
                weapon = choice.Weapon;
                remaining -= choice.Probability;
                if (remaining < 0f) return true;
            }
            return true;
        }

        private void OnValidate()
        {
            m_squadSize = FixedSquadSize;
            MaximumHealth = Mathf.Max(1f, MaximumHealth);
            FireRateMultiplier = Mathf.Max(.05f, FireRateMultiplier);
            ReloadSpeedMultiplier = Mathf.Max(.05f, ReloadSpeedMultiplier);
            SquadMemberSpacing = Mathf.Max(AgentRadius * 2f + .1f, SquadMemberSpacing);
            SpawnSearchRadius = Mathf.Max(SpawnRadius, SpawnSearchRadius);
            SpawnPositionAttempts = Mathf.Max(FixedSquadSize, SpawnPositionAttempts);
        }
    }
}
