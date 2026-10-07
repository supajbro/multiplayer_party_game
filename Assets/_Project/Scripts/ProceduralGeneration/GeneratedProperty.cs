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
        [SerializeField] private GridCoordinate m_anchorCoordinate;
        [SerializeField] private GridCoordinate m_roadCoordinate;
        [SerializeField] private SuburbZone m_zone;
        [SerializeField] private float m_roadElevation;
        [SerializeField] private float m_lotElevation;
        [SerializeField] private float m_drivewayLength;
        [SerializeField] private float m_drivewayGrade;
        [SerializeField] private bool m_vanAccessible;

        public Transform RoadConnection => m_roadConnection;
        public Transform DeliveryPoint => m_deliveryPoint;
        public Vector2Int GridFootprint => m_gridFootprint;
        public Transform VisualRoot => m_visualRoot;
        public bool FitVisualToFootprint => m_fitVisualToFootprint;
        public GridCoordinate AnchorCoordinate => m_anchorCoordinate;
        public GridCoordinate RoadCoordinate => m_roadCoordinate;
        public SuburbZone Zone => m_zone;
        public float RoadElevation => m_roadElevation;
        public float LotElevation => m_lotElevation;
        public float DrivewayLength => m_drivewayLength;
        public float DrivewayGrade => m_drivewayGrade;
        public bool VanAccessible => m_vanAccessible;

        public void Initialise(
            GridCoordinate anchorCoordinate,
            GridCoordinate roadCoordinate,
            Vector2Int gridFootprint,
            Transform roadConnection,
            Transform deliveryPoint,
            SuburbZone zone = SuburbZone.Easy,
            float roadElevation = 0f)
        {
            m_anchorCoordinate = anchorCoordinate;
            m_roadCoordinate = roadCoordinate;
            m_gridFootprint = gridFootprint;
            m_roadConnection = roadConnection;
            m_deliveryPoint = deliveryPoint;
            m_zone = zone;
            m_roadElevation = roadElevation;
            m_lotElevation = transform.position.y;
        }

        public void SetAccessMetadata(Vector3 roadWorldPosition)
        {
            m_lotElevation = transform.position.y;
            Vector3 deliveryPosition = m_deliveryPoint != null
                ? m_deliveryPoint.position
                : transform.position;
            Vector3 horizontal = deliveryPosition - roadWorldPosition;
            horizontal.y = 0f;
            m_drivewayLength = horizontal.magnitude;
            m_drivewayGrade = m_drivewayLength > 0.01f
                ? Mathf.Atan2(Mathf.Abs(m_lotElevation - m_roadElevation), m_drivewayLength) * Mathf.Rad2Deg
                : 0f;
            m_vanAccessible = m_drivewayGrade <= 16f && m_drivewayLength <= 24f;
        }

        public void Initialise(
            GridCoordinate anchorCoordinate,
            Vector2Int gridFootprint,
            Transform roadConnection,
            Transform deliveryPoint) =>
            Initialise(anchorCoordinate, anchorCoordinate, gridFootprint, roadConnection, deliveryPoint);

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
