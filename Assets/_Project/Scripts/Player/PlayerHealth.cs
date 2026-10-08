using System;
using CouchGuys.Networking;
using CouchGuys.ProceduralGeneration;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Server-owned health and replicated knockdown state for a network player.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHealth : NetworkBehaviour
    {
        [Header("Health")]
        [SerializeField, Min(1f)] private float m_maxHealth = 100f;
        [Tooltip("One damage event at or above this fraction of maximum health causes a knockdown.")]
        [SerializeField, Range(0.01f, 1f)] private float m_knockdownDamageFraction = 0.25f;

        [Header("Death And Respawn")]
        [SerializeField, Min(0.1f)] private float m_respawnDelay = 5f;

        [Header("Knockdown")]
        [SerializeField, Min(0.1f)] private float m_knockdownDuration = 2.25f;
        [SerializeField, Min(0f)] private float m_upwardImpulse = 2.5f;
        [SerializeField, Range(10f, 90f)] private float m_fallAngle = 78f;
        [SerializeField, Min(1f)] private float m_fallDegreesPerSecond = 360f;
        [SerializeField, Min(1f)] private float m_getUpDegreesPerSecond = 180f;
        [SerializeField] private Transform m_visualRoot;
        [SerializeField] private ThirdPersonPlayerController m_playerController;

        private readonly SyncVar<float> m_currentHealth = new();
        private readonly SyncVar<bool> m_knockedDown = new();
        private readonly SyncVar<Vector3> m_knockdownDirection = new(Vector3.forward);

        private Quaternion m_visualBaseRotation;
        private float m_visualFallProgress;
        private float m_recoverAtServerTime;
        private float m_respawnAtServerTime = float.PositiveInfinity;

        public event Action<float, float> HealthChanged;
        public event Action<bool> KnockdownChanged;
        public event Action Died;
        public event Action Respawned;

        public float CurrentHealth => m_currentHealth.Value;
        public float MaximumHealth => m_maxHealth;
        public bool IsAlive => m_currentHealth.Value > 0f;
        public bool IsKnockedDown => m_knockedDown.Value;
        public Vector3 AimPoint => transform.position + Vector3.up * 0.9f;

        private void Awake()
        {
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
            m_visualRoot ??= transform.Find("Visual");
            if (m_visualRoot != null)
            {
                m_visualBaseRotation = m_visualRoot.localRotation;
            }

            m_currentHealth.OnChange += OnHealthChanged;
            m_knockedDown.OnChange += OnKnockedDownChanged;
        }

        private void OnDestroy()
        {
            m_currentHealth.OnChange -= OnHealthChanged;
            m_knockedDown.OnChange -= OnKnockedDownChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_currentHealth.Value = m_maxHealth;
            m_knockedDown.Value = false;
            m_respawnAtServerTime = float.PositiveInfinity;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyKnockdownState(m_knockedDown.Value);
            HealthChanged?.Invoke(m_currentHealth.Value, m_maxHealth);
        }

        private void Update()
        {
            if (IsServerInitialized && !IsAlive && Time.time >= m_respawnAtServerTime)
            {
                RespawnAtDepotServer();
            }
            else if (IsServerInitialized && IsAlive && m_knockedDown.Value &&
                     Time.time >= m_recoverAtServerTime)
            {
                m_knockedDown.Value = false;
            }

            UpdateKnockdownVisual();
        }

        [Server]
        public void ApplyDamageServer(float damage, Vector3 impactDirection, float knockbackForce)
        {
            if (damage <= 0f || !IsAlive)
            {
                return;
            }

            float appliedDamage = Mathf.Min(damage, m_currentHealth.Value);
            m_currentHealth.Value = Mathf.Max(0f, m_currentHealth.Value - appliedDamage);
            bool died = !IsAlive;
            Vector3 horizontalDirection = Vector3.ProjectOnPlane(impactDirection, Vector3.up);
            if (horizontalDirection.sqrMagnitude < 0.001f)
            {
                horizontalDirection = transform.forward;
            }

            horizontalDirection.Normalize();
            bool severeHit = appliedDamage >= m_maxHealth * m_knockdownDamageFraction || died;
            float appliedForce = severeHit ? knockbackForce : knockbackForce * 0.3f;
            Vector3 impulse = horizontalDirection * appliedForce;
            if (severeHit)
            {
                impulse += Vector3.up * m_upwardImpulse;
                m_knockdownDirection.Value = horizontalDirection;
                m_recoverAtServerTime = died
                    ? float.PositiveInfinity
                    : Time.time + m_knockdownDuration;
                m_knockedDown.Value = true;
                GetComponent<PlayerCouchCarrier>()?.ReleaseForDamageServer();
                if (died)
                {
                    m_respawnAtServerTime = Time.time + m_respawnDelay;
                    NotifyDiedObserversRpc();
                }
            }

            ApplyImpactTargetRpc(Owner, impulse);
            PlayHitObserversRpc();
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayHitObserversRpc()
        {
            GetComponent<CouchGuyAnimationDriver>()?.PlayHitReaction();
        }

        [TargetRpc]
        private void ApplyImpactTargetRpc(NetworkConnection connection, Vector3 impulse)
        {
            if (IsOwner)
            {
                m_playerController?.ApplyExternalImpulse(impulse);
            }
        }

        [ObserversRpc(RunLocally = true)]
        private void NotifyDiedObserversRpc()
        {
            Died?.Invoke();
        }

        [Server]
        private void RespawnAtDepotServer()
        {
            NeighbourhoodGenerator generator = FindFirstObjectByType<NeighbourhoodGenerator>();
            Transform spawnPoint = generator != null
                ? NeighbourhoodPlayerSpawner.SelectSpawnPoint(
                    generator.GeneratedStartingArea,
                    OwnerId)
                : null;
            if (spawnPoint == null)
            {
                // Generation should always be ready before a Player exists. Retry on
                // the next frame rather than reviving at an unsafe arbitrary position.
                m_respawnAtServerTime = Time.time + 0.1f;
                return;
            }

            GetComponent<PlayerCouchCarrier>()?.ReleaseForDamageServer();
            PlayerCouchCarrier carrier = GetComponent<PlayerCouchCarrier>();
            if (carrier != null)
            {
                carrier.TeleportWithCouchServer(spawnPoint.position, spawnPoint.rotation);
            }
            else
            {
                m_playerController?.Teleport(spawnPoint.position, spawnPoint.rotation);
            }

            m_currentHealth.Value = m_maxHealth;
            m_knockedDown.Value = false;
            m_recoverAtServerTime = 0f;
            m_respawnAtServerTime = float.PositiveInfinity;
            NotifyRespawnedObserversRpc();
        }

        [ObserversRpc(RunLocally = true)]
        private void NotifyRespawnedObserversRpc()
        {
            Respawned?.Invoke();
        }

        private void OnHealthChanged(float previous, float next, bool asServer)
        {
            HealthChanged?.Invoke(next, m_maxHealth);
        }

        private void OnKnockedDownChanged(bool previous, bool next, bool asServer)
        {
            ApplyKnockdownState(next);
        }

        private void ApplyKnockdownState(bool knockedDown)
        {
            m_playerController?.SetMovementLocked(knockedDown);
            KnockdownChanged?.Invoke(knockedDown);
        }

        private void UpdateKnockdownVisual()
        {
            if (m_visualRoot == null)
            {
                return;
            }

            float speed = m_knockedDown.Value ? m_fallDegreesPerSecond : m_getUpDegreesPerSecond;
            float target = m_knockedDown.Value ? 1f : 0f;
            m_visualFallProgress = Mathf.MoveTowards(
                m_visualFallProgress,
                target,
                speed / Mathf.Max(1f, m_fallAngle) * Time.deltaTime);

            Vector3 localDirection = transform.InverseTransformDirection(m_knockdownDirection.Value);
            Vector3 axis = Vector3.Cross(Vector3.up, localDirection);
            if (axis.sqrMagnitude < 0.001f)
            {
                axis = Vector3.right;
            }

            m_visualRoot.localRotation = Quaternion.AngleAxis(
                m_fallAngle * m_visualFallProgress,
                axis.normalized) * m_visualBaseRotation;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_maxHealth = Mathf.Max(1f, m_maxHealth);
            m_knockdownDuration = Mathf.Max(0.1f, m_knockdownDuration);
            m_respawnDelay = Mathf.Max(0.1f, m_respawnDelay);
        }
#endif
    }
}
