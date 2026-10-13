using UnityEngine;

namespace CouchGuys.Gameplay.Shop
{
    public enum ShopItemCategory
    {
        Vehicle,
        Weapon,
        Equipment,
        PlayerUpgrade,
        Cosmetic
    }

    /// <summary>Display and transaction data for one repeatable shop purchase.</summary>
    [CreateAssetMenu(fileName = "ShopItem", menuName = "Couch Guys/Shop/Item")]
    public sealed class ShopItemDefinition : ScriptableObject
    {
        [SerializeField] private string m_itemId = "new_item";
        [SerializeField] private string m_displayName = "New Item";
        [SerializeField, TextArea(2, 4)] private string m_description;
        [SerializeField, Min(0)] private int m_price;
        [SerializeField] private ShopItemCategory m_category;
        [SerializeField] private Sprite m_icon;
        [SerializeField] private ShopPurchaseBehaviour m_purchaseBehaviour;

        public string ItemId => m_itemId;
        public string DisplayName => m_displayName;
        public string Description => m_description;
        public int Price => m_price;
        public ShopItemCategory Category => m_category;
        public Sprite Icon => m_icon;
        public ShopPurchaseBehaviour PurchaseBehaviour => m_purchaseBehaviour;

        private void OnValidate()
        {
            m_itemId = string.IsNullOrWhiteSpace(m_itemId) ? name : m_itemId.Trim();
            m_price = Mathf.Max(0, m_price);
        }
    }
}
