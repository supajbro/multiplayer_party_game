using System.Linq;
using CouchGuys.Gameplay.Shop;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Testing;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Editor
{
    /// <summary>Builds the authored sled model into its gameplay, shop, and test assets.</summary>
    public static class SledSystemBuilder
    {
        private const string ModelPath = "Assets/_Project/Models/Sled/sled_01.fbx";
        private const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/Sled.prefab";
        private const string SettingsPath = "Assets/_Project/Settings/Gameplay/SledSettings.asset";
        private const string SpawnablesPath = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";
        private const string CataloguePath = "Assets/_Project/Resources/Shop/DeliveryManagerShop.asset";
        private const string PurchasePath = "Assets/_Project/Settings/Shop/sled_purchase.asset";
        private const string ItemPath = "Assets/_Project/Settings/Shop/sled.asset";
        private const string TestScenePath = "Assets/Scenes/CouchPhysicsTest.unity";

        [InitializeOnLoadMethod]
        private static void BuildMissingAssetsAfterImport()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                EditorApplication.delayCall += BuildSledSystem;
        }

        [MenuItem("Couch Guys/Vehicles/Build Sled System")]
        public static void BuildSledSystem()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SledSettings settings = BuildSettings();
            NetworkObject prefab = BuildPrefab(settings);
            RegisterPrefab(prefab);
            BuildShopAssets(prefab);
            AssignTestScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Built server-authoritative Sled prefab, settings, shop item, spawn registration, and test-scene reference.");
        }

        public static void BuildFromCommandLine() => BuildSledSystem();

        private static SledSettings BuildSettings()
        {
            SledSettings settings = AssetDatabase.LoadAssetAtPath<SledSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<SledSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            SerializedObject so = new(settings);
            so.FindProperty("m_speedOnePlayer").floatValue = 1.8f;
            so.FindProperty("m_speedTwoPlayers").floatValue = 2.5f;
            so.FindProperty("m_speedThreePlayers").floatValue = 3.2f;
            so.FindProperty("m_speedFourPlayers").floatValue = 4f;
            so.FindProperty("m_steeringStrength").floatValue = 1f;
            so.FindProperty("m_slidingFriction").floatValue = 0.18f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static NetworkObject BuildPrefab(SledSettings settings)
        {
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelPrefab == null) throw new UnityException($"Missing authored sled model at {ModelPath}.");

            GameObject root = new("Sled");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                NetworkObject networkObject = root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                SledController controller = root.AddComponent<SledController>();

                GameObject visualRoot = new("Visual");
                visualRoot.transform.SetParent(root.transform, false);
                GameObject model = PrefabUtility.InstantiatePrefab(modelPrefab, visualRoot.transform) as GameObject;
                if (model == null) throw new UnityException("Could not instantiate sled_01.");
                model.name = "sled_01";
                // The authored Blender file stores metre geometry as centimetre-sized
                // mesh data in Z-up space. Match the established van import convention.
                model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));
                model.transform.localScale = Vector3.one * 100f;
                foreach (Animator animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;

                Bounds bounds = CalculateRendererBounds(model);
                if (bounds.size.x < 1f || bounds.size.x > 6f ||
                    bounds.size.y < 0.3f || bounds.size.y > 2f || bounds.size.z > 5f)
                    throw new UnityException($"sled_01 imported at an unexpected scale/orientation: {bounds.size}.");

                GameObject colliders = Child(root.transform, "CompoundColliders", Vector3.zero);
                AddBox(colliders, new Vector3(0f, 0.56f, 0f), new Vector3(2.45f, 0.16f, 1.68f));
                AddBox(colliders, new Vector3(-0.91f, 0.24f, 0f), new Vector3(0.18f, 0.28f, 2.15f));
                AddBox(colliders, new Vector3(0.91f, 0.24f, 0f), new Vector3(0.18f, 0.28f, 2.15f));
                AddBox(colliders, new Vector3(-1.2f, 0.72f, 0f), new Vector3(0.12f, 0.3f, 1.62f));
                AddBox(colliders, new Vector3(1.2f, 0.72f, 0f), new Vector3(0.12f, 0.3f, 1.62f));
                AddBox(colliders, new Vector3(0f, 0.7f, -0.78f), new Vector3(2.3f, 0.28f, 0.12f));

                GameObject points = Child(root.transform, "GrabPoints", Vector3.zero);
                SledGrabPoint[] grabPoints =
                {
                    AddPoint(points.transform, "GrabPointFrontLeft", new Vector3(-1.12f, 0.62f, 1.05f), 0),
                    AddPoint(points.transform, "GrabPointFrontRight", new Vector3(1.12f, 0.62f, 1.05f), 1),
                    AddPoint(points.transform, "GrabPointRearLeft", new Vector3(-1.12f, 0.62f, -1.05f), 2),
                    AddPoint(points.transform, "GrabPointRearRight", new Vector3(1.12f, 0.62f, -1.05f), 3)
                };

                SetReference(controller, "m_rigidbody", body);
                SetReference(controller, "m_settings", settings);
                SetArray(controller, "m_grabPoints", grabPoints);
                ConfigureNetwork(networkObject, networkTransform, controller);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null) throw new UnityException($"Failed to save {PrefabPath}.");
                return saved.GetComponent<NetworkObject>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void ConfigureNetwork(NetworkObject no, NetworkTransform nt, SledController controller)
        {
            SerializedObject transform = new(nt);
            transform.FindProperty("_componentConfiguration").enumValueIndex =
                (int)NetworkTransform.ComponentConfigurationType.Rigidbody;
            transform.FindProperty("_clientAuthoritative").boolValue = false;
            transform.FindProperty("_synchronizePosition").boolValue = true;
            transform.FindProperty("_synchronizeRotation").boolValue = true;
            transform.FindProperty("_synchronizeScale").boolValue = false;
            transform.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject network = new(no);
            SerializedProperty behaviours = network.FindProperty("NetworkBehaviours");
            behaviours.arraySize = 2;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue = nt;
            behaviours.GetArrayElementAtIndex(1).objectReferenceValue = controller;
            network.ApplyModifiedPropertiesWithoutUndo();
            ConfigureBehaviour(nt, no, 0);
            ConfigureBehaviour(controller, no, 1);
        }

        private static void RegisterPrefab(NetworkObject prefab)
        {
            SinglePrefabObjects collection = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablesPath);
            if (collection == null) throw new UnityException($"Missing {SpawnablesPath}.");
            if (!collection.Prefabs.Contains(prefab)) collection.AddObject(prefab, false, false);
            string pathAndName = $"{AssetDatabase.GetAssetPath(prefab.gameObject)}{prefab.gameObject.name}"
                .Trim().ToLowerInvariant();
            string stableName = new(pathAndName.Where(character =>
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9')).ToArray());
            prefab.SetAssetPathHash(stableName.GetStableHashU64());
            EditorUtility.SetDirty(prefab);
            EditorUtility.SetDirty(collection);
        }

        private static void BuildShopAssets(NetworkObject prefab)
        {
            SledPurchaseBehaviour purchase = AssetDatabase.LoadAssetAtPath<SledPurchaseBehaviour>(PurchasePath);
            if (purchase == null)
            {
                purchase = ScriptableObject.CreateInstance<SledPurchaseBehaviour>();
                AssetDatabase.CreateAsset(purchase, PurchasePath);
            }
            SetReference(purchase, "m_sledPrefab", prefab);

            ShopItemDefinition item = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ShopItemDefinition>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }
            SerializedObject itemSo = new(item);
            itemSo.FindProperty("m_itemId").stringValue = "sled";
            itemSo.FindProperty("m_displayName").stringValue = "Sled";
            itemSo.FindProperty("m_description").stringValue =
                "A physical couch hauler that shines downhill. Hold on tight.";
            itemSo.FindProperty("m_price").intValue = 150;
            itemSo.FindProperty("m_category").enumValueIndex = (int)ShopItemCategory.Vehicle;
            itemSo.FindProperty("m_purchaseBehaviour").objectReferenceValue = purchase;
            itemSo.ApplyModifiedPropertiesWithoutUndo();

            ShopCatalogue catalogue = AssetDatabase.LoadAssetAtPath<ShopCatalogue>(CataloguePath);
            if (catalogue == null) throw new UnityException($"Missing {CataloguePath}.");
            SerializedObject catalogueSo = new(catalogue);
            SerializedProperty items = catalogueSo.FindProperty("m_items");
            bool exists = false;
            for (int index = 0; index < items.arraySize; index++)
                exists |= items.GetArrayElementAtIndex(index).objectReferenceValue == item;
            if (!exists)
            {
                items.arraySize++;
                items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = item;
            }
            catalogueSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignTestScene(NetworkObject prefab)
        {
            Scene scene = EditorSceneManager.OpenScene(TestScenePath, OpenSceneMode.Additive);
            try
            {
                CouchPhysicsTestController controller = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CouchPhysicsTestController>(true))
                    .FirstOrDefault();
                if (controller == null) throw new UnityException("CouchPhysicsTestController is missing.");
                SetReference(controller, "m_sledPrefab", prefab);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static SledGrabPoint AddPoint(Transform parent, string name, Vector3 position, int index)
        {
            GameObject point = Child(parent, name, position);
            SledGrabPoint component = point.AddComponent<SledGrabPoint>();
            SerializedObject so = new(component);
            so.FindProperty("m_pointIndex").intValue = index;
            so.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new UnityException("sled_01 contains no renderers.");
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static void AddBox(GameObject target, Vector3 centre, Vector3 size)
        {
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.center = centre;
            collider.size = size;
        }

        private static GameObject Child(Transform parent, string name, Vector3 position)
        {
            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            return child;
        }

        private static void SetReference(Object target, string property, Object value)
        {
            SerializedObject so = new(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string property, Object[] values)
        {
            SerializedObject so = new(target);
            SerializedProperty array = so.FindProperty(property);
            array.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                array.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureBehaviour(NetworkBehaviour behaviour, NetworkObject no, int index)
        {
            SerializedObject so = new(behaviour);
            so.FindProperty("_componentIndexCache").intValue = index;
            so.FindProperty("_addedNetworkObject").objectReferenceValue = no;
            so.FindProperty("_networkObjectCache").objectReferenceValue = no;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
