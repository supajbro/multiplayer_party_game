using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Weapons;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Player
{
    /// <summary>Combat-only teammate adapter over the existing health, weapons and NavMeshAgent.</summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(PlayerHealth), typeof(PlayerWeaponController))]
    [DisallowMultipleComponent]
    public sealed class WheelieController : NetworkBehaviour
    {
        public struct CombatState
        {
            public bool Engaged, Reloading;
            public Vector3 AimPoint;
            public int Magazine, Reserve;
        }

        [SerializeField] private WheelieSettings m_settings;
        [SerializeField] private NavMeshAgent m_agent;
        [SerializeField] private PlayerHealth m_health;
        [SerializeField] private PlayerWeaponController m_weapon;
        private readonly SyncVar<NetworkObject> m_assignedPlayer = new();
        private readonly SyncVar<int> m_squadId = new();
        private readonly SyncVar<int> m_memberIndex = new();
        private readonly SyncVar<CombatState> m_combat = new();
        private NetworkObject m_pendingLeader;
        private int m_pendingSquad, m_pendingMember;
        private PlayerWeaponType m_startingWeapon;
        private bool m_weaponReady;
        private ThiefHealth m_target;
        private float m_reactAt, m_nextScan, m_nextDestination, m_nextPublish, m_deadAt = float.PositiveInfinity;
        private Vector3 m_impulse;
        private NavMeshPath m_path;
        private static int s_squadSequence;
        private static readonly List<WheelieController> s_instances = new();
        private static ThiefHealth[] s_thieves;
        private static float s_refreshThieves;
        public static IReadOnlyList<WheelieController> Instances => s_instances;
        public WheelieSettings Settings => m_settings;
        public NetworkObject AssignedPlayer => m_assignedPlayer.Value;
        public int SquadId => m_squadId.Value;
        public int MemberIndex => IsSpawned ? m_memberIndex.Value : m_pendingMember;
        internal int InitialMemberIndex => m_pendingMember;
        public CombatState Combat => m_combat.Value;
        public ThiefHealth ServerTarget => m_target;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() { s_instances.Clear(); s_thieves = null; s_refreshThieves = 0f; s_squadSequence = 0; }
        internal static int NextSquadId() => ++s_squadSequence;

        internal void Initialise(NetworkObject leader, int squad, int member, PlayerWeaponType weapon)
        {
            m_pendingLeader = leader; m_pendingSquad = squad; m_pendingMember = member; m_startingWeapon = weapon;
        }

        private void Awake()
        {
            m_agent ??= GetComponent<NavMeshAgent>(); m_health ??= GetComponent<PlayerHealth>();
            m_weapon ??= GetComponent<PlayerWeaponController>(); m_path = new NavMeshPath();
            m_agent.enabled = false;
            if (m_settings != null && TryGetComponent(out CapsuleCollider capsule))
            { capsule.radius = m_settings.AgentRadius; capsule.height = m_settings.AgentHeight; capsule.center = Vector3.up * m_settings.AgentHeight * .5f; }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (m_settings == null || m_pendingLeader == null) { ServerManager.Despawn(NetworkObject); return; }
            m_assignedPlayer.Value = m_pendingLeader; m_squadId.Value = m_pendingSquad; m_memberIndex.Value = m_pendingMember;
            m_agent.radius = m_settings.AgentRadius; m_agent.height = m_settings.AgentHeight;
            m_agent.baseOffset = 0f; m_agent.updateRotation = false; m_agent.updatePosition = true;
            m_agent.speed = m_settings.MovementSpeed; m_agent.acceleration = m_settings.Acceleration;
            m_agent.angularSpeed = m_settings.TurningSpeed; m_agent.autoBraking = true;
            m_agent.avoidancePriority = 35 + (m_pendingSquad * m_settings.SquadSize + m_pendingMember) % 40;
            m_agent.enabled = true; m_agent.Warp(transform.position);
            m_nextScan = Time.time + m_pendingMember * m_settings.EnemyScanInterval / m_settings.SquadSize;
            s_instances.Add(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) m_agent.enabled = false;
        }
        public override void OnStopServer() { s_instances.Remove(this); base.OnStopServer(); }
        private void OnDestroy() => s_instances.Remove(this);

        private void Update()
        {
            if (!IsServerInitialized || !IsSpawned || m_settings == null) return;
            NetworkObject leader = AssignedPlayer;
            // Never reassign to a nearer player after disconnect/despawn.
            if (leader == null || !leader.IsSpawned) { ServerManager.Despawn(NetworkObject); return; }
            if (!m_weaponReady && m_health.IsAlive)
            { m_weapon.ConfigureAiWeaponServer(m_startingWeapon); m_weaponReady = true; }
            if (!m_health.IsAlive)
            {
                Stop(); m_target = null;
                if (float.IsPositiveInfinity(m_deadAt)) m_deadAt = Time.time + m_settings.CorpseLifetime;
                Publish(false, transform.position);
                if (Time.time >= m_deadAt) ServerManager.Despawn(NetworkObject);
                return;
            }
            m_deadAt = float.PositiveInfinity;
            if (m_health.IsKnockedDown || m_health.IsReturningToRing)
            { Stop(); ApplyImpulse(); Publish(false, transform.position); return; }
            if (!m_agent.enabled || !m_agent.isOnNavMesh) return;
            Vector3 leaderPosition = leader.transform.position;
            if (Time.time >= m_nextScan)
            {
                m_nextScan = Time.time + m_settings.EnemyScanInterval;
                ScanEnemies(leaderPosition);
            }
            bool engaged = IsValidTarget(m_target, leaderPosition) &&
                Vector3.Distance(transform.position, leaderPosition) <= m_settings.MaximumDistanceFromLeader;
            Vector3 aim = engaged ? m_target.transform.position + Vector3.up * m_settings.HostileAimHeight : transform.position + transform.forward;
            if (engaged)
            {
                float range = Mathf.Min(m_settings.MaximumEngagementDistance, m_weapon.CurrentWeaponRange);
                bool sight = m_weapon.HasWheelieLineOfSight(m_target);
                float distance = Vector3.Distance(transform.position, m_target.transform.position);
                if (distance > range * m_settings.PreferredWeaponRangeFraction || !sight)
                {
                    Vector3 direction = Vector3.ProjectOnPlane(transform.position - m_target.transform.position, Vector3.up).normalized;
                    SetDestination(m_target.transform.position + direction * (range * m_settings.PreferredWeaponRangeFraction), leaderPosition);
                }
                else Stop();
                TurnTowards(aim - transform.position);
                if (sight && distance <= range && Time.time >= m_reactAt &&
                    Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(aim - transform.position, Vector3.up)) <= m_settings.AimAlignmentDegrees)
                    m_weapon.TryFireWheelieAtThiefServer(m_target);
            }
            else
            {
                m_target = null;
                Vector3 destination = FormationPosition(leaderPosition);
                if (Vector3.Distance(transform.position, destination) <= m_settings.ArrivalTolerance) Stop();
                else SetDestination(destination, leaderPosition);
                if (m_agent.velocity.sqrMagnitude > .01f) TurnTowards(m_agent.velocity);
            }
            float remaining = m_agent.hasPath ? Mathf.Max(0f, m_agent.remainingDistance - m_agent.stoppingDistance) : 0f;
            float speed = Mathf.Min(m_settings.MovementSpeed, Mathf.Sqrt(2f * m_settings.Deceleration * remaining));
            m_agent.acceleration = speed < m_agent.velocity.magnitude ? m_settings.Deceleration : m_settings.Acceleration;
            m_agent.speed = speed;
            ApplyImpulse(); Publish(engaged, aim);
        }

        private Vector3 FormationPosition(Vector3 leader)
        {
            float angle = (MemberIndex + .5f) * Mathf.PI * 2f / m_settings.SquadSize + SquadId * 2.39996323f;
            float radius = Mathf.Max(m_settings.FollowingDistance,
                m_settings.SquadMemberSpacing / (2f * Mathf.Sin(Mathf.PI / m_settings.SquadSize)));
            Vector3 result = leader + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            Vector3 separation = Vector3.zero;
            foreach (var other in s_instances)
            {
                if (other == null || other == this || !other.m_health.IsAlive) continue;
                Vector3 delta = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
                float distance = delta.magnitude;
                if (distance > .001f && distance < m_settings.SquadMemberSpacing)
                    separation += delta / distance * (m_settings.SquadMemberSpacing - distance);
            }
            return result + Vector3.ClampMagnitude(separation, m_settings.SquadMemberSpacing);
        }

        private void ScanEnemies(Vector3 leader)
        {
            if (s_thieves == null || Time.time >= s_refreshThieves)
            { s_thieves = FindObjectsByType<ThiefHealth>(FindObjectsSortMode.None); s_refreshThieves = Time.time + m_settings.EnemyScanInterval; }
            ThiefHealth best = null; float bestScore = float.PositiveInfinity;
            foreach (ThiefHealth candidate in s_thieves)
            {
                if (!IsValidTarget(candidate, leader) || !m_weapon.HasWheelieLineOfSight(candidate)) continue;
                var enemyWeapon = candidate.GetComponent<EnemyWeapon>();
                float score = Vector3.Distance(transform.position, candidate.transform.position);
                if (enemyWeapon != null && enemyWeapon.Target != null && enemyWeapon.Target.NetworkObject == AssignedPlayer)
                    score -= m_settings.ThreatPriorityBonus;
                if (candidate == m_target) score -= m_settings.ArrivalTolerance;
                if (score < bestScore) { bestScore = score; best = candidate; }
            }
            if (best != m_target) { m_target = best; m_reactAt = Time.time + m_settings.ReactionTime; }
        }

        private bool IsValidTarget(ThiefHealth target, Vector3 leader) => target != null && target.IsSpawned && target.IsAlive &&
            target.gameObject.scene == gameObject.scene &&
            Vector3.Distance(target.transform.position, transform.position) <= m_settings.EnemyDetectionRange &&
            Vector3.Distance(target.transform.position, leader) <= m_settings.MaximumDistanceFromLeader;

        private void SetDestination(Vector3 desired, Vector3 leader)
        {
            if (Time.time < m_nextDestination) return;
            m_nextDestination = Time.time + m_settings.DestinationUpdateInterval;
            Vector3 offset = Vector3.ClampMagnitude(desired - leader, m_settings.MaximumDistanceFromLeader);
            if (!NavMesh.SamplePosition(leader + offset, out NavMeshHit hit, m_settings.NavigationSampleRadius, m_agent.areaMask) ||
                Mathf.Abs(hit.position.y - desired.y) > m_settings.MaximumNavigationHeightDifference ||
                !m_agent.CalculatePath(hit.position, m_path) || m_path.status != NavMeshPathStatus.PathComplete) { Stop(); return; }
            // A reachable endpoint alone can still route a pursuit far around the leader.
            if (m_target != null)
                foreach (Vector3 corner in m_path.corners)
                    if (Vector3.Distance(corner, leader) > m_settings.MaximumDistanceFromLeader) { Stop(); return; }
            m_agent.stoppingDistance = m_settings.ArrivalTolerance;
            m_agent.isStopped = false; m_agent.SetPath(m_path);
        }
        private void Stop() { if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh) m_agent.isStopped = true; }
        private void TurnTowards(Vector3 direction)
        {
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), m_settings.TurningSpeed * Time.deltaTime);
        }
        internal void ApplyImpulseServer(Vector3 impulse) { if (IsServerInitialized) m_impulse += Vector3.ProjectOnPlane(impulse, Vector3.up); }
        private void ApplyImpulse()
        {
            if (m_agent.enabled && m_agent.isOnNavMesh && m_impulse.sqrMagnitude > .001f)
                m_agent.Move(m_impulse * Time.deltaTime);
            m_impulse = Vector3.MoveTowards(m_impulse, Vector3.zero, m_settings.ImpulseDamping * Time.deltaTime);
        }
        private void Publish(bool engaged, Vector3 aim)
        {
            if (Time.time < m_nextPublish) return;
            m_nextPublish = Time.time + m_settings.CombatStateInterval;
            m_combat.Value = new CombatState { Engaged = engaged, AimPoint = aim, Reloading = m_weapon.IsReloading,
                Magazine = m_weapon.CurrentMagazine, Reserve = m_weapon.CurrentReserve };
        }
    }
}
