using System.Collections.Generic;
using CouchGuys.Gameplay.Shop;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Creates the initial catalogue without overwriting later designer edits.</summary>
    public static class ShopAssetBuilder
    {
        public const string CataloguePath = "Assets/_Project/Resources/Shop/DeliveryManagerShop.asset";
        private const string ShopFolder = "Assets/_Project/Settings/Shop";

        private readonly struct InitialVehicle
        {
            public readonly string Id;
            public readonly string Name;
            public readonly string Description;
            public readonly int Price;
            public readonly string PrefabPath;

            public InitialVehicle(string id, string name, string description, int price, string prefabPath)
            {
                Id = id;
                Name = name;
                Description = description;
                Price = price;
                PrefabPath = prefabPath;
            }
        }

        private static readonly InitialVehicle[] InitialVehicles =
        {
            new("truck_light", "Light Truck", "Nimble starter truck with room for one couch.", 250,
                "Assets/_Project/Prefabs/Vehicles/LightTruck.prefab")
        };

        public static ShopCatalogue EnsureDefaultCatalogue()
        {
            EnsureFolder("Assets/_Project/Settings", "Shop");
            EnsureFolder("Assets/_Project/Resources", "Shop");
            ShopCatalogue catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(CataloguePath);
            bool createdCatalogue = catalogue == null;
            if (createdCatalogue)
            {
                catalogue = ScriptableObject.CreateInstance<ShopCatalogue>();
                AssetDatabase.CreateAsset(catalogue, CataloguePath);
            }

            List<ShopItemDefinition> initialItems = new();
            foreach (InitialVehicle vehicle in InitialVehicles)
                initialItems.Add(EnsureVehicleItem(vehicle));

            SerializedObject serialisedCatalogue = new(catalogue);
            SerializedProperty items = serialisedCatalogue.FindProperty("m_items");
            for (int index = items.arraySize - 1; index >= 0; index--)
            {
                ShopItemDefinition item = items.GetArrayElementAtIndex(index).objectReferenceValue as ShopItemDefinition;
                if (item == null || (item.ItemId != "van_good" && item.ItemId != "van_best" &&
                    item.ItemId != "truck_good" && item.ItemId != "truck_best")) continue;
                items.GetArrayElementAtIndex(index).objectReferenceValue = null;
                items.DeleteArrayElementAtIndex(index);
            }
            foreach (ShopItemDefinition item in initialItems)
            {
                bool present = false;
                for (int index = 0; index < items.arraySize; index++)
                {
                    if (items.GetArrayElementAtIndex(index).objectReferenceValue == item)
                    {
                        present = true;
                        break;
                    }
                }
                if (present) continue;
                int addedIndex = items.arraySize;
                items.InsertArrayElementAtIndex(addedIndex);
                items.GetArrayElementAtIndex(addedIndex).objectReferenceValue = item;
            }
            serialisedCatalogue.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalogue);
            if (createdCatalogue) AssetDatabase.SaveAssets();
            return catalogue;
        }

        private static ShopItemDefinition EnsureVehicleItem(InitialVehicle vehicle)
        {
            string itemPath = $"{ShopFolder}/{vehicle.Id}.asset";
            ShopItemDefinition item = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(itemPath);
            if (item != null) return item;

            string behaviourPath = $"{ShopFolder}/{vehicle.Id}_purchase.asset";
            VehiclePurchaseBehaviour behaviour =
                AssetDatabase.LoadAssetAtPath<VehiclePurchaseBehaviour>(behaviourPath);
            if (behaviour == null)
            {
                behaviour = ScriptableObject.CreateInstance<VehiclePurchaseBehaviour>();
                SerializedObject serialisedBehaviour = new(behaviour);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(vehicle.PrefabPath);
                serialisedBehaviour.FindProperty("m_vehiclePrefab").objectReferenceValue =
                    prefab != null ? prefab.GetComponent<NetworkObject>() : null;
                serialisedBehaviour.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(behaviour, behaviourPath);
            }

            item = ScriptableObject.CreateInstance<ShopItemDefinition>();
            SerializedObject serialisedItem = new(item);
            serialisedItem.FindProperty("m_itemId").stringValue = vehicle.Id;
            serialisedItem.FindProperty("m_displayName").stringValue = vehicle.Name;
            serialisedItem.FindProperty("m_description").stringValue = vehicle.Description;
            serialisedItem.FindProperty("m_price").intValue = vehicle.Price;
            serialisedItem.FindProperty("m_category").enumValueIndex = (int)ShopItemCategory.Vehicle;
            serialisedItem.FindProperty("m_purchaseBehaviour").objectReferenceValue = behaviour;
            serialisedItem.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(item, itemPath);
            return item;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
