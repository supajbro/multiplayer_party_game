using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace CouchGuys.Player
{
    /// <summary>
    /// Inspector configuration copied from the host Player into its server-driven debug bot.
    /// </summary>
    [Serializable]
    public sealed class DebugCouchBotSettings
    {
        public enum BehaviourState
        {
            CooperateWithPlayer,
            PullAgainstPlayer,
            HoldPosition,
            RotateClockwise,
            RotateAnticlockwise,
            Wander,
            MoveInConfiguredDirection
        }

        public enum CarryPointPreference
        {
            Any = -1,
            FrontLeft = 0,
            FrontRight = 1,
            RearLeft = 2,
            RearRight = 3
        }

        [FormerlySerializedAs("m_spawnBot")]
        [Tooltip("Set above zero on the Player prefab to spawn cooperative carriers for a solo host test.")]
        [SerializeField, Range(0, 3)] private int m_debugBotCount;
        [SerializeField] private BehaviourState m_behaviour = BehaviourState.CooperateWithPlayer;
        [SerializeField] private CarryPointPreference m_preferredCarryPoint = CarryPointPreference.Any;
        [SerializeField] private Vector3 m_spawnOffset = new(2f, 0f, 2f);

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float m_approachSpeed = 3.5f;
        [SerializeField, Min(0.1f)] private float m_carryingSpeed = 7.5f;
        [SerializeField, Min(0f)] private float m_pointStandOff = 0.55f;
        [SerializeField, Min(1f)] private float m_turnSpeed = 540f;
        [SerializeField] private Vector3 m_configuredDirection = Vector3.forward;

        [Header("Decisions")]
        [SerializeField, Min(0.1f)] private float m_targetSearchInterval = 0.75f;
        [SerializeField, Min(0.1f)] private float m_retryDelay = 0.75f;
        [SerializeField, Min(0.1f)] private float m_wanderDirectionInterval = 2f;

        public int DebugBotCount => Mathf.Clamp(m_debugBotCount, 0, 3);
        public BehaviourState Behaviour => m_behaviour;
        public CarryPointPreference PreferredCarryPoint => m_preferredCarryPoint;
        public Vector3 SpawnOffset => m_spawnOffset;
        public float ApproachSpeed => m_approachSpeed;
        public float CarryingSpeed => m_carryingSpeed;
        public float PointStandOff => m_pointStandOff;
        public float TurnSpeed => m_turnSpeed;
        public Vector3 ConfiguredDirection => m_configuredDirection;
        public float TargetSearchInterval => m_targetSearchInterval;
        public float RetryDelay => m_retryDelay;
        public float WanderDirectionInterval => m_wanderDirectionInterval;
    }
}
