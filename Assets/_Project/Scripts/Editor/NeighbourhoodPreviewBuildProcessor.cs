using CouchGuys.ProceduralGeneration;
using FishNet.Component.Observing;
using FishNet.Object;
using FishNet.Observing;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    /// <summary>Omits editor-generated neighbourhoods from the build's scene copy.</summary>
    [BuildCallbackVersion(2)]
    public sealed class NeighbourhoodPreviewBuildProcessor : IProcessSceneWithReport
    {
        // Remove preview objects before other processors register scene objects.
        public int callbackOrder => -10000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Run for Play Mode too: scene objects must not spawn on peers until
            // FishNet has received their scene-load acknowledgement.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkObject networkObject in root.GetComponentsInChildren<NetworkObject>(true))
                {
                    NetworkObserver observer = networkObject.GetComponent<NetworkObserver>();
                    if (observer == null) observer = networkObject.gameObject.AddComponent<NetworkObserver>();
                    bool hasSceneCondition = false;
                    foreach (ObserverCondition existing in observer.ObserverConditions)
                        if (existing is SceneCondition) { hasSceneCondition = true; break; }
                    if (hasSceneCondition && observer.OverrideType != NetworkObserver.ConditionOverrideType.UseManager) continue;
                    SceneCondition condition = AssetDatabase.LoadAssetAtPath<SceneCondition>(
                        "Packages/com.firstgeargames.fishnet/Runtime/Observing/Conditions/ScriptableObjects/SceneCondition.asset");
                    if (condition == null) throw new BuildFailedException("FishNet SceneCondition asset is missing.");
                    SerializedObject serialized = new SerializedObject(observer);
                    SerializedProperty conditions = serialized.FindProperty("_observerConditions");
                    if (!hasSceneCondition)
                    {
                        int index = conditions.arraySize++;
                        conditions.GetArrayElementAtIndex(index).objectReferenceValue = condition;
                    }
                    // Keep explicit conditions when ObserverManager supplies defaults.
                    serialized.FindProperty("_overrideType").intValue = (int)NetworkObserver.ConditionOverrideType.AddMissing;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Scene processors also run when entering Play Mode.
            if (!BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            int removed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (GeneratedNeighbourhoodRoot preview in
                         root.GetComponentsInChildren<GeneratedNeighbourhoodRoot>(true))
                {
                    if (preview == null) continue;
                    // This is the temporary build scene, never the saved editor scene.
                    Object.DestroyImmediate(preview.gameObject);
                    removed++;
                }
            }

            if (removed > 0)
            {
                Debug.Log($"Excluded {removed} generated neighbourhood preview(s) from '{scene.name}'. " +
                          "The neighbourhood will be generated at runtime.");
            }
        }
    }
}
