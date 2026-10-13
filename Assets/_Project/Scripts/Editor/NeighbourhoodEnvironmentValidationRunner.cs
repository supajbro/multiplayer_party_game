using System;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    public static class NeighbourhoodEnvironmentValidationRunner
    {
        private const string RegionPath = "Assets/_Project/Settings/Regions/Region_Suburbs.asset";
        private const string SessionKey = "CouchGuys.EnvironmentValidation.2026-10-13";
        private static readonly int[] ValidationSeeds = { 12345, 24680, 97531 };

        [InitializeOnLoadMethod]
        private static void ValidateOnceAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(SessionKey, false)) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/_Project/Prefabs/Environment/Placeholders/StreetLight_Placeholder.prefab") == null) return;
                SessionState.SetBool(SessionKey, true);
                try { ValidateEnvironment(); }
                catch (Exception exception) { Debug.LogError($"Neighbourhood environment validation failed: {exception}"); }
            };
        }

        [MenuItem("Couch Guys/World/Validate Neighbourhood Environment")]
        public static void ValidateEnvironment()
        {
            RegionDefinition region = AssetDatabase.LoadAssetAtPath<RegionDefinition>(RegionPath);
            if (region == null) throw new InvalidOperationException($"Missing region asset: {RegionPath}");
            for (int index = 0; index < ValidationSeeds.Length; index++) ValidateSeed(region, ValidationSeeds[index]);
            Debug.Log($"Neighbourhood environment validation passed for {ValidationSeeds.Length} seeds: " +
                      string.Join(", ", ValidationSeeds));
        }

        private static void ValidateSeed(RegionDefinition region, int seed)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = new GameObject($"EnvironmentValidation_{seed}");
                SceneManager.MoveGameObjectToScene(root, preview);
                NeighbourhoodGenerator generator = root.AddComponent<NeighbourhoodGenerator>();
                generator.ConfigureDecorative(region);
                generator.Generate(seed);
                if (!generator.IsGenerationReady || !generator.LastValidationSucceeded)
                    throw new InvalidOperationException($"Seed {seed} did not produce a valid neighbourhood.");
                if (generator.GeneratedRoads.Count == 0 || generator.GeneratedProperties.Count == 0 ||
                    generator.DeliveryDestinations.Count == 0)
                    throw new InvalidOperationException($"Seed {seed} lost core roads/properties/destinations.");
                if (generator.GeneratedParkCount < region.Environment.Parks.MinimumParks)
                    throw new InvalidOperationException($"Seed {seed} generated only {generator.GeneratedParkCount} parks.");
                if (generator.GeneratedStreetlightCount == 0)
                    throw new InvalidOperationException($"Seed {seed} generated no streetlights.");
                if (generator.GeneratedResidentialDecorationCount == 0)
                    throw new InvalidOperationException($"Seed {seed} generated no residential decorations.");
                Debug.Log($"Environment seed {seed}: {generator.GeneratedRoads.Count} roads, " +
                          $"{generator.GeneratedProperties.Count} properties, {generator.GeneratedParkCount} parks, " +
                          $"{generator.GeneratedStreetlightCount} streetlights, " +
                          $"{generator.GeneratedResidentialDecorationCount} residential decorations, " +
                          $"{generator.GeneratedRoadsideDecorationCount} roadside decorations.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
