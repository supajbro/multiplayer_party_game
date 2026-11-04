using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.ProceduralGeneration;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Player
{
    public sealed partial class DebugCouchBotController
    {
        [SerializeField] private AITeammateRoleSettings m_roleSettings;
        public AITeammateRoleSettings RoleSettings => m_roleSettings;
        private DeliveryManager m_deliveryManager;
        private bool m_autonomousCarry;
        private readonly Queue<float> m_damageTimes = new();

        private float m_defendUntil;
        private float m_stateLockedUntil;
        private PlayerHealth m_reviveTarget;
        private float m_reviveStarted;
        private readonly Dictionary<PlayerHealth, float> m_reviveRetryAt = new();
        private static readonly Dictionary<PlayerHealth, DebugCouchBotController> s_reviveClaims = new();
        private NavMeshPath m_deliveryPath;
        private static readonly Dictionary<CouchCarryController, DeliveryNavigation> s_deliveryNavigation = new();
        private sealed class DeliveryNavigation
        {
            internal readonly NavMeshPath Path = new();
            internal readonly CouchClearancePath Clearance = new();
            internal List<Vector3> Roads;
            internal int Waypoint;
            internal float NextRefresh;
            internal Vector3 Direction;
            internal bool CrossingLink;
            internal Vector3 LinkExit;
        }


        private void RecordDamage() => m_damageTimes.Enqueue(Time.time);

        public void SetRoleSettings(AITeammateRoleSettings settings) => m_roleSettings = settings;

        private bool TickRoleBehaviour()
        {
            m_autonomousCarry = false;
            if (m_roleSettings == null) return false; // Preserve legacy/debug pull modes.
            while (m_damageTimes.Count > 0 && m_damageTimes.Peek() < Time.time - m_roleSettings.CarrierDamageTimeWindow)
                m_damageTimes.Dequeue();
            bool sustainedDamage = m_damageTimes.Count >= m_roleSettings.CarrierDamageHitThreshold;
            if (sustainedDamage) m_defendUntil = Time.time + m_roleSettings.StateTransitionCooldown;
            ResolveLivingLeader();
            m_deliveryManager ??= FindFirstObjectByType<DeliveryManager>();
            bool humanSupport = m_roleSettings.Role == AITeammateRole.HumanSupport && m_leader != null;
            CouchCarryController objective = humanSupport ? GetLeaderCouch() :
                m_deliveryManager != null && m_deliveryManager.ActiveCouch != null &&
                m_deliveryManager.Deliveries[0].Status == DeliveryState.Transport
                    ? m_deliveryManager.ActiveCouch.GetComponent<CouchCarryController>() : null;

            if (TryTickRevival(humanSupport)) return true;
            Vector3 guardPosition = objective != null ? objective.transform.position :
                m_leader != null ? m_leader.transform.position : transform.position;
            bool canFight = m_roleSettings.Role == AITeammateRole.Combat || humanSupport ||
                            Time.time < m_defendUntil;
            if (canFight)
            {
                // Scan around the objective, then enforce the same leash while attacking.
                if (Time.unscaledTime >= m_nextEnemyScanTime)
                {
                    m_nextEnemyScanTime = Time.unscaledTime + m_settings.EnemyDetectionInterval;
                    m_combatTarget = null;
                    float nearest = m_roleSettings.CombatDetectionRange * m_roleSettings.CombatDetectionRange;
                    foreach (var thief in GetCachedThieves())
                    {
                        if (!IsCombatTargetValid(thief)) continue;
                        float distance = (thief.transform.position - guardPosition).sqrMagnitude;
                        if (distance < nearest) { nearest = distance; m_combatTarget = thief; }
                    }
                }
                if (IsCombatTargetValid(m_combatTarget) &&
                    Vector3.Distance(transform.position, guardPosition) <= m_roleSettings.MaximumCombatChaseDistance &&
                    Vector3.Distance(m_combatTarget.transform.position, guardPosition) <= m_roleSettings.MaximumCombatChaseDistance &&
                    (State == AiState.Combat || Time.time >= m_stateLockedUntil))
                {
                    InterruptCarry(AiState.Combat);
                    TickCombat();
                    return true;
                }
            }
            m_combatTarget = null;
            if (State == AiState.Combat) m_stateLockedUntil = Time.time + m_roleSettings.StateTransitionCooldown;
            if (humanSupport) return false; // Normal sled/couch/follow cooperation with the human.

            m_autonomousCarry = true;
            if (m_targetCouch != objective)
            {
                m_carrier.ReleaseForAiServer();
                ReleaseTargetPointReservation();
                m_targetPoint = null;
                m_targetCouch = objective;
            }
            if (objective == null)
            {
                State = AiState.FollowPlayer;
                TickFollowPlayer();
                return true;
            }
            State = m_carrier.GetCarriedCouchServer() == objective ? AiState.CarryCouch : AiState.MoveToCouch;
            if (State == AiState.CarryCouch) TickCarryCouch(); else TickMoveToCouch();
            return true;
        }

        private void InterruptCarry(AiState state)
        {
            m_carrier.SetDebugBotMovementIntentServer(Vector3.zero);
            m_carrier.ReleaseForAiServer();
            ReleaseTargetPointReservation();
            m_targetPoint = null;
            SetConstrainedCarryMovement(false);
            State = state;
        }

        private bool TryTickRevival(bool humansOnly)
        {
            if (m_roleSettings.RevivalPriority <= 0) return false;
            if (m_reviveTarget != null && (!m_reviveTarget.IsDowned ||
                Time.time - m_reviveStarted > m_roleSettings.ReviveAttemptTimeout ||
                !s_reviveClaims.TryGetValue(m_reviveTarget, out var owner) || owner != this))
            {
                m_reviveRetryAt[m_reviveTarget] = Time.time + m_roleSettings.ReviveRetryDelay;
                ReleaseReviveClaim();
            }
            if (m_reviveTarget == null)
            {
                float nearest = float.PositiveInfinity;
                foreach (var target in PlayerHealth.Instances)
                {
                    if (target == null || target == m_health || !target.IsSpawned || !target.IsDowned ||
                        (humansOnly && target.IsAiTeammate) ||
                        (m_reviveRetryAt.TryGetValue(target, out float retry) && Time.time < retry)) continue;
                    if (s_reviveClaims.TryGetValue(target, out var claimant) && claimant != null &&
                        claimant != this && claimant.m_health.IsAlive && claimant.m_roleSettings != null &&
                        claimant.m_roleSettings.RevivalPriority >= m_roleSettings.RevivalPriority) continue;
                    float distance = Vector3.Distance(transform.position, target.transform.position);
                    if (distance < nearest) { nearest = distance; m_reviveTarget = target; }
                }
                if (m_reviveTarget != null)
                {
                    s_reviveClaims[m_reviveTarget] = this;
                    m_reviveStarted = Time.time;
                }
            }
            if (m_reviveTarget == null) return false;
            InterruptCarry(AiState.Revive);
            SetDestination(m_reviveTarget.transform.position, m_settings.ApproachSpeed, 0.75f);
            m_health.HoldReviveServer(m_reviveTarget.NetworkObject);
            return true;
        }

        private void ReleaseReviveClaim()
        {
            if (m_reviveTarget != null && s_reviveClaims.TryGetValue(m_reviveTarget, out var owner) && owner == this)
                s_reviveClaims.Remove(m_reviveTarget);
            m_reviveTarget = null;
            if (m_health != null && m_health.IsServerInitialized) m_health.CancelOutgoingReviveServer();
        }

        private Vector3 GetDeliveryMovementIntent(CouchCarryController couch)
        {
            var destination = m_deliveryManager != null ? m_deliveryManager.GetDestinationForCouch(couch) : null;
            if (destination == null || destination.DropPosition == null) return Vector3.zero;
            if (!s_deliveryNavigation.TryGetValue(couch, out var navigation))
            {
                // Reuse generated road connectivity when the pedestrian bake has disconnected
                // islands at the depot entrance or a house driveway. Movement still uses the
                // ordinary character constraints and couch collision/physics.
                foreach (var previous in new List<CouchCarryController>(s_deliveryNavigation.Keys))
                    if (previous == null) s_deliveryNavigation.Remove(previous);
                navigation = new DeliveryNavigation();
                var generator = m_deliveryManager.GetComponent<NeighbourhoodGenerator>();
                var roads = new GetawayRoadRouter();
                roads.Build(generator);
                if (destination.RouteMetrics != null)
                    roads.TryRoute(couch.transform.position, destination.RouteMetrics.RoadCoordinate, out navigation.Roads);
                s_deliveryNavigation[couch] = navigation;
            }
            m_deliveryPath = navigation.Path;
            foreach (var teammate in PlayerHealth.Instances)
            {
                if (teammate == null || !teammate.IsAlive || !teammate.IsAiTeammate ||
                    !teammate.TryGetComponent(out DebugCouchBotController bot) || bot.m_targetCouch != couch ||
                    bot.State != AiState.MoveToCouch) continue;
                if (Vector3.Distance(teammate.transform.position, couch.transform.position) > m_roleSettings.FollowDistance * 4f)
                    return Vector3.zero;
            }
            if (navigation.CrossingLink)
            {
                Vector3 remaining = Vector3.ProjectOnPlane(navigation.LinkExit - couch.transform.position, Vector3.up);
                if (remaining.sqrMagnitude > 0.56f) return remaining.normalized;
                navigation.CrossingLink = false;
                navigation.NextRefresh = 0f;
            }
            if (Time.time < navigation.NextRefresh) return navigation.Direction;
            navigation.NextRefresh = Time.time + m_settings.DestinationUpdateInterval;
            Vector3 target = destination.DropPosition.position;
            if (navigation.Roads != null)
            {
                while (navigation.Waypoint < navigation.Roads.Count &&
                    GetawayRouteFollower.ReachedWaypoint(navigation.Roads, navigation.Waypoint,
                        couch.transform.position, 2f, 2f)) navigation.Waypoint++;
                if (navigation.Waypoint < navigation.Roads.Count) target = navigation.Roads[navigation.Waypoint];
            }
            navigation.Direction = Vector3.zero;
            Vector3[] corners = null;
            if (m_roleSettings.CouchNavigationSettings != null)
                navigation.Clearance.TryBuild(couch, target, m_roleSettings.CouchNavigationSettings,
                    5f, true, out corners, out _);
            if (corners == null && NavMesh.SamplePosition(couch.transform.position, out var start, 5f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(target, out var end, 5f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, navigation.Path))
                corners = navigation.Path.corners;
            if (corners != null)
            {
                // Finish the current off-mesh segment before repathing.
                for (int i = 0; i + 1 < corners.Length; i++)
                {
                    if (Vector3.ProjectOnPlane(corners[i] - couch.transform.position, Vector3.up).sqrMagnitude > 4f ||
                        !NavMesh.Raycast(corners[i], corners[i + 1], out _, NavMesh.AllAreas)) continue;
                    navigation.CrossingLink = true;
                    navigation.LinkExit = corners[i + 1];
                    return Vector3.ProjectOnPlane(navigation.LinkExit - couch.transform.position, Vector3.up).normalized;
                }
                for (int i = 1; i < corners.Length; i++)
                {
                    var delta = Vector3.ProjectOnPlane(corners[i] - couch.transform.position, Vector3.up);
                    if (delta.sqrMagnitude > 1f) { navigation.Direction = delta.normalized; break; }
                }
                // Cross short baked-mesh gaps along the already selected road route.
                // Collision continues to prevent movement through walls or scenery.
                if (navigation.Direction == Vector3.zero)
                    navigation.Direction = Vector3.ProjectOnPlane(target - couch.transform.position, Vector3.up).normalized;
            }
            return navigation.Direction;
        }
    }
}
