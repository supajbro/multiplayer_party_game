using System;

namespace CouchGuys.ProceduralGeneration
{
    [Flags]
    public enum RoadConnections
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3
    }

    public enum CardinalDirection
    {
        North,
        East,
        South,
        West
    }
}
