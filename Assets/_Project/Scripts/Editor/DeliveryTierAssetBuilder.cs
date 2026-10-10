using CouchGuys.Gameplay.Delivery;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class DeliveryTierAssetBuilder
    {
        private const string Folder = "Assets/_Project/Settings/Delivery";

        public static DeliveryTierConfig[] EnsureDeliveryTiers()
        {
            EnsureFolder("Assets/_Project/Settings", "Delivery");
            return new[]
            {
                Ensure("EasyDelivery", DeliveryTier.Easy, "Easy Delivery",
                    "A nearby, mostly flat run in the early neighbourhood.", 0, 100,
                    45f, 0.55f, 60f, 180f, 0f, 110f, SuburbZone.Start, SuburbZone.Easy,
                    6f, 18f, DeliveryDestinationCategories.Standard,
                    new Color(0.24f, 0.76f, 0.34f)),
                Ensure("MediumDelivery", DeliveryTier.Medium, "Medium Delivery",
                    "A longer route through the middle streets and rolling hills.", 100, 250,
                    55f, 0.58f, 90f, 300f, 80f, 260f, SuburbZone.Medium, SuburbZone.Hilly,
                    24f, 32f, DeliveryDestinationCategories.Standard |
                    DeliveryDestinationCategories.Commercial | DeliveryDestinationCategories.Landmark,
                    new Color(0.96f, 0.62f, 0.16f)),
                Ensure("HardDelivery", DeliveryTier.Hard, "Hard Delivery",
                    "Haul the couch to a mansion at the far end of the neighbourhood.", 250, 500,
                    75f, 0.65f, 150f, 600f, 160f, 10000f, SuburbZone.Hilly, SuburbZone.Outer,
                    10000f, 60f, DeliveryDestinationCategories.Mansion,
                    new Color(0.82f, 0.23f, 0.27f))
            };
        }

        private static DeliveryTierConfig Ensure(string fileName, DeliveryTier tier,
            string displayName, string description, int price, int reward, float baseTime,
            float timePerMetre, float minimumTime, float maximumTime, float minimumRoute,
            float maximumRoute, SuburbZone minimumZone, SuburbZone maximumZone,
            float maximumElevation, float maximumGrade, DeliveryDestinationCategories categories,
            Color colour)
        {
            string path = $"{Folder}/{fileName}.asset";
            DeliveryTierConfig config = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>(path);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<DeliveryTierConfig>();
            config.ConfigureDefaults(tier, displayName, description, price, reward, baseTime,
                timePerMetre, minimumTime, maximumTime, minimumRoute, maximumRoute,
                minimumZone, maximumZone, maximumElevation, maximumGrade, categories, colour);
            AssetDatabase.CreateAsset(config, path);
            return config;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
