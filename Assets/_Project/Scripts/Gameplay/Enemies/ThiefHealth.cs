using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Server-owned thief health with recoverable NavMesh knockback.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefHealth : NetworkBehaviour
    {
        [SerializeField] private ThiefSettings m_settings;
        [SerializeField] private NavMeshAgent m_agent;
        [SerializeField] private Collider m_collider;
        private float m_maxHealth => m_settings != null
            ? m_settings.Health.MaxHealth : 100f;
        private float m_knockbackDuration => m_settings != null
            ? m_settings.Health.KnockbackDuration : 0.22f;
        private float m_knockbackDamping => m_settings != null
            ? m_settings.Health.KnockbackDamping : 14f;
        private float m_deathDespawnDelay => m_settings != null
            ? m_settings.Health.DeathDespawnDelay : 2.5f;
        private float m_weaponDropChance => m_settings != null
            ? m_settings.Health.WeaponDropChance : 35f;

        private readonly SyncVar<float> m_currentHealth = new();
        private readonly SyncVar<bool> m_dead = new();
        private Vector3 m_knockbackVelocity;
        private float m_resumeNavigationAt;
        private float m_despawnAt = float.PositiveInfinity;
        private ThiefAnimationDriver m_animationDriver;
        private ThiefNavigator m_navigator;

        public event Action<float, float> HealthChanged;
        public bool IsAlive => !m_dead.Value;
        public float CurrentHealth => m_currentHealth.Value;
        public bool IsBeingKnockedBack => m_grappleFlying || Time.time < m_resumeNavigationAt;

        private CharacterController m_grappleFlightController;
        private bool m_grappleFlying;
        private bool m_grappleAgentWasEnabled;
        private Vector3 m_grappleFlightVelocity;
        private float m_grappleFlightStartedAt;
        private float m_grappleFlightDrag;
        private float m_grappleFlightGravity;

        [Server]
        internal void ApplyGrappleFlickServer(Vector3 impulse, float maximumSpeed, float drag, float gravity)
        {
            if (!IsAlive) return;
            if (m_grappleFlightController == null)
            {
                m_grappleFlightController = gameObject.AddComponent<CharacterController>();
                var capsule = m_collider as CapsuleCollider;
                m_grappleFlightController.height = capsule != null ? capsule.height : 1.8f;
                m_grappleFlightController.radius = capsule != null ? capsule.radius : 0.35f;
                m_grappleFlightController.center = capsule != null ? capsule.center : Vector3.up * 0.9f;
                m_grappleFlightController.skinWidth = 0.03f;
                m_grappleFlightController.stepOffset = 0.1f;
            }
            if (!m_grappleFlying)
            {
                m_grappleAgentWasEnabled = m_agent != null && m_agent.enabled;
                m_grappleFlightVelocity = m_knockbackVelocity;
                m_knockbackVelocity = Vector3.zero;
            }
            m_grappleFlying = true;
            m_grappleFlightStartedAt = Time.time;
            m_grappleFlightVelocity = Vector3.ClampMagnitude(m_grappleFlightVelocity + impulse, maximumSpeed);
            m_grappleFlightDrag = drag;
            m_grappleFlightGravity = gravity;
            if (m_agent != null) m_agent.enabled = false;
            if (m_collider != null) m_collider.enabled = false;
            m_grappleFlightController.enabled = true;
        }

        private void UpdateGrappleFlight()
        {
            m_grappleFlightVelocity += Physics.gravity * (m_grappleFlightGravity * Time.deltaTime);
            CollisionFlags flags = m_grappleFlightController.Move(m_grappleFlightVelocity * Time.deltaTime);
            Vector3 horizontal = Vector3.ProjectOnPlane(m_grappleFlightVelocity, Vector3.up);
            m_grappleFlightVelocity += Vector3.MoveTowards(horizontal, Vector3.zero,
                m_grappleFlightDrag * Time.deltaTime) - horizontal;
            if ((flags & CollisionFlags.Above) != 0) m_grappleFlightVelocity.y = Mathf.Min(0f, m_grappleFlightVelocity.y);
            if ((flags & CollisionFlags.Below) == 0 || m_grappleFlightVelocity.y > 0f ||
                Time.time < m_grappleFlightStartedAt + 0.15f) return;
            m_grappleFlightVelocity.y = -2f;
            // Only rejoin a nearby surface at this height; never snap to a floor
            // above/below the landing point or teleport back to the launch point.
            NavMeshHit landing = default;
            if (m_grappleAgentWasEnabled && (!NavMesh.SamplePosition(transform.position, out landing,
                1f, m_agent.areaMask) || Mathf.Abs(landing.position.y - transform.position.y) > 0.5f)) return;
            m_grappleFlying = false;
            m_grappleFlightController.enabled = false;
            if (m_collider != null) m_collider.enabled = true;
            if (m_grappleAgentWasEnabled)
            {
                m_agent.enabled = true;
                m_agent.Warp(landing.position);
            }
            m_knockbackVelocity = Vector3.ProjectOnPlane(m_grappleFlightVelocity, Vector3.up);
            m_resumeNavigationAt = Time.time + m_knockbackDuration;
        }

        private void Awake()
        {
            m_agent ??= GetComponent<NavMeshAgent>();
            m_collider ??= GetComponent<Collider>();
            m_animationDriver = GetComponentInChildren<ThiefAnimationDriver>(true);
            m_navigator = GetComponent<ThiefNavigator>();
            m_currentHealth.OnChange += OnHealthChanged;
            m_dead.OnChange += OnDeadChanged;
        }

        private void OnDestroy()
        {
            m_currentHealth.OnChange -= OnHealthChanged;
            m_dead.OnChange -= OnDeadChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_grappleFlying = false;
            if (m_grappleFlightController != null)
            {
                m_grappleFlightController.enabled = false;
                if (m_collider != null) m_collider.enabled = true;
            }
            m_currentHealth.Value = m_maxHealth;
            m_dead.Value = false;
        }

        private void Update()
        {
            if (!IsServerInitialized)
            {
                return;
            }

            if (m_dead.Value)
            {
                if (Time.time >= m_despawnAt)
                {
                    Despawn(NetworkObject);
                }
                return;
            }

            if (m_grappleFlying)
            {
                UpdateGrappleFlight();
                return;
            }

            if (Time.time < m_resumeNavigationAt)
            {
                Vector3 displacement = m_knockbackVelocity * Time.deltaTime;
                if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
                {
                    m_agent.Move(displacement);
                }
                else
                {
                    transform.position += displacement;
                }
                m_knockbackVelocity = Vector3.MoveTowards(
                    m_knockbackVelocity, Vector3.zero, m_knockbackDamping * Time.deltaTime);
            }
            else if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh && m_agent.isStopped &&
                (m_navigator == null || !m_navigator.IsAlerting))
            {
                m_agent.isStopped = false;
            }
        }

        [Server]
        public void ApplyDamageServer(float damage, Vector3 impactDirection, float knockbackForce)
        {
            ApplyImpactServer(damage, impactDirection, knockbackForce, 0f, false);
        }

        /// <summary>Applies a server-authoritative vehicle push, with optional damage.</summary>
        [Server]
        public void ApplyVehicleImpactServer(
            float damage,
            Vector3 impactDirection,
            float knockbackForce,
            float upwardForce,
            bool directionalKnockback = false)
        {
            ApplyImpactServer(damage, impactDirection, knockbackForce, upwardForce, true, directionalKnockback);
        }

        [Server]
        internal void ApplyGrapplePullServer(Vector3 direction, float acceleration, float speed, float deltaTime)
        {
            if (!IsAlive) return;
            if (m_grappleFlying)
            {
                m_grappleFlightVelocity += direction * Mathf.Min(acceleration * deltaTime,
                    Mathf.Max(0f, speed - Vector3.Dot(m_grappleFlightVelocity, direction)));
                return;
            }
            // Keep the thief on its existing collision-aware NavMesh movement path.
            direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            m_knockbackVelocity += direction * Mathf.Min(acceleration * deltaTime,
                Mathf.Max(0f, speed - Vector3.Dot(m_knockbackVelocity, direction)));
            m_resumeNavigationAt = Time.time + m_knockbackDuration;
            if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
            {
                m_agent.isStopped = true;
                m_agent.ResetPath();
            }
        }

        private void ApplyImpactServer(
            float damage,
            Vector3 impactDirection,
            float knockbackForce,
            float upwardForce,
            bool allowPushWithoutDamage,
            bool directionalKnockback = false)
        {
            if ((!allowPushWithoutDamage && damage <= 0f) ||
                (damage <= 0f && knockbackForce <= 0f && upwardForce <= 0f) ||
                m_dead.Value)
            {
                return;
            }

            float appliedDamage = Mathf.Max(0f, damage);
            m_currentHealth.Value = Mathf.Max(0f, m_currentHealth.Value - appliedDamage);
            Vector3 direction = Vector3.ProjectOnPlane(impactDirection, Vector3.up).normalized;
            Vector3 impulse = direction * Mathf.Max(0f, knockbackForce) +
                              Vector3.up * Mathf.Max(0f, upwardForce);
            if (m_grappleFlying)
            {
                if (directionalKnockback) m_grappleFlightVelocity = impulse;
                else m_grappleFlightVelocity += impulse;
            }
            else if (directionalKnockback) m_knockbackVelocity = impulse;
            else m_knockbackVelocity += impulse;
            m_resumeNavigationAt = Time.time + m_knockbackDuration;
            if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
            {
                m_agent.isStopped = true;
                m_agent.ResetPath();
            }

            if (m_currentHealth.Value <= 0f)
            {
                m_dead.Value = true;
                if (TryGetComponent(out EnemyWeapon weapon) && UnityEngine.Random.value * 100f < m_weaponDropChance)
                    CouchGuys.Gameplay.Weapons.WeaponPickup.SpawnServer(
                        (CouchGuys.Gameplay.Weapons.PlayerWeaponType)weapon.WeaponType,
                        transform.position + Vector3.up, transform.rotation, Vector3.up * 1.5f);
                m_despawnAt = Time.time + m_deathDespawnDelay;
            }
            else if (appliedDamage > 0f)
            {
                PlayHitObserversRpc();
            }
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayHitObserversRpc() => m_animationDriver?.TriggerHit();

        private void OnHealthChanged(float previous, float next, bool asServer) =>
            HealthChanged?.Invoke(next, m_maxHealth);

        private void OnDeadChanged(bool previous, bool next, bool asServer)
        {
            if (!next) return;
            m_grappleFlying = false;
            if (m_grappleFlightController != null) m_grappleFlightController.enabled = false;
            if (m_agent != null) m_agent.enabled = false;
            if (m_collider != null) m_collider.enabled = false;
            m_animationDriver?.TriggerDeath();
        }

    }
}
