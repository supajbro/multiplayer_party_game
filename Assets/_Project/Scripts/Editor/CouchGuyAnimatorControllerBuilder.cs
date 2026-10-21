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
            "Pistol_Idle", "Pistol_Walk", "Pistol_Backward", "Pistol_Strafe_L", "Pistol_Strafe_R", "Pistol_Run", "Pistol_Sprint",
            "AssaultRifle_Idle", "AssaultRifle_Walk", "AssaultRifle_Backward", "AssaultRifle_Strafe_L", "AssaultRifle_Strafe_R", "AssaultRifle_Run", "AssaultRifle_Sprint",
            "Shotgun_Idle", "Shotgun_Walk", "Shotgun_Backward", "Shotgun_Strafe_L", "Shotgun_Strafe_R", "Shotgun_Run", "Shotgun_Sprint"
            , "Pistol_Raise", "AssaultRifle_Raise", "Shotgun_Raise"
        };

        [InitializeOnLoadMethod]
        private static void InstallMissingAnimationSetupAfterReload()
        {
            EditorApplication.playModeStateChanged -= UpgradeWeaponHoldingAfterPlayMode;
            EditorApplication.playModeStateChanged += UpgradeWeaponHoldingAfterPlayMode;
            if (s_installScheduled || AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                return;
            }

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            AnimatorController existingController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            bool controllerMissing = existingController == null;
            bool controllerNeedsDirectionalUpgrade = existingController != null &&
                (!ContainsBlendTree(existingController, "Pistol Carry and Aim") || !HasSprintRaiseWaypoints());
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

                if (controllerMissing || modelMissingFromPlayer) BuildAndInstall();
                else UpgradeWeaponHolding();
            };
        }

        private static void UpgradeWeaponHoldingAfterPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += UpgradeWeaponHolding;
        }

        private static bool HasSprintRaiseWaypoints()
        {
            HashSet<string> missing = new() { "Pistol_Raise", "AssaultRifle_Raise", "Shotgun_Raise" };
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            {
                if (asset is not BlendTree tree || !tree.name.EndsWith(" Carry Speed", StringComparison.Ordinal)) continue;
                foreach (ChildMotion child in tree.children)
                    if (child.motion is AnimationClip clip) missing.Remove(NormaliseClipName(clip.name));
            }
            return missing.Count == 0;
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

        [MenuItem("Couch Guys/Animation/Update Existing Weapon Holding")]
        public static void UpgradeWeaponHolding()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) return;
            ConfigureClipLooping();
            Dictionary<string, AnimationClip> clips = LoadClips();
            // A script reload can precede the FBX refresh. The model postprocessor
            // retries after import, once the new actions are actually available.
            if (!clips.ContainsKey("Pistol_Run") || !clips.ContainsKey("AssaultRifle_Run") ||
                !clips.ContainsKey("Shotgun_Run") || !clips.ContainsKey("Pistol_Raise") ||
                !clips.ContainsKey("AssaultRifle_Raise") || !clips.ContainsKey("Shotgun_Raise")) return;
            ValidateRequiredClips(clips);
            bool hasAim = false;
            foreach (AnimatorControllerParameter parameter in controller.parameters)
                hasAim |= parameter.name == "WeaponAim";
            if (!hasAim) controller.AddParameter("WeaponAim", AnimatorControllerParameterType.Float);
            string[] stateNames = { "Pistol", "Assault Rifle", "Shotgun" };
            string[] prefixes = { "Pistol", "AssaultRifle", "Shotgun" };
            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                int index = Array.IndexOf(stateNames, child.state.name);
                if (index < 0) continue;
                if (child.state.motion is BlendTree oldTree && oldTree.name == $"{prefixes[index]} Carry and Aim")
                {
                    // Upgrade the existing tree in place: normal walking is 3.5 m/s
                    // (Speed = 1), not the partial-stick threshold of 0.45.
                    if (oldTree.children[0].motion is BlendTree speedTree)
                    {
                        List<ChildMotion> motions = new(speedTree.children);
                        ChildMotion walk = motions[1];
                        walk.threshold = 1f;
                        motions[1] = walk;
                        ChildMotion run = motions[2];
                        run.threshold = 1.3f;
                        motions[2] = run;
                        AnimationClip raiseClip = clips[$"{prefixes[index]}_Raise"];
                        if (!motions.Exists(motion => motion.motion == raiseClip))
                            motions.Insert(3, new ChildMotion { motion = raiseClip, threshold = 1.48f, timeScale = 1f });
                        speedTree.children = motions.ToArray();
                        EditorUtility.SetDirty(speedTree);
                    }
                    continue;
                }
                Motion oldMotion = child.state.motion;
                child.state.motion = CreateWeaponTree(controller, clips, prefixes[index]);
                // Only the three previous weapon trees are replaced; other states,
                // transitions, prefab references and the controller GUID survive.
                if (oldMotion is BlendTree) UnityEngine.Object.DestroyImmediate(oldMotion, true);
                EditorUtility.SetDirty(child.state);
            }
            EditorUtility.SetDirty(controller);
            RefreshWeaponMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("Updated CouchGuy weapon carry/aim trees in place; reused replicated movement and shot events.");
        }

        private static void RefreshWeaponMaterials()
        {
            Shader shader = Shader.Find(ToonMaterialWorkflow.ShaderName);
            if (shader == null) throw new UnityException("Couch Guys Toon Lit shader is unavailable.");
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool changed = false;
            Dictionary<Material, Material> materials = new();
            try
            {
                foreach (Transform weapon in player.GetComponentsInChildren<Transform>(true))
                {
                    if (weapon.name != "Weapon_Pistol" && weapon.name != "Weapon_AssaultRifle" &&
                        weapon.name != "Weapon_Shotgun") continue;
                    foreach (Renderer renderer in weapon.GetComponentsInChildren<Renderer>(true))
                    {
                        Renderer source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                        if (source == null) continue;
                        Material[] slots = source.sharedMaterials;
                        for (int index = 0; index < slots.Length; index++)
                        {
                            Material original = slots[index];
                            if (original == null || !original.name.StartsWith("CG_Weapon_", StringComparison.Ordinal))
                                continue;
                            if (!materials.TryGetValue(original, out Material toon))
                            {
                                string path = $"Assets/_Project/Prefabs/Weapons/Materials/{original.name}.mat";
                                toon = AssetDatabase.LoadAssetAtPath<Material>(path);
                                if (toon == null)
                                {
                                    toon = new Material(shader) { name = original.name };
                                    AssetDatabase.CreateAsset(toon, path);
                                }
                                Color color = original.HasProperty("_BaseColor")
                                    ? original.GetColor("_BaseColor") : original.color;
                                toon.SetColor("_BaseColor", color);
                                toon.SetColor("_ShadowColor", new Color(0.4f, 0.48f, 0.62f));
                                toon.SetFloat("_ShadowStrength", 0.5f);
                                toon.SetFloat("_SpecularEnabled", 1f);
                                toon.SetFloat("_SpecularSize", 0.14f);
                                toon.SetFloat("_RimIntensity", 0.12f);
                                toon.SetColor("_EmissionColor", original.name.Contains("YellowLight")
                                    ? color * 1.8f : Color.black);
                                EditorUtility.SetDirty(toon);
                                materials.Add(original, toon);
                            }
                            slots[index] = toon;
                        }
                        Material[] current = renderer.sharedMaterials;
                        bool same = current.Length == slots.Length;
                        for (int index = 0; same && index < slots.Length; index++)
                            same &= current[index] == slots[index];
                        if (same) continue;
                        renderer.sharedMaterials = slots;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                        changed = true;
                    }
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }
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
            Dictionary<string, ModelImporterClipAnimation> configuredClips = new(StringComparer.Ordinal);
            if (configuredAnimations != null)
            {
                foreach (ModelImporterClipAnimation configured in configuredAnimations)
                    configuredClips[NormaliseClipName(configured.name)] = configured;
            }

            if (animations == null || animations.Length == 0)
                throw new UnityException("The Couch Guy FBX contains no animation takes.");

            if (configuredClips.Count != animations.Length)
            {
                changed = true;
            }

            for (int index = 0; index < animations.Length; index++)
            {
                ModelImporterClipAnimation source = animations[index];
                string clipName = NormaliseClipName(source.name);
                if (!configuredClips.TryGetValue(clipName, out ModelImporterClipAnimation animation))
                {
                    animation = source;
                    changed = true;
                }
                // Preserve existing clip IDs/events and compare actual configured
                // loops, rather than the always-unlooped default FBX take settings.
                // This makes the import-triggered weapon upgrade idempotent.
                if (animation.firstFrame != source.firstFrame || animation.lastFrame != source.lastFrame)
                {
                    animation.firstFrame = source.firstFrame;
                    animation.lastFrame = source.lastFrame;
                    changed = true;
                }
                bool shouldLoop = LoopingClips.Contains(clipName);
                if (animation.loopTime != shouldLoop || animation.loopPose != shouldLoop)
                {
                    animation.loopTime = shouldLoop;
                    animation.loopPose = shouldLoop;
                    changed = true;
                }
                animations[index] = animation;
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
                , "Pistol_Run", "AssaultRifle_Run", "Shotgun_Run"
                , "Pistol_Raise", "AssaultRifle_Raise", "Shotgun_Raise"
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
            controller.AddParameter("WeaponAim", AnimatorControllerParameterType.Float);
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
            tree.AddChild(clips[$"{prefix}_Walk"], Vector2.zero);
            tree.AddChild(clips[$"{prefix}_Walk"], Vector2.up);
            tree.AddChild(clips[$"{prefix}_Backward"], Vector2.down);
            tree.AddChild(clips[$"{prefix}_Strafe_L"], Vector2.left);
            tree.AddChild(clips[$"{prefix}_Strafe_R"], Vector2.right);
            // Speed, rather than just forward velocity, chooses the carry tier so
            // strafing/backpedalling sprint poses behave identically on observers.
            BlendTree carry = new()
            {
                name = $"{prefix} Carry Speed",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(carry, controller);
            carry.AddChild(clips[$"{prefix}_Idle"], 0f);
            carry.AddChild(tree, 1f);
            carry.AddChild(clips[$"{prefix}_Run"], 1.3f);
            carry.AddChild(clips[$"{prefix}_Raise"], 1.48f);
            carry.AddChild(clips[$"{prefix}_Sprint"], 1.65f);
            BlendTree aim = new()
            {
                name = $"{prefix} Carry and Aim",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "WeaponAim",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(aim, controller);
            aim.AddChild(carry, 0f);
            aim.AddChild(clips[$"{prefix}_Idle"], 1f);
            return aim;
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

    // Importing the edited existing FBX also upgrades its weapon motions without
    // reinstalling the robot prefab or touching any camera/controller settings.
    internal sealed class CouchGuyWeaponAnimationImport : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            if (Array.IndexOf(imported, CouchGuyAnimatorControllerBuilder.ModelPath) >= 0)
                EditorApplication.delayCall += CouchGuyAnimatorControllerBuilder.UpgradeWeaponHolding;
        }
    }
}
