using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit authoring only. Existing assets and Inspector customizations are retained.</summary>
    public static class BaseballBatAuthoring
    {
        public const string Folder = "Assets/_Project/Prefabs/Weapons/BaseballBat";
        public const string Model = "Assets/_Project/Models/Weapons/BaseballBat/baseball_bat.fbx";
        public const string Visual = Folder + "/BaseballBat.prefab";
        public const string Pickup = Folder + "/BaseballBatPickup.prefab";
        public const string Config = "Assets/_Project/Resources/Weapons/BaseballBatAssets.asset";
        public const string AnimationFolder = "Assets/_Project/Animations/BaseballBat";
        private const string Player = "Assets/_Project/Prefabs/Player.prefab";
        public static string OutputFolder => Path.GetFileName(Directory.GetCurrentDirectory()) == "BatAuthoringProject" ?
            Path.GetFullPath("../BaseballBat") : "Temp/BaseballBat";
        private static readonly string[] Bones = { "Torso", "Head", "UpperArm_L", "LowerArm_L", "Hand_L", "UpperArm_R", "LowerArm_R", "Hand_R" };

        [MenuItem("Couch Guys/Weapons/Install Baseball Bat")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new UnityException("Exit Play mode first.");
            EnsureFolder(Folder + "/Materials"); EnsureFolder(AnimationFolder);
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Visual) == null) BuildVisual();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Pickup) == null) BuildPickup();
            var config = AssetDatabase.LoadAssetAtPath<BaseballBatAssets>(Config);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<BaseballBatAssets>();
                AssetDatabase.CreateAsset(config, Config);
                Set(config, "m_equippedPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(Visual));
                Set(config, "m_pickupPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(Pickup));
            }
            AttachAndAnimate(config);
            RegisterPickup(); InstallShop();
            AssetDatabase.SaveAssets();
            Validate();
        }

        private static Material Material(string name, Color color, bool trail = false)
        {
            string path = Folder + "/Materials/Bat_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = trail ? new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) :
                new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Prefabs/Weapons/Materials/CG_Weapon_Yellow.mat"));
            material.name = "Bat_" + name;
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
            if (trail)
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            }
            AssetDatabase.CreateAsset(material, path); return material;
        }

        private static void BuildVisual()
        {
            var root = new GameObject("Weapon_BaseballBat");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model), root.transform);
                var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var group in model.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(group);
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Vector3 axis = bounds.size.y > bounds.size.z && bounds.size.y > bounds.size.x ? Vector3.up :
                    bounds.size.x > bounds.size.z ? Vector3.right : Vector3.forward;
                if (Vector3.Dot(bounds.center, axis) < 0) axis = -axis;
                model.transform.rotation = Quaternion.FromToRotation(axis, Vector3.forward) * model.transform.rotation;
                foreach (var renderer in renderers)
                {
                    string part = renderer.name;
                    string key = part.Contains("Yellow") ? "Yellow" : part.Contains("Red") ? "Red" :
                        part.Contains("Rubber") ? "Rubber" : part.Contains("Navy") ? "Navy" : part.Contains("Steel") ? "Steel" : "Blue";
                    Color color = key switch
                    {
                        "Yellow" => new Color(1f,.72f,.055f), "Red" => new Color(.83f,.065f,.045f),
                        "Navy" => new Color(.021f,.03f,.055f), "Rubber" => new Color(.012f,.018f,.03f),
                        "Steel" => new Color(.18f,.22f,.27f), _ => new Color(.008f,.32f,.72f)
                    };
                    renderer.sharedMaterials = Enumerable.Repeat(Material(key, color), renderer.sharedMaterials.Length).ToArray();
                    renderer.gameObject.SetActive(true);
                }
                var lods = root.AddComponent<LODGroup>();
                lods.SetLODs(new[] { new LOD(.25f, renderers.Where(r=>r.name.Contains("_LOD0")).Cast<Renderer>().ToArray()),
                    new LOD(.1f, renderers.Where(r=>r.name.Contains("_LOD1")).Cast<Renderer>().ToArray()),
                    new LOD(.015f, renderers.Where(r=>r.name.Contains("_LOD2")).Cast<Renderer>().ToArray()) });
                lods.RecalculateBounds();
                var tip = new GameObject("Muzzle").transform; tip.SetParent(root.transform, false); tip.localPosition = Vector3.forward * .92f;
                var trailObject = new GameObject("BlueSwingTrail"); trailObject.transform.SetParent(root.transform, false);
                trailObject.transform.localPosition = Vector3.forward * .8f;
                var trail = trailObject.AddComponent<TrailRenderer>();
                trail.time = .11f; trail.emitting = false; trail.minVertexDistance = .015f;
                trail.widthCurve = AnimationCurve.Linear(0,.18f,1,0);
                trail.sharedMaterial = Material("MotionTrail", new Color(.1f,.7f,1f,.7f), true);
                trail.startColor = new Color(.1f,.8f,1f,.8f); trail.endColor = new Color(.05f,.4f,1f,0);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; trail.receiveShadows = false;
                var audio = root.AddComponent<AudioSource>();
                audio.playOnAwake = false; audio.spatialBlend = 1; audio.minDistance = 2; audio.maxDistance = 30;
                audio.rolloffMode = AudioRolloffMode.Linear;
                var presentation = root.AddComponent<BaseballBatPresentation>();
                Set(presentation, "m_trail", trail); Set(presentation, "m_audio", audio);
                Set(presentation, "m_swingSound", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/BaseballBat/Bat_Swing.wav"));
                Set(presentation, "m_impactSound", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Car/splat.wav"));
                PrefabUtility.SaveAsPrefabAsset(root, Visual);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildPickup()
        {
            var root = new GameObject("BaseballBatPickup");
            try
            {
                var no = root.AddComponent<NetworkObject>();
                var body = root.AddComponent<Rigidbody>(); body.mass = 1.5f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; body.interpolation = RigidbodyInterpolation.Interpolate;
                var nt = root.AddComponent<NetworkTransform>(); var pickup = root.AddComponent<WeaponPickup>();
                var so = new SerializedObject(pickup); so.FindProperty("m_weaponType").intValue = 5; so.ApplyModifiedPropertiesWithoutUndo();
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Visual), root.transform);
                // Drop collider and centre of mass enclose the complete bat, including its pommel.
                var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.forward * .355f; collider.size = new Vector3(.34f,.18f,1.13f);
                Object.DestroyImmediate(visual.GetComponent<BaseballBatPresentation>());
                Object.DestroyImmediate(visual.GetComponent<AudioSource>());
                var trail = visual.GetComponentInChildren<TrailRenderer>(); if (trail != null) Object.DestroyImmediate(trail.gameObject);
                so = new SerializedObject(nt); so.FindProperty("_clientAuthoritative").boolValue = false;
                so.FindProperty("_synchronizeScale").boolValue = false;
                so.FindProperty("_componentConfiguration").enumValueIndex = (int)NetworkTransform.ComponentConfigurationType.Rigidbody;
                so.ApplyModifiedPropertiesWithoutUndo();
                so = new SerializedObject(no); var list = so.FindProperty("NetworkBehaviours"); list.arraySize = 2;
                list.GetArrayElementAtIndex(0).objectReferenceValue = nt; list.GetArrayElementAtIndex(1).objectReferenceValue = pickup;
                so.ApplyModifiedPropertiesWithoutUndo();
                int index = 0;
                foreach (var behaviour in new NetworkBehaviour[] { nt, pickup })
                {
                    so = new SerializedObject(behaviour); so.FindProperty("_componentIndexCache").intValue = index++;
                    so.FindProperty("_addedNetworkObject").objectReferenceValue = no;
                    so.FindProperty("_networkObjectCache").objectReferenceValue = no; so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, Pickup);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Transform Bone(Transform rig, string name) => rig.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);

        private static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            Vector3 start = upper.position;
            float a = Vector3.Distance(start, lower.position), b = Vector3.Distance(lower.position, hand.position);
            Vector3 line = target - start; float d = Mathf.Clamp(line.magnitude, Mathf.Abs(a-b)+.001f, a+b-.001f);
            Vector3 direction = line.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole-start, direction).normalized;
            float along = (a*a-b*b+d*d)/(2*d);
            Vector3 elbow = start + direction*along + bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation = Quaternion.FromToRotation(lower.position-start, elbow-start)*upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position-lower.position, target-lower.position)*lower.rotation;
        }

        private static void AttachAndAnimate(BaseballBatAssets config)
        {
            var player = PrefabUtility.LoadPrefabContents(Player);
            try
            {
                var weapons = player.GetComponent<PlayerWeaponController>(); var so = new SerializedObject(weapons);
                var models = so.FindProperty("m_weaponModels"); var muzzles = so.FindProperty("m_muzzles");
                if (models.arraySize < 6) models.arraySize = 6; if (muzzles.arraySize < 6) muzzles.arraySize = 6;
                var pistol = (Transform)models.GetArrayElementAtIndex(0).objectReferenceValue;
                var animator = player.GetComponent<CouchGuyAnimationDriver>().Animator;
                var restTransforms = animator.GetComponentsInChildren<Transform>(true);
                var restPositions = restTransforms.Select(t=>t.localPosition).ToArray();
                var restRotations = restTransforms.Select(t=>t.localRotation).ToArray();
                var rig = Bone(animator.transform, "CouchGuy_Rig");
                var hand = Bone(rig, "Hand_L");
                var idle = AssetDatabase.LoadAllAssetsAtPath(CouchGuyAnimatorControllerBuilder.ModelPath).OfType<AnimationClip>()
                    .First(c=>c.name.EndsWith("|Idle"));
                idle.SampleAnimation(animator.gameObject, 0f);
                Quaternion socketRotation = Quaternion.Inverse(hand.rotation)*
                    Quaternion.LookRotation(pistol.TransformDirection(Vector3.down), pistol.TransformDirection(Vector3.forward));
                var bat = models.GetArrayElementAtIndex(5).objectReferenceValue as Transform;
                if (bat == null || bat.name != "Weapon_BaseballBat")
                {
                    bat = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Visual), hand)).transform;
                    bat.name = "Weapon_BaseballBat"; bat.localPosition = Vector3.zero; bat.localRotation = socketRotation;
                    Vector3 scale = hand.lossyScale;
                    bat.localScale = new Vector3(1/Mathf.Abs(scale.x),1/Mathf.Abs(scale.y),1/Mathf.Abs(scale.z));
                    bat.gameObject.SetActive(false);
                    models.GetArrayElementAtIndex(5).objectReferenceValue = bat;
                    muzzles.GetArrayElementAtIndex(5).objectReferenceValue = bat.Find("Muzzle");
                }
                so.FindProperty("m_baseballBatAssets").objectReferenceValue = config;
                var definitions = so.FindProperty("m_weapons");
                if (definitions.arraySize < 6) definitions.arraySize = 6;
                var definition = definitions.GetArrayElementAtIndex(5);
                if (definition.FindPropertyRelative("m_type").intValue != 5)
                {
                    definition.FindPropertyRelative("m_type").intValue = 5;
                    definition.FindPropertyRelative("m_displayName").stringValue = "Baseball Bat";
                    definition.FindPropertyRelative("m_automatic").boolValue = false;
                    definition.FindPropertyRelative("m_fireInterval").floatValue = 2;
                    definition.FindPropertyRelative("m_range").floatValue = 1.8f;
                    definition.FindPropertyRelative("m_damagePerPellet").floatValue = 5;
                    definition.FindPropertyRelative("m_startingReserve").intValue = 0;
                    definition.FindPropertyRelative("m_magazineCapacity").intValue = 1;
                    foreach (string field in new[] { "m_bulletPrefab", "m_muzzleFlashPrefab", "m_shellEjectionPrefab" })
                        definition.FindPropertyRelative(field).objectReferenceValue = null;
                    definition.FindPropertyRelative("m_reticleSettings").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<ReticleSettings>("Assets/_Project/Settings/Reticles/ShotgunReticle.asset");
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                for (int i=0;i<restTransforms.Length;i++)
                    restTransforms[i].SetLocalPositionAndRotation(restPositions[i],restRotations[i]);
                PrefabUtility.SaveAsPrefabAsset(player, Player);
                idle.SampleAnimation(animator.gameObject, 0);
                BuildAnimations(player.transform, animator, rig, idle, bat.localRotation);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        private static void BuildAnimations(Transform player, Animator animator, Transform rig, AnimationClip idle, Quaternion socketRotation)
        {
            var ready = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + "/Bat_Ready.anim");
            var swing = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + "/Bat_Swing.anim");
            if (ready == null || swing == null)
            {
                Transform[] bones = Bones.Select(n=>Bone(rig,n)).ToArray();
                float shoulder = player.InverseTransformPoint(bones[2].position).y;
                float[] times = { 0,.2f,.3f,.4f,.65f,.85f };
                Vector3[] positions = { new(-.02f,shoulder-.32f,.22f), new(.25f,shoulder-.02f,.02f),
                    new(.06f,shoulder-.21f,.40f), new(-.21f,shoulder-.25f,.26f), new(-.24f,shoulder-.26f,.18f), new(-.02f,shoulder-.32f,.22f) };
                Vector3[] directions = { new(.85f,.5f,.1f), new(.42f,.84f,-.35f), new(0,.06f,1),
                    new(-.96f,.1f,.25f), new(-.75f,.3f,-.5f), new(.85f,.5f,.1f) };
                float[] twists = { 0, -32, 0, 38, 48, 0 };
                var curves = new AnimationCurve[bones.Length,4];
                for (int i=0;i<bones.Length;i++) for(int j=0;j<4;j++) curves[i,j] = new AnimationCurve();
                Quaternion[] first = new Quaternion[bones.Length];
                for (int frame=0;frame<=102;frame++)
                {
                    float t = frame/120f; int phase = 0;
                    while (phase < times.Length-2 && t > times[phase+1]) phase++;
                    float u = Mathf.SmoothStep(0,1,Mathf.InverseLerp(times[phase],times[phase+1],t));
                    idle.SampleAnimation(animator.gameObject, 0);
                    bones[0].localRotation *= Quaternion.Euler(0,Mathf.Lerp(twists[phase],twists[phase+1],u),0);
                    Vector3 grip = player.TransformPoint(Vector3.Lerp(positions[phase],positions[phase+1],u));
                    Vector3 direction = player.TransformDirection(Vector3.Slerp(directions[phase],directions[phase+1],u).normalized);
                    float rightReach = Vector3.Distance(bones[2].position,bones[3].position)+Vector3.Distance(bones[3].position,bones[4].position)-.006f;
                    float leftReach = Vector3.Distance(bones[5].position,bones[6].position)+Vector3.Distance(bones[6].position,bones[7].position)-.006f;
                    // Project the paired grips into both arms' reachable volumes before solving either arm.
                    for(int iteration=0;iteration<24;iteration++)
                    {
                        grip = bones[2].position+Vector3.ClampMagnitude(grip-bones[2].position,rightReach);
                        Vector3 supportTarget=grip+direction*.095f;
                        grip += bones[5].position+Vector3.ClampMagnitude(supportTarget-bones[5].position,leftReach)-supportTarget;
                    }
                    Quaternion orientation = Quaternion.LookRotation(direction, player.up)*Quaternion.Inverse(socketRotation);
                    SolveArm(bones[2],bones[3],bones[4],grip,grip+player.right*.45f-player.up*.35f);
                    bones[4].rotation = orientation;
                    Vector3 support = grip + direction*.095f;
                    SolveArm(bones[5],bones[6],bones[7],support,support-player.right*.45f-player.up*.35f);
                    bones[7].rotation = orientation;
                    for(int i=0;i<bones.Length;i++)
                    {
                        Quaternion q=bones[i].localRotation;
                        if(frame==0) first[i]=q;
                        curves[i,0].AddKey(t,q.x);curves[i,1].AddKey(t,q.y);curves[i,2].AddKey(t,q.z);curves[i,3].AddKey(t,q.w);
                    }
                }
                if (ready == null)
                {
                    ready = new AnimationClip { name="Bat_Ready",frameRate=60 };
                    for(int i=0;i<bones.Length;i++) for(int j=0;j<4;j++)
                        AnimationUtility.SetEditorCurve(ready,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[i],animator.transform),
                            typeof(Transform),"m_LocalRotation."+"xyzw"[j]),AnimationCurve.Constant(0,1,first[i][j]));
                    var settings=AnimationUtility.GetAnimationClipSettings(ready);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(ready,settings);
                    ready.EnsureQuaternionContinuity();AssetDatabase.CreateAsset(ready,AnimationFolder+"/Bat_Ready.anim");
                }
                if (swing == null)
                {
                    swing = new AnimationClip { name="Bat_Swing",frameRate=60 };
                    for(int i=0;i<bones.Length;i++) for(int j=0;j<4;j++)
                    {
                        for(int k=0;k<curves[i,j].length;k++)
                        { AnimationUtility.SetKeyLeftTangentMode(curves[i,j],k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curves[i,j],k,AnimationUtility.TangentMode.Linear); }
                        AnimationUtility.SetEditorCurve(swing,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[i],animator.transform),typeof(Transform),"m_LocalRotation."+"xyzw"[j]),curves[i,j]);
                    }
                    swing.EnsureQuaternionContinuity();AssetDatabase.CreateAsset(swing,AnimationFolder+"/Bat_Swing.anim");
                }
            }
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(CouchGuyAnimatorControllerBuilder.ControllerPath);
            if(controller.layers.Any(l=>l.name=="Baseball Bat"))return;
            var mask=new AvatarMask { name="Bat Upper Body" };
            var maskTransforms=animator.GetComponentsInChildren<Transform>(true);mask.transformCount=maskTransforms.Length;
            var upperBones=Bones.Select(n=>Bone(rig,n)).ToArray();
            for(int i=0;i<mask.transformCount;i++)
            {
                mask.SetTransformPath(i,AnimationUtility.CalculateTransformPath(maskTransforms[i],animator.transform));
                mask.SetTransformActive(i,upperBones.Contains(maskTransforms[i]));
            }
            var machine=new AnimatorStateMachine { name="Baseball Bat" };
            AssetDatabase.AddObjectToAsset(mask,controller);AssetDatabase.AddObjectToAsset(machine,controller);
            var readyState=machine.AddState("Bat_Ready");readyState.motion=ready;readyState.writeDefaultValues=false;machine.defaultState=readyState;
            var swingState=machine.AddState("Bat_Swing");swingState.motion=swing;swingState.writeDefaultValues=false;
            controller.AddLayer(new AnimatorControllerLayer { name="Baseball Bat",stateMachine=machine,avatarMask=mask,defaultWeight=0,
                blendingMode=AnimatorLayerBlendingMode.Override });EditorUtility.SetDirty(controller);
        }

        private static void RegisterPickup()
        {
            var no=AssetDatabase.LoadAssetAtPath<GameObject>(Pickup).GetComponent<NetworkObject>();
            string stable=new((Pickup+no.name).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            no.SetAssetPathHash(stable.GetStableHashU64());EditorUtility.SetDirty(no);
            PrefabUtility.SavePrefabAsset(no.gameObject);
            // Saving a prefab can refresh FishNet's collections. Resolve and persist the collection afterwards.
            no=AssetDatabase.LoadAssetAtPath<GameObject>(Pickup).GetComponent<NetworkObject>();
            var list=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            var so=new SerializedObject(list);var prefabs=so.FindProperty("_prefabs");
            if(!list.Prefabs.Contains(no))
            {int index=prefabs.arraySize++;prefabs.GetArrayElementAtIndex(index).objectReferenceValue=no;}
            so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(list);AssetDatabase.SaveAssetIfDirty(list);
        }

        private static void InstallShop()
        {
            const string id="weapon_baseballbat";
            var catalogue=AssetDatabase.LoadAssetAtPath<ShopCatalogue>("Assets/_Project/Resources/Shop/DeliveryManagerShop.asset");
            if(catalogue.Find(id)!=null)return;
            var purchase=ScriptableObject.CreateInstance<WeaponPurchaseBehaviour>();var so=new SerializedObject(purchase);
            so.FindProperty("m_weaponType").intValue=5;so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(purchase,"Assets/_Project/Settings/Shop/"+id+"_purchase.asset");
            var item=ScriptableObject.CreateInstance<ShopItemDefinition>();so=new SerializedObject(item);
            so.FindProperty("m_itemId").stringValue=id;so.FindProperty("m_displayName").stringValue="Baseball Bat";
            so.FindProperty("m_description").stringValue="A little damage. A ridiculous launch. Unlimited swings, two seconds apart.";
            so.FindProperty("m_price").intValue=200;so.FindProperty("m_category").enumValueIndex=(int)ShopItemCategory.Weapon;
            so.FindProperty("m_purchaseBehaviour").objectReferenceValue=purchase;so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(item,"Assets/_Project/Settings/Shop/"+id+".asset");
            so=new SerializedObject(catalogue);var items=so.FindProperty("m_items");int index=items.arraySize++;
            items.GetArrayElementAtIndex(index).objectReferenceValue=item;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(catalogue);
        }

        [MenuItem("Couch Guys/Weapons/Validate Baseball Bat")]
        public static void Validate()
        {
            var player=AssetDatabase.LoadAssetAtPath<GameObject>(Player);var so=new SerializedObject(player.GetComponent<PlayerWeaponController>());
            var bat=so.FindProperty("m_weaponModels").GetArrayElementAtIndex(5).objectReferenceValue as Transform;
            if(bat==null || bat.parent.name!="Hand_L")throw new UnityException("Bat hand attachment missing.");
            if(bat.GetComponentsInChildren<Collider>(true).Length!=0)throw new UnityException("Equipped bat contains colliders.");
            var config=AssetDatabase.LoadAssetAtPath<BaseballBatAssets>(Config);
            if(config==null || config.PickupPrefab==null || config.EquippedPrefab==null)throw new UnityException("Bat config references missing.");
            var pickup=config.PickupPrefab;
            var list=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            if(!list.Prefabs.Contains(pickup.GetComponent<NetworkObject>()))throw new UnityException("Pickup not registered.");
            if(pickup.GetComponent<Collider>().isTrigger || pickup.GetComponent<Rigidbody>().isKinematic)throw new UnityException("Pickup must be physical.");
            foreach(var lod in config.EquippedPrefab.GetComponent<LODGroup>().GetLODs())
                if(lod.renderers.Length==0)throw new UnityException("Empty LOD.");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(CouchGuyAnimatorControllerBuilder.ControllerPath);
            if(!controller.layers.Any(l=>l.name=="Baseball Bat"))throw new UnityException("Animation layer missing.");
            if(AssetDatabase.LoadAssetAtPath<ShopCatalogue>("Assets/_Project/Resources/Shop/DeliveryManagerShop.asset").Find("weapon_baseballbat")==null)
                throw new UnityException("Shop item missing.");
            foreach(string name in new[]{"Bat_Ready","Bat_Swing"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder+"/"+name+".anim");
                if(clip==null)throw new UnityException("Missing "+name);
                foreach(var binding in AnimationUtility.GetCurveBindings(clip))
                    if(!Bones.Contains(binding.path.Split('/').Last()))throw new UnityException("Clip changes lower-body/ring: "+binding.path);
            }
            Directory.CreateDirectory(OutputFolder);
            File.WriteAllText(Path.Combine(OutputFolder,"validation.txt"),"PASS: saved bat and pickup, 3 LODs, hand socket, upper-body clips, spawn registration, config and shop.\n");
            Debug.Log("Baseball bat asset validation PASS.");
        }

        [MenuItem("Couch Guys/Weapons/Preview Baseball Bat Swing")]
        public static void Preview()
        {
            var preview=new PreviewRenderUtility();
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Player));
            try
            {
                preview.AddSingleGO(player);
                var animator=player.GetComponent<CouchGuyAnimationDriver>().Animator;
                var weaponData=new SerializedObject(player.GetComponent<PlayerWeaponController>()).FindProperty("m_weaponModels");
                for(int i=0;i<weaponData.arraySize;i++)
                    ((Transform)weaponData.GetArrayElementAtIndex(i).objectReferenceValue)?.gameObject.SetActive(i==5);
                var idle=AssetDatabase.LoadAllAssetsAtPath(CouchGuyAnimatorControllerBuilder.ModelPath).OfType<AnimationClip>().First(c=>c.name.EndsWith("|Idle"));
                var swing=AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder+"/Bat_Swing.anim");
                preview.camera.orthographic=true;preview.camera.orthographicSize=1.2f;
                preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=30;
                preview.camera.transform.position=new Vector3(2.7f,1.9f,4.5f);
                preview.camera.transform.LookAt(new Vector3(0,1,0));
                preview.camera.backgroundColor=new Color(.12f,.17f,.23f);preview.camera.clearFlags=CameraClearFlags.SolidColor;
                preview.lights[0].intensity=1.5f;preview.lights[0].transform.rotation=Quaternion.Euler(40,30,0);
                preview.lights[1].intensity=1;preview.lights[1].transform.rotation=Quaternion.Euler(340,220,0);
                var sheet=new Texture2D(1536,512,TextureFormat.RGB24,false);
                float[] times={0,.2f,.32f,.65f};
                for(int i=0;i<times.Length;i++)
                {
                    idle.SampleAnimation(animator.gameObject,0);swing.SampleAnimation(animator.gameObject,times[i]);
                    for(int w=0;w<weaponData.arraySize;w++)
                        ((Transform)weaponData.GetArrayElementAtIndex(w).objectReferenceValue)?.gameObject.SetActive(w==5);
                    preview.BeginStaticPreview(new Rect(0,0,384,512));preview.Render(true);
                    var tile=preview.EndStaticPreview();sheet.SetPixels(i*384,0,384,512,tile.GetPixels());Object.DestroyImmediate(tile);
                }
                sheet.Apply();Directory.CreateDirectory(OutputFolder);File.WriteAllBytes(Path.Combine(OutputFolder,"swing-preview.png"),sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
            }
            finally{preview.Cleanup();if(player!=null)Object.DestroyImmediate(player);}
        }

        private static void Set(Object target,string property,Object value)
        { var so=new SerializedObject(target);so.FindProperty(property).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(target); }
        private static void EnsureFolder(string path)
        { if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');EnsureFolder(path[..slash]);AssetDatabase.CreateFolder(path[..slash],path[(slash+1)..]); }
    }
}
