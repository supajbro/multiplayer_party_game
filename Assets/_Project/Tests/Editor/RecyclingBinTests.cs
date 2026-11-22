using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Healing;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class RecyclingBinTests
    {
        private const string BinPath = "Assets/_Project/Prefabs/Props/RecyclingBin.prefab";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        [Test]
        public void DepotInheritsSavedIntakeAndRefundSettings()
        {
            var depot = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/World/depot_02.prefab");
            try
            {
                var bin = depot.GetComponentsInChildren<RecyclingBin>(true).Single();
                var data = new SerializedObject(bin);
                var intake = (BoxCollider)data.FindProperty("m_intake").objectReferenceValue;
                Assert.That(intake, Is.Not.Null);
                Assert.That(intake.isTrigger && intake.enabled, Is.True);
                Assert.That(bin.Contains(bin.EntryPoint), Is.True);
                Assert.That(bin.Contains(bin.EntryPoint + Vector3.up * 2f), Is.False);
                Assert.That(data.FindProperty("m_refundFraction").floatValue, Is.EqualTo(.25f));
                Assert.That(bin.GetComponentsInChildren<MeshCollider>().Length, Is.EqualTo(9), "Preserve the eight hollow walls and floor.");
            }
            finally { PrefabUtility.UnloadPrefabContents(depot); }
        }

        [Test]
        public void AssistanceRequiresProximityFacingAndAnUnobstructedArc()
        {
            var binObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BinPath));
            var player = new GameObject("Recycling test player");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                binObject.transform.position = new Vector3(500, 100, 500);
                var bin = binObject.GetComponent<RecyclingBin>();
                player.transform.position = binObject.transform.position + Vector3.back * 3f;
                player.transform.rotation = Quaternion.identity;
                Assert.That((bool)Call(bin, "IsInRangeAndFacing", player.transform), Is.True);
                player.transform.rotation = Quaternion.Euler(0, 90, 0);
                Assert.That((bool)Call(bin, "IsInRangeAndFacing", player.transform), Is.False, "Sideways throws should stay normal.");
                player.transform.rotation = Quaternion.Euler(0, 180, 0);
                Assert.That((bool)Call(bin, "IsInRangeAndFacing", player.transform), Is.False);
                player.transform.rotation = Quaternion.identity;
                player.transform.position += Vector3.back * 5f;
                Assert.That((bool)Call(bin, "IsInRangeAndFacing", player.transform), Is.False);
                player.transform.position += Vector3.forward * 5f;
                Vector3 start = player.transform.position + Vector3.up + Vector3.forward * .65f;
                wall.transform.position = binObject.transform.position + Vector3.right * 20f;
                Physics.SyncTransforms();
                Assert.That((bool)Call(bin, "HasClearThrowPath", player.transform, start), Is.True);
                wall.transform.position = bin.GetFlightPosition(start, .4f);
                wall.transform.localScale = new Vector3(4, .2f, 4);
                Physics.SyncTransforms();
                Assert.That((bool)Call(bin, "HasClearThrowPath", player.transform, start), Is.False, "A low ceiling must block assistance.");
                Assert.That(Vector3.Distance(bin.GetFlightPosition(start, 0), start), Is.LessThan(.001f));
                Assert.That(bin.Contains(bin.GetFlightPosition(start, 1)), Is.True);
                Assert.That(bin.GetFlightPosition(start, .8f).y, Is.GreaterThan(binObject.transform.position.y + 1.63f), "Descend through the opening above the rim.");
            }
            finally { Object.DestroyImmediate(binObject); Object.DestroyImmediate(player); Object.DestroyImmediate(wall); }
        }

        [Test]
        public void RefundUsesEveryWeaponPriceAndHealingPriceWithWholeCurrencyRounding()
        {
            var catalogue = Resources.Load<ShopCatalogue>("Shop/DeliveryManagerShop");
            var obj = new GameObject("Recycle price test"); obj.SetActive(false);
            var pickup = obj.AddComponent<WeaponPickup>();
            try
            {
                var data = new SerializedObject(pickup);
                int checkedWeapons = 0;
                foreach (var item in catalogue.Items.Where(i => i != null && i.PurchaseBehaviour is WeaponPurchaseBehaviour))
                {
                    data.FindProperty("m_weaponType").intValue = item.InventoryId;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    Assert.That(RecyclingBin.TryGetRefund(pickup, catalogue, .25f, out int value), Is.True, item.name);
                    Assert.That(value, Is.EqualTo(item.Price / 4), item.name);
                    checkedWeapons++;
                }
                Assert.That(checkedWeapons, Is.GreaterThanOrEqualTo(6));
                data.FindProperty("m_weaponType").intValue = 999;
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(RecyclingBin.TryGetRefund(pickup, catalogue, .25f, out _), Is.False);
                foreach (var item in HealingItemCatalogue.Load().Items.Where(i => i != null))
                {
                    data.FindProperty("m_healingItem").objectReferenceValue = item;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    Assert.That(RecyclingBin.TryGetRefund(pickup, null, .25f, out int value), Is.True);
                    Assert.That(value, Is.EqualTo(item.PurchasePrice / 4));
                }
                Assert.That(pickup.TryPickupServer(null), Is.False, "Unspawned pickups cannot be claimed.");
            }
            finally { Object.DestroyImmediate(obj); }
        }
    }

    [InitializeOnLoad]
    public static class RecyclingBinValidation
    {
        static RecyclingBinValidation() => EditorApplication.update += CheckRequest;
        private static void CheckRequest()
        {
            const string request = "Temp/RecyclingBinValidation.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            Run();
        }

        [MenuItem("Couch Guys/Weapons/Validate Recycling Bin")]
        public static void Run()
        {
            try
            {
                var tests = new RecyclingBinTests();
                tests.DepotInheritsSavedIntakeAndRefundSettings();
                tests.AssistanceRequiresProximityFacingAndAnUnobstructedArc();
                tests.RefundUsesEveryWeaponPriceAndHealingPriceWithWholeCurrencyRounding();
                File.WriteAllText("Temp/RecyclingBinValidation.txt", "PASS: depot nested prefab intake; proximity and facing gates; low-ceiling obstruction; arc arrival inside bin; all weapon and healing prices; quarter-price rounding and unknown-price preservation.\n");
            }
            catch (Exception e) { File.WriteAllText("Temp/RecyclingBinValidation.txt", "FAIL: " + e); Debug.LogException(e); }
        }
    }
}
