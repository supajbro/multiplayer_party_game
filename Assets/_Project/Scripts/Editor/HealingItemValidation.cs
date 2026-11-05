using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Healing;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Player;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    // Explicit local FishNet host integration test. Never runs on import or normal Play.
    [InitializeOnLoad]
    public static class HealingItemValidation
    {
        private const string Pending = "HealingValidation.Pending", Result = "Temp/HealingItemPlayResults.txt";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static IEnumerator s_routine;
        private static NetworkManager s_network, s_remote;
        static HealingItemValidation() { EditorApplication.playModeStateChanged += StateChanged; }
        [MenuItem("Couch Guys/Healing Items/Test Local Host %#&F3")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            File.WriteAllText(Result, "START: healing item FishNet host integration checks\n");
            SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending,false))
            { SessionState.SetBool(Pending,false); s_routine = Probe(); EditorApplication.update += Tick; }
            if (state == PlayModeStateChange.ExitingPlayMode) { EditorApplication.update -= Tick; s_routine = null; }
        }
        private static void Tick()
        {
            try { if (s_routine != null && !s_routine.MoveNext()) Finish(); }
            catch (Exception e) { File.AppendAllText(Result,"FAIL: "+e+"\n"); Finish(); }
        }
        private static void Finish()
        {
            EditorApplication.update -= Tick; s_routine = null;
            if (s_remote != null) s_remote.ClientManager.StopConnection();
            if (s_network != null) { s_network.ClientManager.StopConnection(); s_network.ServerManager.StopConnection(true); }
            EditorApplication.ExitPlaymode();
        }
        private static void Check(bool condition,string message)
        { if (!condition) throw new Exception(message); File.AppendAllText(Result,"PASS: "+message+"\n"); }
        private static void SetSync<T>(object target,string name,T value) => ((SyncVar<T>)target.GetType().GetField(name,Fields).GetValue(target)).Value = value;
        private static IEnumerator Probe()
        {
            for (int i=0;i<SceneManager.sceneCount;i++) foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            foreach (var old in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var bootstrap = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("64e03b333ee845f5bff4d89fa9a228c4")));
            s_network = bootstrap.GetComponentInChildren<NetworkManager>();
            var spawner = s_network.GetComponent<CouchGuys.Networking.NeighbourhoodPlayerSpawner>(); if (spawner != null) Object.DestroyImmediate(spawner);
            yield return null;
            s_network.ServerManager.StartConnection(); s_network.ClientManager.StartConnection();
            float timeout=Time.realtimeSinceStartup+15;
            while (s_network.ServerManager.Clients.Count==0 && Time.realtimeSinceStartup<timeout) yield return null;
            Check(s_network.ServerManager.Clients.Count>0,"FishNet host connected");
            var owner=s_network.ServerManager.Clients.Values.First();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=Vector3.down*.5f; floor.transform.localScale=new Vector3(100,1,100);
            var source=AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Player.prefab");
            var player=Object.Instantiate(source,Vector3.up*.05f,Quaternion.identity); s_network.ServerManager.Spawn(player,owner);
            var rival=Object.Instantiate(source,Vector3.right,Quaternion.identity); s_network.ServerManager.Spawn(rival,owner);
            var inventory=player.GetComponent<PlayerWeaponController>(); var health=player.GetComponent<PlayerHealth>(); var healing=player.GetComponent<HealingItem>();
            // Freeze movement to keep interaction-distance checks deterministic.
            player.GetComponent<ThirdPersonPlayerController>().enabled=false;
            rival.GetComponent<ThirdPersonPlayerController>().enabled=false;
            player.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled=false;
            rival.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled=false;
            var deliveryRoot=new GameObject("Healing shop test"); deliveryRoot.SetActive(false);
            var manager=deliveryRoot.AddComponent<DeliveryManager>();
            deliveryRoot.GetComponent<CouchGuys.ProceduralGeneration.NeighbourhoodGenerator>().enabled=false; manager.enabled=false;
            var networkData=new SerializedObject(deliveryRoot.GetComponent<NetworkObject>()); networkData.FindProperty("SceneId").ulongValue=43; networkData.ApplyModifiedPropertiesWithoutUndo();
            var managerData=new SerializedObject(manager);
            managerData.FindProperty("_networkObjectCache").objectReferenceValue=deliveryRoot.GetComponent<NetworkObject>();
            managerData.FindProperty("_addedNetworkObject").objectReferenceValue=deliveryRoot.GetComponent<NetworkObject>();
            managerData.FindProperty("m_shopCatalogue").objectReferenceValue=AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
            managerData.ApplyModifiedPropertiesWithoutUndo(); deliveryRoot.SetActive(true);
            // This follows the existing gameplay probes' scene-object setup; spawn supplies authority.
            s_network.ServerManager.Spawn(deliveryRoot.GetComponent<NetworkObject>());
            var npc=new GameObject("Test dispatcher").AddComponent<DeliveryNPC>(); npc.SetDeliveryManager(manager);
            var carrier=player.GetComponent<PlayerCouchCarrier>();
            SetSync(manager,"m_currency",10000);
            Check(manager.TryBeginDialogueServer(npc,carrier),"Dispatcher dialogue opened");
            var catalogue=HealingItemCatalogue.Load();
            Check(catalogue != null && catalogue.Items.Length==3,"Three healing settings load from configuration");
            var shop=AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
            uint request=1;
            foreach (var settings in catalogue.Items)
            {
                int id=catalogue.IdOf(settings), before=manager.CurrentCurrency;
                Check(settings.PickupPrefab != null && settings.PickupPrefab.HealingSettings==settings && settings.Icon != null && settings.UseEffectPrefab != null,"Configured pickup/icon/effects: "+settings.DisplayName);
                Check(((FishNet.Managing.Object.SinglePrefabObjects)s_network.SpawnablePrefabs).Prefabs.Contains(settings.PickupPrefab.NetworkObject),"Pickup registered for multiplayer: "+settings.DisplayName);
                Check(shop.Find(settings.name).Price==settings.PurchasePrice,"Shop price follows settings: "+settings.DisplayName);
                Check(manager.TryPurchaseShopItemServer(carrier,settings.name,request),"Purchased into inventory: "+settings.DisplayName);
                Check(manager.CurrentCurrency==before-settings.PurchasePrice,"Existing currency charged once");
                Check(!manager.TryPurchaseShopItemServer(carrier,settings.name,request++),"Duplicate purchase request rejected");
                int slot=Enumerable.Range(0,5).First(s=>inventory.GetInventoryWeaponIndex(s)==id);
                typeof(PlayerWeaponController).GetMethod("EquipInventorySlotServer",Fields).Invoke(inventory,new object[]{slot});
                float switchAt=Time.time+.25f; while(Time.time<switchAt) yield return null;
                Check(!healing.TryBeginUseServer() && inventory.GetInventoryWeaponIndex(slot)==id,"Full-health use preserves item");
                // Non-default maximum health proves the heal uses max health rather than a fixed amount.
                typeof(PlayerHealth).GetField("m_maxHealth",Fields).SetValue(health,200f); SetSync(health,"m_currentHealth",20f);
                healing.RequestUseServerRpc();
                float requestDeadline = Time.time + .75f;
                while (!healing.IsUsing && Time.time < requestDeadline) yield return null;
                Check(healing.IsUsing,"Owner RPC starts timed healing");
                float effectDeadline = Time.time + .5f;
                while (typeof(HealingItem).GetField("m_useEffect",Fields).GetValue(healing) as GameObject == null && Time.time < effectDeadline) yield return null;
                Check(typeof(HealingItem).GetField("m_useEffect",Fields).GetValue(healing) as GameObject != null,
                    "Observer client receives healing animation/effect state");
                Check(!healing.TryBeginUseServer(),"Repeated use rejected while busy");
                float end=Time.time+settings.UseDuration+.4f; while(Time.time<end) yield return null;
                Check(Mathf.Abs(health.CurrentHealth-Mathf.Min(200,20+2*settings.HealingPercentage))<.01f,"Maximum-health percentage applied: "+settings.DisplayName);
                Check(inventory.GetInventoryWeaponIndex(slot)==-1 && !healing.IsUsing,"Exactly one item consumed after duration");
                SetSync(health,"m_currentHealth",200f);
                var pickup=WeaponPickup.SpawnHealingServer(settings,player.transform.position+Vector3.forward*.7f,Quaternion.identity,Vector3.zero);
                Check(pickup!=null && pickup.IsSpawned,"Network world pickup spawned");
                Check(pickup.TryPickupServer(inventory),"Existing pickup system grants healing item");
                Check(!pickup.TryPickupServer(rival.GetComponent<PlayerWeaponController>()),"Second player cannot claim the same pickup");
                slot=Enumerable.Range(0,5).First(s=>inventory.GetInventoryWeaponIndex(s)==id);
                typeof(PlayerWeaponController).GetMethod("EquipInventorySlotServer",Fields).Invoke(inventory,new object[]{slot});
                switchAt=Time.time+.25f; while(Time.time<switchAt) yield return null;
                SetSync(health,"m_currentHealth",199f); Check(healing.TryBeginUseServer(),"Partial-health use starts");
                end=Time.time+settings.UseDuration+.3f; while(Time.time<end) yield return null;
                Check(health.CurrentHealth==200,"Healing capped at maximum");
            }
            // A separate client-only manager exercises actual packet serialization and remote replicas.
            var remoteRoot=new GameObject("Healing remote client"); remoteRoot.SetActive(false);
            s_remote=remoteRoot.AddComponent<NetworkManager>();
            typeof(NetworkManager).GetField("_persistence",Fields).SetValue(s_remote,NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad",Fields).SetValue(s_remote,false);
            s_remote.SpawnablePrefabs=s_network.SpawnablePrefabs;
            remoteRoot.SetActive(true); yield return null;
            s_remote.TransportManager.Transport.SetClientAddress("127.0.0.1");
            s_remote.TransportManager.Transport.SetPort(s_network.TransportManager.Transport.GetPort());
            s_remote.ClientManager.StartConnection();
            timeout=Time.realtimeSinceStartup+10;
            while ((!s_remote.IsClientStarted || s_network.ServerManager.Clients.Count<2) && Time.realtimeSinceStartup<timeout) yield return null;
            Check(s_remote.IsClientOnlyStarted && s_network.ServerManager.Clients.Count==2,"Independent client-only network connection established");
            var remoteOwner=s_network.ServerManager.Clients.Values.First(c=>c.ClientId!=owner.ClientId);
            s_network.SceneManager.AddConnectionToScene(remoteOwner,player.gameObject.scene);
            var remotePlayer=Object.Instantiate(source,new Vector3(-4,.05f,0),Quaternion.identity);
            s_network.ServerManager.Spawn(remotePlayer,remoteOwner);
            remotePlayer.GetComponent<ThirdPersonPlayerController>().enabled=false;
            timeout=Time.realtimeSinceStartup+5;
            while(!s_remote.ClientManager.Objects.Spawned.ContainsKey(remotePlayer.ObjectId) && Time.realtimeSinceStartup<timeout) yield return null;
            Check(s_remote.ClientManager.Objects.Spawned.ContainsKey(remotePlayer.ObjectId),"Remote player replica spawned from saved prefab");
            var replica=s_remote.ClientManager.Objects.Spawned[remotePlayer.ObjectId];
            replica.GetComponent<ThirdPersonPlayerController>().enabled=false;
            replica.GetComponent<CouchGuys.Input.PlayerInputReader>().enabled=false;
            var remoteInventory=remotePlayer.GetComponent<PlayerWeaponController>();
            var remoteHealth=remotePlayer.GetComponent<PlayerHealth>();
            foreach(var settings in catalogue.Items)
            {
                SetSync(remoteHealth,"m_currentHealth",10f);
                Check(remoteInventory.AcquireHealingItemServer(settings),"Server grants remote player's item: "+settings.DisplayName);
                timeout=Time.realtimeSinceStartup+3;
                while(replica.GetComponent<PlayerWeaponController>().EquippedHealingItem!=settings && Time.realtimeSinceStartup<timeout) yield return null;
                float readyAt=Time.time+.3f; while(Time.time<readyAt) yield return null;
                replica.GetComponent<HealingItem>().RequestUseServerRpc();
                timeout=Time.realtimeSinceStartup+2;
                while(!replica.GetComponent<HealingItem>().IsUsing && Time.realtimeSinceStartup<timeout) yield return null;
                Check(replica.GetComponent<HealingItem>().IsUsing,"Remote owner request and replicated use state accepted");
                timeout=Time.realtimeSinceStartup+settings.UseDuration+2;
                float expected=Mathf.Min(100,10+settings.HealingPercentage);
                while((Mathf.Abs(replica.GetComponent<PlayerHealth>().CurrentHealth-expected)>.01f ||
                    replica.GetComponent<PlayerWeaponController>().GetInventoryWeaponIndex(0)!=-1) && Time.realtimeSinceStartup<timeout) yield return null;
                Check(Mathf.Abs(remoteHealth.CurrentHealth-expected)<.01f && Mathf.Abs(replica.GetComponent<PlayerHealth>().CurrentHealth-expected)<.01f &&
                    replica.GetComponent<PlayerWeaponController>().GetInventoryWeaponIndex(0)==-1,"Remote health and consumed inventory agree with server: "+settings.DisplayName);
            }
            var tape=catalogue.Items[0];
            Check(inventory.AcquireHealingItemServer(tape),"Acquire cancellation test item");
            float ready=Time.time+.25f; while(Time.time<ready) yield return null;
            SetSync(health,"m_currentHealth",100f); Check(healing.TryBeginUseServer(),"Cancellation test begins");
            typeof(PlayerWeaponController).GetMethod("EquipInventorySlotServer",Fields).Invoke(inventory,new object[]{4});
            Check(!healing.IsUsing && health.CurrentHealth==100 && inventory.GetInventoryHealingItem(0)==tape,"Switching slots cancels without consumption or healing");
            while(inventory.CanAcquireHealingItemServer(tape)) inventory.AcquireHealingItemServer(tape);
            int money=manager.CurrentCurrency;
            Check(!manager.TryPurchaseShopItemServer(carrier,tape.name,request++) && manager.CurrentCurrency==money,"Full inventory rejects purchase without charging");
            Check(catalogue.SelectDrop(.001f)==catalogue.Items[0] && catalogue.SelectDrop(.026f)==catalogue.Items[1] && catalogue.SelectDrop(.0327f)==catalogue.Items[2] && catalogue.SelectDrop(.5f)==null,"Single-roll rare drop intervals select zero or one item");
            SetSync(manager,"m_currency",0);
            var rivalInventory = rival.GetComponent<PlayerWeaponController>();
            var rivalCarrier = rival.GetComponent<PlayerCouchCarrier>();
            Check(manager.TryBeginDialogueServer(npc,rivalCarrier),"Second player opens dispatcher");
            Check(!manager.TryPurchaseShopItemServer(rivalCarrier,tape.name,1) && rivalInventory.GetInventoryWeaponIndex(0)==-1,
                "Insufficient currency cannot grant an item");
            floor.AddComponent<Unity.AI.Navigation.NavMeshSurface>().BuildNavMesh();
            var chanceField = typeof(HealingItemSettings).GetField("m_dropChance",Fields);
            var original = catalogue.Items.Select(item=>item.DropChance).ToArray();
            try
            {
                for(int index=0;index<3;index++)
                {
                    for(int i=0;i<3;i++) chanceField.SetValue(catalogue.Items[i], i==index ? 1f : 0f);
                    var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/_Project/Prefabs/Enemies/Thief_Pistol.prefab"),
                        new Vector3(12,0,12),Quaternion.identity);
                    s_network.ServerManager.Spawn(enemy);
                    int Count() => Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None).Count(p=>p.IsSpawned && p.HealingSettings!=null);
                    int before=Count();
                    var thief=enemy.GetComponent<CouchGuys.Gameplay.Enemies.ThiefHealth>();
                    thief.ApplyDamageServer(10000,Vector3.forward,0);
                    Check(Count()==before+1,"Thief death spawns one healing pickup: "+catalogue.Items[index].DisplayName);
                    thief.ApplyDamageServer(10000,Vector3.forward,0);
                    Check(Count()==before+1,"Repeated death damage cannot duplicate healing drop");
                }
            }
            finally { for(int i=0;i<3;i++) chanceField.SetValue(catalogue.Items[i],original[i]); }
            File.AppendAllText(Result,"COMPLETE: all three items passed shop, pickup, server-use and local host networking checks.\n");
        }
    }
}


