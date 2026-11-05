using CouchGuys.Gameplay.Weapons;
using UnityEngine;

namespace CouchGuys.Gameplay.Healing
{
    [CreateAssetMenu(menuName = "Couch Guys/Healing Item Catalogue")]
    public sealed class HealingItemCatalogue : ScriptableObject
    {
        // Stable inventory IDs: append entries; do not reorder existing items.
        public const int FirstInventoryId = 6;
        [SerializeField] private HealingItemSettings[] m_items;
        public HealingItemSettings[] Items => m_items;
        private static HealingItemCatalogue s_loaded;
        public static HealingItemCatalogue Load() => s_loaded != null ? s_loaded : (s_loaded = Resources.Load<HealingItemCatalogue>("Healing/HealingItems"));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => s_loaded = null;
        private void OnValidate()
        {
            if (m_items == null) return;
            float total = 0f;
            foreach (var item in m_items) if (item != null) total += item.DropChance;
            if (total > 1f) Debug.LogError("Healing drop chances must sum to at most 1 (100%).", this);
        }
        public HealingItemSettings Find(int inventoryId) => m_items != null && inventoryId >= FirstInventoryId &&
            inventoryId - FirstInventoryId < m_items.Length ? m_items[inventoryId - FirstInventoryId] : null;
        public int IdOf(HealingItemSettings item)
        {
            if (item == null || m_items == null) return -1;
            int index = System.Array.IndexOf(m_items, item);
            return index >= 0 ? FirstInventoryId + index : -1;
        }
        // A single roll chooses zero or one item, never one roll per item.
        public HealingItemSettings SelectDrop(float roll)
        {
            if (m_items == null || roll < 0f || roll >= 1f) return null;
            float cumulative = 0f;
            foreach (var item in m_items)
            {
                if (item == null) continue;
                cumulative += item.DropChance;
                if (roll < cumulative) return item;
            }
            return null;
        }
        public static void DropFromThiefServer(Vector3 position)
        {
            if (!FishNet.InstanceFinder.IsServerStarted) return;
            var item = Load()?.SelectDrop(Random.value);
            if (item != null) WeaponPickup.SpawnHealingServer(item, position + Vector3.up, Quaternion.identity, Vector3.up);
        }
    }
}
