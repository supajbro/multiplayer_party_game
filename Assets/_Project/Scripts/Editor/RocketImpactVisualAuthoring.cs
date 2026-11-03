using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.Editor
{
    /// <summary>Explicit edit of the saved impact prefab; never runs on import, reload or Play.</summary>
    public static class RocketImpactVisualAuthoring
    {
        private const string Folder = RocketLauncherAuthoring.Folder;
        [MenuItem("Couch Guys/Weapons/Match Rocket Impact Reference %#&e")]
        public static void UpdateImpact()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before editing the saved impact.");
            var root = PrefabUtility.LoadPrefabContents(RocketLauncherAuthoring.Explosion);
            try
            {
                ApplyReferenceStyle(root);
                PrefabUtility.SaveAsPrefabAsset(root, RocketLauncherAuthoring.Explosion);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            ValidateAndPreview();
        }

        public static void ApplyReferenceStyle(GameObject root)
        {
            ParticleSystem Part(string name) => root.GetComponentsInChildren<ParticleSystem>(true).Single(p => p.name == name);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                // This small unlit shader consumes standard vertex streams rather
                // than Unity's procedural mesh-particle instancing buffer.
                renderer.enableGPUInstancing = false;
                renderer.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream> {
                    ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
                    ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            }
            Material fire = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/VFX_Fire.mat");
            fire.SetFloat("_FireRamp", 1f); EditorUtility.SetDirty(fire);
            Mesh spike = SavedMesh("BlastSpike", SpikeMesh);
            Mesh rock = SavedMesh("ImpactRock", RockMesh);
            Mesh ring = SavedMesh("ReferenceShockwave", GlowRingMesh);

            ConfigureFire(Part("02_OrangeOuterCloud"), new Color(1.3f,.24f,.012f), 1.3f, 2.7f, 4.8f, .03f, .37f, 1.15f);
            ConfigureFire(Part("03_GoldenFireball"), new Color(1.5f,.64f,.015f), 1.05f, 2.35f, 3.8f, .045f, .32f, .8f);
            ConfigureFire(Part("04_YellowCore"), new Color(1.8f,1.45f,.16f), .55f, 1.85f, 2.5f, .015f, .27f, .4f);

            ParticleSystem flash = Part("01_WhiteYellowFlash");
            var fm = flash.main; fm.startLifetime = .085f; fm.startSize = new ParticleSystem.MinMaxCurve(3.8f, 5.4f);
            fm.startColor = new Color(2f, 1.85f, .55f);
            flash.GetComponent<ParticleSystemRenderer>().sharedMaterial = FlatMaterial("VFX_Flash", Color.white);
            var fs = flash.sizeOverLifetime; fs.size = new ParticleSystem.MinMaxCurve(1f, Curve(1f, 1.25f, .01f));

            ParticleSystem spikes = Part("05_CartoonSpikes");
            var sm = spikes.main; sm.startSize = new ParticleSystem.MinMaxCurve(1.8f, 3.0f);
            sm.startLifetime = new ParticleSystem.MinMaxCurve(.14f, .22f); sm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 10f);
            sm.startColor = Color.white;
            var sl = spikes.limitVelocityOverLifetime; sl.enabled = false;
            var sr = spikes.GetComponent<ParticleSystemRenderer>(); sr.mesh = spike;
            sr.alignment = ParticleSystemRenderSpace.Velocity; sr.sharedMaterial = FlatMaterial("VFX_Flash", Color.white);
            var ss = spikes.shape; ss.radius = .7f;
            var sz = spikes.sizeOverLifetime; sz.size = new ParticleSystem.MinMaxCurve(1f, Curve(.6f, 1f, .05f));

            ParticleSystem debris = Part("08_ChunkyDebris");
            debris.GetComponent<ParticleSystemRenderer>().mesh = rock;
            var dm = debris.main; dm.startSize = new ParticleSystem.MinMaxCurve(.14f, .3f);

            // Keep the rounded smoke/dust settings and the existing small particle counts.
            Transform shockwave = root.transform.Find("ShockwaveRing");
            shockwave.GetComponent<MeshFilter>().sharedMesh = ring;
            Material ringMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/VFX_Shockwave.mat");
            ringMaterial.SetFloat("_RingGlow", 1f);
            ringMaterial.SetColor("_BaseColor", new Color(2.8f, 1.15f, .035f, 1f));
            EditorUtility.SetDirty(ringMaterial);
        }

        private static void ConfigureFire(ParticleSystem ps, Color color, float radius, float size,
            float speed, float delay, float lifetime, float height)
        {
            var main = ps.main;
            main.startDelay = delay; main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * .85f, lifetime);
            main.startColor = color; main.startSize = new ParticleSystem.MinMaxCurve(size * .8f, size * 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .55f, speed);
            main.startRotation3D = false;
            ps.transform.localPosition = Vector3.up * height;
            ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = radius;
            var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = 2f; limit.dampen = .12f;
            var growth = ps.sizeOverLifetime;
            growth.size = new ParticleSystem.MinMaxCurve(1f, Curve(.35f, 1.25f, .05f));
            var fade = ps.colorOverLifetime;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f,.55f,.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, .55f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(height * 1000f + 1f);
        }

        private static AnimationCurve Curve(float start, float peak, float end) => new(
            new Keyframe(0f, start), new Keyframe(.35f, peak), new Keyframe(.7f, peak), new Keyframe(1f, end));

        private static Material FlatMaterial(string name, Color color)
        {
            string path = Folder + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Couch Guys/Rocket Cartoon VFX")) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path); return material;
        }

        private static Mesh SavedMesh(string name, Func<Mesh> create)
        {
            string path = Folder + "/Meshes/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = create(); mesh.name = name; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (mesh.colors.Length == 0) mesh.colors = Enumerable.Repeat(Color.white, mesh.vertexCount).ToArray();
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }

        private static Mesh SpikeMesh()
        {
            // A tapered solid shard, readable from every camera direction.
            return new Mesh {
                vertices = new[] { new Vector3(-.13f,0,0), new Vector3(0,.13f,0), new Vector3(.13f,0,0), new Vector3(0,-.13f,0), new Vector3(0,0,1.3f) },
                triangles = new[] {0,1,4,1,2,4,2,3,4,3,0,4,0,3,2,0,2,1},
                colors = new[] {new Color(1,.22f,0),new Color(1,.22f,0),new Color(1,.22f,0),new Color(1,.22f,0),new Color(2,1.7f,.1f)}
            };
        }
        private static Mesh RockMesh()
        {
            Vector3[] points = { Vector3.up*.6f, Vector3.down*.4f, new(-.5f,0,-.35f), new(.4f,0,-.45f), new(.5f,0,.3f), new(-.35f,0,.5f) };
            int[] faces = {0,3,2,0,4,3,0,5,4,0,2,5,1,2,3,1,3,4,1,4,5,1,5,2};
            // Separate face vertices for chunky flat shading.
            return new Mesh { vertices = faces.Select(i=>points[i]).ToArray(), triangles = Enumerable.Range(0,faces.Length).ToArray() };
        }
        private static Mesh GlowRingMesh()
        {
            const int segments = 64;
            var vertices = new Vector3[segments*3]; var uv = new Vector2[vertices.Length];
            var triangles = new int[segments*12];
            for (int i=0;i<segments;i++)
            {
                float angle = i*Mathf.PI*2/segments;
                for(int band=0;band<3;band++)
                {
                    vertices[i*3+band] = new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)) * Mathf.Lerp(.94f,1f,band*.5f);
                    uv[i*3+band] = new Vector2(i/(float)segments,band*.5f);
                }
                for(int band=0;band<2;band++)
                {
                    int a=i*3+band,b=((i+1)%segments)*3+band,k=i*12+band*6;
                    triangles[k]=a;triangles[k+1]=b;triangles[k+2]=a+1;
                    triangles[k+3]=b;triangles[k+4]=b+1;triangles[k+5]=a+1;
                }
            }
            return new Mesh {vertices=vertices,uv=uv,triangles=triangles};
        }

        [MenuItem("Couch Guys/Weapons/Preview Rocket Impact Reference %#&p")]
        public static void ValidateAndPreview()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(RocketLauncherAuthoring.Explosion);
            var particles=prefab.GetComponentsInChildren<ParticleSystem>(true);
            if(particles.Length!=9 || particles.Sum(p=>p.main.maxParticles)>=100) throw new UnityException("Impact particle budget exceeded.");
            foreach(var ps in particles)
            {
                if(ps.main.loop || ps.main.startDelay.constantMax+ps.main.startLifetime.constantMax>1f) throw new UnityException("Impact must clear within one second.");
                if(ShaderUtil.ShaderHasError(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader)) throw new UnityException("Impact shader error.");
            }
            Directory.CreateDirectory("Temp/RocketImpact");
            Scene preview=EditorSceneManager.NewPreviewScene();
            GameObject effect=null, cameraObject=null;
            RenderTexture rt=null; Texture2D capture=null;
            try
            {
                effect=(GameObject)PrefabUtility.InstantiatePrefab(prefab,preview);
                cameraObject=new GameObject("ImpactPreviewCamera"); SceneManager.MoveGameObjectToScene(cameraObject,preview);
                var camera=cameraObject.AddComponent<Camera>(); camera.scene=preview;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.055f,.095f);
                camera.orthographic=true;camera.orthographicSize=4.8f;
                camera.transform.position=new Vector3(8,5,-11);camera.transform.LookAt(new Vector3(0,1.3f,0));
                rt=new RenderTexture(900,700,24);camera.targetTexture=rt;
                foreach(float time in new[]{.06f,.18f,.48f})
                {
                    foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>())ps.Simulate(time,false,true,true);
                    if (Mathf.Abs(time-.18f)<.001f)
                    {
                        var diagnostics = new System.Text.StringBuilder();
                        foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>())
                        {
                            var data = new ParticleSystem.Particle[ps.main.maxParticles];
                            int count = ps.GetParticles(data);
                            var baked = new Mesh();
                            ps.GetComponent<ParticleSystemRenderer>().BakeMesh(baked,camera,ParticleSystemBakeMeshOptions.Default);
                            diagnostics.AppendLine($"{ps.name}: count={count}, start={ps.main.startColor.color}, current={(count>0?data[0].GetCurrentColor(ps):Color.white)}, baked={(baked.colors.Length>0?baked.colors[0]:Color.black)}, mat={ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.GetColor("_BaseColor")}");
                            Object.DestroyImmediate(baked);
                        }
                        File.WriteAllText("Temp/RocketImpact/diagnostics.txt",diagnostics.ToString());
                    }
                    Transform ring=effect.transform.Find("ShockwaveRing");
                    float t=Mathf.Clamp01(time/.4f);ring.localScale=Vector3.one*(5f*(1-Mathf.Pow(1-t,3)));
                    var properties=new MaterialPropertyBlock();properties.SetFloat("_Opacity",1-t*t);ring.GetComponent<Renderer>().SetPropertyBlock(properties);
                    camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;
                    capture=new Texture2D(900,700,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,900,700),0,0);capture.Apply();
                    File.WriteAllBytes($"Temp/RocketImpact/impact-{Mathf.RoundToInt(time*100):00}.png",capture.EncodeToPNG());
                    RenderTexture.active=previous;Object.DestroyImmediate(capture);capture=null;
                }
                File.WriteAllText("Temp/RocketImpact/validation.txt","PASS: saved reference-styled impact; nine local particle layers, under 100 particles, sub-second lifetimes, valid shaders. Previewed flash, peak and dissipation.\n");
            }
            finally
            {
                if(capture!=null)Object.DestroyImmediate(capture);
                if(cameraObject!=null)cameraObject.GetComponent<Camera>().targetTexture=null;
                if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}
                EditorSceneManager.ClosePreviewScene(preview);
            }
            Debug.Log("Rocket impact reference update and validation PASS.");
        }
    }
}
