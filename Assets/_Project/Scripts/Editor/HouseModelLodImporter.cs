using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>
    /// Keeps the approved house FBX on Unity 6's generated mesh-LOD path. Each imported Mesh
    /// stores reduced index ranges, so the wrapper prefab can reuse the same materials and mesh
    /// asset while its LODGroup forces a different genuine geometry level on each renderer set.
    /// </summary>
    public sealed class HouseModelLodImporter : AssetPostprocessor
    {
        private const int MaximumMeshLod = 3;

        private void OnPreprocessModel()
        {
            if (assetPath != HousePrefabBuilder.HouseModelPath)
            {
                return;
            }

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.generateMeshLods = true;
            importer.meshLodGenerationFlags = 0;
            importer.maximumMeshLod = MaximumMeshLod;
        }

        [MenuItem("Couch Guys/Optimisation/Reimport And Audit House LODs")]
        public static void ReimportAndAudit()
        {
            ModelImporter importer = AssetImporter.GetAtPath(HousePrefabBuilder.HouseModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"House model importer not found at {HousePrefabBuilder.HouseModelPath}.");
                return;
            }

            importer.generateMeshLods = true;
            importer.meshLodGenerationFlags = 0;
            importer.maximumMeshLod = MaximumMeshLod;
            importer.SaveAndReimport();
            HousePrefabBuilder.EnsureHousePrefabs();
            LogTriangleAudit();
        }

        public static void LogTriangleAudit()
        {
            Mesh[] meshes = AssetDatabase.LoadAllAssetsAtPath(HousePrefabBuilder.HouseModelPath)
                .OfType<Mesh>()
                .ToArray();
            long[] triangleTotals = new long[MaximumMeshLod + 1];
            int meshesWithAllLevels = 0;

            for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
            {
                Mesh mesh = meshes[meshIndex];
                if (mesh.lodCount >= triangleTotals.Length)
                {
                    meshesWithAllLevels++;
                }

                for (int level = 0; level < triangleTotals.Length; level++)
                {
                    int availableLevel = Mathf.Clamp(level, 0, Mathf.Max(0, mesh.lodCount - 1));
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    {
                        triangleTotals[level] += (long)mesh.GetLod(submesh, availableLevel).indexCount / 3L;
                    }
                }
            }

            string ratios = triangleTotals[0] <= 0
                ? "unavailable"
                : $"LOD1={100d * triangleTotals[1] / triangleTotals[0]:F1}%, " +
                  $"LOD2={100d * triangleTotals[2] / triangleTotals[0]:F1}%, " +
                  $"LOD3={100d * triangleTotals[3] / triangleTotals[0]:F1}%";
            Debug.Log(
                $"House LOD triangle audit: meshes={meshes.Length}, " +
                $"meshesWithFourLevels={meshesWithAllLevels}, " +
                $"LOD0={triangleTotals[0]:N0}, LOD1={triangleTotals[1]:N0}, " +
                $"LOD2={triangleTotals[2]:N0}, LOD3={triangleTotals[3]:N0}; {ratios}.");
        }
    }
}
