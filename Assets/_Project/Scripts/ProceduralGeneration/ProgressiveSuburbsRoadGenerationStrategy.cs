using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Creates a dense conventional grid near the depot and progressively sparser,
    /// less symmetrical connected streets farther into the Suburbs. A central road
    /// is always generated from the depot to the outer boundary as the vehicle-safe spine.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Couch Guys/World/Road Strategies/Progressive Suburbs",
        fileName = "RoadStrategy_ProgressiveSuburbs")]
    public sealed class ProgressiveSuburbsRoadGenerationStrategy : RegionRoadGenerationStrategy
    {
        [SerializeField, Range(0.1f, 0.5f)] private float m_denseGridFraction = 0.34f;
        [SerializeField, Range(0.25f, 1f)] private float m_mediumStreetDensity = 0.72f;
        [SerializeField, Range(0.2f, 1f)] private float m_hillyStreetDensity = 0.52f;
        [SerializeField, Range(0.15f, 1f)] private float m_outerStreetDensity = 0.38f;

        public override void Generate(RegionRoadGenerationContext context, System.Random random)
        {
            RegionGridSettings grid = context.Grid;
            int spanX = grid.BlockWidth + 1;
            int spanY = grid.BlockHeight + 1;
            int minimumX = -(grid.GridWidth / 2) * spanX;
            int maximumY = grid.GridHeight * spanY;
            int boundaryCount = grid.GridWidth + 1;
            int spineBoundary = Mathf.Clamp(-minimumX / spanX, 0, boundaryCount - 1);
            List<HashSet<int>> activeBoundaries = new List<HashSet<int>>(grid.GridHeight);

            for (int row = 0; row < grid.GridHeight; row++)
            {
                float progress = (row + 0.5f) / grid.GridHeight;
                float density = progress <= m_denseGridFraction
                    ? 1f
                    : progress <= 0.58f
                        ? m_mediumStreetDensity
                        : progress <= 0.82f
                            ? m_hillyStreetDensity
                            : m_outerStreetDensity;
                int desired = Mathf.Clamp(Mathf.RoundToInt(boundaryCount * density), 2, boundaryCount);
                activeBoundaries.Add(SelectBoundaries(
                    boundaryCount,
                    spineBoundary,
                    desired,
                    progress <= m_denseGridFraction,
                    random));
            }

            HashSet<Vector2Int> roads = new HashSet<Vector2Int>();
            for (int row = 0; row < grid.GridHeight; row++)
            {
                int minimumY = row * spanY;
                int maximumRowY = minimumY + spanY;
                HashSet<int> active = activeBoundaries[row];
                foreach (int boundary in active)
                {
                    int localX = minimumX + boundary * spanX;
                    AddLine(roads, localX, minimumY, localX, maximumRowY);
                }

                // Join every vertical street at the block boundary. Including the
                // neighbouring row's streets lets branches merge cleanly rather than
                // producing disconnected late-zone islands.
                HashSet<int> junctions = new HashSet<int>(active);
                if (row > 0)
                {
                    junctions.UnionWith(activeBoundaries[row - 1]);
                }

                int left = spineBoundary;
                int right = spineBoundary;
                foreach (int boundary in junctions)
                {
                    left = Mathf.Min(left, boundary);
                    right = Mathf.Max(right, boundary);
                }

                AddLine(
                    roads,
                    minimumX + left * spanX,
                    minimumY,
                    minimumX + right * spanX,
                    minimumY);
            }

            HashSet<int> finalJunctions = activeBoundaries[activeBoundaries.Count - 1];
            int finalLeft = spineBoundary;
            int finalRight = spineBoundary;
            foreach (int boundary in finalJunctions)
            {
                finalLeft = Mathf.Min(finalLeft, boundary);
                finalRight = Mathf.Max(finalRight, boundary);
            }

            AddLine(
                roads,
                minimumX + finalLeft * spanX,
                maximumY,
                minimumX + finalRight * spanX,
                maximumY);

            List<Vector2Int> orderedRoads = new List<Vector2Int>(roads);
            orderedRoads.Sort((first, second) =>
            {
                int yComparison = first.y.CompareTo(second.y);
                return yComparison != 0 ? yComparison : first.x.CompareTo(second.x);
            });
            for (int index = 0; index < orderedRoads.Count; index++)
            {
                Vector2Int road = orderedRoads[index];
                context.AddRoad(road.x, road.y);
            }
        }

        private static HashSet<int> SelectBoundaries(
            int boundaryCount,
            int spineBoundary,
            int desired,
            bool useFullGrid,
            System.Random random)
        {
            HashSet<int> selected = new HashSet<int> { spineBoundary };
            if (useFullGrid)
            {
                for (int index = 0; index < boundaryCount; index++)
                {
                    selected.Add(index);
                }

                return selected;
            }

            // Preserve at least one changing side street so later rows remain useful
            // residential areas rather than becoming a single corridor.
            int forcedSide = spineBoundary + (random.Next(0, 2) == 0 ? -1 : 1);
            selected.Add(Mathf.Clamp(forcedSide, 0, boundaryCount - 1));
            while (selected.Count < desired)
            {
                selected.Add(random.Next(0, boundaryCount));
            }

            return selected;
        }

        private static void AddLine(
            HashSet<Vector2Int> roads,
            int startX,
            int startY,
            int endX,
            int endY)
        {
            int stepX = startX == endX ? 0 : startX < endX ? 1 : -1;
            int stepY = startY == endY ? 0 : startY < endY ? 1 : -1;
            int x = startX;
            int y = startY;
            while (true)
            {
                roads.Add(new Vector2Int(x, y));
                if (x == endX && y == endY)
                {
                    return;
                }

                x += stepX;
                y += stepY;
            }
        }
    }
}
