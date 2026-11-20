using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    [InitializeOnLoad]
    public sealed class RobotFaceDisplayTests
    {
        private const string Report = "Temp/RobotCRTValidation.txt";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static RobotFaceDisplayTests()
        {
            // Explicit request only; no asset generation or mutation on import/reload.
            EditorApplication.update += () =>
            {
                const string request = "Temp/RobotCRTValidation.request";
                if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                    EditorApplication.isPlayingOrWillChangePlaymode) return;
                File.Delete(request);
                Run();
            };
        }
        [MenuItem("Couch Guys/Art/Validate CRT Display")]
        public static void Run()
        {
            File.WriteAllText(Report, "");
            try
            {
                var tests = new RobotFaceDisplayTests();
                tests.SavedAssetsAndExpressionPriority();
                tests.ExistingScreenBindsAndRestores();
                File.AppendAllText(Report, "PASS: assets, expression priority, existing screen UV render, restoration.\n");
            }
            catch (Exception e) { File.AppendAllText(Report, "FAIL: " + e + "\n"); Debug.LogException(e); }
        }

        [Test]
        public void SavedAssetsAndExpressionPriority()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var controller = prefab.GetComponent<RobotFaceExpressionController>();
            var savedScreen = prefab.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "FaceDisplay");
            var importedScreen = PrefabUtility.GetCorrespondingObjectFromSource(savedScreen);
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(importedScreen, out string modelGuid, out long rendererId))
                File.AppendAllText(Report, $"Existing screen source: {modelGuid}, renderer {rendererId}\n");
            Assert.That(controller, Is.Not.Null);
            Assert.That(new SerializedObject(controller).FindProperty("m_faceRenderer").objectReferenceValue,
                Is.EqualTo(savedScreen), "Saved controller must reference the rectangular screen");
            Assert.That(prefab.GetComponents<RobotFaceDisplay>().Length, Is.EqualTo(1));
            var settings = Resources.Load<RobotFaceDisplaySettings>("RobotFaceDisplaySettings");
            Assert.That(settings.Material.shader.name, Is.EqualTo("Couch Guys/Robot CRT Display"));
            var atlas = settings.Material.GetTexture("_ExpressionAtlas");
            Assert.That(atlas.width, Is.EqualTo(1280)); Assert.That(atlas.height, Is.EqualTo(256));
            Assert.That(Enum.GetValues(typeof(RobotFaceExpression)).Cast<RobotFaceExpression>()
                .Select(RobotFaceDisplay.AtlasIndex).Distinct().Count(), Is.EqualTo(10));
            Assert.That((int)RobotFaceExpression.Focused, Is.EqualTo(4), "Saved emote enum IDs must remain stable");

            GameObject go = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player.prefab");
            try
            {
                var health = go.GetComponent<PlayerHealth>();
                var face = go.GetComponent<RobotFaceExpressionController>();
                typeof(RobotFaceExpressionController).GetField("m_health", Private).SetValue(face, health);
                var resolve = typeof(RobotFaceExpressionController).GetMethod("ResolveExpression", Private);
                RobotFaceExpression Resolve(bool ready = true, bool downed = false) =>
                    (RobotFaceExpression)resolve.Invoke(face, new object[] { Time.time, ready, downed });
                void SetState(string name, object value)
                {
                    var field = typeof(PlayerHealth).GetFields(Private | BindingFlags.Public)
                        .FirstOrDefault(f => f.Name == name && f.FieldType.Name.StartsWith("SyncVar"));
                    Assert.That(field, Is.Not.Null, string.Join(", ", typeof(PlayerHealth).GetFields(Private | BindingFlags.Public).Select(f => f.Name + ":" + f.FieldType.Name)));
                    object sync = field.GetValue(health);
                    Assert.That(sync, Is.Not.Null, "SyncVar initialized");
                    sync.GetType().GetField("_value", Private).SetValue(sync, value);
                }
                SetState("m_currentHealth", 100f);
                Assert.That(Resolve(), Is.EqualTo(RobotFaceExpression.Happy));
                face.SetEmoteExpression(RobotFaceExpression.Happy);
                Assert.That(Resolve(), Is.EqualTo(RobotFaceExpression.Celebrating));
                SetState("m_currentHealth", 20f);
                Assert.That(Resolve(), Is.EqualTo(RobotFaceExpression.Damaged));
                face.NotifyDamage();
                Assert.That(Resolve(), Is.EqualTo(RobotFaceExpression.Surprised));
                typeof(RobotFaceExpressionController).GetField("m_rebootUntil", Private).SetValue(face, Time.time + 1);
                Assert.That(Resolve(), Is.EqualTo(RobotFaceExpression.Reviving));
                Assert.That(Resolve(true, true), Is.EqualTo(RobotFaceExpression.Downed));
                SetState("m_currentHealth", 0f);
                SetState("m_knockedDown", true);
                SetState("m_reviver", go.GetComponent<FishNet.Object.NetworkObject>());
                Assert.That(Resolve(true, true), Is.EqualTo(RobotFaceExpression.Reviving));
                SetState("m_reviver", null);
                Assert.That(Resolve(true, true), Is.EqualTo(RobotFaceExpression.Downed));
                File.AppendAllText(Report, "PASS: idle/emote/low health/damage/reboot/downed priority, stable enum IDs, ten atlas tiles.\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(go); }
        }

        [Test]
        public void ExistingScreenBindsAndRestores()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var source = prefab.GetComponent<CouchGuyAnimationDriver>().Animator.gameObject;
            var preview = new PreviewRenderUtility();
            GameObject model = Object.Instantiate(source);
            preview.AddSingleGO(model);
            Texture2D sheet = null;
            try
            {
                model.transform.position = Vector3.zero;
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                Renderer screen = renderers.Single(r => r.name == "FaceDisplay");
                var original = screen.sharedMaterial;
                int transforms = model.GetComponentsInChildren<Transform>(true).Length;
                var originalProperties = new MaterialPropertyBlock();
                originalProperties.SetFloat("_CRTTest", 42); screen.SetPropertyBlock(originalProperties, 0);
                var display = model.AddComponent<RobotFaceDisplay>();
                typeof(RobotFaceDisplay).GetMethod("Awake", Private).Invoke(display, null);
                typeof(RobotFaceDisplay).GetMethod("OnEnable", Private).Invoke(display, null);
                Assert.That(screen.sharedMaterial == Resources.Load<RobotFaceDisplaySettings>("RobotFaceDisplaySettings").Material,
                    Is.True, "Renderer must share the saved material's native Unity object");
                Assert.That(renderers.Where(r => r.name.StartsWith("FaceGraphic_")).All(r => r.forceRenderingOff), Is.True);
                Assert.That(model.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(transforms));
                display.PlayDamageGlitch();
                display.PlayReboot();
                typeof(RobotFaceDisplay).GetMethod("LateUpdate", Private).Invoke(display,null);
                var animated = new MaterialPropertyBlock(); screen.GetPropertyBlock(animated,0);
                Assert.That(animated.GetVector("_Effects").x, Is.GreaterThan(0), "Damage starts local glitch");
                Assert.That(animated.GetVector("_State").w, Is.LessThan(.1f), "Recovery starts local boot wipe");
                const int w = 400, h = 220;
                sheet = new Texture2D(w * 5, h * 2, TextureFormat.RGB24, false);
                var expressions = new[] { RobotFaceExpression.Happy, RobotFaceExpression.Excited, RobotFaceExpression.Surprised,
                    RobotFaceExpression.Focused, RobotFaceExpression.Confused, RobotFaceExpression.Damaged,
                    RobotFaceExpression.Downed, RobotFaceExpression.Reviving, RobotFaceExpression.Angry, RobotFaceExpression.Celebrating };
                // Head crop, using the actual saved character's mesh and skin/UVs.
                Vector3 centre = screen.bounds.center;
                preview.camera.transform.position = centre + new Vector3(0, 0, 3);
                preview.camera.transform.LookAt(centre);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = Mathf.Max(screen.bounds.size.y * 0.85f, .2f);
                preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.04f, .07f, .12f);
                preview.ambientColor = new Color(.55f,.55f,.6f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40,180,0);
                File.AppendAllText(Report, $"Screen bounds: {screen.bounds}; original material: {original.name}\n");
                for (int i = 0; i < expressions.Length; i++)
                {
                    display.SetExpression(expressions[i]);
                    typeof(RobotFaceDisplay).GetMethod("LateUpdate", Private).Invoke(display,null);
                    var block = new MaterialPropertyBlock(); screen.GetPropertyBlock(block,0);
                    block.SetVector("_State",new Vector4(i,i,1,1));
                    block.SetVector("_Motion",new Vector4(1,1, i==7?1:0,1));
                    block.SetVector("_Effects",new Vector4(0,.0015f, i==5?1:0,0));
                    screen.SetPropertyBlock(block,0);
                    preview.BeginPreview(new Rect(0,0,w,h),GUIStyle.none);
                    preview.camera.Render();
                    var texture = preview.EndPreview();
                    var rt = RenderTexture.GetTemporary(w,h,0);
                    var previous = RenderTexture.active;
                    Texture2D tile = null;
                    try
                    {
                        Graphics.Blit(texture,rt); RenderTexture.active=rt;
                        tile = new Texture2D(w,h,TextureFormat.RGB24,false);
                        tile.ReadPixels(new Rect(0,0,w,h),0,0);tile.Apply();
                        int cyanPixels = tile.GetPixels().Count(c => c.g > .35f && c.b > .4f && c.r < c.g * .65f);
                        Assert.That(cyanPixels, Is.GreaterThan(150), $"Expression {expressions[i]} must visibly illuminate the screen");
                        sheet.SetPixels(i%5*w,(1-i/5)*h,w,h,tile.GetPixels());
                    }
                    finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(tile!=null)Object.DestroyImmediate(tile); }
                }
                sheet.Apply(); File.WriteAllBytes("Temp/RobotCRTExpressions.png",sheet.EncodeToPNG());
                display.RestoreOriginalDisplay();
                Assert.That(screen.sharedMaterial == original, Is.True, "Original native material restored");
                var restored = new MaterialPropertyBlock();screen.GetPropertyBlock(restored,0);
                Assert.That(restored.GetFloat("_CRTTest"), Is.EqualTo(42));
                Assert.That(renderers.Where(r => r.name.StartsWith("FaceGraphic_")).All(r => !r.forceRenderingOff), Is.True);
                var messages = ShaderUtil.GetShaderMessages(Resources.Load<RobotFaceDisplaySettings>("RobotFaceDisplaySettings").Material.shader);
                Assert.That(messages.Any(m => m.severity.ToString() == "Error"), Is.False,
                    string.Join("\n",messages.Select(m => m.message)));
                File.AppendAllText(Report, "PASS: actual model screen renders ten expressions; no shader errors, no hierarchy changes; material/property block/legacy graphics restored.\n");
            }
            finally { if(sheet!=null) Object.DestroyImmediate(sheet);preview.Cleanup(); }
        }
    }
}
