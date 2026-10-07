using System;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CouchGuys.EditorTools
{
    /// <summary>Repeatable generation soak test available from the Editor and command line.</summary>
    public static class ProgressiveSuburbsValidationRunner
    {
        private const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";
        private const int FirstSeed = 10000;
        private const int SeedCount = 100;

        [MenuItem("Couch Guys/Validation/Validate 100 Progressive Suburbs Seeds")]
        public static void RunInteractiveValidation()
        {
            RunValidation(false);
        }

        /// <summary>
        /// Command-line entry point:
        /// Unity -batchmode -projectPath ... -executeMethod
        /// CouchGuys.EditorTools.ProgressiveSuburbsValidationRunner.RunBatchValidation -quit
        /// </summary>
        public static void RunBatchValidation()
        {
            RunValidation(true);
        }

        private static void RunValidation(bool throwOnFailure)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Suburbs validation cannot run while entering or in Play Mode.");
            }

            EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            NeighbourhoodGenerator generator = UnityEngine.Object.FindFirstObjectByType<NeighbourhoodGenerator>();
            if (generator == null)
            {
                throw new InvalidOperationException("The gameplay scene contains no NeighbourhoodGenerator.");
            }

            RegionDefinition region = generator.SelectedRegion;
            int originalSeed = generator.CurrentSeed != 0 ? generator.CurrentSeed : 12345;
            int failureCount = 0;
            int determinismFailures = 0;
            int minimumEarlyPool = int.MaxValue;
            int minimumFinalPool = int.MaxValue;
            float smallestDifficultyIncrease = float.PositiveInfinity;

            try
            {
                for (int offset = 0; offset < SeedCount; offset++)
                {
                    int seed = FirstSeed + offset;
                    if (!Application.isBatchMode)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Progressive Suburbs Validation",
                            $"Generating seed {seed} ({offset + 1}/{SeedCount})",
                            (offset + 1f) / SeedCount);
                    }

                    generator.Generate(region, seed);
                    int earlyPool = generator.GetStrictDestinationCountForStage(0);
                    int finalPool = generator.GetStrictDestinationCountForStage(6);
                    minimumEarlyPool = Mathf.Min(minimumEarlyPool, earlyPool);
                    minimumFinalPool = Mathf.Min(minimumFinalPool, finalPool);
                    if (!generator.HasGeneratedNeighbourhood || !generator.LastValidationSucceeded ||
                        !generator.IsDeliveryDifficultyReady || earlyPool <= 0 || finalPool <= 0 ||
                        !TryGetBestDifficulty(generator, 0, out float earlyDifficulty) ||
                        !TryGetBestDifficulty(generator, 6, out float finalDifficulty))
                    {
                        failureCount++;
                        continue;
                    }

                    float increase = finalDifficulty - earlyDifficulty;
                    smallestDifficultyIncrease = Mathf.Min(smallestDifficultyIncrease, increase);
                    if (increase <= 0f)
                    {
                        failureCount++;
                    }

                    if (offset == 0)
                    {
                        int firstSignature = CalculateGenerationSignature(generator);
                        generator.Generate(region, seed);
                        if (firstSignature != CalculateGenerationSignature(generator))
                        {
                            determinismFailures++;
                        }
                    }
                }
            }
            finally
            {
                generator.Generate(region, originalSeed);
                EditorUtility.ClearProgressBar();
            }

            string result =
                $"Progressive Suburbs validation: {SeedCount - failureCount}/{SeedCount} seeds passed; " +
                $"determinism failures={determinismFailures}; minimum Stage 1 pool={minimumEarlyPool}; " +
                $"minimum final pool={minimumFinalPool}; smallest final difficulty increase=" +
                $"{smallestDifficultyIncrease:F1}.";
            if (failureCount > 0 || determinismFailures > 0)
            {
                Debug.LogError(result, generator);
                if (throwOnFailure)
                {
                    throw new InvalidOperationException(result);
                }

                return;
            }

            Debug.Log(result, generator);
        }

        private static bool TryGetBestDifficulty(
            NeighbourhoodGenerator generator,
            int stageIndex,
            out float difficulty)
        {
            DeliveryDestinationQuery query = DeliveryDestinationQuery.ForSuburbsStage(stageIndex);
            float bestSuitability = float.PositiveInfinity;
            difficulty = 0f;
            foreach (DeliveryDestination destination in generator.DeliveryDestinations)
            {
                DeliveryRouteMetrics metrics = destination != null ? destination.RouteMetrics : null;
                if (!query.Matches(metrics))
                {
                    continue;
                }

                float suitability = query.CalculateSuitability(metrics, stageIndex);
                if (suitability < bestSuitability)
                {
                    bestSuitability = suitability;
                    difficulty = metrics.DifficultyScore;
                }
            }

            return !float.IsPositiveInfinity(bestSuitability);
        }

        private static int CalculateGenerationSignature(NeighbourhoodGenerator generator)
        {
            unchecked
            {
                int signature = generator.CurrentSeed;
                foreach (GeneratedRoad road in generator.GeneratedRoads)
                {
                    signature = signature * 397 ^ road.Coordinate.X;
                    signature = signature * 397 ^ road.Coordinate.Y;
                    signature = signature * 397 ^ Mathf.RoundToInt(road.Elevation * 1000f);
                    signature = signature * 397 ^ (int)road.Zone;
                    signature = signature * 397 ^ (int)road.Role;
                }

                foreach (DeliveryDestination destination in generator.DeliveryDestinations)
                {
                    DeliveryRouteMetrics metrics = destination.RouteMetrics;
                    signature = signature * 397 ^ metrics.RoadCoordinate.X;
                    signature = signature * 397 ^ metrics.RoadCoordinate.Y;
                    signature = signature * 397 ^ Mathf.RoundToInt(metrics.DifficultyScore * 100f);
                }

                return signature;
            }
        }
    }
}
