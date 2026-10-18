using System;
using System.Collections.Generic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Server-authoritative hand-off point for the next-region progression system.
    /// Generated locally with the deterministic world, but only server physics may
    /// accept an occupied player vehicle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RegionTransitionTrigger : MonoBehaviour
    {
        public static event Action<VanController, string> ServerTransitionReached;

        [SerializeField] private string m_destinationRegionId = "next-region";
        private readonly HashSet<int> m_acceptedVehicles = new HashSet<int>();

        public string DestinationRegionId => m_destinationRegionId;

        public void Initialise(string destinationRegionId)
        {
            m_destinationRegionId = string.IsNullOrWhiteSpace(destinationRegionId)
                ? "next-region"
                : destinationRegionId.Trim().ToLowerInvariant();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (InstanceFinder.NetworkManager == null ||
                !InstanceFinder.NetworkManager.ServerManager.Started)
            {
                return;
            }

            VanController van = other.GetComponentInParent<VanController>();
            if (van == null || !van.IsServerInitialized || van.IsDestroyed ||
                van.Driver == null || !m_acceptedVehicles.Add(van.GetInstanceID()))
            {
                return;
            }

            if (!van.Driver.TryGetComponent(out PlayerVanInteractor driver) ||
                driver.CurrentVan != van)
            {
                m_acceptedVehicles.Remove(van.GetInstanceID());
                return;
            }

            Debug.Log(
                $"Player vehicle legitimately reached the '{m_destinationRegionId}' region gateway. " +
                "No full region loader is registered, so the placeholder progression event was raised.",
                this);
            driver.ShowVehicleFeedbackServer($"Region route unlocked: {m_destinationRegionId}");
            ServerTransitionReached?.Invoke(van, m_destinationRegionId);
        }

        private void OnTriggerExit(Collider other)
        {
            VanController van = other.GetComponentInParent<VanController>();
            if (van != null) m_acceptedVehicles.Remove(van.GetInstanceID());
        }
    }
}
