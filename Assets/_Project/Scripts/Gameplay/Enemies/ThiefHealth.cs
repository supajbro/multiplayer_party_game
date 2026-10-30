using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;
using CouchGuys.Gameplay.Pedestrians;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Server-owned health and recovery, using the shared pedestrian bone physics.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefHealth : NetworkBehaviour
    {
        [SerializeField] private ThiefSettings m_settings;
        [SerializeField] private NavMeshAgent m_agent;
        [SerializeField] private Collider m_collider;
        [SerializeField] private PedestrianRagdoll m_ragdoll;
        [SerializeField, Range(0f, 1f), Tooltip("Scales weapon knockback on ragdoll bodies; vehicle impacts retain their full impulse.")]
        private float m_damageRagdollImpulseScale = 0.2f;
        private float MaxHealth => m_settings != null ? m_settings.Health.MaxHealth : 100f;
        private float KnockbackDuration => m_settings != null ? m_settings.Health.KnockbackDuration : 0.22f;
        private readonly SyncVar<float> m_currentHealth = new();
        private readonly SyncVar<bool> m_dead = new();
        // State and initial impulse replicate only on transitions. NetworkTransform carries the hips root.
        private readonly SyncVar<Vector3> m_deathImpulse = new();
        private readonly SyncVar<Vector3> m_recoveryPosition = new();
        private readonly SyncVar<byte> m_ragdollState = new(); // standing, grapple, death
        private bool m_grappleHeld, m_deathRagdollApplied;
        private float m_recoverAfter, m_groundedSince;
        private Vector3 m_knockbackVelocity;
        private float m_resumeNavigationAt;
        private float m_despawnAt = float.PositiveInfinity;
        private ThiefAnimationDriver m_animationDriver;
        private ThiefNavigator m_navigator;
        private EnemyWeapon m_weapon;

        public event Action<float, float> HealthChanged;
        public bool IsAlive => !m_dead.Value;
        public float CurrentHealth => m_currentHealth.Value;
        public bool IsBeingKnockedBack => m_ragdollState.Value != 0 || Time.time < m_resumeNavigationAt;

        private void Awake()
        {
            m_agent ??= GetComponent<NavMeshAgent>();
            m_collider ??= GetComponent<Collider>();
            m_ragdoll ??= GetComponentInChildren<PedestrianRagdoll>(true);
            m_animationDriver = GetComponentInChildren<ThiefAnimationDriver>(true);
            m_navigator = GetComponent<ThiefNavigator>();
            m_weapon = GetComponent<EnemyWeapon>();
            m_currentHealth.OnChange += OnHealthChanged;
            m_dead.OnChange += OnDeadChanged;
            m_ragdollState.OnChange += OnRagdollChanged;
        }

        private void OnDestroy()
        {
            m_currentHealth.OnChange -= OnHealthChanged;
            m_dead.OnChange -= OnDeadChanged;
            m_ragdollState.OnChange -= OnRagdollChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_grappleHeld = false;
            m_despawnAt = float.PositiveInfinity;
            m_currentHealth.Value = MaxHealth;
            m_dead.Value = false;
            m_ragdollState.Value = 0;
            ApplyRagdollState();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyRagdollState();
        }

        [Server]
        internal void SetGrappledServer(bool held)
        {
            m_grappleHeld = held;
            m_groundedSince = 0f;
            if (!IsAlive) return;
            if (held && m_ragdollState.Value == 0)
            {
                m_ragdollState.Value = 1;
                m_recoverAfter = Time.time + 0.2f;
            }
        }

        [Server]
        internal void ApplyGrappleFlickServer(Vector3 impulse, float maximumSpeed, float drag, float gravity)
        {
            if (!IsAlive || m_ragdoll == null) return;
            m_ragdoll.AddImpulse(Vector3.ClampMagnitude(m_ragdoll.Body.linearVelocity + impulse,
                maximumSpeed) - m_ragdoll.Body.linearVelocity);
            m_recoverAfter = Time.time + 0.2f;
        }

        [Server]
        internal void ApplyGrapplePullServer(Vector3 direction, float acceleration, float speed, float deltaTime)
        {
            if (!IsAlive || m_ragdollState.Value == 0 || m_ragdoll == null) return;
            m_ragdoll.Pull(direction, acceleration, speed, deltaTime);
        }

        private void FixedUpdate()
        {
            if (m_ragdollState.Value == 0 || m_ragdoll == null || !m_ragdoll.IsFallen ||
                (!IsServerInitialized && !IsClientInitialized)) return;
            if (m_ragdoll.enabled) m_ragdoll.FollowRoot(transform, IsServerInitialized);
            if (!IsServerInitialized || !IsAlive || m_grappleHeld || Time.time < m_recoverAfter) return;
            if (!m_ragdoll.TryGround(out Vector3 ground)) { m_groundedSince = 0f; return; }
            if (m_groundedSince == 0f) m_groundedSince = Time.time;
            if (Time.time < m_groundedSince + 0.2f) return;
            Vector3 point = new Vector3(m_ragdoll.Body.position.x, ground.y, m_ragdoll.Body.position.z);
            if (!NavMesh.SamplePosition(point, out NavMeshHit landing, 1f, m_agent.areaMask) ||
                Mathf.Abs(landing.position.y - ground.y) > 0.35f) return;
            m_ragdoll.RepositionRoot(transform, landing.position);
            m_recoveryPosition.Value = landing.position;
            m_resumeNavigationAt = Time.time + 0.4f;
            m_ragdollState.Value = 0;
        }

        private void ApplyRagdollState()
        {
            bool fallen = m_ragdollState.Value != 0 || !IsAlive;
            if (m_agent != null)
            {
                m_agent.enabled = !fallen && IsServerInitialized;
                if (m_agent.enabled && m_agent.isOnNavMesh) m_agent.Warp(transform.position);
            }
            if (m_collider != null) m_collider.enabled = !fallen;
            if (m_animationDriver != null) m_animationDriver.enabled = !fallen;
            if (m_weapon != null)
            {
                if (fallen && IsServerInitialized) m_weapon.SetTargetServer(null);
                m_weapon.enabled = !fallen;
            }
            if (m_ragdoll == null) return;
            if (fallen && (!m_ragdoll.IsFallen || (m_ragdollState.Value == 2 && !m_deathRagdollApplied)))
            {
                m_deathRagdollApplied = m_ragdollState.Value == 2;
                m_ragdoll.Fall(m_deathRagdollApplied ? m_deathImpulse.Value : m_knockbackVelocity,
                    m_deathRagdollApplied);
                m_knockbackVelocity = Vector3.zero;
            }
            else if (!fallen)
            {
                m_deathRagdollApplied = false;
                m_ragdoll.Recover();
            }
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            if (!IsAlive)
            {
                if (Time.time >= m_despawnAt) Despawn(NetworkObject);
                return;
            }
            if (m_ragdollState.Value != 0) return;
            if (Time.time < m_resumeNavigationAt)
            {
                Vector3 displacement = m_knockbackVelocity * Time.deltaTime;
                if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh) m_agent.Move(displacement);
                else transform.position += displacement;
                float damping = m_settings != null ? m_settings.Health.KnockbackDamping : 14f;
                m_knockbackVelocity = Vector3.MoveTowards(m_knockbackVelocity, Vector3.zero, damping * Time.deltaTime);
            }
            else if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh && m_agent.isStopped &&
                (m_navigator == null || !m_navigator.IsAlerting)) m_agent.isStopped = false;
        }

        [Server]
        public void ApplyDamageServer(float damage, Vector3 impactDirection, float knockbackForce) =>
            ApplyImpactServer(damage, impactDirection, knockbackForce, 0f, false,
                ragdollImpulseScale: m_damageRagdollImpulseScale);

        [Server]
        public void ApplyVehicleImpactServer(float damage, Vector3 impactDirection, float knockbackForce,
            float upwardForce, bool directionalKnockback = false) =>
            ApplyImpactServer(damage, impactDirection, knockbackForce, upwardForce, true, directionalKnockback);

        private void ApplyImpactServer(float damage, Vector3 impactDirection, float knockbackForce,
            float upwardForce, bool allowPushWithoutDamage, bool directionalKnockback = false,
            float ragdollImpulseScale = 1f)
        {
            if ((!allowPushWithoutDamage && damage <= 0f) ||
                (damage <= 0f && knockbackForce <= 0f && upwardForce <= 0f) || !IsAlive) return;
            float appliedDamage = Mathf.Max(0f, damage);
            m_currentHealth.Value = Mathf.Max(0f, m_currentHealth.Value - appliedDamage);
            Vector3 impulse = impactDirection.normalized * Mathf.Max(0f, knockbackForce) +
                Vector3.up * Mathf.Max(0f, upwardForce);
            if (m_currentHealth.Value <= 0f)
            {
                m_deathImpulse.Value = impulse * ragdollImpulseScale;
                m_ragdollState.Value = 2;
                m_dead.Value = true;
                float chance = m_settings != null ? m_settings.Health.WeaponDropChance : 35f;
                if (TryGetComponent(out EnemyWeapon weapon) && UnityEngine.Random.value * 100f < chance)
                    CouchGuys.Gameplay.Weapons.WeaponPickup.SpawnServer(
                        (CouchGuys.Gameplay.Weapons.PlayerWeaponType)weapon.WeaponType,
                        transform.position + Vector3.up, transform.rotation, Vector3.up * 1.5f);
                m_despawnAt = Time.time + (m_settings != null ? m_settings.Health.DeathDespawnDelay : 2.5f);
                return;
            }
            if (m_ragdollState.Value != 0) m_ragdoll?.AddImpulse(impulse * ragdollImpulseScale);
            else
            {
                if (directionalKnockback) m_knockbackVelocity = impulse;
                else m_knockbackVelocity += impulse;
                m_resumeNavigationAt = Time.time + KnockbackDuration;
                if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
                {
                    m_agent.isStopped = true;
                    m_agent.ResetPath();
                }
                if (appliedDamage > 0f) PlayHitObserversRpc();
            }
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayHitObserversRpc() => m_animationDriver?.TriggerHit();
        private void OnHealthChanged(float previous, float next, bool asServer) => HealthChanged?.Invoke(next, MaxHealth);
        private void OnRagdollChanged(byte previous, byte next, bool asServer)
        {
            if (previous != 0 && next == 0 && !IsServerInitialized)
            {
                if (m_ragdoll != null && m_ragdoll.IsFallen)
                    m_ragdoll.RepositionRoot(transform, m_recoveryPosition.Value);
                else transform.position = m_recoveryPosition.Value;
            }
            ApplyRagdollState();
        }
        private void OnDeadChanged(bool previous, bool next, bool asServer)
        {
            if (next) { m_grappleHeld = false; ApplyRagdollState(); }
        }
    }
}
