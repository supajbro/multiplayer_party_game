using CouchGuys.Gameplay.Vehicles;
using System.Linq;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Managing.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    /// <summary>Builds the network-ready vehicle variants around the shared delivery truck visual.</summary>
    public static class VanPrefabBuilder
    {
        private const float SeatedPlayerRootDrop = .8f;
        private const string SettingsFolder = "Assets/_Project/Settings/Vehicles";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Vehicles";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [InitializeOnLoadMethod]
        private static void BuildMissingVanAssetsAfterImport()
        {
            GameObject light = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/LightVan.prefab");
            Transform imported = light != null ? FindDescendant(light.transform, "ImportedTruckModel") : null;
            if (light == null || imported == null || FindDescendant(light.transform, "Seat_00_Driver") == null ||
                !AreVanPrefabsRegistered())
                EditorApplication.delayCall += BuildVanSystem;
        }

        [MenuItem("Couch Guys/Vehicles/Build Van System")]
        public static void BuildVanSystem()
        {
            EnsureFolder("Assets/_Project/Settings", "Vehicles");
            EnsureFolder("Assets/_Project/Prefabs", "Vehicles");
            VanSettings light = BuildSettings("LightVanSettings", VanQuality.Light, "Light Van", 45f, 60f, .35f, 100f, 5f, .62f, 1.25f);
            VanSettings good = BuildSettings("GoodVanSettings", VanQuality.Good, "Good Van", 65f, 90f, .25f, 175f, 8f, .78f, 1f);
            VanSettings best = BuildSettings("BestVanSettings", VanQuality.Best, "Best Van", 90f, 130f, .16f, 275f, 12f, .9f, .72f);
            BuildPrefab("LightVan", light);
            BuildPrefab("GoodVan", good);
            BuildPrefab("BestVan", best);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RegisterVanPrefabs();
            PlayerPrefabBuilder.BuildPlayerPrefab();
            AddTestVanToScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Couch Guys truck visual, four seats, cargo points, networking, and SampleScene test vehicle built.");
        }

        public static void BuildFromCommandLine() => BuildVanSystem();

        private static VanSettings BuildSettings(string file, VanQuality quality, string displayName,
            float speed, float fuel, float drivingConsumption, float health, float acceleration, float traction, float bulletMultiplier)
        {
            string path = $"{SettingsFolder}/{file}.asset";
            VanSettings settings = AssetDatabase.LoadAssetAtPath<VanSettings>(path);
            if (settings == null) { settings = ScriptableObject.CreateInstance<VanSettings>(); AssetDatabase.CreateAsset(settings, path); }
            SerializedObject so = new(settings);
            Set(so, "m_quality", (int)quality); Set(so, "m_displayName", displayName);
            Set(so, "m_maxForwardSpeed", speed); Set(so, "m_maxFuel", fuel);
            Set(so, "m_fuelConsumptionDriving", drivingConsumption); Set(so, "m_maxHealth", health);
            Set(so, "m_acceleration", acceleration); Set(so, "m_traction", traction);
            Set(so, "m_bulletDamageMultiplier", bulletMultiplier);
            Set(so, "m_collisionDamageMultiplier", quality == VanQuality.Light ? 5f : quality == VanQuality.Good ? 3.5f : 2.3f);
            Set(so, "m_maximumCollisionDamage", quality == VanQuality.Light ? 55f : quality == VanQuality.Good ? 42f : 32f);
            Set(so, "m_steeringSensitivity", quality == VanQuality.Light ? 72f : quality == VanQuality.Good ? 92f : 108f);
            Set(so, "m_highSpeedSteeringMultiplier", quality == VanQuality.Light ? .32f : quality == VanQuality.Good ? .44f : .55f);
            Set(so, "m_vehicleMass", quality == VanQuality.Light ? 1250f : quality == VanQuality.Good ? 1450f : 1650f);
            Set(so, "m_doorOpenAngle", quality == VanQuality.Light ? 100f : quality == VanQuality.Good ? 110f : 120f);
            Set(so, "m_doorAnimationDuration", quality == VanQuality.Light ? .7f : quality == VanQuality.Good ? .55f : .42f);
            SetInteger(so, "m_cargoCapacity", quality == VanQuality.Best ? 2 : 1);
            Set(so, "m_couchLoadingDuration", quality == VanQuality.Light ? 1f : quality == VanQuality.Good ? .72f : .5f);
            Set(so, "m_couchUnloadingDuration", quality == VanQuality.Light ? .85f : quality == VanQuality.Good ? .65f : .48f);
            SetBool(so, "m_allowDrivingWithDoorsOpen", false);
            SetBool(so, "m_autoPositionLoadedCouches", true);
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(settings); return settings;
        }

        private static void BuildPrefab(string name, VanSettings settings)
        {
            GameObject root = new(name);
            try
            {
                Rigidbody rb = root.AddComponent<Rigidbody>(); rb.mass = settings.VehicleMass; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.centerOfMass = new Vector3(0f, 1.05f, -.1f); rb.maxAngularVelocity = 5f;
                NetworkObject networkObject = root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                VanController controller = root.AddComponent<VanController>();

                GameObject visual = Child(root.transform, "VisualModel", Vector3.zero);
                GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Models/truck/truck.fbx");
                if (modelPrefab == null) throw new UnityException("Missing imported truck model: truck.fbx");
                GameObject imported = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, visual.transform);
                imported.name = "ImportedTruckModel";
                // Unity imports this FBX's centimetre-sized, Z-up hierarchy without
                // baking its authored axes. Convert it once at the visual root:
                // source +Z becomes Unity +Y and the imported -X nose becomes +Z.
                imported.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.LookRotation(Vector3.up, Vector3.left));
                imported.transform.localScale = Vector3.one * 100f;
                foreach (Animator animator in imported.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                Bounds bounds = CalculateRendererBounds(imported);
                GameObject colliders = Child(root.transform, "Colliders", Vector3.zero);
                Transform loadingAnchor = Child(root.transform, "CargoLoadingPoint", new Vector3(0f, 1.32f, -4.23f)).transform;
                Vector3 rearDirection = Vector3.ProjectOnPlane(loadingAnchor.position - root.transform.position, Vector3.up).normalized;
                AddCompoundColliders(colliders, bounds, rearDirection);
                Transform[] wheelTransforms =
                {
                    RequireDescendant(imported.transform, "Tire_Front_Left"),
                    RequireDescendant(imported.transform, "Tire_Front_Right"),
                    RequireDescendant(imported.transform, "Tire_Rear_Left"),
                    RequireDescendant(imported.transform, "Tire_Rear_Right")
                };
                Transform seats = Child(root.transform, "Seats", Vector3.zero).transform;
                Transform[] seatAnchors =
                {
                    CreateSeatAnchor(seats, "Seat_00_Driver", RequireDescendant(imported.transform, "SeatAnchor_Front_Right"), root.transform),
                    CreateSeatAnchor(seats, "Seat_01_FrontPassenger", RequireDescendant(imported.transform, "SeatAnchor_Front_Left"), root.transform),
                    CreateSeatAnchor(seats, "Seat_02_RearPassenger", RequireDescendant(imported.transform, "SeatAnchor_Rear_Right"), root.transform),
                    CreateSeatAnchor(seats, "Seat_03_RearPassenger", RequireDescendant(imported.transform, "SeatAnchor_Rear_Left"), root.transform)
                };
                Transform cameraTarget = Child(root.transform, "CameraTarget", new Vector3(0f, Mathf.Clamp(bounds.size.y * .68f, 1.45f, 2f), 0f)).transform;
                Transform leftExit = Child(root.transform, "ExitPointLeft", new Vector3(-1.65f, .95f, 1.25f)).transform;
                Transform rightExit = Child(root.transform, "ExitPointRight", new Vector3(1.65f, .95f, 1.25f)).transform;
                Transform rearInteraction = Child(root.transform, "RearDoorInteractionPoint", new Vector3(.8f, 1.65f, -4.36f)).transform;
                GameObject cargo = Child(root.transform, "Cargo", Vector3.zero);
                int capacity = settings.CargoCapacity;
                Transform[] cargoPoints = new Transform[capacity];
                for (int index = 0; index < capacity; index++)
                {
                    Vector3 position = index == 0
                        ? new Vector3(0f, .89f, -1.22f)
                        : new Vector3(0f, .89f, -2.75f);
                    GameObject point = Child(cargo.transform, $"CargoPoint_{index + 1:00}", position);
                    point.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    cargoPoints[index] = point.transform;
                }
                Renderer renderer = RequireDescendant(imported.transform, "Cab_FrontNose").GetComponent<Renderer>();

                SetReference(controller, "m_settings", settings); SetReference(controller, "m_rigidbody", rb);
                SetReference(controller, "m_visualModel", visual.transform); SetReference(controller, "m_cameraTarget", cameraTarget);
                SetArray(controller, "m_seatAnchors", seatAnchors); SetReference(controller, "m_destroyedRenderer", renderer);
                SetArray(controller, "m_exitPoints", new Object[] { leftExit, rightExit }); SetArray(controller, "m_wheels", wheelTransforms);
                SetReference(controller, "m_rearDoorInteractionPoint", rearInteraction); SetReference(controller, "m_cargoLoadingPoint", loadingAnchor);
                SetArray(controller, "m_cargoPoints", cargoPoints);

                SerializedObject nt = new(networkTransform);
                nt.FindProperty("_componentConfiguration").enumValueIndex = (int)NetworkTransform.ComponentConfigurationType.Rigidbody;
                nt.FindProperty("_clientAuthoritative").boolValue = false; nt.FindProperty("_synchronizePosition").boolValue = true;
                nt.FindProperty("_synchronizeRotation").boolValue = true; nt.FindProperty("_synchronizeScale").boolValue = false; nt.ApplyModifiedPropertiesWithoutUndo();
                ConfigureNetworkBehaviours(networkObject, networkTransform, controller);
                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{name}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AddCompoundColliders(GameObject colliders, Bounds bounds, Vector3 rearDirection)
        {
            float rearSign = Mathf.Abs(rearDirection.z) >= Mathf.Abs(rearDirection.x) ? Mathf.Sign(rearDirection.z) : 1f;
            AddBox(colliders, new Vector3(bounds.center.x, bounds.min.y + .28f, bounds.center.z), new Vector3(bounds.size.x * .84f, .56f, bounds.size.z * .92f));
            AddBox(colliders, new Vector3(bounds.center.x, bounds.max.y - .12f, bounds.center.z), new Vector3(bounds.size.x * .88f, .24f, bounds.size.z * .72f));
            float wallHeight = Mathf.Max(.5f, bounds.size.y - .72f);
            AddBox(colliders, new Vector3(bounds.min.x + .09f, bounds.min.y + .36f + wallHeight * .5f, bounds.center.z), new Vector3(.18f, wallHeight, bounds.size.z * .72f));
            AddBox(colliders, new Vector3(bounds.max.x - .09f, bounds.min.y + .36f + wallHeight * .5f, bounds.center.z), new Vector3(.18f, wallHeight, bounds.size.z * .72f));
            float frontZ = bounds.center.z - rearSign * (bounds.extents.z - .18f);
            AddBox(colliders, new Vector3(bounds.center.x, bounds.center.y, frontZ), new Vector3(bounds.size.x * .88f, bounds.size.y * .78f, .36f));
        }

        private static void AddBox(GameObject target, Vector3 centre, Vector3 size)
        {
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.center = centre; collider.size = size;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new UnityException($"{root.name} contains no renderers.");
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static Transform RequireDescendant(Transform root, string name)
        {
            Transform result = FindDescendant(root, name);
            if (result == null) throw new UnityException($"Imported van '{root.name}' is missing required transform '{name}'.");
            return result;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform CreateSeatAnchor(
            Transform parent,
            string name,
            Transform modelMarker,
            Transform vehicleRoot)
        {
            Vector3 seatPosition = vehicleRoot.InverseTransformPoint(modelMarker.position) -
                Vector3.up * SeatedPlayerRootDrop;
            GameObject anchor = Child(parent, name, seatPosition);
            anchor.transform.localRotation = Quaternion.identity;
            return anchor.transform;
        }

        private static void ConfigureNetworkBehaviours(NetworkObject no, NetworkTransform nt, VanController controller)
        {
            SerializedObject nso = new(no); SerializedProperty list = nso.FindProperty("NetworkBehaviours"); list.arraySize = 2;
            list.GetArrayElementAtIndex(0).objectReferenceValue = nt; list.GetArrayElementAtIndex(1).objectReferenceValue = controller; nso.ApplyModifiedPropertiesWithoutUndo();
            ConfigureBehaviour(nt, no, 0); ConfigureBehaviour(controller, no, 1);
        }

        private static void ConfigureBehaviour(NetworkBehaviour behaviour, NetworkObject no, int index)
        {
            SerializedObject so = new(behaviour); so.FindProperty("_componentIndexCache").intValue = index;
            so.FindProperty("_addedNetworkObject").objectReferenceValue = no; so.FindProperty("_networkObjectCache").objectReferenceValue = no; so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddTestVanToScene()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath); bool temporary = !scene.IsValid() || !scene.isLoaded;
            if (temporary) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == "TestVan_Light") Object.DestroyImmediate(root);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/LightVan.prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); instance.name = "TestVan_Light";
            NeighbourhoodGenerator generator = Object.FindFirstObjectByType<NeighbourhoodGenerator>();
            Vector3 position = new(5f, .1f, 5f); Quaternion rotation = Quaternion.identity;
            if (generator != null && generator.GeneratedStartingArea != null)
            {
                Transform road = generator.GeneratedStartingArea.RoadConnection;
                position = road.position - road.forward * 5f + road.right * 3.5f + Vector3.up * .1f;
                rotation = Quaternion.LookRotation(road.forward, Vector3.up);
            }
            instance.transform.SetPositionAndRotation(position, rotation);
            NetworkObject no = instance.GetComponent<NetworkObject>(); no.SetSceneId(0x56414E5F4C495445UL); EditorUtility.SetDirty(no);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            if (temporary) EditorSceneManager.CloseScene(scene, true);
        }

        private static bool AreVanPrefabsRegistered()
        {
            SinglePrefabObjects collection = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            if (collection == null) return false;
            string[] names = { "LightVan", "GoodVan", "BestVan" };
            foreach (string name in names)
            {
                NetworkObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab")?.GetComponent<NetworkObject>();
                if (prefab == null || !collection.Prefabs.Contains(prefab)) return false;
            }
            return true;
        }

        private static void RegisterVanPrefabs()
        {
            SinglePrefabObjects collection = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>("Assets/_Project/Settings/NetworkSpawnablePrefabs.asset");
            if (collection == null) return;
            string[] names = { "LightVan", "GoodVan", "BestVan" };
            foreach (string name in names)
            {
                NetworkObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab")?.GetComponent<NetworkObject>();
                if (prefab != null && !collection.Prefabs.Contains(prefab)) collection.AddObject(prefab, false, false);
            }
            // The FishNet asset postprocessor may run between the three SaveAsPrefabAsset
            // calls and see a partially-built collection. Initialize every collection
            // entry here so newly generated prefabs never retain the sentinel hash of 0.
            foreach (NetworkObject prefab in collection.Prefabs)
            {
                if (prefab == null) continue;
                string pathAndName = $"{AssetDatabase.GetAssetPath(prefab.gameObject)}{prefab.gameObject.name}"
                    .Trim().ToLowerInvariant();
                string stableName = new(pathAndName.Where(character =>
                    (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9')).ToArray());
                prefab.SetAssetPathHash(stableName.GetStableHashU64());
                EditorUtility.SetDirty(prefab);
            }
            EditorUtility.SetDirty(collection);
        }

        private static GameObject Child(Transform parent, string name, Vector3 position) { GameObject go = new(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; return go; }
        private static void EnsureFolder(string parent, string child) { string path = $"{parent}/{child}"; if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child); }
        private static void Set(SerializedObject so, string name, float value) => so.FindProperty(name).floatValue = value;
        private static void Set(SerializedObject so, string name, int value) => so.FindProperty(name).enumValueIndex = value;
        private static void SetInteger(SerializedObject so, string name, int value) => so.FindProperty(name).intValue = value;
        private static void SetBool(SerializedObject so, string name, bool value) => so.FindProperty(name).boolValue = value;
        private static void Set(SerializedObject so, string name, string value) => so.FindProperty(name).stringValue = value;
        private static void SetReference(Object target, string property, Object value) { SerializedObject so = new(target); so.FindProperty(property).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetArray(Object target, string property, Object[] values) { SerializedObject so = new(target); SerializedProperty p = so.FindProperty(property); p.arraySize = values.Length; for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
