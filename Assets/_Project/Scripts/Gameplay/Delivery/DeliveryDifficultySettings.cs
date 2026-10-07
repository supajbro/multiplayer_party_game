using System;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    [Serializable]
    public struct DeliveryDifficultySettings
    {
        [Header("Weights")]
        [Min(0f)] public float RouteDistanceWeight;
        [Min(0f)] public float DirectDistanceWeight;
        [Min(0f)] public float ElevationGainWeight;
        [Min(0f)] public float MaximumGradeWeight;
        [Min(0f)] public float TurnWeight;
        [Min(0f)] public float IntersectionWeight;
        [Min(0f)] public float FinalCarryWeight;
        [Min(0f)] public float FinalCarryElevationWeight;
        [Min(0f)] public float ProgressionZoneWeight;

        [Header("Normalization Targets")]
        [Min(1f)] public float LongRouteDistance;
        [Min(1f)] public float LongDirectDistance;
        [Min(1f)] public float HighElevationGain;
        [Min(1f)] public float SteepRoadGrade;
        [Min(1)] public int ManyTurns;
        [Min(1)] public int ManyIntersections;
        [Min(1f)] public float LongFinalCarry;
        [Min(1f)] public float HighFinalCarryElevation;

        public float TotalWeight => Mathf.Max(
            0.001f,
            RouteDistanceWeight + DirectDistanceWeight + ElevationGainWeight +
            MaximumGradeWeight + TurnWeight + IntersectionWeight + FinalCarryWeight +
            FinalCarryElevationWeight + ProgressionZoneWeight);

        public static DeliveryDifficultySettings SuburbsDefault => new DeliveryDifficultySettings
        {
            RouteDistanceWeight = 28f,
            DirectDistanceWeight = 8f,
            ElevationGainWeight = 18f,
            MaximumGradeWeight = 14f,
            TurnWeight = 7f,
            IntersectionWeight = 4f,
            FinalCarryWeight = 7f,
            FinalCarryElevationWeight = 6f,
            ProgressionZoneWeight = 8f,
            LongRouteDistance = 360f,
            LongDirectDistance = 280f,
            HighElevationGain = 28f,
            SteepRoadGrade = 22f,
            ManyTurns = 12,
            ManyIntersections = 18,
            LongFinalCarry = 28f,
            HighFinalCarryElevation = 10f
        };
    }
}
