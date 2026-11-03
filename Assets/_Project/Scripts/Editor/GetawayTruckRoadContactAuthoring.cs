using System;
using System.IO;
using System.Reflection;
using CouchGuys.Gameplay.Vehicles;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class GetawayTruckRoadContactAuthoring
{
    private const string Path = "Assets/_Project/Prefabs/Vehicles/ThiefGetawayTruck.prefab";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static GetawayTruckRoadContactAuthoring()
    {
        if (SessionState.GetBool("GetawayRoadContact.Validation", false)) EditorApplication.update += RunInPlayMode;
    }

    private static void RunInPlayMode()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.update -= RunInPlayMode;
        SessionState.SetBool("GetawayRoadContact.Validation", false);
        try
        {
            Validate();
            new CouchGuys.Tests.Editor.GetawayTruckMovementTests().RogueMotorDrivesActualTruckThroughTurnsWithoutTipping();
            Debug.Log("PASS GETAWAY FLAT ROAD TURNS AND ESCAPE REGRESSION");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [MenuItem("Couch Guys/Tools/Apply Getaway Road Contacts")]
    public static void Apply()
    {
        var root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var body = root.GetComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.None;
            body.centerOfMass = new Vector3(0f, .35f, -.1f);
            foreach (var box in root.GetComponentsInChildren<BoxCollider>())
            {
                Vector3 size = box.size, centre = box.center;
                // Raise the old flat skid and shell bottoms above the rounded contacts.
                if (size.z > 6f) { centre.y = 1.3f; size.y = .4f; }
                else if ((size.x < .3f || size.z < .5f) && size.y > 2f)
                {
                    float top = centre.y + size.y * .5f;
                    size.y = top - 1.1f; centre.y = (top + 1.1f) * .5f;
                }
                box.size = size; box.center = centre;
            }
            if (root.GetComponents<SphereCollider>().Length == 0)
                foreach (float x in new[] { -1.32f, 1.32f })
                    foreach (float z in new[] { -2.4f, 2.4f })
                    {
                        var contact = root.AddComponent<SphereCollider>();
                        contact.center = new Vector3(x, .45f, z);
                        contact.radius = .45f;
                    }
            PrefabUtility.SaveAsPrefabAsset(root, Path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        if (Application.isBatchMode)
        {
            SessionState.SetBool("GetawayRoadContact.Validation", true);
            EditorApplication.update -= RunInPlayMode;
            EditorApplication.update += RunInPlayMode;
            EditorApplication.EnterPlaymode();
        }
    }

    [MenuItem("Couch Guys/Tests/Validate Getaway Road Contact")]
    public static void Validate()
    {
        // Exercise the actual prefab on flat tile seams, a climb, a crest, and a descent.
        // Manual local-scene physics leaves the user's scene and game session untouched.
        var scene = SceneManager.CreateScene("Getaway road contact regression", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physics = scene.GetPhysicsScene();
        var configuration = Resources.Load<GetawayTruckSettings>("Vehicles/GetawayTruckSettings");
        var origin = new Vector3(41000, 0f, 41000);
        GameObject truck = null;
        try
        {
            void Road(float from, float to, float height, float angle)
            {
                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = "Ground truck regression";
                SceneManager.MoveGameObjectToScene(tile, scene);
                Quaternion rotation = Quaternion.Euler(-angle, 0f, 0f);
                tile.transform.rotation = rotation;
                tile.transform.localScale = new Vector3(18f, 1f, (to - from) / Mathf.Cos(angle * Mathf.Deg2Rad));
                tile.transform.position = origin + new Vector3(0f, height, (from + to) * .5f) - rotation * Vector3.up * .5f;
            }
            float rise = 20f * Mathf.Tan(12f * Mathf.Deg2Rad);
            Road(-30f, -20f, 0f, 0f); Road(-20f, -10f, 0f, 0f); Road(-10f, 0f, 0f, 0f);
            Road(0f, 10f, rise * .25f, 12f); Road(10f, 20f, rise * .75f, 12f);
            Road(20f, 30f, rise, 0f);
            Road(30f, 40f, rise * .75f, -12f); Road(40f, 50f, rise * .25f, -12f);
            Road(50f, 70f, 0f, 0f);
            truck = Object.Instantiate(configuration.TruckPrefab.gameObject, origin + new Vector3(0f, .1f, -24f), Quaternion.identity);
            SceneManager.MoveGameObjectToScene(truck, scene);
            var body = truck.GetComponent<Rigidbody>();
            // Awake is woven to a public method by FishNet, so include both visibilities.
            typeof(TruckController).GetMethod("Awake", Hidden).Invoke(truck.GetComponent<TruckController>(), null);
            Type motorType = typeof(GetawayTruckController).Assembly.GetType("CouchGuys.Gameplay.Vehicles.RogueVehicleMotor", true);
            object motor = Activator.CreateInstance(motorType, Hidden, null, new object[] { body }, null);
            motorType.GetMethod("ConfigureRoadContact", Hidden).Invoke(motor,
                new object[] { configuration.GroundAlignmentStrength, configuration.GroundAlignmentDamping });
            var drive = motorType.GetMethod("Drive", Hidden);
            Vector3 goal = origin + new Vector3(0f, 0f, 60f);
            bool climbed = false, descended = false;
            float maximumLateral = 0f, maximumGap = 0f;
            for (int i = 0; i < 1800; i++)
            {
                drive.Invoke(motor, new object[] { configuration.DrivingSettings, goal, 8f, 1f, .02f, true });
                physics.Simulate(.02f);
                Vector3 p = body.position - origin;
                maximumLateral = Mathf.Max(maximumLateral, Mathf.Abs(p.x));
                if (p.z > 6f && p.z < 14f) climbed |= Vector3.Dot(body.rotation * Vector3.up, Quaternion.Euler(-12f, 0f, 0f) * Vector3.up) > .99f;
                if (p.z > 36f && p.z < 44f) descended |= Vector3.Dot(body.rotation * Vector3.up, Quaternion.Euler(12f, 0f, 0f) * Vector3.up) > .99f;
                float roadHeight = p.z < 0f ? 0f : p.z < 20f ? p.z * Mathf.Tan(12f * Mathf.Deg2Rad) :
                    p.z < 30f ? rise : p.z < 50f ? (50f - p.z) * Mathf.Tan(12f * Mathf.Deg2Rad) : 0f;
                maximumGap = Mathf.Max(maximumGap, p.y - roadHeight);
                if (Vector3.Dot(body.rotation * Vector3.up, Vector3.up) < .8f)
                    throw new Exception("Truck tipped during road-contact regression.");
                if (p.z > 57f) break;
            }
            if (body.position.z - origin.z < 57f || !climbed || !descended || maximumLateral > 1f || maximumGap > .8f)
                throw new Exception($"Road contact failed: position={body.position - origin}, climb={climbed}, descent={descended}, lateral={maximumLateral:F2}, gap={maximumGap:F2}");
            string result = $"PASS GETAWAY ROAD CONTACT: actual prefab crossed tile seams, climbed and descended 12-degree slopes, stayed aligned and upright; maximum ground gap {maximumGap:F2}m.";
            Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/GetawayRoadContact.txt", result);
            Debug.Log(result);
        }
        finally
        {
            if (truck != null) Object.DestroyImmediate(truck);
            foreach (var item in scene.GetRootGameObjects()) Object.DestroyImmediate(item);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
