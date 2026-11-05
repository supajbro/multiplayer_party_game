using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CouchGuys.Gameplay.Healing;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Gameplay.Shop;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace CouchGuys.Editor
{
    // Manual visual migration. Does not run on import, reload or Play.
    public static class HealingItemModelAuthoring
    {
        private const string Models="Assets/_Project/Models/Healing", Prefabs="Assets/_Project/Prefabs/Healing";
        private static readonly string[] Names={"EmergencyDuctTape","ProbablyFineRepairSpray","PremiumRobotOil"};
        private static readonly Dictionary<string,Color> Colours=new()
        {
            ["Heal_TapeBlue"]=new(.23f,.13f,.87f), ["Heal_Cardboard"]=new(.75f,.64f,.49f),
            ["Heal_CardboardEdge"]=new(.87f,.79f,.65f), ["Heal_SprayGreen"]=new(.17f,.82f,.29f),
            ["Heal_Silver"]=new(.82f,.86f,.88f),["Heal_White"]=new(.97f,.99f,1),
            ["Heal_NozzleDark"]=new(.04f,.07f,.05f),["Heal_OilLime"]=new(.51f,.88f,.055f),
            ["Heal_OilPanel"]=new(.07f,.25f,.10f),["Heal_OilRim"]=new(.35f,.67f,.08f)
        };
        [MenuItem("Couch Guys/Healing Items/Apply Blender Models %#&F4")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            Directory.CreateDirectory(Models+"/Materials"); Directory.CreateDirectory(Prefabs+"/Models"); AssetDatabase.Refresh();
            var materials=new Dictionary<string,Material>();
            var template=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Prefabs/Weapons/Materials/CG_Weapon_Blue.mat");
            foreach(var pair in Colours)
            {
                string path=$"{Models}/Materials/{pair.Key}.mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null)
                {
                    material=new Material(template){name=pair.Key}; material.SetColor("_BaseColor",pair.Value);
                    material.SetColor("_RimColor",Color.Lerp(pair.Value,Color.white,.3f));
                    material.SetFloat("_SpecularIntensity",pair.Key=="Heal_Silver" ? .65f : pair.Key.Contains("Cardboard") ? .06f : .35f);
                    AssetDatabase.CreateAsset(material,path);
                }
                materials.Add(pair.Key,material);
            }
            foreach(var name in Names)
            {
                string fbx=$"{Models}/{name}.fbx";
                var importer=(ModelImporter)AssetImporter.GetAtPath(fbx);
                if(importer==null) throw new Exception("Missing FBX: "+fbx);
                importer.globalScale=1; importer.useFileScale=true; importer.importAnimation=false;
                importer.importCameras=false; importer.importLights=false; importer.addCollider=false;
                importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
                foreach(var pair in materials) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),pair.Key),pair.Value);
                importer.SaveAndReimport();
                var fbxAsset=AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                string visualPath=$"{Prefabs}/Models/{name}Model.prefab";
                if(AssetDatabase.LoadAssetAtPath<GameObject>(visualPath)==null)
                {
                    var model=(GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                    try { model.name=name+"Model"; PrefabUtility.SaveAsPrefabAsset(model,visualPath); }
                    finally { Object.DestroyImmediate(model); }
                }
                var root=PrefabUtility.LoadPrefabContents($"{Prefabs}/{name}Pickup.prefab");
                try
                {
                    // Existing pickups have root physics/networking; only child visuals are replaced.
                    var oldVisual=root.transform.Find("Visual");
                    if(oldVisual!=null) Object.DestroyImmediate(oldVisual.gameObject);
                    else foreach(Transform child in root.transform.Cast<Transform>().ToArray())
                        if(child.GetComponent<Renderer>()!=null) Object.DestroyImmediate(child.gameObject);
                    var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(visualPath),root.transform);
                    visual.name="Visual"; visual.transform.localPosition=Vector3.zero;
                    visual.transform.localRotation=Quaternion.identity; visual.transform.localScale=Vector3.one;
                    // Keep the existing collider component and its settings, fitting only its
                    // centre/size to the authored mesh so small items rest on the ground.
                    Bounds fit=new Bounds(); bool foundMesh=false;
                    foreach(var filter in visual.GetComponentsInChildren<MeshFilter>())
                    {
                        Bounds meshBounds=filter.sharedMesh.bounds;
                        for(int corner=0;corner<8;corner++)
                        {
                            Vector3 sign=new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1);
                            Vector3 point=root.transform.InverseTransformPoint(filter.transform.TransformPoint(meshBounds.center+Vector3.Scale(meshBounds.extents,sign)));
                            if(!foundMesh){fit=new Bounds(point,Vector3.zero);foundMesh=true;}else fit.Encapsulate(point);
                        }
                    }
                    var collider=root.GetComponent<BoxCollider>();
                    if(collider!=null && foundMesh){collider.center=fit.center;collider.size=Vector3.Max(fit.size,Vector3.one*.03f);}
                    PrefabUtility.SaveAsPrefabAsset(root,$"{Prefabs}/{name}Pickup.prefab");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var player=PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player.prefab");
            try
            {
                var inventory=player.GetComponent<PlayerWeaponController>(); var data=new SerializedObject(inventory);
                var pistol=(Transform)data.FindProperty("m_weaponModels").GetArrayElementAtIndex(0).objectReferenceValue;
                if(pistol==null) throw new Exception("Existing hand attachment missing.");
                var held=data.FindProperty("m_healingModels"); held.arraySize=3;
                for(int i=0;i<3;i++)
                {
                    var old=pistol.parent.Find("Held_"+Names[i]);
                    if(old==null)
                    {
                        var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Models/{Names[i]}Model.prefab"),pistol.parent);
                        visual.name="Held_"+Names[i]; old=visual.transform;
                        // Authored metres; compensate for the imported rig's attachment scale.
                        Vector3 scale=pistol.parent.lossyScale;
                        old.localScale=new Vector3(1/Mathf.Abs(scale.x),1/Mathf.Abs(scale.y),1/Mathf.Abs(scale.z));
                        old.rotation=player.transform.rotation;
                        old.position=pistol.parent.position+player.transform.forward*.06f+player.transform.up*.05f;
                        old.gameObject.SetActive(false);
                    }
                    held.GetArrayElementAtIndex(i).objectReferenceValue=old;
                }
                data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(player,"Assets/_Project/Prefabs/Player.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets(); Validate(); RenderPreviews();
        }
        [MenuItem("Couch Guys/Healing Items/Render Model Previews")]
        public static void RenderPreviews()
        {
            Directory.CreateDirectory("Tools/Blender/Previews/Healing/Unity");
            for(int i=0;i<3;i++)
            {
                RenderPreview(AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/{Names[i]}Pickup.prefab"),Names[i]+"Pickup",i,false);
                RenderPreview(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"),Names[i]+"Held",i,true);
            }
        }
        private static void RenderPreview(GameObject prefab,string name,int index,bool held)
        {
            var preview=new PreviewRenderUtility();
            try
            {
                var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab); preview.AddSingleGO(root);
                if(held)
                {
                    var inventory=root.GetComponent<PlayerWeaponController>();
                    typeof(PlayerWeaponController).GetMethod("SetModelVisibility",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                        .Invoke(inventory,new object[]{6+index});
                    foreach(var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled=false;
                }
                Bounds bounds=new Bounds(root.transform.position,Vector3.zero); bool found=false;
                foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                { if(!renderer.enabled || renderer is ParticleSystemRenderer) continue; if(!found){bounds=renderer.bounds;found=true;} else bounds.Encapsulate(renderer.bounds); }
                float size=Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z);
                preview.camera.orthographic=true; preview.camera.orthographicSize=size*.7f;
                preview.camera.transform.position=bounds.center+new Vector3(.65f,.35f,1.2f)*size*2;
                preview.camera.transform.LookAt(bounds.center); preview.camera.nearClipPlane=.01f; preview.camera.farClipPlane=100;
                preview.camera.clearFlags=CameraClearFlags.SolidColor; preview.camera.backgroundColor=new Color(.13f,.15f,.19f);
                preview.lights[0].intensity=1.3f; preview.lights[0].transform.rotation=Quaternion.Euler(40,25,0);
                preview.lights[1].intensity=.8f; preview.ambientColor=new Color(.4f,.4f,.4f);
                preview.BeginStaticPreview(new Rect(0,0,650,650));preview.Render(true);
                var texture=preview.EndStaticPreview();File.WriteAllBytes($"Tools/Blender/Previews/Healing/Unity/{name}.png",texture.EncodeToPNG());Object.DestroyImmediate(texture);
            }
            finally { preview.Cleanup(); }
        }
        [MenuItem("Couch Guys/Healing Items/Validate Blender Models")]
        public static void Validate()
        {
            var report=new List<string>(); var catalogue=HealingItemCatalogue.Load();
            var shop=AssetDatabase.LoadAssetAtPath<ShopCatalogue>(ShopAssetBuilder.CataloguePath);
            var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            var held=new SerializedObject(player.GetComponent<PlayerWeaponController>()).FindProperty("m_healingModels");
            for(int i=0;i<3;i++)
            {
                var settings=catalogue.Items[i]; var pickup=settings.PickupPrefab;
                if(settings.name!=Names[i] || settings.HealingPercentage!=new[]{10,50,100}[i]) throw new Exception("Healing definition changed");
                if(shop.Find(Names[i]).PurchaseBehaviour!=settings || pickup.HealingSettings!=settings) throw new Exception("Shop/pickup settings mismatch");
                if(pickup.GetComponent<Rigidbody>()==null || pickup.GetComponent<Collider>()==null || pickup.GetComponent<FishNet.Component.Transforming.NetworkTransform>()==null) throw new Exception("Pickup components missing");
                var visual=pickup.transform.Find("Visual"); if(visual==null || visual.GetComponentsInChildren<MeshFilter>().Length==0) throw new Exception("Pickup model missing");
                var hand=(Transform)held.GetArrayElementAtIndex(i).objectReferenceValue;
                if(hand==null || !hand.parent.name.Contains("Hand")) throw new Exception("Held hand reference missing: "+hand?.parent.name);
                foreach(var renderer in visual.GetComponentsInChildren<Renderer>())
                    if(renderer.sharedMaterials.Any(m=>m==null || m.shader==null)) throw new Exception("Missing model material");
                int triangles=visual.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh.triangles.Length/3);
                report.Add($"PASS {Names[i]}: FBX imported; {triangles} triangles; saved pickup and hand models; shop/config/network components intact; healing {settings.HealingPercentage}%.");
            }
            File.WriteAllLines("Temp/HealingModelValidation.txt",report);Debug.Log(string.Join("\n",report));
        }
    }
}

