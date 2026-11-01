using System;
using System.IO;
using System.Linq;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit, one-time authoring. Never invoked by import, reload or Play mode.</summary>
    public static class WaterFountainAuthoring
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Environment/WaterFountain/water_fountain.prefab";
        const string Legacy = "Assets/_Project/Prefabs/Environment/Placeholders/Fountain_Placeholder.prefab";
        const string Model = "Assets/_Project/Models/Environment/WaterFountain/water_fountain.fbx";
        const string Materials = "Assets/_Project/Materials/Environment/WaterFountain";

        [MenuItem("Tools/Couch Guys/Water Fountain/Create Saved Prefab %#&f")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))
            { Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); return; }
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
            importer.bakeAxisConversion = true; importer.generateSecondaryUV = true;
            importer.isReadable = false; importer.SaveAndReimport();
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (!imported) throw new InvalidOperationException("Import water_fountain.fbx first.");
            var mats = new[] {
                Material("Fountain_Stone", new Color(.77f,.68f,.59f), 0,.32f),
                Material("Fountain_Blue", new Color(.025f,.36f,.85f), .2f,.48f),
                Material("Fountain_Inset", new Color(.24f,.26f,.31f), 0,.22f),
                Material("Fountain_Bolts", new Color(.70f,.75f,.79f), .7f,.65f),
                Material("Fountain_Water", new Color(.005f,.38f,.65f), 0,0,"CouchGuys/Environment/FountainWater") };
            var droplets = Material("Fountain_Droplets", new Color(.04f,.8f,1,.85f),0,0,"CouchGuys/Environment/FountainParticle");
            var ripples = Material("Fountain_ImpactRipples", new Color(.3f,.95f,1,.6f),0,0,"CouchGuys/Environment/FountainParticle");
            ripples.SetFloat("_Ring",1);
            bool migrated = AssetDatabase.LoadAssetAtPath<GameObject>(Legacy);
            if (!migrated) throw new InvalidOperationException("Expected the original prefab to preserve its GUID and root fileID.");
            string error = AssetDatabase.MoveAsset(Legacy, PrefabPath);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.name = "water_fountain";
                for (int i=root.transform.childCount-1;i>=0;i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(imported,root.transform);
                model.name = "water_fountain_model";
                foreach (var automaticLOD in model.GetComponentsInChildren<LODGroup>()) Object.DestroyImmediate(automaticLOD);
                // Normalize exported metre bounds once in the saved wrapper, never at runtime.
                var rs = model.GetComponentsInChildren<MeshRenderer>();
                var b = rs.First(r=>r.name.EndsWith("LOD0")).bounds;
                float scale = 5.6f / Mathf.Max(b.size.x,b.size.z);
                model.transform.localScale = Vector3.one*scale;
                model.transform.localPosition = Vector3.up*(-b.min.y*scale);
                foreach (var r in rs)
                {
                    r.sharedMaterials = r.sharedMaterials.Select(m=>mats.FirstOrDefault(x=>m && m.name.StartsWith(x.name,StringComparison.Ordinal)) ?? mats[0]).ToArray();
                    if (r.name.Contains("Water")) { r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false; }
                }
                var group = root.AddComponent<LODGroup>();
                var water = rs.Where(r=>r.name.Contains("Water")).Cast<Renderer>().ToArray();
                group.SetLODs(new[] {
                    new LOD(.22f,rs.Where(r=>r.name.EndsWith("LOD0")).Cast<Renderer>().Concat(water).ToArray()) {fadeTransitionWidth=.15f},
                    new LOD(.10f,rs.Where(r=>r.name.EndsWith("LOD1")).Cast<Renderer>().Concat(water).ToArray()) {fadeTransitionWidth=.2f},
                    new LOD(.012f,rs.Where(r=>r.name.EndsWith("LOD2")).Cast<Renderer>().Concat(water).ToArray()) {fadeTransitionWidth=.2f} });
                group.fadeMode=LODFadeMode.CrossFade; group.animateCrossFading=true; group.RecalculateBounds();
                Collider(root,"Foundation",new Vector3(0,.16f,0),new Vector3(5.5f,.32f,5.5f));
                for(int side=0;side<4;side++)
                {
                    var q=Quaternion.Euler(0,90*side,0);
                    Collider(root,"Basin wall",q*new Vector3(0,.73f,-2.43f),side%2==0?new Vector3(5.35f,.83f,.58f):new Vector3(.58f,.83f,5.35f));
                }
                Collider(root,"Pedestal",new Vector3(0,1.53f,0),new Vector3(1.42f,1.28f,1.42f));
                Collider(root,"Upper bowl",new Vector3(0,2.23f,0),new Vector3(1.59f,.35f,1.59f));

                var jet = Particle(root,"Primary rounded jet",new Vector3(0,2.27f,0),droplets,100,65,1.12f,.17f);
                var main=jet.main; main.startSpeed=new ParticleSystem.MinMaxCurve(4.1f,4.7f); main.gravityModifier=1;
                var shape=jet.shape; shape.enabled=true; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=11; shape.radius=.065f;
                jet.transform.localRotation=Quaternion.Euler(-90,0,0);
                var renderer=jet.GetComponent<ParticleSystemRenderer>(); renderer.renderMode=ParticleSystemRenderMode.Stretch;
                renderer.lengthScale=1.6f; renderer.velocityScale=.035f;
                var small=Particle(root,"Secondary droplets",new Vector3(0,2.27f,0),droplets,30,15,1.14f,.085f);
                main=small.main; main.startSpeed=new ParticleSystem.MinMaxCurve(3.5f,4.5f); main.gravityModifier=1;
                shape=small.shape; shape.enabled=true; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=23; shape.radius=.11f;
                small.transform.localRotation=Quaternion.Euler(-90,0,0);
                // Deterministic landing annulus: trajectory radius ~0.6-1.6 m at basin height.
                var splash=Particle(root,"Landing splashes",new Vector3(0,.95f,0),droplets,28,20,.28f,.065f);
                main=splash.main; main.startSpeed=new ParticleSystem.MinMaxCurve(.5f,1.2f); main.gravityModifier=1;
                shape=splash.shape; shape.enabled=true; shape.shapeType=ParticleSystemShapeType.Donut; shape.radius=1.12f; shape.donutRadius=.27f; shape.angle=0;
                splash.transform.localRotation=Quaternion.Euler(-90,0,0);
                var ripple=Particle(root,"Expanding impact ripples",new Vector3(0,.947f,0),ripples,16,8,.7f,.25f);
                main=ripple.main; main.startSpeed=0; main.startRotation=0;
                shape=ripple.shape; shape.enabled=true; shape.shapeType=ParticleSystemShapeType.Donut; shape.radius=1.12f; shape.donutRadius=.27f;
                ripple.transform.localRotation=Quaternion.Euler(-90,0,0);
                renderer=ripple.GetComponent<ParticleSystemRenderer>(); renderer.renderMode=ParticleSystemRenderMode.HorizontalBillboard;
                var size=ripple.sizeOverLifetime; size.enabled=true; size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.25f,1,2.5f));
                var effects=root.AddComponent<FountainWaterEffects>();
                var so=new SerializedObject(effects); var array=so.FindProperty("m_particles"); array.arraySize=4;
                var systems=new[]{jet,small,splash,ripple};
                for(int i=0;i<systems.Length;i++) array.GetArrayElementAtIndex(i).objectReferenceValue=systems[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Validate();
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Debug.Log("WATER_FOUNTAIN_BUILD_OK");
        }

        static Material Material(string name,Color color,float metallic,float smoothness,string shader="Universal Render Pipeline/Lit")
        {
            string path=$"{Materials}/{name}.mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m) return m;
            var s=shader=="CouchGuys/Environment/FountainWater"
                ? AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/FountainWater.shadergraph") : Shader.Find(shader);
            if(!s) throw new InvalidOperationException("Missing shader: "+shader);
            m=new Material(s){name=name}; m.SetColor("_BaseColor",color);
            if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metallic);
            if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smoothness);
            m.enableInstancing=true; AssetDatabase.CreateAsset(m,path); return m;
        }
        static void Collider(GameObject root,string name,Vector3 center,Vector3 size)
        {
            var c=root.AddComponent<BoxCollider>(); c.center=center; c.size=size;
        }
        static ParticleSystem Particle(GameObject root,string name,Vector3 pos,Material mat,int cap,float rate,float life,float size)
        {
            var go=new GameObject(name); go.transform.SetParent(root.transform,false); go.transform.localPosition=pos;
            var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var m=ps.main; m.loop=true; m.duration=3; m.playOnAwake=false; m.prewarm=false;
            m.simulationSpace=ParticleSystemSimulationSpace.Local; m.cullingMode=ParticleSystemCullingMode.Pause;
            m.maxParticles=cap; m.startLifetime=life; m.startSize=new ParticleSystem.MinMaxCurve(size*.75f,size*1.15f);
            m.startColor=new ParticleSystem.MinMaxGradient(new Color(.25f,.9f,1,.85f),Color.white);
            var emission=ps.emission; emission.rateOverTime=rate;
            var shape=ps.shape; shape.enabled=false;
            var collision=ps.collision; collision.enabled=false;
            var col=ps.colorOverLifetime; col.enabled=true; var g=new Gradient();
            g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.2f,.85f,1),1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.08f),new GradientAlphaKey(.85f,.7f),new GradientAlphaKey(0,1)});
            col.color=g;
            var r=ps.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial=mat; r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false;
            r.maxParticleSize=.4f; return ps;
        }

        [MenuItem("Tools/Couch Guys/Water Fountain/Validate and Render %#&v")]
        public static void Validate()
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if(!asset) throw new InvalidOperationException("Missing fountain prefab");
            if(AssetDatabase.AssetPathToGUID(PrefabPath)!="5f073b482531af8439406b2a846e3004") throw new InvalidOperationException("Original GUID lost");
            var group=asset.GetComponent<LODGroup>(); var lods=group.GetLODs();
            if(asset.GetComponentsInChildren<LODGroup>().Length!=1)throw new InvalidOperationException("Duplicate LOD groups");
            if(lods.Length!=3 || lods.Any(l=>l.renderers.Length!=3)) throw new InvalidOperationException("Invalid LODs");
            if(asset.GetComponentsInChildren<Transform>(true).Any(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)) throw new InvalidOperationException("Missing script");
            foreach(var r in asset.GetComponentsInChildren<Renderer>())
                foreach(var m in r.sharedMaterials)
                    if(!m || !m.shader || ShaderUtil.ShaderHasError(m.shader)) throw new InvalidOperationException("Missing or invalid material/shader on "+r.name);
            var meta=asset.GetComponent<EnvironmentPlacementMetadata>();
            if(meta.Footprint!=new Vector2(5.5f,5.5f) || meta.Clearance!=1)throw new InvalidOperationException("Placement changed");
            if(asset.GetComponents<BoxCollider>().Length!=7)throw new InvalidOperationException("Missing structure colliders");
            // Verify the serialized region reference, independent of region settings implementation.
            string regionText=File.ReadAllText("Assets/_Project/Settings/Regions/Region_Suburbs.asset");
            if(!regionText.Contains("Fountain: {fileID: 7539993096332369416, guid: 5f073b482531af8439406b2a846e3004"))throw new InvalidOperationException("Generator reference changed");
            var preview=EditorSceneManager.NewPreviewScene();
            RenderTexture rt=null; Texture2D image=null;
            try
            {
                var root=(GameObject)PrefabUtility.InstantiatePrefab(asset,preview);
                root.GetComponent<LODGroup>().ForceLOD(0);
                var systems=root.GetComponentsInChildren<ParticleSystem>();
                foreach(var ps in systems)
                {
                    if(ps.collision.enabled)throw new InvalidOperationException("Particle collisions enabled");
                    ps.Simulate(2.3f,true,true,true);
                    if(ps.particleCount==0)throw new InvalidOperationException("No simulated particles: "+ps.name);
                }
                var cameraGO=new GameObject("Fountain preview camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGO,preview);
                var cam=cameraGO.AddComponent<Camera>(); cam.scene=preview; cam.transform.position=new Vector3(6.8f,5.5f,7.6f);
                cam.transform.LookAt(new Vector3(0,1.25f,0)); cam.fieldOfView=39; cam.clearFlags=CameraClearFlags.SolidColor;
                cam.backgroundColor=new Color(.035f,.075f,.15f); cam.nearClipPlane=.1f; cam.farClipPlane=40;
                var lightGO=new GameObject("Preview key light"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGO,preview);
                var light=lightGO.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.6f; light.transform.rotation=Quaternion.Euler(45,-35,0);
                rt=new RenderTexture(1000,900,24); rt.antiAliasing=4; cam.targetTexture=rt; cam.Render();
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials)
                        if (ShaderUtil.ShaderHasError(m.shader)) throw new InvalidOperationException("Shader compilation failed: "+m.shader.name);
                var previous=RenderTexture.active; RenderTexture.active=rt;
                image=new Texture2D(1000,900,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1000,900),0,0); image.Apply(); RenderTexture.active=previous;
                Directory.CreateDirectory("Tools/Blender/Previews"); File.WriteAllBytes("Tools/Blender/Previews/water_fountain_unity.png",image.EncodeToPNG());
                var triangles=lods.Select(l=>l.renderers.OfType<MeshRenderer>().First(r=>r.name.Contains("LOD")).GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0)).ToArray();
                Debug.Log("WATER_FOUNTAIN_VALIDATION_OK: 3 LODs, materials/shaders, 4 simulated particle systems, 7 colliders, original GUID/root reference/footprint. Preview saved.");
                File.WriteAllText("Tools/Blender/Previews/water_fountain_validation.txt","Validated saved prefab: 3 LODs, 4 live particle simulations, no particle collisions, 7 box colliders, material/shader validity, original prefab GUID, root fileID and generator reference.\nNative camera culling is checked separately in Play mode.\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); if(rt)Object.DestroyImmediate(rt); if(image)Object.DestroyImmediate(image); }
        }
    }
}
