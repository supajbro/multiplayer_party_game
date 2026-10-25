using System;
using System.IO;
using System.Reflection;
using CouchGuys.Gameplay.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    public static class InteractionPromptPreview
    {
        [MenuItem("Couch Guys/UI/Verify and Preview Interaction Prompt")]
        public static void Verify()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string outputDirectory = Application.isBatchMode ? "PreviewArtifacts" : "Temp";
            Directory.CreateDirectory(outputDirectory);
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture previous = RenderTexture.active;
            GameObject hud = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<PlayerWeaponHudView>(
                    "Assets/_Project/Prefabs/UI/PlayerWeaponHud.prefab");
                hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                hud.transform.localScale = Vector3.one;
                foreach (Transform child in hud.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
                var view = hud.GetComponent<PlayerWeaponHudView>();
                InteractionPromptView prompt = view.m_interactionPrompt;
                if (prompt == null) throw new Exception("HUD prompt reference missing.");
                foreach (FieldInfo field in typeof(InteractionPromptView).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                    if (typeof(Object).IsAssignableFrom(field.FieldType) && (Object)field.GetValue(prompt) == null)
                        throw new Exception("Missing prompt reference: " + field.Name);
                typeof(InteractionPromptView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(prompt, null);
                var visibility = (CanvasGroup)Get(prompt, "m_visibility");
                var content = (CanvasGroup)Get(prompt, "m_content");
                var label = (Text)Get(prompt, "m_action");
                var key = (Text)Get(prompt, "m_key");
                var fill = (Image)Get(prompt, "m_holdFill");
                Check(visibility.alpha == 0f, "Starts hidden");
                prompt.Present("Press E to Pick Up Couch", 0f, 0f);
                Set(prompt, "m_time", 1f);
                prompt.Present("Press E to Pick Up Couch", 0f, 0f);
                Check(visibility.alpha == 1f && key.text == "E" && label.text == "Pick Up Couch", "Appear and key split");
                prompt.Present("Hold E to Drive Truck", 2f, 1f);
                Set(prompt, "m_contentAlpha", 0f);
                prompt.Present("Hold E to Ride in Truck", 2f, 1f);
                Check(label.text == "Hold to Ride in Truck" && fill.fillAmount == 1f, "Latest label wins and hold clamps");
                prompt.Present(null, 0f, 0f);
                Set(prompt, "m_time", 0.08f);
                prompt.Present(null, 0f, 0f);
                float interrupted = visibility.alpha;
                prompt.Present("Press E to Open Doors", 0f, 0f);
                Check(visibility.alpha >= interrupted && visibility.alpha <= 1f, "Hide reversal stays continuous");
                Set(prompt, "m_time", 1f);
                Set(prompt, "m_contentAlpha", 0f);
                prompt.Present("Truck is full", 0f, 0f);
                Check(!((GameObject)Get(prompt, "m_keycap")).activeSelf && label.text == "Truck is full", "Feedback without a key");
                prompt.Present(null, 0f, 0f);
                Set(prompt, "m_time", 1f);
                prompt.Present(null, 0f, 0f);
                Check(visibility.alpha == 0f, "Hide completes");
                prompt.Present("Press E to Pick Up Couch", 0f, 0f);
                Set(prompt, "m_time", 1f);
                prompt.Present("Press E to Pick Up Couch", 0f, 0f);
                content.alpha = 1f;

                var cameraObject = new GameObject("PromptPreviewCamera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.32f, 0.45f, 0.49f);
                camera.cullingMask = 1 << 5;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.orthographic = true;
                camera.orthographicSize = 540f;
                camera.transform.position = new Vector3(0, 0, -10);
                Canvas canvas = hud.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1024, 768) })
                {
                    var target = new RenderTexture(size.x, size.y, 24);
                    camera.targetTexture = target;
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    var image = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    image.Apply();
                    File.WriteAllBytes($"{outputDirectory}/InteractionPrompt-{size.x}x{size.y}.png", image.EncodeToPNG());
                    Object.DestroyImmediate(image);
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                File.WriteAllText(Path.Combine(outputDirectory, "InteractionPromptValidation.txt"), "PASS: serialized references, hidden startup, key/action split, latest label, progress clamp, hide reversal, keyless feedback, completed hide; rendered 1920x1080, 1280x720, 1024x768.");
                Debug.Log("Interaction prompt verification passed. Previews saved to Temp/InteractionPrompt-*.png");
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(outputDirectory, "InteractionPromptValidation.txt"), error.ToString());
                Debug.LogException(error);
            }
            finally
            {
                RenderTexture.active = previous;
                if (hud != null) Object.DestroyImmediate(hud);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static object Get(InteractionPromptView view, string name) =>
            typeof(InteractionPromptView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
        private static void Set(InteractionPromptView view, string name, object value) =>
            typeof(InteractionPromptView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, value);
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Interaction prompt check failed: " + message);
        }
    }
}

