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
        [SerializeField, Min(0.02f), Tooltip("Maximum gap below the standing collider before entering airborne ragdoll.")]
        private float m_groundSupportDistance = 0.15f;
        [SerializeField, Tooltip("Random airborne flip speed in radians per second. Zero disables the extra tumble.")]
        private Vector2 m_airborneFlipSpeed = NpcImpactPhysics.AirborneFlipSpeed;
        [Header("Ragdoll Recovery")]
        [SerializeField, Min(.2f), Tooltip("Seconds a living thief stays ragdolled after landing before getting up.")]
        private float m_groundedRecoveryDuration = NpcImpactPhysics.GroundedRecoveryDuration;
        [SerializeField, Min(.2f), Tooltip("Delay after landing before searching farther for a clear standing spot.")]
        private float m_recoveryFallbackDelay = 1.25f;
        [SerializeField, Min(1f)] private float m_recoverySearchRadius = 4f;
        [SerializeField, Min(.02f), Tooltip("Shared allowed height difference between physical ground and the baked NavMesh.")]
        private float m_navigationSurfaceTolerance = .35f;
        [SerializeField, Range(0f, 1f), Tooltip("Scales weapon knockback on ragdoll bodies; vehicle impacts retain their full impulse.")]
        private float m_damageRagdollImpulseScale = NpcImpactPhysics.DamageRagdollImpulseScale;
        private float MaxHealth => m_settings != null ? m_settings.Health.MaxHealth : 100f;
        private float KnockbackDuration => m_settings != null ? m_settings.Health.KnockbackDuration : NpcImpactPhysics.KnockbackDuration;
        private readonly SyncVar<float> m_currentHealth = new();
        private readonly SyncVar<bool> m_dead = new();
        // State and initial impulse replicate only on transitions. NetworkTransform carries the hips root.
        private readonly SyncVar<Vector3> m_deathImpulse = new();
        private readonly SyncVar<Vector3> m_recoveryPosition = new();
        private readonly SyncVar<Vector3> m_airborneImpulse = new();
        private readonly SyncVar<Vector3> m_airborneSpin = new();
        private readonly SyncVar<byte> m_ragdollState = new(); // standing, airborne/grapple, death
        private bool m_grappleHeld, m_deathRagdollApplied;
        private float m_recoverAfter, m_groundedSince;
        private float m_lastGroundContactAt;
        private float m_nextRecoveryAttempt;
        private float m_nextNavigationAttachAttempt;
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
            Physics.SyncTransforms();
            if (!HasGroundSupport()) BeginAirborneRagdoll();
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
                BeginAirborneRagdoll();
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
            if (IsServerInitialized && IsAlive && m_ragdollState.Value == 0 && !HasGroundSupport())
                BeginAirborneRagdoll();
            if (m_ragdollState.Value == 0 || m_ragdoll == null || !m_ragdoll.IsFallen ||
                (!IsServerInitialized && !IsClientInitialized)) return;
            if (m_ragdoll.enabled) m_ragdoll.FollowRoot(transform, IsServerInitialized);
            if (!IsServerInitialized || !IsAlive || m_grappleHeld || Time.time < m_recoverAfter) return;
            if (!m_ragdoll.TryGround(out Vector3 ground))
            {
                // Small bounces while settling should not restart the fallback
                // timer forever. Recovery still requires contact on this step.
                if (Time.time > m_lastGroundContactAt + .25f) m_groundedSince = 0f;
                return;
            }
            m_lastGroundContactAt = Time.time;
            if (m_groundedSince == 0f) m_groundedSince = Time.time;
            if (!HasFinishedGroundedRecovery() || Time.time < m_nextRecoveryAttempt) return;
            m_nextRecoveryAttempt = Time.time + .2f;
            if (!TryFindRecoveryPosition(ground, out Vector3 standingPosition)) return;
            m_ragdoll.RepositionRoot(transform, standingPosition);
            m_recoveryPosition.Value = standingPosition;
            m_resumeNavigationAt = Time.time + 0.4f;
            m_ragdollState.Value = 0;
        }

        private bool HasFinishedGroundedRecovery() =>
            Time.time >= m_groundedSince + Mathf.Max(.2f, m_groundedRecoveryDuration);

        private bool TryGetNavigationPosition(Vector3 point, float radius, out Vector3 position)
        {
            position = default;
            var filter = new NavMeshQueryFilter {
                agentTypeID = m_agent != null ? m_agent.agentTypeID : 0,
                areaMask = m_agent != null ? m_agent.areaMask : NavMesh.AllAreas
            };
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, radius, filter) ||
                Mathf.Abs(hit.position.y - point.y) > m_navigationSurfaceTolerance ||
                !TryStandingPosition(hit.position, out Vector3 supported) ||
                Mathf.Abs(hit.position.y - supported.y) > m_navigationSurfaceTolerance) return false;
            // Keep the sampled NavMesh point instead of projecting the root back
            // off the mesh onto a physics surface with a different height.
            position = hit.position;
            return true;
        }

        private bool TryAttachNavigation(float radius)
        {
            if (m_agent == null) return false;
            if (m_agent.enabled && m_agent.isOnNavMesh) return true;
            if (!TryGetNavigationPosition(transform.position, radius, out Vector3 position)) return false;
            Vector3 previousPosition = transform.position;
            m_agent.enabled = false;
            transform.position = position;
            m_agent.enabled = true;
            if (!m_agent.Warp(position) || !m_agent.isOnNavMesh)
            {
                m_agent.enabled = false;
                transform.position = previousPosition;
                return false;
            }
            m_navigator?.ResumeAfterRagdollServer();
            return true;
        }

        private bool TryFindRecoveryPosition(Vector3 ground, out Vector3 position)
        {
            position = default;
            bool fallback = Time.time >= m_groundedSince + m_recoveryFallbackDelay;
            Vector3 point = new Vector3(m_ragdoll != null ? m_ragdoll.Body.position.x : ground.x,
                ground.y, m_ragdoll != null ? m_ragdoll.Body.position.z : ground.z);
            if (TryGetNavigationPosition(point, fallback ? m_recoverySearchRadius : 1f, out position)) return true;
            if (!fallback) return false;
            // Being off the baked NavMesh must not keep a living thief unconscious.
            // Physical support and capsule clearance, rather than a timeout alone,
            // decide whether it is safe to stand up.
            if (TryStandingPosition(point, out position) || TryStandingPosition(ground, out position)) return true;
            for (int ring = 1; ring <= 3; ring++)
            for (int step = 0; step < 8; step++)
            {
                float angle = step * Mathf.PI / 4f;
                Vector3 candidate = ground + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) *
                    (m_recoverySearchRadius * ring / 3f);
                if (TryStandingPosition(candidate, out position)) return true;
            }
            return false;
        }

        private bool TryStandingPosition(Vector3 candidate, out Vector3 position) =>
            NpcImpactPhysics.TryStandingPosition(transform, m_collider, candidate, out position);

        private bool HasGroundSupport()
        {
            // Probe the feet, not the NavMesh: an agent may still report being on a
            // NavMesh after its physical support has gone or it has been lifted.
            Bounds bounds = m_collider != null && m_collider.enabled
                ? m_collider.bounds : new Bounds(transform.position + Vector3.up * .9f, new Vector3(.5f, 1.8f, .5f));
            float radius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * .65f, .04f, .2f);
            Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + radius + .08f, bounds.center.z);
            float supportDistance = m_agent != null && m_agent.enabled && m_agent.isOnNavMesh
                ? Mathf.Max(m_groundSupportDistance, m_navigationSurfaceTolerance) : m_groundSupportDistance;
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, radius, Vector3.down,
                         .08f + supportDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < .65f) continue;
                return true;
            }
            return false;
        }

        private void BeginAirborneRagdoll()
        {
            if (!IsAlive || m_ragdoll == null || m_ragdollState.Value != 0) return;
            Vector3 velocity = m_knockbackVelocity;
            if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
                velocity += m_agent.velocity;
            // Replicate the initial velocity before the state transition so remote
            // clients start in the same airborne pose, then follow the server root.
            m_airborneImpulse.Value = velocity;
            m_airborneSpin.Value = CreateAirborneSpin(velocity);
            m_groundedSince = 0f;
            m_nextRecoveryAttempt = 0f;
            m_nextNavigationAttachAttempt = 0f;
            m_lastGroundContactAt = 0f;
            m_recoverAfter = Time.time + .2f;
            m_ragdollState.Value = 1;
            ApplyRagdollState();
        }

        private Vector3 CreateAirborneSpin(Vector3 velocity)
        {
            return NpcImpactPhysics.CreateAirborneSpin(velocity, transform.forward, m_airborneFlipSpeed);
        }

        private void ApplyRagdollState()
        {
            bool fallen = m_ragdollState.Value != 0 || !IsAlive;
            if (m_agent != null)
            {
                if (fallen || !IsServerInitialized) m_agent.enabled = false;
                else TryAttachNavigation(.5f);
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
                m_ragdoll.Fall(m_deathRagdollApplied ? m_deathImpulse.Value : m_airborneImpulse.Value,
                    m_deathRagdollApplied);
                m_ragdoll.AddTumble(m_airborneSpin.Value);
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
            if (!HasGroundSupport())
            {
                BeginAirborneRagdoll();
                if (m_ragdollState.Value != 0) return;
            }
            RetryNavigationAfterRecovery();
            if (Time.time < m_resumeNavigationAt)
            {
                float damping = m_settings != null ? m_settings.Health.KnockbackDamping : NpcImpactPhysics.KnockbackDamping;
                Vector3 displacement = NpcImpactPhysics.StepKnockback(ref m_knockbackVelocity, damping, Time.deltaTime);
                if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh) m_agent.Move(displacement);
                else transform.position += displacement;
            }
            else if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh && m_agent.isStopped &&
                (m_navigator == null || !m_navigator.IsAlerting)) m_agent.isStopped = false;
        }

        private void RetryNavigationAfterRecovery()
        {
            if (m_agent == null || (m_agent.enabled && m_agent.isOnNavMesh) ||
                Time.time < m_resumeNavigationAt || Time.time < m_nextNavigationAttachAttempt) return;
            m_nextNavigationAttachAttempt = Time.time + .5f;
            TryAttachNavigation(m_recoverySearchRadius);
        }

        [Server]
        public void ApplyDamageServer(float damage, Vector3 impactDirection, float knockbackForce,
            CouchGuys.Player.PlayerHealth attacker = null)
        {
            bool wasAlive = IsAlive;
            ApplyImpactServer(damage, impactDirection, knockbackForce, 0f, false,
                ragdollImpulseScale: m_damageRagdollImpulseScale);
            if (wasAlive && IsAlive && damage > 0f && attacker != null)
                m_navigator?.ReactToAttackServer(attacker);
        }

        [Server]
        public void ApplyBulletDamageServer(float damage, Vector3 direction, float force, Vector3 position,
            CouchGuys.Player.PlayerHealth attacker = null)
        {
            if (!IsAlive || damage <= 0f) return;
            PlayBloodObserversRpc(position, direction, false);
            ApplyDamageServer(damage, direction, force, attacker);
        }

        [ObserversRpc]
        private void PlayBloodObserversRpc(Vector3 position, Vector3 direction, bool vehicle) =>
            CouchGuys.Gameplay.Weapons.BloodBurst.Play(position, direction, vehicle);

        [Server]
        public void ApplyVehicleImpactServer(float damage, Vector3 impactDirection, float knockbackForce,
            float upwardForce, bool directionalKnockback = false, Vector3? impactPosition = null)
        {
            if (IsAlive && damage > 0f)
                PlayBloodObserversRpc(impactPosition ?? transform.position + Vector3.up * 0.7f,
                    Vector3.up + impactDirection.normalized * 0.35f, true);
            ApplyImpactServer(damage, impactDirection, knockbackForce, upwardForce, true, directionalKnockback);
        }

        /// <summary>Launches survivors using the shared airborne ragdoll and landing recovery; deaths retain full blast impulse.</summary>
        [Server]
        public void ApplyExplosionDamageServer(float damage, Vector3 direction, float force, float lift,
            CouchGuys.Player.PlayerHealth attacker = null)
        {
            if (!IsAlive || damage <= 0f) return;
            ApplyImpactServer(damage, direction, force, lift, false, true);
            if (!IsAlive) return;
            m_grappleHeld = false;
            m_groundedSince = 0f;
            m_recoverAfter = Time.time + 1f;
            if (m_ragdoll != null && m_ragdollState.Value == 0)
            {
                BeginAirborneRagdoll();
                m_recoverAfter = Time.time + 1f;
            }
            if (attacker != null) m_navigator?.ReactToAttackServer(attacker);
        }

        [Server]
        public void ApplyBatImpactServer(float damage, Vector3 direction, float force, float lift,
            CouchGuys.Player.PlayerHealth attacker)
        {
            if (!IsAlive) return;
            ApplyImpactServer(damage, direction, force, lift, true, true);
            if (!IsAlive) return;
            m_grappleHeld = false;
            m_groundedSince = 0f;
            m_recoverAfter = Time.time + 1f;
            if (m_ragdoll != null && m_ragdollState.Value == 0) BeginAirborneRagdoll();
            if (attacker != null) m_navigator?.ReactToAttackServer(attacker);
        }

        private void ApplyImpactServer(float damage, Vector3 impactDirection, float knockbackForce,
            float upwardForce, bool allowPushWithoutDamage, bool directionalKnockback = false,
            float ragdollImpulseScale = 1f)
        {
            if ((!allowPushWithoutDamage && damage <= 0f) ||
                (damage <= 0f && knockbackForce <= 0f && upwardForce <= 0f) || !IsAlive) return;
            float appliedDamage = Mathf.Max(0f, damage);
            m_currentHealth.Value = Mathf.Max(0f, m_currentHealth.Value - appliedDamage);
            Vector3 impulse = NpcImpactPhysics.Impulse(impactDirection, knockbackForce, upwardForce);
            if (m_currentHealth.Value <= 0f)
            {
                CouchGuys.Gameplay.Healing.HealingItemCatalogue.DropFromThiefServer(transform.position);
                m_deathImpulse.Value = impulse * ragdollImpulseScale;
                m_airborneSpin.Value = impulse.y > .5f || m_ragdollState.Value != 0 || !HasGroundSupport()
                    ? CreateAirborneSpin(impulse) : Vector3.zero;
                m_ragdollState.Value = 2;
                m_dead.Value = true;
                float chance = m_settings != null ? m_settings.Health.WeaponDropChance : 35f;
                if (TryGetComponent(out EnemyWeapon weapon) && UnityEngine.Random.value * 100f < chance)
                    CouchGuys.Gameplay.Weapons.WeaponPickup.SpawnServer(
                        (CouchGuys.Gameplay.Weapons.PlayerWeaponType)weapon.WeaponType,
                        transform.position + Vector3.up, transform.rotation, Vector3.up * 1.5f);
                else if (weapon == null && TryGetComponent(out GrapplerThief _) && UnityEngine.Random.value * 100f < chance)
                    CouchGuys.Gameplay.Weapons.WeaponPickup.SpawnServer(
                        CouchGuys.Gameplay.Weapons.PlayerWeaponType.GrappleGun,
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
                if (impulse.y > .5f) BeginAirborneRagdoll();
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
