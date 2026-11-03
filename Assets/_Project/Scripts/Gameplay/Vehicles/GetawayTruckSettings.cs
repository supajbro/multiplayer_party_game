using FishNet.Object;
using CouchGuys.Gameplay.Enemies;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    [CreateAssetMenu(menuName = "Couch Guys/Vehicles/Getaway Truck Settings")]
    public sealed class GetawayTruckSettings : ScriptableObject
    {
        public NetworkObject TruckPrefab;
        [Tooltip("Truck-tuned version of the same driving profile used by rogue cars.")]
        public RogueCarSettings DrivingSettings;
        [Min(1f)] public float CarrierNavigationSearchRadius = 32f;
        [Min(0f), Tooltip("Horizontal space beyond the couch bounds for its carriers.")] public float CarryClearancePadding = 1f;
        [Min(1)] public int MaximumCarryDetourObstacles = 8;
        [Min(.1f)] public float ParkingSettleTime = 1f;
        [Min(1f)] public float ParkedRecoveryTimeout = 6f;
        [Min(1f)] public float ParkingRepositionDistance = 6f;
        [Min(.1f)] public float RouteProgressDistance = 1f;
        [Min(1f)] public float Health = 300f;
        [Min(.1f)] public float ApproachSpeed = 8f;
        [Min(.1f)] public float EscapeSpeed = 15f;
        [Min(5f)] public float ParkingDistance = 12f;
        [Min(.1f)] public float LoadingDistance = 2.5f;
        [Min(.1f)] public float ReplacementDelay = 8f;
        [Min(0f)] public float MinimumSpawnDistance = 100f;
        [Min(.1f)] public float DecisionInterval = 1f;
        [Min(.1f)] public float WaypointRadius = 1.5f;
        [Min(.1f)] public float LookAheadDistance = 4f;
        [Min(.1f)] public float WaypointPassDistance = 4f;
        [Min(.1f)] public float CornerSpeed = 3f;
        [Min(1f)] public float MinimumChaseTime = 10f;
        [Min(1f)] public float MinimumEscapeRouteLength = 80f;
        [Min(1f)] public float StuckReverseDelay = 5f;
        [Min(1f)] public float StuckRecoveryTimeout = 25f;
        [Min(1f)] public float DepartureTimeout = 90f;
        [Min(.1f)] public float WreckLifetime = 3f;
        [Min(.1f)] public float GroundOffset = .12f;
        public Vector3 ParkingClearance = new Vector3(1.8f, 1.8f, 4.5f);
        [Tooltip("Half size of the chassis swept along road links to reject barriers and narrow passages.")]
        public Vector3 DrivingClearance = new Vector3(1.65f, 1.6f, 3.9f);
        public float DrivingClearanceHeight = 1.9f;
    }
}
