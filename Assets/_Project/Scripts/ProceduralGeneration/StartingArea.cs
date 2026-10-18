using UnityEngine;
using CouchGuys.Gameplay.Depot;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Required generation markers supplied by a starting-area prefab.</summary>
    [DisallowMultipleComponent]
    public sealed class StartingArea : MonoBehaviour
    {
        [SerializeField] private Transform m_roadConnection;
        [SerializeField] private Transform[] m_playerSpawnPoints = new Transform[4];
        [SerializeField] private Transform m_deliveryNpcSpawnPoint;
        [SerializeField] private Vector2Int m_gridFootprint = new Vector2Int(3, 3);
        [SerializeField] private DepotController m_depot;

        public Transform RoadConnection => m_roadConnection;
        public Transform[] PlayerSpawnPoints => m_playerSpawnPoints;
        public Transform DeliveryNpcSpawnPoint => m_deliveryNpcSpawnPoint;
        public Vector2Int GridFootprint => m_gridFootprint;
        public DepotController Depot => m_depot;

        public void Initialise(
            Transform roadConnection,
            Transform[] playerSpawnPoints,
            Transform deliveryNpcSpawnPoint,
            Vector2Int gridFootprint)
        {
            m_roadConnection = roadConnection;
            m_playerSpawnPoints = playerSpawnPoints;
            m_deliveryNpcSpawnPoint = deliveryNpcSpawnPoint;
            m_gridFootprint = gridFootprint;
        }

        public void SetDeliveryNpcSpawnPoint(Transform spawnPoint)
        {
            m_deliveryNpcSpawnPoint = spawnPoint;
        }

        public void SetDepot(DepotController depot)
        {
            m_depot = depot;
        }
    }
}
