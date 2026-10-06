using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Server-side lifetime registration added to spawned thief instances.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefSpawnMember : MonoBehaviour
    {
        private EnemySpawnManager m_manager;
        private NetworkObject m_networkObject;

        public void Register(EnemySpawnManager manager, NetworkObject networkObject)
        {
            m_manager = manager;
            m_networkObject = networkObject;
        }

        private void OnDestroy()
        {
            m_manager?.Unregister(m_networkObject);
        }
    }
}
