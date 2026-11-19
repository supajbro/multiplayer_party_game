using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, one-off wiring of the saved thief prefabs; never rebuilds their models.</summary>
    [InitializeOnLoad]
    public static class CarryPresentationAuthoring
    {
        static CarryPresentationAuthoring() => EditorApplication.update += CheckRequest;

        private static void CheckRequest()
        {
            // A caller must explicitly create this request. Import/reload alone never edits assets.
            const string request = "Temp/CarryPresentation.Author.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try { WirePrefabs(); }
            catch (Exception e) { File.WriteAllText("Temp/CarryPresentation.Author.result", e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Couch Guys/Thieves/Wire Carry Presentation")]
        public static void WirePrefabs()
        {
            var effect = AssetDatabase.LoadAssetAtPath<MagneticCarryEffect>("Assets/_Project/Prefabs/VFX/MagneticCarryAura.prefab");
            if (effect == null) throw new Exception("Missing magnetic carry effect prefab.");
            string report = "";
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Enemies" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab.GetComponent<ThiefNavigator>() == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var aura = root.GetComponent<MagneticCarryAura>() ?? root.AddComponent<MagneticCarryAura>();
                    var so = new SerializedObject(aura);
                    so.FindProperty("m_thief").objectReferenceValue = root.GetComponent<ThiefNavigator>();
                    so.FindProperty("m_auraPrefab").objectReferenceValue = effect;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    var weapon = root.GetComponent<EnemyWeapon>();
                    if (weapon != null)
                    {
                        var renderers = root.GetComponentsInChildren<Renderer>(true)
                            .Where(r => r.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                r.sharedMaterials.Any(m => m != null && m.name.StartsWith("CG_Weapon_"))).ToArray();
                        report += path + ": " + string.Join(", ", renderers.Select(r => r.name)) + "\n";
                        if (renderers.Length == 0) throw new Exception("No separate held weapon renderers found: " + path);
                        so = new SerializedObject(weapon);
                        var property = so.FindProperty("m_heldWeaponRenderers");
                        property.arraySize = renderers.Length;
                        for (int i = 0; i < renderers.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            File.WriteAllText("Temp/CarryPresentation.Author.result", "PASS\n" + report);
        }
    }
}
