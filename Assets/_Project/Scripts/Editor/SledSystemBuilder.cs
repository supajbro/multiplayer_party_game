using System.Linq;
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
    /// <summary>Builds the authored sled model into its gameplay and test assets.</summary>
    public static class SledSystemBuilder
    {
        private const string ModelPath = "Assets/_Project/Models/Sled/sled_01.fbx";
        private const string PrefabPath = "Assets/_Project/Prefabs/Vehicles/Sled.prefab";
        private const string SettingsPath = "Assets/_Project/Settings/Gameplay/SledCarrySettings.asset";
        private const string SpawnablesPath = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";
        private const string TestScenePath = "Assets/Scenes/CouchPhysicsTest.unity";

        [MenuItem("Couch Guys/Vehicles/Build Sled System")]
        public static void BuildSledSystem()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SledSettings settings = BuildSettings();
            NetworkObject prefab = BuildPrefab(settings);
            RegisterPrefab(prefab);
            AssignTestScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Built server-authoritative Sled prefab, settings, spawn registration, and test-scene reference.");
        }

        public static void BuildFromCommandLine() => BuildSledSystem();

        private static SledSettings BuildSettings()
        {
            return AssetDatabase.LoadAssetAtPath<SledSettings>(SettingsPath)
                ?? throw new UnityException($"Missing authored settings at {SettingsPath}.");
        }

        private static NetworkObject BuildPrefab(SledSettings settings)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new UnityException($"Missing authored sled prefab at {PrefabPath}.");
            return prefab.GetComponent<NetworkObject>();
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
