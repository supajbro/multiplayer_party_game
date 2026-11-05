using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.Gameplay.Healing
{
    // The same asset is both the consumable definition and its shop fulfilment strategy.
    [CreateAssetMenu(menuName = "Couch Guys/Healing Item", fileName = "HealingItem")]
    public sealed class HealingItemSettings : ShopPurchaseBehaviour
    {
        [SerializeField] private string m_displayName;
        [SerializeField, Range(0f, 100f)] private float m_healingPercentage = 10f;
        [SerializeField, Min(0)] private int m_purchasePrice = 25;
        [Tooltip("Absolute probability per thief death. All item probabilities together must be <= 1.")]
        [SerializeField, Range(0f, 1f)] private float m_dropChance = .025f;
        [SerializeField, Min(.1f)] private float m_useDuration = 1f;
        [SerializeField] private Sprite m_icon;
        [SerializeField] private WeaponPickup m_pickupPrefab;
        [SerializeField] private GameObject m_useEffectPrefab;
        [SerializeField] private GameObject m_healEffectPrefab;
        [SerializeField] private AudioClip m_useAudio;
        [SerializeField] private AudioClip m_healAudio;
        [SerializeField, Min(.1f)] private float m_effectLifetime = 3f;

        public string DisplayName => m_displayName;
        public float HealingPercentage => Mathf.Clamp(m_healingPercentage, 0f, 100f);
        public int PurchasePrice => Mathf.Max(0, m_purchasePrice);
        public float DropChance => Mathf.Clamp01(m_dropChance);
        public float UseDuration => Mathf.Max(.1f, m_useDuration);
        public Sprite Icon => m_icon;
        public WeaponPickup PickupPrefab => m_pickupPrefab;
        public GameObject UseEffectPrefab => m_useEffectPrefab;
        public GameObject HealEffectPrefab => m_healEffectPrefab;
        public AudioClip UseAudio => m_useAudio;
        public AudioClip HealAudio => m_healAudio;
        public float EffectLifetime => Mathf.Max(.1f, m_effectLifetime);

        public override bool CanPurchase(DeliveryManager manager, DeliveryNPC npc, PlayerCouchCarrier player,
            out string unavailableReason)
        {
            bool available = manager != null && manager.IsServerInitialized && player != null &&
                player.TryGetComponent(out PlayerWeaponController inventory) && inventory.CanAcquireHealingItemServer(this);
            unavailableReason = available ? null : "Cannot carry this item (inventory full or player unavailable)";
            return available;
        }

        public override bool TryFulfil(DeliveryManager manager, DeliveryNPC npc, PlayerCouchCarrier player,
            out string failureReason)
        {
            if (!CanPurchase(manager, npc, player, out failureReason)) return false;
            bool success = player.GetComponent<PlayerWeaponController>().AcquireHealingItemServer(this);
            failureReason = success ? null : "Inventory is full";
            return success;
        }
    }
}
