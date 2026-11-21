// Explicit offline authoring in an isolated Unity project. No import/reload hooks.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class AuthorThiefEnemy01
{
    const string ModelPath = "Assets/_Project/Models/Thieves/thief_enemy_01.fbx";
    const string MaterialPath = "Assets/_Project/Materials/Thieves/ThiefEnemy01/thief_enemy_01.mat";
    const string AnimationFolder = "Assets/_Project/Animations/Thieves/ThiefEnemy01";
    const string PrefabPath = "Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab";
    static readonly string[] Variants = { "Pistol", "AssaultRifle", "Shotgun" };
    static readonly string[] Actions = { "Idle", "Walk", "Shoot", "Death", "Frustration" };
    static readonly int[] Budgets = { 10000, 4000, 1500, 700 };
    static string ReportFolder;
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static string ClipName(string name) => name.Substring(name.LastIndexOf('|') + 1);

    public static void Build()
    {
        ReportFolder = Argument("-thiefReport");
        Directory.CreateDirectory(ReportFolder);
        try
        {
            Require(!File.Exists(PrefabPath), "Refusing to overwrite the saved prefab. Preserve Inspector changes before explicitly authoring again.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            Require(importer != null, "Missing FBX importer");
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.motionNodeName = "Root";
            importer.optimizeGameObjects = false; // Saved renderers share the editable skeleton.
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.isReadable = true; // Only while validating; disabled for the delivered asset.
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            importer.animationRotationError = .25f;
            importer.animationPositionError = .1f;
            importer.animationScaleError = .1f;
            importer.SaveAndReimport();
            var definitions = importer.defaultClipAnimations;
            Require(definitions.Length == 15, "Expected all 15 Character01 armed-variant clips, got " + definitions.Length);
            foreach (var clip in definitions)
            {
                clip.name = ClipName(clip.name);
                clip.loopTime = clip.loopPose = clip.name.EndsWith("_Idle") || clip.name.EndsWith("_Walk");
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = definitions;
            importer.SaveAndReimport();
            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            foreach (string variant in Variants)
                foreach (string action in Actions)
                    Require(clips.ContainsKey(variant + "_Thief_" + action), "Missing " + variant + " " + action);
            var controllers = Variants.ToDictionary(v => v, v => BuildController(v, clips));
            Folder("Assets/_Project/Prefabs/Enemies");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "thief_enemy_01";
            try
            {
                var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
                Require(animator.avatar != null && animator.avatar.isValid, "Invalid Generic avatar");
                animator.runtimeAnimatorController = controllers["Pistol"];
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderBy(r => r.name.EndsWith("_LOD1") ? 1 : r.name.EndsWith("_LOD2") ? 2 : r.name.EndsWith("_LOD3") ? 3 : 0).ToArray();
                Require(renderers.Length == 4, "Expected four skinned LOD meshes");
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                Require(material && material.shader && material.shader.name == "Universal Render Pipeline/Lit", "Missing URP Lit material");
                Require(material.GetTexture("_BaseMap") && material.GetTexture("_BumpMap"), "Missing character textures");
                var report = new List<string>();
                for (int i = 0; i < 4; i++)
                {
                    var r = renderers[i];
                    Require(r.sharedMesh.triangles.Length / 3 <= Budgets[i], "LOD triangle budget exceeded");
                    Require(r.sharedMesh.subMeshCount == 1 && r.sharedMesh.uv.Length == r.sharedMesh.vertexCount, "Invalid LOD material/UV layout");
                    var weights = r.sharedMesh.boneWeights;
                    Require(weights.All(w => Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) < .002f), "Non-normalized skin weights");
                    Require(r.bones.All(b => b != null), "Missing skinning bone reference");
                    r.sharedMaterials = new[] { material };
                    r.quality = i < 2 ? SkinQuality.Bone4 : SkinQuality.Bone2;
                    r.updateWhenOffscreen = false;
                    r.skinnedMotionVectors = false;
                    r.shadowCastingMode = i < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    r.receiveShadows = true;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                    report.Add($"LOD{i}: {r.sharedMesh.triangles.Length / 3} triangles, {r.sharedMesh.vertexCount} imported vertices, {(i < 2 ? 4 : 2)} bone influences, one shared material.");
                }
                // Conservative per-renderer bounds cover every transferred clip; no per-frame bounds updates.
                var bounds = new Bounds[4];
                var initialized = new bool[4];
                var baked = new Mesh();
                foreach (var clip in clips.Values)
                {
                    for (int sample = 0; sample <= 12; sample++)
                    {
                        clip.SampleAnimation(root, clip.length * sample / 12f);
                        for (int i = 0; i < 4; i++)
                        {
                            renderers[i].BakeMesh(baked, true);
                            baked.RecalculateBounds();
                            Require(baked.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), "Non-finite skinned position");
                            if (!initialized[i]) { bounds[i] = baked.bounds; initialized[i] = true; }
                            else bounds[i].Encapsulate(baked.bounds);
                        }
                    }
                    Require(clip.length > .5f && clip.frameRate == 30f, "Invalid clip timing " + clip.name);
                    report.Add($"Animation: {clip.name}, {clip.length:F3}s, {clip.frameRate}fps, loop={clip.isLooping}, curves={AnimationUtility.GetCurveBindings(clip).Length}.");
                }
                Object.DestroyImmediate(baked);
                // Restore the imported T-pose after sampling clips, then carry the sampled bounds across.
                var rest = (GameObject)PrefabUtility.InstantiatePrefab(model);
                var sourceTransforms = rest.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, rest.transform));
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var original = sourceTransforms[AnimationUtility.CalculateTransformPath(t, root.transform)];
                    t.localPosition = original.localPosition; t.localRotation = original.localRotation; t.localScale = original.localScale;
                }
                Object.DestroyImmediate(rest);
                RestoreBindPose(renderers[0]);
                for (int i = 0; i < 4; i++)
                {
                    var scale = renderers[i].transform.lossyScale;
                    bounds[i].Expand(new Vector3(.1f / Mathf.Abs(scale.x), .1f / Mathf.Abs(scale.y), .1f / Mathf.Abs(scale.z)));
                    renderers[i].localBounds = bounds[i];
                }
                var group = root.AddComponent<LODGroup>();
                var thresholds = new[] { .35f, .16f, .07f, .025f };
                group.SetLODs(renderers.Select((r, i) => new LOD(thresholds[i], new Renderer[] { r })).ToArray());
                group.fadeMode = LODFadeMode.None; // No double rendering during crossfades.
                group.animateCrossFading = false;
                group.localReferencePoint = new Vector3(0, .825f, 0);
                group.size = 1.65f;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                importer.isReadable = false;
                importer.SaveAndReimport();
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var savedLODs = saved.GetComponent<LODGroup>().GetLODs();
                Require(savedLODs.Length == 4 && savedLODs.All(l => l.renderers.Length == 1 && l.renderers[0] != null), "Saved LOD references invalid");
                Require(saved.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => !r.sharedMesh.isReadable), "CPU mesh copy retained");
                Require(saved.GetComponent<Animator>().cullingMode == AnimatorCullingMode.CullCompletely, "Offscreen animation is not culled");
                report.Add("PASS: saved prefab has four exclusive LOD levels; common avatar/skeleton/material; animation-aware skin bounds; no CPU-readable meshes; offscreen animation culling enabled.");
                report.Add("LOD screen-height transitions: 0.35 / 0.16 / 0.07 / 0.025 (culled below 2.5%). LOD2/3 cast no shadows.");
                File.WriteAllLines(Path.Combine(ReportFolder, "UnityLODAnimationValidation.txt"), report);
                Debug.Log("THIEF_ENEMY_01_AUTHORING_PASS");
            }
            finally { Object.DestroyImmediate(root); }
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(ReportFolder, "UnityLODAnimationFailure.txt"), e.ToString());
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static AnimatorController BuildController(string variant, Dictionary<string, AnimationClip> clips)
    {
        Folder(AnimationFolder);
        string path = $"{AnimationFolder}/thief_enemy_01_{variant}.controller";
        Require(!File.Exists(path), "Refusing to overwrite existing controller: " + path);
        var c = AnimatorController.CreateAnimatorControllerAtPath(path);
        c.AddParameter("Speed", AnimatorControllerParameterType.Float);
        c.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
        c.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        c.AddParameter("Frustration", AnimatorControllerParameterType.Trigger);
        c.AddParameter(new AnimatorControllerParameter { name = "FrustrationSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f / clips[variant + "_Thief_Frustration"].length });
        var machine = c.layers[0].stateMachine;
        var states = new Dictionary<string, AnimatorState>();
        foreach (string action in Actions)
        {
            var state = machine.AddState(action);
            state.motion = clips[variant + "_Thief_" + action];
            state.writeDefaultValues = true;
            states[action] = state;
        }
        machine.defaultState = states["Idle"];
        states["Frustration"].speedParameter = "FrustrationSpeed";
        states["Frustration"].speedParameterActive = true;
        states["Frustration"].speed = clips[variant + "_Thief_Frustration"].length;
        var death = machine.AddAnyStateTransition(states["Death"]);
        Transition(death, false, .06f);death.AddCondition(AnimatorConditionMode.If, 0, "Dead");
        var shoot = machine.AddAnyStateTransition(states["Shoot"]);
        Transition(shoot, false, .04f);shoot.AddCondition(AnimatorConditionMode.If, 0, "Shoot");shoot.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
        var frustration = machine.AddAnyStateTransition(states["Frustration"]);
        Transition(frustration, false, .08f);frustration.AddCondition(AnimatorConditionMode.If, 0, "Frustration");frustration.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
        var walk = states["Idle"].AddTransition(states["Walk"]);
        Transition(walk, false, .1f);walk.AddCondition(AnimatorConditionMode.Greater, .08f, "Speed");
        var idle = states["Walk"].AddTransition(states["Idle"]);
        Transition(idle, false, .1f);idle.AddCondition(AnimatorConditionMode.Less, .08f, "Speed");
        foreach (string action in new[] { "Shoot", "Frustration" })
        {
            var toWalk = states[action].AddTransition(states["Walk"]);
            Transition(toWalk, true, .08f);toWalk.AddCondition(AnimatorConditionMode.Greater, .08f, "Speed");
            var toIdle = states[action].AddTransition(states["Idle"]);
            Transition(toIdle, true, .08f);toIdle.AddCondition(AnimatorConditionMode.Less, .08f, "Speed");
        }
        EditorUtility.SetDirty(c);
        return c;
    }
    static void Transition(AnimatorStateTransition t, bool exit, float duration)
    { t.hasExitTime = exit;t.exitTime = .98f;t.hasFixedDuration = true;t.duration = duration;t.canTransitionToSelf = false; }
    public static void RestoreBindPose(SkinnedMeshRenderer renderer)
    {
        var bind = renderer.sharedMesh.bindposes;
        var matrices = renderer.bones.Select((bone,i) => new { bone, world = renderer.transform.localToWorldMatrix * bind[i].inverse })
            .OrderBy(x => x.bone.GetComponentsInParent<Transform>(true).Length).ToArray();
        foreach (var item in matrices)
        {
            Matrix4x4 local = item.bone.parent != null ? item.bone.parent.worldToLocalMatrix * item.world : item.world;
            item.bone.SetLocalPositionAndRotation(local.GetColumn(3), local.rotation);
            item.bone.localScale = local.lossyScale;
        }
    }
    static void Folder(string path)
    {
        string current = "Assets";
        foreach (string part in path.Split('/').Skip(1))
        { string next = current + "/" + part;if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, part);current = next; }
    }
    static string Argument(string name)
    { var args = Environment.GetCommandLineArgs();int i = Array.IndexOf(args, name);if (i < 0 || i + 1 >= args.Length) throw new ArgumentException(name);return args[i + 1]; }
}
