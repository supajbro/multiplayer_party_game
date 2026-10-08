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
        [SerializeField, Min(1f)] private float m_maxHealth = 100f;
        [SerializeField, Min(0.05f)] private float m_knockbackDuration = 0.22f;
        [SerializeField, Min(0f)] private float m_knockbackDamping = 14f;
        [SerializeField, Min(0f)] private float m_deathDespawnDelay = 2.5f;
        [SerializeField] private NavMeshAgent m_agent;
        [SerializeField] private Collider m_collider;

        private readonly SyncVar<float> m_currentHealth = new();
        private readonly SyncVar<bool> m_dead = new();
        private Vector3 m_knockbackVelocity;
        private float m_resumeNavigationAt;
        private float m_despawnAt = float.PositiveInfinity;
        private ThiefAnimationDriver m_animationDriver;

        public event Action<float, float> HealthChanged;
        public bool IsAlive => !m_dead.Value;
        public float CurrentHealth => m_currentHealth.Value;

        private void Awake()
        {
            m_agent ??= GetComponent<NavMeshAgent>();
            m_collider ??= GetComponent<Collider>();
            m_animationDriver = GetComponentInChildren<ThiefAnimationDriver>(true);
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
            else if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh && m_agent.isStopped)
            {
                m_agent.isStopped = false;
            }
        }

        [Server]
        public void ApplyDamageServer(float damage, Vector3 impactDirection, float knockbackForce)
        {
            if (damage <= 0f || m_dead.Value)
            {
                return;
            }

            m_currentHealth.Value = Mathf.Max(0f, m_currentHealth.Value - damage);
            Vector3 direction = Vector3.ProjectOnPlane(impactDirection, Vector3.up).normalized;
            m_knockbackVelocity += direction * Mathf.Max(0f, knockbackForce);
            m_resumeNavigationAt = Time.time + m_knockbackDuration;
            if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
            {
                m_agent.isStopped = true;
                m_agent.ResetPath();
            }

            if (m_currentHealth.Value <= 0f)
            {
                m_dead.Value = true;
                m_despawnAt = Time.time + m_deathDespawnDelay;
            }
            else
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
            if (m_agent != null) m_agent.enabled = false;
            if (m_collider != null) m_collider.enabled = false;
            m_animationDriver?.TriggerDeath();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_maxHealth = Mathf.Max(1f, m_maxHealth);
            m_knockbackDuration = Mathf.Max(0.05f, m_knockbackDuration);
        }
#endif
    }
}
