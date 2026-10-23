using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Creates an editable wrapper while keeping the imported FBX linked.</summary>
    public static class StreetLightPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Environment/StreetLight.prefab";
        private const string LegacyPath = "Assets/_Project/Prefabs/Environment/Placeholders/StreetLight_Placeholder.prefab";
        private const string ModelPath = "Assets/_Project/Models/StreetLights/street_light_02.fbx";

        [InitializeOnLoadMethod]
        private static void BuildAfterImport() => EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EnsurePrefab();
        };

        public static GameObject EnsurePrefab()
        {
            // Existing wrappers belong to the artist; never overwrite editor adjustments.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null) return prefab;
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) return null;

            // Move the original asset to preserve its GUID and existing region references.
            bool migrating = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyPath) != null;
            if (migrating)
            {
                string error = AssetDatabase.MoveAsset(LegacyPath, PrefabPath);
                if (!string.IsNullOrEmpty(error)) throw new System.InvalidOperationException(error);
            }

            GameObject root = migrating ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("StreetLight");
            try
            {
                root.name = "StreetLight";
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                if (!migrating)
                {
                    root.AddComponent<EnvironmentPlacementMetadata>().Configure(new Vector2(1.1f, 1.1f), 0.35f, true, true);
                    root.AddComponent<StreetlightPlaceholder>();
                }

                var alignment = new GameObject("ModelAlignment").transform;
                alignment.SetParent(root.transform, false);
                // The FBX arm points left; generated lights expect the arm along local +Z.
                alignment.localRotation = Quaternion.Euler(0f, 90f, 0f);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, alignment);
                visual.name = "street_light_02";
                Bounds bounds = visual.GetComponentInChildren<Renderer>().bounds;
                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                float scale = 6.2f / bounds.size.y;
                alignment.localScale = Vector3.one * scale;
                alignment.localPosition = Vector3.up * (-bounds.min.y * scale);

                var lamp = new GameObject("Light");
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.localPosition = new Vector3(0f, 5.55f, 1.48f);
                Light light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.enabled = false;
                light.color = new Color(1f, 0.68f, 0.28f);
                light.intensity = 2.2f;
                light.range = 11f;
                var controller = new SerializedObject(root.GetComponent<StreetlightPlaceholder>());
                controller.FindProperty("m_light").objectReferenceValue = light;
                controller.ApplyModifiedPropertiesWithoutUndo();

                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"StreetLight prefab created: height 6.2, arm +Z, model scale {scale:F6}.");
                return prefab;
            }
            finally
            {
                if (migrating) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }
    }
}
