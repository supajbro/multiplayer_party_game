using CouchGuys.Gameplay.Delivery;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Creates the editable default delivery NPC prefab without overwriting customisations.</summary>
    public static class DeliveryNPCPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/DeliveryNPC.prefab";

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabAfterImport()
        {
            EditorApplication.delayCall += () => EnsureDeliveryNpcPrefab();
        }

        [MenuItem("Couch Guys/Build Delivery NPC Prefab")]
        public static void BuildDeliveryNpcPrefab()
        {
            EnsureDeliveryNpcPrefab();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        public static GameObject EnsureDeliveryNpcPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<DeliveryNPC>() != null)
            {
                return existing;
            }

            GameObject root = new GameObject("DeliveryNPC");
            try
            {
                root.AddComponent<DeliveryNPC>();

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.up;
                Object.DestroyImmediate(visual.GetComponent<Collider>());

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null)
                {
                    throw new UnityException($"Failed to save the Delivery NPC prefab at {PrefabPath}.");
                }

                AssetDatabase.SaveAssets();
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
