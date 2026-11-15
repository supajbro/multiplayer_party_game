using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Shop
{
    [CreateAssetMenu(fileName = "VehiclePurchase", menuName = "Couch Guys/Shop/Purchase Behaviours/Vehicle")]
    public sealed class VehiclePurchaseBehaviour : ShopPurchaseBehaviour
    {
        [SerializeField] private NetworkObject m_vehiclePrefab;
        [SerializeField, Tooltip("Minimum clear space for this vehicle. Zero uses the shop's default clearance.")]
        private Vector3 m_spawnClearanceSize;

        public NetworkObject VehiclePrefab => m_vehiclePrefab;

        public override bool CanPurchase(
            DeliveryManager manager,
            DeliveryNPC npc,
            PlayerCouchCarrier player,
            out string unavailableReason)
        {
            if (m_vehiclePrefab == null || (m_vehiclePrefab.GetComponent<TruckController>() == null &&
                m_vehiclePrefab.GetComponent<MagneticHoverboardController>() == null))
            {
                unavailableReason = "Vehicle is not configured";
                return false;
            }

            if (!manager.CanSpawnPurchasedVehicleServer(m_vehiclePrefab, npc, out unavailableReason, player, m_spawnClearanceSize))
                return false;

            unavailableReason = string.Empty;
            return true;
        }

        public override bool TryFulfil(
            DeliveryManager manager,
            DeliveryNPC npc,
            PlayerCouchCarrier player,
            out string failureReason) =>
            manager.TrySpawnPurchasedVehicleServer(m_vehiclePrefab, npc, out failureReason, player, m_spawnClearanceSize);
    }
}
