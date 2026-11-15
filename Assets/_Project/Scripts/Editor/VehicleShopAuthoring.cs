using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Shop;
using CouchGuys.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit creation of standalone vehicle shop prefabs using the saved weapon shop assets.</summary>
    public static class VehicleShopAuthoring
    {
        public const string TerminalPath = "Assets/_Project/Prefabs/UI/VehicleShopTerminal.prefab";
        public const string DisplayPath = "Assets/_Project/Prefabs/UI/VehicleShopDisplay.prefab";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Couch Guys/Vehicle Shop/Create Missing Prefabs")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before authoring prefabs.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TerminalPath) == null)
            {
                if (!AssetDatabase.CopyAsset(WeaponTerminalAuthoring.PrefabPath, TerminalPath)) throw new Exception("Could not copy the terminal prefab.");
                var root = PrefabUtility.LoadPrefabContents(TerminalPath);
                try
                {
                    root.name = "VehicleShopTerminal";
                    var marker = root.GetComponent<WeaponShopTerminal>();
                    var data = new SerializedObject(marker);
                    data.FindProperty("m_category").enumValueIndex = (int)ShopItemCategory.Vehicle;
                    data.FindProperty("m_prompt").stringValue = "Press E to browse vehicles";
                    var spawn = new GameObject("VehicleSpawnPoint").transform;
                    spawn.SetParent(root.transform, false);
                    spawn.localPosition = new Vector3(6f, 0f, 1.5f);
                    data.FindProperty("m_vehicleSpawnPoint").objectReferenceValue = spawn;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    data = new SerializedObject(root.GetComponent<DispatcherMenuController>());
                    data.FindProperty("m_defaultNotice").stringValue = "VEHICLE SHOP | Click to buy | E / ESC to return";
                    data.ApplyModifiedPropertiesWithoutUndo();
                    var idle = (GameObject)data.FindProperty("m_idleScreen").objectReferenceValue;
                    idle.transform.Find("Title").GetComponent<TMP_Text>().text = "VEHICLE SHOP";
                    PrefabUtility.SaveAsPrefabAsset(root, TerminalPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(DisplayPath) == null)
            {
                if (!AssetDatabase.CopyAsset(WeaponAdvertisementAuthoring.PrefabPath, DisplayPath)) throw new Exception("Could not copy the display prefab.");
                var root = PrefabUtility.LoadPrefabContents(DisplayPath);
                try
                {
                    root.name = "VehicleShopDisplay";
                    var data = new SerializedObject(root.GetComponent<WeaponAdvertisementDisplay>());
                    data.FindProperty("m_category").enumValueIndex = (int)ShopItemCategory.Vehicle;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
                    var first = catalogue.Items.First(item => item != null && item.Category == ShopItemCategory.Vehicle && item.PurchaseBehaviour != null);
                    foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (label.name == "Featured") label.text = "FEATURED VEHICLE";
                        if (label.name == "PlaceholderText") label.text = "[ VEHICLE IMAGE ]";
                        if (label.name == "WeaponName") label.text = first.DisplayName;
                        if (label.name == "WeaponPrice") label.text = "$" + first.Price.ToString("N0", CultureInfo.InvariantCulture);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, DisplayPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Validate();
        }

        [MenuItem("Couch Guys/Vehicle Shop/Validate Prefabs And Advertising")]
        public static void Validate()
        {
            var terminal = AssetDatabase.LoadAssetAtPath<GameObject>(TerminalPath);
            Require(terminal != null, "Vehicle terminal prefab exists");
            var marker = terminal.GetComponent<WeaponShopTerminal>();
            Require(marker != null && marker.Category == ShopItemCategory.Vehicle, "Terminal uses the shared vehicle category filter");
            var controller = new SerializedObject(terminal.GetComponent<DispatcherMenuController>());
            foreach (string field in new[] { "m_menuPrefab", "m_cameraFocusPoint", "m_screenCanvasAnchor", "m_idleScreen" })
                Require(controller.FindProperty(field).objectReferenceValue != null, "Terminal reference: " + field);
            var catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
            var vehicles = catalogue.Items.Where(item => item != null && item.Category == ShopItemCategory.Vehicle && item.PurchaseBehaviour != null).Distinct().ToArray();
            Require(vehicles.Length >= 2, "Existing vehicles are advertised");
            foreach (var item in vehicles)
            {
                var prefab = (item.PurchaseBehaviour as VehiclePurchaseBehaviour)?.VehiclePrefab ??
                    (item.PurchaseBehaviour as SledPurchaseBehaviour)?.SledPrefab;
                Require(prefab != null, "Vehicle has an existing saved purchase prefab: " + item.DisplayName);
            }

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DisplayPath), scene);
                var display = instance.GetComponent<WeaponAdvertisementDisplay>();
                var data = new SerializedObject(display);
                Require(data.FindProperty("m_category").enumValueIndex == (int)ShopItemCategory.Vehicle, "Display shares the vehicle filter");
                Require(instance.GetComponentInChildren<Canvas>().renderMode == RenderMode.WorldSpace, "Advertising uses a world-space canvas");
                Require(instance.GetComponentsInChildren<Selectable>(true).Length == 0 && instance.GetComponentsInChildren<GraphicRaycaster>(true).Length == 0, "Advertising has no interaction");
                var start = typeof(WeaponAdvertisementDisplay).GetMethod("OnEnable", Private);
                var advance = typeof(WeaponAdvertisementDisplay).GetMethod("Advance", Private);
                start.Invoke(display, null);
                var panels = new[] { "m_firstPanel", "m_secondPanel" }.Select(field =>
                    (RectTransform)data.FindProperty(field).FindPropertyRelative("m_rect").objectReferenceValue).ToArray();
                for (int index = 0; index <= vehicles.Length * 2; index++)
                {
                    if (index > 0)
                    {
                        advance.Invoke(display, new object[] { index == 1 ? 5f : 4.35f });
                        advance.Invoke(display, new object[] { .325f });
                        Require(panels.All(panel => panel.gameObject.activeSelf), "Both panels participate in the slide");
                        Require(Mathf.Abs(Mathf.Abs(panels[0].anchoredPosition.x - panels[1].anchoredPosition.x) - 1280f) < .1f, "Slide has no gaps");
                        advance.Invoke(display, new object[] { .325f });
                    }
                    var visible = panels.Single(panel => panel.gameObject.activeSelf);
                    var item = vehicles[index % vehicles.Length];
                    Require(visible.Find("WeaponName").GetComponent<TMP_Text>().text == item.DisplayName, "Advertised name comes from catalogue");
                    Require(visible.Find("WeaponPrice").GetComponent<TMP_Text>().text == "$" + item.Price.ToString("N0", CultureInfo.InvariantCulture), "Advertised price comes from catalogue");
                }
                typeof(WeaponAdvertisementAuthoring).GetMethod("ValidateCatalogueChanges", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { display, catalogue, vehicles[0], start, advance });
                typeof(WeaponAdvertisementAuthoring).GetMethod("RenderPreview", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { instance, scene });
                File.Copy("Temp/WeaponAdvertisementPreview.png", "Temp/VehicleShopDisplayPreview.png", true);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            File.WriteAllText("Temp/VehicleShopValidation.txt", "PASS: shared terminal wiring, existing vehicle definitions, world-space noninteractive advertising, two gap-free loops with correct names/prices.\n" +
                string.Join("\n", vehicles.Select(item => item.DisplayName + " $" + item.Price)) + "\n");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
