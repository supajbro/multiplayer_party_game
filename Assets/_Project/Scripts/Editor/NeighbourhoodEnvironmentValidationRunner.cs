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
        private const string SessionKey = "CouchGuys.EnvironmentValidation.2026-10-14.CardinalFrontageV4";
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
                if (generator.GeneratedParkCount < 1 || generator.GeneratedParkCount > 2 ||
                    generator.GeneratedParkBlockCount != generator.GeneratedParkCount)
                    throw new InvalidOperationException(
                        $"Seed {seed} generated an invalid park allocation: " +
                        $"{generator.GeneratedParkCount} parks across " +
                        $"{generator.GeneratedParkBlockCount} reserved blocks.");
                foreach (GeneratedProperty property in generator.GeneratedProperties)
                    if (generator.IsCoordinateInParkBlock(property.AnchorCoordinate))
                        throw new InvalidOperationException(
                            $"Seed {seed} placed house '{property.name}' inside a park block.");
                if (generator.GeneratedStreetlightCount == 0)
                    throw new InvalidOperationException($"Seed {seed} generated no streetlights.");
                if (generator.GeneratedResidentialDecorationCount == 0)
                    throw new InvalidOperationException($"Seed {seed} generated no residential decorations.");
                ValidateDriveways(generator, seed);
                ValidateGrass(generator, seed);
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

            Material drivewayMaterial = surface.GetComponent<MeshRenderer>().sharedMaterial;
            if (drivewayMaterial == null ||
                drivewayMaterial.GetTexture("_BaseMap") == null ||
                drivewayMaterial.GetTexture("_BumpMap") == null ||
                drivewayMaterial.GetFloat("_BumpScale") <= 0f)
            {
                throw new InvalidOperationException(
                    $"Seed {seed} did not apply the pebble albedo and normal-mapped driveway material.");
            }

            int garageConnections = 0;
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

                Vector3 roadToGarage = flatSegment.normalized;
                if (Vector3.Dot(roadToGarage, -property.FrontageDirection) < 0.995f)
                    throw new InvalidOperationException(
                        $"Seed {seed} driveway '{property.name}' bends instead of running straight to the road.");

                float startHeightError = Mathf.Abs(
                    property.DrivewayStart.y - generator.SampleTerrainHeight(property.DrivewayStart));
                if (startHeightError > 0.15f)
                {
                    throw new InvalidOperationException(
                        $"Seed {seed} driveway '{property.name}' misses the road terrain by " +
                        $"{startHeightError:F3}m.");
                }

                Renderer garageDoor = FindGarageDoorRenderer(property);
                if (property.DrivewayAnchor == null && garageDoor != null)
                {
                    Vector3 endAtGarageHeight = property.DrivewayEnd;
                    endAtGarageHeight.y = garageDoor.bounds.center.y;
                    if (garageDoor.bounds.SqrDistance(endAtGarageHeight) > 0.04f)
                        throw new InvalidOperationException(
                            $"Seed {seed} driveway '{property.name}' does not finish at its garage door.");
                    garageConnections++;
                }
            }

            if (garageConnections == 0)
                throw new InvalidOperationException(
                    $"Seed {seed} did not validate any automatic garage-door driveway connections.");
        }

        private static Renderer FindGarageDoorRenderer(GeneratedProperty property)
        {
            Renderer[] renderers = property.GetComponentsInChildren<Renderer>(true);
            Renderer largestGarageDoor = null;
            float largestArea = 0f;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                bool isGarageDoor = ContainsGarageDoor(renderer.name);
                Transform current = renderer.transform.parent;
                while (current != null && current != property.transform)
                {
                    isGarageDoor |= ContainsGarageDoor(current.name);
                    current = current.parent;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    if (materials[materialIndex] != null &&
                        ContainsGarageDoor(materials[materialIndex].name))
                        isGarageDoor = true;

                if (!isGarageDoor) continue;
                Bounds bounds = renderer.bounds;
                float area = Mathf.Max(
                    bounds.size.x * bounds.size.y,
                    Mathf.Max(
                        bounds.size.x * bounds.size.z,
                        bounds.size.y * bounds.size.z));
                if (area <= largestArea) continue;
                largestGarageDoor = renderer;
                largestArea = area;
            }

            return largestGarageDoor;
        }

        private static bool ContainsGarageDoor(string value) =>
            !string.IsNullOrEmpty(value) &&
            value.IndexOf("garage", StringComparison.OrdinalIgnoreCase) >= 0 &&
            value.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void ValidateGrass(NeighbourhoodGenerator generator, int seed)
        {
            if (!generator.GeneratedTerrain.TryGetComponent(out MeshRenderer terrainRenderer) ||
                terrainRenderer.sharedMaterial == null ||
                terrainRenderer.sharedMaterial.shader == null ||
                terrainRenderer.sharedMaterial.shader.name != "Couch Guys/Suburbs Mowed Lawn")
            {
                throw new InvalidOperationException(
                    $"Seed {seed} did not apply the generated mowed-lawn material.");
            }

            GeneratedGrassTufts grass = generator.GetComponentInChildren<GeneratedGrassTufts>(true);
            if (grass == null || grass.TuftCount <= 0 ||
                !grass.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null ||
                filter.sharedMesh.vertexCount != grass.TuftCount * 8 ||
                grass.TryGetComponent(out Collider _))
            {
                throw new InvalidOperationException(
                    $"Seed {seed} generated invalid or colliding decorative grass tufts.");
            }
        }
    }
}
