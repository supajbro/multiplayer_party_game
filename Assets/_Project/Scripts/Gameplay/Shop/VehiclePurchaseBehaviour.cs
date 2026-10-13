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

        public NetworkObject VehiclePrefab => m_vehiclePrefab;

        public override bool CanPurchase(
            DeliveryManager manager,
            DeliveryNPC npc,
            PlayerCouchCarrier player,
            out string unavailableReason)
        {
            if (m_vehiclePrefab == null || m_vehiclePrefab.GetComponent<VanController>() == null)
            {
                unavailableReason = "Vehicle is not configured";
                return false;
            }

            if (!manager.CanSpawnPurchasedVehicleServer(m_vehiclePrefab, npc, out unavailableReason))
                return false;

            unavailableReason = string.Empty;
            return true;
        }

        public override bool TryFulfil(
            DeliveryManager manager,
            DeliveryNPC npc,
            PlayerCouchCarrier player,
            out string failureReason) =>
            manager.TrySpawnPurchasedVehicleServer(m_vehiclePrefab, npc, out failureReason);
    }
}
