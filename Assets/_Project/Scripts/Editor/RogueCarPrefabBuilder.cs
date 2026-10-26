using System.Linq;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Traffic;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class RogueCarPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Enemies/RogueCar.prefab";
        private const string SourcePath = "Assets/_Project/Resources/Traffic/TrafficCar.prefab";
        private const string SettingsPath = "Assets/_Project/Settings/Delivery/RogueCarSettings.asset";
        private const string HardTierPath = "Assets/_Project/Settings/Delivery/HardDelivery.asset";
        private const string SpawnablesPath = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";

        [InitializeOnLoadMethod]
        private static void BuildMissingAfterImport()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath) != null)
                EditorApplication.delayCall += () => EnsurePrefab();
        }

        [MenuItem("Couch Guys/Enemies/Build Rogue Car Prefab")]
        public static void Build()
        {
            Selection.activeObject = EnsurePrefab();
        }

        [MenuItem("Couch Guys/Enemies/Spawn Rogue Car Now (Play Mode)")]
        private static void SpawnNow()
        {
            TrafficManager manager = Object.FindFirstObjectByType<TrafficManager>();
            if (!Application.isPlaying || manager == null)
            {
                Debug.LogWarning("Enter Play mode as host/server and start a Hard delivery before testing Rogue Car spawning.");
                return;
            }
            manager.SpawnRogueCarNow();
        }

        public static GameObject EnsurePrefab()
        {
            RogueCarSettings settings = AssetDatabase.LoadAssetAtPath<RogueCarSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<RogueCarSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(SourcePath);
                try
                {
                    root.name = "RogueCar";
                    RogueCar rogue = root.AddComponent<RogueCar>();
                    var so = new SerializedObject(rogue);
                    so.FindProperty("m_settings").objectReferenceValue = settings;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            if (prefab == null || prefab.GetComponent<RogueCar>() == null)
                throw new UnityException($"Invalid Rogue Car prefab at {PrefabPath}.");

            NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
            string stableName = new string(($"{PrefabPath}{prefab.name}").ToLowerInvariant()
                .Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')).ToArray());
            networkObject.SetAssetPathHash(stableName.GetStableHashU64());
            EditorUtility.SetDirty(networkObject);
            PrefabUtility.SavePrefabAsset(prefab);
            Register(networkObject, SpawnablesPath);
            Register(networkObject, "Assets/DefaultPrefabObjects.asset");

            DeliveryTierConfig hard = AssetDatabase.LoadAssetAtPath<DeliveryTierConfig>(HardTierPath);
            if (hard != null)
            {
                var so = new SerializedObject(hard);
                // Preserve user-assigned prefab and settings overrides.
                if (so.FindProperty("m_rogueCarPrefab").objectReferenceValue == null)
                    so.FindProperty("m_rogueCarPrefab").objectReferenceValue = networkObject;
                if (so.FindProperty("m_rogueCarSettings").objectReferenceValue == null)
                    so.FindProperty("m_rogueCarSettings").objectReferenceValue = settings;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static void Register(NetworkObject prefab, string path)
        {
            SinglePrefabObjects collection = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(path);
            if (collection == null || collection.Prefabs.Contains(prefab)) return;
            collection.AddObject(prefab, true, false);
            EditorUtility.SetDirty(collection);
        }
    }
}
