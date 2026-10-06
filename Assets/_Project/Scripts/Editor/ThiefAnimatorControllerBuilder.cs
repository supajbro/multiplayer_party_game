using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Builds weapon-specific controllers from the Actions embedded in each thief FBX.</summary>
    public static class ThiefAnimatorControllerBuilder
    {
        public const string AnimationFolder = "Assets/_Project/Animations/Thieves";

        private static readonly string[] RequiredClips =
        {
            "Thief_Idle", "Thief_Walk", "Thief_Shoot", "Thief_Death"
        };

        [MenuItem("Couch Guys/Animation/Build Thief Animator Controllers")]
        public static void BuildAll()
        {
            Build(ThiefPrefabBuilder.PistolModelPath, ThiefPrefabBuilder.PistolControllerPath);
            Build(ThiefPrefabBuilder.AssaultRifleModelPath, ThiefPrefabBuilder.AssaultRifleControllerPath);
            Build(ThiefPrefabBuilder.ShotgunModelPath, ThiefPrefabBuilder.ShotgunControllerPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void EnsureAll()
        {
            Ensure(ThiefPrefabBuilder.PistolModelPath, ThiefPrefabBuilder.PistolControllerPath);
            Ensure(ThiefPrefabBuilder.AssaultRifleModelPath, ThiefPrefabBuilder.AssaultRifleControllerPath);
            Ensure(ThiefPrefabBuilder.ShotgunModelPath, ThiefPrefabBuilder.ShotgunControllerPath);
        }

        private static void Ensure(string modelPath, string controllerPath)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) == null)
            {
                Build(modelPath, controllerPath);
            }
        }

        private static void Build(string modelPath, string controllerPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null)
            {
                throw new UnityException($"Thief model was not found at {modelPath}.");
            }

            ConfigureImporter(modelPath);
            Dictionary<string, AnimationClip> clips = LoadClips(modelPath);
            List<string> missing = new();
            foreach (string required in RequiredClips)
            {
                if (!clips.ContainsKey(required))
                {
                    missing.Add(required);
                }
            }

            if (missing.Count > 0)
            {
                throw new UnityException(
                    $"The thief FBX at {modelPath} is missing clips: {string.Join(", ", missing)}.");
            }

            EnsureFolder(AnimationFolder);
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                AssetDatabase.DeleteAsset(controllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = AddState(machine, "Idle", clips["Thief_Idle"], new Vector3(250f, 30f));
            AnimatorState walk = AddState(machine, "Walk", clips["Thief_Walk"], new Vector3(500f, 30f));
            AnimatorState shoot = AddState(machine, "Shoot", clips["Thief_Shoot"], new Vector3(375f, -120f));
            AnimatorState death = AddState(machine, "Death", clips["Thief_Death"], new Vector3(650f, -120f));
            machine.defaultState = idle;

            AddCondition(idle, walk, "Speed", AnimatorConditionMode.Greater, 0.08f, false);
            AddCondition(walk, idle, "Speed", AnimatorConditionMode.Less, 0.08f, false);

            AnimatorStateTransition shootTransition = machine.AddAnyStateTransition(shoot);
            shootTransition.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");
            ConfigureTransition(shootTransition, false, 0f, 0.04f);

            AddCondition(shoot, walk, "Speed", AnimatorConditionMode.Greater, 0.08f, true, 0.92f);
            AddCondition(shoot, idle, "Speed", AnimatorConditionMode.Less, 0.08f, true, 0.92f);

            AnimatorStateTransition deathTransition = machine.AddAnyStateTransition(death);
            deathTransition.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            deathTransition.canTransitionToSelf = false;
            ConfigureTransition(deathTransition, false, 0f, 0.06f);

            EditorUtility.SetDirty(controller);
            Debug.Log($"Built thief Animator Controller at {controllerPath}.");
        }

        private static void ConfigureImporter(string modelPath)
        {
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer)
            {
                throw new UnityException($"The asset at {modelPath} is not an imported model.");
            }

            bool changed = !importer.importAnimation ||
                           importer.animationType != ModelImporterAnimationType.Generic ||
                           importer.optimizeGameObjects;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.optimizeGameObjects = false;

            ModelImporterClipAnimation[] importedClips = importer.clipAnimations;
            if (importedClips == null || importedClips.Length == 0)
            {
                importedClips = importer.defaultClipAnimations;
            }

            foreach (ModelImporterClipAnimation clip in importedClips)
            {
                string name = NormaliseClipName(clip.name);
                bool shouldLoop = name == "Thief_Idle" || name == "Thief_Walk";
                if (clip.loopTime != shouldLoop || clip.loopPose != shouldLoop)
                {
                    clip.loopTime = shouldLoop;
                    clip.loopPose = shouldLoop;
                    changed = true;
                }
            }

            if (changed || importer.clipAnimations == null || importer.clipAnimations.Length == 0)
            {
                importer.clipAnimations = importedClips;
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, AnimationClip> LoadClips(string modelPath)
        {
            Dictionary<string, AnimationClip> clips = new(StringComparer.Ordinal);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (asset is AnimationClip clip &&
                    !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    clips[NormaliseClipName(clip.name)] = clip;
                }
            }

            return clips;
        }

        private static AnimatorState AddState(
            AnimatorStateMachine machine, string name, Motion motion, Vector3 position)
        {
            AnimatorState state = machine.AddState(name, position);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static void AddCondition(
            AnimatorState from, AnimatorState to, string parameter,
            AnimatorConditionMode mode, float threshold, bool hasExitTime, float exitTime = 0f)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.AddCondition(mode, threshold, parameter);
            ConfigureTransition(transition, hasExitTime, exitTime, 0.1f);
        }

        private static void ConfigureTransition(
            AnimatorStateTransition transition, bool hasExitTime, float exitTime, float duration)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
        }

        private static string NormaliseClipName(string clipName)
        {
            int separator = clipName.LastIndexOf('|');
            return separator >= 0 ? clipName[(separator + 1)..] : clipName;
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] sections = folderPath.Split('/');
            string current = sections[0];
            for (int index = 1; index < sections.Length; index++)
            {
                string next = $"{current}/{sections[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, sections[index]);
                }
                current = next;
            }
        }
    }
}
