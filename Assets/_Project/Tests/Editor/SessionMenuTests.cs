using System.Reflection;
using CouchGuys.Input;
using CouchGuys.Networking;
using CouchGuys.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CouchGuys.Tests.Editor
{
    public sealed class SessionMenuTests
    {
        [TestCase("PauseMenu", false)]
        [TestCase("GameOverMenu", true)]
        public void MenuPrefabsHaveEditableControls(string name, bool voting)
        {
            var view = AssetDatabase.LoadAssetAtPath<SessionMenuView>($"Assets/Prefabs/UI/{name}.prefab");
            Assert.That(view, Is.Not.Null);
            Assert.That(view.PrimaryButton, Is.Not.Null);
            Assert.That(view.ExitButton, Is.Not.Null);
            Assert.That(view.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(view.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(view.GetComponentInChildren<EventSystem>(true), Is.Not.Null);
            Assert.That(view.PrimaryButton.GetComponentInChildren<Text>().text, Is.EqualTo(voting ? "Retry" : "Resume"));
            if (voting)
            {
                Assert.That(view.ProgressLabel, Is.Not.Null);
                Assert.That(view.LocalReadyLabel, Is.Not.Null);
            }
        }

        [TestCase("SoloNetworkBootstrap")]
        [TestCase("SteamMultiplayerBootstrap")]
        public void BothSessionTransportsReferenceMenus(string name)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Resources/{name}.prefab");
            var controller = root.GetComponent<GameplaySessionController>();
            Assert.That(controller, Is.Not.Null);
            var serialized = new SerializedObject(controller);
            Assert.That(serialized.FindProperty("m_pauseMenuPrefab").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("m_gameOverMenuPrefab").objectReferenceValue, Is.Not.Null);
        }

        [Test]
        public void RetryTransitionIsIncludedInBuild()
        {
            Assert.That(System.Array.Exists(EditorBuildSettings.scenes,
                s => s.enabled && s.path == "Assets/Scenes/RetryTransition.unity"), Is.True);
        }

        [Test]
        public void PauseAndGameOverKeepSeparateInputSuppressionAndTimeScale()
        {
            var root = new GameObject("Session input test");
            root.SetActive(false); // No networking is started in this asset/input test.
            var controller = root.AddComponent<GameplaySessionController>();
            var input = root.AddComponent<PlayerInputReader>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo instance = typeof(GameplaySessionController).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = instance.GetValue(null);
            float timeScale = Time.timeScale;
            try
            {
                instance.SetValue(null, controller);
                typeof(GameplaySessionController).GetField("m_paused", flags).SetValue(controller, true);
                Assert.That(GameplaySessionController.BlocksLocalInput, Is.True);
                input.SetGameplaySuppressed(false); // Vehicle/loading callbacks cannot clear menu blocking.
                Assert.That(input.Move, Is.EqualTo(Vector2.zero));
                Assert.That(input.VehicleMove, Is.EqualTo(Vector2.zero));
                Assert.That(input.VehicleLook, Is.EqualTo(Vector2.zero));
                Assert.That(input.VehicleInteractHeld, Is.False);
                Assert.That(input.VehicleHornPressedThisFrame, Is.False);
                Assert.That(input.ShootHeld, Is.False);
                Assert.That(input.HotbarSelectionPressedThisFrame, Is.EqualTo(-1));
                typeof(GameplaySessionController).GetField("m_paused", flags).SetValue(controller, false);
                typeof(GameplaySessionController).GetField("m_localState", flags).SetValue(controller, new SessionStateMessage { GameOver = true });
                Assert.That(GameplaySessionController.BlocksLocalInput, Is.True);
                Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            }
            finally
            {
                instance.SetValue(null, previous);
                Object.DestroyImmediate(root);
            }
        }
    }
}
