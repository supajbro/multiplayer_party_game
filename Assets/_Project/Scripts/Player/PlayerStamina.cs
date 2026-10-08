using System;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Input;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Server-authoritative stamina shared by sprinting and couch carrying.</summary>
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerCouchCarrier), typeof(PlayerHealth))]
    [DisallowMultipleComponent]
    public sealed class PlayerStamina : NetworkBehaviour
    {
        [Header("Capacity")]
        [SerializeField, Min(1f)] private float m_maximumStamina = 100f;
        [SerializeField, Min(0f)] private float m_minimumStaminaToResume = 15f;

        [Header("Drain Per Second")]
        [SerializeField, Min(0f)] private float m_sprintDrain = 12f;
        [SerializeField, Min(0f)] private float m_couchCarryDrain = 6f;
        [SerializeField, Min(0f)] private float m_couchSprintAdditionalDrain = 12f;

        [Header("Recovery")]
        [SerializeField, Min(0f)] private float m_recoveryRate = 15f;
        [SerializeField, Min(0f)] private float m_recoveryDelay = 1.5f;

        [Header("Carrier Multipliers")]
        [SerializeField] private AnimationCurve m_carrierDrainMultiplier = new(
            new Keyframe(1f, 1f), new Keyframe(2f, 0.75f),
            new Keyframe(3f, 0.6f), new Keyframe(4f, 0.5f));

        [Header("Networking")]
        [SerializeField, Min(0.05f)] private float m_replicationInterval = 0.1f;
        [SerializeField, Min(0.05f)] private float m_intentSendInterval = 0.2f;

        private readonly SyncVar<float> m_currentStamina = new();
        private readonly SyncVar<bool> m_exhausted = new();
        private PlayerInputReader m_input;
        private PlayerCouchCarrier m_carrier;
        private PlayerHealth m_health;
        private float m_serverStamina;
        private float m_lastDrainTime;
        private float m_nextReplicationTime;
        private float m_nextIntentTime;
        private float m_lastServerMovementTime;
        private bool m_serverSprintRequested;
        private Vector3 m_previousServerPosition;

        public event Action<float, float> StaminaChanged;
        public float CurrentStamina => IsServerInitialized ? m_serverStamina : m_currentStamina.Value;
        public float MaximumStamina => m_maximumStamina;
        public bool IsExhausted => m_exhausted.Value;
        public bool CanSprint => !m_exhausted.Value && CurrentStamina > 0.01f;
        public bool CanAttachToCouch => !m_exhausted.Value && CurrentStamina > 0.01f;

        private void Awake()
        {
            m_input = GetComponent<PlayerInputReader>();
            m_carrier = GetComponent<PlayerCouchCarrier>();
            m_health = GetComponent<PlayerHealth>();
            m_currentStamina.OnChange += OnStaminaChanged;
        }

        private void OnDestroy() => m_currentStamina.OnChange -= OnStaminaChanged;

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_serverStamina = m_maximumStamina;
            m_currentStamina.Value = m_maximumStamina;
            m_exhausted.Value = false;
            m_previousServerPosition = transform.position;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            StaminaChanged?.Invoke(m_currentStamina.Value, m_maximumStamina);
        }

        private void Update()
        {
            if (IsOwner && m_input != null && m_input.isActiveAndEnabled &&
                Time.unscaledTime >= m_nextIntentTime)
            {
                bool sprintRequested = m_input.SprintHeld && m_input.Move.sqrMagnitude > 0.0025f;
                if (IsServerInitialized) m_serverSprintRequested = sprintRequested;
                else SubmitSprintIntentServerRpc(sprintRequested);
                m_nextIntentTime = Time.unscaledTime + m_intentSendInterval;
            }

            if (IsServerInitialized) UpdateServerStamina();
        }

        [ServerRpc]
        private void SubmitSprintIntentServerRpc(bool sprintRequested, Channel channel = Channel.Unreliable) =>
            m_serverSprintRequested = sprintRequested;

        [Server]
        private void UpdateServerStamina()
        {
            if (m_health == null || m_health.IsAiTeammate)
            {
                m_serverStamina = m_maximumStamina;
                ReplicateIfDue(false);
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 displacement = Vector3.ProjectOnPlane(transform.position - m_previousServerPosition, Vector3.up);
            m_previousServerPosition = transform.position;
            if (deltaTime > 0f && displacement.sqrMagnitude > 0.000025f)
                m_lastServerMovementTime = Time.time;
            bool moving = Time.time - m_lastServerMovementTime <= 0.25f;
            bool carrying = m_carrier != null && m_carrier.IsCarrying;
            bool sprinting = moving && m_serverSprintRequested && !m_exhausted.Value;
            float drain = 0f;

            if (carrying)
            {
                CouchCarryController couch = m_carrier.GetCarriedCouchServer();
                int carrierCount = Mathf.Max(1, couch != null ? couch.ValidServerCarrierCount : 1);
                float multiplier = Mathf.Max(0f, m_carrierDrainMultiplier.Evaluate(carrierCount));
                drain = (m_couchCarryDrain + (sprinting ? m_couchSprintAdditionalDrain : 0f)) * multiplier;
            }
            else if (sprinting)
            {
                drain = m_sprintDrain;
            }

            if (drain > 0f && m_health.IsAlive)
            {
                m_serverStamina = Mathf.Max(0f, m_serverStamina - drain * deltaTime);
                m_lastDrainTime = Time.time;
                if (m_serverStamina <= 0f)
                {
                    m_exhausted.Value = true;
                    m_serverSprintRequested = false;
                    if (carrying) m_carrier.ReleaseForStaminaServer();
                }
            }
            else if (!carrying && Time.time >= m_lastDrainTime + m_recoveryDelay)
            {
                m_serverStamina = Mathf.Min(m_maximumStamina, m_serverStamina + m_recoveryRate * deltaTime);
                if (m_exhausted.Value && m_serverStamina >= m_minimumStaminaToResume)
                    m_exhausted.Value = false;
            }

            ReplicateIfDue(m_serverStamina <= 0f || m_serverStamina >= m_maximumStamina);
        }

        [Server]
        private void ReplicateIfDue(bool force)
        {
            if (!force && Time.unscaledTime < m_nextReplicationTime) return;
            m_nextReplicationTime = Time.unscaledTime + m_replicationInterval;
            if (!Mathf.Approximately(m_currentStamina.Value, m_serverStamina))
                m_currentStamina.Value = m_serverStamina;
        }

        private void OnStaminaChanged(float previous, float next, bool asServer) =>
            StaminaChanged?.Invoke(next, m_maximumStamina);

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_maximumStamina = Mathf.Max(1f, m_maximumStamina);
            m_minimumStaminaToResume = Mathf.Clamp(m_minimumStaminaToResume, 0f, m_maximumStamina);
            m_replicationInterval = Mathf.Max(0.05f, m_replicationInterval);
            m_intentSendInterval = Mathf.Max(0.05f, m_intentSendInterval);
        }
#endif
    }
}