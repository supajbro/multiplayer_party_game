using System;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Creates the initial data assets and migrates the existing Suburbs inspector values.</summary>
    public static class RegionAssetBuilder
    {
        private const string Root = "Assets/_Project/Settings/Regions";

        public static RegionDefinition EnsureDefaultRegions(
            NeighbourhoodGenerator generator,
            GameObject[] housePrefabs)
        {
            EnsureFolder("Assets/_Project/Settings", "Regions");
            GridRoadGenerationStrategy gridStrategy = LoadOrCreate<GridRoadGenerationStrategy>(
                $"{Root}/RoadStrategy_Grid.asset", out _);

            LotDefinition[] houseLots = new LotDefinition[housePrefabs?.Length ?? 0];
            for (int index = 0; index < houseLots.Length; index++)
            {
                string path = $"{Root}/Lot_SuburbanHouse_{index + 1:00}.asset";
                houseLots[index] = LoadOrCreate<LotDefinition>(path, out bool lotCreated);
                if (lotCreated)
                {
                    SerializedObject lot = new SerializedObject(houseLots[index]);
                    lot.FindProperty("m_prefab").objectReferenceValue = housePrefabs[index];
                    lot.FindProperty("m_lotType").enumValueIndex = (int)LotType.Residential;
                    lot.FindProperty("m_minimumFootprint").vector2IntValue = new Vector2Int(2, 2);
                    lot.FindProperty("m_canBeDeliveryDestination").boolValue = true;
                    lot.FindProperty("m_destinationDisplayName").stringValue = $"House {index + 1}";
                    lot.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(houseLots[index]);
                }
            }

            LotDefinition[] countryLots = new LotDefinition[housePrefabs?.Length ?? 0];
            for (int index = 0; index < countryLots.Length; index++)
            {
                string path = $"{Root}/Lot_CountryHouse_{index + 1:00}.asset";
                countryLots[index] = LoadOrCreate<LotDefinition>(path, out bool lotCreated);
                if (lotCreated)
                {
                    SerializedObject lot = new SerializedObject(countryLots[index]);
                    lot.FindProperty("m_prefab").objectReferenceValue = housePrefabs[index];
                    lot.FindProperty("m_lotType").enumValueIndex = (int)LotType.Residential;
                    lot.FindProperty("m_minimumFootprint").vector2IntValue = new Vector2Int(2, 2);
                    lot.FindProperty("m_maximumSlope").floatValue = 30f;
                    lot.FindProperty("m_canBeDeliveryDestination").boolValue = true;
                    lot.FindProperty("m_destinationDisplayName").stringValue = $"Country House {index + 1}";
                    lot.FindProperty("m_deliveryDifficultyModifier").floatValue = 1.25f;
                    lot.FindProperty("m_deliveryRewardModifier").floatValue = 1.4f;
                    lot.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(countryLots[index]);
                }
            }

            RegionDefinition suburbs = LoadOrCreate<RegionDefinition>(
                $"{Root}/Region_Suburbs.asset", out bool suburbsCreated);
            if (suburbsCreated)
            {
                ConfigureRegionIdentity(suburbs, "suburbs", "Suburbs", 0, 1, gridStrategy);
                CopySuburbsConfiguration(generator, suburbs, houseLots);
            }

            RegionDefinition countryside = EnsurePlaceholder(
                "Region_Countryside.asset", "countryside", "Countryside", 5000, 3, gridStrategy,
                new RegionGridSettings
                {
                    GridWidth = 5, GridHeight = 6, BlockWidth = 7, BlockHeight = 8,
                    RoadTileSize = 12f, MinimumLotsPerBlock = 1, LotsPerBlock = 2, MinimumLotSpacing = 2
                },
                Elevation(2f, 10f, 32f, 35f));
            ConfigureCountrysideIfEmpty(countryside, countryLots, generator.DeliveryNpcPrefab);
            RegionDefinition mountains = EnsurePlaceholder(
                "Region_Mountains.asset", "mountains", "Mountains", 20000, 6, null,
                new RegionGridSettings
                {
                    GridWidth = 4, GridHeight = 7, BlockWidth = 6, BlockHeight = 7,
                    RoadTileSize = 12f, MinimumLotsPerBlock = 1, LotsPerBlock = 2, MinimumLotSpacing = 2
                },
                Elevation(6f, 18f, 90f, 45f));
            RegionDefinition snow = EnsurePlaceholder(
                "Region_SnowResort.asset", "snow_resort", "Snow Resort", 50000, 8, null,
                new RegionGridSettings
                {
                    GridWidth = 4, GridHeight = 7, BlockWidth = 6, BlockHeight = 7,
                    RoadTileSize = 12f, MinimumLotsPerBlock = 1, LotsPerBlock = 3, MinimumLotSpacing = 2
                },
                Elevation(7f, 20f, 110f, 45f));
            RegionDefinition volcano = EnsurePlaceholder(
                "Region_Volcano.asset", "volcano", "Volcano", 100000, 10, null,
                new RegionGridSettings
                {
                    GridWidth = 3, GridHeight = 8, BlockWidth = 8, BlockHeight = 8,
                    RoadTileSize = 14f, MinimumLotsPerBlock = 1, LotsPerBlock = 1, MinimumLotSpacing = 3
                },
                Elevation(10f, 28f, 160f, 50f));

            SerializedObject serialisedGenerator = new SerializedObject(generator);
            SerializedProperty available = serialisedGenerator.FindProperty("m_availableRegions");
            RegionDefinition[] regions = { suburbs, countryside, mountains, snow, volcano };
            available.arraySize = regions.Length;
            for (int index = 0; index < regions.Length; index++)
            {
                available.GetArrayElementAtIndex(index).objectReferenceValue = regions[index];
            }

            if (serialisedGenerator.FindProperty("m_selectedRegion").objectReferenceValue == null)
            {
                serialisedGenerator.FindProperty("m_selectedRegion").objectReferenceValue = suburbs;
            }

            serialisedGenerator.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(generator);
            AssetDatabase.SaveAssets();
            return suburbs;
        }

        private static void ConfigureCountrysideIfEmpty(
            RegionDefinition countryside,
            LotDefinition[] countryLots,
            GameObject deliveryNpcPrefab)
        {
            SerializedObject serialised = new SerializedObject(countryside);
            SerializedProperty lots = serialised.FindProperty("m_lots");
            if (lots.arraySize > 0)
            {
                return;
            }

            serialised.FindProperty("m_description").stringValue =
                "Long, sparse delivery routes across rolling placeholder countryside. " +
                "Designed to make vehicles increasingly valuable.";
            serialised.FindProperty("m_deliveryNpcPrefab").objectReferenceValue = deliveryNpcPrefab;
            serialised.FindProperty("m_lotDensity").floatValue = 0.65f;
            lots.arraySize = countryLots.Length;
            for (int index = 0; index < countryLots.Length; index++)
            {
                SerializedProperty entry = lots.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Definition").objectReferenceValue = countryLots[index];
                entry.FindPropertyRelative("Weight").floatValue = index == 0 ? 3f : 2f;
            }

            serialised.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(countryside);
        }

        private static void CopySuburbsConfiguration(
            NeighbourhoodGenerator generator,
            RegionDefinition suburbs,
            LotDefinition[] houseLots)
        {
            SerializedObject source = new SerializedObject(generator);
            SerializedObject target = new SerializedObject(suburbs);
            SerializedProperty grid = target.FindProperty("m_grid");
            CopyInt(source, "m_gridWidth", grid, "GridWidth");
            CopyInt(source, "m_gridHeight", grid, "GridHeight");
            CopyInt(source, "m_blockWidth", grid, "BlockWidth");
            CopyInt(source, "m_blockHeight", grid, "BlockHeight");
            CopyFloat(source, "m_roadTileSize", grid, "RoadTileSize");
            CopyInt(source, "m_minimumHousesPerBlock", grid, "MinimumLotsPerBlock");
            CopyInt(source, "m_housesPerBlock", grid, "LotsPerBlock");
            CopyInt(source, "m_minimumHouseSpacing", grid, "MinimumLotSpacing");

            SerializedProperty elevation = target.FindProperty("m_elevation");
            elevation.FindPropertyRelative("Enabled").boolValue = source.FindProperty("m_elevationEnabled").boolValue;
            CopyFloat(source, "m_minimumElevationStep", elevation, "MinimumStep");
            CopyFloat(source, "m_elevationStep", elevation, "MaximumStep");
            CopyFloat(source, "m_maximumElevation", elevation, "MaximumElevation");
            CopyFloat(source, "m_maximumRoadSlope", elevation, "MaximumRoadSlope");

            CopyObjectArray(source.FindProperty("m_roadPrefabs"), target.FindProperty("m_roadPrefabs"));
            target.FindProperty("m_groundPrefab").objectReferenceValue =
                source.FindProperty("m_residentialGroundPrefab").objectReferenceValue;
            target.FindProperty("m_startingAreaPrefab").objectReferenceValue =
                source.FindProperty("m_startingAreaPrefab").objectReferenceValue;
            target.FindProperty("m_deliveryNpcPrefab").objectReferenceValue =
                source.FindProperty("m_deliveryNpcPrefab").objectReferenceValue;
            target.FindProperty("m_mansionPrefab").objectReferenceValue =
                source.FindProperty("m_mansionPrefab").objectReferenceValue;

            SerializedProperty lots = target.FindProperty("m_lots");
            lots.arraySize = houseLots.Length;
            for (int index = 0; index < houseLots.Length; index++)
            {
                SerializedProperty entry = lots.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Definition").objectReferenceValue = houseLots[index];
                entry.FindPropertyRelative("Weight").floatValue = 1f;
            }

            target.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(suburbs);
        }

        private static RegionDefinition EnsurePlaceholder(
            string fileName,
            string id,
            string displayName,
            int unlockCost,
            int difficulty,
            RegionRoadGenerationStrategy strategy,
            RegionGridSettings grid,
            RegionElevationSettings elevation)
        {
            RegionDefinition region = LoadOrCreate<RegionDefinition>(
                $"{Root}/{fileName}", out bool created);
            if (created)
            {
                ConfigureRegionIdentity(region, id, displayName, unlockCost, difficulty, strategy);
                SerializedObject serialised = new SerializedObject(region);
                SetGrid(serialised.FindProperty("m_grid"), grid);
                SetElevation(serialised.FindProperty("m_elevation"), elevation);
                serialised.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(region);
            }

            return region;
        }

        private static void ConfigureRegionIdentity(
            RegionDefinition region,
            string id,
            string displayName,
            int unlockCost,
            int difficulty,
            RegionRoadGenerationStrategy strategy)
        {
            SerializedObject serialised = new SerializedObject(region);
            serialised.FindProperty("m_regionId").stringValue = id;
            serialised.FindProperty("m_displayName").stringValue = displayName;
            serialised.FindProperty("m_unlockCost").intValue = unlockCost;
            serialised.FindProperty("m_difficulty").intValue = difficulty;
            serialised.FindProperty("m_roadStrategy").objectReferenceValue = strategy;
            serialised.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(region);
        }

        private static RegionElevationSettings Elevation(
            float minimum,
            float maximum,
            float total,
            float roadSlope) => new RegionElevationSettings
        {
            Enabled = true,
            MinimumStep = minimum,
            MaximumStep = maximum,
            MaximumElevation = total,
            MaximumRoadSlope = roadSlope
        };

        private static void SetGrid(SerializedProperty property, RegionGridSettings value)
        {
            property.FindPropertyRelative("GridWidth").intValue = value.GridWidth;
            property.FindPropertyRelative("GridHeight").intValue = value.GridHeight;
            property.FindPropertyRelative("BlockWidth").intValue = value.BlockWidth;
            property.FindPropertyRelative("BlockHeight").intValue = value.BlockHeight;
            property.FindPropertyRelative("RoadTileSize").floatValue = value.RoadTileSize;
            property.FindPropertyRelative("MinimumLotsPerBlock").intValue = value.MinimumLotsPerBlock;
            property.FindPropertyRelative("LotsPerBlock").intValue = value.LotsPerBlock;
            property.FindPropertyRelative("MinimumLotSpacing").intValue = value.MinimumLotSpacing;
        }

        private static void SetElevation(SerializedProperty property, RegionElevationSettings value)
        {
            property.FindPropertyRelative("Enabled").boolValue = value.Enabled;
            property.FindPropertyRelative("MinimumStep").floatValue = value.MinimumStep;
            property.FindPropertyRelative("MaximumStep").floatValue = value.MaximumStep;
            property.FindPropertyRelative("MaximumElevation").floatValue = value.MaximumElevation;
            property.FindPropertyRelative("MaximumRoadSlope").floatValue = value.MaximumRoadSlope;
        }

        private static void CopyInt(
            SerializedObject source,
            string sourceName,
            SerializedProperty target,
            string targetName) =>
            target.FindPropertyRelative(targetName).intValue = source.FindProperty(sourceName).intValue;

        private static void CopyFloat(
            SerializedObject source,
            string sourceName,
            SerializedProperty target,
            string targetName) =>
            target.FindPropertyRelative(targetName).floatValue = source.FindProperty(sourceName).floatValue;

        private static void CopyObjectArray(SerializedProperty source, SerializedProperty target)
        {
            target.arraySize = source.arraySize;
            for (int index = 0; index < source.arraySize; index++)
            {
                target.GetArrayElementAtIndex(index).objectReferenceValue =
                    source.GetArrayElementAtIndex(index).objectReferenceValue;
            }
        }

        private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                created = false;
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
