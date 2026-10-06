using CouchGuys.ProceduralGeneration;
using CouchGuys.Gameplay.Enemies;
using FishNet.Managing.Object;
using UnityEngine.AI;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.EditorTools
{
    [CustomEditor(typeof(NeighbourhoodGenerator))]
    public sealed class NeighbourhoodGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            NeighbourhoodGenerator generator = (NeighbourhoodGenerator)target;
            EditorGUILayout.LabelField("Generated Region", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Region", generator.CurrentRegionName);
            EditorGUILayout.LabelField("Seed", generator.CurrentSeed.ToString());
            EditorGUILayout.LabelField("Ready", generator.IsGenerationReady ? "Yes" : "No");
            EditorGUILayout.LabelField("Roads", generator.GeneratedRoads.Count.ToString());
            EditorGUILayout.LabelField("Lots", generator.GeneratedLots.Count.ToString());
            EditorGUILayout.LabelField("Delivery Destinations", generator.DeliveryDestinations.Count.ToString());
            EditorGUILayout.LabelField("Landmarks", generator.GeneratedLandmarkCount.ToString());
            EditorGUILayout.LabelField("Props", generator.GeneratedPropCount.ToString());
            EditorGUILayout.LabelField("Hazards", generator.GeneratedHazardCount.ToString());
            EditorGUILayout.LabelField("Generation Time", $"{generator.LastGenerationMilliseconds:F1} ms");
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate"))
                {
                    generator.Generate();
                    MarkChanged(generator);
                }

                if (GUILayout.Button("Regenerate"))
                {
                    generator.Generate(generator.CurrentSeed != 0 ? generator.CurrentSeed : generator.SelectSeed());
                    MarkChanged(generator);
                }

                if (GUILayout.Button("Clear"))
                {
                    generator.Clear();
                    MarkChanged(generator);
                }
            }

            EditorGUILayout.HelpBox(
                "For multiplayer, add NeighbourhoodSeedSynchroniser to this GameObject. " +
                "Unity will also add the required FishNet NetworkObject.",
                MessageType.Info);
        }

        private static void MarkChanged(NeighbourhoodGenerator generator)
        {
            EditorUtility.SetDirty(generator);
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            }
        }
    }

    [CustomEditor(typeof(EnemySpawnManager))]
    public sealed class EnemySpawnManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            bool changed = DrawDefaultInspector();
            EditorGUILayout.Space();
            EnemySpawnManager manager = (EnemySpawnManager)target;
            if (manager.ThiefPrefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign the existing networked Thief prefab to enable spawning.",
                    MessageType.Warning);
            }
            else if (manager.ThiefPrefab.GetComponentInChildren<NavMeshAgent>() == null)
            {
                EditorGUILayout.HelpBox(
                    "The assigned Thief prefab needs a NavMeshAgent.",
                    MessageType.Error);
            }

            if (changed && manager.ThiefPrefab != null)
            {
                RegisterNetworkPrefab(manager.ThiefPrefab);
            }

            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Current Region", manager.CurrentRegionName);
            EditorGUILayout.LabelField("Spawning", manager.SpawningEnabled ? "Enabled" : "Disabled");
            EditorGUILayout.LabelField("NavMesh Ready", manager.NavMeshReady ? "Yes" : "No");
            EditorGUILayout.LabelField(
                "Active Thieves",
                $"{manager.ActiveThiefCount} / {manager.MaximumActiveThieves}");
            EditorGUILayout.LabelField(
                "Next Spawn",
                float.IsPositiveInfinity(manager.NextSpawnIn) ? "--" : $"{manager.NextSpawnIn:F1}s");
            EditorGUILayout.LabelField("Last Group Size", manager.LastSpawnGroupSize.ToString());
        }

        private static void RegisterNetworkPrefab(FishNet.Object.NetworkObject prefab)
        {
            const string spawnablesPath = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";
            SinglePrefabObjects spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(spawnablesPath);
            if (spawnables == null)
            {
                return;
            }

            spawnables.AddObject(prefab, true, false);
            EditorUtility.SetDirty(spawnables);
            AssetDatabase.SaveAssetIfDirty(spawnables);
        }
    }
}
