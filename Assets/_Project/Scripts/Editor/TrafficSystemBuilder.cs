using System.Linq;
using CouchGuys.Gameplay.Traffic;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    public static class TrafficSystemBuilder
    {
        private const string SettingsDirectory = "Assets/_Project/Resources/Traffic";
        private const string TrafficSettingsPath = SettingsDirectory + "/TrafficSettings.asset";
        private const string CarSettingsPath = SettingsDirectory + "/CarSettings.asset";
        private const string PrefabPath = SettingsDirectory + "/TrafficCar.prefab";
        private const string VisualPrefabDirectory = SettingsDirectory + "/CarModels";
        private const string BodyMaterialPath = SettingsDirectory + "/TrafficCarBody.mat";
        private const string TrimMaterialPath = SettingsDirectory + "/TrafficCarTrim.mat";
        private const string SpawnablesPath = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";

        [InitializeOnLoadMethod]
        private static void BuildMissingAssetsAfterImport()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(VehicleAssetUpgrade.HatchbackPath) != null &&
                (prefab == null || prefab.GetComponentInChildren<TrafficWheelAnimator>(true) == null ||
                    prefab.GetComponentsInChildren<LODGroup>(true).Length != 1))
                EditorApplication.delayCall += BuildTrafficSystem;
        }

        [MenuItem("Couch Guys/Traffic/Build Traffic System")]
        public static void BuildTrafficSystem()
        {
            EnsureDirectory();
            TrafficSettings trafficSettings = LoadOrCreate<TrafficSettings>(TrafficSettingsPath);
            CarSettings carSettings = LoadOrCreate<CarSettings>(CarSettingsPath);
            GameObject[] visualPrefabs = BuildVisualPrefabs();
            NetworkObject prefab = BuildCarPrefab(carSettings, visualPrefabs);
            RegisterPrefab(prefab);
            EditorUtility.SetDirty(trafficSettings);
            EditorUtility.SetDirty(carSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Built traffic settings, {visualPrefabs.Length} car visual prefabs, " +
                      "the server-authoritative traffic car, and FishNet spawn registration.");
        }

        public static void BuildFromCommandLine() => BuildTrafficSystem();

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static NetworkObject BuildCarPrefab(CarSettings settings, GameObject[] visualPrefabs)
        {
            GameObject root = new GameObject("TrafficCar");
            Material bodyMaterial = LoadOrCreateMaterial(
                BodyMaterialPath, "Traffic Car Body", new Color(0.1f, 0.52f, 0.9f));
            Material darkMaterial = LoadOrCreateMaterial(
                TrimMaterialPath, "Traffic Car Trim", new Color(0.055f, 0.065f, 0.075f));
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 1150f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.centerOfMass = new Vector3(0f, -0.25f, 0f);
                body.linearDamping = 0.15f;
                body.angularDamping = 3.5f;
                body.maxAngularVelocity = 4f;
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.9f, 0f);
                collider.size = new Vector3(1.78f, 1.08f, 3.75f);
                for (int side = -1; side <= 1; side += 2)
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    SphereCollider contact = root.AddComponent<SphereCollider>();
                    contact.center = new Vector3(side * 0.78f, 0.36f, axle * 1.28f);
                    contact.radius = 0.36f;
                }

                NetworkObject networkObject = root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                TrafficVehicleController controller = root.AddComponent<TrafficVehicleController>();

                GameObject visualAnchor = new GameObject("VisualAnchor");
                visualAnchor.transform.SetParent(root.transform, false);
                GameObject[] visualVariants = new GameObject[visualPrefabs.Length];
                for (int index = 0; index < visualPrefabs.Length; index++)
                {
                    GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefabs[index]);
                    visual.name = visualPrefabs[index].name;
                    visual.transform.SetParent(visualAnchor.transform, false);
                    visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    visual.transform.localScale = Vector3.one;
                    visual.SetActive(index == 0);
                    visualVariants[index] = visual;
                }
                if (visualPrefabs.Length == 0)
                {
                    CreatePlaceholderVisual(visualAnchor.transform, bodyMaterial, darkMaterial);
                }

                SetReference(controller, "m_settings", settings);
                SetReference(controller, "m_rigidbody", body);
                SetReference(controller, "m_visualAnchor", visualAnchor.transform);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                SerializedObject controllerSo = new SerializedObject(controller);
                SerializedProperty rendererArray = controllerSo.FindProperty("m_renderers");
                rendererArray.arraySize = renderers.Length;
                for (int index = 0; index < renderers.Length; index++)
                    rendererArray.GetArrayElementAtIndex(index).objectReferenceValue = renderers[index];
                SerializedProperty visualArray = controllerSo.FindProperty("m_visualPrefabs");
                visualArray.arraySize = visualPrefabs.Length;
                for (int index = 0; index < visualPrefabs.Length; index++)
                    visualArray.GetArrayElementAtIndex(index).objectReferenceValue = visualPrefabs[index];
                SerializedProperty variantArray = controllerSo.FindProperty("m_visualVariants");
                variantArray.arraySize = visualVariants.Length;
                for (int index = 0; index < visualVariants.Length; index++)
                    variantArray.GetArrayElementAtIndex(index).objectReferenceValue = visualVariants[index];
                controllerSo.ApplyModifiedPropertiesWithoutUndo();
                ConfigureNetwork(networkObject, networkTransform, controller);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null) throw new UnityException($"Failed to save {PrefabPath}.");
                return saved.GetComponent<NetworkObject>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject[] BuildVisualPrefabs()
        {
            EnsureVisualDirectory();
            return new[] { VehicleAssetUpgrade.BuildTrafficVisual() };
        }

        private static void CreatePlaceholderVisual(
            Transform parent,
            Material bodyMaterial,
            Material darkMaterial)
        {
            CreateVisual(parent, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0.72f, 0f), new Vector3(1.82f, 0.72f, 4f), bodyMaterial);
            CreateVisual(parent, PrimitiveType.Cube, "Cabin",
                new Vector3(0f, 1.25f, -0.15f), new Vector3(1.55f, 0.65f, 2f), bodyMaterial);
            CreateVisual(parent, PrimitiveType.Cube, "FrontWindow",
                new Vector3(0f, 1.29f, 0.88f), new Vector3(1.42f, 0.48f, 0.08f), darkMaterial);
            for (int side = -1; side <= 1; side += 2)
            for (int axle = -1; axle <= 1; axle += 2)
            {
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = "Wheel";
                wheel.transform.SetParent(parent, false);
                wheel.transform.localPosition = new Vector3(side * 0.9f, 0.38f, axle * 1.3f);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.transform.localScale = new Vector3(0.62f, 0.2f, 0.62f);
                Object.DestroyImmediate(wheel.GetComponent<Collider>());
                wheel.GetComponent<Renderer>().sharedMaterial = darkMaterial;
            }
        }

        private static Renderer CreateVisual(Transform parent, PrimitiveType type, string name,
            Vector3 position, Vector3 scale, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = scale;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            Renderer renderer = visual.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static Material LoadOrCreateMaterial(string path, string name, Color colour)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { name = name, color = colour };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ConfigureNetwork(NetworkObject no, NetworkTransform nt,
            TrafficVehicleController controller)
        {
            SerializedObject transformSo = new SerializedObject(nt);
            transformSo.FindProperty("_componentConfiguration").enumValueIndex =
                (int)NetworkTransform.ComponentConfigurationType.Rigidbody;
            transformSo.FindProperty("_clientAuthoritative").boolValue = false;
            transformSo.FindProperty("_synchronizePosition").boolValue = true;
            transformSo.FindProperty("_synchronizeRotation").boolValue = true;
            transformSo.FindProperty("_synchronizeScale").boolValue = false;
            transformSo.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject networkSo = new SerializedObject(no);
            SerializedProperty behaviours = networkSo.FindProperty("NetworkBehaviours");
            behaviours.arraySize = 2;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue = nt;
            behaviours.GetArrayElementAtIndex(1).objectReferenceValue = controller;
            networkSo.ApplyModifiedPropertiesWithoutUndo();
            ConfigureBehaviour(nt, no, 0);
            ConfigureBehaviour(controller, no, 1);
        }

        private static void ConfigureBehaviour(NetworkBehaviour behaviour, NetworkObject no, int index)
        {
            SerializedObject so = new SerializedObject(behaviour);
            so.FindProperty("_componentIndexCache").intValue = index;
            so.FindProperty("_addedNetworkObject").objectReferenceValue = no;
            so.FindProperty("_networkObjectCache").objectReferenceValue = no;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterPrefab(NetworkObject prefab)
        {
            SinglePrefabObjects collection = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablesPath);
            if (collection == null) throw new UnityException($"Missing {SpawnablesPath}.");
            if (!collection.Prefabs.Contains(prefab)) collection.AddObject(prefab, false, false);
            string pathAndName = $"{AssetDatabase.GetAssetPath(prefab.gameObject)}{prefab.gameObject.name}"
                .Trim().ToLowerInvariant();
            string stableName = new string(pathAndName.Where(character =>
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9')).ToArray());
            prefab.SetAssetPathHash(stableName.GetStableHashU64());
            EditorUtility.SetDirty(prefab);
            EditorUtility.SetDirty(collection);
        }

        private static void SetReference(Object target, string property, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureDirectory()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources"))
                AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            if (!AssetDatabase.IsValidFolder(SettingsDirectory))
                AssetDatabase.CreateFolder("Assets/_Project/Resources", "Traffic");
        }

        private static void EnsureVisualDirectory()
        {
            EnsureDirectory();
            if (!AssetDatabase.IsValidFolder(VisualPrefabDirectory))
                AssetDatabase.CreateFolder(SettingsDirectory, "CarModels");
        }
    }
}
