using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.EditorTools
{
    [CustomEditor(typeof(NeighbourhoodGenerator))]
    public sealed class NeighbourhoodGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            NeighbourhoodGenerator generator = (NeighbourhoodGenerator)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate"))
                {
                    generator.Generate();
                    MarkChanged(generator);
                }

                if (GUILayout.Button("Regenerate"))
                {
                    generator.Generate(generator.CurrentSeed != 0 ? generator.CurrentSeed : generator.SelectSeed());
                    MarkChanged(generator);
                }

                if (GUILayout.Button("Clear"))
                {
                    generator.Clear();
                    MarkChanged(generator);
                }
            }

            EditorGUILayout.HelpBox(
                "For multiplayer, add NeighbourhoodSeedSynchroniser to this GameObject. " +
                "Unity will also add the required FishNet NetworkObject.",
                MessageType.Info);
        }

        private static void MarkChanged(NeighbourhoodGenerator generator)
        {
            EditorUtility.SetDirty(generator);
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            }
        }
    }
}
