using System;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public abstract class RegionRoadGenerationStrategy : ScriptableObject
    {
        public abstract void Generate(RegionRoadGenerationContext context, System.Random random);
    }

    public sealed class RegionRoadGenerationContext
    {
        private readonly Action<int, int> m_addRoad;

        public RegionGridSettings Grid { get; }

        internal RegionRoadGenerationContext(RegionGridSettings grid, Action<int, int> addRoad)
        {
            Grid = grid;
            m_addRoad = addRoad;
        }

        public void AddRoad(int localX, int localY)
        {
            m_addRoad(localX, localY);
        }
    }
}
