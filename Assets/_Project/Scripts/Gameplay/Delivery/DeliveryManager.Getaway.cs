using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    public sealed partial class DeliveryManager
    {
        [Header("Thief Getaway")]
        [SerializeField] private GetawayTruckSettings m_getawaySettings;
        private readonly SyncVar<NetworkObject> m_activeGetaway = new();
        private readonly List<GetawayTruckController> m_getaways = new();
        private readonly GetawayRoadRouter m_getawayRoads = new();
        private CouchCarryController m_getawayCouch;
        private float m_getawaySpawnTime = float.PositiveInfinity;
        private bool m_getawayReplacement;
        private bool m_getawayAttemptActive;

        public GetawayTruckController ActiveGetawayTruck => m_activeGetaway.Value != null
            ? m_activeGetaway.Value.GetComponent<GetawayTruckController>() : null;

        internal bool GeneratorIsTunnelApproach(GridCoordinate coordinate) => m_generator.IsTunnelApproachRoad(coordinate);

        private void BeginGetawayServer(CouchCarryController couch)
        {
            EndGetawayServer();
            m_getawaySettings ??= Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
            if (m_getawaySettings == null || m_getawaySettings.TruckPrefab == null) return;
            if (m_getawaySettings.DrivingSettings == null ||
                m_getawaySettings.TruckPrefab.GetComponent<GetawayTruckController>() == null)
            {
                Debug.LogError("Getaway settings require a driving profile and a getaway truck prefab.", this);
                return;
            }
            m_getawayRoads.Build(m_generator);
            m_getawayCouch = couch;
            m_getawayAttemptActive = true;
            m_getawayReplacement = false;
            m_getawaySpawnTime = Time.time;
        }

        private void UpdateGetawayServer()
        {
            m_getaways.RemoveAll(truck => truck == null || !truck.IsSpawned);
            if (m_getawayAttemptActive && (m_getawayCouch == null || !m_getawayCouch.IsSpawned))
            {
                // An externally removed couch must not leave an unfinishable purchased delivery.
                FailAndResetActiveDeliveryServer();
                EndGetawayServer();
                return;
            }
            if (m_getawayCouch == null || !HasActiveDelivery || ActiveCouch != m_getawayCouch.NetworkObject)
            {
                if (m_getawayCouch != null || ActiveGetawayTruck != null) EndGetawayServer();
                return;
            }
            if (ActiveGetawayTruck != null || Time.time < m_getawaySpawnTime) return;
            m_getawaySpawnTime = Time.time + m_getawaySettings.DecisionInterval;
            TrafficRoadPortal? selected = null;
            float farthest = -1f;
            bool meetsMinimum = false;
            foreach (TrafficRoadPortal portal in m_getawayRoads.Portals)
            {
                float distance = Vector3.Distance(portal.WorldPosition, m_getawayCouch.transform.position);
                float destinationDistance = ActiveDestination != null
                    ? Vector3.Distance(portal.WorldPosition, ActiveDestination.DropPosition.position) : distance;
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
            truck.InitialiseServer(this, m_getawayCouch, m_getawayRoads, m_getawayReplacement);
            m_getaways.Add(truck);
            m_activeGetaway.Value = instance;
            m_getawaySpawnTime = float.PositiveInfinity;
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
            if (!IsServerInitialized || ActiveGetawayTruck != truck) return;
            m_activeGetaway.Value = null;
            if (m_getawayCouch == null || !HasActiveDelivery) return;
            m_getawayReplacement = true;
            m_getawaySpawnTime = Time.time + m_getawaySettings.ReplacementDelay;
        }

        internal void GetawayEscapedServer(GetawayTruckController truck, CouchCarryController couch)
        {
            if (!IsServerInitialized || ActiveGetawayTruck != truck || m_getawayCouch != couch ||
                ActiveCouch != couch.NetworkObject || !couch.IsLoaded || couch.LoadedTruckObject != truck.NetworkObject) return;
            // Detach before the delivery despawns the couch; no child survives under the truck.
            couch.transform.SetParent(null, true);
            EndGetawayServer();
            FailAndResetActiveDeliveryServer();
            if (truck != null && truck.IsSpawned) Despawn(truck.NetworkObject);
        }

        private void EndGetawayServer()
        {
            m_getawayAttemptActive = false;
            m_getawaySpawnTime = float.PositiveInfinity;
            m_getawayCouch = null;
            GetawayTruckController truck = ActiveGetawayTruck;
            m_activeGetaway.Value = null;
            if (truck != null && truck.IsSpawned) truck.DepartServer();
        }

        private void ClearGetawaysServer()
        {
            if (!IsServerInitialized) return;
            EndGetawayServer();
            foreach (GetawayTruckController truck in m_getaways)
                if (truck != null && truck.IsSpawned) Despawn(truck.NetworkObject);
            m_getaways.Clear();
        }

    }
}
