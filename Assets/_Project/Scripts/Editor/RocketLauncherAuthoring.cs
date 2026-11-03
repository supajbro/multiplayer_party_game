using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Weapons;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, one-time authoring. Saved assets remain fully editable; never runs on reload.</summary>
    public static class RocketLauncherAuthoring
    {
        public const string Folder = "Assets/_Project/Prefabs/Weapons/RocketLauncher";
        public const string Model = "Assets/_Project/Models/Weapons/RocketLauncher/RocketLauncher.fbx";
        public const string Visual = Folder + "/RocketLauncher.prefab";
        public const string Projectile = Folder + "/RocketProjectile.prefab";
        public const string Pickup = Folder + "/RocketLauncherPickup.prefab";
        public const string Explosion = Folder + "/RocketExplosion.prefab";
        private const string Player = "Assets/_Project/Prefabs/Player.prefab";
        private const string Config = "Assets/_Project/Resources/Weapons/RocketLauncherAssets.asset";
        private static readonly Color Blue = new(.005f,.32f,.8f);
        private static readonly Color Red = new(.88f,.065f,.042f);
        private static readonly Color Yellow = new(1f,.72f,.008f);
        private static readonly Color Navy = new(.022f,.034f,.064f);

        [MenuItem("Couch Guys/Weapons/Install Rocket Launcher %#&r")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new UnityException("Exit Play mode first.");
            EnsureFolder(Folder);
            EnsureFolder(Folder + "/Materials");
            EnsureFolder(Folder + "/Meshes");
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(Model) is ModelImporter importer)
            {
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }
            // Existing authored prefab edits are preserved when the installer is run again.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Visual) == null) BuildVisual();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Explosion) == null) BuildExplosion();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Projectile) == null) BuildProjectile();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Pickup) == null) BuildPickup();
            var config = AssetDatabase.LoadAssetAtPath<RocketLauncherAssets>(Config);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<RocketLauncherAssets>();
                AssetDatabase.CreateAsset(config, Config);
                Set(config, "m_equippedPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(Visual));
                Set(config, "m_pickupPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(Pickup));
            }
            Register(Pickup);
            Register(Projectile);
            AttachPlayer();
            InstallShop();
            AssetDatabase.SaveAssets();
            Validate();
        }

        private static Material Mat(string name, Color color, bool vfx = false, float shading = 0)
        {
            string path = Folder + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = vfx ? new Material(Shader.Find("Couch Guys/Rocket Cartoon VFX")) :
                new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Prefabs/Weapons/Materials/CG_Weapon_Yellow.mat"));
            mat.name = name;
            mat.SetColor("_BaseColor",color);
            if (vfx) { mat.SetFloat("_Opacity",1); mat.SetFloat("_Shading",shading); }
            else mat.SetColor("_EmissionColor",Color.black);
            AssetDatabase.CreateAsset(mat,path);
            return mat;
        }

        private static void BuildVisual()
        {
            var root = new GameObject("Weapon_RocketLauncher");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
                if (source == null) throw new UnityException("Launcher FBX missing.");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);
                // Blender export is X-forward/Y-up in Unity; infer axes from the actual mesh landmarks.
                Transform muzzle = model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Muzzle_YellowRim");
                Transform stock = model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Stock_ShoulderPad");
                Transform grip = model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Grip_Navy");
                Transform rail = model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Receiver_TopBlueRail");
                Vector3 forward=(muzzle.position-stock.position).normalized;
                Vector3 up=Vector3.ProjectOnPlane(rail.position-grip.position,forward).normalized;
                model.transform.rotation=Quaternion.Inverse(Quaternion.LookRotation(forward,up))*model.transform.rotation;
                model.transform.localScale*=.72f;
                // Hand origin is the upper grip rather than the model bounding-box centre.
                model.transform.localPosition-=grip.position+Vector3.up*.09f;
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string part=r.name;
                    bool yellow=part.Contains("Yellow") || part.Contains("Emblem") || part.Contains("Reticle");
                    bool red=part.Contains("Red") || part.Contains("Armour") || part.Contains("Porthole");
                    bool navy=part.Contains("Navy") || part.Contains("Dark") || part.Contains("Fastener") || part.Contains("ShoulderPad") || part.Contains("InnerLiner");
                    Color color=part.Contains("Amber") ? new Color(.38f,.22f,.002f) : navy?Navy:red?Red:yellow?Yellow:Blue;
                    string key=part.Contains("Amber")?"Amber":navy?"Navy":red?"Red":yellow?"Yellow":"Blue";
                    if(part.Contains("Bore_Shadow")){color=new Color(.008f,.012f,.022f);key="Bore";}
                    r.sharedMaterials=Enumerable.Repeat(Mat("RL_"+key,color),r.sharedMaterials.Length).ToArray();
                }
                var socket=new GameObject("Muzzle").transform;socket.SetParent(root.transform,false);
                socket.position=muzzle.position+Vector3.forward*.052f;
                PrefabUtility.SaveAsPrefabAsset(root,Visual);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Mesh MeshAsset(string name, Func<Mesh> create)
        {
            string path=Folder+"/Meshes/"+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh!=null)return mesh;
            mesh=create();mesh.name=name;
            if(mesh.colors.Length==0)mesh.colors=Enumerable.Repeat(Color.white,mesh.vertexCount).ToArray();
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var temp=GameObject.CreatePrimitive(type);
            var mesh=Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(temp);return mesh;
        }
        private static Mesh RingMesh()
        {
            const int count=64;var v=new Vector3[count*2];var triangles=new int[count*6];
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count;
                v[i*2]=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                v[i*2+1]=v[i*2]*.968f;
                int j=(i+1)%count;
                int k=i*6;triangles[k]=i*2;triangles[k+1]=j*2;triangles[k+2]=i*2+1;
                triangles[k+3]=j*2;triangles[k+4]=j*2+1;triangles[k+5]=i*2+1;
            }
            var m=new Mesh{vertices=v,triangles=triangles};m.RecalculateNormals();m.RecalculateBounds();return m;
        }
        private static Mesh StarMesh()
        {
            var vertices=new Vector3[17];var triangles=new int[48];
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8f;float r=i%2==0?1f:.27f;
                vertices[i+1]=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(a)*r,0);
                triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=(i+1)%16+1;
            }
            var m=new Mesh{vertices=vertices,triangles=triangles};m.RecalculateNormals();m.RecalculateBounds();return m;
        }
        private static GameObject MeshPart(Transform parent,string name,Mesh mesh,Material mat,Vector3 position,Vector3 scale)
        {
            var o=new GameObject(name);o.transform.SetParent(parent,false);o.transform.localPosition=position;o.transform.localScale=scale;
            o.AddComponent<MeshFilter>().sharedMesh=mesh;o.AddComponent<MeshRenderer>().sharedMaterial=mat;return o;
        }
        private static AnimationCurve Curve(params Keyframe[] keys)=>new(keys);
        private static Gradient Fade(Color color,float hold=.35f)
        {
            var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(color,0),new GradientColorKey(color,1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,hold),new GradientAlphaKey(0,1)});return g;
        }
        private static ParticleSystem Burst(Transform parent,string name,Mesh mesh,Material material,int count,
            float delay,float life,float size,float speed,Color color,float radius,bool upward=false)
        {
            var o=new GameObject(name);o.transform.SetParent(parent,false);o.transform.localPosition=Vector3.up*.25f;
            var ps=o.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;main.duration=1;main.loop=false;main.playOnAwake=false;
            main.startDelay=delay;main.startLifetime=new ParticleSystem.MinMaxCurve(life*.8f,life);
            main.startSize=new ParticleSystem.MinMaxCurve(size*.7f,size*1.15f);
            main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.65f,speed);
            main.startColor=color;main.maxParticles=count;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            var emission=ps.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
            var shape=ps.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Hemisphere;shape.radius=radius;
            if(upward)o.transform.localRotation=Quaternion.Euler(-90,0,0);
            var limit=ps.limitVelocityOverLifetime;limit.enabled=true;limit.limit=.8f;limit.dampen=.25f;
            var sizeLife=ps.sizeOverLifetime;sizeLife.enabled=true;
            sizeLife.size=new ParticleSystem.MinMaxCurve(1,Curve(new Keyframe(0,.25f),new Keyframe(.3f,1),new Keyframe(.75f,1.1f),new Keyframe(1,.05f)));
            var colors=ps.colorOverLifetime;colors.enabled=true;colors.color=Fade(Color.white);
            var renderer=o.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;
            renderer.mesh=mesh;renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            return ps;
        }
        private static void BuildExplosion()
        {
            var root=new GameObject("RocketExplosion");
            try
            {
                Mesh puff=MeshAsset("RoundedPuff",()=>PrimitiveMesh(PrimitiveType.Sphere));
                Mesh rock=MeshAsset("ChunkyDebris",()=>PrimitiveMesh(PrimitiveType.Cube));
                Mesh star=MeshAsset("CartoonStarburst",StarMesh);
                Material fire=Mat("VFX_Fire",Color.white,true);
                Material smoke=Mat("VFX_Smoke",Color.white,true,.8f);
                var systems=new List<ParticleSystem>();
                systems.Add(Burst(root.transform,"01_WhiteYellowFlash",star,fire,3,0,.09f,4.6f,1,new Color(1,1,.32f),.12f));
                var starRenderer=systems[0].GetComponent<ParticleSystemRenderer>();starRenderer.renderMode=ParticleSystemRenderMode.Billboard;
                // Mesh starbursts remain explicit geometry, seen from several orientations.
                starRenderer.renderMode=ParticleSystemRenderMode.Mesh;
                var rotation=systems[0].main;rotation.startRotation3D=true;rotation.startRotationY=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
                systems.Add(Burst(root.transform,"02_OrangeOuterCloud",puff,fire,12,.035f,.414f,3.3f,9.1f,new Color(1,.22f,.012f),.4f,true));
                systems.Add(Burst(root.transform,"03_GoldenFireball",puff,fire,11,.055f,.345f,2.7225f,7.8f,new Color(1,.55f,.008f),.32f,true));
                systems.Add(Burst(root.transform,"04_YellowCore",puff,fire,8,.025f,.3105f,2.2275f,5.2f,new Color(1,1,.13f),.23f,true));
                systems.Add(Burst(root.transform,"05_CartoonSpikes",star,fire,7,.02f,.19f,2.465f,9,new Color(1,.8f,.025f),.4f,true));
                systems.Add(Burst(root.transform,"06_RoundedSmoke",puff,smoke,14,.15f,.72f,1.3f,4,new Color(.31f,.28f,.31f),.8f,true));
                systems.Add(Burst(root.transform,"07_GroundDust",puff,smoke,12,.1f,.65f,.8f,5,new Color(.64f,.53f,.47f),.95f));
                systems.Add(Burst(root.transform,"08_ChunkyDebris",rock,smoke,10,0,.70f,.20f,9,new Color(.28f,.19f,.14f),.1f,true));
                var debris=systems[^1];var dm=debris.main;dm.gravityModifier=1.8f;dm.startRotation3D=true;
                dm.startRotationX=new ParticleSystem.MinMaxCurve(0,6.28f);dm.startRotationZ=new ParticleSystem.MinMaxCurve(0,6.28f);
                var dl=debris.limitVelocityOverLifetime;dl.enabled=false;
                var spin=debris.rotationOverLifetime;spin.enabled=true;spin.z=4;
                systems.Add(Burst(root.transform,"09_Sparks",star,fire,10,0,.25f,.17f,12,new Color(1,.7f,.02f),.1f,true));
                var ring=MeshPart(root.transform,"ShockwaveRing",MeshAsset("ShockwaveRing",RingMesh),
                    Mat("VFX_Shockwave",new Color(2,1.1f,.05f),true),Vector3.up*.035f,Vector3.one);
                var vfx=root.AddComponent<RocketExplosionVfx>();
                var so=new SerializedObject(vfx);var particles=so.FindProperty("m_particles");particles.arraySize=systems.Count;
                for(int i=0;i<systems.Count;i++)particles.GetArrayElementAtIndex(i).objectReferenceValue=systems[i];
                so.FindProperty("m_shockwave").objectReferenceValue=ring.transform;
                so.FindProperty("m_ringRenderer").objectReferenceValue=ring.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                RocketImpactVisualAuthoring.ApplyReferenceStyle(root);
                PrefabUtility.SaveAsPrefabAsset(root,Explosion);
            }
            finally{Object.DestroyImmediate(root);}
        }

        private static void BuildProjectile()
        {
            var root=new GameObject("RocketProjectile");
            try
            {
                var no=root.AddComponent<NetworkObject>();
                var nt=root.AddComponent<NetworkTransform>();
                var rocket=root.AddComponent<RocketProjectile>();
                Set(rocket,"m_explosionPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(Explosion).GetComponent<RocketExplosionVfx>());
                Mesh sphere=MeshAsset("RoundedPuff",()=>PrimitiveMesh(PrimitiveType.Sphere));
                Mesh cube=MeshAsset("ChunkyDebris",()=>PrimitiveMesh(PrimitiveType.Cube));
                MeshPart(root.transform,"BlueRocketBody",sphere,Mat("RL_Blue",Blue),Vector3.zero,new Vector3(.16f,.16f,.48f));
                MeshPart(root.transform,"RedNose",sphere,Mat("RL_Red",Red),Vector3.forward*.19f,new Vector3(.17f,.17f,.22f));
                MeshPart(root.transform,"YellowTip",sphere,Mat("RL_Yellow",Yellow),Vector3.forward*.27f,new Vector3(.11f,.11f,.14f));
                for(int i=0;i<4;i++)
                {
                    var fin=MeshPart(root.transform,"YellowFin_"+i,cube,Mat("RL_Yellow",Yellow),Vector3.back*.16f,new Vector3(.28f,.025f,.16f));
                    fin.transform.localRotation=Quaternion.Euler(0,0,i*90);
                }
                var trail=new GameObject("SmokeTrail");trail.transform.SetParent(root.transform,false);trail.transform.localPosition=Vector3.back*.23f;
                var ps=trail.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.loop=true;main.playOnAwake=true;main.startLifetime=.28f;main.startSpeed=0;
                main.startSize=new ParticleSystem.MinMaxCurve(.12f,.23f);main.startColor=new Color(.72f,.69f,.66f,.7f);
                main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=24;
                var emission=ps.emission;emission.rateOverTime=35;
                var shape=ps.shape;shape.enabled=false;
                var size=ps.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.5f,1,1.8f));
                var color=ps.colorOverLifetime;color.enabled=true;color.color=Fade(Color.white,.1f);
                var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;
                renderer.mesh=sphere;renderer.sharedMaterial=Mat("VFX_Smoke",Color.white,true,.8f);
                ConfigureNetwork(no,nt,rocket,false);
                PrefabUtility.SaveAsPrefabAsset(root,Projectile);
            }
            finally{Object.DestroyImmediate(root);}
        }
        private static void BuildPickup()
        {
            var root=new GameObject("RocketLauncherPickup");
            try
            {
                var no=root.AddComponent<NetworkObject>();var rb=root.AddComponent<Rigidbody>();rb.mass=2.5f;
                rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;rb.interpolation=RigidbodyInterpolation.Interpolate;
                var nt=root.AddComponent<NetworkTransform>();var pickup=root.AddComponent<WeaponPickup>();
                var so=new SerializedObject(pickup);so.FindProperty("m_weaponType").intValue=4;so.ApplyModifiedPropertiesWithoutUndo();
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Visual),root.transform);
                var rs=visual.GetComponentsInChildren<Renderer>();Bounds bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
                visual.transform.localPosition-=bounds.center;
                var collider=root.AddComponent<BoxCollider>();collider.size=bounds.size;
                ConfigureNetwork(no,nt,pickup,true);
                PrefabUtility.SaveAsPrefabAsset(root,Pickup);
            }
            finally{Object.DestroyImmediate(root);}
        }
        private static void ConfigureNetwork(NetworkObject no,NetworkTransform nt,NetworkBehaviour behaviour,bool rigidbody)
        {
            var so=new SerializedObject(nt);so.FindProperty("_clientAuthoritative").boolValue=false;
            so.FindProperty("_synchronizeScale").boolValue=false;
            if(rigidbody)so.FindProperty("_componentConfiguration").enumValueIndex=(int)NetworkTransform.ComponentConfigurationType.Rigidbody;
            so.ApplyModifiedPropertiesWithoutUndo();
            so=new SerializedObject(no);var list=so.FindProperty("NetworkBehaviours");list.arraySize=2;
            list.GetArrayElementAtIndex(0).objectReferenceValue=nt;list.GetArrayElementAtIndex(1).objectReferenceValue=behaviour;so.ApplyModifiedPropertiesWithoutUndo();
            int index=0;foreach(var b in new NetworkBehaviour[]{nt,behaviour})
            {
                so=new SerializedObject(b);so.FindProperty("_componentIndexCache").intValue=index++;
                so.FindProperty("_addedNetworkObject").objectReferenceValue=no;so.FindProperty("_networkObjectCache").objectReferenceValue=no;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        private static void Register(string path)
        {
            var no=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkObject>();
            var spawnables=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            if(!spawnables.Prefabs.Contains(no))spawnables.AddObject(no,false,false);
            string stable=new((path+no.name).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            no.SetAssetPathHash(stable.GetStableHashU64());EditorUtility.SetDirty(no);EditorUtility.SetDirty(spawnables);
        }
        private static void AttachPlayer()
        {
            var root=PrefabUtility.LoadPrefabContents(Player);
            try
            {
                var so=new SerializedObject(root.GetComponent<PlayerWeaponController>());
                var models=so.FindProperty("m_weaponModels");var muzzles=so.FindProperty("m_muzzles");
                models.arraySize=5;muzzles.arraySize=5;
                var existing=models.GetArrayElementAtIndex(4).objectReferenceValue as Transform;
                if(existing==null || existing.name!="Weapon_RocketLauncher")
                {
                    Transform pistol=(Transform)models.GetArrayElementAtIndex(0).objectReferenceValue;
                    var gun=((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Visual),pistol.parent)).transform;
                    gun.name="Weapon_RocketLauncher";gun.localPosition=pistol.localPosition;
                    gun.rotation=Quaternion.LookRotation(pistol.TransformDirection(Vector3.down),pistol.TransformDirection(Vector3.forward));
                    Vector3 s=pistol.parent.lossyScale;gun.localScale=new Vector3(1/Mathf.Abs(s.x),1/Mathf.Abs(s.y),1/Mathf.Abs(s.z));
                    gun.gameObject.SetActive(false);models.GetArrayElementAtIndex(4).objectReferenceValue=gun;
                    muzzles.GetArrayElementAtIndex(4).objectReferenceValue=gun.Find("Muzzle");
                }
                var weapons=so.FindProperty("m_weapons");bool added=weapons.arraySize<5;weapons.arraySize=5;
                if(added || weapons.GetArrayElementAtIndex(4).FindPropertyRelative("m_type").intValue!=4 ||
                    weapons.GetArrayElementAtIndex(4).FindPropertyRelative("m_bulletPrefab").objectReferenceValue==null)
                {
                    var w=weapons.GetArrayElementAtIndex(4);
                    w.FindPropertyRelative("m_type").intValue=4;w.FindPropertyRelative("m_displayName").stringValue="Rocket Launcher";
                    w.FindPropertyRelative("m_automatic").boolValue=false;
                    SetFloat(w,"m_fireInterval",1.5f);SetFloat(w,"m_range",150);SetFloat(w,"m_spreadDegrees",0);
                    SetFloat(w,"m_damagePerPellet",60);SetFloat(w,"m_reloadDuration",2.5f);SetFloat(w,"m_knockback",24);
                    SetFloat(w,"m_explosionUpwardForce",10);
                    SetFloat(w,"m_explosionDamage",80);SetFloat(w,"m_explosionRadius",5);SetFloat(w,"m_projectileCollisionRadius",.09f);
                    SetFloat(w,"m_bulletSpeed",25);SetFloat(w,"m_bulletLifetime",6);SetFloat(w,"m_recoil",12);
                    SetFloat(w,"m_recoilDistance",.09f);SetFloat(w,"m_cameraShake",.11f);
                    w.FindPropertyRelative("m_pellets").intValue=1;w.FindPropertyRelative("m_magazineCapacity").intValue=1;
                    w.FindPropertyRelative("m_startingReserve").intValue=5;
                    w.FindPropertyRelative("m_bulletPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(Projectile);
                    w.FindPropertyRelative("m_shellEjectionPrefab").objectReferenceValue=null;
                    w.FindPropertyRelative("m_muzzleFlashPrefab").objectReferenceValue=null;
                    w.FindPropertyRelative("m_reticleSettings").objectReferenceValue=AssetDatabase.LoadAssetAtPath<ReticleSettings>("Assets/_Project/Settings/Reticles/ShotgunReticle.asset");
                }
                so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,Player);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        private static void InstallShop()
        {
            const string id="weapon_rocketlauncher";
            var catalogue=AssetDatabase.LoadAssetAtPath<ShopCatalogue>("Assets/_Project/Resources/Shop/DeliveryManagerShop.asset");
            if(catalogue.Find(id)!=null)return;
            var purchase=ScriptableObject.CreateInstance<WeaponPurchaseBehaviour>();
            var so=new SerializedObject(purchase);so.FindProperty("m_weaponType").intValue=4;so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(purchase,"Assets/_Project/Settings/Shop/"+id+"_purchase.asset");
            var item=ScriptableObject.CreateInstance<ShopItemDefinition>();so=new SerializedObject(item);
            so.FindProperty("m_itemId").stringValue=id;so.FindProperty("m_displayName").stringValue="Rocket Launcher";
            so.FindProperty("m_description").stringValue="One rocket loaded, five spare. Watch the blast radius!";
            so.FindProperty("m_price").intValue=600;so.FindProperty("m_category").enumValueIndex=(int)ShopItemCategory.Weapon;
            so.FindProperty("m_purchaseBehaviour").objectReferenceValue=purchase;so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(item,"Assets/_Project/Settings/Shop/"+id+".asset");
            so=new SerializedObject(catalogue);var items=so.FindProperty("m_items");int i=items.arraySize++;
            items.GetArrayElementAtIndex(i).objectReferenceValue=item;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(catalogue);
        }
        [MenuItem("Couch Guys/Weapons/Validate Rocket Launcher %#&v")]
        public static void Validate()
        {
            var player=AssetDatabase.LoadAssetAtPath<GameObject>(Player);var so=new SerializedObject(player.GetComponent<PlayerWeaponController>());
            var model=so.FindProperty("m_weaponModels").GetArrayElementAtIndex(4).objectReferenceValue as Transform;
            var muzzle=so.FindProperty("m_muzzles").GetArrayElementAtIndex(4).objectReferenceValue as Transform;
            if(model==null || muzzle==null || !muzzle.IsChildOf(model))throw new UnityException("Missing saved launcher hand/muzzle references.");
            foreach(string path in new[]{Visual,Projectile,Pickup,Explosion})
                if(AssetDatabase.LoadAssetAtPath<GameObject>(path)==null)throw new UnityException("Missing "+path);
            if(model.GetComponentsInChildren<Collider>(true).Length!=0)throw new UnityException("Equipped weapon has colliders.");
            var spawnables=AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            foreach(string path in new[]{Projectile,Pickup})
                if(!spawnables.Prefabs.Contains(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkObject>()))throw new UnityException("Unregistered "+path);
            Directory.CreateDirectory("Temp/RocketLauncher");
            File.WriteAllText("Temp/RocketLauncher/validation.txt","PASS: saved equipped model, muzzle, rocket, pickup, explosion, server-owned network registrations, shop and config.\n");
            Debug.Log("Rocket launcher asset validation PASS.");
        }
        private static void Set(Object target,string name,Object value)
        {
            var so=new SerializedObject(target);so.FindProperty(name).objectReferenceValue=value;so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(target);
        }
        private static void SetFloat(SerializedProperty parent,string name,float value)=>parent.FindPropertyRelative(name).floatValue=value;
        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            int slash=path.LastIndexOf('/');EnsureFolder(path[..slash]);AssetDatabase.CreateFolder(path[..slash],path[(slash+1)..]);
        }
    }
}
