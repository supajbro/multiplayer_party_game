using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Required generation markers supplied by a starting-area prefab.</summary>
    [DisallowMultipleComponent]
    public sealed class StartingArea : MonoBehaviour
    {
        [SerializeField] private Transform m_roadConnection;
        [SerializeField] private Transform[] m_playerSpawnPoints = new Transform[4];
        [SerializeField] private Vector2Int m_gridFootprint = new Vector2Int(3, 3);

        public Transform RoadConnection => m_roadConnection;
        public Transform[] PlayerSpawnPoints => m_playerSpawnPoints;
        public Vector2Int GridFootprint => m_gridFootprint;

        public void Initialise(Transform roadConnection, Transform[] playerSpawnPoints, Vector2Int gridFootprint)
        {
            m_roadConnection = roadConnection;
            m_playerSpawnPoints = playerSpawnPoints;
            m_gridFootprint = gridFootprint;
        }
    }
}
