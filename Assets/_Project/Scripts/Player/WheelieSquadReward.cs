using CouchGuys.Networking;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>A saved trigger cube. The server alone grants one squad to the human who touches it.</summary>
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class WheelieSquadReward : NetworkBehaviour
    {
        internal static event System.Action ClientSpawned;
        [SerializeField] private WheelieSettings m_settings;
        [SerializeField, Min(1f)] private float m_lifetime = 90f;
        [SerializeField, Min(.1f)] private float m_failedClaimRetryInterval = .5f;
        private float m_groundHeight, m_expiresAt, m_nextClaim;
        private bool m_claimed;
        public WheelieSettings Settings => m_settings;
        internal void Initialise(float groundHeight) => m_groundHeight = groundHeight;
        public override void OnStartServer()
        { base.OnStartServer(); m_expiresAt = Time.time + m_lifetime; }
        public override void OnStartClient() { base.OnStartClient(); ClientSpawned?.Invoke(); }
        private void Update()
        { if (IsServerInitialized && IsSpawned && Time.time >= m_expiresAt) ServerManager.Despawn(NetworkObject); }
        private void OnTriggerEnter(Collider other) => TryCollectServer(other);
        private void OnTriggerStay(Collider other) => TryCollectServer(other);
        internal bool TryCollectServer(Collider other)
        {
            if (!IsServerInitialized || !IsSpawned || m_claimed || Time.time < m_nextClaim || other == null) return false;
            var player = other.GetComponentInParent<PlayerHealth>();
            var owner = player != null ? player.GetComponent<NetworkPlayerOwnership>() : null;
            if (owner == null || !owner.IsServerInitialized || !owner.IsSpawned || !owner.Owner.IsValid || !owner.Owner.IsActive ||
                !player.IsAlive || player.IsAiTeammate || owner.gameObject.scene != gameObject.scene ||
                GameplaySessionController.IsServerGameOver) return false;
            m_claimed = true; m_nextClaim = Time.time + m_failedClaimRetryInterval;
            Vector3 origin = new Vector3(player.transform.position.x, m_groundHeight, player.transform.position.z);
            if (!WheelieSquadSpawner.TrySpawnServer(owner, m_settings, origin)) { m_claimed = false; return false; }
            ServerManager.Despawn(NetworkObject);
            return true;
        }
    }
}
