// Explicit batch-mode asset check. Copied into an isolated temporary project;
// never installed as an import, reload, or Play-mode authoring hook.
using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ValidatePixalAmmunition
{
    static readonly string[] Names = {
        "Shotgun_Shell", "AssaultRifle_Cartridge", "Pistol_Cartridge",
        "AssaultRifle_Bullet", "Pistol_Bullet", "Shotgun_Pellet"
    };

    public static void Run()
    {
        string output = Argument("-ammunitionReport");
        Directory.CreateDirectory(output);
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string name in Names)
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    $"Assets/_Project/Models/Weapons/Ammunition/{name}.asset");
                Require(mesh != null, name + ": Mesh failed to import");
                Require(mesh.vertexCount > 0 && mesh.subMeshCount == 1, name + ": Empty mesh");
                Require(mesh.normals.Length == mesh.vertexCount, name + ": Missing normals");
                Require(mesh.uv.Length == mesh.vertexCount, name + ": Missing UVs");
                Require(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)),
                    name + ": Non-finite vertex");
                Require(mesh.triangles.All(i => i >= 0 && i < mesh.vertexCount), name + ": Invalid index");
                int limit = name.EndsWith("Pellet") ? 96 : name.EndsWith("Bullet") ? 256 : 1200;
                Require(mesh.triangles.Length / 3 <= limit, name + ": Triangle budget exceeded");
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    $"Assets/_Project/Materials/Weapons/Ammunition/{name}.mat");
                Require(material && material.shader && material.shader.name == "Universal Render Pipeline/Lit",
                    name + ": Invalid URP material");
                Require(material.GetTexture("_BaseMap") != null && material.GetTexture("_MetallicGlossMap") != null,
                    name + ": Missing texture");
                bool projectile = name.EndsWith("Bullet") || name.EndsWith("Pellet");
                string folder = projectile ? "Projectiles" : "Ammunition";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/_Project/Prefabs/Weapons/{folder}/{name}.prefab");
                Require(prefab != null, name + ": Prefab failed to import");
                Require(prefab.GetComponent<MeshFilter>().sharedMesh == mesh, name + ": Broken mesh reference");
                Require(prefab.GetComponent<MeshRenderer>().sharedMaterial == material, name + ": Broken material reference");
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) == 0, name + ": Missing script");

                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                if (projectile)
                {
                    Require(instance.GetComponent<TrailRenderer>() != null, name + ": Trail was lost");
                    Require(instance.GetComponent<Collider>() == null, name + ": Unexpected projectile collider");
                    WeaponProjectileVisual visual = instance.GetComponent<WeaponProjectileVisual>();
                    Require(visual != null, name + ": Projectile component was lost");
                    PlayerWeaponType type = name.StartsWith("Pistol") ? PlayerWeaponType.Pistol :
                        name.StartsWith("Assault") ? PlayerWeaponType.AssaultRifle : PlayerWeaponType.Shotgun;
                    WeaponDefinition weapon = WeaponDefinition.CreateDefaults().Single(w => w.Type == type);
                    visual.Launch(Vector3.zero, Vector3.forward * 10, weapon, null);
                    Require(instance.transform.forward.z > .999f, name + ": Wrong firing axis");
                    Require(instance.transform.localScale == weapon.BulletSize, name + ": Incorrect runtime scale");
                    if (name.EndsWith("Pellet"))
                    {
                        Vector3 size = instance.GetComponent<MeshRenderer>().bounds.size;
                        Require(Mathf.Abs(size.x-size.z) < .001f && Mathf.Abs(size.x-size.y) < .001f,
                            "Shotgun pellet stretches instead of remaining round");
                    }
                    instance.GetComponent<TrailRenderer>().enabled = false;
                    instance.transform.rotation = Quaternion.Euler(-90,0,0);
                }
                else
                {
                    Require(Mathf.Abs(mesh.bounds.min.y) < .00001f, name + ": Pivot is not on the base");
                    Require(Mathf.Abs(mesh.bounds.size.x-mesh.bounds.size.z) < .00001f,
                        name + ": Cartridge cross-section is not round");
                }
                RenderViews(instance, material, name, output);
                UnityEngine.Object.DestroyImmediate(instance);
                Debug.Log($"PIXAL_ASSET_PASS {name}: {mesh.vertexCount} vertices, {mesh.triangles.Length/3} triangles");
            }
            File.WriteAllText(Path.Combine(output,"UnityValidation.json"),
                "{\"passed\":true,\"assets\":6,\"checks\":[\"Unity import\",\"mesh and material references\",\"UVs and normals\",\"triangle budgets\",\"projectile launch\",\"preserved trails\",\"round shotgun pellet\",\"three-angle render per asset\"]}");
            Debug.Log("PIXAL_AMMUNITION_VALIDATION_PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output,"UnityValidation.json"),
                "{\"passed\":false,\"error\":\""+error.Message.Replace("\\","\\\\").Replace("\"","\\\"")+"\"}");
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    static void RenderViews(GameObject instance, Material source, string name, string output)
    {
        // Use Unity's built-in preview shader in this empty validation project.
        // Production prefabs retain their generated URP Lit materials.
        Material preview = new(Shader.Find("Standard"));
        preview.mainTexture = source.GetTexture("_BaseMap");
        preview.color = Color.white;
        preview.SetFloat("_Metallic", .15f);
        preview.SetFloat("_Glossiness", .3f);
        instance.GetComponent<MeshRenderer>().sharedMaterial = preview;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.6f,.6f,.6f);
        GameObject lamp = new("Preview key");
        Light light = lamp.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        lamp.transform.rotation = Quaternion.Euler(35,-35,0);
        GameObject cameraObject = new("Preview camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.09f,.11f,.14f);
        camera.orthographic = true;
        camera.nearClipPlane = .0001f;
        camera.farClipPlane = 100;
        Bounds bounds = instance.GetComponent<MeshRenderer>().bounds;
        float height = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y,bounds.size.z));
        camera.orthographicSize = height * .64f;
        Vector3[] views = {new(2,1,-4),new(-2,1,4),new(4,.7f,0)};
        for (int i=0;i<views.Length;i++)
        {
            camera.transform.position = bounds.center + views[i].normalized * height * 4;
            camera.transform.LookAt(bounds.center);
            RenderTexture target = new(512,512,24,RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new(512,512,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,512,512),0,0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(output,$"{name}_{i}.png"),image.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
        UnityEngine.Object.DestroyImmediate(cameraObject);
        UnityEngine.Object.DestroyImmediate(lamp);
        UnityEngine.Object.DestroyImmediate(preview);
    }

    static string Argument(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args,key);
        if (index < 0 || index+1 == args.Length) throw new ArgumentException(key);
        return args[index+1];
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
