using FishNet.Object;
using CouchGuys.Player;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>
    /// Lightweight server-side navigation for the placeholder capsule thief.
    /// Replace or disable this when the production thief AI is imported.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public sealed class ThiefNavigator : NetworkBehaviour
    {
        [SerializeField] private NavMeshAgent m_agent;
        [SerializeField, Min(0.05f)] private float m_pathRefreshInterval = 0.35f;
        [SerializeField, Min(1f)] private float m_aimTurnSpeed = 540f;
        [SerializeField, Min(0.25f)] private float m_targetNavMeshSampleRadius = 6f;

        private PlayerHealth m_target;
        private EnemyWeapon m_weapon;
        private float m_nextPathRefreshTime;
        private float m_nextTargetSearchTime;

        private void Awake()
        {
            m_agent ??= GetComponent<NavMeshAgent>();
            m_weapon ??= GetComponent<EnemyWeapon>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (m_agent != null)
            {
                m_agent.enabled = true;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized && m_agent != null)
            {
                m_agent.enabled = false;
            }
        }

        public void SetTargetServer(PlayerHealth target, float stoppingDistance)
        {
            m_target = target;
            if (m_agent != null)
            {
                m_agent.stoppingDistance = Mathf.Max(0.25f, stoppingDistance);
            }

            m_nextPathRefreshTime = 0f;
        }

        private void Update()
        {
            if (!IsServerInitialized)
            {
                return;
            }

            if ((m_target == null || !m_target.IsAlive) &&
                Time.time >= m_nextTargetSearchTime)
            {
                m_nextTargetSearchTime = Time.time + m_pathRefreshInterval;
                SetTargetServer(FindNearestLivingTarget(), m_agent != null
                    ? m_agent.stoppingDistance
                    : 1.25f);
                m_weapon?.SetTargetServer(m_target);
            }

            if (m_agent == null || !m_agent.enabled ||
                !m_agent.isOnNavMesh || m_target == null || !m_target.IsAlive)
            {
                return;
            }

            Vector3 targetOffset = Vector3.ProjectOnPlane(
                m_target.transform.position - transform.position,
                Vector3.up);
            if (targetOffset.sqrMagnitude <= m_agent.stoppingDistance * m_agent.stoppingDistance &&
                targetOffset.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(targetOffset.normalized, Vector3.up),
                    m_aimTurnSpeed * Time.deltaTime);
            }

            if (Time.time < m_nextPathRefreshTime)
            {
                return;
            }

            m_nextPathRefreshTime = Time.time + m_pathRefreshInterval;
            if (!NavMesh.SamplePosition(
                    m_target.transform.position,
                    out NavMeshHit targetHit,
                    m_targetNavMeshSampleRadius,
                    m_agent.areaMask))
            {
                return;
            }

            // NavMeshAgent may remain in its arrived state after the target is
            // knocked out of the old stopping radius. Explicitly resume it before
            // updating the destination so displaced players are pursued immediately.
            m_agent.isStopped = false;
            m_agent.SetDestination(targetHit.position);
        }

        private PlayerHealth FindNearestLivingTarget()
        {
            PlayerHealth[] players = FindObjectsByType<PlayerHealth>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            PlayerHealth best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < players.Length; index++)
            {
                PlayerHealth candidate = players[index];
                if (candidate == null || !candidate.IsAlive ||
                    candidate.NetworkObject == null ||
                    !candidate.NetworkObject.IsSpawned)
                {
                    continue;
                }

                float distance =
                    (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_pathRefreshInterval = Mathf.Max(0.05f, m_pathRefreshInterval);
            m_aimTurnSpeed = Mathf.Max(1f, m_aimTurnSpeed);
            m_targetNavMeshSampleRadius = Mathf.Max(0.25f, m_targetNavMeshSampleRadius);
        }
#endif
    }
}
