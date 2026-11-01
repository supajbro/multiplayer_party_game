using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Depot;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    // Explicit deterministic visual check against the saved prefab; does not alter the user's scene.
    public static class DepotCurtainProbe
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Private).Invoke(o, args);
        static T Read<T>(object o, string field) => (T)o.GetType().GetField(field, Private).GetValue(o);
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

        [MenuItem("Couch Guys/Depot 02/Validate Flexible Curtain %#&F5")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new Exception("Run curtain validation outside Play mode.");
            const string report = "Temp/depot02-curtain-validation.txt";
            var preview = new PreviewRenderUtility();
            GameObject root = null, couch = null;
            try
            {
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CouchGuys.Editor.Depot02PrefabBuilder.PrefabPath));
                preview.AddSingleGO(root);
                var curtain = root.GetComponentInChildren<ConveyorStripCurtain>();
                Require(curtain != null, "Missing saved curtain component");
                var frame = Read<Transform>(curtain, "m_frame");
                var strips = Read<MeshFilter[]>(curtain, "m_strips");
                Require(strips.Length == 9 && strips.All(s => s != null && s.sharedMesh.isReadable), "Invalid saved strips");
                Require(frame.GetComponentsInChildren<Collider>().Length == 0, "Moving curtain has colliders");
                Require(Vector3.Dot(frame.forward, Vector3.right) > .99f, "Curtain does not bend along belt travel");
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                    if (!renderer.transform.IsChildOf(curtain.transform)) renderer.enabled = false;
                foreach (var lod in curtain.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);
                Call(curtain, "Awake");
                Require(Read<Array>(curtain, "m_states") != null, "Visual simulation did not initialize");
                Vector3[][] rest = strips.Select(s => s.sharedMesh.vertices).ToArray();
                preview.camera.transform.position = frame.TransformPoint(new Vector3(5, .6f, 7));
                preview.camera.transform.LookAt(frame.TransformPoint(new Vector3(0, -1.3f, 0)));
                preview.camera.fieldOfView = 43;
                preview.camera.enabled = true;
                preview.camera.nearClipPlane = .05f; preview.camera.farClipPlane = 100;
                preview.ambientColor = new Color(.65f, .7f, .8f);
                preview.lights[0].intensity = 1.8f; preview.lights[0].transform.rotation = Quaternion.Euler(35, 120, 0);
                preview.lights[1].intensity = 1.2f;
                curtain.GetType().GetField("m_gameplayCamera", Private).SetValue(curtain, preview.camera);
                Require((bool)Call(curtain, "IsVisible"), "Camera should see curtain");
                Render(preview, "idle");
                couch = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Couch.prefab"));
                preview.AddSingleGO(couch);
                var couchData = new SerializedObject(couch.GetComponent<CouchGuys.Gameplay.Couch.CouchCarryController>());
                var model = (GameObject)couchData.FindProperty("m_modelPrefabs").GetArrayElementAtIndex(0).objectReferenceValue;
                Object.Instantiate(model, (Transform)couchData.FindProperty("m_modelRoot").objectReferenceValue, false);
                // Match the real conveyor pose: wide couch axis spans the opening.
                couch.transform.SetPositionAndRotation(new Vector3(-13.6f, 1.25f, -1.8f), Quaternion.Euler(0, 90, 0));
                var bounds = (Bounds)Call(curtain, "CouchBoundsInFrame", couch.GetComponent<BoxCollider>());
                for (int i = 0; i < 90; i++) Call(curtain, "Simulate", bounds, true, 1f / 60);
                float middle = strips[4].sharedMesh.vertices.Max(v => v.z);
                float edge = strips[0].sharedMesh.vertices.Max(v => v.z);
                Require(middle > .2f && middle > edge + .15f, "Contact strips must flex more than outer strips");
                for (int s = 0; s < strips.Length; s++)
                {
                    var bent = strips[s].sharedMesh.vertices;
                    for (int v = 0; v < bent.Length; v++)
                        if (rest[s][v].y > -.001f) Require(Vector3.Distance(rest[s][v], bent[v]) < .001f, "Top pivot moved");
                }
                Render(preview, "passing");
                couch.SetActive(false);
                bool overshot = false;
                for (int i = 0; i < 720; i++)
                {
                    Call(curtain, "Simulate", default(Bounds), false, 1f / 60);
                    var states = Read<Array>(curtain, "m_states");
                    object state = states.GetValue(4);
                    float angle = (float)state.GetType().GetField("angle").GetValue(state);
                    if (angle < -.05f) overshot = true;
                    if (i == 14) Render(preview, "settling");
                }
                Require(overshot, "Spring should swing through rest before settling");
                Require(strips.Select((s, i) => s.sharedMesh.vertices.Zip(rest[i], (a, b) => Vector3.Distance(a, b)).Max()).Max() < .0001f,
                    "Strips did not settle to saved idle pose");
                for (int i = 0; i < 45; i++) Call(curtain, "Simulate", bounds, true, 1f / 30);
                Require(strips[4].sharedMesh.vertices.Max(v => v.z) > .2f, "Subsequent couch did not animate");
                preview.camera.transform.rotation = Quaternion.LookRotation(-frame.forward);
                preview.camera.transform.position = frame.position - frame.forward * 10;
                Require(!(bool)Call(curtain, "IsVisible"), "Offscreen curtain should be culled");
                Call(curtain, "RestoreIdle");
                preview.camera.transform.position = frame.position + frame.forward * 100;
                preview.camera.transform.LookAt(frame.position);
                Require(!(bool)Call(curtain, "IsVisible"), "Distant curtain should be culled");
                File.WriteAllText(report, $"PASS: saved nine strips; correct travel direction; contacted tip displacement={middle:F3}m, outer={edge:F3}m; fixed top pivots; independent flex; overshoot; exact rest; subsequent couch at 30Hz; frustum and distance culling.\nPreviews: Temp/depot02-curtain-idle.png, passing.png, settling.png\n");
                Debug.Log(File.ReadAllText(report));
            }
            catch (Exception e) { File.WriteAllText(report, "FAIL: " + e); Debug.LogException(e); }
            finally
            {
                if (root != null) root.GetComponent<DepotController>().ReleaseDoorAnimations();
                preview.camera.targetTexture = null;
                preview.Cleanup();
            }
        }
        static void Render(PreviewRenderUtility preview, string pose)
        {
            preview.BeginStaticPreview(new Rect(0, 0, 1000, 800)); preview.Render(true);
            var image = preview.EndStaticPreview();
            File.WriteAllBytes("Temp/depot02-curtain-" + pose + ".png", image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
