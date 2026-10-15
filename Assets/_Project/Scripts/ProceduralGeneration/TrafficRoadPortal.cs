using System;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [Serializable]
    public readonly struct TrafficRoadPortal
    {
        public readonly GridCoordinate Coordinate;
        public readonly CardinalDirection OutwardDirection;
        public readonly Vector3 WorldPosition;
        public readonly Vector3 WorldOutward;

        public TrafficRoadPortal(
            GridCoordinate coordinate,
            CardinalDirection outwardDirection,
            Vector3 worldPosition,
            Vector3 worldOutward)
        {
            Coordinate = coordinate;
            OutwardDirection = outwardDirection;
            WorldPosition = worldPosition;
            WorldOutward = worldOutward;
        }
    }
}
