using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public sealed class ThiefNavigator : NetworkBehaviour
    {
        [Header("Navigation")]
        [SerializeField] private NavMeshAgent m_agent;
        [SerializeField, Min(0.05f)] private float m_pathRefreshInterval = 0.45f;
        [SerializeField, Min(0.1f)] private float m_tacticalRepositionInterval = 2.5f;
        [SerializeField, Min(0f)] private float m_repositionIntervalVariation = 1f;
        [SerializeField, Min(0.25f)] private float m_targetNavMeshSampleRadius = 6f;
        [SerializeField, Min(0.25f)] private float m_tacticalSampleRadius = 4f;
        [SerializeField, Min(0.1f)] private float m_targetMovementThreshold = 2.5f;
        [SerializeField, Min(1)] private int m_tacticalPositionAttempts = 4;
        [Header("Formation")]
        [SerializeField, Min(0f)] private float m_positionVariation = 1.25f;
        [SerializeField, Min(0f)] private float m_supportRangeMultiplier = 1.45f;
        [SerializeField, Min(0f)] private float m_minimumSlotSeparation = 1.75f;
        [SerializeField, Min(1f)] private float m_aimTurnSpeed = 260f;

        private PlayerHealth m_target;
        private EnemyWeapon m_weapon;
        private NavMeshPath m_path;
        private Vector3 m_lastTargetPosition;
        private Vector3 m_destination;
        private float m_nextPathTime;
        private float m_nextSearchTime;
        private float m_nextRepositionTime;
        private float m_slotAngle;
        private float m_rangeMultiplier = 1f;
        private int m_memberIndex;
        private bool m_hasDestination;

        private void Awake()
        {
            m_agent ??= GetComponent<NavMeshAgent>();
            m_weapon ??= GetComponent<EnemyWeapon>();
            m_path = new NavMeshPath();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (m_agent != null)
            {
                m_agent.enabled = true;
                m_agent.avoidancePriority = 30 + (m_memberIndex * 13) % 50;
            }
            m_nextPathTime = Time.time + Random.Range(0f, m_pathRefreshInterval);
            m_nextRepositionTime = Time.time + Random.Range(0f, m_tacticalRepositionInterval);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized && m_agent != null) m_agent.enabled = false;
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
            if (m_target != target) { m_hasDestination = false; m_nextRepositionTime = 0f; }
            m_target = target;
            if (m_agent != null) m_agent.stoppingDistance = Mathf.Max(0.25f, stoppingDistance * 0.72f);
            m_nextPathTime = 0f;
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            if ((m_target == null || !m_target.IsAlive) && Time.time >= m_nextSearchTime)
            {
                m_nextSearchTime = Time.time + m_pathRefreshInterval;
                SetTargetServer(FindNearestLivingTarget(), m_weapon != null ? m_weapon.PreferredRange : 1.25f);
                m_weapon?.SetTargetServer(m_target);
            }
            if (m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh || m_target == null || !m_target.IsAlive) return;

            Vector3 aim = Vector3.ProjectOnPlane(m_target.AimPoint - transform.position, Vector3.up);
            if (aim.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(aim.normalized), m_aimTurnSpeed * Time.deltaTime);

            if (Time.time < m_nextPathTime) return;
            m_nextPathTime = Time.time + m_pathRefreshInterval;
            Vector3 targetPosition = m_target.transform.position;
            if (!m_hasDestination || Time.time >= m_nextRepositionTime ||
                (targetPosition - m_lastTargetPosition).sqrMagnitude >= m_targetMovementThreshold * m_targetMovementThreshold)
                RecalculateDestination(targetPosition);
            if (m_hasDestination) { m_agent.isStopped = false; m_agent.SetDestination(m_destination); }
        }

        private void RecalculateDestination(Vector3 targetPosition)
        {
            m_lastTargetPosition = targetPosition;
            m_nextRepositionTime = Time.time + m_tacticalRepositionInterval +
                Random.Range(-m_repositionIntervalVariation, m_repositionIntervalVariation);
            Vector3 forward = Vector3.ProjectOnPlane(m_target.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = transform.position - targetPosition;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            float range = Mathf.Max(1f, m_weapon != null ? m_weapon.PreferredRange : m_agent.stoppingDistance) * m_rangeMultiplier;
            for (int i = 0; i < m_tacticalPositionAttempts; i++)
            {
                Vector3 radial = Quaternion.AngleAxis(m_slotAngle + (i == 0 ? 0f : Random.Range(-18f, 18f)), Vector3.up) * forward;
                Vector2 jitter = Random.insideUnitCircle * m_positionVariation;
                if (TryDestination(targetPosition + radial * range + new Vector3(jitter.x, 0f, jitter.y))) return;
            }
            if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, m_targetNavMeshSampleRadius, m_agent.areaMask) &&
                HasPath(hit.position)) { m_destination = hit.position; m_hasDestination = true; }
        }

        private bool TryDestination(Vector3 candidate)
        {
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, m_tacticalSampleRadius, m_agent.areaMask) ||
                (hit.position - m_target.transform.position).sqrMagnitude < m_minimumSlotSeparation * m_minimumSlotSeparation ||
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
            PlayerHealth[] players = FindObjectsByType<PlayerHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerHealth candidate = players[i];
                if (candidate == null || !candidate.IsAlive || candidate.NetworkObject == null || !candidate.NetworkObject.IsSpawned) continue;
                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            return best;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_pathRefreshInterval = Mathf.Max(0.05f, m_pathRefreshInterval);
            m_tacticalRepositionInterval = Mathf.Max(0.1f, m_tacticalRepositionInterval);
            m_repositionIntervalVariation = Mathf.Clamp(m_repositionIntervalVariation, 0f, m_tacticalRepositionInterval * 0.8f);
            m_targetNavMeshSampleRadius = Mathf.Max(0.25f, m_targetNavMeshSampleRadius);
            m_tacticalSampleRadius = Mathf.Max(0.25f, m_tacticalSampleRadius);
            m_tacticalPositionAttempts = Mathf.Max(1, m_tacticalPositionAttempts);
        }
#endif
    }
}
