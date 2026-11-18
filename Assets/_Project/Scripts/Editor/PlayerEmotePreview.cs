using System;
using System.IO;
using System.Linq;
using CouchGuys.Player;
using CouchGuys.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    [InitializeOnLoad]
    public static class PlayerEmotePreview
    {
        static PlayerEmotePreview()
        {
            EditorApplication.update += () =>
            {
                if (!File.Exists("Temp/PlayerEmotePreview.request") || EditorApplication.isPlayingOrWillChangePlaymode ||
                    EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                File.Delete("Temp/PlayerEmotePreview.request");
                try { Render(); } catch (Exception e) { File.WriteAllText("Temp/PlayerEmotePreview.txt", e.ToString()); }
            };
        }

        [MenuItem("Couch Guys/Player/Render Emote Previews")]
        public static void Render()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var animator = prefab.GetComponent<CouchGuyAnimationDriver>().Animator;
            var definitions = AssetDatabase.FindAssets("t:EmoteDefinition", new[] { "Assets/_Project/Animations/Emotes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EmoteDefinition>(AssetDatabase.GUIDToAssetPath(g))).OrderBy(d => d.Id).ToArray();
            const int size = 360;
            var sheet = new Texture2D(size * 3, size * 2, TextureFormat.RGB24, false);
            for (int i = 0; i < definitions.Length; i++)
            {
                var preview = new PreviewRenderUtility();
                var model = Object.Instantiate(animator.gameObject);
                preview.AddSingleGO(model);
                try
                {
                    model.transform.position = Vector3.zero;
                    definitions[i].Clip.SampleAnimation(model, definitions[i].Duration * .35f);
                    var bounds = new Bounds(); bool first = true;
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                    { if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds); }
                    Vector3 centre = bounds.center;
                    // Rotate the complete existing visual around its body centre, as runtime does.
                    Quaternion rotation = definitions[i].Rotation(definitions[i].Duration * .35f);
                    model.transform.RotateAround(centre, Vector3.right, rotation.eulerAngles.x);
                    model.transform.RotateAround(centre, Vector3.up, rotation.eulerAngles.y);
                    model.transform.RotateAround(centre, Vector3.forward, rotation.eulerAngles.z);
                    preview.camera.transform.position = centre + new Vector3(2, .7f, 4.5f);
                    preview.camera.transform.LookAt(centre);
                    preview.camera.fieldOfView = 35;
                    preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100;
                    preview.camera.clearFlags = CameraClearFlags.SolidColor;
                    preview.camera.backgroundColor = new Color(.04f, .07f, .12f);
                    preview.lights[0].intensity = 1.3f;
                    preview.lights[0].transform.rotation = Quaternion.Euler(40, 35, 0);
                    preview.lights[1].intensity = 1;
                    preview.ambientColor = new Color(.45f, .45f, .5f);
                    preview.BeginPreview(new Rect(0, 0, size, size), GUIStyle.none);
                    preview.camera.Render();
                    var texture = preview.EndPreview();
                    var rt = RenderTexture.GetTemporary(size, size, 0);
                    Graphics.Blit(texture, rt);
                    var previous = RenderTexture.active; RenderTexture.active = rt;
                    var tile = new Texture2D(size, size, TextureFormat.RGB24, false);
                    tile.ReadPixels(new Rect(0, 0, size, size), 0, 0); tile.Apply();
                    sheet.SetPixels(i % 3 * size, (1 - i / 3) * size, size, size, tile.GetPixels());
                    RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(tile);
                }
                finally { preview.Cleanup(); }
            }
            sheet.Apply(); File.WriteAllBytes("Temp/EmotePreviews.png", sheet.EncodeToPNG()); Object.DestroyImmediate(sheet);
            var wheel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<EmoteWheelView>("Assets/_Project/Prefabs/UI/EmoteWheel.prefab"));
            var cameraObject = new GameObject("Emote preview camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var target = RenderTexture.GetTemporary(1280, 720, 24);
            try
            {
                wheel.Present(definitions, new Vector2(Screen.width * .5f, Screen.height * .5f + Screen.height * .2f));
                camera.targetTexture = target; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.08f, .12f, .2f);
                camera.cullingMask = 1 << 5;
                foreach (Transform child in wheel.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
                var canvas = wheel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render();
                var previous = RenderTexture.active; RenderTexture.active = target;
                var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                File.WriteAllBytes("Temp/EmoteWheelPreview.png", texture.EncodeToPNG());
                RenderTexture.active = previous; Object.DestroyImmediate(texture);
            }
            finally { Object.DestroyImmediate(wheel.gameObject); Object.DestroyImmediate(cameraObject); RenderTexture.ReleaseTemporary(target); }
            File.WriteAllText("Temp/PlayerEmotePreview.txt", "PASS: rendered existing robot poses and saved wheel.");
        }
    }
}
