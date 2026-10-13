using CouchGuys.Gameplay.Delivery;
using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.Gameplay.Shop
{
    /// <summary>
    /// Server-side fulfilment strategy for a shop item. New item types add a new
    /// behaviour asset rather than branching inside the Delivery Manager.
    /// </summary>
    public abstract class ShopPurchaseBehaviour : ScriptableObject
    {
        public abstract bool CanPurchase(
            DeliveryManager manager,
            DeliveryNPC npc,
            PlayerCouchCarrier player,
            out string unavailableReason);

        public abstract bool TryFulfil(
            DeliveryManager manager,
            DeliveryNPC npc,
            PlayerCouchCarrier player,
            out string failureReason);
    }
}
