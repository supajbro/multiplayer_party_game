using System;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [Serializable]
    public readonly struct GridCoordinate : IEquatable<GridCoordinate>
    {
        public static readonly GridCoordinate Zero = new GridCoordinate(0, 0);

        [SerializeField] private readonly int m_x;
        [SerializeField] private readonly int m_y;

        public int X => m_x;
        public int Y => m_y;

        public GridCoordinate(int x, int y)
        {
            m_x = x;
            m_y = y;
        }

        public bool Equals(GridCoordinate other) => m_x == other.m_x && m_y == other.m_y;
        public override bool Equals(object obj) => obj is GridCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(m_x, m_y);
        public override string ToString() => $"({m_x}, {m_y})";

        public static GridCoordinate operator +(GridCoordinate left, GridCoordinate right) =>
            new GridCoordinate(left.m_x + right.m_x, left.m_y + right.m_y);
        public static GridCoordinate operator -(GridCoordinate left, GridCoordinate right) =>
            new GridCoordinate(left.m_x - right.m_x, left.m_y - right.m_y);
        public static bool operator ==(GridCoordinate left, GridCoordinate right) => left.Equals(right);
        public static bool operator !=(GridCoordinate left, GridCoordinate right) => !left.Equals(right);
    }
}
