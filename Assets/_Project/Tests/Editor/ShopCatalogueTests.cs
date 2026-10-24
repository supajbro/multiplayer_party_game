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
        public void DefaultCatalogueContainsConfiguredTrucks()
        {
            ShopCatalogue catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(CataloguePath);
            Assert.That(catalogue, Is.Not.Null);
            AssertItem(catalogue, "van_light", 250);
            AssertItem(catalogue, "van_good", 500);
            AssertItem(catalogue, "van_best", 1000);
            ShopItemDefinition sled = catalogue.Find("sled");
            Assert.That(sled, Is.Not.Null, "Missing sled");
            Assert.That(sled.Price, Is.EqualTo(150));
            Assert.That(sled.PurchaseBehaviour, Is.TypeOf<SledPurchaseBehaviour>());
            Assert.That(((SledPurchaseBehaviour)sled.PurchaseBehaviour).SledPrefab, Is.Not.Null);
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
