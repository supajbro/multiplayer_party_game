using System.Collections.Generic;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Depot;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Non-attacking group companion. The existing navigator owns authority and lifecycle.</summary>
    [DisallowMultipleComponent]
    public sealed class SpeakerThief : MonoBehaviour
    {
        [SerializeField, Min(.5f)] private float m_followDistance = 2.5f;
        [SerializeField, Min(.5f)] private float m_catchUpDistance = 7f;
        [SerializeField, Min(1f)] private float m_catchUpMultiplier = 1.2f;
        [SerializeField, Min(.1f)] private float m_repathInterval = .35f;
        [SerializeField, Min(.5f)] private float m_sampleRadius = 3f;
        private NavMeshAgent m_agent;
        private ThiefNavigator m_navigator;
        private ThiefSpawnMember m_member;
        private float m_speed;
        private float m_nextPathTime;

        private void Awake()
        {
            m_agent = GetComponent<NavMeshAgent>();
            m_navigator = GetComponent<ThiefNavigator>();
            m_member = GetComponent<ThiefSpawnMember>();
            m_speed = m_agent != null ? m_agent.speed : 4.5f;
        }

        // Called only by the server navigator, after shared knockback/death/safe-zone guards.
        internal void FollowGroupServer()
        {
            if (m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh || Time.time < m_nextPathTime) return;
            m_nextPathTime = Time.time + Mathf.Max(.1f, m_repathInterval);
            var members = m_member != null ? m_member.GroupMembers : null;
            NetworkObject leader = null;
            float bestDistance = float.PositiveInfinity;
            if (members != null)
                foreach (NetworkObject other in members)
                {
                    if (other == null || other.gameObject == gameObject || !other.IsSpawned || other.GetComponent<SpeakerThief>() != null ||
                        !other.TryGetComponent(out ThiefHealth health) || !health.IsAlive ||
                        !other.TryGetComponent(out ThiefNavigator navigator) || navigator.IsDeparting) continue;
                    float distance = (other.transform.position - transform.position).sqrMagnitude;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;leader = other;
                }
            if (leader == null)
            {
                m_navigator.BeginDepartureServer(); // Never fall back to player/couch targeting.
                return;
            }
            m_agent.speed = m_speed * (bestDistance > m_catchUpDistance * m_catchUpDistance ? Mathf.Max(1f, m_catchUpMultiplier) : 1f);
            m_agent.stoppingDistance = Mathf.Max(.5f, m_followDistance);
            if (bestDistance <= m_agent.stoppingDistance * m_agent.stoppingDistance)
            {
                m_agent.isStopped = true;
                return;
            }
            if (NavMesh.SamplePosition(leader.transform.position, out NavMeshHit hit, m_sampleRadius, m_agent.areaMask) &&
                !DepotSafeZone.Contains(hit.position))
            {
                m_agent.isStopped = false;
                m_agent.SetDestination(hit.position);
            }
        }

        public static bool IsRegular(NetworkObject member) => member != null && member.GetComponent<SpeakerThief>() == null &&
            member.GetComponent<GrapplerThief>() == null && !(member.TryGetComponent(out EnemyWeapon weapon) && weapon.IsRocketLauncher);

        private static bool IsUsable(NetworkObject member) => member != null && member.GetComponent<NavMeshAgent>() != null &&
            member.GetComponent<ThiefHealth>() != null && member.GetComponent<ThiefNavigator>() != null;

        /// <summary>One roll per final filtered group; returns an existing regular slot, never an extra member.</summary>
        public static int SelectReplacement(IReadOnlyList<NetworkObject> group, DeliveryTierConfig tier, float roll, int ordinal = 0)
        {
            if (group == null || tier == null) return -1;
            int total = 0, regulars = 0;
            foreach (NetworkObject member in group)
            {
                if (!IsUsable(member)) continue;
                if (member.GetComponent<SpeakerThief>() != null) return -1;
                total++;
                if (IsRegular(member)) regulars++;
            }
            if (regulars == 0 || !tier.ShouldReplaceWithSpeakerThief(total, roll)) return -1;
            int desired = (ordinal & int.MaxValue) % regulars;
            for (int i = 0; i < group.Count; i++)
                if (IsUsable(group[i]) && IsRegular(group[i]) && desired-- == 0) return i;
            return -1;
        }
    }
}
