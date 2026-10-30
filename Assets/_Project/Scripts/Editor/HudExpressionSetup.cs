using System;
using System.Linq;
using CouchGuys.Gameplay.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, one-time migration. Never runs on import or script reload.</summary>
    public static class HudExpressionSetup
    {
        private const string Sheet = "Assets/_Project/UI/Expressions/happy_expressions.png";
        private const string Status = "Assets/_Project/Prefabs/UI/PlayerStatusBar.prefab";
        private const string Hud = "Assets/_Project/Prefabs/UI/PlayerWeaponHud.prefab";
        private const string Player = "Assets/_Project/Prefabs/Player.prefab";

        [MenuItem("Tools/HUD/Apply Happy Expressions %&h")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var importer = (TextureImporter)AssetImporter.GetAtPath(Sheet);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            var slices = new SpriteMetaData[12];
            for (int i = 0; i < slices.Length; i++)
                slices[i] = new SpriteMetaData
                {
                    name = $"happy_{i:00}", alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    rect = new Rect((i % 4) * (width / 4), (2 - i / 4) * (height / 3), width / 4, height / 3)
                };
#pragma warning disable CS0618
            importer.spritesheet = slices;
#pragma warning restore CS0618
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            Sprite[] frames = AssetDatabase.LoadAllAssetsAtPath(Sheet).OfType<Sprite>()
                .OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            if (frames.Length != 12) throw new InvalidOperationException("Expected 12 happy frames.");

            Edit(Status, root =>
            {
                var bar = root.GetComponent<PlayerStatusBar>();
                var portrait = root.transform.Find("BadgeMask/Portrait");
                var raw = portrait.GetComponent<RawImage>();
                if (raw != null) UnityEngine.Object.DestroyImmediate(raw);
                var image = portrait.GetComponent<Image>();
                if (image == null) image = portrait.gameObject.AddComponent<Image>();
                image.sprite = frames[0];
                image.preserveAspect = true;
                image.raycastTarget = false;
                var data = new SerializedObject(bar);
                data.FindProperty("m_expression").objectReferenceValue = image;
                var happy = data.FindProperty("m_happyFrames");
                happy.arraySize = frames.Length;
                for (int i = 0; i < frames.Length; i++) happy.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
                data.ApplyModifiedPropertiesWithoutUndo();
            });
            Edit(Hud, root =>
            {
                var view = root.GetComponent<PlayerWeaponHudView>();
                var faceRect = (RectTransform)view.m_healthBar.transform.Find("BadgeMask/Portrait");
                faceRect.anchorMin = new Vector2(0.32f, 0.34f);
                faceRect.anchorMax = new Vector2(0.70f, 0.68f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(faceRect);
                // The stamina variant uses the same status prefab for its lightning badge.
                view.m_staminaBar.transform.Find("BadgeMask/Portrait").gameObject.SetActive(false);
            });
            Edit(Player, root =>
            {
                var camera = root.transform.Find("PortraitCamera");
                if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            });
            Validate();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(Hud);
            AssetDatabase.OpenAsset(Selection.activeObject);
        }

        private static void Edit(string path, Action<GameObject> edit)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/HUD/Validate Happy Expressions %&j")]
        public static void Validate()
        {
            var view = AssetDatabase.LoadAssetAtPath<GameObject>(Hud).GetComponent<PlayerWeaponHudView>();
            var bar = view.m_healthBar;
            var image = bar.transform.Find("BadgeMask/Portrait").GetComponent<Image>();
            var data = new SerializedObject(bar);
            if (image == null || !image.preserveAspect || data.FindProperty("m_expression").objectReferenceValue != image ||
                data.FindProperty("m_happyFrames").arraySize != 12 || data.FindProperty("m_idleFrame").intValue != 0 ||
                bar.GetComponentInChildren<RawImage>()?.texture is RenderTexture ||
                AssetDatabase.LoadAssetAtPath<GameObject>(Player).transform.Find("PortraitCamera") != null)
                throw new InvalidOperationException("HUD expression validation failed.");
            for (int i = 0; i < 12; i++)
            {
                var sprite = (Sprite)data.FindProperty("m_happyFrames").GetArrayElementAtIndex(i).objectReferenceValue;
                if (sprite == null || sprite.name != $"happy_{i:00}" || sprite.rect.size != new Vector2(362, 362) ||
                    sprite.pivot != new Vector2(181, 181))
                    throw new InvalidOperationException($"Invalid expression frame {i}.");
            }
            VerifyPlaybackAndPreview();
            System.IO.File.WriteAllText("Library/HudExpressionValidation.txt", "PASS: idle smile, blink and big smile sequences, non-interruption, happy fallback and independent instances; 1920x1080 and 1280x720 renders saved.");
            Debug.Log("HUD happy expressions applied and validated.");
        }

        private static void VerifyPlaybackAndPreview()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(PlayerStatusBar);
            var start = type.GetField("m_animationStartedAt", flags);
            var animation = type.GetField("m_animation", flags);
            var next = type.GetField("m_nextFrameAt", flags);
            var update = type.GetMethod("UpdateExpression", flags);
            RenderTexture previous = RenderTexture.active;
            try
            {
                var canvasObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Hud), scene);
                var view = canvasObject.GetComponent<PlayerWeaponHudView>();
                var bar = view.m_healthBar;
                var image = bar.transform.Find("BadgeMask/Portrait").GetComponent<Image>();
                var happy = (Sprite[])type.GetField("m_happyFrames", flags).GetValue(bar);
                foreach (float health in new[] { 1f, 0.6f, 0.59f, 0.3f, 0.29f, 0f })
                {
                    bar.SetHealthExpression(health);
                    if (type.GetField("m_activeFrames", flags).GetValue(bar) != happy)
                        throw new InvalidOperationException("Happy fallback failed.");
                }
                type.GetMethod("OnEnable", flags).Invoke(bar, null);
                if (image.sprite != happy[0]) throw new InvalidOperationException("Idle smile failed.");
                foreach (var gesture in new[] { (name: "Blink", frames: new[] { 8, 10, 8 }, duration: 0.2f),
                    (name: "BigHappy", frames: new[] { 2, 6, 2 }, duration: 0.55f) })
                {
                    animation.SetValue(bar, Enum.Parse(animation.FieldType, gesture.name));
                    type.GetField("m_animationDuration", flags).SetValue(bar, gesture.duration);
                    // Both timers are overdue: an active gesture must still run to completion.
                    type.GetField("m_nextBlinkAt", flags).SetValue(bar, 0.0);
                    type.GetField("m_nextHappyAt", flags).SetValue(bar, 0.0);
                    for (int step = 0; step < 3; step++)
                    {
                        start.SetValue(bar, Time.unscaledTimeAsDouble - (step + 0.25) * gesture.duration / 3.0);
                        next.SetValue(bar, 0.0);
                        update.Invoke(bar, null);
                        if (image.sprite != happy[gesture.frames[step]] || animation.GetValue(bar).ToString() != gesture.name)
                            throw new InvalidOperationException("Gesture sequence/interruption failed.");
                    }
                    start.SetValue(bar, Time.unscaledTimeAsDouble - gesture.duration - 0.01);
                    next.SetValue(bar, 0.0);
                    update.Invoke(bar, null);
                    if (image.sprite != happy[0] || animation.GetValue(bar).ToString() != "Idle")
                        throw new InvalidOperationException("Return to idle failed.");
                }
                var second = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Status), scene);
                var other = second.GetComponent<PlayerStatusBar>();
                other.SetHealthExpression(0.2f);
                if (type.GetField("m_animationStartedAt", flags).GetValue(bar).Equals(start.GetValue(other)))
                    throw new InvalidOperationException("Animation state was shared.");
                UnityEngine.Object.DestroyImmediate(second);
                var cameraObject = new GameObject("ExpressionPreview", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
                camera.orthographic = true;
                camera.cullingMask = 1 << 5;
                camera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(scene);
                foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
                {
                    var target = new RenderTexture(size.x, size.y, 24);
                    camera.targetTexture = target;
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    pixels.Apply();
                    System.IO.File.WriteAllBytes($"Temp/HudExpressions_{size.x}.png", pixels.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(pixels);
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
