using CouchGuys.Gameplay.Delivery;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class SuburbsProgressionAssetBuilder
    {
        private const string SettingsFolder = "Assets/_Project/Settings/Gameplay";
        private const string DefinitionPath = SettingsFolder + "/SuburbsChapterDefinition.asset";

        public static SuburbsChapterDefinition EnsureDefinition()
        {
            SuburbsChapterDefinition definition =
                AssetDatabase.LoadAssetAtPath<SuburbsChapterDefinition>(DefinitionPath);
            if (definition != null)
            {
                return definition;
            }

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Settings"))
                {
                    AssetDatabase.CreateFolder("Assets/_Project", "Settings");
                }

                AssetDatabase.CreateFolder("Assets/_Project/Settings", "Gameplay");
            }

            definition = ScriptableObject.CreateInstance<SuburbsChapterDefinition>();
            definition.ResetToDefaults();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }
    }
}
