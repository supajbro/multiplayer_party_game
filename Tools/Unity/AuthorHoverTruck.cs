using System;
using System.IO;
using System.Linq;
using CouchGuys.Gameplay.Vehicles;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Explicit in-place authoring in the isolated validation project.</summary>
public static class AuthorHoverTruck
{
    const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/Truck.prefab";
    const string ModelPath = "Assets/_Project/Models/Vehicles/HoverTruck/HoverTruck.fbx";
    const string MaterialFolder = "Assets/_Project/Materials/Vehicles/HoverTruck";
    const string SettingsFolder = "Assets/Settings/Vehicles";

    static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Transform Marker(Transform parent, string name, Vector3 position)
    {
        var result = new GameObject(name).transform;
        result.SetParent(parent, false);
        result.localPosition = position;
        return result;
    }
    static void Reference(SerializedObject target, string name, Object value) => target.FindProperty(name).objectReferenceValue = value;
    static void Array(SerializedObject target, string name, params Object[] values)
    {
        var property = target.FindProperty(name);
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++) property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
    }
    static Transform NormalizePivot(Transform original, Transform parent)
    {
        string name = original.name;
        original.name += "_Imported";
        var pivot = Marker(parent, name, Vector3.zero);
        pivot.SetPositionAndRotation(original.position, parent.rotation);
        foreach (Transform child in original.Cast<Transform>().ToArray()) child.SetParent(pivot, true);
        Object.DestroyImmediate(original.gameObject);
        return pivot;
    }

    public static void Build()
    {
        try
        {
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(SettingsFolder);
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.bakeAxisConversion = true;
            importer.globalScale = 1f;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var controller = root.GetComponent<TruckController>();
                var so = new SerializedObject(controller);
                var visual = (Transform)so.FindProperty("m_visualModel").objectReferenceValue;
                foreach (Transform child in visual.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                visual.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                var imported = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
                PrefabUtility.UnpackPrefabInstance(imported, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                imported.name = "ImportedHoverTruckModel";
                Debug.Log("Imported seat " + Find(imported.transform, "SeatAnchor_0").position + " dock " + Find(imported.transform, "CargoPoint_0").position);
                Vector3 seat0 = root.transform.InverseTransformPoint(Find(imported.transform, "SeatAnchor_0").position);
                Require(seat0.x < 0f && seat0.y > 1.5f && seat0.z > 2f, "Imported model axes or scale are incorrect " + seat0);
                MakeMaterials(imported);

                var driving = Object.Instantiate(controller.Settings);
                driving.name = "Hover Truck Driving";
                AssetDatabase.CreateAsset(driving, SettingsFolder + "/HoverTruckDriving.asset");
                var drive = new SerializedObject(driving);
                drive.FindProperty("m_displayName").stringValue = "Hover Truck";
                drive.FindProperty("m_acceleration").floatValue = 8f;
                drive.FindProperty("m_uphillPowerMultiplier").floatValue = .8f;
                drive.FindProperty("m_cargoCapacity").intValue = 4;
                drive.FindProperty("m_allowDrivingWithDoorsOpen").boolValue = true;
                drive.FindProperty("m_doorInteractionRange").floatValue = 2.35f;
                drive.FindProperty("m_couchLoadingRange").floatValue = 3f;
                drive.FindProperty("m_cameraDistance").floatValue = 8.5f;
                drive.ApplyModifiedPropertiesWithoutUndo();
                var hover = ScriptableObject.CreateInstance<HoverTruckSettings>();
                AssetDatabase.CreateAsset(hover, SettingsFolder + "/HoverTruck.asset");
                var turrets = ScriptableObject.CreateInstance<TruckTurretSettings>();
                AssetDatabase.CreateAsset(turrets, SettingsFolder + "/HoverTruckTurrets.asset");
                turrets.ProjectilePrefab = BuildBolt();
                turrets.ImpactPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Weapons/Effects/Impact_MetalSparks.prefab");
                EditorUtility.SetDirty(turrets);

                var seats = Enumerable.Range(0, 4).Select(i => Find(imported.transform, "SeatAnchor_" + i)).ToArray();
                for (int index = 0; index < seats.Length; index++) seats[index] = NormalizePivot(seats[index], visual);
                var exits = new Transform[4];
                var existingExits = so.FindProperty("m_exitPoints");
                for (int index = 0; index < 4; index++)
                {
                    exits[index] = existingExits.GetArrayElementAtIndex(index).objectReferenceValue as Transform;
                    if (exits[index] == null) exits[index] = Marker(root.transform, "SeatExit_" + index, Vector3.zero);
                    Vector3 seat = root.transform.InverseTransformPoint(seats[index].position);
                    float side = index == 0 || index == 2 ? -1f : 1f;
                    exits[index].localPosition = new Vector3(side * 2.55f, 0f, seat.z);
                    exits[index].localRotation = Quaternion.LookRotation(Vector3.right * side);
                }
                var cargo = new Transform[4];
                var oldCargo = so.FindProperty("m_cargoPoints");
                for (int index = 0; index < 4; index++)
                {
                    cargo[index] = oldCargo.GetArrayElementAtIndex(index).objectReferenceValue as Transform;
                    if (cargo[index] == null) cargo[index] = Marker(visual, "CargoPoint_" + index, Vector3.zero);
                    cargo[index].SetParent(visual, false);
                    cargo[index].position = Find(imported.transform, "CargoPoint_" + index).position;
                    cargo[index].localRotation = Quaternion.Euler(0f, 90f, 0f);
                }
                var yaw = new Transform[3];
                var pitch = new Transform[3];
                var muzzle = new Transform[3];
                for (int index = 0; index < 3; index++)
                {
                    yaw[index] = NormalizePivot(Find(imported.transform, "TurretYaw_" + index), visual);
                    pitch[index] = NormalizePivot(Find(yaw[index], "TurretPitch_" + index), yaw[index]);
                    muzzle[index] = Find(pitch[index], "TurretMuzzle_" + index);
                    muzzle[index].rotation = visual.rotation;
                    Require(Vector3.Dot(muzzle[index].forward, root.transform.forward) > .99f, "Muzzle axis");
                }
                var clamps = Enumerable.Range(0, 8).Select(i => Find(imported.transform, "DockClamp_" + i)).ToArray();
                for (int index = 0; index < clamps.Length; index++) clamps[index] = NormalizePivot(clamps[index], clamps[index].parent);
                var glows = Enumerable.Range(0, 6).Select(i => Find(imported.transform, "HoverGlow_" + i)).ToArray();
                for (int index = 0; index < glows.Length; index++) glows[index] = NormalizePivot(glows[index], glows[index].parent);
                var probes = Enumerable.Range(0, 6).Select(i => Find(imported.transform, "HoverProbe_" + i)).ToArray();

                var colliders = root.transform.Find("Colliders");
                if (colliders != null) Object.DestroyImmediate(colliders.gameObject);
                colliders = Marker(root.transform, "Colliders", Vector3.zero);
                Box(colliders.gameObject, new Vector3(0f, .72f, 0f), new Vector3(3.55f, .6f, 9.4f));
                Box(colliders.gameObject, new Vector3(0f, 1.2f, 4f), new Vector3(3.2f, .7f, 1.2f));
                foreach (float side in new[] { -1f, 1f })
                    Box(colliders.gameObject, new Vector3(side * 1.77f, 1.18f, -1.6f), new Vector3(.18f, .6f, 5.8f));
                var body = root.GetComponent<Rigidbody>();
                body.centerOfMass = new Vector3(0f, .65f, 0f);
                body.constraints = RigidbodyConstraints.None;
                Reference(so, "m_settings", driving);
                Reference(so, "m_hoverSettings", hover);
                Reference(so, "m_turretSettings", turrets);
                Reference(so, "m_destroyedRenderer", Find(imported.transform, "Front_Nose").GetComponent<Renderer>());
                Reference(so, "m_leftDoorPivot", null);
                Reference(so, "m_rightDoorPivot", null);
                Reference(so, "m_shutterSpace", null);
                Array(so, "m_wheels");
                Array(so, "m_shutterSlats");
                Array(so, "m_seatAnchors", seats);
                Array(so, "m_exitPoints", exits);
                Array(so, "m_cargoPoints", cargo);
                Array(so, "m_turretYaw", yaw);
                Array(so, "m_turretPitch", pitch);
                Array(so, "m_turretMuzzles", muzzle);
                Array(so, "m_dockClamps", clamps);
                Array(so, "m_hoverGlows", glows);
                Array(so, "m_hoverProbes", probes);
                var loading = (Transform)so.FindProperty("m_cargoLoadingPoint").objectReferenceValue;
                loading.localPosition = new Vector3(0f, 1f, -4.9f);
                var rear = (Transform)so.FindProperty("m_rearDoorInteractionPoint").objectReferenceValue;
                rear.localPosition = new Vector3(0f, 1f, -5f);
                var cameraTarget = (Transform)so.FindProperty("m_cameraTarget").objectReferenceValue;
                cameraTarget.localPosition = new Vector3(0f, 1.5f, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            ValidateHoverTruck.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void Box(GameObject root, Vector3 centre, Vector3 size)
    {
        var collider = root.AddComponent<BoxCollider>();
        collider.center = centre;
        collider.size = size;
    }

    static HoverTruckBolt BuildBolt()
    {
        const string folder = "Assets/_Project/Prefabs/Vehicles";
        Directory.CreateDirectory(folder);
        var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        try
        {
            root.name = "HoverTruckBolt";
            Object.DestroyImmediate(root.GetComponent<Collider>());
            root.transform.localScale = new Vector3(.11f, .11f, .35f);
            root.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Hover_Cyan.mat");
            root.AddComponent<HoverTruckBolt>();
            var trail = root.AddComponent<TrailRenderer>();
            trail.sharedMaterial = root.GetComponent<Renderer>().sharedMaterial;
            trail.time = .08f;
            trail.startWidth = .075f;
            trail.endWidth = 0f;
            trail.startColor = Color.cyan;
            trail.endColor = new Color(0f, 1f, 1f, 0f);
            return PrefabUtility.SaveAsPrefabAsset(root, folder + "/HoverTruckBolt.prefab").GetComponent<HoverTruckBolt>();
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void MakeMaterials(GameObject model)
    {
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
            {
                string name = source.name;
                string path = MaterialFolder + "/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) return material;
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                material.color = source.color;
                material.SetFloat("_Smoothness", .45f);
                if (source.mainTexture != null) material.mainTexture = source.mainTexture;
                if (name.Contains("Cyan") || name.Contains("Taillight"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", source.color * 3f);
                }
                AssetDatabase.CreateAsset(material, path);
                return material;
            }).ToArray();
        }
    }
}
