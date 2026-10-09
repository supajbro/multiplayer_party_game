using System;
using System.Linq;
using CouchGuys.Gameplay.Delivery;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Creates and maintains the reusable frog delivery NPC prefab.</summary>
    public static class DeliveryNPCPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/DeliveryNPC.prefab";
        private const string FrogModelPath = "Assets/_Project/Models/DeliveryMan/frog_01.fbx";
        private const string ControllerPath = "Assets/_Project/Animations/DeliveryManagerIdle.controller";
        private const string VisualName = "FrogVisual";
        private const float TargetHeight = 2.2f;

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabAfterImport()
        {
            EditorApplication.delayCall += BuildAfterImport;
        }

        [MenuItem("Couch Guys/Build Delivery NPC Prefab")]
        public static void BuildDeliveryNpcPrefab()
        {
            ConfigureFrogImporter();
            Selection.activeObject = EnsureDeliveryNpcPrefab(true);
        }

        public static GameObject EnsureDeliveryNpcPrefab(bool forceRebuild = false)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!forceRebuild && IsConfigured(existing))
            {
                return existing;
            }

            AnimationClip idleClip = FindIdleClip();
            GameObject frogModel = AssetDatabase.LoadAssetAtPath<GameObject>(FrogModelPath);
            if (idleClip == null || frogModel == null)
            {
                Debug.LogWarning($"Delivery NPC frog model or idle clip is missing at {FrogModelPath}.");
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            RuntimeAnimatorController controller = EnsureIdleController(idleClip);
            bool loadedContents = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = loadedContents
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("DeliveryNPC");
            try
            {
                root.name = "DeliveryNPC";
                EnsureGameplayComponents(root);
                ReplaceVisual(root, frogModel, controller);

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
                if (loadedContents)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static bool IsConfigured(GameObject prefab)
        {
            if (prefab == null || prefab.GetComponent<DeliveryNPC>() == null ||
                prefab.GetComponent<CapsuleCollider>() == null)
            {
                return false;
            }

            Transform visual = prefab.transform.Find(VisualName);
            Animator animator = visual != null ? visual.GetComponent<Animator>() : null;
            return animator != null &&
                AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) == ControllerPath;
        }
        private static void BuildAfterImport()
        {
            ConfigureFrogImporter();
            EnsureDeliveryNpcPrefab();
        }

        private static void ConfigureFrogImporter()
        {
            if (AssetImporter.GetAtPath(FrogModelPath) is not ModelImporter importer)
            {
                return;
            }

            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            foreach (ModelImporterClipAnimation clip in clips)
            {
                bool isIdle = clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isIdle && !clip.loopTime)
                {
                    clip.loopTime = true;
                    changed = true;
                }
            }

            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        private static AnimationClip FindIdleClip()
        {
            return AssetDatabase.LoadAllAssetsAtPath(FrogModelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip =>
                    !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase) &&
                    clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static RuntimeAnimatorController EnsureIdleController(AnimationClip idleClip)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState idleState = stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => state.name == "Idle");
            if (idleState == null)
            {
                idleState = stateMachine.AddState("Idle");
            }

            idleState.motion = idleClip;
            idleState.speed = 1f;
            stateMachine.defaultState = idleState;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void EnsureGameplayComponents(GameObject root)
        {
            if (root.GetComponent<DeliveryNPC>() == null)
            {
                root.AddComponent<DeliveryNPC>();
            }

            CapsuleCollider interactionCollider = root.GetComponent<CapsuleCollider>();
            if (interactionCollider == null)
            {
                interactionCollider = root.AddComponent<CapsuleCollider>();
                interactionCollider.radius = 0.5f;
                interactionCollider.height = 2f;
                interactionCollider.center = Vector3.up;
            }
        }

        private static void ReplaceVisual(
            GameObject root,
            GameObject frogModel,
            RuntimeAnimatorController controller)
        {
            for (int index = root.transform.childCount - 1; index >= 0; index--)
            {
                Transform child = root.transform.GetChild(index);
                if (child.name == "Visual" || child.name == VisualName)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(frogModel, root.transform);
            visual.name = VisualName;
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.transform.localScale = Vector3.one;

            foreach (Collider visualCollider in visual.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(visualCollider);
            }

            Animator animator = visual.GetComponent<Animator>();
            if (animator == null)
            {
                animator = visual.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            FitVisualToRoot(visual);
        }

        private static void FitVisualToRoot(GameObject visual)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            if (bounds.size.y <= Mathf.Epsilon)
            {
                return;
            }

            visual.transform.localScale = Vector3.one * (TargetHeight / bounds.size.y);
            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            visual.transform.position += Vector3.up * (visual.transform.parent.position.y - bounds.min.y);
        }
    }
}