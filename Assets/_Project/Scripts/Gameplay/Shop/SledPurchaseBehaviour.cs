using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Shop
{
    [CreateAssetMenu(fileName = "SledPurchase", menuName = "Couch Guys/Shop/Purchase Behaviours/Sled")]
    public sealed class SledPurchaseBehaviour : ShopPurchaseBehaviour
    {
        [SerializeField] private NetworkObject m_sledPrefab;
        public NetworkObject SledPrefab => m_sledPrefab;

        public override bool CanPurchase(
            DeliveryManager manager, DeliveryNPC npc, PlayerCouchCarrier player,
            out string unavailableReason)
        {
            if (m_sledPrefab == null || m_sledPrefab.GetComponent<SledController>() == null)
            {
                unavailableReason = "Sled is not configured";
                return false;
            }
            return manager.CanSpawnPurchasedVehicleServer(m_sledPrefab, npc, out unavailableReason);
        }

        public override bool TryFulfil(
            DeliveryManager manager, DeliveryNPC npc, PlayerCouchCarrier player,
            out string failureReason) =>
            manager.TrySpawnPurchasedVehicleServer(m_sledPrefab, npc, out failureReason);
    }
}
