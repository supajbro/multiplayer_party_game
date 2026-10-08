using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Player
{
    /// <summary>
    /// Server-authoritative teammate controller built on the original debug couch bot.
    /// It drives an unowned Player prefab through NavMesh navigation and the normal
    /// couch, weapon, health, animation, and NetworkTransform systems.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerCouchCarrier))]
    [RequireComponent(typeof(PlayerHealth))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class DebugCouchBotController : MonoBehaviour
    {
        public enum AiState
        {
            FollowPlayer,
            MoveToCouch,
            CarryCouch,
            Combat,
            Dead
        }

        private static readonly Dictionary<CouchCarryPoint, DebugCouchBotController>
            s_carryPointReservations = new();
        private static ThiefHealth[] s_cachedThieves;
        private static float s_nextThiefCacheRefreshTime;

        private NavMeshAgent m_agent;
        private PlayerCouchCarrier m_carrier;
        private PlayerHealth m_health;
        private PlayerWeaponController m_weapon;
        private NetworkObject m_networkObject;
        private DebugCouchBotSettings m_settings;
        private PlayerHealth m_leader;
        private CouchCarryController m_targetCouch;
        private CouchCarryPoint m_targetPoint;
        private ThiefHealth m_combatTarget;
        private Vector3 m_externalVelocity;
        private float m_nextDestinationTime;
        private float m_nextGrabAttemptTime;
        private float m_nextEnemyScanTime;
        private float m_nextLeaderSearchTime;
        private int m_botIndex;
        private bool m_isInitialised;
        private bool m_weaponConfigured;
        private bool m_deadStateApplied;

        public bool IsAiTeammate => m_isInitialised;
        public int BotIndex => m_botIndex;
        public AiState State { get; private set; } = AiState.FollowPlayer;

        private void Awake()
        {
            ResolveReferences();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedCache()
        {
            s_carryPointReservations.Clear();
            s_cachedThieves = null;
            s_nextThiefCacheRefreshTime = 0f;
        }

        internal void Initialise(
            DebugCouchBotSettings settings,
            int botIndex,
            PlayerHealth leader)
        {
            if (settings == null)
            {
                return;
            }

            ResolveReferences();
            m_settings = settings;
            m_botIndex = Mathf.Max(0, botIndex);
            m_leader = leader;
            m_isInitialised = true;
            enabled = true;
            ConfigureAgent();
        }

        internal void ApplyExternalImpulseServer(Vector3 impulse)
        {
            if (m_isInitialised && m_health != null && m_health.IsAlive)
            {
                m_externalVelocity += impulse;
            }
        }

        private void Update()
        {
            if (!CanSimulate())
            {
                return;
            }

            if (!m_weaponConfigured)
            {
                m_weapon?.ConfigureAiWeaponServer(PlayerWeaponType.AssaultRifle);
                m_weaponConfigured = true;
            }

            if (!m_health.IsAlive)
            {
                EnterDeadState();
                return;
            }

            m_deadStateApplied = false;
            if (m_health.IsKnockedDown)
            {
                State = AiState.FollowPlayer;
                m_carrier.SetDebugBotMovementIntentServer(Vector3.zero);
                StopAgent();
                ApplyKnockback();
                return;
            }

            ResolveLivingLeader();
            CouchCarryController leaderCouch = GetLeaderCouch();
            if (leaderCouch != null)
            {
                m_combatTarget = null;
                if (m_carrier.IsCarrying)
                {
                    if (m_carrier.GetCarriedCouchServer() != leaderCouch)
                    {
                        m_carrier.ReleaseForAiServer();
                        m_targetPoint = null;
                        State = AiState.MoveToCouch;
                    }
                    else
                    {
                        State = AiState.CarryCouch;
                    }
                }
                else
                {
                    m_targetCouch = leaderCouch;
                    State = AiState.MoveToCouch;
                }
            }
            else
            {
                m_targetCouch = null;
                ReleaseTargetPointReservation();
                m_targetPoint = null;
                if (m_carrier.IsCarrying)
                {
                    m_carrier.ReleaseForAiServer();
                }

                RefreshCombatTarget();
                State = m_combatTarget != null ? AiState.Combat : AiState.FollowPlayer;
            }

            switch (State)
            {
                case AiState.MoveToCouch:
                    TickMoveToCouch();
                    break;
                case AiState.CarryCouch:
                    TickCarryCouch();
                    break;
                case AiState.Combat:
                    TickCombat();
                    break;
                default:
                    TickFollowPlayer();
                    break;
            }

            ApplyKnockback();
            UpdateFacing();
        }

        private bool CanSimulate()
        {
            return m_isInitialised && m_settings != null && m_networkObject != null &&
                   m_networkObject.IsServerInitialized && !m_networkObject.Owner.IsValid &&
                   m_health != null && m_carrier != null && m_agent != null;
        }

        private void TickFollowPlayer()
        {
            m_carrier.SetDebugBotMovementIntentServer(Vector3.zero);
            if (m_leader == null)
            {
                StopAgent();
                return;
            }

            Vector3 localOffset = m_botIndex switch
            {
                0 => new Vector3(-1f, 0f, -1f),
                1 => new Vector3(1f, 0f, -1f),
                _ => new Vector3(0f, 0f, -1.6f)
            };
            localOffset *= m_settings.FollowDistance;
            Vector3 destination = m_leader.transform.position +
                                  m_leader.transform.TransformDirection(localOffset);
            SetDestination(destination, m_settings.ApproachSpeed, m_settings.FollowDistance * 0.45f);
        }

        private void TickMoveToCouch()
        {
            if (m_targetCouch == null || !m_targetCouch.IsSpawned)
            {
                m_targetPoint = null;
                StopAgent();
                return;
            }

            if (!IsTargetPointValid())
            {
                ReleaseTargetPointReservation();
                m_targetPoint = FindAvailablePoint(m_targetCouch);
            }

            if (m_targetPoint == null)
            {
                Vector3 waitingOffset = Quaternion.Euler(0f, m_botIndex * 120f, 0f) *
                                        (Vector3.back * 2f);
                SetDestination(
                    m_targetCouch.transform.position + waitingOffset,
                    m_settings.ApproachSpeed,
                    0.75f);
                return;
            }

            Vector3 standPosition = CalculateStandPosition(m_targetPoint);
            SetDestination(standPosition, m_settings.ApproachSpeed, 0.15f);
            float grabDistance = Mathf.Max(0.7f, m_settings.PointStandOff + 0.45f);
            if ((transform.position - standPosition).sqrMagnitude <= grabDistance * grabDistance &&
                Time.unscaledTime >= m_nextGrabAttemptTime)
            {
                bool grabbed = m_carrier.TryGrabPointForDebugBotServer(
                    m_targetCouch,
                    m_targetPoint.PointIndex);
                m_nextGrabAttemptTime = Time.unscaledTime + m_settings.RetryDelay;
                if (grabbed)
                {
                    ReleaseTargetPointReservation();
                    State = AiState.CarryCouch;
                }
                else
                {
                    ReleaseTargetPointReservation();
                    m_targetPoint = null;
                }
            }
        }

        private void TickCarryCouch()
        {
            CouchCarryController couch = m_carrier.GetCarriedCouchServer();
            if (couch == null || couch != GetLeaderCouch())
            {
                m_carrier.ReleaseForAiServer();
                m_targetPoint = null;
                State = AiState.FollowPlayer;
                return;
            }

            PlayerCouchCarrier humanCarrier = couch.GetFirstHumanCarrierServer(m_carrier);
            Vector3 movementIntent = humanCarrier != null
                ? humanCarrier.GetServerMovementIntent()
                : Vector3.zero;
            movementIntent = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(movementIntent, Vector3.up),
                1f);
            m_carrier.SetDebugBotMovementIntentServer(movementIntent);

            CouchCarryPoint point = couch.GetPoint(m_carrier.CarriedPointIndex);
            if (point == null)
            {
                m_carrier.ReleaseForAiServer();
                return;
            }

            m_targetPoint = point;
            float speed = m_settings.CarryingSpeed *
                          m_carrier.CalculateCarryingSpeedMultiplier(couch);
            SetDestination(CalculateStandPosition(point), speed, 0.08f);
        }

        private void TickCombat()
        {
            if (!IsCombatTargetValid(m_combatTarget))
            {
                m_combatTarget = null;
                State = AiState.FollowPlayer;
                StopAgent();
                return;
            }

            Vector3 offset = Vector3.ProjectOnPlane(
                m_combatTarget.transform.position - transform.position,
                Vector3.up);
            float combatRange = m_settings.CombatRange;
            if (offset.sqrMagnitude > combatRange * combatRange)
            {
                SetDestination(
                    m_combatTarget.transform.position,
                    m_settings.ApproachSpeed,
                    combatRange * 0.8f);
                return;
            }

            StopAgent();
            FaceDirection(offset);
            m_weapon?.TryFireAtThiefServer(m_combatTarget);
        }

        private void RefreshCombatTarget()
        {
            if (IsCombatTargetValid(m_combatTarget) &&
                (m_combatTarget.transform.position - transform.position).sqrMagnitude <=
                m_settings.EnemyDetectionRadius * m_settings.EnemyDetectionRadius)
            {
                return;
            }

            m_combatTarget = null;
            if (Time.unscaledTime < m_nextEnemyScanTime)
            {
                return;
            }

            m_nextEnemyScanTime = Time.unscaledTime + m_settings.EnemyDetectionInterval;
            ThiefHealth[] thieves = GetCachedThieves();
            float bestDistance = m_settings.EnemyDetectionRadius * m_settings.EnemyDetectionRadius;
            for (int index = 0; index < thieves.Length; index++)
            {
                ThiefHealth thief = thieves[index];
                if (!IsCombatTargetValid(thief))
                {
                    continue;
                }

                float distance = (thief.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    m_combatTarget = thief;
                }
            }
        }

        private ThiefHealth[] GetCachedThieves()
        {
            if (s_cachedThieves == null || Time.unscaledTime >= s_nextThiefCacheRefreshTime)
            {
                s_cachedThieves = FindObjectsByType<ThiefHealth>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
                s_nextThiefCacheRefreshTime =
                    Time.unscaledTime + m_settings.EnemyDetectionInterval;
            }

            return s_cachedThieves;
        }

        private static bool IsCombatTargetValid(ThiefHealth target)
        {
            return target != null && target.isActiveAndEnabled && target.IsAlive &&
                   target.NetworkObject != null && target.NetworkObject.IsSpawned;
        }

        private CouchCarryPoint FindAvailablePoint(CouchCarryController couch)
        {
            for (int offset = 0; offset < CouchCarryController.MaximumCarryPoints; offset++)
            {
                int pointIndex = (m_botIndex + 1 + offset) %
                                 CouchCarryController.MaximumCarryPoints;
                CouchCarryPoint point = couch.GetPoint(pointIndex);
                if (point == null || !point.IsAvailable)
                {
                    continue;
                }

                if (s_carryPointReservations.TryGetValue(point, out DebugCouchBotController owner) &&
                    owner != null && owner != this && owner.m_health != null &&
                    owner.m_health.IsAlive)
                {
                    continue;
                }

                s_carryPointReservations[point] = this;
                return point;
            }

            return null;
        }

        private void ReleaseTargetPointReservation()
        {
            if (m_targetPoint != null &&
                s_carryPointReservations.TryGetValue(
                    m_targetPoint,
                    out DebugCouchBotController owner) &&
                owner == this)
            {
                s_carryPointReservations.Remove(m_targetPoint);
            }
        }

        private bool IsTargetPointValid()
        {
            return m_targetPoint != null && m_targetPoint.Couch == m_targetCouch &&
                   m_targetPoint.IsAvailable;
        }

        private Vector3 CalculateStandPosition(CouchCarryPoint point)
        {
            Vector3 awayFromCouch = Vector3.ProjectOnPlane(
                point.transform.position - point.Couch.CouchRigidbody.worldCenterOfMass,
                Vector3.up);
            if (awayFromCouch.sqrMagnitude < 0.001f)
            {
                awayFromCouch = -point.Couch.transform.forward;
            }

            float standOff = Mathf.Min(
                m_settings.PointStandOff,
                m_carrier.MaximumCarrySeparation);
            return point.transform.position + awayFromCouch.normalized * standOff;
        }

        private void SetDestination(Vector3 destination, float speed, float stoppingDistance)
        {
            if (!m_agent.enabled)
            {
                return;
            }

            if (!m_agent.isOnNavMesh &&
                NavMesh.SamplePosition(
                    transform.position,
                    out NavMeshHit currentHit,
                    8f,
                    m_agent.areaMask))
            {
                m_agent.Warp(currentHit.position);
            }

            if (!m_agent.isOnNavMesh)
            {
                return;
            }

            m_agent.speed = Mathf.Max(0.1f, speed);
            m_agent.stoppingDistance = Mathf.Max(0.05f, stoppingDistance);
            if (Time.unscaledTime < m_nextDestinationTime)
            {
                return;
            }

            m_nextDestinationTime =
                Time.unscaledTime + m_settings.DestinationUpdateInterval;
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, m_agent.areaMask))
            {
                m_agent.isStopped = false;
                m_agent.SetDestination(hit.position);
            }
        }

        private void StopAgent()
        {
            if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
            {
                m_agent.isStopped = true;
                m_agent.ResetPath();
            }
        }

        private void UpdateFacing()
        {
            if (State == AiState.Combat && m_combatTarget != null)
            {
                FaceDirection(m_combatTarget.transform.position - transform.position);
            }
            else if (State == AiState.CarryCouch && m_targetPoint != null)
            {
                FaceDirection(m_targetPoint.transform.position - transform.position);
            }
            else if (m_agent != null && m_agent.enabled)
            {
                FaceDirection(m_agent.desiredVelocity);
            }
        }

        private void FaceDirection(Vector3 direction)
        {
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude < 0.001f)
            {
                return;
            }

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction.normalized, Vector3.up),
                m_settings.TurnSpeed * Time.deltaTime);
        }

        private void ApplyKnockback()
        {
            if (m_externalVelocity.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
            {
                m_agent.Move(m_externalVelocity * Time.deltaTime);
            }

            m_externalVelocity = Vector3.MoveTowards(
                m_externalVelocity,
                Vector3.zero,
                8f * Time.deltaTime);
        }

        private CouchCarryController GetLeaderCouch()
        {
            if (m_leader == null || !m_leader.IsAlive ||
                !m_leader.TryGetComponent(out PlayerCouchCarrier carrier))
            {
                return null;
            }

            return carrier.GetCarriedCouchServer();
        }

        private void ResolveLivingLeader()
        {
            if (m_leader != null && m_leader.IsAlive && !m_leader.IsAiTeammate)
            {
                return;
            }

            if (Time.unscaledTime < m_nextLeaderSearchTime)
            {
                return;
            }

            m_nextLeaderSearchTime = Time.unscaledTime + 1f;
            PlayerHealth[] players = FindObjectsByType<PlayerHealth>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            float bestDistance = float.PositiveInfinity;
            m_leader = null;
            for (int index = 0; index < players.Length; index++)
            {
                PlayerHealth player = players[index];
                if (player == null || !player.IsAlive || player.IsAiTeammate ||
                    player.NetworkObject == null || !player.NetworkObject.IsSpawned ||
                    !player.Owner.IsValid)
                {
                    continue;
                }

                float distance = (player.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    m_leader = player;
                }
            }
        }

        private void EnterDeadState()
        {
            State = AiState.Dead;
            if (m_deadStateApplied)
            {
                return;
            }

            m_deadStateApplied = true;
            m_carrier.SetDebugBotMovementIntentServer(Vector3.zero);
            m_carrier.ReleaseForAiServer();
            m_combatTarget = null;
            m_targetCouch = null;
            ReleaseTargetPointReservation();
            m_targetPoint = null;
            StopAgent();
            if (m_agent != null)
            {
                m_agent.enabled = false;
            }
        }

        private void OnDestroy()
        {
            ReleaseTargetPointReservation();
        }

        private void ConfigureAgent()
        {
            m_agent = GetComponent<NavMeshAgent>();
            if (m_agent == null)
            {
                m_agent = gameObject.AddComponent<NavMeshAgent>();
            }

            if (m_agent == null)
            {
                Debug.LogError("AI teammate requires a NavMeshAgent.", this);
                enabled = false;
                return;
            }

            CharacterController characterController = GetComponent<CharacterController>();
            m_agent.radius = characterController != null
                ? Mathf.Max(0.1f, characterController.radius)
                : 0.4f;
            m_agent.height = characterController != null
                ? Mathf.Max(0.5f, characterController.height)
                : 1.8f;
            m_agent.baseOffset = 0f;
            m_agent.speed = m_settings.ApproachSpeed;
            m_agent.angularSpeed = m_settings.TurnSpeed;
            m_agent.acceleration = 18f;
            m_agent.autoBraking = true;
            m_agent.updateRotation = false;
            m_agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                m_agent.Warp(hit.position);
            }
        }

        private void ResolveReferences()
        {
            m_carrier ??= GetComponent<PlayerCouchCarrier>();
            m_health ??= GetComponent<PlayerHealth>();
            m_weapon ??= GetComponent<PlayerWeaponController>();
            m_networkObject ??= GetComponent<NetworkObject>();
        }
    }
}