using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [DisallowMultipleComponent]
    public sealed class GeneratedRoad : MonoBehaviour
    {
        [SerializeField] private GridCoordinate m_coordinate;
        [SerializeField] private RoadConnections m_connections;
        [SerializeField] private SuburbZone m_zone;
        [SerializeField] private GeneratedRoadRole m_role;
        [SerializeField] private float m_elevation;
        [SerializeField] private float m_maximumConnectedGrade;

        public GridCoordinate Coordinate => m_coordinate;
        public RoadConnections Connections => m_connections;
        public SuburbZone Zone => m_zone;
        public GeneratedRoadRole Role => m_role;
        public float Elevation => m_elevation;
        public float MaximumConnectedGrade => m_maximumConnectedGrade;

        public void Initialise(GridCoordinate coordinate, RoadConnections connections)
        {
            Initialise(
                coordinate,
                connections,
                SuburbZone.Easy,
                GeneratedRoadRole.Residential,
                transform.position.y,
                0f);
        }

        public void Initialise(
            GridCoordinate coordinate,
            RoadConnections connections,
            SuburbZone zone,
            GeneratedRoadRole role,
            float elevation,
            float maximumConnectedGrade)
        {
            m_coordinate = coordinate;
            m_connections = connections;
            m_zone = zone;
            m_role = role;
            m_elevation = elevation;
            m_maximumConnectedGrade = maximumConnectedGrade;
        }
    }
}
