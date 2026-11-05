using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, one-time authoring. Nothing runs on import, reload or entering play.</summary>
    public static class MagneticHoverboardAuthoring
    {
        public const string PrefabPath="Assets/_Project/Prefabs/Vehicles/MagneticHoverboard.prefab";
        public const string SettingsPath="Assets/Settings/Vehicles/MagneticHoverboardSettings.asset";
        private const string ModelPath="Assets/_Project/Models/Hoverboard/magnetic_hoverboard.fbx";
        public static void CreateAndTest()
        {
            CreateAssets();
            MagneticHoverboardValidation.Run();
        }
        public static void ValidateAndTest()
        {
            ValidateAssets();
            MagneticHoverboardValidation.Run();
        }
        [MenuItem("Couch Guys/Vehicles/Create Magnetic Hoverboard %#F8")]
        public static void CreateAssets()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop play before authoring.");
            Directory.CreateDirectory("Assets/Settings/Vehicles");
            Directory.CreateDirectory("Assets/_Project/Materials/Vehicles/Hoverboard");
            AssetDatabase.Refresh();
            var settings=AssetDatabase.LoadAssetAtPath<MagneticHoverboardSettings>(SettingsPath);
            if(settings==null) { settings=ScriptableObject.CreateInstance<MagneticHoverboardSettings>(); AssetDatabase.CreateAsset(settings,SettingsPath); }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if(prefab==null) prefab=CreatePrefab(settings);
            UpgradePlayer();
            var collection=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            var no=prefab.GetComponent<NetworkObject>();
            if(!collection.Prefabs.Contains(no)) collection.AddObject(no,false,false);
            string stable=new string(($"{PrefabPath}{prefab.name}").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            no.SetAssetPathHash(stable.GetStableHashU64()); EditorUtility.SetDirty(no); EditorUtility.SetDirty(collection);
            AddToSampleScene(prefab);
            AssetDatabase.SaveAssets();
            ValidateAssets();
            Debug.Log("MAGNETIC_HOVERBOARD_AUTHORING_PASSED");
        }
        private static GameObject CreatePrefab(MagneticHoverboardSettings settings)
        {
            var root=new GameObject("MagneticHoverboard");
            try
            {
                var no=root.AddComponent<NetworkObject>();
                var body=root.AddComponent<Rigidbody>(); body.mass=settings.Mass; body.isKinematic=true;
                var nt=root.AddComponent<NetworkTransform>();
                var controller=root.AddComponent<MagneticHoverboardController>();
                var so=new SerializedObject(nt);
                so.FindProperty("_componentConfiguration").enumValueIndex=(int)NetworkTransform.ComponentConfigurationType.Rigidbody;
                so.FindProperty("_clientAuthoritative").boolValue=false;
                so.FindProperty("_synchronizeScale").boolValue=false;
                so.ApplyModifiedPropertiesWithoutUndo();
                var visual=Child(root.transform,"Visual",Vector3.zero);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.transform.SetParent(visual,false);
                PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                var renderers=model.GetComponentsInChildren<Renderer>(true);
                var bounds=renderers[0].bounds;
                foreach(var r in renderers) bounds.Encapsulate(r.bounds);
                if(bounds.size.z<3f || bounds.size.z>6f || bounds.size.y>3f) throw new Exception($"Unexpected model import axes / units: {bounds}");
                foreach(var r in renderers) r.sharedMaterials=r.sharedMaterials.Select(ConvertMaterial).ToArray();
                Transform left=Find(model,"Magnetic_Arm_Left"), right=Find(model,"Magnetic_Arm_Right");
                Transform lp=Child(visual,"LeftArmPivot",visual.InverseTransformPoint(left.position));
                Transform rp=Child(visual,"RightArmPivot",visual.InverseTransformPoint(right.position));
                left.SetParent(lp,true); right.SetParent(rp,true);
                var driver=Child(visual,"DriverAnchor",settings.DriverPositionOffset); driver.localRotation=Quaternion.Euler(settings.DriverRotationOffset);
                var cargo=Child(visual,"CouchAnchor",settings.AttachmentPositionOffset); cargo.localRotation=Quaternion.Euler(settings.AttachmentRotationOffset);
                var camera=Child(root.transform,"CameraTarget",new Vector3(0,.9f,0));
                var leftGrip=Child(visual,"LeftGrip",new Vector3(-.43f,1.83f,1.34f));
                var rightGrip=Child(visual,"RightGrip",new Vector3(.43f,1.83f,1.34f));
                var probes=new System.Collections.Generic.List<Transform>();
                foreach(float z in new[]{-1.68f,.03f,1.63f}) foreach(float x in new[]{-.43f,.43f})
                    probes.Add(Child(root.transform,"HoverProbe_"+probes.Count,new Vector3(x,0,z)));
                BoxCollider hull=root.AddComponent<BoxCollider>(); hull.center=new Vector3(0,.45f,0); hull.size=new Vector3(1.4f,.43f,4.3f);
                BoxCollider column=root.AddComponent<BoxCollider>(); column.center=new Vector3(0,1.25f,1.48f); column.size=new Vector3(.42f,1.05f,.3f);
                BoxCollider cargoShape=root.AddComponent<BoxCollider>(); cargoShape.center=new Vector3(0,1.35f,-.85f); cargoShape.size=new Vector3(.96f,1.05f,2.28f); cargoShape.enabled=false;
                Reference(controller,"m_settings",settings); Reference(controller,"m_body",body); Reference(controller,"m_visual",visual);
                Reference(controller,"m_driverAnchor",driver); Reference(controller,"m_couchAnchor",cargo); Reference(controller,"m_cameraTarget",camera);
                Reference(controller,"m_leftArm",lp); Reference(controller,"m_rightArm",rp); Reference(controller,"m_leftGrip",leftGrip); Reference(controller,"m_rightGrip",rightGrip);
                Reference(controller,"m_cargoCollider",cargoShape);
                var cs=new SerializedObject(controller); var array=cs.FindProperty("m_hoverProbes"); array.arraySize=probes.Count;
                for(int i=0;i<probes.Count;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=probes[i];
                var loading=cs.FindProperty("m_loadingPoints"); loading.arraySize=2;
                for(int i=0;i<2;i++)
                {
                    var point=GameObject.CreatePrimitive(PrimitiveType.Cube); point.name=i==0?"LoadPoint_Left":"LoadPoint_Right";
                    point.transform.SetParent(visual,false); point.transform.localPosition=new Vector3(i==0?-.9f:.9f,.76f,-.85f);
                    point.transform.localScale=new Vector3(.06f,.07f,.28f); Object.DestroyImmediate(point.GetComponent<Collider>());
                    point.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Vehicles/Hoverboard/Cyan emission.mat");
                    loading.GetArrayElementAtIndex(i).objectReferenceValue=point.transform;
                }
                cs.ApplyModifiedPropertiesWithoutUndo();
                ConfigureNetwork(root);
                return PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Material ConvertMaterial(Material original)
        {
            string label=original.name.Replace("HB • ","");
            string path="Assets/_Project/Materials/Vehicles/Hoverboard/"+label+".mat";
            Material result=AssetDatabase.LoadAssetAtPath<Material>(path); if(result!=null)return result;
            result=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Hoverboard "+label};
            Color color=original.HasProperty("_BaseColor")?original.GetColor("_BaseColor"):original.color;
            if(label.Contains("Cobalt"))color=new Color(.025f,.16f,.8f);
            if(label.Contains("Cyan"))color=new Color(.005f,.77f,1f);
            if(label.Contains("Rear coral"))color=new Color(1f,.035f,.012f);
            result.SetColor("_BaseColor",color); result.SetFloat("_Metallic",label.Contains("alloy")?.65f:.2f);
            result.SetFloat("_Smoothness",label.Contains("rubber")?.2f:.5f);
            if(label.Contains("emission"))
            {
                result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor",color*(label.Contains("Cyan")?4f:2f));
                result.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            AssetDatabase.CreateAsset(result,path); return result;
        }
        private static void UpgradePlayer()
        {
            const string path="Assets/_Project/Prefabs/Player.prefab";
            GameObject root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var interactor=root.GetComponent<PlayerHoverboardInteractor>()??root.AddComponent<PlayerHoverboardInteractor>();
                Reference(interactor,"m_input",root.GetComponent<CouchGuys.Input.PlayerInputReader>());
                Reference(interactor,"m_controller",root.GetComponent<ThirdPersonPlayerController>());
                Reference(interactor,"m_capsule",root.GetComponent<CharacterController>());
                Reference(interactor,"m_camera",root.GetComponentInChildren<CouchGuys.CameraSystem.ThirdPersonCameraController>(true));
                Reference(interactor,"m_networkTransform",root.GetComponent<NetworkTransform>());
                var hitbox=root.transform.Find("HoverboardDriverHitbox");
                if(hitbox==null)
                {
                    hitbox=Child(root.transform,"HoverboardDriverHitbox",Vector3.zero);
                    var capsule=hitbox.gameObject.AddComponent<CapsuleCollider>(); var source=root.GetComponent<CharacterController>();
                    capsule.center=source.center; capsule.radius=source.radius; capsule.height=source.height; capsule.enabled=false;
                }
                Reference(interactor,"m_driverHitbox",hitbox.GetComponent<CapsuleCollider>());
                ConfigureNetwork(root); PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void ConfigureNetwork(GameObject root)
        {
            var no=root.GetComponent<NetworkObject>();
            // Retain established RPC component indices; append only new behaviours.
            var present=root.GetComponents<NetworkBehaviour>();
            var behaviours=no.NetworkBehaviours.Where(b=>b!=null && present.Contains(b))
                .Concat(present).Distinct().ToArray();
            var so=new SerializedObject(no); var array=so.FindProperty("NetworkBehaviours"); array.arraySize=behaviours.Length;
            for(int i=0;i<behaviours.Length;i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue=behaviours[i];
                var bs=new SerializedObject(behaviours[i]); bs.FindProperty("_componentIndexCache").intValue=i;
                bs.FindProperty("_addedNetworkObject").objectReferenceValue=no; bs.FindProperty("_networkObjectCache").objectReferenceValue=no;
                bs.ApplyModifiedPropertiesWithoutUndo();
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void AddToSampleScene(GameObject prefab)
        {
            const string path="Assets/Scenes/SampleScene.unity";
            Scene scene=SceneManager.GetSceneByPath(path); bool opened=!scene.isLoaded;
            if(opened) scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var existing=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MagneticHoverboardController>(true)).FirstOrDefault();
                if(existing==null)
                {
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                    instance.transform.position=new Vector3(4,1,6);
                    var so=new SerializedObject(instance.GetComponent<MagneticHoverboardController>());
                    so.FindProperty("m_placeNearDepot").boolValue=true; so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<MagneticHoverboardController>());
                    existing=instance.GetComponent<MagneticHoverboardController>();
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
                var sceneObject=existing.GetComponent<NetworkObject>();
                if(!sceneObject.IsSceneObject)
                {
                    sceneObject.SetSceneId((path+"/MagneticHoverboard").GetStableHashU64());
                    EditorUtility.SetDirty(sceneObject); PrefabUtility.RecordPrefabInstancePropertyModifications(sceneObject);
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
            }
            finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
        }
        [MenuItem("Couch Guys/Vehicles/Validate Magnetic Hoverboard %#F9")]
        public static void ValidateAssets()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if(prefab==null)throw new Exception("Missing hoverboard prefab");
            var controller=prefab.GetComponent<MagneticHoverboardController>();
            var settings=AssetDatabase.LoadAssetAtPath<MagneticHoverboardSettings>(SettingsPath);
            Check(controller.Settings==settings,"Shared settings reference");
            var so=new SerializedObject(controller);
            foreach(string field in new[]{"m_body","m_visual","m_driverAnchor","m_couchAnchor","m_cameraTarget","m_leftArm","m_rightArm","m_leftGrip","m_rightGrip","m_cargoCollider"})
                Check(so.FindProperty(field).objectReferenceValue!=null,"Missing "+field);
            Check(so.FindProperty("m_hoverProbes").arraySize==6,"Hover probe count");
            var loadingPoints=so.FindProperty("m_loadingPoints");
            Check(loadingPoints.arraySize==2 && loadingPoints.GetArrayElementAtIndex(0).objectReferenceValue!=null &&
                loadingPoints.GetArrayElementAtIndex(1).objectReferenceValue!=null,"Two saved side loading points");
            Check(prefab.GetComponentsInChildren<WheelCollider>(true).Length==0,"Hover motor must not use wheels");
            Check(!new SerializedObject(prefab.GetComponent<NetworkTransform>()).FindProperty("_clientAuthoritative").boolValue,"Server transform authority");
            Check(prefab.GetComponent<NetworkObject>().NetworkBehaviours.Count==2,"Vehicle network behaviours");
            Check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab").GetComponent<PlayerHoverboardInteractor>()!=null,"Player integration");
            Check(prefab.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Any(m=>m!=null&&m.IsKeywordEnabled("_EMISSION")),"Emission materials");
            var spawnables=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            Check(spawnables.Prefabs.Contains(prefab.GetComponent<NetworkObject>()),"Spawn registration");
            Scene scene=SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity"); bool opened=!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Additive);
            try
            {
                var boards=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MagneticHoverboardController>(true)).ToArray();
                Check(boards.Length==1 && boards[0].GetComponent<NetworkObject>().IsSceneObject,"Single networked SampleScene board");
                Check(new SerializedObject(boards[0]).FindProperty("m_placeNearDepot").boolValue,"Procedural depot placement");
            }
            finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/hoverboard-validation.txt","PASS: saved prefab, shared settings, six probes, server authority, player integration, materials, spawn registration.\n");
            Debug.Log("MAGNETIC_HOVERBOARD_ASSET_VALIDATION_PASSED");
        }
        private static void Check(bool value,string message) { if(!value)throw new Exception(message); }
        private static Transform Find(GameObject root,string name)=>root.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);
        private static Transform Child(Transform parent,string name,Vector3 position)
        { var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=position; return go.transform; }
        private static void Reference(Object target,string field,Object value)
        { var so=new SerializedObject(target); so.FindProperty(field).objectReferenceValue=value; so.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
