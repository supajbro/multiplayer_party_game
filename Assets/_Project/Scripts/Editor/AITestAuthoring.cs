using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using CouchGuys.Testing;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Editor
{
    public static class AITestAuthoring
    {
        private const string ScenePath = "Assets/Scenes/AITest.unity";
        [MenuItem("Couch Guys/AI Test/Create Scene %#F8")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlaying || File.Exists(ScenePath))
            {
                Debug.LogWarning("AITest creation stopped: exit Play mode and preserve any existing AITest scene.");
                return;
            }
            const string settingsFolder = "Assets/_Project/Settings/AI";
            const string prefabFolder = "Assets/_Project/Prefabs/AI";
            Directory.CreateDirectory(settingsFolder);
            Directory.CreateDirectory(prefabFolder);
            AssetDatabase.Refresh();
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var prefabs = new NetworkObject[4];
            for (int i = 0; i < 4; i++)
            {
                string roleName = ((AITeammateRole)i).ToString();
                string rolePath = $"{settingsFolder}/AI_{roleName}.asset";
                var settings = AssetDatabase.LoadAssetAtPath<AITeammateRoleSettings>(rolePath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<AITeammateRoleSettings>();
                    settings.Role = (AITeammateRole)i;
                    settings.CouchNavigationSettings = AssetDatabase.LoadAssetAtPath<CouchGuys.Gameplay.Vehicles.GetawayTruckSettings>("Assets/_Project/Resources/Vehicles/GetawayTruckSettings.asset");
                    settings.RevivalPriority = i == 3 ? 3 : i == 0 ? 2 : 0;
                    settings.CombatDetectionRange = i == 1 ? 18f : 12f;
                    settings.MaximumCombatChaseDistance = i == 1 ? 22f : 14f;
                    AssetDatabase.CreateAsset(settings, rolePath);
                }
                string prefabPath = $"{prefabFolder}/AI_{roleName}.prefab";
                if (!File.Exists(prefabPath))
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                    instance.name = $"AI_{roleName}";
                    instance.GetComponent<DebugCouchBotController>().SetRoleSettings(settings);
                    if (instance.GetComponent<NavMeshAgent>() == null) instance.AddComponent<NavMeshAgent>();
                    instance.GetComponent<NavMeshAgent>().enabled = false;
                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                    Object.DestroyImmediate(instance);
                }
                prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<NetworkObject>();
            }
            // Add a default Inspector configuration to production teammates without changing their spawn settings.
            var player = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player.prefab");
            try
            {
                var bot = player.GetComponent<DebugCouchBotController>();
                if (bot.RoleSettings == null)
                    bot.SetRoleSettings(AssetDatabase.LoadAssetAtPath<AITeammateRoleSettings>($"{settingsFolder}/AI_HumanSupport.asset"));
                if (player.GetComponent<NavMeshAgent>() == null) player.AddComponent<NavMeshAgent>();
                player.GetComponent<NavMeshAgent>().enabled = false;
                PrefabUtility.SaveAsPrefabAsset(player, "Assets/_Project/Prefabs/Player.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            RegisterTeammates();
            AssetDatabase.CopyAsset("Assets/Scenes/SampleScene.unity", ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = new GameObject("AI Test");
            var harness = root.AddComponent<AITestController>();
            var cameraObject = new GameObject("AI Spectator Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 18f, -16f), Quaternion.Euler(48f, 0f, 0f));
            var camera = cameraObject.GetComponent<Camera>();
            camera.farClipPlane = 1500f;
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (other != camera) other.gameObject.SetActive(false);
            var data = new SerializedObject(harness);
            data.FindProperty("m_localBootstrap").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/SoloNetworkBootstrap.prefab");
            data.FindProperty("m_generator").objectReferenceValue = Object.FindFirstObjectByType<NeighbourhoodGenerator>();
            data.FindProperty("m_deliveryManager").objectReferenceValue = Object.FindFirstObjectByType<DeliveryManager>();
            data.FindProperty("m_spectatorCamera").objectReferenceValue = camera;
            var team = data.FindProperty("m_teammatePrefabs");
            team.arraySize = 4;
            for (int i = 0; i < 4; i++) team.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("AITest scene, four editable role assets, and saved teammate prefab variants created.");
        }

        [MenuItem("Couch Guys/AI Test/Register Teammates")]
        public static void RegisterTeammates()
        {
            var registry = AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(
                "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            var data = new SerializedObject(registry);
            var list = data.FindProperty("_prefabs");
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/AI" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)).GetComponent<NetworkObject>();
                bool found = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == prefab) found = true;
                if (!found)
                {
                    int index = list.arraySize++;
                    list.GetArrayElementAtIndex(index).objectReferenceValue = prefab;
                }
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Couch Guys/AI Test/Play And Record %#F9")]
        public static void PlayAndRecord()
        {
            if (EditorApplication.isPlaying) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath);
            }
            File.WriteAllText("Logs/AITestValidation.txt", "AITest runtime observations\n");
            File.WriteAllText("Logs/AITestStates.txt", "AITest state transitions\n");
            s_recordedStates.Clear();
            SessionState.SetBool("AITest.Recording", true);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Couch Guys/AI Test/Add Depot Entrance Link")]
        public static void AddDepotEntranceLink()
        {
            const string path = "Assets/_Project/Prefabs/World/depot_02.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var depot = root.GetComponentInChildren<CouchGuys.Gameplay.Depot.DepotController>(true);
                var data = new SerializedObject(depot);
                var doorway = data.FindProperty("m_vehicleGarageDoor").FindPropertyRelative("m_safetyVolume").objectReferenceValue as BoxCollider;
                if (doorway == null) throw new System.InvalidOperationException("Depot entrance safety volume is required.");
                var existing = root.transform.Find("Teammate Entrance Navigation Link");
                var child = existing != null ? existing.gameObject : new GameObject("Teammate Entrance Navigation Link");
                child.transform.SetParent(root.transform, false);
                Vector3 centre = doorway.transform.TransformPoint(doorway.center);
                centre.y = root.transform.position.y + 0.1f;
                child.transform.SetPositionAndRotation(centre, root.transform.rotation);
                var link = child.GetComponent<Unity.AI.Navigation.NavMeshLink>();
                if (link == null) link = child.AddComponent<Unity.AI.Navigation.NavMeshLink>();
                link.startPoint = Vector3.back * 2.5f;
                link.endPoint = Vector3.forward * 2.5f;
                link.width = 4f;
                link.bidirectional = true;
                link.autoUpdate = true;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.SaveAssets();
                Debug.Log($"Saved depot entrance navigation link at {centre}.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static UnityEditor.TestTools.TestRunner.Api.TestRunnerApi s_runner;
        public static void RunChecks()
        {
            s_runner = ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>();
            s_runner.RegisterCallbacks(new CheckResults());
            s_runner.Execute(new UnityEditor.TestTools.TestRunner.Api.ExecutionSettings(
                new UnityEditor.TestTools.TestRunner.Api.Filter
                {
                    testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                    testNames = new[] { "CouchGuys.Tests.Editor.AITeammateRoleTests", "CouchGuys.Tests.Editor.MultipleDeliveryTests" }
                }));
        }
        private sealed class CheckResults : UnityEditor.TestTools.TestRunner.Api.ICallbacks
        {
            public void RunStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor tests) { }
            public void TestStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test) { }
            public void TestFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result) { }
            public void RunFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
            {
                UnityEditor.TestTools.TestRunner.Api.TestRunnerApi.SaveResultToFile(result, "Logs/AITestChecks.xml");
                Debug.Log($"AI and stacked-delivery checks: {result.PassCount} passed, {result.FailCount} failed.");
                Object.DestroyImmediate(s_runner);
            }
        }

        [InitializeOnLoadMethod]
        private static void RegisterRecorder() => EditorApplication.update += Record;
        private static double s_nextRecord;
        private static double s_nextStates;
        private static readonly System.Collections.Generic.Dictionary<int, DebugCouchBotController.AiState> s_recordedStates = new();
        // Explicit local command entry point for automated Editor validation; never rebuild on import.
        private static void ProcessCommand()
        {
            const string request = ".utmp/AITestCommand.txt";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            if (command == "create") CreateScene();
            else if (command == "play") { SessionState.SetBool("AITest.AllowRemoteValidation", false); PlayAndRecord(); }
            else if (command == "play-network") { SessionState.SetBool("AITest.AllowRemoteValidation", true); PlayAndRecord(); }
            else if (command == "register") RegisterTeammates();
            else if (command == "entrance-link") AddDepotEntranceLink();
            else if (command == "stop") { EditorApplication.isPlaying = false; SessionState.SetBool("AITest.AllowRemoteValidation", false); }
            else if (command == "down-carrier" && EditorApplication.isPlaying)
            {
                var carrier = Object.FindObjectsByType<DebugCouchBotController>(FindObjectsSortMode.None)
                    .FirstOrDefault(bot => bot.RoleSettings != null && bot.RoleSettings.Role == AITeammateRole.Carrier);
                carrier?.GetComponent<PlayerHealth>().ApplyDamageServer(1000f, Vector3.forward, 0f);
            }
            else if (command == "visuals" && EditorApplication.isPlaying)
            {
                var generator = Object.FindFirstObjectByType<NeighbourhoodGenerator>();
                var network = FishNet.InstanceFinder.NetworkManager;
                var connection = network.ServerManager.Clients.Values.FirstOrDefault(c => c.ClientId == network.ClientManager.Connection.ClientId);
                var renderers = generator.GetComponentsInChildren<Renderer>(true);
                var terrain = generator.GeneratedTerrain.GetComponent<Renderer>();
                bool enrolled = connection != null && connection.Scenes.Contains(generator.gameObject.scene);
                int visible = renderers.Count(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy);
                bool terrainVisible = terrain.enabled && !terrain.forceRenderingOff && terrain.gameObject.activeInHierarchy;
                File.WriteAllText("Logs/AITestVisuals.txt", $"enrolled={enrolled} renderers={renderers.Length} visible={visible} terrainVisible={terrainVisible}\n" +
                    (enrolled && terrainVisible && visible > 0 ? "PASS" : "FAIL"));
            }
            else if (command == "tests" && !EditorApplication.isPlaying) RunChecks();
            else if (command == "role-probes" && EditorApplication.isPlaying) AITestRoleProbes.Run();
            else if (command == "build-client" && !EditorApplication.isPlaying)
            {
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = "Builds/AITestValidation/AITestClient.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                File.WriteAllText("Logs/AITestClientBuild.txt", result.summary.result + " errors=" + result.summary.totalErrors);
            }
        }
        private static void Record()
        {
            ProcessCommand();
            if (!SessionState.GetBool("AITest.Recording", false)) return;
            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool("AITest.Recording", false);
                return;
            }
            if (EditorApplication.timeSinceStartup >= s_nextStates)
            {
                s_nextStates = EditorApplication.timeSinceStartup + 0.2;
                foreach (var bot in Object.FindObjectsByType<DebugCouchBotController>(FindObjectsSortMode.None))
                {
                    if (s_recordedStates.TryGetValue(bot.GetInstanceID(), out var previous) && previous == bot.State) continue;
                    s_recordedStates[bot.GetInstanceID()] = bot.State;
                    File.AppendAllText("Logs/AITestStates.txt", $"t={Time.time:F2} {bot.RoleSettings?.Role} {bot.State} health={bot.GetComponent<PlayerHealth>().CurrentHealth}\n");
                }
            }
            if (EditorApplication.timeSinceStartup < s_nextRecord) return;
            s_nextRecord = EditorApplication.timeSinceStartup + 5;
            var manager = Object.FindFirstObjectByType<DeliveryManager>();
            var bots = Object.FindObjectsByType<DebugCouchBotController>(FindObjectsSortMode.None);
            string text = $"t={Time.time:F1} bots={bots.Length} delivery={manager?.State} attempt={manager?.AttemptNumber} currency={manager?.CurrentCurrency}\n";
            foreach (var bot in bots)
            {
                text += $"  {bot.name} {bot.RoleSettings?.Role} {bot.State} pos={bot.transform.position} health={bot.GetComponent<PlayerHealth>().CurrentHealth}\n";
                var field = typeof(DebugCouchBotController).GetField("m_deliveryPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var path = field?.GetValue(bot) as NavMeshPath;
                text += $"    route={path?.status} corners={string.Join(";", path?.corners ?? new Vector3[0])} intent={bot.GetComponent<PlayerCouchCarrier>().GetType().GetMethod("GetServerMovementIntent")?.Invoke(bot.GetComponent<PlayerCouchCarrier>(), null)}\n";
            }
            if (manager != null && manager.HasActiveDelivery)
                text += $"  job={manager.Deliveries[0].Id} couch={manager.ActiveCouch?.transform.position} destination={manager.ActiveDestination?.DropPosition?.position}\n";
            File.AppendAllText("Logs/AITestValidation.txt", text);
        }
    }
}
