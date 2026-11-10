using CouchGuys.Gameplay.Healing;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        public const int HealingStackLimit = 6;
        private readonly SyncList<int> m_healingQuantities = new();
        [UnityEngine.SerializeField] private UnityEngine.Transform[] m_healingModels = new UnityEngine.Transform[3];

        public HealingItemSettings GetInventoryHealingItem(int slot) => HealingItemCatalogue.Load()?.Find(GetInventoryWeaponIndex(slot));
        public int GetInventoryHealingQuantity(int slot) => GetInventoryHealingItem(slot) != null &&
            slot >= 0 && slot < m_healingQuantities.Count ? m_healingQuantities[slot] : 0;
        public HealingItemSettings EquippedHealingItem => GetInventoryHealingItem(SelectedHotbarSlot);
        public bool IsItemSwitchReady => UnityEngine.Time.time >= m_switchCompleteAt;
        private int FindEmptyItemSlot()
        {
            for (int slot = 0; slot < m_inventory.Count; slot++)
                if (m_inventory[slot] == -1) return slot;
            return -1;
        }
        private int FindHealingItemSlot(int id)
        {
            for (int slot = 0; slot < m_inventory.Count; slot++)
                if (m_inventory[slot] == id && m_healingQuantities[slot] < HealingStackLimit) return slot;
            return FindEmptyItemSlot();
        }
        public bool CanAcquireHealingItemServer(HealingItemSettings settings)
        {
            int id = HealingItemCatalogue.Load()?.IdOf(settings) ?? -1;
            return CanHandleWeaponServer && id >= 0 && FindHealingItemSlot(id) >= 0;
        }
        [Server]
        public bool AcquireHealingItemServer(HealingItemSettings settings)
        {
            if (!CanAcquireHealingItemServer(settings)) return false;
            int id = HealingItemCatalogue.Load().IdOf(settings);
            int slot = FindHealingItemSlot(id);
            bool empty = m_inventory[slot] == -1;
            m_healingQuantities[slot] = empty ? 1 : m_healingQuantities[slot] + 1;
            m_inventory[slot] = id;
            if (empty && m_equippedIndex.Value < 0) EquipInventorySlotServer(slot);
            return true;
        }
        [Server]
        public bool ConsumeHealingItemServer(int slot, int expectedId)
        {
            if (slot != SelectedHotbarSlot || GetInventoryWeaponIndex(slot) != expectedId ||
                HealingItemCatalogue.Load()?.Find(expectedId) == null || !CanHandleWeaponServer || !m_health.CanHeal ||
                GetInventoryHealingQuantity(slot) <= 0) return false;
            RemoveHealingItemServer(slot);
            return true;
        }

        private void RemoveHealingItemServer(int slot)
        {
            m_healingQuantities[slot]--;
            if (m_healingQuantities[slot] > 0) return;
            m_inventory[slot] = -1;
            if (slot == SelectedHotbarSlot) m_equippedIndex.Value = -1;
        }
    }
}
