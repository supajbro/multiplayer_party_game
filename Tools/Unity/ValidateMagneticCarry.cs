// Isolated-project behavioral validation. Uses the real controller, rig and effect prefab.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CouchGuys.Player;

[InitializeOnLoad]
public static class ValidateMagneticCarry
{
    const string Key = "MagneticCarryValidationRunning";
    static readonly List<GameObject> Robots = new();
    static readonly List<MagneticCarryEffect> Effects = new();
    static readonly List<Transform> Targets = new();
    static float start;
    static int step;
    static ValidateMagneticCarry() { EditorApplication.playModeStateChanged += State; }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }
    static void Check(bool value, string description)
    { if (!value) throw new Exception(description); Debug.Log("PASS: " + description); }
    static Transform Bone(GameObject robot, string name) => robot.GetComponentsInChildren<Transform>()
        .Single(t => t.name == "CouchGuy_Rig").GetComponentsInChildren<Transform>().Single(t => t.name == name);
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false) || state != PlayModeStateChange.EnteredPlayMode) return;
        try
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/CouchGuy/CouchGuy_Redesigned.fbx");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Animations/CouchGuy.controller");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/VFX/MagneticCarryAura.prefab");
            for (int i = 0; i < 4; i++)
            {
                var robot = UnityEngine.Object.Instantiate(model);
                robot.transform.position = Vector3.right * i * 4;
                foreach (var t in robot.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("Weapon_")) t.gameObject.SetActive(false);
                var animator = robot.GetComponent<Animator>();
                if (animator == null) animator = robot.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.SetBool("Grounded", true); animator.SetFloat("Speed", i % 2 == 0 ? 0 : .6f);
                animator.SetBool("IsCarrying", true); animator.SetTrigger("Pickup");
                var target = new GameObject("Replicated Anchor Test " + i).transform;
                target.position = robot.transform.position + new Vector3(0, .7f, 1.1f);
                var effect = UnityEngine.Object.Instantiate(prefab).GetComponent<MagneticCarryEffect>();
                effect.Attach(Bone(robot, "Hand_L"), Bone(robot, "Hand_R"), Bone(robot, "Torso"), target);
                Robots.Add(robot); Effects.Add(effect); Targets.Add(target);
            }
            start = Time.time; step = 0;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }
    static void Tick()
    {
        try
        {
            float elapsed = Time.time - start;
            if (step == 0 && elapsed > .65f)
            {
                foreach (var robot in Robots)
                {
                    var animator = robot.GetComponent<Animator>();
                    Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Carrying"), "Start transitions into carrying loop");
                    foreach (string side in new[] { "L", "R" })
                    {
                        Vector3 reach = Bone(robot, "Hand_" + side).position - Bone(robot, "UpperArm_" + side).position;
                        Check(reach.z > .2f && Mathf.Abs(reach.x) > .04f, "Both hands extend forward and outward");
                    }
                }
                Check(Effects.All(e => e.GetComponentsInChildren<ParticleSystem>().Any(p => p.particleCount > 0)), "Four independent particle streams remain alive after burst");
                RenderPreview();
                Targets[1].SetPositionAndRotation(new Vector3(4.4f, 1f, 1.5f), Quaternion.Euler(0, 70, 0));
                Robots[1].transform.position += Vector3.left * .15f;
                step++;
            }
            if (step == 1 && elapsed > .8f)
            {
                foreach (var line in Effects[1].GetComponentsInChildren<LineRenderer>().Where(l => !l.loop))
                    Check(Vector3.Distance(line.GetPosition(line.positionCount - 1), Targets[1].TransformPoint(0, .04f, 0)) < .001f,
                        "Streams follow the assigned moving and rotating anchor");
                Effects[0].Release();
                Robots[0].GetComponent<Animator>().SetBool("IsCarrying", false);
                Robots[0].GetComponent<Animator>().SetTrigger("Drop");
                step++;
            }
            if (step == 2 && elapsed > 1.6f)
            {
                Check(Effects[0] == null && Effects.Skip(1).All(e => e != null), "Detaching one player only removes that aura");
                Check(Robots[0].GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "Release returns to locomotion");
                UnityEngine.Object.Destroy(Targets[1].gameObject);
                UnityEngine.Object.Destroy(Robots[2]);
                step++;
            }
            if (step == 3 && elapsed > 2.4f)
            {
                Check(Effects[1] == null && Effects[2] == null && Effects[3] != null, "Destroyed target/body cleans up only its own effect");
                File.WriteAllText("MagneticCarryBehaviorValidation.txt", "PASS: four independent animations and particle streams; animated rig reach; moving/rotating endpoint; independent release; locomotion recovery; target/body destruction cleanup.\n");
                SessionState.SetBool(Key, false);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
            }
            if (elapsed > 15) throw new Exception("Behavior validation timeout");
        }
        catch (Exception e) { Fail(e); }
    }
    static void RenderPreview()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.position = new Vector3(0, -.12f, 0);
        var light = new GameObject("Preview Key").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(40, -35, 0); light.intensity = 1.2f;
        RenderSettings.ambientLight = new Color(.4f, .45f, .6f);
        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(3.2f, 2.5f, 4f);
        camera.transform.LookAt(new Vector3(0, 1, .25f));
        camera.orthographic = true; camera.orthographicSize = 1.65f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .055f, .11f);
        var rt = new RenderTexture(960, 960, 24); camera.targetTexture = rt;
        camera.Render(); RenderTexture.active = rt;
        var image = new Texture2D(960, 960, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 960), 0, 0); image.Apply();
        File.WriteAllBytes("MagneticCarryPreview.png", image.EncodeToPNG());
        RenderTexture.active = null; camera.targetTexture = null;
        UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(image);
    }
    static void Fail(Exception e)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
        Debug.LogException(e); EditorApplication.Exit(1);
    }
}
