using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Property metadata used both by prefabs and future delivery systems.</summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedProperty : MonoBehaviour
    {
        [SerializeField] private Transform m_roadConnection;
        [SerializeField] private Transform m_deliveryPoint;
        [SerializeField] private Vector2Int m_gridFootprint = Vector2Int.one;

        public Transform RoadConnection => m_roadConnection;
        public Transform DeliveryPoint => m_deliveryPoint;
        public Vector2Int GridFootprint => m_gridFootprint;
        public GridCoordinate AnchorCoordinate { get; private set; }

        public void Initialise(
            GridCoordinate anchorCoordinate,
            Vector2Int gridFootprint,
            Transform roadConnection,
            Transform deliveryPoint)
        {
            AnchorCoordinate = anchorCoordinate;
            m_gridFootprint = gridFootprint;
            m_roadConnection = roadConnection;
            m_deliveryPoint = deliveryPoint;
        }
    }
}
