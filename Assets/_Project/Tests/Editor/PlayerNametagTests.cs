using System;
using System.IO;
using System.Reflection;
using CouchGuys.Networking;
using CouchGuys.Player;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Tests.Editor
{
    public sealed class PlayerNametagTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void SavedPlayer_ResolvesAnimatedHeadAndBuildSafeView()
        {
            GameObject player = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player.prefab");
            try
            {
                PlayerNametag tag = player.GetComponent<PlayerNametag>();
                Assert.That(tag, Is.Not.Null);
                Assert.That(Get<NetworkPlayerOwnership>(tag, "m_identity"),
                    Is.EqualTo(player.GetComponent<NetworkPlayerOwnership>()));
                Invoke(tag, "Awake");
                Transform head = Get<Transform>(tag, "m_head");
                Assert.That(head, Is.Not.Null);
                Assert.That(head.name, Is.EqualTo("Head"));
                Assert.That(head.IsChildOf(Get<Transform>(tag, "m_visualRoot")), Is.True);
                Vector3 original = head.position;
                Transform visual = Get<Transform>(tag, "m_visualRoot");
                visual.rotation = Quaternion.Euler(78f, 0f, 0f);
                visual.position += new Vector3(3f, 0f, 4f);
                Assert.That(Vector3.Distance(head.position, original), Is.GreaterThan(2f),
                    "The nametag must retain the moving head transform during knockdown.");
                Assert.That(Get<float>(tag, "m_maximumVisibilityDistance"), Is.EqualTo(25f));
                RectTransform view = Get<RectTransform>(tag, "m_nametagPrefab");
                Assert.That(AssetDatabase.GetAssetPath(view),
                    Is.EqualTo("Assets/_Project/Prefabs/UI/PlayerNametag.prefab"));
                Assert.That(view.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
                TMP_Text text = view.GetComponentInChildren<TMP_Text>(true);
                Assert.That(text.font, Is.Not.Null);
                Assert.That(text.fontSharedMaterial, Is.Not.Null);
                Assert.That(text.fontSharedMaterial.GetFloat("_OutlineWidth"), Is.GreaterThan(0f));
                Assert.That(text.richText, Is.False);
                Assert.That(text.raycastTarget, Is.False);
                Assert.That(text.color, Is.EqualTo(Color.white));
                Assert.That(text.GetComponent<UnityEngine.UI.GraphicRaycaster>(), Is.Null);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        [Test]
        public void Occlusion_IgnoresPlayersAndTriggersButHidesBehindSolidGeometry()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject target = Create(scene, "Target", new Vector3(0f, 0f, 10f));
                PlayerNametag tag = target.AddComponent<PlayerNametag>();
                GameObject viewer = Create(scene, "Viewer", Vector3.zero);
                BoxCollider ownBody = Create(scene, "Target body", new Vector3(0f, 0f, 9f)).AddComponent<BoxCollider>();
                ownBody.transform.SetParent(target.transform, true);
                BoxCollider viewerBody = Create(scene, "Viewer body", Vector3.forward).AddComponent<BoxCollider>();
                viewerBody.transform.SetParent(viewer.transform, true);
                BoxCollider obstacle = Create(scene, "Wall", new Vector3(0f, 0f, 5f)).AddComponent<BoxCollider>();
                obstacle.isTrigger = true;
                Physics.SyncTransforms();
                Assert.That(LineOfSight(tag, viewer.transform), Is.True);
                obstacle.isTrigger = false;
                Physics.SyncTransforms();
                Assert.That(LineOfSight(tag, viewer.transform), Is.False);
                obstacle.gameObject.layer = 2;
                typeof(PlayerNametag).GetField("m_obstructionLayers", Private).SetValue(tag, (LayerMask)~(1 << 2));
                Physics.SyncTransforms();
                Assert.That(LineOfSight(tag, viewer.transform), Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static bool LineOfSight(PlayerNametag tag, Transform viewer) =>
            (bool)Invoke(tag, "HasLineOfSight", Vector3.zero, new Vector3(0f, 0f, 10f), viewer);

        private static GameObject Create(Scene scene, string name, Vector3 position)
        {
            GameObject instance = new(name);
            SceneManager.MoveGameObjectToScene(instance, scene);
            instance.transform.position = position;
            return instance;
        }

        private static T Get<T>(object target, string field) =>
            (T)target.GetType().GetField(field, Private).GetValue(target);
        private static object Invoke(object target, string method, params object[] arguments) =>
            target.GetType().GetMethod(method, Private).Invoke(target, arguments);

        [MenuItem("Tools/Couch Guys/Validate Player Nametags")]
        public static void ValidateFromMenu()
        {
            string path = "Temp/NametagValidation/editor-result.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                var tests = new PlayerNametagTests();
                tests.SavedPlayer_ResolvesAnimatedHeadAndBuildSafeView();
                tests.Occlusion_IgnoresPlayersAndTriggersButHidesBehindSolidGeometry();
                File.WriteAllText(path, "PASS: Saved prefab, identity reference, animated head, TMP material, 25m default, solid occlusion, player/trigger exclusions and layer mask.");
                Debug.Log(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                File.WriteAllText(path, "FAIL: " + exception);
                Debug.LogException(exception);
            }
        }
    }
}
