using System.Collections.Generic;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using FishNet;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    public sealed partial class TrafficManager
    {
        private readonly List<NetworkObject> m_rogueCars = new();
        private readonly HashSet<Vector3> m_rogueSpawnPoints = new();
        private DeliveryManager m_rogueDelivery;
        private NetworkObject m_rogueCouch;
        private int m_rogueSpawnCount;
        private float m_rogueCheckAt;
        [SerializeField, TextArea, Tooltip("Runtime diagnostic for the independent Rogue Car spawn timer.")]
        private string m_rogueSpawnStatus = "Waiting for an active Hard delivery.";

        private DeliveryTierConfig GetActiveRogueTier()
        {
            if (m_rogueDelivery == null) m_rogueDelivery = GetComponent<DeliveryManager>();
            DeliveryTierConfig tier = m_rogueDelivery != null
                ? m_rogueDelivery.GetTierConfig(m_rogueDelivery.ActiveTierIndex) : null;
            return tier != null && tier.Tier == DeliveryTier.Hard &&
                m_rogueDelivery.State == DeliveryState.Transport && m_rogueDelivery.ActiveCouch != null
                ? tier : null;
        }

        private void TrackRogueDelivery(RogueCarSettings settings)
        {
            if (m_rogueCouch == m_rogueDelivery.ActiveCouch) return;
            ClearRogueCars();
            m_rogueCouch = m_rogueDelivery.ActiveCouch;
            m_rogueCheckAt = Time.time + Mathf.Max(0f, settings.InitialSpawnDelay);
        }

        private void UpdateRogueSpawning()
        {
            if (!IsServerStarted())
            {
                m_rogueCars.Clear();
                m_rogueCouch = null;
                m_rogueSpawnStatus = "Waiting for the server.";
                return;
            }
            DeliveryTierConfig tier = GetActiveRogueTier();
            if (tier == null)
            {
                ClearRogueCars();
                m_rogueSpawnStatus = "Waiting for an active Hard delivery in Transport state.";
                return;
            }
            if (!m_regionReady || tier.RogueCarSettings == null || tier.RogueCarPrefab == null)
            {
                m_rogueSpawnStatus = "Cannot spawn: missing road routes, Rogue Car Settings, or Rogue Car Prefab on the Hard tier.";
                return;
            }
            RogueCarSettings settings = tier.RogueCarSettings;
            TrackRogueDelivery(settings);
            m_rogueCars.RemoveAll(car => car == null || !car.IsSpawned);
            if (m_rogueCars.Count >= settings.MaxActiveCars || m_rogueSpawnCount >= settings.MaxSpawnsPerDelivery)
            {
                m_rogueSpawnStatus = $"Spawn limit reached. Active: {m_rogueCars.Count}; spawned this delivery: {m_rogueSpawnCount}.";
                return;
            }
            if (Time.time < m_rogueCheckAt)
            {
                m_rogueSpawnStatus = $"Next check in {m_rogueCheckAt - Time.time:F0}s ({settings.SpawnChance:P0} chance).";
                return;
            }
            m_rogueCheckAt = Time.time + Mathf.Max(0.1f, settings.SpawnCheckInterval);
            if (Random.value >= Mathf.Clamp01(settings.SpawnChance))
            {
                m_rogueSpawnStatus = "Chance roll skipped this check.";
                return;
            }
            TrySpawnRogueCar(tier);
        }

        [ContextMenu("Spawn Rogue Car Now (Active Hard Delivery Only)")]
        public void SpawnRogueCarNow()
        {
            DeliveryTierConfig tier = IsServerStarted() ? GetActiveRogueTier() : null;
            if (tier == null || tier.RogueCarSettings == null || tier.RogueCarPrefab == null || !m_regionReady)
            {
                Debug.LogWarning("Rogue Car test spawn requires a running server, an active Hard delivery, generated roads, and assigned Rogue Car settings/prefab.", this);
                return;
            }
            TrackRogueDelivery(tier.RogueCarSettings);
            m_rogueCars.RemoveAll(car => car == null || !car.IsSpawned);
            if (m_rogueCars.Count >= tier.RogueCarSettings.MaxActiveCars ||
                m_rogueSpawnCount >= tier.RogueCarSettings.MaxSpawnsPerDelivery)
            {
                Debug.LogWarning("Rogue Car test spawn blocked by the configured active/per-delivery limits.", this);
                return;
            }
            // Only bypass timing and the chance roll. Hard-only, clearance and limits still apply.
            TrySpawnRogueCar(tier);
        }

        private bool TryFindRogueSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            float bestScore = float.PositiveInfinity;
            bool found = false;
            m_rogueSpawnPoints.Clear();
            float minDistance = Mathf.Max(24f, m_settings.MinimumPlayerSpawnDistance);
            foreach (TrafficRoute route in m_laneGraph.Routes)
            for (int i = 0; i < route.Waypoints.Length - 1; i++)
            {
                Vector3 roadPoint = route.Waypoints[i];
                if (!m_rogueSpawnPoints.Add(roadPoint)) continue;
                Vector3 candidate = roadPoint + Vector3.up * 0.7f;
                Vector3 direction = Vector3.ProjectOnPlane(route.Waypoints[i + 1] - roadPoint, Vector3.up);
                if (direction.sqrMagnitude < 0.01f) continue;
                bool visible = false;
                float nearestPlayer = float.PositiveInfinity;
                foreach (PlayerHealth player in PlayerHealth.Instances)
                {
                    if (player == null || !player.IsSpawned || !player.IsAlive) continue;
                    Vector3 offset = candidate - player.transform.position;
                    nearestPlayer = Mathf.Min(nearestPlayer, offset.sqrMagnitude);
                    visible |= Vector3.Dot(player.transform.forward, offset.normalized) > 0f;
                }
                if (nearestPlayer < minDistance * minDistance || float.IsPositiveInfinity(nearestPlayer)) continue;
                // Prefer nearby points behind players, with visible safe points as a fallback.
                float score = nearestPlayer * (visible ? 4f : 1f);
                if (score >= bestScore || !IsSpawnClear(roadPoint)) continue;
                Quaternion candidateRotation = Quaternion.LookRotation(direction, Vector3.up);
                if (Physics.CheckBox(candidate + Vector3.up * 0.9f, new Vector3(1f, 0.53f, 2f),
                    candidateRotation, ~0, QueryTriggerInteraction.Ignore)) continue;
                position = candidate;
                rotation = candidateRotation;
                bestScore = score;
                found = true;
            }
            return found;
        }

        private void TrySpawnRogueCar(DeliveryTierConfig tier)
        {
            if (m_settings == null || !TryFindRogueSpawnPose(out Vector3 position, out Quaternion rotation))
            {
                m_rogueSpawnStatus = "No clear road spawn point outside the player safety distance.";
                Debug.LogWarning("Rogue Car spawn skipped: no clear vehicle spawn point. Will retry at the next check.", this);
                return;
            }
            NetworkObject prefab = tier.RogueCarPrefab;
            if (prefab.GetComponent<TrafficVehicleController>() == null || prefab.GetComponent<RogueCar>() == null)
            {
                m_rogueSpawnStatus = "Invalid Rogue Car Prefab: missing TrafficVehicleController or RogueCar.";
                Debug.LogError(m_rogueSpawnStatus, this);
                return;
            }
            NetworkObject instance = Instantiate(prefab, position, rotation);
            TrafficVehicleController vehicle = instance.GetComponent<TrafficVehicleController>();
            InstanceFinder.ServerManager.Spawn(instance);
            vehicle.InitialiseRogueServer(tier.RogueCarSettings, this,
                vehicle.VisualVariantCount > 0 ? Random.Range(0, vehicle.VisualVariantCount) : 0);
            m_rogueCars.Add(instance);
            m_rogueSpawnCount++;
            m_rogueSpawnStatus = $"Spawned {instance.name} at {position}.";
            Debug.Log($"Rogue Car spawned at {position} for the active Hard delivery.", instance);
        }

        private void ClearRogueCars()
        {
            if (IsServerStarted())
                foreach (NetworkObject car in m_rogueCars)
                    if (car != null && car.IsSpawned) InstanceFinder.ServerManager.Despawn(car);
            m_rogueCars.Clear();
            m_rogueCouch = null;
            m_rogueSpawnCount = 0;
        }
    }
}
