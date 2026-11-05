using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Depot;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    public static class DispatcherRoamingSetup
    {
        private const string NpcPath = "Assets/_Project/Prefabs/DeliveryNPC.prefab";
        private static readonly string[] DepotPaths = {
            "Assets/_Project/Prefabs/World/CouchGuysDepotV2.prefab",
            "Assets/_Project/Prefabs/World/depot_02.prefab"
        };

        [MenuItem("Couch Guys/Dispatcher/Configure Roaming %#F11")]
        public static void Configure()
        {
            if (EditorApplication.isPlaying) { InspectRuntime(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var npc = PrefabUtility.LoadPrefabContents(NpcPath);
            try
            {
                if (npc.GetComponent<DispatcherRoaming>() == null)
                {
                    npc.AddComponent<DispatcherRoaming>();
                    PrefabUtility.SaveAsPrefabAsset(npc, NpcPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(npc); }
            for (int variant = 0; variant < DepotPaths.Length; variant++)
            {
                var root = PrefabUtility.LoadPrefabContents(DepotPaths[variant]);
                try
                {
                    // Explicit authoring only. Existing waypoint customizations are preserved.
                    if (root.GetComponent<DispatcherRoamingArea>() != null) continue;
                    var area = root.AddComponent<DispatcherRoamingArea>();
                    var markers = new GameObject("DispatcherRoamingWaypoints").transform;
                    markers.SetParent(root.transform, false);
                    float y = root.transform.InverseTransformPoint(root.GetComponent<DepotController>().DeliveryNpcMarker.position).y;
                    Vector3[] positions = variant == 0
                        ? new[] { new Vector3(-10f,y,2.3f), new Vector3(-8.5f,y,3.5f), new Vector3(-10.6f,y,4.8f) }
                        : new[] { new Vector3(-6.6f,y,-.8f), new Vector3(-3.5f,y,1.5f), new Vector3(-7f,y,2.5f) };
                    var serialized = new SerializedObject(area);
                    var points = serialized.FindProperty("m_waypoints"); points.arraySize = positions.Length;
                    for (int i = 0; i < positions.Length; i++)
                    {
                        var point = new GameObject($"SafeRoamingPoint_{i + 1}").transform;
                        point.SetParent(markers, false); point.localPosition = positions[i];
                        points.GetArrayElementAtIndex(i).objectReferenceValue = point;
                    }
                    serialized.FindProperty("m_interior").boundsValue = variant == 0
                        ? new Bounds(new Vector3(-9.775f, .5f, 2.65f), new Vector3(5.25f,4f,6.3f))
                        : new Bounds(new Vector3(-5.15f, .5f, .75f), new Vector3(7.7f,4f,8.5f));
                    var exclusions = serialized.FindProperty("m_exclusions"); exclusions.arraySize = 2;
                    exclusions.GetArrayElementAtIndex(0).boundsValue = new Bounds(new Vector3(7.5f,1f,0f), new Vector3(12f,8f,30f));
                    exclusions.GetArrayElementAtIndex(1).boundsValue = new Bounds(new Vector3(0f,1f,10.5f), new Vector3(40f,8f,6f));
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, DepotPaths[variant]);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Validate();
        }

        public static void InspectRuntime()
        {
            var lines = new List<string> { $"Playing={EditorApplication.isPlaying}, time={Time.time}, scale={Time.timeScale}" };
            foreach(var roaming in Object.FindObjectsByType<DispatcherRoaming>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                var network=Get<NeighbourhoodSeedSynchroniser>(roaming,"m_network");
                var generator=Get<NeighbourhoodGenerator>(roaming,"m_generator");
                var area=Get<DispatcherRoamingArea>(roaming,"m_area");
                var home=Get<Transform>(roaming,"m_home");
                lines.Add($"NPC={roaming.name}, enabled={roaming.isActiveAndEnabled}, state={roaming.State}, started={Get<bool>(roaming,"m_started")}, network={network}, server={network!=null && network.IsServerInitialized}, client={network!=null && network.IsClientInitialized}, ready={generator!=null && generator.IsGenerationReady}, area={area}, home={home}, wait={Get<float>(roaming,"m_waitUntil")-Time.time}, position={roaming.transform.position}");
                if(area==null)continue;
                for(int i=0;i<area.WaypointCount;i++)
                {
                    var p=area.GetWaypoint(i).position;
                    bool clear=(bool)typeof(DispatcherRoaming).GetMethod("PositionClear",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(roaming,new object[]{p});
                    bool floor=(bool)typeof(DispatcherRoaming).GetMethod("HasFloor",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(roaming,new object[]{p});
                    lines.Add($"Point{i}={p}, bounds={area.AllowsSegment(roaming.transform.position,p,.65f)}, travel={roaming.CanTravel(roaming.transform.position,p)}, clear={clear}, floor={floor}");
                    var delta=p-roaming.transform.position;
                    foreach(var hit in Physics.BoxCastAll(roaming.transform.position+Vector3.up*1.15f,new Vector3(.65f,1.1f,.65f),delta.normalized,Quaternion.identity,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                        if(!hit.collider.transform.IsChildOf(roaming.transform))lines.Add("Hit="+hit.collider.name+", root="+hit.collider.transform.root.name+", distance="+hit.distance);
                    foreach(var c in Physics.OverlapBox(p+Vector3.up*1.15f,new Vector3(.65f,1.1f,.65f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore))
                        if(!c.transform.IsChildOf(roaming.transform))lines.Add("Overlap="+c.name+", root="+c.transform.root.name);
                    if(Physics.Raycast(p+Vector3.up*.3f,Vector3.down,out var ground,.6f,~0,QueryTriggerInteraction.Ignore))lines.Add($"Floor={ground.collider.name}, normal={ground.normal}");
                }
            }
            if(!lines.Any(l=>l.StartsWith("NPC=")))lines.Add("No live DispatcherRoaming components found.");
            File.WriteAllLines("Tools/DispatcherRoamingRuntime.txt",lines);
            Debug.Log(string.Join("\n",lines));
        }

        [MenuItem("Couch Guys/Dispatcher/Test Live Roaming %#F8")]
        public static void TestLiveRoaming()
        {
            if(!EditorApplication.isPlaying)return;
            var menu=Object.FindFirstObjectByType<CouchGuys.UI.MainMenuManager>();
            if(menu!=null) { menu.StartSolo(); return; }
            InspectRuntime();
            foreach(var roaming in Object.FindObjectsByType<DispatcherRoaming>(FindObjectsSortMode.None))
            {
                var network=Get<NeighbourhoodSeedSynchroniser>(roaming,"m_network");
                if(network!=null && network.IsServerInitialized && roaming.State==DispatcherRoaming.RoamingState.Idle)
                    Set(roaming,"m_waitUntil",Time.time);
            }
        }

        private static void SetDialogueSession(DeliveryManager manager, DeliveryNPC npc, CouchGuys.Player.PlayerCouchCarrier player)
        {
            var sessions = (System.Collections.IDictionary)typeof(DeliveryManager).GetField("m_dialogues", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            sessions.Clear();
            if (player == null) return;
            var type = typeof(DeliveryManager).GetNestedType("DispatcherSession", BindingFlags.NonPublic);
            var session = System.Activator.CreateInstance(type);
            type.GetField("Npc").SetValue(session, npc);
            sessions.Add(player, session);
        }

        private static void Set(object instance, string field, object value) =>
            instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        private static T Get<T>(object instance, string field) =>
            (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void Tick(DispatcherRoaming roaming)
        {
            typeof(DispatcherRoaming).GetMethod("TickServer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(roaming, new object[] { .1f });
            Physics.SyncTransforms();
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        [MenuItem("Couch Guys/Dispatcher/Validate Roaming")]
        public static void Validate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var report = new List<string>();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                foreach (string path in DepotPaths)
                {
                    var depotObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                    depotObject.transform.position = new Vector3(10000f,0f,10000f);
                    var area = depotObject.GetComponent<DispatcherRoamingArea>();
                    var depot = depotObject.GetComponent<DepotController>();
                    Transform home = depot.DeliveryNpcMarker;
                    var actor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NpcPath), scene);
                    actor.transform.SetPositionAndRotation(home.position, home.rotation);
                    var roaming = actor.GetComponent<DispatcherRoaming>();
                    var managerObject = new GameObject("DispatcherTestManager");
                    SceneManager.MoveGameObjectToScene(managerObject, scene);
                    var manager = managerObject.AddComponent<DeliveryManager>();
                    actor.transform.SetParent(home,true);
                    actor.GetComponent<DeliveryNPC>().SetDeliveryManager(manager);
                    Set(roaming,"m_area",null); Set(roaming,"m_home",null);
                    typeof(DispatcherRoaming).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(roaming,null);
                    Check(Get<Transform>(roaming,"m_home")==home && Get<DispatcherRoamingArea>(roaming,"m_area")==area,
                        "Reload did not restore dispatcher references.");
                    Set(roaming,"m_area",area); Set(roaming,"m_home",home);
                    Set(roaming,"m_manager",manager); Set(roaming,"m_npc",actor.GetComponent<DeliveryNPC>());
                    Physics.SyncTransforms();
                    Check(area != null && area.WaypointCount > 0, "Saved waypoint references missing.");
                    Check(!area.AllowsSegment(home.position,depot.VehicleArrival.position,.65f), "Truck area is reachable.");
                    Check(!area.AllowsSegment(home.position,depotObject.transform.TransformPoint(0f,0f,100f),.65f), "Exterior is reachable.");
                    var nodes = new List<Vector3> {home.position};
                    for (int i = 0; i < area.WaypointCount; i++) nodes.Add(area.GetWaypoint(i).position);
                    Check((bool)typeof(DispatcherRoaming).GetMethod("FindRoutes",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(roaming,null),"Route graph unavailable.");
                    int[] parents = Get<int[]>(roaming,"m_parents");
                    Check(Enumerable.Range(0,area.WaypointCount).Any(i=>parents[i+2]>=0),"No collider-safe departure from the saved home.");
                    if (path.EndsWith("depot_02.prefab"))
                    {
                        Check(!roaming.CanTravel(home.position,area.GetWaypoint(0).position),"Direct path incorrectly passes through the desk.");
                        Check(parents[2]>=0,"Desk detour failed to reach the front aisle.");
                    }
                    var playerAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab").GetComponent<CouchGuys.Player.PlayerCouchCarrier>();
                    float scheduled=Time.time+60f;
                    Set(roaming,"m_waitUntil",scheduled);
                    SetDialogueSession(manager, actor.GetComponent<DeliveryNPC>(), playerAsset);
                    Tick(roaming);
                    SetDialogueSession(manager, null, null); Tick(roaming);
                    Check(Get<float>(roaming,"m_waitUntil")==scheduled,"Parked conversation reset the roaming countdown.");
                    var temporaryBlocks=new List<GameObject>();
                    for(int i=0;i<area.WaypointCount;i++)
                    {
                        var block=new GameObject("TemporaryWaypointBlocker"); SceneManager.MoveGameObjectToScene(block,scene);
                        block.transform.position=area.GetWaypoint(i).position+Vector3.up;
                        block.AddComponent<BoxCollider>().size=Vector3.one; temporaryBlocks.Add(block);
                    }
                    Physics.SyncTransforms(); Set(roaming,"m_waitUntil",-1f); Tick(roaming);
                    Check(roaming.State==DispatcherRoaming.RoamingState.Idle &&
                        Mathf.Abs(Get<float>(roaming,"m_waitUntil")-Time.time-5f)<.1f,"Blocked destinations restarted the long idle interval.");
                    foreach(var block in temporaryBlocks)Object.DestroyImmediate(block);
                    Physics.SyncTransforms();
                    Set(roaming,"m_waitUntil",-1f); Tick(roaming);
                    Check(roaming.State == DispatcherRoaming.RoamingState.Roaming,"No safe destination was selected.");
                    Set(roaming,"m_roamUntil",Time.time+1000f);
                    for (int i=0; i<1000 && Get<int>(roaming,"m_visits")==0; i++) Tick(roaming);
                    Check(Get<int>(roaming,"m_visits")==1,"Dispatcher failed to reach a waypoint.");
                    for (int i=0; i<6000 && Get<int>(roaming,"m_visits")<6; i++)
                    {
                        Set(roaming,"m_pauseUntil",-1f);
                        Tick(roaming);
                    }
                    Check(Get<int>(roaming,"m_visits")>=6,"Dispatcher stopped after the old three-visit cap.");
                    Check(Get<int>(roaming,"m_trailCount")<=area.WaypointCount+1,"Return trail grew beyond its bounded capacity.");
                    SetDialogueSession(manager, actor.GetComponent<DeliveryNPC>(), playerAsset);
                    Vector3 stopped=actor.transform.position; Tick(roaming);
                    Check(actor.transform.position==stopped && Get<float>(roaming,"m_speed")==0f,"Interaction did not pause movement.");
                    SetDialogueSession(manager, null, null); Tick(roaming);
                    Check(roaming.State==DispatcherRoaming.RoamingState.Returning,"Interaction release did not force return.");
                    for(int i=0;i<1500 && roaming.State!=DispatcherRoaming.RoamingState.Idle;i++) Tick(roaming);
                    Check(roaming.State==DispatcherRoaming.RoamingState.Idle && Vector3.Distance(actor.transform.position,home.position)<.001f &&
                        Quaternion.Angle(actor.transform.rotation,home.rotation)<.01f,"Return failed to restore exact home pose.");
                    Check(Get<float>(roaming,"m_waitUntil")-Time.time>=Get<Vector2>(roaming,"m_roamingInterval").x-.1f,"Next cycle did not respect idle interval.");
                    // Deadline path and blocked-route recovery, without waiting on wall-clock timers.
                    Set(roaming,"m_waitUntil",-1f); Tick(roaming);
                    Set(roaming,"m_roamUntil",Time.time+1000f);
                    for(int i=0;i<1000 && Get<int>(roaming,"m_visits")==0;i++) Tick(roaming);
                    Set(roaming,"m_roamUntil",-1f); Tick(roaming); Tick(roaming);
                    Check(roaming.State==DispatcherRoaming.RoamingState.Returning,"Roaming deadline did not start return.");
                    var blocker=new GameObject("TestDynamicBlocker"); SceneManager.MoveGameObjectToScene(blocker,scene);
                    blocker.transform.position=actor.transform.position+Vector3.up;
                    blocker.AddComponent<BoxCollider>().size=new Vector3(.2f,1f,.2f);
                    Set(roaming,"m_blockedAt",Time.time); Set(roaming,"m_blockedReturnTimeout",0f);
                    Set(roaming,"m_nextRecovery",-1f); Physics.SyncTransforms();
                    Tick(roaming);
                    Vector3 blockedPosition=actor.transform.position;
                    Tick(roaming);
                    Check(roaming.State==DispatcherRoaming.RoamingState.Returning && actor.transform.position==blockedPosition,
                        "Blocked return teleported or crossed a collider.");
                    Object.DestroyImmediate(blocker);
                    Physics.SyncTransforms();
                    for(int i=0;i<3000 && roaming.State!=DispatcherRoaming.RoamingState.Idle;i++) Tick(roaming);
                    Check(roaming.State==DispatcherRoaming.RoamingState.Idle,"Return did not recover after clearing the collider.");
                    report.Add("PASS "+path+": saved home has a collider-safe waypoint route; desk detour checked for depot_02; truck/exterior rejected; six destination visits; bounded trail; dialogue pause/release; 5-10 second idle interval; deadline return; blocked return waits without teleporting and recovers after obstruction removal.");
                    Object.DestroyImmediate(actor); Object.DestroyImmediate(managerObject); Object.DestroyImmediate(depotObject);
                }
                report.Add("Networking: existing NeighbourhoodSeedSynchroniser server-only pose SyncVar, 10 Hz while moving, seed/region guards, late-join retained state and client interpolation. Multi-process playtest still required.");
                File.WriteAllLines("Tools/DispatcherRoamingValidation.txt",report);
                Debug.Log(string.Join("\n",report));
            }
            finally
            {
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene,true);
            }
        }

        [MenuItem("Couch Guys/Dispatcher/Inspect Roaming Layout")]
        public static void Inspect()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var lines = new List<string>();
            var loaded = SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
            bool opened = !loaded.isLoaded;
            if (opened) loaded = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            try
            {
                foreach (var manager in loaded.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DeliveryManager>(true)))
                {
                    lines.Add("Manager=" + manager.name);
                    var generator = new SerializedObject(manager.GetComponent<NeighbourhoodGenerator>());
                    foreach (string key in new[] { "m_startingAreaPrefab", "m_selectedRegion" })
                        lines.Add(key + "=" + AssetDatabase.GetAssetPath(generator.FindProperty(key)?.objectReferenceValue));
                }
            }
            finally { if (opened) EditorSceneManager.CloseScene(loaded, true); }
            foreach (string path in new[] { "Assets/_Project/Prefabs/World/CouchGuysDepotV2.prefab", "Assets/_Project/Prefabs/World/depot_02.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    lines.Add("DEPOT=" + path);
                    var depot = root.GetComponent<DepotController>();
                    lines.Add("Home=" + root.transform.InverseTransformPoint(depot.DeliveryNpcMarker.position));
                    lines.Add("Truck=" + root.transform.InverseTransformPoint(depot.VehicleArrival.position));
                    foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    {
                        if (!collider.enabled || collider.isTrigger) continue;
                        var b=collider.bounds;
                        lines.Add(collider.name + " center=" + root.transform.InverseTransformPoint(b.center).ToString("F2") + " size=" + b.size.ToString("F2"));
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Directory.CreateDirectory("Tools");
            File.WriteAllLines("Tools/DispatcherRoamingLayout.txt", lines);
            Debug.Log("Dispatcher roaming layout written.");
        }
    }
}
