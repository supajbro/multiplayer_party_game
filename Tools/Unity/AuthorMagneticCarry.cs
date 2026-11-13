// Explicit, offline asset authoring only. Place in an isolated Unity project's Assets/Editor.
// Never runs on import/reload/play and never rebuilds the Player prefab.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using CouchGuys.Player;

public static class AuthorMagneticCarry
{
    const string Root = "Assets/_Project/";
    const string Folder = Root + "Animations/MagneticCarry";
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Build()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Root + "Prefabs/VFX");
            Directory.CreateDirectory(Root + "Materials/Player");
            AssetDatabase.Refresh();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/CouchGuy/CouchGuy_Redesigned.fbx");
            var clips = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(model)).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name.Split('|').Last());
            var idle = clips["Idle"];
            var carryIdle = CreateClip(model, idle, "Carry_Idle", 2f, 0);
            var carryMove = CreateClip(model, clips["HoverMove"], "Carry_Move", 1f, 1);
            var start = CreateClip(model, idle, "Carry_Start", .38f, 2);
            var release = CreateClip(model, idle, "Carry_Release", .32f, 3);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "Animations/CouchGuy.controller");
            var machine = controller.layers[0].stateMachine;
            var states = machine.states.Select(s => s.state).ToArray();
            var pickup = states.Single(s => s.name == "Pickup" || s.name == "Carry_Start");
            var drop = states.Single(s => s.name == "Drop" || s.name == "Carry_Release");
            var carrying = states.Single(s => s.name == "Carrying");
            pickup.name = "Carry_Start"; pickup.motion = start;
            drop.name = "Carry_Release"; drop.motion = release;
            var tree = (BlendTree)carrying.motion;
            var children = tree.children;
            for (int i = 0; i < children.Length; i++) children[i].motion = i == 0 ? carryIdle : carryMove;
            tree.children = children;
            foreach (var state in states.Where(s => s != pickup && s != carrying && s != drop))
                foreach (var transition in state.transitions)
                    if (transition.destinationState == carrying && transition.conditions.Any(c =>
                        c.parameter == "IsCarrying" && c.mode == AnimatorConditionMode.If))
                        transition.destinationState = pickup;
            // Mid-start release, late-join recovery and rapid reattachment all use the same synced bool.
            foreach (var transition in pickup.transitions)
            {
                bool exiting = transition.conditions.Any(c => c.parameter == "IsCarrying" && c.mode == AnimatorConditionMode.IfNot);
                if (exiting) { transition.destinationState = drop; transition.hasExitTime = false; }
                else { transition.hasExitTime = true; transition.exitTime = .9f; }
            }
            foreach (var transition in carrying.transitions)
                if (transition.conditions.Any(c => c.parameter == "IsCarrying" && c.mode == AnimatorConditionMode.IfNot))
                    transition.destinationState = drop;
            if (!drop.transitions.Any(t => t.destinationState == pickup))
            {
                var back = drop.AddTransition(pickup);
                back.AddCondition(AnimatorConditionMode.If, 0, "IsCarrying");
                back.duration = .1f; back.hasFixedDuration = true; back.hasExitTime = false;
                // Evaluate reattachment before the normal exit-time transition.
                drop.transitions = new[] { back }.Concat(drop.transitions.Where(t => t != back)).ToArray();
            }
            EditorUtility.SetDirty(controller);
            BuildEffect();
            AssetDatabase.SaveAssets();
            Validate(model, controller);
            File.WriteAllText("MagneticCarryValidation.txt", "PASS: four clips, existing rig bindings, preserved controller layers, independent prefab instances and authored VFX components.\n");
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        EditorApplication.Exit(0);
    }

    static AnimationClip CreateClip(GameObject model, AnimationClip source, string name, float length, int mode)
    {
        var instance = UnityEngine.Object.Instantiate(model);
        var rig = instance.GetComponentsInChildren<Transform>().Single(t => t.name == "CouchGuy_Rig");
        var bones = rig.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("UpperArm_") ||
            t.name.StartsWith("LowerArm_") || t.name == "Hand_L" || t.name == "Hand_R").ToArray();
        Require(bones.Length == 6, "Expected the existing six arm bones");
        source.SampleAnimation(instance, 0f);
        var neutral = bones.ToDictionary(b => b, b => b.localRotation);
        var restHand = bones.Where(b => b.name.StartsWith("Hand_")).ToDictionary(b => b, b => b.rotation);
        // Copy the existing hover, eye and body channels; replace only arm rotation curves.
        var output = UnityEngine.Object.Instantiate(source);
        output.name = name; output.frameRate = 30;
        foreach (var binding in AnimationUtility.GetCurveBindings(output))
        {
            var curve = AnimationUtility.GetEditorCurve(output, binding);
            var keys = curve.keys;
            for (int k = 0; k < keys.Length; k++)
            {
                keys[k].time *= length / source.length;
                keys[k].inTangent *= source.length / length;
                keys[k].outTangent *= source.length / length;
            }
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(output, binding, curve);
        }
        var curves = bones.ToDictionary(b => b, b => new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() });
        int frames = Mathf.CeilToInt(length * 30);
        for (int frame = 0; frame <= frames; frame++)
        {
            float t = frame / (float)frames, time = t * length;
            source.SampleAnimation(instance, t * source.length);
            foreach (var bone in bones) bone.localRotation = neutral[bone];
            float weight = mode == 2 ? Mathf.SmoothStep(0, 1, t) : mode == 3 ? 1f - Mathf.SmoothStep(0, 1, t) : 1f;
            foreach (string side in new[] { "L", "R" })
            {
                Transform upper = bones.Single(b => b.name == "UpperArm_" + side);
                Transform lower = bones.Single(b => b.name == "LowerArm_" + side);
                Transform hand = bones.Single(b => b.name == "Hand_" + side);
                float sign = Mathf.Sign(upper.position.x - rig.position.x);
                float wave = Mathf.Sin(time * Mathf.PI * (mode == 1 ? 2f : 1f));
                // Short forward reach, wide open palms and bent elbows leave an air gap at the couch.
                Vector3 desired = upper.position + new Vector3(sign * (.12f + .015f * wave), .025f + .025f * wave, .32f);
                Quaternion u = upper.localRotation, l = lower.localRotation, h = hand.localRotation;
                Solve(upper, lower, hand, desired);
                hand.rotation = Quaternion.AngleAxis(-68f + 3f * wave, Vector3.right) * restHand[hand];
                upper.localRotation = Quaternion.Slerp(u, upper.localRotation, weight);
                lower.localRotation = Quaternion.Slerp(l, lower.localRotation, weight);
                hand.localRotation = Quaternion.Slerp(h, hand.localRotation, weight);
            }
            foreach (var bone in bones)
            {
                Quaternion q = bone.localRotation;
                float[] values = { q.x, q.y, q.z, q.w };
                for (int component = 0; component < 4; component++) curves[bone][component].AddKey(time, values[component]);
            }
        }
        foreach (var bone in bones)
            for (int c = 0; c < 4; c++)
                output.SetCurve(AnimationUtility.CalculateTransformPath(bone, instance.transform), typeof(Transform),
                    "localRotation." + "xyzw"[c], curves[bone][c]);
        output.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(output);
        settings.startTime = 0; settings.stopTime = length; settings.loopTime = mode < 2; settings.loopBlend = mode < 2;
        AnimationUtility.SetAnimationClipSettings(output, settings);
        string path = Folder + "/" + name + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) { EditorUtility.CopySerialized(output, existing); UnityEngine.Object.DestroyImmediate(output); output = existing; }
        else AssetDatabase.CreateAsset(output, path);
        UnityEngine.Object.DestroyImmediate(instance);
        return output;
    }
    static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target)
    {
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
        Vector3 delta = target - upper.position;
        float d = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
        Vector3 f = delta.normalized, bend = Vector3.ProjectOnPlane(Vector3.down, f).normalized;
        float along = (a * a + d * d - b * b) / (2 * d);
        Vector3 elbow = upper.position + f * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
    }
    static void BuildEffect()
    {
        string materialPath = Root + "Materials/Player/MagneticAura.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(Shader.Find("CouchGuys/Magnetic Aura")); AssetDatabase.CreateAsset(material, materialPath); }
        var root = new GameObject("MagneticCarryAura");
        var effect = root.AddComponent<MagneticCarryEffect>();
        var left = Line(root, "Left Hand Stream", 14, false, material);
        var right = Line(root, "Right Hand Stream", 14, false, material);
        var pulse = Line(root, "Attachment Pulse", 24, true, material);
        var particles = Particles(root, "Flowing Sparks", material, 64, false);
        var burst = Particles(root, "Attach Burst", material, 12, true);
        var so = new SerializedObject(effect);
        so.FindProperty("m_streams").arraySize = 2;
        so.FindProperty("m_streams").GetArrayElementAtIndex(0).objectReferenceValue = left;
        so.FindProperty("m_streams").GetArrayElementAtIndex(1).objectReferenceValue = right;
        so.FindProperty("m_pulse").objectReferenceValue = pulse;
        so.FindProperty("m_particles").objectReferenceValue = particles;
        so.FindProperty("m_attachBurst").objectReferenceValue = burst;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, Root + "Prefabs/VFX/MagneticCarryAura.prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }
    static LineRenderer Line(GameObject root, string name, int count, bool loop, Material material)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material; line.positionCount = count; line.loop = loop; line.useWorldSpace = true;
        line.numCapVertices = 2; line.widthMultiplier = .025f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
        return line;
    }
    static ParticleSystem Particles(GameObject root, string name, Material material, int maximum, bool burst)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = !burst; main.duration = .3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = maximum;
        main.startLifetime = burst ? .3f : .5f; main.startSpeed = burst ? .6f : 0f;
        main.startSize = burst ? .065f : .035f;
        var emission = ps.emission; emission.enabled = burst; emission.rateOverTime = 0;
        if (burst) emission.SetBursts(new[] { new ParticleSystem.Burst(0, 10) });
        var shape = ps.shape; shape.enabled = burst; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .06f;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        return ps;
    }
    static void Validate(GameObject model, AnimatorController controller)
    {
        foreach (string name in new[] { "Carry_Start", "Carry_Idle", "Carry_Move", "Carry_Release" })
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + name + ".anim");
            Require(clip != null && clip.length > .1f, "Missing clip " + name);
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                Require(model.transform.Find(binding.path) != null || binding.path == "", "Broken rig binding " + binding.path);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/VFX/MagneticCarryAura.prefab");
        Require(prefab.GetComponentsInChildren<ParticleSystem>().Length == 2, "Particle systems missing");
        Require(prefab.GetComponentsInChildren<LineRenderer>().Length == 3, "Lines missing");
        var copies = new List<GameObject>();
        for (int i = 0; i < 4; i++) copies.Add(UnityEngine.Object.Instantiate(prefab));
        Require(copies.Select(x => x.GetComponent<MagneticCarryEffect>().GetInstanceID()).Distinct().Count() == 4, "Shared effect instance");
        foreach (var copy in copies) UnityEngine.Object.DestroyImmediate(copy);
        Require(controller.layers.Length >= 2 && controller.parameters.Any(p => p.name == "IsCarrying"), "Controller altered");
        Debug.Log("MAGNETIC_CARRY_VALIDATION_PASS");
    }
}
