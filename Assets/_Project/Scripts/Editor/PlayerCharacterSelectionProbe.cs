using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Player;
using CouchGuys.UI;
using CouchGuys.Networking;
using FishNet.Managing;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    [InitializeOnLoad]
    public static class PlayerCharacterSelectionProbe
    {
        private const string Pending = "CharacterSelection.Probe";
        private const string Result = "Temp/PlayerCharacterSelection/Play.result";
        private static IEnumerator s_probe;
        private static NetworkManager s_host, s_remote;
        static PlayerCharacterSelectionProbe() => EditorApplication.playModeStateChanged += StateChanged;

        [MenuItem("Couch Guys/Main Menu/Test Character Selection In Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var current = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (current.isDirty || string.IsNullOrEmpty(current.path) || UnityEngine.SceneManagement.SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Save the current scene before running the isolated character check.");
            Directory.CreateDirectory("Temp/PlayerCharacterSelection");
            File.WriteAllText(Result, "START\n");
            SessionState.SetString(Pending + ".Scene", current.path);
            SessionState.SetString(Pending + ".Selection", PlayerPrefs.GetString("Player.Character", string.Empty));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Temp/AITest.unity");
            SessionState.SetBool(Pending, true);EditorApplication.EnterPlaymode();
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { s_probe = Probe();EditorApplication.update += Tick; }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                string previous = SessionState.GetString(Pending + ".Selection", string.Empty);
                if (previous.Length == 0) PlayerPrefs.DeleteKey("Player.Character");else PlayerPrefs.SetString("Player.Character", previous);
                PlayerPrefs.Save();
                EditorSceneManager.OpenScene(SessionState.GetString(Pending + ".Scene", "Assets/Scenes/MainMenu.unity"));
            }
        }
        private static void Tick()
        {
            try { if (s_probe != null && !s_probe.MoveNext()) Finish(); }
            catch (Exception e) { File.AppendAllText(Result, "FAIL: " + e + "\n");Debug.LogException(e);Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick;s_probe = null;
            if (s_remote != null) s_remote.ClientManager.StopConnection();
            if (s_host != null) { s_host.ClientManager.StopConnection();s_host.ServerManager.StopConnection(true); }
            EditorApplication.ExitPlaymode();
        }
        private static void Require(bool value, string text) { if (!value) throw new Exception(text);File.AppendAllText(Result, "PASS: " + text + "\n"); }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static Dictionary<int, NetworkObject> Choices(NetworkManager manager) =>
            (Dictionary<int, NetworkObject>)typeof(NeighbourhoodPlayerSpawner).GetField("m_characterPrefabs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager.GetComponentInChildren<NeighbourhoodPlayerSpawner>());

        private static NetworkManager Manager()
        {
            var staging = new GameObject("Inactive network setup");staging.SetActive(false);
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab"),staging.transform);
            var manager = go.GetComponentInChildren<NetworkManager>(true);var so = new SerializedObject(manager);
            so.FindProperty("_persistence").enumValueIndex = (int)NetworkManager.PersistenceType.AllowMultiple;
            so.ApplyModifiedPropertiesWithoutUndo();go.transform.SetParent(null);go.SetActive(true);Object.Destroy(staging);
            return manager;
        }

        private static IEnumerator Probe()
        {
            GameLaunchContext.BeginSolo();
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterCatalog>(PlayerCharacterAuthoring.CatalogPath);
            catalog.Select(0);
            var canvas = new GameObject("Menu Canvas",typeof(Canvas)).GetComponent<Canvas>();canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var menu = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/MainMenuUI.prefab"),canvas.transform);
            var menuGroup = menu.AddComponent<CanvasGroup>();
            var arrows = menu.transform.Find("Customise Arrows").GetComponent<CanvasGroup>();
            var buttons = arrows.GetComponentsInChildren<Button>();
            var left = buttons.First(b=>b.name.StartsWith("Left"));var right = buttons.First(b=>b.name.StartsWith("Right"));
            var anchor = new GameObject("Player Anchor").transform;
            var robot = Object.Instantiate(catalog[0].MenuVisualPrefab,anchor).GetComponent<Animator>();
            var camera = new GameObject("Menu Camera",typeof(Camera)).GetComponent<Camera>();camera.transform.position = new Vector3(0,2.4f,-7);
            var host = new GameObject("Menu Intro");host.SetActive(false);var intro = host.AddComponent<MainMenuIntro>();
            Set(intro,"m_enterScreen",new GameObject("Enter",typeof(CanvasGroup)).GetComponent<CanvasGroup>());
            Set(intro,"m_menu",menuGroup);Set(intro,"m_menuAnimator",menu.GetComponent<Animator>());
            Set(intro,"m_menuEntrance",AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/MainMenuEntrance.anim"));
            Set(intro,"m_robotAnchor",anchor);Set(intro,"m_robotAnimator",robot);Set(intro,"m_menuCamera",camera);
            Set(intro,"m_customiseArrows",arrows);Set(intro,"m_characterCatalog",catalog);
            Set(intro,"m_previousCharacterButton",left);Set(intro,"m_nextCharacterButton",right);
            host.SetActive(true);Set(intro,"m_menuReady",true);anchor.gameObject.SetActive(true);menuGroup.alpha=1;
            menu.GetComponent<Animator>().enabled=true;menu.GetComponent<Animator>().Play("Slide In",0,1);menu.GetComponent<Animator>().Update(0);
            intro.OpenCustomise();
            double until = EditorApplication.timeSinceStartup + 2;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Require(right.IsInteractable() && left.IsInteractable() && !menuGroup.interactable && arrows.blocksRaycasts,"Arrows accept input while the main menu stays blocked");
            right.onClick.Invoke();
            Require(catalog.SelectedIndex==1 && !robot.gameObject.activeSelf && anchor.GetComponentsInChildren<Renderer>().Any(r=>r.name=="Wheelie_Body"),"Next arrow swaps to saved Wheelie preview and persists selection");
            left.onClick.Invoke();Require(catalog.SelectedIndex==0 && robot.gameObject.activeSelf,"Previous arrow restores current player preview");
            left.onClick.Invoke();Require(catalog.SelectedIndex==1,"Previous arrow wraps from first to last character");
            Object.Destroy(host);Object.Destroy(canvas.gameObject);Object.Destroy(anchor.gameObject);Object.Destroy(camera.gameObject);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=Vector3.down*.5f;ground.transform.localScale=new Vector3(40,1,40);
            s_host=Manager();yield return null;
            s_host.ServerManager.StartConnection();s_host.ClientManager.StartConnection();
            until=EditorApplication.timeSinceStartup+15;
            while ((Choices(s_host).Count<1 || !s_host.ClientManager.Started) && EditorApplication.timeSinceStartup<until) yield return null;
            Require(Choices(s_host).Count==1 && Choices(s_host).Values.First()==catalog[1].PlayerPrefab,"Host scene-load handshake selects Wheelie prefab");
            var owner=s_host.ServerManager.Clients.Values.First();
            var wheelie=Object.Instantiate(Choices(s_host)[owner.ClientId],new Vector3(0,.1f,0),Quaternion.identity);
            s_host.ServerManager.Spawn(wheelie,owner);
            Require(wheelie.GetComponent<ThirdPersonPlayerController>()!=null && wheelie.GetComponent<WheelieController>()==null,"Spawned Wheelie is a playable player with no AI controller");
            catalog.Select(0);s_remote=Manager();yield return null;s_remote.ClientManager.StartConnection();
            until=EditorApplication.timeSinceStartup+15;
            while ((Choices(s_host).Count<2 || !s_remote.ClientManager.Objects.Spawned.ContainsKey(wheelie.ObjectId)) && EditorApplication.timeSinceStartup<until) yield return null;
            Require(Choices(s_host).Count==2 && Choices(s_host).Values.Contains(catalog[0].PlayerPrefab),"Independent client selects original prefab without changing host selection");
            Require(s_remote.ClientManager.Objects.Spawned.TryGetValue(wheelie.ObjectId,out var replica) && replica.GetComponent<WheeliePlayerPresentation>()!=null,"Independent client receives the selected Wheelie prefab");
            var remoteOwner=s_host.ServerManager.Clients.Values.First(c=>c.ClientId!=owner.ClientId);
            var original=Object.Instantiate(Choices(s_host)[remoteOwner.ClientId],new Vector3(3,.1f,0),Quaternion.identity);
            s_host.ServerManager.Spawn(original,remoteOwner);
            until=EditorApplication.timeSinceStartup+5;
            while (!s_remote.ClientManager.Objects.Spawned.ContainsKey(original.ObjectId) && EditorApplication.timeSinceStartup<until) yield return null;
            Require(s_remote.ClientManager.Objects.Spawned.TryGetValue(original.ObjectId,out var own) && own.IsOwner && own.GetComponent<ThirdPersonPlayerController>().enabled,"Remote client owns the selected original player and retains local controls");
            File.AppendAllText(Result,"PASS: ALL CHECKS COMPLETED\n");
        }
    }
}
