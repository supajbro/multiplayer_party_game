using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Editor;
using CouchGuys.Player;
using CouchGuys.Input;
using CouchGuys.ProceduralGeneration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Explicit isolated-project authoring and real CharacterController Play mode probe.</summary>
[InitializeOnLoad]
public static class ValidateJumpingPillow
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Report = "JumpingPillowValidation.txt";
    static IEnumerator probe;
    static float deadline;
    static ValidateJumpingPillow()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Pillow.Probe", false))
            {
                SessionState.SetBool("Pillow.Probe", false);
                deadline = Time.realtimeSinceStartup + 30;
                probe = PlayProbe();
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("Pillow.Exit", false))
                EditorApplication.Exit(SessionState.GetBool("Pillow.Failed", false) ? 1 : 0);
        };
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Log(string message) { File.AppendAllText(Report, message + "\n"); Debug.Log(message); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static float Vertical(ThirdPersonPlayerController player) => (float)typeof(ThirdPersonPlayerController).GetField("m_verticalVelocity", Private).GetValue(player);

    public static void BuildAndRun()
    {
        File.WriteAllText(Report, "Jumping pillow validation\n");
        JumpingPillowAuthoring.Build();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath);
        Require(prefab != null, "Saved prefab missing");
        Require(prefab.GetComponentsInChildren<MeshRenderer>().Length == 2, "Expected surface and anchor renderers");
        Require(prefab.GetComponentsInChildren<Collider>().Length == 1, "Expected one collider");
        Require(prefab.GetComponentsInChildren<MonoBehaviour>().All(x => x != null), "Missing script");
        Require(prefab.GetComponentsInChildren<MeshRenderer>().SelectMany(r => r.sharedMaterials).All(m => m.shader.name == "Couch Guys/Toon Lit"), "Toon materials missing");
        foreach (float height in new[] { -8f, 0f, 72f })
        {
            var instance = Object.Instantiate(prefab, new Vector3(27,height,-19), Quaternion.identity);
            try
            {
                var pillow = instance.GetComponent<JumpingPillow>();
                var filters = instance.GetComponentsInChildren<MeshFilter>();
                var sources = filters.Select(f => f.sharedMesh).ToArray();
                var originals = sources.Select(m => m.vertices).ToArray();
                Func<Vector3,float> terrain = p => height + .17f*(p.x-27) + .09f*(p.z+19) + .1f*Mathf.Sin(p.x);
                pillow.FitToTerrain(terrain);
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int f=0; f<filters.Length; f++)
                    {
                        var actual=filters[f].sharedMesh.vertices;
                        Require(actual.Length==originals[f].Length, "Topology changed");
                        Require(filters[f].sharedMesh.subMeshCount==sources[f].subMeshCount, "Colour sections changed");
                        for (int i=0;i<actual.Length;i++)
                        {
                            Vector3 before=filters[f].transform.TransformPoint(originals[f][i]);
                            Vector3 after=filters[f].transform.TransformPoint(actual[i]);
                            Require(Mathf.Abs(after.y-(before.y+terrain(before)-height+.01f))<.0001f, "Terrain fitting mismatch");
                        }
                        Require(sources[f].vertices.SequenceEqual(originals[f]), "Source mesh was modified");
                    }
                    pillow.FitToTerrain(terrain);
                }
                Physics.SyncTransforms();
                Require(pillow.SurfaceCollider.Raycast(new Ray(new Vector3(27,height+20,-19),Vector3.down),out var hit,40), "Fitted collider not beneath crown");
                Require(Mathf.Abs(hit.point.y-(terrain(hit.point)+1.34f+.01f))<.03f, "Collider terrain height mismatch");
            }
            finally { Object.DestroyImmediate(instance); }
        }
        Log("PASS: saved prefab, toon materials, source isolation, repeated fitting and collider alignment at -8 / 0 / 72 m on uneven slopes.");
        SessionState.SetBool("Pillow.Probe", true);
        SessionState.SetBool("Pillow.Exit", true);
        SessionState.SetBool("Pillow.Failed", false);
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (probe == null || !EditorApplication.isPlaying) return;
        try
        {
            Require(Time.realtimeSinceStartup<deadline,"Play probe timed out");
            if (probe.MoveNext()) return;
            Log("PASS: all jumping pillow checks.");
        }
        catch(Exception e) { Log("FAIL: "+e); SessionState.SetBool("Pillow.Failed",true); }
        probe=null;
        EditorApplication.ExitPlaymode();
    }

    static IEnumerator PlayProbe()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(JumpingPillowAuthoring.PrefabPath);
        var root=Object.Instantiate(prefab,new Vector3(0,12,0),Quaternion.identity);
        var pillow=root.GetComponent<JumpingPillow>();
        pillow.FitToTerrain(p=>12f);
        Physics.SyncTransforms();
        var players=new ThirdPersonPlayerController[2];
        for(int i=0;i<2;i++)
        {
            var go=new GameObject("PillowPlayer"+i);
            go.transform.position=new Vector3(i==0?-2:2,20,0);
            var cc=go.AddComponent<CharacterController>(); cc.center=Vector3.up; cc.height=2; cc.radius=.35f;
            go.AddComponent<PlayerInputReader>().enabled=false;
            players[i]=go.AddComponent<ThirdPersonPlayerController>();
            // Check contact filtering with the actual saved collider, before falling.
            var bounce=typeof(ThirdPersonPlayerController).GetMethod("TryBounceFromPillow",Private);
            Set(players[i],"m_verticalVelocity",-12f);
            Require(!(bool)bounce.Invoke(players[i],new object[]{pillow.SurfaceCollider,Vector3.right}),"Side contact bounced");
            Require(!(bool)bounce.Invoke(players[i],new object[]{pillow.SurfaceCollider,Vector3.down}),"Underside contact bounced");
            Set(players[i],"m_verticalVelocity",2f);
            Require(!(bool)bounce.Invoke(players[i],new object[]{pillow.SurfaceCollider,Vector3.up}),"Ascending player bounced");
            Set(players[i],"m_verticalVelocity",-1f);
            Require(!(bool)bounce.Invoke(players[i],new object[]{pillow.SurfaceCollider,Vector3.up}),"Walking contact bounced");
            Set(players[i],"m_verticalVelocity",0f);
        }
        int[] bounces={0,0}; float[] previous={0,0}; float[] launchY={0,0}; float[] peakRise={0,0};
        float end=Time.time+12f;
        while(Time.time<end && (bounces.Min()<2 || peakRise.Min()<2.8f))
        {
            yield return null;
            for(int i=0;i<2;i++)
            {
                float v=Vertical(players[i]);
                if(v>8 && previous[i]<0)
                {
                    bounces[i]++; launchY[i]=players[i].transform.position.y;
                    Require(v<14f,"Unbounded bounce speed");
                }
                if(bounces[i]>0) peakRise[i]=Mathf.Max(peakRise[i],players[i].transform.position.y-launchY[i]);
                previous[i]=v;
            }
        }
        Require(bounces.Min()>=2,"Both players must repeatedly bounce from real CharacterController landings");
        Require(peakRise.Min()>2.8f && peakRise.Max()<4.1f,"Bounce height out of range");
        Log("PASS: two simultaneous CharacterControllers repeatedly bounced; rises="+string.Join(", ",peakRise)+"; side, underside, ascending and walking contacts rejected.");
        foreach(var p in players) Object.Destroy(p.gameObject);
        Object.Destroy(root);
    }
}
