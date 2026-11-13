using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    /// <summary>Presentation for a server-simulated bolt, instantiated from a saved prefab.</summary>
    public sealed class HoverTruckBolt : MonoBehaviour
    {
        private Vector3 m_velocity;
        public void Launch(Vector3 velocity, float lifetime)
        {
            m_velocity = velocity;
            Destroy(gameObject, lifetime);
        }

        private void Update() => transform.position += m_velocity * Time.deltaTime;
    }
}
