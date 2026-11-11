using System;
using System.Collections.Generic;
using CouchGuys.Networking;
using CouchGuys.Input;
using CouchGuys.Gameplay.Weapons;
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
        private static readonly List<PlayerHealth> s_instances = new();

        [Header("Health")]
        [SerializeField, Min(1f)] private float m_maxHealth = 100f;
        [Tooltip("One damage event at or above this fraction of maximum health causes a knockdown.")]
        [SerializeField, Range(0.01f, 1f)] private float m_knockdownDamageFraction = 0.25f;

        [Header("Death And Respawn")]
        [SerializeField, Min(0.1f)] private float m_respawnDelay = 30f;
        [SerializeField, Min(0.1f)] private float m_reviveDuration = 3f;
        [SerializeField, Min(0.1f)] private float m_reviveRange = 1.8f;

        [Header("Knockdown")]
        [SerializeField, Min(0.1f)] private float m_knockdownDuration = 2.25f;
        [SerializeField, Min(0.1f)] private float m_ringRecoveryDuration = 0.45f;
        [Header("Ring Knockback")]
        [SerializeField, Range(0f, 1f)] private float m_ringKnockbackMultiplier = .45f;
        [SerializeField, Min(.1f)] private float m_ringKnockbackDamping = 12f;
        [SerializeField, Min(0f)] private float m_ringKnockbackMaxDistance = 3f;
        [SerializeField, Min(.05f)] private float m_ringKnockbackRadius = .4f;
        [Header("Knockdown Motion")]
        [SerializeField, Min(0f)] private float m_upwardImpulse = 2.5f;
        [SerializeField, Range(10f, 90f)] private float m_fallAngle = 78f;
        [SerializeField, Min(1f)] private float m_fallDegreesPerSecond = 360f;
        [SerializeField, Min(1f)] private float m_getUpDegreesPerSecond = 180f;
        [SerializeField] private Transform m_visualRoot;
        [SerializeField] private ThirdPersonPlayerController m_playerController;

        private readonly SyncVar<float> m_currentHealth = new();
        private readonly SyncVar<bool> m_knockedDown = new();
        private readonly SyncVar<Vector3> m_knockdownDirection = new(Vector3.forward);
        private readonly SyncVar<Vector3> m_ringAnchor = new();
        private readonly SyncVar<Vector3> m_ringReturnPosition = new();
        private readonly SyncVar<bool> m_returningToRing = new();
        private RobotHoverVFX m_hover;
        private DebugCouchBotController m_ringRecoveryBot;
        private bool m_localRingRecovery;
        private bool m_recoveryReported;
        private float m_ringRecoveryElapsed;
        private Vector3 m_ringRecoveryStart;
        private Vector3 m_ringKnockbackVelocity;
        private float m_ringKnockbackRemainingDistance;
        private readonly SyncVar<int> m_aiTeammateIndex = new(-1);

        private readonly SyncVar<float> m_downedRemaining = new();
        private readonly SyncVar<float> m_reviveProgress = new();
        private readonly SyncVar<NetworkObject> m_reviver = new();
        private PlayerHealth m_activeReviveTarget;
        private PlayerHealth m_localReviveTarget;
        private PlayerInputReader m_input;
        private PlayerCouchCarrier m_carrier;
        private PlayerTruckInteractor m_truck;
        private Vector3 m_deathPosition;
        private float m_reviveStartedAt, m_reviveLeaseUntil, m_nextReviveHeartbeat, m_nextStatePublish;
        private Quaternion m_visualBaseRotation;
        private float m_visualFallProgress;
        private float m_recoverAtServerTime;
        private float m_respawnAtServerTime = float.PositiveInfinity;
        private float m_nextRespawnAttempt;
        private RobotFaceExpressionController m_faceExpression;

        public event Action<float, float> HealthChanged;
        public event Action<bool> KnockdownChanged;
        public event Action Died;
        internal event Action DamageReceivedServer;
        public event Action Respawned;

        public static IReadOnlyList<PlayerHealth> Instances => s_instances;

        public bool CanHeal => IsAlive && !IsKnockedDown && CurrentHealth < MaximumHealth;

        [Server]
        public bool TryHealPercentageServer(float percentage)
        {
            if (!CanHeal || float.IsNaN(percentage) || float.IsInfinity(percentage) || percentage <= 0f) return false;
            m_currentHealth.Value = Mathf.Min(m_maxHealth, m_currentHealth.Value + m_maxHealth * Mathf.Clamp(percentage, 0f, 100f) / 100f);
            return true;
        }

        public float CurrentHealth => m_currentHealth.Value;
        public float MaximumHealth => m_maxHealth;
        public bool IsAlive => m_currentHealth.Value > 0f;
        public bool IsDowned => !IsAlive && m_knockedDown.Value;
        public bool HasPendingRespawn => IsDowned && !IsAiTeammate;
        public float DownedRemaining => m_downedRemaining.Value;
        public bool CanRevive => IsSpawned && IsAlive && !IsKnockedDown &&
            !(TryGetComponent(out PlayerHoverboardInteractor hoverboard) && hoverboard.IsMounted) &&
            ((IsAiTeammate && IsServerInitialized && !Owner.IsValid) ||
             (!IsAiTeammate && Owner.IsValid && Owner.IsActive)) &&
            (m_carrier == null || !m_carrier.IsCarrying) && (m_truck == null || m_truck.CurrentTruck == null);
        public bool HasReviveInteraction => FindReviveTarget() != null;
        public bool IsKnockedDown => m_knockedDown.Value;
        public bool IsEnemyTargetable => isActiveAndEnabled && IsAlive && !IsKnockedDown &&
            !IsReturningToRing && NetworkObject != null && NetworkObject.IsSpawned;
        public bool IsReturningToRing => m_returningToRing.Value;
        public Vector3 RingAnchor => m_ringAnchor.Value;
        public bool IsAiTeammate => m_aiTeammateIndex.Value >= 0;
        public int AiTeammateIndex => m_aiTeammateIndex.Value;
        public Vector3 AimPoint
        {
            get
            {
                PlayerTruckInteractor interactor = GetComponent<PlayerTruckInteractor>();
                return interactor != null && interactor.CurrentTruck != null
                    ? interactor.CurrentTruck.CameraTarget.position
                    : transform.position + Vector3.up * 0.9f;
            }
        }

        private void Awake()
        {
            if (!s_instances.Contains(this)) s_instances.Add(this);
            m_playerController ??= GetComponent<ThirdPersonPlayerController>();
            m_input = GetComponent<PlayerInputReader>();
            m_carrier = GetComponent<PlayerCouchCarrier>();
            m_truck = GetComponent<PlayerTruckInteractor>();
            m_faceExpression = GetComponent<RobotFaceExpressionController>();
            m_hover = GetComponent<RobotHoverVFX>();
            m_ringRecoveryBot = GetComponent<DebugCouchBotController>();
            m_visualRoot ??= transform.Find("Visual");
            if (m_visualRoot != null)
            {
                m_visualBaseRotation = m_visualRoot.localRotation;
            }

            m_currentHealth.OnChange += OnHealthChanged;
            m_knockedDown.OnChange += OnKnockedDownChanged;
            m_returningToRing.OnChange += OnReturningToRingChanged;
        }

        private void OnDestroy()
        {
            s_instances.Remove(this);
            m_currentHealth.OnChange -= OnHealthChanged;
            m_knockedDown.OnChange -= OnKnockedDownChanged;
            m_returningToRing.OnChange -= OnReturningToRingChanged;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstances() => s_instances.Clear();

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_currentHealth.Value = m_maxHealth;
            m_knockedDown.Value = false;
            m_returningToRing.Value = false;
            m_respawnAtServerTime = float.PositiveInfinity;
            m_nextRespawnAttempt = 0f;
            m_downedRemaining.Value = 0f;
            m_reviveProgress.Value = 0f;
            m_reviver.Value = null;
            m_activeReviveTarget = null;
            DebugCouchBotController bot = GetComponent<DebugCouchBotController>();
            m_aiTeammateIndex.Value = bot != null && bot.IsAiTeammate ? bot.BotIndex : -1;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyKnockdownState(m_knockedDown.Value);
            OnReturningToRingChanged(false, m_returningToRing.Value, false);
            HealthChanged?.Invoke(m_currentHealth.Value, m_maxHealth);
        }

        private void Update()
        {
            if (IsServerInitialized) SimulateRingKnockback(Time.deltaTime);
            UpdateReviveInput();
            if (IsServerInitialized) UpdateReviveServer();
            if (IsServerInitialized && HasPendingRespawn && Time.time >= m_respawnAtServerTime &&
                Time.time >= m_nextRespawnAttempt)
            {
                RespawnAtDepotServer();
            }
            else if (IsServerInitialized && IsAlive && m_knockedDown.Value &&
                     Time.time >= m_recoverAtServerTime)
            {
                BeginRingRecoveryServer();
            }

            UpdateKnockdownVisual();
        }

        private void StartRingKnockback(Vector3 impulse)
        {
            m_ringKnockbackVelocity = Vector3.ClampMagnitude(m_ringKnockbackVelocity +
                Vector3.ProjectOnPlane(impulse, Vector3.up) * m_ringKnockbackMultiplier, 8f);
            m_ringKnockbackRemainingDistance = m_ringKnockbackMaxDistance;
        }

        private void SimulateRingKnockback(float deltaTime)
        {
            if (!m_knockedDown.Value || m_returningToRing.Value ||
                m_ringKnockbackVelocity.sqrMagnitude < .001f || m_ringKnockbackRemainingDistance <= 0f)
            {
                m_ringKnockbackVelocity = Vector3.zero;
                return;
            }
            Vector3 movement = Vector3.ClampMagnitude(m_ringKnockbackVelocity * deltaTime, m_ringKnockbackRemainingDistance);
            if (movement.sqrMagnitude < .000001f) return;
            Vector3 start = m_ringReturnPosition.Value;
            float distance = movement.magnitude;
            Vector3 direction = movement / distance;
            foreach (RaycastHit hit in Physics.SphereCastAll(start + Vector3.up * (m_ringKnockbackRadius + .1f),
                         m_ringKnockbackRadius, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y >= .65f) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - .02f));
            }
            Vector3 candidate = start + direction * distance;
            RaycastHit ground = default;
            float closest = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(candidate + Vector3.up * .4f, Vector3.down,
                         .8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < .65f || hit.distance >= closest) continue;
                ground = hit; closest = hit.distance;
            }
            if (closest == float.PositiveInfinity || distance <= .001f)
            {
                m_ringKnockbackVelocity = Vector3.zero;
                return;
            }
            Vector3 displacement = ground.point - start;
            // Move the detached ring and its eventual recovery destination together.
            // The airborne player's current transform never drives this motion.
            m_ringAnchor.Value += displacement;
            m_ringReturnPosition.Value += displacement;
            m_ringKnockbackRemainingDistance -= distance;
            m_ringKnockbackVelocity = Vector3.MoveTowards(m_ringKnockbackVelocity, Vector3.zero,
                m_ringKnockbackDamping * deltaTime);
        }

        [Server]
        private void BeginRingRecoveryServer()
        {
            if (m_returningToRing.Value) return;
            m_recoverAtServerTime = float.PositiveInfinity;
            m_returningToRing.Value = true;
        }

        private void OnReturningToRingChanged(bool previous, bool next, bool asServer)
        {
            if (!next)
            {
                if (m_localRingRecovery) m_playerController?.SetExternalControlPaused(false);
                m_localRingRecovery = false;
                return;
            }
            bool movementAuthority = IsOwner || (IsServerInitialized && !Owner.IsValid);
            if (!next || !movementAuthority || m_localRingRecovery) return;
            m_localRingRecovery = true;
            m_recoveryReported = false;
            m_ringRecoveryElapsed = 0f;
            m_ringRecoveryStart = transform.position;
            m_playerController?.SetExternalControlPaused(true);
        }

        private void LateUpdate()
        {
            if (!m_localRingRecovery) return;
            if (!m_returningToRing.Value)
            {
                m_localRingRecovery = false;
                m_playerController?.SetExternalControlPaused(false);
                return;
            }
            m_ringRecoveryElapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(m_ringRecoveryElapsed / m_ringRecoveryDuration);
            Vector3 position = Vector3.Lerp(m_ringRecoveryStart, m_ringReturnPosition.Value,
                Mathf.SmoothStep(0f, 1f, progress));
            m_playerController?.Teleport(position, transform.rotation);
            if (IsServerInitialized && !Owner.IsValid)
                m_ringRecoveryBot?.SetRingRecoveryPositionServer(position);
            if (progress < 1f || m_recoveryReported) return;
            m_recoveryReported = true;
            if (IsServerInitialized) FinishRingRecoveryServer();
            else FinishRingRecoveryServerRpc();
        }

        // Completion comes from the movement owner so network latency cannot unlock
        // controls before that owner reaches the ring. NetworkTransform carries the motion.
        [ServerRpc]
        private void FinishRingRecoveryServerRpc() => FinishRingRecoveryServer();

        [Server]
        private void FinishRingRecoveryServer()
        {
            if (!m_returningToRing.Value || !IsAlive) return;
            m_returningToRing.Value = false;
            m_knockedDown.Value = false;
        }

        public PlayerHealth FindReviveTarget()
        {
            if (!CanRevive) return null;
            PlayerHealth nearest = null;
            float distance = m_reviveRange * m_reviveRange;
            foreach (PlayerHealth target in s_instances)
            {
                if (target == null || target == this || !target.IsSpawned || !target.IsDowned ||
                    (!target.IsAiTeammate && target.DownedRemaining <= 0f) ||
                    (target.m_reviver.Value != null && target.m_reviver.Value != NetworkObject)) continue;
                float candidate = (target.transform.position - transform.position).sqrMagnitude;
                if (candidate > distance) continue;
                distance = candidate;
                nearest = target;
            }
            return nearest;
        }

        public string GetRevivePrompt(out float progress, out float duration)
        {
            PlayerHealth target = FindReviveTarget();
            duration = target != null ? target.m_reviveDuration : 0f;
            progress = target != null && target.m_reviver.Value == NetworkObject ? target.m_reviveProgress.Value : 0f;
            return target != null ? "Hold E to Revive" : null;
        }

        private void UpdateReviveInput()
        {
            if (!IsOwner || !IsClientInitialized) return;
            PlayerHealth target = m_input != null && m_input.isActiveAndEnabled && m_input.VehicleInteractHeld
                ? FindReviveTarget() : null;
            if (m_localReviveTarget != target)
            {
                if (m_localReviveTarget != null) ReleaseReviveServerRpc();
                m_localReviveTarget = target;
                m_nextReviveHeartbeat = 0f;
            }
            if (target == null || Time.unscaledTime < m_nextReviveHeartbeat) return;
            HoldReviveServerRpc(target.NetworkObject);
            m_nextReviveHeartbeat = Time.unscaledTime + 0.1f;
        }

        [ServerRpc]
        private void HoldReviveServerRpc(NetworkObject targetObject)
        {
            HoldReviveServer(targetObject);
        }

        [Server]
        internal void HoldReviveServer(NetworkObject targetObject)
        {
            if (!CanRevive || targetObject == null || !targetObject.IsSpawned ||
                !targetObject.TryGetComponent(out PlayerHealth target) || target == this || !target.IsDowned ||
                Time.time >= target.m_respawnAtServerTime ||
                (target.transform.position - transform.position).sqrMagnitude > m_reviveRange * m_reviveRange) return;
            if (target.m_reviver.Value != null && Time.time >= target.m_reviveLeaseUntil)
                target.ClearReviveServer();
            if (target.m_reviver.Value != null && target.m_reviver.Value != NetworkObject) return;
            if (m_activeReviveTarget != target) CancelOutgoingReviveServer();
            if (target.m_reviver.Value == null)
            {
                target.m_reviver.Value = NetworkObject;
                target.m_reviveStartedAt = Time.time;
                target.m_reviveProgress.Value = 0f;
            }
            m_activeReviveTarget = target;
            target.m_reviveLeaseUntil = Time.time + 0.5f;
        }

        [ServerRpc]
        private void ReleaseReviveServerRpc() => CancelOutgoingReviveServer();

        internal void CancelOutgoingReviveServer()
        {
            if (m_activeReviveTarget != null && m_activeReviveTarget.m_reviver.Value == NetworkObject)
                m_activeReviveTarget.ClearReviveServer();
            m_activeReviveTarget = null;
        }

        private void ClearReviveServer()
        {
            if (m_reviver.Value != null && m_reviver.Value.TryGetComponent(out PlayerHealth reviver) &&
                reviver.m_activeReviveTarget == this) reviver.m_activeReviveTarget = null;
            m_reviver.Value = null;
            m_reviveProgress.Value = 0f;
        }

        private void UpdateReviveServer()
        {
            if (!IsDowned) return;
            PlayerHealth reviver = null;
            bool claimed = m_reviver.Value != null;
            if (claimed) m_reviver.Value.TryGetComponent(out reviver);
            bool valid = claimed && reviver != null && reviver.CanRevive &&
                Time.time < m_reviveLeaseUntil &&
                (reviver.transform.position - transform.position).sqrMagnitude <= reviver.m_reviveRange * reviver.m_reviveRange;
            if (!valid) ClearReviveServer();
            // Timeout wins when both deadlines are crossed in the same server frame.
            if (valid && Time.time < m_respawnAtServerTime && Time.time >= m_reviveStartedAt + m_reviveDuration)
            {
                ClearReviveServer();
                m_respawnAtServerTime = float.PositiveInfinity;
                m_downedRemaining.Value = 0f;
                m_currentHealth.Value = m_maxHealth * 0.5f;
                BeginRingRecoveryServer();
                return;
            }
            if (Time.time < m_nextStatePublish) return;
            m_nextStatePublish = Time.time + 0.1f;
            m_downedRemaining.Value = IsAiTeammate ? 0f : Mathf.Max(0f, m_respawnAtServerTime - Time.time);
            m_reviveProgress.Value = valid ? Mathf.Min(m_reviveDuration, Time.time - m_reviveStartedAt) : 0f;
        }

        public override void OnStopServer()
        {
            CancelOutgoingReviveServer();
            ClearReviveServer();
            base.OnStopServer();
        }

        [Server]
        public void ApplyDamageServer(float damage, Vector3 impactDirection, float knockbackForce)
        {
            ApplyDamageInternal(damage, impactDirection, knockbackForce, false);
        }

        /// <summary>Applies damage while preserving the complete supplied horizontal impact force.</summary>
        [Server]
        public void ApplyDamageWithFullKnockbackServer(
            float damage,
            Vector3 impactDirection,
            float knockbackForce)
        {
            ApplyDamageInternal(damage, impactDirection, knockbackForce, true);
        }

        /// <summary>Full blast launch, including lighter splash hits, through the existing damage/recovery path.</summary>
        [Server]
        public void ApplyExplosionDamageServer(float damage, Vector3 direction, float force, float lift)
        {
            ApplyDamageInternal(damage, direction, force, true, lift, false, true, true);
        }

        [Server]
        public void ApplyBatImpactServer(float damage, Vector3 direction, float force, float lift)
        {
            ApplyDamageInternal(damage, direction, force, true, lift, true, true, true, true);
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
            ApplyDamageInternal(
                damage, impactDirection, knockbackForce, true, upwardForce, true, directionalKnockback);
        }

        /// <summary>Non-damaging retaliation using the existing launch, knockdown and recovery path.</summary>
        [Server]
        internal void ApplyDispatcherRetaliationServer(Vector3 direction, float force)
        {
            ApplyDamageInternal(0f, direction, force, true, explicitUpwardForce: force * .45f,
                allowPushWithoutDamage: true, directionalKnockback: true, explosionLaunch: true,
                forceKnockdown: true);
            GetComponent<PlayerWeaponController>()?.CancelActionsForDownedServer();
        }

        private void ApplyDamageInternal(
            float damage,
            Vector3 impactDirection,
            float knockbackForce,
            bool useFullKnockback,
            float explicitUpwardForce = 0f,
            bool allowPushWithoutDamage = false,
            bool directionalKnockback = false,
            bool explosionLaunch = false,
            bool batLaunch = false,
            bool forceKnockdown = false)
        {
            if ((!allowPushWithoutDamage && damage <= 0f) ||
                (damage <= 0f && knockbackForce <= 0f && explicitUpwardForce <= 0f) ||
                !IsAlive)
            {
                return;
            }

            float appliedDamage = Mathf.Min(Mathf.Max(0f, damage), m_currentHealth.Value);
            m_currentHealth.Value = Mathf.Max(0f, m_currentHealth.Value - appliedDamage);
            if (appliedDamage > 0f) DamageReceivedServer?.Invoke();
            bool died = !IsAlive;
            Vector3 horizontalDirection = Vector3.ProjectOnPlane(impactDirection, Vector3.up);
            if (horizontalDirection.sqrMagnitude < 0.001f)
            {
                horizontalDirection = transform.forward;
            }

            horizontalDirection.Normalize();
            bool severeHit = forceKnockdown || appliedDamage >= m_maxHealth * m_knockdownDamageFraction || died;
            float appliedForce = severeHit || useFullKnockback
                ? knockbackForce
                : knockbackForce * 0.3f;
            Vector3 impulse = horizontalDirection * appliedForce;
            impulse += Vector3.up * Mathf.Max(0f, explicitUpwardForce);
            if (severeHit)
            {
                if (!m_knockedDown.Value)
                {
                    m_ringReturnPosition.Value = transform.position;
                    m_ringAnchor.Value = m_hover != null ? m_hover.RingWorldPosition : transform.position;
                }
                m_returningToRing.Value = false;
                impulse += Vector3.up * Mathf.Max(0f, m_upwardImpulse - explicitUpwardForce);
                m_knockdownDirection.Value = horizontalDirection;
                m_recoverAtServerTime = died
                    ? float.PositiveInfinity
                    : Time.time + m_knockdownDuration;
                m_knockedDown.Value = true;
                StartRingKnockback(horizontalDirection * appliedForce);
                if (died)
                {
                    m_truck?.DetachDownedServer();
                    GetComponent<PlayerHoverboardInteractor>()?.ReleaseServer(true);
                    m_deathPosition = transform.position;
                    m_nextRespawnAttempt = 0f;
                    m_downedRemaining.Value = m_respawnDelay;
                    GetComponent<PlayerWeaponController>()?.CancelActionsForDownedServer();
                    CancelOutgoingReviveServer();
                    m_respawnAtServerTime = IsAiTeammate
                        ? float.PositiveInfinity
                        : Time.time + m_respawnDelay;
                    GameplaySessionController.Instance?.EvaluateGameOverServer();
                    NotifyDiedObserversRpc();
                }
            }

            if (severeHit || directionalKnockback)
                GetComponent<PlayerCouchCarrier>()?.ReleaseForDamageServer();

            if (Owner.IsValid)
            {
                ApplyImpactTargetRpc(Owner, impulse, directionalKnockback, explosionLaunch, batLaunch);
            }
            else
            {
                if (explosionLaunch) GetComponent<DebugCouchBotController>()?.ApplyEnemyGrappleThrowServer(impulse, 2f);
                else GetComponent<DebugCouchBotController>()?.ApplyExternalImpulseServer(impulse, directionalKnockback);
            }

            if (appliedDamage > 0f) PlayHitObserversRpc();
        }

        private float m_enemyGrappleAvailableAt;
        internal bool CanBeEnemyGrappled => Time.time >= m_enemyGrappleAvailableAt;

        [Server]
        internal bool TryClaimEnemyGrappleServer(float protectionDuration)
        {
            if (!IsAlive || IsKnockedDown || !CanBeEnemyGrappled) return false;
            m_enemyGrappleAvailableAt = Time.time + Mathf.Max(1f, protectionDuration);
            return true;
        }

        [Server]
        internal void ApplyEnemyGrappleThrowServer(Vector3 velocity, float duration, float drag)
        {
            if (!IsAlive || IsKnockedDown) return;
            GetComponent<PlayerCouchCarrier>()?.ReleaseForDamageServer();
            if (Owner.IsValid) ApplyEnemyGrappleThrowTargetRpc(Owner, velocity, duration, drag);
            else GetComponent<DebugCouchBotController>()?.ApplyEnemyGrappleThrowServer(velocity, drag);
        }

        [TargetRpc]
        private void ApplyEnemyGrappleThrowTargetRpc(NetworkConnection connection, Vector3 velocity, float duration, float drag)
        {
            if (IsOwner && IsAlive && !IsKnockedDown)
                m_playerController?.ApplyEnemyGrappleThrow(velocity, duration, drag);
        }

        // Server chooses every pull. The owning movement motor applies it once,
        // just like impacts, and its existing NetworkTransform replicates movement.
        [Server]
        internal void ApplyGrappleFlickServer(Vector3 impulse, float maximumSpeed, float duration, float drag, float gravity)
        {
            if (!IsAlive) return;
            GetComponent<PlayerCouchCarrier>()?.ReleaseForDamageServer();
            if (Owner.IsValid) ApplyGrappleFlickTargetRpc(Owner, impulse, maximumSpeed, duration, drag, gravity);
            else GetComponent<DebugCouchBotController>()?.ApplyGrappleFlickServer(impulse, maximumSpeed, drag, gravity);
        }

        [TargetRpc]
        private void ApplyGrappleFlickTargetRpc(NetworkConnection connection, Vector3 impulse,
            float maximumSpeed, float duration, float drag, float gravity)
        {
            if (IsOwner && IsAlive) m_playerController?.ApplyGrappleFlick(impulse, maximumSpeed, duration, drag, gravity);
        }

        [Server]
        internal void ApplyGrapplePullServer(Vector3 direction, float acceleration, float speed, float deltaTime)
        {
            if (!IsAlive) return;
            if (Owner.IsValid) ApplyGrapplePullTargetRpc(Owner, direction, acceleration, speed, deltaTime);
            else GetComponent<DebugCouchBotController>()?.ApplyGrapplePullServer(direction, acceleration, speed, deltaTime);
        }

        [TargetRpc]
        private void ApplyGrapplePullTargetRpc(NetworkConnection connection, Vector3 direction,
            float acceleration, float speed, float deltaTime)
        {
            if (IsOwner && IsAlive) m_playerController?.ApplyGrapplePull(direction, acceleration, speed, deltaTime);
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayHitObserversRpc()
        {
            GetComponent<CouchGuyAnimationDriver>()?.PlayHitReaction();
            m_faceExpression ??= GetComponent<RobotFaceExpressionController>();
            m_faceExpression?.NotifyDamage();
        }

        [TargetRpc]
        private void ApplyImpactTargetRpc(NetworkConnection connection, Vector3 impulse, bool vehicleImpact, bool explosionLaunch, bool batLaunch)
        {
            if (IsOwner)
            {
                if (batLaunch) m_playerController?.ApplyBatLaunch(impulse);
                else if (explosionLaunch) m_playerController?.ApplyEnemyGrappleThrow(impulse, 1.25f, 2f);
                else m_playerController?.ApplyExternalImpulse(impulse, vehicleImpact);
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
                m_nextRespawnAttempt = Time.time + 0.1f;
                return;
            }

            // Successful drops consume their slots; failed spawns retain items for retry.
            PlayerWeaponController weapons = GetComponent<PlayerWeaponController>();
            if (weapons != null && !weapons.DropInventoryForRespawnServer(m_deathPosition))
            {
                m_nextRespawnAttempt = Time.time + 1f;
                return;
            }
            ClearReviveServer();
            m_downedRemaining.Value = 0f;
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
            m_returningToRing.Value = false;
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

            bool falling = m_knockedDown.Value && !m_returningToRing.Value;
            float speed = falling ? m_fallDegreesPerSecond :
                m_returningToRing.Value ? m_fallAngle / m_ringRecoveryDuration : m_getUpDegreesPerSecond;
            float target = falling ? 1f : 0f;
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
            m_ringRecoveryDuration = Mathf.Max(0.1f, m_ringRecoveryDuration);
            m_respawnDelay = Mathf.Max(0.1f, m_respawnDelay);
            m_reviveDuration = Mathf.Max(0.1f, m_reviveDuration);
            m_reviveRange = Mathf.Max(0.1f, m_reviveRange);
        }
#endif
    }
}
