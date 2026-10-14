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
        private const string SessionKey = "CouchGuys.EnvironmentValidation.2026-10-14.Driveways";
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
                ValidateDriveways(generator, seed);
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

        private static void ValidateDriveways(NeighbourhoodGenerator generator, int seed)
        {
            GeneratedTerrainMesh surface = generator.GeneratedDrivewaySurface;
            if (surface == null || surface.VertexCount <= 0 || surface.TriangleCount <= 0 ||
                !surface.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null ||
                !surface.TryGetComponent(out MeshCollider collider) ||
                collider.sharedMesh != filter.sharedMesh)
            {
                throw new InvalidOperationException(
                    $"Seed {seed} generated no valid combined driveway mesh/collider.");
            }

            foreach (GeneratedProperty property in generator.GeneratedProperties)
            {
                Vector3 flatSegment = property.DrivewayEnd - property.DrivewayStart;
                flatSegment.y = 0f;
                if (property.ReservedDrivewayWidth <= 0f || flatSegment.sqrMagnitude <= 0.01f ||
                    property.DrivewayGrade > 30f)
                {
                    throw new InvalidOperationException(
                        $"Seed {seed} has invalid driveway metadata on '{property.name}'.");
                }

                float startHeightError = Mathf.Abs(
                    property.DrivewayStart.y - generator.SampleTerrainHeight(property.DrivewayStart));
                if (startHeightError > 0.15f)
                {
                    throw new InvalidOperationException(
                        $"Seed {seed} driveway '{property.name}' misses the road terrain by " +
                        $"{startHeightError:F3}m.");
                }
            }
        }
    }
}
