using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    public sealed partial class DeliveryManager
    {
        [Header("Thief Getaway")]
        [SerializeField] private GetawayTruckSettings m_getawaySettings;
        private readonly List<GetawayTruckController> m_getaways = new();
        private readonly GetawayRoadRouter m_getawayRoads = new();
        public GetawayTruckController GetGetawayForCouch(CouchCarryController couch) =>
            couch != null && m_jobs.TryGetValue(couch.DeliveryId, out var runtime) ? runtime.Getaway : null;

        internal bool GeneratorIsTunnelApproach(GridCoordinate coordinate) => m_generator.IsTunnelApproachRoad(coordinate);

        private void UpdateGetawayServer(DeliveryInstance job, JobRuntime runtime)
        {
            if (job.Couch == null || runtime.Getaway != null || Time.time < runtime.GetawaySpawnTime) return;
            m_getawaySettings ??= Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            if (m_getawaySettings == null || m_getawaySettings.TruckPrefab == null ||
                m_getawaySettings.DrivingSettings == null) return;
            m_getaways.RemoveAll(truck => truck == null || !truck.IsSpawned);
            m_getawayRoads.Build(m_generator);
            CouchCarryController couch = job.Couch.GetComponent<CouchCarryController>();
            DeliveryDestination destination = GetDestination(job.DestinationIndex);
            runtime.GetawaySpawnTime = Time.time + m_getawaySettings.DecisionInterval;
            TrafficRoadPortal? selected = null;
            float farthest = -1f;
            bool meetsMinimum = false;
            foreach (TrafficRoadPortal portal in m_getawayRoads.Portals)
            {
                float distance = Vector3.Distance(portal.WorldPosition, couch.transform.position);
                float destinationDistance = destination != null
                    ? Vector3.Distance(portal.WorldPosition, destination.DropPosition.position) : distance;
                bool qualifies = Mathf.Min(distance, destinationDistance) >= m_getawaySettings.MinimumSpawnDistance;
                if ((meetsMinimum && !qualifies) || (qualifies == meetsMinimum && distance <= farthest) ||
                    !GetawaySpawnClear(portal)) continue;
                // Prefer the requested minimum; on smaller maps use the farthest real tunnel.
                farthest = distance;
                meetsMinimum = qualifies;
                selected = portal;
            }
            if (!selected.HasValue) return;
            TrafficRoadPortal entry = selected.Value;
            Vector3 position = entry.WorldPosition + Vector3.up * m_getawaySettings.GroundOffset;
            NetworkObject instance = Instantiate(m_getawaySettings.TruckPrefab, position,
                Quaternion.LookRotation(-entry.WorldOutward, Vector3.up));
            GetawayTruckController truck = instance.GetComponent<GetawayTruckController>();
            truck.ConfigureBeforeSpawn(m_getawaySettings);
            Spawn(instance);
            truck.InitialiseServer(this, couch, m_getawayRoads, runtime.Replacement);
            m_getaways.Add(truck);
            runtime.Getaway = truck;
            runtime.GetawaySpawnTime = float.PositiveInfinity;
        }

        private bool GetawaySpawnClear(TrafficRoadPortal portal)
        {
            Quaternion rotation = Quaternion.LookRotation(-portal.WorldOutward);
            foreach (Collider hit in Physics.OverlapBox(portal.WorldPosition + Vector3.up *
                (m_getawaySettings.ParkingClearance.y + .25f), m_getawaySettings.ParkingClearance,
                rotation, ~0, QueryTriggerInteraction.Ignore))
                if (hit != null && !TruckController.IsGroundSurface(hit)) return false;
            return true;
        }

        internal void NotifyGetawayDestroyedServer(GetawayTruckController truck)
        {
            if (!IsServerInitialized) return;
            foreach (var runtime in m_jobs.Values)
                if (runtime.Getaway == truck)
                {
                    runtime.Getaway = null;
                    runtime.Replacement = true;
                    runtime.GetawaySpawnTime = Time.time + m_getawaySettings.ReplacementDelay;
                    break;
                }
        }

        internal void GetawayEscapedServer(GetawayTruckController truck, CouchCarryController couch)
        {
            if (!IsServerInitialized || couch == null || GetGetawayForCouch(couch) != truck ||
                !IsDeliveryCouch(couch) || !couch.IsLoaded || couch.LoadedTruckObject != truck.NetworkObject) return;
            couch.transform.SetParent(null, true);
            ResolveDeliveryServer(couch.DeliveryId, false);
            if (truck != null && truck.IsSpawned) Despawn(truck.NetworkObject);
        }

        private void ClearGetawaysServer()
        {
            if (!IsServerInitialized) return;
            foreach (GetawayTruckController truck in m_getaways)
                if (truck != null && truck.IsSpawned) Despawn(truck.NetworkObject);
            m_getaways.Clear();
        }

    }
}
