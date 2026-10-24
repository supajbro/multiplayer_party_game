using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.Gameplay.Shop
{
    [CreateAssetMenu(fileName = "WeaponPurchase", menuName = "Couch Guys/Shop/Purchase Behaviours/Weapon")]
    public sealed class WeaponPurchaseBehaviour : ShopPurchaseBehaviour
    {
        [SerializeField] private PlayerWeaponType m_weaponType;
        public override bool CanPurchase(DeliveryManager manager, DeliveryNPC npc, PlayerCouchCarrier player,
            out string unavailableReason)
        {
            bool available = manager != null && manager.IsServerInitialized && player != null &&
                player.TryGetComponent(out PlayerWeaponController weapon) && weapon.CanHandleWeaponServer;
            unavailableReason = available ? null : "You cannot equip a weapon right now";
            return available;
        }

        public override bool TryFulfil(DeliveryManager manager, DeliveryNPC npc, PlayerCouchCarrier player,
            out string failureReason)
        {
            if (!CanPurchase(manager, npc, player, out failureReason)) return false;
            bool success = player.GetComponent<PlayerWeaponController>().AcquireWeaponServer(m_weaponType);
            failureReason = success ? null : "Weapon could not be equipped";
            return success;
        }
    }
}
