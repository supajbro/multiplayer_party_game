using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [CreateAssetMenu(menuName = "Couch Guys/World/Road Strategies/Grid", fileName = "RoadStrategy_Grid")]
    public sealed class GridRoadGenerationStrategy : RegionRoadGenerationStrategy
    {
        public override void Generate(RegionRoadGenerationContext context, System.Random random)
        {
            RegionGridSettings grid = context.Grid;
            int minimumX = -(grid.GridWidth / 2) * (grid.BlockWidth + 1);
            int maximumX = minimumX + grid.GridWidth * (grid.BlockWidth + 1);
            int maximumY = grid.GridHeight * (grid.BlockHeight + 1);

            for (int boundary = 0; boundary <= grid.GridWidth; boundary++)
            {
                int localX = minimumX + boundary * (grid.BlockWidth + 1);
                for (int localY = 0; localY <= maximumY; localY++)
                {
                    context.AddRoad(localX, localY);
                }
            }

            for (int boundary = 0; boundary <= grid.GridHeight; boundary++)
            {
                int localY = boundary * (grid.BlockHeight + 1);
                for (int localX = minimumX; localX <= maximumX; localX++)
                {
                    context.AddRoad(localX, localY);
                }
            }
        }
    }
}
