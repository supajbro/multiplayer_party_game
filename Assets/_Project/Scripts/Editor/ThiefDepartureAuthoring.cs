using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Enemies;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, additive authoring of the departure clip in existing controllers.</summary>
    public static class ThiefDepartureAuthoring
    {
        [MenuItem("Couch Guys/Animation/Add Thief Departure Animation %#&F10")]
        public static void Apply()
        {
            foreach (string variant in new[] { "Pistol", "AssaultRifle", "Shotgun" })
                AddToController(variant);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/ThiefDepartureAssets.txt", "PASS imported one-second frustration clips and updated three existing controllers in place.\n");
            Debug.Log("Thief departure animation assets saved.");
        }

        private static void AddToController(string variant)
        {
            string model = $"Assets/_Project/Models/Thieves/Character01Thief/Armed/Character01_Thief_{variant}.fbx";
            string path = $"Assets/_Project/Animations/Thieves/Character01/Character01_Thief_{variant}.controller";
            AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(model);
            var clips = new List<ModelImporterClipAnimation>(importer.clipAnimations);
            if (!clips.Any(c => c.name.EndsWith("Thief_Frustration", StringComparison.Ordinal)))
            {
                var clip = importer.defaultClipAnimations.First(c =>
                    c.name.EndsWith("Thief_Frustration", StringComparison.Ordinal));
                clip.loopTime = false;
                clip.loopPose = false;
                clip.keepOriginalPositionXZ = true;
                clip.keepOriginalPositionY = true;
                clips.Add(clip);
                importer.clipAnimations = clips.ToArray();
                importer.SaveAndReimport();
            }
            var motion = AssetDatabase.LoadAllAssetsAtPath(model).OfType<AnimationClip>().First(c =>
                !c.name.StartsWith("__preview__", StringComparison.Ordinal) &&
                c.name.EndsWith("Thief_Frustration", StringComparison.Ordinal));
            if (Mathf.Abs(motion.length - 1f) > .04f)
                throw new Exception($"Unexpected frustration clip duration in {model}: {motion.length}");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var machine = controller.layers[0].stateMachine;
            if (!controller.parameters.Any(p => p.name == "FrustrationSpeed"))
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = "FrustrationSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f
                });
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Frustration");
            if (state == null)
            {
                state = machine.AddState("Frustration", new Vector3(250, 180));
                AddExit(state, machine.states.First(s => s.state.name == "Idle").state, AnimatorConditionMode.Less);
                AddExit(state, machine.states.First(s => s.state.name == "Walk").state, AnimatorConditionMode.Greater);
            }
            state.motion = motion;
            state.speedParameter = "FrustrationSpeed";
            state.speedParameterActive = true;
            state.writeDefaultValues = true;
            EditorUtility.SetDirty(controller);
        }

        private static void AddExit(AnimatorState from, AnimatorState to, AnimatorConditionMode condition)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = .98f;
            transition.hasFixedDuration = true;
            transition.duration = .08f;
            transition.AddCondition(condition, .08f, "Speed");
        }
    }
}
