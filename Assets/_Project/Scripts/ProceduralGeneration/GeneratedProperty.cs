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
        [SerializeField] private Transform m_visualRoot;
        [SerializeField] private bool m_fitVisualToFootprint;

        public Transform RoadConnection => m_roadConnection;
        public Transform DeliveryPoint => m_deliveryPoint;
        public Vector2Int GridFootprint => m_gridFootprint;
        public Transform VisualRoot => m_visualRoot;
        public bool FitVisualToFootprint => m_fitVisualToFootprint;
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

        public void ConfigurePrefab(
            Vector2Int gridFootprint,
            Transform roadConnection,
            Transform deliveryPoint,
            Transform visualRoot,
            bool fitVisualToFootprint)
        {
            m_gridFootprint = gridFootprint;
            m_roadConnection = roadConnection;
            m_deliveryPoint = deliveryPoint;
            m_visualRoot = visualRoot;
            m_fitVisualToFootprint = fitVisualToFootprint;
        }
    }
}
