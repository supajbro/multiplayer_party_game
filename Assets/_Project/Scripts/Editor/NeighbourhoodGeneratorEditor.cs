using CouchGuys.ProceduralGeneration;
using CouchGuys.Gameplay.Delivery;
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
            DrawProgressionSummary(generator);
            EditorGUILayout.LabelField("Lots", generator.GeneratedLots.Count.ToString());
            EditorGUILayout.LabelField("Delivery Destinations", generator.DeliveryDestinations.Count.ToString());
            EditorGUILayout.LabelField(
                "Difficulty Analysis",
                generator.IsDeliveryDifficultyReady ? "Ready" : "Not Ready");
            if (generator.IsDeliveryDifficultyReady)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    for (int stageIndex = 0; stageIndex < 7; stageIndex++)
                    {
                        EditorGUILayout.LabelField(
                            $"Stage {stageIndex + 1} Strict Pool",
                            generator.GetStrictDestinationCountForStage(stageIndex).ToString());
                    }
                }
            }
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

            if (GUILayout.Button("Validate 100 Deterministic Seeds"))
            {
                ValidateSeeds(generator);
                MarkChanged(generator);
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

        private static void DrawProgressionSummary(NeighbourhoodGenerator generator)
        {
            int[] roadsPerZone = new int[5];
            int primaryRoads = 0;
            float minimumElevation = float.PositiveInfinity;
            float maximumElevation = float.NegativeInfinity;
            foreach (GeneratedRoad road in generator.GeneratedRoads)
            {
                if (road == null)
                {
                    continue;
                }

                roadsPerZone[(int)road.Zone]++;
                primaryRoads += road.Role == GeneratedRoadRole.Primary ? 1 : 0;
                minimumElevation = Mathf.Min(minimumElevation, road.Elevation);
                maximumElevation = Mathf.Max(maximumElevation, road.Elevation);
            }

            EditorGUILayout.LabelField("Validation", generator.LastValidationSucceeded ? "Passed" : "Not Passed");
            if (generator.GeneratedTerrain != null)
            {
                EditorGUILayout.LabelField(
                    "Terrain Mesh",
                    $"{generator.GeneratedTerrain.VertexCount:N0} vertices / " +
                    $"{generator.GeneratedTerrain.TriangleCount:N0} triangles");
            }

            EditorGUILayout.LabelField("Primary Route Tiles", primaryRoads.ToString());
            if (!float.IsPositiveInfinity(minimumElevation))
            {
                EditorGUILayout.LabelField(
                    "Road Elevation Range",
                    $"{minimumElevation:F1}m – {maximumElevation:F1}m");
            }

            using (new EditorGUI.IndentLevelScope())
            {
                for (int zoneIndex = 0; zoneIndex < roadsPerZone.Length; zoneIndex++)
                {
                    EditorGUILayout.LabelField(
                        $"{(SuburbZone)zoneIndex} Roads",
                        roadsPerZone[zoneIndex].ToString());
                }
            }
        }

        private static void ValidateSeeds(NeighbourhoodGenerator generator)
        {
            const int seedCount = 100;
            const int firstSeed = 10000;
            int originalSeed = generator.CurrentSeed != 0 ? generator.CurrentSeed : generator.SelectSeed();
            RegionDefinition originalRegion = generator.SelectedRegion;
            int[] minimumStrictPools =
                { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };
            int analysisFailures = 0;
            int progressionFailures = 0;

            try
            {
                for (int offset = 0; offset < seedCount; offset++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Validating Suburbs Delivery Difficulty",
                        $"Generating seed {firstSeed + offset} ({offset + 1}/{seedCount})",
                        (offset + 1f) / seedCount);
                    generator.Generate(originalRegion, firstSeed + offset);
                    if (!generator.IsDeliveryDifficultyReady)
                    {
                        analysisFailures++;
                        continue;
                    }

                    for (int stageIndex = 0; stageIndex < minimumStrictPools.Length; stageIndex++)
                    {
                        minimumStrictPools[stageIndex] = Mathf.Min(
                            minimumStrictPools[stageIndex],
                            generator.GetStrictDestinationCountForStage(stageIndex));
                    }

                    float easiestScore = FindBestSuitability(generator, 0, out float earlyDifficulty);
                    float finalScore = FindBestSuitability(generator, 6, out float lateDifficulty);
                    if (float.IsPositiveInfinity(easiestScore) || float.IsPositiveInfinity(finalScore) ||
                        lateDifficulty <= earlyDifficulty)
                    {
                        progressionFailures++;
                    }
                }
            }
            finally
            {
                generator.Generate(originalRegion, originalSeed);
                EditorUtility.ClearProgressBar();
            }

            Debug.Log(
                $"Validated {seedCount} Suburbs seeds. Analysis failures: {analysisFailures}; " +
                $"Stage 1-to-7 progression failures: {progressionFailures}; minimum strict pools: " +
                $"[{string.Join(", ", minimumStrictPools)}].",
                generator);
        }

        private static float FindBestSuitability(
            NeighbourhoodGenerator generator,
            int stageIndex,
            out float selectedDifficulty)
        {
            DeliveryDestinationQuery query = DeliveryDestinationQuery.ForSuburbsStage(stageIndex);
            float bestSuitability = float.PositiveInfinity;
            selectedDifficulty = 0f;
            foreach (DeliveryDestination destination in generator.DeliveryDestinations)
            {
                DeliveryRouteMetrics metrics = destination != null ? destination.RouteMetrics : null;
                if (metrics == null || !metrics.IsReachable)
                {
                    continue;
                }

                float suitability = query.CalculateSuitability(metrics, stageIndex);
                if (suitability < bestSuitability)
                {
                    bestSuitability = suitability;
                    selectedDifficulty = metrics.DifficultyScore;
                }
            }

            return bestSuitability;
        }
    }

    [CustomEditor(typeof(DeliveryDestination))]
    public sealed class DeliveryDestinationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DeliveryDestination destination = (DeliveryDestination)target;
            DeliveryRouteMetrics metrics = destination.RouteMetrics;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generated Route", EditorStyles.boldLabel);
            if (metrics == null)
            {
                EditorGUILayout.HelpBox("Generate the neighbourhood to calculate route metadata.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Reachable", metrics.IsReachable ? "Yes" : "No");
            EditorGUILayout.LabelField("Progression Zone", metrics.ProgressionZone.ToString());
            EditorGUILayout.LabelField("Difficulty Band", metrics.Zone.ToString());
            EditorGUILayout.LabelField("Route Distance", $"{metrics.RouteLength:F1} m");
            EditorGUILayout.LabelField("Destination Elevation", $"{metrics.DestinationElevation:F1} m");
            EditorGUILayout.LabelField("Elevation Gain", $"{metrics.CumulativeElevationGain:F1} m");
            EditorGUILayout.LabelField("Maximum Road Grade", $"{metrics.MaximumRoadGrade:F1}°");
            EditorGUILayout.LabelField("Turns", metrics.TurnCount.ToString());
            EditorGUILayout.LabelField("Intersections", metrics.IntersectionCount.ToString());
            EditorGUILayout.LabelField("Final Carry", $"{metrics.FinalCarryDistance:F1} m");
            EditorGUILayout.LabelField("Van Accessible Door", metrics.VanAccessible ? "Yes" : "No – park and carry");
            EditorGUILayout.LabelField("Difficulty", $"{metrics.DifficultyScore:F1} / 100");
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
            if (manager.EnemyPrefabs == null || manager.EnemyPrefabs.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Assign the networked Pistol, Assault Rifle, and Shotgun enemy prefabs to enable spawning.",
                    MessageType.Warning);
            }
            else
            {
                for (int index = 0; index < manager.EnemyPrefabs.Count; index++)
                {
                    FishNet.Object.NetworkObject prefab = manager.EnemyPrefabs[index];
                    if (prefab != null && prefab.GetComponentInChildren<NavMeshAgent>() == null)
                    {
                        EditorGUILayout.HelpBox(
                            $"{prefab.name} needs a NavMeshAgent.",
                            MessageType.Error);
                    }
                }
            }

            if (changed && manager.EnemyPrefabs != null)
            {
                for (int index = 0; index < manager.EnemyPrefabs.Count; index++)
                {
                    RegisterNetworkPrefab(manager.EnemyPrefabs[index]);
                }
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
            if (spawnables == null || prefab == null)
            {
                return;
            }

            spawnables.AddObject(prefab, true, false);
            EditorUtility.SetDirty(spawnables);
            AssetDatabase.SaveAssetIfDirty(spawnables);
        }
    }
}
