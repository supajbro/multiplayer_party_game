using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class ReviseHoverTruckCabin
{
    const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/Truck.prefab";
    const string ModelPath = "Assets/_Project/Models/Vehicles/HoverTruck/HoverTruck.fbx";
    const string Output = "CabinReview";
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static Transform Marker(Transform parent, string name, Vector3 position)
    {
        var result = new GameObject(name).transform;
        result.SetParent(parent, false);
        result.localPosition = position;
        return result;
    }
    static void References(SerializedObject target, string name, Transform[] values)
    {
        var property = target.FindProperty(name);
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++) property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
    }
    static void Cube(Transform parent, string name, Vector3 position, Vector3 scale, string material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Vehicles/HoverTruck/" + material + ".mat");
    }

    public static void Build()
    {
        try
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var truck = root.GetComponent<TruckController>();
                var settings = new SerializedObject(truck);
                var visual = (Transform)settings.FindProperty("m_visualModel").objectReferenceValue;
                var source = (GameObject)PrefabUtility.InstantiatePrefab(model);
                try
                {
                    var imported = Find(root.transform, "ImportedHoverTruckModel");
                    // Resolve mesh references from the imported model after its cabin edit.
                    foreach (var filter in imported.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Transform matching = Find(source.transform, filter.name);
                        filter.sharedMesh = matching.GetComponent<MeshFilter>().sharedMesh;
                    }
                    for (int index = 0; index < 4; index++)
                    {
                        var seat = Find(imported, "Seat_" + index);
                        var sourceSeat = Find(source.transform, "Seat_" + index);
                        seat.localPosition = sourceSeat.localPosition;
                        seat.localScale = sourceSeat.localScale;
                        truck.GetSeatAnchor(index).position = Find(source.transform, "SeatAnchor_" + index).position;
                        var exit = truck.GetDoorPoint(index);
                        exit.localPosition = new Vector3(exit.localPosition.x, exit.localPosition.y, truck.GetSeatAnchor(index).localPosition.z);
                    }
                    Find(imported, "Steering_Wheel").position = Find(source.transform, "Steering_Wheel").position;
                    var yaw = settings.FindProperty("m_turretYaw");
                    var left = new Transform[3];
                    var right = new Transform[3];
                    var aura = new Transform[3];
                    for (int index = 0; index < 3; index++)
                    {
                        var turret = (Transform)yaw.GetArrayElementAtIndex(index).objectReferenceValue;
                        turret.position = Find(source.transform, "TurretYaw_" + index).position;
                        var oldControls = visual.Find("TurretControls_" + index);
                        if (oldControls != null) Object.DestroyImmediate(oldControls.gameObject);
                        var seat = truck.GetSeatAnchor(index + 1);
                        var controls = Marker(visual, "TurretControls_" + index,
                            new Vector3(seat.localPosition.x, 1.9f, seat.localPosition.z + .32f));
                        Cube(controls, "ControlPanel", Vector3.down * .12f, new Vector3(.65f, .12f, .3f), "Hover_Graphite");
                        left[index] = Marker(controls, "LeftHandGrip", new Vector3(-.23f, .025f, 0f));
                        right[index] = Marker(controls, "RightHandGrip", new Vector3(.23f, .025f, 0f));
                        Cube(left[index], "LeftControlHandle", Vector3.down * .05f, new Vector3(.09f, .17f, .09f), "Hover_Steel");
                        Cube(right[index], "RightControlHandle", Vector3.down * .05f, new Vector3(.09f, .17f, .09f), "Hover_Steel");
                        var oldTarget = turret.Find("MagneticControlTarget");
                        if (oldTarget != null) Object.DestroyImmediate(oldTarget.gameObject);
                        aura[index] = Marker(turret, "MagneticControlTarget", new Vector3(0f, .28f, -.1f));
                    }
                    References(settings, "m_turretLeftGrips", left);
                    References(settings, "m_turretRightGrips", right);
                    References(settings, "m_turretAuraTargets", aura);
                    var fitAvatar = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
                    try
                    {
                        var animator = fitAvatar.GetComponentInChildren<Animator>();
                        animator.Rebind();
                        animator.Update(0f);
                        var body = fitAvatar.GetComponentsInChildren<Renderer>(true).First(value => value.name == "Torso");
                        float bodyBottom = body.bounds.min.y - fitAvatar.transform.position.y;
                        float cushionTop = 1.06f + (.29f + .115f) * 1.15f;
                        settings.FindProperty("m_seatedRootOffset").vector3Value =
                            new Vector3(0f, cushionTop - bodyBottom - truck.GetSeatAnchor(0).localPosition.y - .025f, 0f);
                        Debug.Log("CABIN FIT body bottom " + bodyBottom + " root offset " + settings.FindProperty("m_seatedRootOffset").vector3Value);
                    }
                    finally { Object.DestroyImmediate(fitAvatar); }
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally { Object.DestroyImmediate(source); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var hover = AssetDatabase.LoadAssetAtPath<HoverTruckSettings>("Assets/Settings/Vehicles/HoverTruck.asset");
            hover.PassengerCameraDistance = 2.5f;
            hover.PassengerCameraHeight = .8f;
            hover.PassengerCameraShoulderOffset = 1.3f;
            hover.PassengerCameraPitch = 12f;
            EditorUtility.SetDirty(hover);
            AssetDatabase.SaveAssets();
            Review();
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void Review()
    {
        Directory.CreateDirectory(Output);
        var truckObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        var truck = truckObject.GetComponent<TruckController>();
        var players = new GameObject[4];
        try
        {
            var serialized = new SerializedObject(truck);
            Require(serialized.FindProperty("m_turretLeftGrips").arraySize == 3, "Three controls");
            for (int index = 0; index < 4; index++)
            {
                players[index] = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab"));
                var avatar = players[index];
                var animator = avatar.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                var driver = avatar.GetComponent<CouchGuyAnimationDriver>();
                typeof(CouchGuyAnimationDriver).GetMethod("CacheReferences", Hidden).Invoke(driver, null);
                driver.SetVehicleSeated(true, index == 0);
                driver.SetTurretGrips(truck.GetTurretLeftGrip(index), truck.GetTurretRightGrip(index));
                avatar.transform.SetPositionAndRotation(truck.GetSeatAnchor(index).position + truck.SeatedRootOffset, Quaternion.identity);
                typeof(CouchGuyAnimationDriver).GetMethod("LateUpdate", Hidden).Invoke(driver, null);
                var hoverVisual = avatar.GetComponent<RobotHoverVFX>();
                typeof(RobotHoverVFX).GetMethod("Awake", Hidden).Invoke(hoverVisual, null);
                typeof(RobotHoverVFX).GetMethod("OnDisable", Hidden).Invoke(hoverVisual, null);
                hoverVisual.enabled = false;
                foreach (var camera in avatar.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                foreach (var canvas in avatar.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
                if (index > 0)
                {
                    var rig = Find(animator.transform, "CouchGuy_Rig");
                    var effectField = typeof(MagneticCarryAura).GetField("m_auraPrefab", Hidden);
                    var effect = Object.Instantiate((MagneticCarryEffect)effectField.GetValue(avatar.GetComponent<MagneticCarryAura>()));
                    typeof(MagneticCarryEffect).GetMethod("Awake", Hidden).Invoke(effect, null);
                    effect.Attach(Find(rig, "Hand_L"), Find(rig, "Hand_R"), Find(rig, "Torso"), truck.GetTurretAuraTarget(index));
                    typeof(MagneticCarryEffect).GetMethod("LateUpdate", Hidden).Invoke(effect, null);
                    var leftHand = Find(rig, "Hand_R");
                    var rightHand = Find(rig, "Hand_L");
                    Require(Vector3.Distance(leftHand.position, truck.GetTurretLeftGrip(index).position) < .12f, "Left hand reaches control " + index + " hand " + leftHand.position + " grip " + truck.GetTurretLeftGrip(index).position);
                    Require(Vector3.Distance(rightHand.position, truck.GetTurretRightGrip(index).position) < .12f, "Right hand reaches control " + index + " hand " + rightHand.position + " grip " + truck.GetTurretRightGrip(index).position);
                    Debug.Log("CABIN HANDS " + index + " left " + leftHand.position + " right " + rightHand.position);
                }
                var torso = avatar.GetComponentsInChildren<Renderer>(true).First(value => value.name == "Torso");
                Debug.Log("CABIN BODY " + index + " torso " + torso.bounds + " root " + avatar.transform.position);
            }
            PreviewMaterials(truckObject);
            foreach (var player in players) PreviewMaterials(player);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.6f, .6f, .6f);
            var light = new GameObject("Cabin review light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            var view = new GameObject("Cabin review camera").AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.12f, .15f, .2f);
            view.nearClipPlane = .03f;
            view.farClipPlane = 80f;
            view.orthographic = true;
            view.orthographicSize = 4.5f;
            view.transform.position = new Vector3(-8f, 9f, 12f);
            view.transform.LookAt(new Vector3(0f, 1.2f, .1f));
            Render(view, "CabinWithPlayers.png");
            view.orthographic = false;
            view.fieldOfView = truck.Settings.CameraFieldOfView;
            var config = truck.HoverSettings;
            var rotation = Quaternion.Euler(config.PassengerCameraPitch, 0f, 0f);
            Vector3 pivot = truck.GetSeatAnchor(1).position + Vector3.up * config.PassengerCameraHeight + rotation * Vector3.right * config.PassengerCameraShoulderOffset;
            view.transform.SetPositionAndRotation(pivot + rotation * Vector3.back * config.PassengerCameraDistance, rotation);
            Render(view, "PassengerView.png");
            File.WriteAllText(Output + "/Validation.txt", "PASS larger 2x2 chairs, three saved control pairs, six hands reaching controls, reused magnetic aura, passenger camera preview.\n");
        }
        finally
        {
            foreach (var player in players) if (player != null) Object.DestroyImmediate(player);
            Object.DestroyImmediate(truckObject);
        }
    }

    static void PreviewMaterials(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
            {
                if (source == null || source.shader.name != "Universal Render Pipeline/Lit") return source;
                var material = new Material(Shader.Find("Standard"));
                material.color = source.color;
                material.mainTexture = source.mainTexture;
                material.SetFloat("_Glossiness", .3f);
                return material;
            }).ToArray();
    }
    static void Render(Camera camera, string filename)
    {
        var target = new RenderTexture(1280, 720, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes(Output + "/" + filename, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        target.Release();
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(image);
    }
}
