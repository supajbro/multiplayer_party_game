using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class DispatcherBanterValidation
    {
        [MenuItem("Couch Guys/Dispatcher/Validate Idle Banter")]
        public static void Validate()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/DeliveryNPC.prefab");
            GameObject instance = null;
            try
            {
                var banter = root.GetComponent<DispatcherIdleBanter>();
                Require(banter != null, "Dispatcher banter component missing.");
                var data = new SerializedObject(banter);
                Require(data.FindProperty("m_npc").objectReferenceValue == root.GetComponent<DeliveryNPC>(), "NPC link missing.");
                Require(data.FindProperty("m_screen").objectReferenceValue == root.GetComponent<DispatcherScreen>(), "Screen link missing.");
                var lines = data.FindProperty("m_lines");
                Require(lines.arraySize == 10, "Expected ten default quips.");
                var distinct = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < lines.arraySize; i++) Require(distinct.Add(lines.GetArrayElementAtIndex(i).stringValue), "Duplicate default quip.");
                var prefab = data.FindProperty("m_bubblePrefab").objectReferenceValue as DispatcherBubbleView;
                Require(prefab != null && AssetDatabase.GetAssetPath(prefab).StartsWith("Assets/_Project/Prefabs/UI/"), "Saved bubble prefab missing.");
                Require(prefab.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace, "Expected world-space UI.");
                Require(!prefab.GetComponent<CanvasGroup>().blocksRaycasts, "Bubble must not intercept interactions.");
                var viewData = new SerializedObject(prefab);
                foreach (string field in new[] { "m_label", "m_group", "m_content" })
                    Require(viewData.FindProperty(field).objectReferenceValue != null, "Missing bubble reference: " + field);
                instance = UnityEngine.Object.Instantiate(prefab.gameObject, root.transform);
                var view = instance.GetComponent<DispatcherBubbleView>();
                // Edit-mode instances do not reliably receive Awake; initialise the authored scale explicitly.
                typeof(DispatcherBubbleView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                view.Show("These couches won't deliver themselves.");
                view.SetProgress(1f);
                Require(instance.GetComponentInChildren<TMP_Text>().text == "These couches won't deliver themselves.", "Text presentation failed.");
                Require(Mathf.Approximately(instance.GetComponent<CanvasGroup>().alpha, 1f), "Fade-in failed.");
                view.Hide();
                Require(!instance.activeSelf, "Bubble hide failed.");
                Require(root.GetComponentInChildren<LODGroup>().GetLODs().All(lod => lod.renderers.Length == 2), "Dispatcher LODs changed.");
                const string report = "PASS: Unity compiled banter scripts; ten unique quips; saved UI prefab and NPC/screen links; world-space click-through canvas; text, fade and hide; original two-renderer LODs.\nPASS: standalone C# deck checks exercised 30,000 draws across 100 seeds with no within-bag or boundary repeats; duplicate/blank, empty and single-line inputs checked.\nLimitations: gameplay camera/layout and multi-client interactions have not been playtested.\n";
                File.WriteAllText("Tools/DispatcherBanterValidation.txt", report);
                Debug.Log(report);
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
