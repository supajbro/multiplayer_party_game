using CouchGuys.Player;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Resolves the imported FBX face slot and bakes the reference into Player.prefab.</summary>
    public static class RobotFacePrefabSetup
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string FaceMaterialPath = "Assets/_Project/Resources/RobotDigitalFace.mat";

        [InitializeOnLoadMethod]
        private static void ConfigureMissingPrefabReference()
        {
            EditorApplication.delayCall += () =>
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                RobotFaceExpressionController controller =
                    prefab != null ? prefab.GetComponent<RobotFaceExpressionController>() : null;
                if (controller == null) return;

                SerializedObject serializedController = new(controller);
                if (serializedController.FindProperty("m_faceRenderer").objectReferenceValue == null)
                    ConfigurePlayerPrefab();
            };
        }

        [MenuItem("Couch Guys/Art/Configure Robot Digital Face %#9")]
        public static void ConfigurePlayerPrefab()
        {
            Material faceMaterial = AssetDatabase.LoadAssetAtPath<Material>(FaceMaterialPath);
            if (faceMaterial == null)
                throw new UnityException($"Robot face material is missing at {FaceMaterialPath}.");

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                RobotFaceExpressionController controller =
                    root.GetComponent<RobotFaceExpressionController>() ??
                    root.AddComponent<RobotFaceExpressionController>();
                if (!TryFindFaceSlot(root, out Renderer faceRenderer, out int materialIndex))
                    throw new UnityException("Could not find a face, screen, display, or visor renderer in Player.prefab.");

                Material[] materials = faceRenderer.sharedMaterials;
                materials[materialIndex] = faceMaterial;
                faceRenderer.sharedMaterials = materials;

                SerializedObject serializedController = new(controller);
                serializedController.FindProperty("m_faceRenderer").objectReferenceValue = faceRenderer;
                serializedController.FindProperty("m_materialIndex").intValue = materialIndex;
                serializedController.FindProperty("m_faceMaterial").objectReferenceValue = faceMaterial;
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log(
                    $"Configured robot digital face on renderer '{GetPath(faceRenderer.transform, root.transform)}', " +
                    $"material slot {materialIndex} ('{faceRenderer.sharedMaterials[materialIndex].name}').");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        internal static bool TryFindFaceSlot(
            GameObject root,
            out Renderer bestRenderer,
            out int bestMaterialIndex)
        {
            bestRenderer = null;
            bestMaterialIndex = 0;
            int bestScore = int.MinValue;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer candidate = renderers[rendererIndex];
                if (candidate is ParticleSystemRenderer) continue;

                int rendererScore = NameScore(candidate.name);
                Material[] materials = candidate.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    int score = rendererScore + NameScore(materials[materialIndex]?.name);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestRenderer = candidate;
                    bestMaterialIndex = materialIndex;
                }
            }

            if (bestScore > 0) return true;
            bestRenderer = null;
            return false;
        }

        private static int NameScore(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            string lower = value.ToLowerInvariant();
            int score = 0;
            if (lower == "face") score += 100;
            if (lower.Contains("face")) score += 45;
            if (lower.Contains("screen")) score += 35;
            if (lower.Contains("display")) score += 30;
            if (lower.Contains("visor")) score += 20;
            return score;
        }

        private static string GetPath(Transform target, Transform root)
        {
            string path = target.name;
            while (target.parent != null && target.parent != root)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return root.name + "/" + path;
        }
    }
}
