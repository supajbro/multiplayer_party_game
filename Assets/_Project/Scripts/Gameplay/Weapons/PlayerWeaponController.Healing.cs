using CouchGuys.Gameplay.Healing;
using FishNet.Object;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        public HealingItemSettings GetInventoryHealingItem(int slot) => HealingItemCatalogue.Load()?.Find(GetInventoryWeaponIndex(slot));
        public HealingItemSettings EquippedHealingItem => GetInventoryHealingItem(SelectedHotbarSlot);
        public bool IsItemSwitchReady => UnityEngine.Time.time >= m_switchCompleteAt;
        private int FindEmptyItemSlot()
        {
            for (int slot = 0; slot < m_inventory.Count; slot++)
                if (m_inventory[slot] == -1) return slot;
            return -1;
        }
        public bool CanAcquireHealingItemServer(HealingItemSettings settings) => CanHandleWeaponServer &&
            HealingItemCatalogue.Load()?.IdOf(settings) >= 0 && FindEmptyItemSlot() >= 0;
        [Server]
        public bool AcquireHealingItemServer(HealingItemSettings settings)
        {
            if (!CanAcquireHealingItemServer(settings)) return false;
            int slot = FindEmptyItemSlot();
            m_inventory[slot] = HealingItemCatalogue.Load().IdOf(settings);
            if (m_equippedIndex.Value < 0) EquipInventorySlotServer(slot);
            return true;
        }
        [Server]
        public bool ConsumeHealingItemServer(int slot, int expectedId)
        {
            if (slot != SelectedHotbarSlot || GetInventoryWeaponIndex(slot) != expectedId ||
                HealingItemCatalogue.Load()?.Find(expectedId) == null || !CanHandleWeaponServer || !m_health.CanHeal) return false;
            m_inventory[slot] = -1;
            m_equippedIndex.Value = -1;
            return true;
        }
    }
}
