using System;
using System.IO;
using System.Linq;
using System.Text;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CouchGuys.EditorTools
{
    public static class TunnelValidation
    {
        [MenuItem("Tools/Couch Guys/Tunnel/Validate In Current Scene")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run tunnel authoring validation outside Play mode.");
            var previous=SceneManager.GetActiveScene();
            var opened=default(Scene);
            if(!Object.FindFirstObjectByType<NeighbourhoodGenerator>())
            {
                opened=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Additive);
                SceneManager.SetActiveScene(opened);
            }
            try { ValidateCurrent(); }
            finally
            {
                if(opened.IsValid()) { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(opened,true); }
            }
        }

        static void ValidateCurrent()
        {
            var generator=Object.FindFirstObjectByType<NeighbourhoodGenerator>();
            if (!generator) throw new InvalidOperationException("Open the existing gameplay scene first.");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(TunnelAuthoring.PrefabPath);
            if (!prefab || !prefab.GetComponent<TunnelGeometry>()) throw new InvalidOperationException("Missing saved tunnel.");
            var report=new StringBuilder();
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>())
                report.AppendLine($"{filter.name}: bounds={filter.sharedMesh.bounds}, vertices={filter.sharedMesh.vertexCount}");
            foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>())
                if(renderer.sharedMaterials.Any(m=>!m || !m.shader || ShaderUtil.ShaderHasError(m.shader)))
                    throw new InvalidOperationException("Missing/invalid tunnel material.");
            int original=generator.CurrentSeed!=0?generator.CurrentSeed:12345;
            try
            {
                foreach(int seed in new[]{original,10002,10007}.Distinct())
                {
                    generator.Generate(seed); Physics.SyncTransforms();
                    var tunnels=generator.GeneratedBorder.GetComponentsInChildren<TunnelGeometry>();
                    if(tunnels.Length!=generator.TrafficRoadPortals.Count+(generator.HasSummitExit?1:0))
                        throw new InvalidOperationException("Missing tunnel placement.");
                    foreach(var tunnel in tunnels)
                    {
                        if(tunnel.GetComponentsInChildren<MeshCollider>().Length!=7)
                            throw new InvalidOperationException("Missing saved tunnel shell colliders.");
                        // Three lanes of truck-sized clearance boxes across the full tunnel.
                        for(int step=0;step<=12;step++)
                        foreach(float x in new[]{-3f,0f,3f})
                        {
                            var center=tunnel.transform.TransformPoint(new Vector3(x,2.45f,-6f+step));
                            var hits=Physics.OverlapBox(center,new Vector3(1.45f,1.9f,.5f),tunnel.transform.rotation,~0,QueryTriggerInteraction.Ignore);
                            if(hits.Any(c=>c.transform.IsChildOf(tunnel.transform)))
                                throw new InvalidOperationException($"Truck clearance obstructed at {tunnel.name}, x={x}, step={step}");
                        }
                        var trigger=tunnel.GetComponentInChildren<RegionTransitionTrigger>(true);
                        if(!trigger || trigger.gameObject.activeSelf!=(tunnel.name=="SummitRegionGateway"))
                            throw new InvalidOperationException("Gateway trigger activation changed.");
                    }
                    report.AppendLine($"PASS seed {seed}: {tunnels.Length} tunnels, 12m through-clearance for players/NPC cars/trucks; saved colliders/materials and summit trigger verified.");
                }
            }
            finally { generator.Generate(original); }
            Physics.SyncTransforms();
            Render(generator);
            report.AppendLine("Limitations: edit-mode geometry/physics checks only; no network host/client drive-through or gameplay trigger event test.");
            File.WriteAllText("ArtSource/Environment/Tunnel/UnityValidation.txt",report.ToString());
            Debug.Log(report.ToString());
        }

        static void Render(NeighbourhoodGenerator generator)
        {
            var tunnel=generator.GeneratedBorder.GetComponentsInChildren<TunnelGeometry>().First(t=>t.name.StartsWith("Boundary"));
            var go=new GameObject("Temporary tunnel validation camera"); var camera=go.AddComponent<Camera>();
            var rt=new RenderTexture(1100,760,24); var previous=RenderTexture.active;
            try
            {
                camera.transform.position=tunnel.transform.TransformPoint(new Vector3(-10f,5.4f,-22f));
                camera.transform.LookAt(tunnel.transform.TransformPoint(new Vector3(0,3f,0)));
                camera.fieldOfView=48; camera.nearClipPlane=.1f; camera.farClipPlane=700;
                camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
                var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
                File.WriteAllBytes("ArtSource/Environment/Tunnel/UnityPreview.png",image.EncodeToPNG()); Object.DestroyImmediate(image);
                Selection.activeGameObject=tunnel.gameObject;
                if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(tunnel.transform.TransformPoint(new Vector3(0,3,0)),camera.transform.rotation,26f);
            }
            finally { RenderTexture.active=previous; camera.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); }
        }
    }
}
