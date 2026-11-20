using CouchGuys.Player;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Explicit opt-in authoring only. Never modifies assets on reload/import.</summary>
    public static class RobotFacePrefabSetup
    {
        [MenuItem("Couch Guys/Art/Configure Robot Digital Face %#9")]
        public static void ConfigurePlayerPrefab()
        {
            const string path = "Assets/_Project/Prefabs/Player.prefab";
            var settings = AssetDatabase.LoadAssetAtPath<RobotFaceDisplaySettings>(
                "Assets/_Project/Resources/RobotFaceDisplaySettings.asset");
            if (settings == null) throw new UnityException("RobotFaceDisplaySettings is missing.");
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var controller = root.GetComponent<RobotFaceExpressionController>();
                if (controller == null) throw new UnityException("Existing robot expression controller is missing.");
                Renderer screen = System.Array.Find(root.GetComponentsInChildren<Renderer>(true), r => r.name == "FaceDisplay");
                if (screen == null) throw new UnityException("Existing FaceDisplay mesh is missing.");
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("m_faceRenderer").objectReferenceValue = screen;
                serialized.FindProperty("m_materialIndex").intValue = 0;
                serialized.FindProperty("m_displaySettings").objectReferenceValue = settings;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("Saved CRT settings and existing screen reference. Original materials and hierarchy preserved.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
