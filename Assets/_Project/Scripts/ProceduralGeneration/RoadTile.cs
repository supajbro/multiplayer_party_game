using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Cached connection metadata for one modular road prefab.</summary>
    [DisallowMultipleComponent]
    public sealed class RoadTile : MonoBehaviour
    {
        [SerializeField] private RoadConnections m_connections = RoadConnections.North | RoadConnections.South;

        public RoadConnections Connections => m_connections;

        public static RoadConnections RotateClockwise(RoadConnections connections, int quarterTurns)
        {
            quarterTurns = ((quarterTurns % 4) + 4) % 4;
            RoadConnections result = connections;
            for (int turn = 0; turn < quarterTurns; turn++)
            {
                RoadConnections rotated = RoadConnections.None;
                if ((result & RoadConnections.North) != 0) rotated |= RoadConnections.East;
                if ((result & RoadConnections.East) != 0) rotated |= RoadConnections.South;
                if ((result & RoadConnections.South) != 0) rotated |= RoadConnections.West;
                if ((result & RoadConnections.West) != 0) rotated |= RoadConnections.North;
                result = rotated;
            }

            return result;
        }
    }
}
