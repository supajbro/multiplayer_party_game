using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [DisallowMultipleComponent]
    public sealed class GeneratedRoad : MonoBehaviour
    {
        public GridCoordinate Coordinate { get; private set; }
        public RoadConnections Connections { get; private set; }

        public void Initialise(GridCoordinate coordinate, RoadConnections connections)
        {
            Coordinate = coordinate;
            Connections = connections;
        }
    }
}
