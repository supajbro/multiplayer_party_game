using System;
using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.Gameplay.Shop
{
    [CreateAssetMenu(fileName = "ShopCatalogue", menuName = "Couch Guys/Shop/Catalogue")]
    public sealed class ShopCatalogue : ScriptableObject
    {
        [SerializeField] private ShopItemDefinition[] m_items = Array.Empty<ShopItemDefinition>();

        public IReadOnlyList<ShopItemDefinition> Items => m_items;

        public ShopItemDefinition Find(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || m_items == null) return null;
            if (itemId == "van_light") itemId = "truck_light"; // Existing saves/purchase requests.
            for (int index = 0; index < m_items.Length; index++)
            {
                ShopItemDefinition item = m_items[index];
                if (item != null && string.Equals(item.ItemId, itemId, StringComparison.Ordinal))
                    return item;
            }
            return null;
        }

        private void OnValidate()
        {
            HashSet<string> ids = new(StringComparer.Ordinal);
            if (m_items == null) return;
            foreach (ShopItemDefinition item in m_items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ItemId)) continue;
                if (!ids.Add(item.ItemId))
                    Debug.LogError($"Duplicate shop item ID '{item.ItemId}' in {name}.", this);
            }
        }
    }
}
