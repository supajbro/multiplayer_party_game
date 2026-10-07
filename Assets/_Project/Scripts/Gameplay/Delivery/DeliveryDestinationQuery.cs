using CouchGuys.ProceduralGeneration;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Stage-specific destination envelope and deterministic ranking.</summary>
    public readonly struct DeliveryDestinationQuery
    {
        public readonly float MinimumRouteLength;
        public readonly float MaximumRouteLength;
        public readonly float MaximumElevationGain;
        public readonly float MaximumRoadGrade;
        public readonly float TargetRouteLength;
        public readonly float TargetDifficultyScore;
        public readonly SuburbZone MinimumZone;
        public readonly SuburbZone MaximumZone;

        private DeliveryDestinationQuery(
            float minimumRouteLength,
            float maximumRouteLength,
            float maximumElevationGain,
            float maximumRoadGrade,
            float targetRouteLength,
            float targetDifficultyScore,
            SuburbZone minimumZone,
            SuburbZone maximumZone)
        {
            MinimumRouteLength = minimumRouteLength;
            MaximumRouteLength = maximumRouteLength;
            MaximumElevationGain = maximumElevationGain;
            MaximumRoadGrade = maximumRoadGrade;
            TargetRouteLength = targetRouteLength;
            TargetDifficultyScore = targetDifficultyScore;
            MinimumZone = minimumZone;
            MaximumZone = maximumZone;
        }

        public static DeliveryDestinationQuery ForSuburbsStage(int stageIndex)
        {
            return Mathf.Clamp(stageIndex, 0, 6) switch
            {
                0 => new DeliveryDestinationQuery(0f, 96f, 4f, 18f, 40f, 10f, SuburbZone.Start, SuburbZone.Easy),
                1 => new DeliveryDestinationQuery(40f, 152f, 10f, 24f, 96f, 25f, SuburbZone.Easy, SuburbZone.Medium),
                2 => new DeliveryDestinationQuery(72f, 208f, 16f, 30f, 136f, 40f, SuburbZone.Medium, SuburbZone.Hilly),
                3 => new DeliveryDestinationQuery(96f, 272f, 24f, 38f, 184f, 48f, SuburbZone.Medium, SuburbZone.Hilly),
                4 => new DeliveryDestinationQuery(128f, 352f, 36f, 45f, 240f, 62f, SuburbZone.Hilly, SuburbZone.Outer),
                5 => new DeliveryDestinationQuery(160f, float.MaxValue, float.MaxValue, 60f, 304f, 76f, SuburbZone.Hilly, SuburbZone.Outer),
                _ => new DeliveryDestinationQuery(192f, float.MaxValue, float.MaxValue, 60f, 384f, 90f, SuburbZone.Outer, SuburbZone.Outer)
            };
        }

        public bool Matches(DeliveryRouteMetrics metrics)
        {
            return metrics != null && metrics.IsReachable &&
                metrics.RouteLength >= MinimumRouteLength &&
                metrics.RouteLength <= MaximumRouteLength &&
                metrics.CumulativeElevationGain <= MaximumElevationGain &&
                metrics.MaximumRoadGrade <= MaximumRoadGrade &&
                metrics.ProgressionZone >= MinimumZone &&
                metrics.ProgressionZone <= MaximumZone;
        }

        public float CalculateSuitability(DeliveryRouteMetrics metrics, int stageIndex)
        {
            if (metrics == null || !metrics.IsReachable)
            {
                return float.MaxValue;
            }

            // The tutorial strongly prioritises a short, flat route. Later stages aim
            // at progressively larger route/score targets while still preferring less
            // extreme elevation than the stage envelope permits.
            if (stageIndex <= 0)
            {
                return metrics.RouteLength +
                    metrics.CumulativeElevationGain * 18f +
                    metrics.MaximumRoadGrade * 2f +
                    metrics.FinalCarryDistance * 0.25f;
            }

            return Mathf.Abs(metrics.RouteLength - TargetRouteLength) +
                Mathf.Abs(metrics.DifficultyScore - TargetDifficultyScore) * 2f +
                metrics.MaximumRoadGrade * 0.15f;
        }
    }
}
