using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Player;
using CouchGuys.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, one-time asset authoring. Reload/import never rewrites assets.
    /// A request file is an automation equivalent of invoking the menu command.</summary>
    [InitializeOnLoad]
    public static class PlayerEmoteAuthoring
    {
        private const string Folder = "Assets/_Project/Animations/Emotes";
        private const string Request = "Temp/PlayerEmoteAuthoring.request";
        private static readonly string[] Names = { "Silly Dance", "Backflip", "Finger Guns", "Moonwalk Shuffle", "Spin", "Celebration Wiggle" };
        static PlayerEmoteAuthoring()
        {
            EditorApplication.update += () =>
            {
                if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                    EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(Request);
                try { CreateMissingAssets(); }
                catch (Exception ex) { File.WriteAllText("Temp/PlayerEmoteAuthoring.txt", ex.ToString()); Debug.LogException(ex); }
            };
        }

        [MenuItem("Couch Guys/Player/Create Missing Emote Assets")]
        public static void CreateMissingAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            GameObject player = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Players/Player.prefab");
            try
            {
                var animator = player.GetComponent<CouchGuyAnimationDriver>().Animator;
                var rig = animator.GetComponentsInChildren<Transform>(true).First(t => t.name == "CouchGuy_Rig");
                var boneNames = new HashSet<string> { "CouchGuy_Rig", "Root", "Torso", "Head", "Eye_L", "Eye_R",
                    "UpperArm_L", "LowerArm_L", "Hand_L", "UpperArm_R", "LowerArm_R", "Hand_R" };
                Transform[] bones = rig.GetComponentsInChildren<Transform>(true).Where(t => boneNames.Contains(t.name)).ToArray();
                var originalPoses = bones.ToDictionary(b => b, b => (b.localPosition, b.localRotation));
                var idle = animator.runtimeAnimatorController.animationClips.First(c => c.name.EndsWith("Idle", StringComparison.Ordinal));
                idle.SampleAnimation(animator.gameObject, 0);
                var poses = bones.ToDictionary(b => b, b => (b.localPosition, b.localRotation));
                EmoteDefinition[] definitions = new EmoteDefinition[6];
                for (int i = 0; i < 6; i++)
                {
                    string path = Folder + "/" + Names[i].Replace(" ", "") + ".asset";
                    definitions[i] = AssetDatabase.LoadAssetAtPath<EmoteDefinition>(path);
                    // An explicit repair marker updates only our newly authored clips, retaining their GUIDs.
                    bool repair = File.Exists("Temp/PlayerEmoteClips.repair");
                    if (definitions[i] != null && !repair) continue;
                    float duration = i == 1 ? 1.5f : i == 2 ? 1.8f : i == 4 ? 1.35f : 2f;
                    bool loop = i == 0 || i == 3 || i == 5;
                    var clip = new AnimationClip { name = Names[i], frameRate = 30, wrapMode = loop ? WrapMode.Loop : WrapMode.Once };
                    var curves = bones.ToDictionary(b => b, b => new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() });
                    int frames = Mathf.RoundToInt(duration * 30);
                    for (int f = 0; f <= frames; f++)
                    {
                        foreach (var b in bones) b.SetLocalPositionAndRotation(poses[b].Item1, poses[b].Item2);
                        float u = f / (float)frames;
                        Pose(bones, i, u);
                        foreach (var b in bones)
                        {
                            Quaternion q = b.localRotation;
                            float[] values = { q.x, q.y, q.z, q.w };
                            for (int axis = 0; axis < 4; axis++) curves[b][axis].AddKey(u * duration, values[axis]);
                        }
                    }
                    string[] axes = { "x", "y", "z", "w" };
                    foreach (var b in bones)
                    {
                        string bonePath = AnimationUtility.CalculateTransformPath(b, animator.transform);
                        for (int axis = 0; axis < 4; axis++)
                            clip.SetCurve(bonePath, typeof(Transform), "m_LocalRotation." + axes[axis], curves[b][axis]);
                    }
                    clip.EnsureQuaternionContinuity();
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = loop;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    string clipPath = Folder + "/" + Names[i].Replace(" ", "") + ".anim";
                    AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (existingClip != null) { EditorUtility.CopySerialized(clip, existingClip); Object.DestroyImmediate(clip); clip = existingClip; }
                    else AssetDatabase.CreateAsset(clip, clipPath);
                    var definition = definitions[i] != null ? definitions[i] : ScriptableObject.CreateInstance<EmoteDefinition>();
                    definition.Id = (byte)(i + 1);
                    definition.DisplayName = Names[i] == "Moonwalk Shuffle" ? "Moonwalk / Shuffle" : Names[i];
                    definition.PreviewLabel = new[] { "ARM FLAILS", "FULL BACKFLIP", "DOUBLE POINT", "HOVER GLIDE", "360 SPIN", "FIST PUMPS" }[i];
                    definition.Loop = loop;
                    definition.Clip = clip;
                    definition.Expression = i == 2 ? RobotFaceExpression.Focused : RobotFaceExpression.Happy;
                    definition.HoverX = Motion(duration, u => i == 3 ? .24f * Mathf.Sin(u * Mathf.PI * 2) : 0);
                    definition.HoverY = Motion(duration, u => i == 1 ? .5f * Mathf.Sin(u * Mathf.PI) :
                        i == 4 ? .12f * Mathf.Sin(u * Mathf.PI) : .075f * (1 - Mathf.Cos(u * Mathf.PI * (i == 5 ? 8 : 4))));
                    definition.HoverZ = Motion(duration, u => i == 3 ? -.17f * Mathf.Sin(u * Mathf.PI * 2) : 0);
                    definition.Pitch = Motion(duration, u => i == 1 ? -360f * Smooth(u) : i == 0 ? 8f * Mathf.Sin(u * Mathf.PI * 4) : 0);
                    definition.Yaw = Motion(duration, u => i == 4 ? 360f * Smooth(u) : i == 3 ? 16f * Mathf.Sin(u * Mathf.PI * 2) : 0);
                    definition.Roll = Motion(duration, u => i == 0 ? 16f * Mathf.Sin(u * Mathf.PI * 2) :
                        i == 5 ? 12f * Mathf.Sin(u * Mathf.PI * 8) : i == 2 ? -8f * Mathf.Sin(u * Mathf.PI) : 0);
                    if (definitions[i] == null) AssetDatabase.CreateAsset(definition, path);
                    EditorUtility.SetDirty(clip); EditorUtility.SetDirty(definition);
                    definitions[i] = definition;
                }
                foreach (var b in bones) b.SetLocalPositionAndRotation(originalPoses[b].Item1, originalPoses[b].Item2);
                var playback = player.GetComponent<PlayerEmotePlayback>() ?? player.AddComponent<PlayerEmotePlayback>();
                Set(playback, "m_animator", animator);
                Set(playback, "m_visualRoot", player.transform.Find("Visual"));
                SetArray(playback, "m_bones", bones);
                var controller = player.GetComponent<PlayerEmoteController>() ?? player.AddComponent<PlayerEmoteController>();
                Set(controller, "m_playback", playback);
                SetArray(controller, "m_definitions", definitions);
                var wheel = AssetDatabase.LoadAssetAtPath<EmoteWheelView>("Assets/_Project/Prefabs/UI/EmoteWheel.prefab");
                if (wheel == null) wheel = BuildWheel();
                else
                {
                    GameObject contents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/UI/EmoteWheel.prefab");
                    try
                    {
                        bool changed = false;
                        foreach (var sector in contents.GetComponentsInChildren<EmoteWheelSector>(true))
                            if (sector.GetComponent<CanvasRenderer>() == null) { sector.gameObject.AddComponent<CanvasRenderer>(); changed = true; }
                        if (changed) PrefabUtility.SaveAsPrefabAsset(contents, "Assets/_Project/Prefabs/UI/EmoteWheel.prefab");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
                Set(controller, "m_wheelPrefab", wheel);
                var networkObject = player.GetComponent<FishNet.Object.NetworkObject>();
                var networkSerialized = new SerializedObject(networkObject);
                var behaviours = networkSerialized.FindProperty("NetworkBehaviours");
                int componentIndex = -1;
                for (int i = 0; i < behaviours.arraySize; i++)
                    if (behaviours.GetArrayElementAtIndex(i).objectReferenceValue == controller) componentIndex = i;
                if (componentIndex < 0)
                {
                    componentIndex = behaviours.arraySize; behaviours.arraySize++;
                    behaviours.GetArrayElementAtIndex(componentIndex).objectReferenceValue = controller;
                    networkSerialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var networkBehaviour = new SerializedObject(controller);
                networkBehaviour.FindProperty("_componentIndexCache").intValue = componentIndex;
                networkBehaviour.FindProperty("_addedNetworkObject").objectReferenceValue = networkObject;
                networkBehaviour.FindProperty("_networkObjectCache").objectReferenceValue = networkObject;
                networkBehaviour.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, "Assets/_Project/Prefabs/Players/Player.prefab");
                AssetDatabase.SaveAssets();
                if (File.Exists("Temp/PlayerEmoteClips.repair")) File.Delete("Temp/PlayerEmoteClips.repair");
                File.WriteAllText("Temp/PlayerEmoteAuthoring.txt", "PASS: 6 saved clips/definitions, wheel and player bindings.\nRig: " +
                    string.Join(", ", bones.Select(b => b.name)));
                Debug.Log("Created missing emote assets and wired player prefab. Existing emote assets are preserved.");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        private static float Smooth(float u) => u * u * u * (u * (u * 6 - 15) + 10);
        private static AnimationCurve Motion(float duration, Func<float, float> evaluate)
        {
            var curve = new AnimationCurve();
            for (int f = 0; f <= 60; f++) curve.AddKey(duration * f / 60f, evaluate(f / 60f));
            for (int k = 0; k < curve.length; k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
            }
            return curve;
        }

        private static void Pose(Transform[] bones, int emote, float u)
        {
            Transform Find(string name) => bones.FirstOrDefault(b => b.name == name);
            void Rotate(string name, Vector3 angle)
            {
                var bone = Find(name);
                if (bone != null) bone.localRotation *= Quaternion.Euler(angle);
            }
            float wave = Mathf.Sin(u * Mathf.PI * 4);
            float envelope = Mathf.Sin(u * Mathf.PI);
            Rotate("Torso", new Vector3(emote == 3 ? 8 * wave : 0, emote == 0 ? 12 * wave : 0, emote == 5 ? 6 * wave : 0));
            Rotate("Head", new Vector3(emote == 3 ? -8 * wave : emote == 2 ? -8 * envelope : 5 * wave, 8 * wave, 0));
            foreach (bool left in new[] { true, false })
            {
                string side = left ? "L" : "R";
                var upper = Find("UpperArm_" + side);
                var lower = Find("LowerArm_" + side);
                var hand = Find("Hand_" + side);
                if (upper == null || lower == null || hand == null) throw new InvalidOperationException("Robot arm bones missing.");
                // Infer outward from the actual saved shoulder positions, independent of FBX naming conventions.
                float sign = Mathf.Sign(Vector3.Dot(upper.position - Find("Torso").position, upper.root.right));
                Vector3 outward = animatorDirection(upper, sign);
                Vector3 forward = upper.root.forward;
                Vector3 up = upper.root.up;
                Vector3 upperDirection, foreDirection;
                switch (emote)
                {
                    case 0:
                        upperDirection = outward * .8f + up * (.35f + Mathf.Sin(u * Mathf.PI * 4 + (left ? 0 : Mathf.PI)) * .9f);
                        foreDirection = forward * .5f + up * (1f + .3f * wave); break;
                    case 1:
                        upperDirection = outward * .4f - up * .6f + forward * .4f;
                        foreDirection = up * .7f + forward * .5f; break;
                    case 2:
                        upperDirection = outward * .4f - up * .2f + forward;
                        foreDirection = forward + up * (.05f + .2f * Mathf.Sin(u * Mathf.PI * 6)); break;
                    case 3:
                        upperDirection = outward * .5f - up * .5f + forward * (left ? wave : -wave) * .6f;
                        foreDirection = forward * .8f + up * .4f; break;
                    case 4:
                        upperDirection = outward + up * .15f;
                        foreDirection = outward + forward * .2f; break;
                    default:
                        upperDirection = outward * .5f + up * .5f;
                        foreDirection = up + forward * (.3f + .4f * Mathf.Sin(u * Mathf.PI * 8 + (left ? 0 : Mathf.PI))); break;
                }
                upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, upperDirection.normalized) * upper.rotation;
                lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, foreDirection.normalized) * lower.rotation;
                Rotate("Hand_" + side, new Vector3(0, 0, 10 * wave));
            }
        }

        private static Vector3 animatorDirection(Transform bone, float sign) => bone.root.right * sign;

        private static EmoteWheelView BuildWheel()
        {
            var root = new GameObject("EmoteWheel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 80;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                var view = root.AddComponent<EmoteWheelView>();
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/Barlow_Semi_Condensed/BarlowSemiCondensed-Regular SDF.asset");
                RectTransform Panel(string name, Transform parent, Vector2 size, Vector2 position)
                {
                    var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                    rect.SetParent(parent, false); rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
                }
                TMP_Text Label(string name, Transform parent, Vector2 size, Vector2 position, string value, float fontSize)
                {
                    var rect = Panel(name, parent, size, position);
                    var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
                    text.font = font; text.text = value; text.fontSize = fontSize;
                    text.fontStyle = FontStyles.Bold; text.alignment = TextAlignmentOptions.Center;
                    text.raycastTarget = false; return text;
                }
                var shade = Panel("Backdrop", root.transform, Vector2.zero, Vector2.zero);
                shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one;
                var image = shade.gameObject.AddComponent<Image>(); image.color = new Color(.015f, .025f, .05f, .55f); image.raycastTarget = false;
                var wheel = Panel("Wheel", root.transform, new Vector2(620, 620), Vector2.zero);
                var segments = new EmoteWheelSector[6]; var labels = new TMP_Text[6];
                for (int i = 0; i < 6; i++)
                {
                    var rect = Panel("Segment " + (i + 1), wheel, new Vector2(620, 620), Vector2.zero);
                    segments[i] = rect.gameObject.AddComponent<EmoteWheelSector>();
                    segments[i].Segment = i; segments[i].raycastTarget = false;
                    float a = i * Mathf.PI / 3;
                    labels[i] = Label("Label " + (i + 1), wheel, new Vector2(175, 110),
                        new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 226, Names[i], 23);
                }
                var title = Label("Selected Name", wheel, new Vector2(250, 75), new Vector2(0, 28), "EMOTES", 29);
                title.color = new Color(1, .66f, .08f);
                var preview = Label("Preview Label", wheel, new Vector2(245, 80), new Vector2(0, -45), "Point to select", 21);
                Label("Header", root.transform, new Vector2(700, 70), new Vector2(0, 390), "COUCH GUYS  /  EMOTES", 44);
                Label("Instructions", root.transform, new Vector2(900, 60), new Vector2(0, -380), "HOLD B  •  POINT TO SELECT  •  RELEASE TO PLAY\nCentre / outside to cancel", 24);
                Set(view, "m_wheel", wheel); SetArray(view, "m_segments", segments); SetArray(view, "m_labels", labels);
                Set(view, "m_name", title); Set(view, "m_preview", preview);
                return PrefabUtility.SaveAsPrefabAsset(root, "Assets/_Project/Prefabs/UI/EmoteWheel.prefab").GetComponent<EmoteWheelView>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void Set(Object target, string name, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(name).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetArray<T>(Object target, string name, T[] values) where T : Object
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(name); property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
