// Read-only saved-asset checks in the isolated authoring project.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ValidateThiefEnemy01Playback
{
    const string PrefabPath = "Assets/_Project/Prefabs/Enemies/thief_enemy_01.prefab";
    const string AnimationFolder = "Assets/_Project/Animations/Thieves/ThiefEnemy01";
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "-thiefReport") + 1];
        var lines = new List<string>();
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            foreach (string variant in new[] { "Pistol", "AssaultRifle", "Shotgun" })
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    var animator = root.GetComponent<Animator>();
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>($"{AnimationFolder}/thief_enemy_01_{variant}.controller");
                    animator.Rebind();animator.Update(0);
                    var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    var mesh = new Mesh();
                    foreach (string action in new[] { "Idle", "Walk", "Shoot", "Death", "Frustration" })
                    {
                        string state = "Base Layer." + action;
                        Require(animator.HasState(0, Animator.StringToHash(state)), "Missing controller state " + state);
                        animator.Play(state, 0, .5f);animator.Update(0);
                        Require(animator.GetCurrentAnimatorStateInfo(0).IsName(state), "Failed to enter " + variant + " " + action);
                        foreach (var renderer in renderers)
                        {
                            renderer.BakeMesh(mesh,true);
                            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                            foreach (Vector3 local in mesh.vertices)
                            {
                                Vector3 world = renderer.transform.TransformPoint(local);
                                Require(float.IsFinite(world.x) && float.IsFinite(world.y) && float.IsFinite(world.z) && world.magnitude < 4f, "Invalid skinned vertex during " + state + " renderer=" + renderer.name + " local=" + local + " world=" + world + " scale=" + renderer.transform.lossyScale + " root=" + root.transform.position + " hips=" + root.GetComponentsInChildren<Transform>().First(t=>t.name=="Hips").position);
                                Require(renderer.localBounds.Contains(local), "Animated vertex is outside saved skin bounds: " + variant + " " + state + " " + renderer.name);
                                min = Vector3.Min(min, world); max = Vector3.Max(max, world);
                            }
                            Require((max-min).magnitude > 1f && (max-min).magnitude < 3f, "Unexpected animated model size");
                        }
                        lines.Add("PASS: " + variant + " " + action + " plays through Animator with all four LODs inside their saved bounds.");
                    }
                    Object.DestroyImmediate(mesh);
                    animator.SetBool("Dead", false);animator.SetFloat("Speed", 0);animator.Play("Base Layer.Idle", 0, 0);animator.Update(0);
                    animator.SetFloat("Speed", 1);
                    for (int i=0;i<20;i++) animator.Update(1f/30f);
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Walk"), "Speed did not enter Walk");
                    animator.SetBool("Dead", true);animator.SetTrigger("Shoot");
                    for (int i=0;i<20;i++) animator.Update(1f/30f);
                    Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Death"), "Death did not take precedence over Shoot");
                    lines.Add("PASS: " + variant + " Speed transition and Death priority.");
                }
                finally { Object.DestroyImmediate(root); }
            }
            File.WriteAllLines(Path.Combine(output,"UnityAnimatorPlaybackValidation.txt"),lines);
            Debug.Log("THIEF_ENEMY_01_PLAYBACK_PASS");
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(output,"UnityAnimatorPlaybackFailure.txt"),e.ToString());
            Debug.LogException(e);EditorApplication.Exit(1);
        }
    }
}
