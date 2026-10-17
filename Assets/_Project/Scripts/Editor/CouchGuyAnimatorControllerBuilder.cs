using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>
    /// Builds the generated robot's Animator Controller from the actions embedded in
    /// its FBX. Re-running this command intentionally refreshes the generated asset.
    /// </summary>
    public static class CouchGuyAnimatorControllerBuilder
    {
        public const string ModelPath = "Assets/_Project/Models/CouchGuy/CouchGuy_Redesigned.fbx";
        public const string ControllerPath = "Assets/_Project/Animations/CouchGuy.controller";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        private static bool s_installScheduled;

        private static readonly HashSet<string> LoopingClips = new(StringComparer.Ordinal)
        {
            "Idle",
            "HoverMove",
            "HoverMove_Backward",
            "Strafe_L",
            "Strafe_R",
            "Sprint",
            "Carry_Idle",
            "Carry_Move",
            "Carry_Sprint",
            "Hold_Item",
            "Pistol_Idle", "Pistol_Walk", "Pistol_Backward", "Pistol_Strafe_L", "Pistol_Strafe_R", "Pistol_Sprint",
            "AssaultRifle_Idle", "AssaultRifle_Walk", "AssaultRifle_Backward", "AssaultRifle_Strafe_L", "AssaultRifle_Strafe_R", "AssaultRifle_Sprint",
            "Shotgun_Idle", "Shotgun_Walk", "Shotgun_Backward", "Shotgun_Strafe_L", "Shotgun_Strafe_R", "Shotgun_Sprint"
        };

        [InitializeOnLoadMethod]
        private static void InstallMissingAnimationSetupAfterReload()
        {
            if (s_installScheduled || AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                return;
            }

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            AnimatorController existingController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            bool controllerMissing = existingController == null;
            bool controllerNeedsDirectionalUpgrade = existingController != null &&
                !ContainsBlendTree(existingController, "Pistol Directional Locomotion");
            bool modelMissingFromPlayer = playerPrefab == null ||
                playerPrefab.transform.Find("Visual/CouchGuy") == null ||
                playerPrefab.GetComponent<CouchGuys.Player.CouchGuyAnimationDriver>() == null;
            if (!controllerMissing && !controllerNeedsDirectionalUpgrade && !modelMissingFromPlayer)
            {
                return;
            }

            s_installScheduled = true;
            EditorApplication.delayCall += () =>
            {
                s_installScheduled = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                BuildAndInstall();
            };
        }

        private static bool ContainsBlendTree(AnimatorController controller, string blendTreeName)
        {
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                foreach (ChildAnimatorState childState in layer.stateMachine.states)
                {
                    if (childState.state.motion is BlendTree tree && tree.name == blendTreeName)
                        return true;
                }
            }
            return false;
        }

        [MenuItem("Couch Guys/Animation/Build and Install Couch Guy %#&F12")]
        public static void BuildAndInstall()
        {
            // The visual installer edits the existing prefab in place, preserving
            // weapon definitions, VFX prefabs, reticles, network IDs and tuning.
            // Rebuilding the whole Player prefab here silently reset those assets.
            CouchGuyPlayerVisualInstaller.Install();
        }

        public static void BuildAnimatorController()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                throw new UnityException(
                    $"Couch Guy model was not found at {ModelPath}. Export the Blender " +
                    "character there as an FBX with Bake Animation and All Actions enabled.");
            }

            ConfigureClipLooping();
            Dictionary<string, AnimationClip> clips = LoadClips();
            ValidateRequiredClips(clips);

            EnsureFolder("Assets/_Project/Animations");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AddParameters(controller);
            BuildStateMachine(controller, clips);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Couch Guy Animator Controller built successfully at {ControllerPath}.");
        }

        private static void ConfigureClipLooping()
        {
            if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
            {
                throw new UnityException($"The asset at {ModelPath} is not an imported 3D model.");
            }

            bool changed = false;
            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }

            if (importer.optimizeGameObjects)
            {
                importer.optimizeGameObjects = false;
                changed = true;
            }

            // Always seed from the FBX takes. Using the previously serialised
            // clipAnimations array here hid newly-authored Blender actions forever:
            // Unity kept reimporting the old 36-clip list even though the FBX had 48.
            ModelImporterClipAnimation[] animations = importer.defaultClipAnimations;
            ModelImporterClipAnimation[] configuredAnimations = importer.clipAnimations;
            HashSet<string> configuredNames = new(StringComparer.Ordinal);
            if (configuredAnimations != null)
            {
                foreach (ModelImporterClipAnimation configured in configuredAnimations)
                    configuredNames.Add(NormaliseClipName(configured.name));
            }

            if (animations == null || animations.Length == 0)
                throw new UnityException("The Couch Guy FBX contains no animation takes.");

            if (configuredNames.Count != animations.Length)
            {
                changed = true;
            }

            foreach (ModelImporterClipAnimation animation in animations)
            {
                string clipName = NormaliseClipName(animation.name);
                if (!configuredNames.Contains(clipName))
                    changed = true;
                bool shouldLoop = LoopingClips.Contains(clipName);
                if (animation.loopTime == shouldLoop)
                {
                    continue;
                }

                animation.loopTime = shouldLoop;
                animation.loopPose = shouldLoop;
                changed = true;
            }

            if (changed || configuredAnimations == null || configuredAnimations.Length == 0)
            {
                importer.clipAnimations = animations;
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, AnimationClip> LoadClips()
        {
            Dictionary<string, AnimationClip> clips = new(StringComparer.Ordinal);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (asset is not AnimationClip clip || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }

                clips[NormaliseClipName(clip.name)] = clip;
            }

            return clips;
        }

        private static void ValidateRequiredClips(IReadOnlyDictionary<string, AnimationClip> clips)
        {
            string[] required =
            {
                "Idle", "HoverMove", "HoverMove_Backward", "Strafe_L", "Strafe_R",
                "Pickup", "Carry_Idle", "Carry_Move", "Carry_Sprint", "Drop", "Hold_Item",
                "Point", "Hit_Reaction", "Jump"
                , "Pistol_Idle", "Pistol_Walk", "Pistol_Backward", "Pistol_Strafe_L", "Pistol_Strafe_R", "Pistol_Sprint", "Pistol_Shoot"
                , "AssaultRifle_Idle", "AssaultRifle_Walk", "AssaultRifle_Backward", "AssaultRifle_Strafe_L", "AssaultRifle_Strafe_R", "AssaultRifle_Sprint", "AssaultRifle_Shoot"
                , "Shotgun_Idle", "Shotgun_Walk", "Shotgun_Backward", "Shotgun_Strafe_L", "Shotgun_Strafe_R", "Shotgun_Sprint", "Shotgun_Shoot"
            };

            List<string> missing = new();
            foreach (string clipName in required)
            {
                if (!clips.ContainsKey(clipName))
                {
                    missing.Add(clipName);
                }
            }

            if (missing.Count > 0)
            {
                throw new UnityException(
                    $"The Couch Guy FBX is missing animation clips: {string.Join(", ", missing)}. " +
                    "Regenerate the Blender scene and export with All Actions enabled.");
            }
        }

        private static void AddParameters(AnimatorController controller)
        {
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsCarrying", AnimatorControllerParameterType.Bool);
            controller.AddParameter("HoldingItem", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Pointing", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Pickup", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Drop", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("EquippedWeapon", AnimatorControllerParameterType.Int);
            controller.AddParameter("WeaponShoot", AnimatorControllerParameterType.Trigger);
        }

        private static void BuildStateMachine(
            AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            machine.name = "Couch Guy Base Layer";

            BlendTree locomotionTree = CreateLocomotionTree(controller, clips);
            BlendTree carryingTree = CreateCarryingTree(controller, clips);

            AnimatorState locomotion = AddState(machine, "Locomotion", locomotionTree, new Vector3(260f, 60f));
            AnimatorState pickup = AddState(machine, "Pickup", clips["Pickup"], new Vector3(520f, -20f));
            AnimatorState carrying = AddState(machine, "Carrying", carryingTree, new Vector3(770f, 60f));
            AnimatorState drop = AddState(machine, "Drop", clips["Drop"], new Vector3(520f, 150f));
            AnimatorState holding = AddState(machine, "Hold Item", clips["Hold_Item"], new Vector3(260f, 270f));
            AnimatorState pointing = AddState(machine, "Point", clips["Point"], new Vector3(20f, 270f));
            AnimatorState jump = AddState(machine, "Jump", clips["Jump"], new Vector3(20f, 60f));
            AnimatorState hit = AddState(machine, "Hit Reaction", clips["Hit_Reaction"], new Vector3(520f, 300f));
            AnimatorState pistol = AddState(machine, "Pistol", CreateWeaponTree(controller, clips,
                "Pistol"), new Vector3(1020f, -120f));
            AnimatorState rifle = AddState(machine, "Assault Rifle", CreateWeaponTree(controller, clips,
                "AssaultRifle"), new Vector3(1020f, 20f));
            AnimatorState shotgun = AddState(machine, "Shotgun", CreateWeaponTree(controller, clips,
                "Shotgun"), new Vector3(1020f, 160f));
            machine.defaultState = locomotion;

            AddConditionTransition(locomotion, pickup, "Pickup", AnimatorConditionMode.If, 0f, false);
            AddConditionTransition(locomotion, carrying, "IsCarrying", AnimatorConditionMode.If, 0f, false);
            AddConditionTransition(locomotion, holding, "HoldingItem", AnimatorConditionMode.If, 0f, false);
            AddConditionTransition(locomotion, pointing, "Pointing", AnimatorConditionMode.If, 0f, false);
            AddConditionTransition(locomotion, jump, "Grounded", AnimatorConditionMode.IfNot, 0f, false);
            AddWeaponTransitions(locomotion, pistol, rifle, shotgun, carrying);
            // Recoil is applied procedurally by CouchGuyAnimationDriver from the
            // authoritative shot event.  It therefore layers over this directional
            // locomotion instead of restarting a full-body state on every rifle round.

            AddConditionTransition(pickup, carrying, "IsCarrying", AnimatorConditionMode.If, 0f, true, 0.72f);
            AddConditionTransition(pickup, locomotion, "IsCarrying", AnimatorConditionMode.IfNot, 0f, true, 0.72f);
            AddConditionTransition(carrying, drop, "Drop", AnimatorConditionMode.If, 0f, false);
            AddConditionTransition(carrying, locomotion, "IsCarrying", AnimatorConditionMode.IfNot, 0f, false);
            AddExitTransition(drop, locomotion, 0.85f);
            AddConditionTransition(holding, locomotion, "HoldingItem", AnimatorConditionMode.IfNot, 0f, false);
            AddConditionTransition(pointing, locomotion, "Pointing", AnimatorConditionMode.IfNot, 0f, false);
            AddConditionTransition(jump, locomotion, "Grounded", AnimatorConditionMode.If, 0f, false);

            AnimatorStateTransition hitTransition = machine.AddAnyStateTransition(hit);
            hitTransition.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            ConfigureTransition(hitTransition, false, 0f);
            AddConditionTransition(hit, carrying, "IsCarrying", AnimatorConditionMode.If, 0f, true, 0.82f);
            AddConditionTransition(hit, locomotion, "IsCarrying", AnimatorConditionMode.IfNot, 0f, true, 0.82f);
        }

        private static BlendTree CreateLocomotionTree(
            AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            BlendTree tree = new()
            {
                name = "Locomotion 2D",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(clips["Idle"], Vector2.zero);
            tree.AddChild(clips["HoverMove"], Vector2.up);
            tree.AddChild(clips["HoverMove_Backward"], Vector2.down);
            tree.AddChild(clips["Strafe_L"], Vector2.left);
            tree.AddChild(clips["Strafe_R"], Vector2.right);
            tree.AddChild(clips["Sprint"], new Vector2(0f, 1.65f));
            return tree;
        }

        private static BlendTree CreateCarryingTree(
            AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips)
        {
            BlendTree tree = new()
            {
                name = "Carrying 1D",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(clips["Carry_Idle"], 0f);
            tree.AddChild(clips["Carry_Move"], 1f);
            tree.AddChild(clips["Carry_Sprint"], 1.65f);
            return tree;
        }

        private static BlendTree CreateWeaponTree(AnimatorController controller,
            IReadOnlyDictionary<string, AnimationClip> clips, string prefix)
        {
            BlendTree tree = new()
            {
                name = $"{prefix} Directional Locomotion",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(clips[$"{prefix}_Idle"], Vector2.zero);
            tree.AddChild(clips[$"{prefix}_Walk"], Vector2.up);
            tree.AddChild(clips[$"{prefix}_Backward"], Vector2.down);
            tree.AddChild(clips[$"{prefix}_Strafe_L"], Vector2.left);
            tree.AddChild(clips[$"{prefix}_Strafe_R"], Vector2.right);
            tree.AddChild(clips[$"{prefix}_Sprint"], new Vector2(0f, 1.65f));
            return tree;
        }

        private static void AddWeaponTransitions(AnimatorState locomotion, AnimatorState pistol,
            AnimatorState rifle, AnimatorState shotgun, AnimatorState carrying)
        {
            AnimatorState[] states = { locomotion, pistol, rifle, shotgun };
            AnimatorState[] weapons = { pistol, rifle, shotgun };
            for (int fromIndex = 0; fromIndex < states.Length; fromIndex++)
            {
                AnimatorState from = states[fromIndex];
                for (int weaponIndex = 0; weaponIndex < weapons.Length; weaponIndex++)
                {
                    if (from == weapons[weaponIndex]) continue;
                    AddConditionTransition(from, weapons[weaponIndex], "EquippedWeapon",
                        AnimatorConditionMode.Equals, weaponIndex + 1, false);
                }
                if (from != locomotion)
                {
                    AddConditionTransition(from, locomotion, "EquippedWeapon", AnimatorConditionMode.Equals, 0f, false);
                    AddConditionTransition(from, carrying, "IsCarrying", AnimatorConditionMode.If, 0f, false);
                }
            }
        }

        private static AnimatorState AddState(
            AnimatorStateMachine machine,
            string name,
            Motion motion,
            Vector3 position)
        {
            AnimatorState state = machine.AddState(name, position);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static void AddConditionTransition(
            AnimatorState from,
            AnimatorState to,
            string parameter,
            AnimatorConditionMode mode,
            float threshold,
            bool hasExitTime,
            float exitTime = 0f)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.AddCondition(mode, threshold, parameter);
            ConfigureTransition(transition, hasExitTime, exitTime);
        }

        private static void AddExitTransition(AnimatorState from, AnimatorState to, float exitTime)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            ConfigureTransition(transition, true, exitTime);
        }

        private static void ConfigureTransition(
            AnimatorStateTransition transition,
            bool hasExitTime,
            float exitTime)
        {
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.hasFixedDuration = true;
            transition.duration = 0.16f;
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
