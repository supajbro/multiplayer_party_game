using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Shop;
using NUnit.Framework;
using UnityEditor;

namespace CouchGuys.Tests.Editor
{
    public sealed class ShopCatalogueTests
    {
        private const string CataloguePath =
            "Assets/_Project/Resources/Shop/DeliveryManagerShop.asset";

        [Test]
        public void DefaultCatalogueContainsTruckAndExcludesRemovedVehicles()
        {
            ShopCatalogue catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(CataloguePath);
            Assert.That(catalogue, Is.Not.Null);
            AssertItem(catalogue, "van_light", 250);
            AssertItem(catalogue, "truck", 250);
            Assert.That(catalogue.Find("van_good"), Is.Null);
            Assert.That(catalogue.Find("van_best"), Is.Null);
            Assert.That(catalogue.Find("sled"), Is.Null);
        }

        [TestCase("EmergencyDuctTape")]
        [TestCase("ProbablyFineRepairSpray")]
        [TestCase("PremiumRobotOil")]
        public void HealingItemsAreAvailableAtWeaponTerminal(string itemId)
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(CataloguePath);
            var item = catalogue.Find(itemId);
            Assert.That(item, Is.Not.Null);
            Assert.That(DeliveryManager.IsShopItemAllowed(item, true), Is.True);
            Assert.That(DeliveryManager.IsShopItemAllowed(item, true, ShopItemCategory.Vehicle), Is.False);
        }

        [Test]
        public void DispatcherRejectsEveryShopItem()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(CataloguePath);
            foreach (var item in catalogue.Items)
                Assert.That(DeliveryManager.IsShopItemAllowed(item, false), Is.False, item.name);
        }

        private static void AssertItem(ShopCatalogue catalogue, string itemId, int price)
        {
            ShopItemDefinition item = catalogue.Find(itemId);
            Assert.That(item, Is.Not.Null, $"Missing {itemId}");
            Assert.That(item.Price, Is.EqualTo(price));
            Assert.That(item.PurchaseBehaviour, Is.TypeOf<VehiclePurchaseBehaviour>());
            Assert.That(((VehiclePurchaseBehaviour)item.PurchaseBehaviour).VehiclePrefab, Is.Not.Null);
        }
    }
}
