using FishNet.Object;
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

        private Transform m_target;
        private float m_nextPathRefreshTime;

        private void Awake()
        {
            m_agent ??= GetComponent<NavMeshAgent>();
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

        public void SetTargetServer(Transform target)
        {
            m_target = target;
            m_nextPathRefreshTime = 0f;
        }

        private void Update()
        {
            if (!IsServerInitialized || m_agent == null || !m_agent.enabled ||
                !m_agent.isOnNavMesh || m_target == null || Time.time < m_nextPathRefreshTime)
            {
                return;
            }

            m_nextPathRefreshTime = Time.time + m_pathRefreshInterval;
            m_agent.SetDestination(m_target.position);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_pathRefreshInterval = Mathf.Max(0.05f, m_pathRefreshInterval);
        }
#endif
    }
}
