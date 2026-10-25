using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(ThiefAudioEmitter))]
    [DisallowMultipleComponent]
    public sealed class ThiefNavigator : NetworkBehaviour, ICouchCarrierServer
    {
        [SerializeField] private ThiefSettings m_settings;
        [SerializeField] private NavMeshAgent m_agent;
        private float m_pathRefreshInterval => m_settings != null
            ? m_settings.Navigation.PathRefreshInterval : 0.45f;
        private float m_tacticalRepositionInterval => m_settings != null
            ? m_settings.Navigation.TacticalRepositionInterval : 2.5f;
        private float m_repositionIntervalVariation => m_settings != null
            ? m_settings.Navigation.RepositionIntervalVariation : 1f;
        private float m_targetNavMeshSampleRadius => m_settings != null
            ? m_settings.Navigation.TargetNavMeshSampleRadius : 6f;
        private float m_tacticalSampleRadius => m_settings != null
            ? m_settings.Navigation.TacticalSampleRadius : 4f;
        private float m_targetMovementThreshold => m_settings != null
            ? m_settings.Navigation.TargetMovementThreshold : 2.5f;
        private int m_tacticalPositionAttempts => m_settings != null
            ? m_settings.Navigation.TacticalPositionAttempts : 4;
        private float m_positionVariation => m_settings != null
            ? m_settings.Navigation.PositionVariation : 1.25f;
        private float m_supportRangeMultiplier => m_settings != null
            ? m_settings.Navigation.SupportRangeMultiplier : 1.45f;
        private float m_minimumSlotSeparation => m_settings != null
            ? m_settings.Navigation.MinimumSlotSeparation : 1.75f;
        private float m_aimTurnSpeed => m_settings != null
            ? m_settings.Navigation.AimTurnSpeed : 260f;
        private float m_alertChance => m_settings != null
            ? m_settings.Alerts.AlertChance : 25f;
        private float m_alertDistance => m_settings != null
            ? m_settings.Alerts.AlertDistance : 14f;
        private float m_alertResetDistance => m_settings != null
            ? m_settings.Alerts.AlertResetDistance : 20f;
        private float m_alertPauseDuration => m_settings != null
            ? m_settings.Alerts.AlertPauseDuration : 1.2f;
        private float m_alertCooldown => m_settings != null
            ? m_settings.Alerts.AlertCooldown : 10f;

        private readonly SyncVar<bool> m_alerting = new();
        private ThiefAudioEmitter m_audio;
        private bool m_alertEncounterChecked;
        private float m_alertUntil;
        private float m_nextAlertTime;
        public bool IsAlerting => m_alerting.Value;

        private const float CouchGrabDistance = 2.1f;
        private const float EscapeDistance = 20f;
        private const float ThreatDecisionDistance = 8f;
        private const int MaximumThiefCouchCarriers = 2;

        private PlayerHealth m_target;
        private PlayerHealth m_closestPlayer;
        private EnemyWeapon m_weapon;
        private ThiefHealth m_health;
        private DeliveryManager m_deliveryManager;
        private CouchCarryController m_carriedCouch;
        private int m_carriedPointIndex = -1;
        private NavMeshPath m_path;
        private Vector3 m_lastTargetPosition;
        private Vector3 m_destination;
        private Vector3 m_carryIntent;
        private float m_nextPathTime;
        private float m_nextSearchTime;
        private float m_nextPlayerSearchTime;
        private float m_nextRepositionTime;
        private float m_nextCouchSearchTime;
        private float m_nextCarryDecisionTime;
        private float m_fightUntil;
        private float m_slotAngle;
        private float m_rangeMultiplier = 1f;
        private int m_memberIndex;
        private bool m_hasDestination;
        private bool m_usingConstrainedCarryMovement;
        private float m_normalNavigationSpeed;
        private bool m_normalUpdateRotation;
        private Collider m_carrierCollider;
        private CouchCarryController m_collisionIgnoredCouch;

        NetworkObject ICouchCarrierServer.CarrierNetworkObject => NetworkObject;
        bool ICouchCarrierServer.IsCarrierAvailable => IsSpawned &&
            (m_health == null || m_health.IsAlive);

        private void Awake()
        {
            m_agent ??= GetComponent<NavMeshAgent>();
            m_weapon ??= GetComponent<EnemyWeapon>();
            m_health ??= GetComponent<ThiefHealth>();
            m_audio = GetComponent<ThiefAudioEmitter>();
            m_carrierCollider = GetComponent<Collider>();
            m_path = new NavMeshPath();
            if (m_agent != null)
            {
                ApplyMovementSettings();
                m_normalNavigationSpeed = m_agent.speed;
                m_normalUpdateRotation = m_agent.updateRotation;
            }
        }

        private void ApplyMovementSettings()
        {
            if (m_settings == null || m_agent == null) return;
            m_agent.speed = m_settings.Movement.Speed;
            m_agent.acceleration = m_settings.Movement.Acceleration;
            m_agent.angularSpeed = m_settings.Movement.AngularSpeed;
            m_agent.stoppingDistance = m_settings.Movement.StoppingDistance;
            m_agent.autoBraking = m_settings.Movement.AutoBraking;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_alerting.Value = false;
            m_alertEncounterChecked = false;
            m_alertUntil = 0f;
            m_nextAlertTime = 0f;
            m_deliveryManager = FindFirstObjectByType<DeliveryManager>();
            if (m_agent != null)
            {
                m_agent.enabled = true;
                m_agent.avoidancePriority = 30 + (m_memberIndex * 13) % 50;
            }
            m_nextPathTime = Time.time + Random.Range(0f, m_pathRefreshInterval);
            m_nextRepositionTime = Time.time + Random.Range(0f, m_tacticalRepositionInterval);
            m_nextCarryDecisionTime = Time.time + Random.Range(1.5f, 3.5f);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized && m_agent != null) m_agent.enabled = false;
            m_audio.StartLocal();
        }

        public override void OnStopClient()
        {
            m_audio.StopLocal();
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            m_alerting.Value = false;
            ReleaseCarriedCouchServer();
            SetConstrainedCarryMovement(false);
            UpdateCarriedCouchCollision(null);
            base.OnStopServer();
        }

        private void OnDestroy()
        {
            UpdateCarriedCouchCollision(null);
        }

        public void ConfigureTacticalSlotServer(int index, int groupSize)
        {
            m_memberIndex = Mathf.Max(0, index);
            if (index == 0) { m_slotAngle = 0f; m_rangeMultiplier = 0.82f; }
            else if (index == 1) { m_slotAngle = -85f; m_rangeMultiplier = 1f; }
            else if (index == 2) { m_slotAngle = 85f; m_rangeMultiplier = 1f; }
            else if (index == 3) { m_slotAngle = 180f; m_rangeMultiplier = m_supportRangeMultiplier; }
            else { m_slotAngle = 360f * index / Mathf.Max(1, groupSize); m_rangeMultiplier = 1f + (index % 2) * 0.15f; }
            m_hasDestination = false;
            m_nextRepositionTime = 0f;
        }

        public void SetTargetServer(PlayerHealth target, float stoppingDistance)
        {
            bool changed = m_target != target;
            if (changed) { m_hasDestination = false; m_nextRepositionTime = 0f; }
            m_target = target;
            if (m_agent != null) m_agent.stoppingDistance = Mathf.Max(0.25f, stoppingDistance * 0.72f);
            if (changed) m_nextPathTime = 0f;
        }

        private void Update()
        {
            if (!IsServerInitialized || (m_health != null && !m_health.IsAlive)) return;
            if (m_health != null && m_health.IsBeingKnockedBack)
            {
                m_alerting.Value = false;
                ReleaseCarriedCouchServer();
                SetConstrainedCarryMovement(false);
                m_carryIntent = Vector3.zero;
                return;
            }

            if (UpdateProximityAlert(GetClosestLivingTarget())) return;

            CouchCarryController couch = GetActiveCouch();
            if (m_carriedCouch != null && m_carriedCouch != couch)
                ReleaseCarriedCouchServer();

            if (couch != null && couch.IsLoaded)
            {
                ReleaseCarriedCouchServer();
                SetConstrainedCarryMovement(false);
                var truck = couch.LoadedTruckObject.GetComponent<CouchGuys.Gameplay.Vehicles.TruckController>();
                if (truck != null && !truck.IsDestroyed)
                {
                    m_weapon?.SetTruckTargetServer(truck);
                    if (m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh) return;
                    m_agent.stoppingDistance = Mathf.Max(0.25f,
                        (m_weapon != null ? m_weapon.PreferredRange : 1.25f) * 0.72f);
                    Vector3 aim = Vector3.ProjectOnPlane(truck.CameraTarget.position - transform.position, Vector3.up);
                    if (aim.sqrMagnitude > 0.001f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(aim), m_aimTurnSpeed * Time.deltaTime);
                    if (Time.time >= m_nextPathTime)
                    {
                        m_nextPathTime = Time.time + m_pathRefreshInterval;
                        if (!m_hasDestination || Time.time >= m_nextRepositionTime ||
                            (truck.transform.position - m_lastTargetPosition).sqrMagnitude >=
                            m_targetMovementThreshold * m_targetMovementThreshold)
                            RecalculateCombatDestination(truck.transform.position, truck.transform);
                        ApplyDestination();
                    }
                }
                else m_weapon?.SetTargetServer(null);
                return;
            }

            PlayerHealth closestPlayer = GetClosestLivingTarget();
            PlayerCouchCarrier humanCarrier = couch != null
                ? couch.GetPlayerCarrierServer(m_memberIndex)
                : null;

            if (m_carriedCouch != null)
                UpdateCarryingObjective(closestPlayer);
            else if (couch != null && humanCarrier == null && Time.time >= m_fightUntil)
                UpdateStealObjective(couch);
            else
                UpdateFightObjective(humanCarrier != null ? humanCarrier.GetComponent<PlayerHealth>() : closestPlayer);
        }

        private bool UpdateProximityAlert(PlayerHealth player)
        {
            if (IsAlerting)
            {
                if (Time.time < m_alertUntil)
                {
                    // Clear weapon targets throughout the pause, regardless of Update order.
                    m_weapon?.SetTargetServer(null);
                    m_carryIntent = Vector3.zero;
                    if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
                    {
                        m_agent.isStopped = true;
                        m_agent.velocity = Vector3.zero;
                    }
                    return true;
                }
                m_alerting.Value = false;
                m_hasDestination = false;
                m_nextPathTime = 0f;
                if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
                    m_agent.isStopped = false;
                return false;
            }

            float distanceSquared = player != null
                ? (player.transform.position - transform.position).sqrMagnitude
                : float.PositiveInfinity;
            if (distanceSquared > Mathf.Max(m_alertDistance, m_alertResetDistance) *
                Mathf.Max(m_alertDistance, m_alertResetDistance))
                m_alertEncounterChecked = false;
            // Do not interrupt an existing couch grip or a knockback recovery.
            if (m_carriedCouch != null || distanceSquared > m_alertDistance * m_alertDistance ||
                m_alertEncounterChecked || m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh)
                return false;

            // Roll once on entering range, including failed rolls and cooldown entries.
            m_alertEncounterChecked = true;
            if (Time.time < m_nextAlertTime || Random.value * 100f >= m_alertChance) return false;
            m_nextAlertTime = Time.time + m_alertCooldown;
            m_alertUntil = Time.time + Mathf.Max(0.1f, m_alertPauseDuration);
            m_alerting.Value = true;
            m_agent.isStopped = true;
            m_agent.ResetPath();
            m_agent.velocity = Vector3.zero;
            m_hasDestination = false;
            m_carryIntent = Vector3.zero;
            m_weapon?.SetTargetServer(null);
            PlayAlertObserversRpc();
            return true;
        }

        // No local prediction or buffered replay: each current observer, including host, hears one alert.
        [ObserversRpc]
        private void PlayAlertObserversRpc() => m_audio.PlayAlert();

        private CouchCarryController GetActiveCouch()
        {
            if (Time.time >= m_nextCouchSearchTime)
            {
                m_nextCouchSearchTime = Time.time + 0.75f;
                m_deliveryManager ??= FindFirstObjectByType<DeliveryManager>();
            }

            NetworkObject couchObject = m_deliveryManager != null ? m_deliveryManager.ActiveCouch : null;
            return couchObject != null && couchObject.IsSpawned &&
                   couchObject.TryGetComponent(out CouchCarryController couch)
                ? couch
                : null;
        }

        private void UpdateStealObjective(CouchCarryController couch)
        {
            if (couch.IsLoaded) return;
            m_weapon?.SetTargetServer(null);
            if (couch.ThiefCarrierCountServer() >= MaximumThiefCouchCarriers)
            {
                // Leave two points open so players can contest the couch while the
                // remaining thieves escort or screen the carriers.
                if ((m_memberIndex & 1) == 0) MoveTo(couch.transform.position, 3f);
                else UpdateFightObjective(GetClosestLivingTarget());
                return;
            }
            CouchCarryPoint point = FindBestAvailablePoint(couch);
            if (point == null)
            {
                // Split full squads into escort and combat roles rather than having
                // every thief crowd the same couch position.
                if ((m_memberIndex & 1) == 0) MoveTo(couch.transform.position, 3f);
                else UpdateFightObjective(GetClosestLivingTarget());
                return;
            }

            if ((point.transform.position - transform.position).sqrMagnitude <=
                CouchGrabDistance * CouchGrabDistance &&
                couch.TryGrabPointServer(this, point.PointIndex))
            {
                m_hasDestination = false;
                m_nextCarryDecisionTime = Time.time + Random.Range(1.5f, 3.5f);
                return;
            }

            MoveTo(point.transform.position, 0.65f);
        }

        private void UpdateCarryingObjective(PlayerHealth closestPlayer)
        {
            m_weapon?.SetTargetServer(null);
            float threatDistance = closestPlayer != null
                ? Vector3.Distance(transform.position, closestPlayer.transform.position)
                : float.PositiveInfinity;

            if (Time.time >= m_nextCarryDecisionTime)
            {
                m_nextCarryDecisionTime = Time.time + Random.Range(1.5f, 3.5f);
                bool contested = m_carriedCouch.HasHumanCarrierServer();
                float fightChance = contested ? 0.42f : 0.12f;
                fightChance += Mathf.InverseLerp(ThreatDecisionDistance, 1.5f, threatDistance) * 0.3f;
                fightChance += (m_memberIndex % 3) * 0.06f;
                if (closestPlayer != null && threatDistance <= ThreatDecisionDistance &&
                    Random.value < fightChance)
                {
                    ReleaseCarriedCouchServer();
                    m_fightUntil = Time.time + Random.Range(2.5f, 5f);
                    UpdateFightObjective(closestPlayer);
                    return;
                }
            }

            Vector3 escapeTarget = CalculateEscapeTarget();
            MoveTo(escapeTarget, 0.25f);
            Vector3 desired = m_agent != null && m_agent.enabled && m_agent.hasPath
                ? Vector3.ProjectOnPlane(m_agent.steeringTarget - transform.position, Vector3.up)
                : Vector3.ProjectOnPlane(escapeTarget - transform.position, Vector3.up);
            m_carryIntent = desired.sqrMagnitude > 0.01f ? desired.normalized : Vector3.zero;
            ApplyConstrainedCarryMovement(m_carryIntent);
        }

        private Vector3 CalculateEscapeTarget()
        {
            Vector3 couchPosition = m_carriedCouch.transform.position;
            Transform objective = m_deliveryManager != null && m_deliveryManager.ActiveDestination != null
                ? m_deliveryManager.ActiveDestination.DropPosition
                : null;
            Vector3 away = objective != null
                ? Vector3.ProjectOnPlane(couchPosition - objective.position, Vector3.up)
                : Vector3.ProjectOnPlane(couchPosition - transform.position, Vector3.up);
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away = Quaternion.AngleAxis((m_memberIndex % 3 - 1) * 12f, Vector3.up) * away.normalized;
            Vector3 candidate = couchPosition + away * EscapeDistance;
            return NavMesh.SamplePosition(candidate, out NavMeshHit hit, m_targetNavMeshSampleRadius, m_agent.areaMask)
                ? hit.position
                : candidate;
        }

        private void UpdateFightObjective(PlayerHealth target)
        {
            if (target == null || !target.IsAlive)
            {
                if (Time.time < m_nextSearchTime) return;
                m_nextSearchTime = Time.time + m_pathRefreshInterval;
                target = GetClosestLivingTarget();
            }

            SetTargetServer(target, m_weapon != null ? m_weapon.PreferredRange : 1.25f);
            m_weapon?.SetTargetServer(m_target);
            if (m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh || m_target == null) return;

            Vector3 aim = Vector3.ProjectOnPlane(m_target.AimPoint - transform.position, Vector3.up);
            if (aim.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(aim.normalized), m_aimTurnSpeed * Time.deltaTime);

            if (Time.time < m_nextPathTime) return;
            m_nextPathTime = Time.time + m_pathRefreshInterval;
            Vector3 targetPosition = m_target.transform.position;
            if (!m_hasDestination || Time.time >= m_nextRepositionTime ||
                (targetPosition - m_lastTargetPosition).sqrMagnitude >= m_targetMovementThreshold * m_targetMovementThreshold)
                RecalculateCombatDestination(targetPosition);
            ApplyDestination();
        }

        private void MoveTo(Vector3 targetPosition, float stoppingDistance)
        {
            if (m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh || Time.time < m_nextPathTime) return;
            m_nextPathTime = Time.time + m_pathRefreshInterval;
            m_agent.stoppingDistance = stoppingDistance;
            if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, m_targetNavMeshSampleRadius, m_agent.areaMask) &&
                HasPath(hit.position))
            {
                m_destination = hit.position;
                m_hasDestination = true;
                ApplyDestination();
            }
        }

        private void ApplyDestination()
        {
            if (!m_hasDestination) return;
            m_agent.isStopped = false;
            m_agent.SetDestination(m_destination);
        }

        private void ApplyConstrainedCarryMovement(Vector3 movementIntent)
        {
            SetConstrainedCarryMovement(true);
            if (m_carriedCouch == null || m_agent == null || !m_agent.enabled ||
                !m_agent.isOnNavMesh)
            {
                return;
            }

            CouchCarryPoint point = m_carriedCouch.GetPoint(m_carriedPointIndex);
            if (point == null)
            {
                ReleaseCarriedCouchServer();
                return;
            }

            Transform anchor = point.transform;
            Vector3 outwardDirection = Vector3.ProjectOnPlane(
                anchor.position - m_carriedCouch.transform.position,
                Vector3.up);
            float maximumDistance = m_carriedCouch.MaxCarryDistance;
            float minimumDistance = Mathf.Min(PlayerCouchCarrier.CarryDistance, maximumDistance);
            float comfortableDistance = Mathf.Clamp(
                maximumDistance * PlayerCouchCarrier.SoftConstraintStartRatio,
                minimumDistance,
                maximumDistance);
            Vector3 constrainedMovement = ThirdPersonPlayerController.ApplyDirectionalResistance(
                movementIntent,
                transform.position,
                anchor.position,
                outwardDirection,
                minimumDistance,
                comfortableDistance,
                maximumDistance,
                PlayerCouchCarrier.CouchPushingSpeedMultiplier,
                out _);

            float speed = m_carriedCouch.GetConfiguredCarrierMoveSpeedServer();
            Vector3 displacement = constrainedMovement * speed * Time.deltaTime;
            Vector3 attachmentCorrection = ThirdPersonPlayerController.CalculateAttachmentCorrection(
                transform.position + displacement,
                anchor.position,
                outwardDirection,
                minimumDistance,
                maximumDistance);
            // updatePosition is disabled while carrying, so discard the agent's own
            // simulated advance before applying the shared constrained displacement.
            m_agent.nextPosition = transform.position;
            m_agent.Move(displacement + attachmentCorrection);
            transform.position = m_agent.nextPosition;

            // NavMeshAgent.Move may trim a correction at a NavMesh edge. The grip
            // limit is authoritative, so finish any remainder before resynchronising
            // the agent's internal position.
            transform.position += ThirdPersonPlayerController.CalculateAttachmentCorrection(
                transform.position,
                anchor.position,
                outwardDirection,
                minimumDistance,
                maximumDistance);
            m_agent.nextPosition = transform.position;

            Vector3 facing = PlayerCouchCarrier.CalculateCarryFacingDirection(
                transform.position,
                anchor.position);
            if (facing.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(facing.normalized),
                    m_aimTurnSpeed * Time.deltaTime);
            }
        }

        private void SetConstrainedCarryMovement(bool constrained)
        {
            if (m_usingConstrainedCarryMovement == constrained)
            {
                return;
            }

            m_usingConstrainedCarryMovement = constrained;
            if (m_agent == null)
            {
                return;
            }

            if (constrained)
            {
                // Keep path/steering data available, but prevent the agent's own
                // simulation from advancing independently of the grip constraint.
                m_agent.speed = 0f;
                m_agent.updatePosition = false;
                m_agent.updateRotation = false;
                m_agent.nextPosition = transform.position;
                return;
            }

            if (m_agent.enabled)
            {
                m_agent.nextPosition = transform.position;
            }
            m_agent.updatePosition = true;
            m_agent.updateRotation = m_normalUpdateRotation;
            m_agent.speed = m_normalNavigationSpeed;
        }

        private CouchCarryPoint FindBestAvailablePoint(CouchCarryController couch)
        {
            CouchCarryPoint best = null;
            float bestDistance = float.PositiveInfinity;
            for (int offset = 0; offset < CouchCarryController.MaximumCarryPoints; offset++)
            {
                int index = (m_memberIndex + offset) % CouchCarryController.MaximumCarryPoints;
                CouchCarryPoint point = couch.GetPoint(index);
                if (point == null || !point.IsAvailable) continue;
                float distance = (point.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = point; }
            }
            return best;
        }

        private void RecalculateCombatDestination(Vector3 targetPosition, Transform target = null)
        {
            m_lastTargetPosition = targetPosition;
            m_nextRepositionTime = Time.time + m_tacticalRepositionInterval +
                Random.Range(-m_repositionIntervalVariation, m_repositionIntervalVariation);
            target = target != null ? target : m_target != null ? m_target.transform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = transform.position - targetPosition;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            float range = Mathf.Max(1f, m_weapon != null ? m_weapon.PreferredRange : m_agent.stoppingDistance) * m_rangeMultiplier;
            for (int i = 0; i < m_tacticalPositionAttempts; i++)
            {
                Vector3 radial = Quaternion.AngleAxis(m_slotAngle + (i == 0 ? 0f : Random.Range(-18f, 18f)), Vector3.up) * forward;
                Vector2 jitter = Random.insideUnitCircle * m_positionVariation;
                if (TryCombatDestination(targetPosition + radial * range + new Vector3(jitter.x, 0f, jitter.y))) return;
            }
            if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, m_targetNavMeshSampleRadius, m_agent.areaMask) &&
                HasPath(hit.position)) { m_destination = hit.position; m_hasDestination = true; }
        }

        private bool TryCombatDestination(Vector3 candidate)
        {
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, m_tacticalSampleRadius, m_agent.areaMask) ||
                (hit.position - m_lastTargetPosition).sqrMagnitude < m_minimumSlotSeparation * m_minimumSlotSeparation ||
                !HasPath(hit.position)) return false;
            m_destination = hit.position;
            m_hasDestination = true;
            return true;
        }

        private bool HasPath(Vector3 destination) =>
            m_agent.CalculatePath(destination, m_path) && m_path.status == NavMeshPathStatus.PathComplete;

        private PlayerHealth FindNearestLivingTarget()
        {
            PlayerHealth best = null;
            float bestDistance = float.PositiveInfinity;
            IReadOnlyList<PlayerHealth> players = PlayerHealth.Instances;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerHealth candidate = players[i];
                if (candidate == null || !candidate.isActiveAndEnabled || !candidate.IsAlive ||
                    candidate.NetworkObject == null || !candidate.NetworkObject.IsSpawned) continue;
                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            return best;
        }

        private PlayerHealth GetClosestLivingTarget()
        {
            if (m_closestPlayer == null || !m_closestPlayer.IsAlive ||
                Time.time >= m_nextPlayerSearchTime)
            {
                m_nextPlayerSearchTime = Time.time + m_pathRefreshInterval;
                m_closestPlayer = FindNearestLivingTarget();
            }
            return m_closestPlayer;
        }

        private void ReleaseCarriedCouchServer()
        {
            CouchCarryController couch = m_carriedCouch;
            int pointIndex = m_carriedPointIndex;
            if (couch != null) couch.ReleasePointServer(pointIndex, this);
            else
            {
                m_carriedCouch = null;
                m_carriedPointIndex = -1;
                m_carryIntent = Vector3.zero;
                SetConstrainedCarryMovement(false);
                UpdateCarriedCouchCollision(null);
            }
        }

        Vector3 ICouchCarrierServer.CalculateDesiredCarryPosition(Vector3 couchCentre)
        {
            Vector3 direction = Vector3.ProjectOnPlane(couchCentre - transform.position, Vector3.up);
            if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
            return transform.position + direction.normalized * PlayerCouchCarrier.CarryDistance +
                   Vector3.up * PlayerCouchCarrier.CarryHeight;
        }

        Vector3 ICouchCarrierServer.GetServerMovementIntent() => m_carryIntent;

        void ICouchCarrierServer.SetCarriedCouchServer(CouchCarryController couch, int pointIndex)
        {
            m_carriedCouch = couch;
            m_carriedPointIndex = pointIndex;
            m_carryIntent = Vector3.zero;
            m_hasDestination = false;
            m_nextPathTime = 0f;
            if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
            {
                m_agent.ResetPath();
            }
            UpdateCarriedCouchCollision(couch);
            SetConstrainedCarryMovement(true);
        }

        void ICouchCarrierServer.ClearCarriedCouchServer(CouchCarryController couch, int pointIndex)
        {
            if (m_carriedCouch != couch || m_carriedPointIndex != pointIndex) return;
            m_carriedCouch = null;
            m_carriedPointIndex = -1;
            m_carryIntent = Vector3.zero;
            SetConstrainedCarryMovement(false);
            UpdateCarriedCouchCollision(null);
        }

        private void UpdateCarriedCouchCollision(CouchCarryController couch)
        {
            if (couch == m_collisionIgnoredCouch)
            {
                return;
            }

            m_collisionIgnoredCouch?.SetCarrierCollisionIgnored(m_carrierCollider, false);
            m_collisionIgnoredCouch = couch;
            m_collisionIgnoredCouch?.SetCarrierCollisionIgnored(m_carrierCollider, true);
        }

    }
}
