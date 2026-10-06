using CouchGuys.Gameplay.Enemies;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Editor
{
    /// <summary>Builds the correctly scaled network-ready capsule used as the placeholder Thief.</summary>
    public static class ThiefPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Thief.prefab";
        private const string SpawnablesPath = "Assets/_Project/Settings/NetworkSpawnablePrefabs.asset";

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabAfterImport()
        {
            EditorApplication.delayCall += () => EnsureThiefPrefab();
        }

        [MenuItem("Couch Guys/Build Thief Prefab")]
        public static void BuildThiefPrefab()
        {
            Selection.activeObject = EnsureThiefPrefab();
        }

        public static GameObject EnsureThiefPrefab()
        {
            EnsureFolder("Assets/_Project/Prefabs", "Enemies");
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (IsValid(existing))
            {
                RegisterNetworkPrefab(existing.GetComponent<NetworkObject>());
                return existing;
            }

            GameObject root = new GameObject("Thief");
            try
            {
                NetworkObject networkObject = root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
                CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
                ThiefNavigator navigator = root.AddComponent<ThiefNavigator>();

                // Root pivot remains at ground level so NavMesh placement and agent movement
                // do not require a compensating vertical offset.
                agent.radius = 0.35f;
                agent.height = 1.8f;
                agent.baseOffset = 0f;
                agent.speed = 4.5f;
                agent.angularSpeed = 720f;
                agent.acceleration = 16f;
                agent.stoppingDistance = 1.25f;
                agent.autoBraking = true;

                collider.radius = 0.35f;
                collider.height = 1.8f;
                collider.center = new Vector3(0f, 0.9f, 0f);

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "CapsuleVisual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                Object.DestroyImmediate(visual.GetComponent<Collider>());

                SerializedObject serialisedTransform = new SerializedObject(networkTransform);
                serialisedTransform.FindProperty("_componentConfiguration").enumValueIndex =
                    (int)NetworkTransform.ComponentConfigurationType.Disabled;
                serialisedTransform.FindProperty("_clientAuthoritative").boolValue = false;
                serialisedTransform.FindProperty("_synchronizePosition").boolValue = true;
                serialisedTransform.FindProperty("_synchronizeRotation").boolValue = true;
                serialisedTransform.FindProperty("_synchronizeScale").boolValue = false;
                serialisedTransform.ApplyModifiedPropertiesWithoutUndo();

                SerializedObject serialisedNavigator = new SerializedObject(navigator);
                serialisedNavigator.FindProperty("m_agent").objectReferenceValue = agent;
                serialisedNavigator.ApplyModifiedPropertiesWithoutUndo();

                ConfigureNetworkBehaviours(networkObject, networkTransform, navigator);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null)
                {
                    throw new UnityException($"Failed to save the Thief prefab at {PrefabPath}.");
                }

                RegisterNetworkPrefab(saved.GetComponent<NetworkObject>());
                AssetDatabase.SaveAssets();
                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureNetworkBehaviours(
            NetworkObject networkObject,
            NetworkTransform networkTransform,
            ThiefNavigator navigator)
        {
            SerializedObject serialisedNetworkObject = new SerializedObject(networkObject);
            SerializedProperty behaviours = serialisedNetworkObject.FindProperty("NetworkBehaviours");
            behaviours.arraySize = 2;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue = networkTransform;
            behaviours.GetArrayElementAtIndex(1).objectReferenceValue = navigator;
            serialisedNetworkObject.ApplyModifiedPropertiesWithoutUndo();
            SetNetworkBehaviourReferences(networkTransform, networkObject, 0);
            SetNetworkBehaviourReferences(navigator, networkObject, 1);
        }

        private static void SetNetworkBehaviourReferences(
            NetworkBehaviour behaviour,
            NetworkObject networkObject,
            int componentIndex)
        {
            SerializedObject serialised = new SerializedObject(behaviour);
            serialised.FindProperty("_componentIndexCache").intValue = componentIndex;
            serialised.FindProperty("_addedNetworkObject").objectReferenceValue = networkObject;
            serialised.FindProperty("_networkObjectCache").objectReferenceValue = networkObject;
            serialised.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterNetworkPrefab(NetworkObject networkObject)
        {
            SinglePrefabObjects spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablesPath);
            if (spawnables == null || networkObject == null)
            {
                return;
            }

            spawnables.AddObject(networkObject, true, false);
            EditorUtility.SetDirty(spawnables);
        }

        private static bool IsValid(GameObject prefab)
        {
            return prefab != null && prefab.GetComponent<NetworkObject>() != null &&
                   prefab.GetComponent<NetworkTransform>() != null &&
                   prefab.GetComponent<NavMeshAgent>() != null &&
                   prefab.GetComponent<CapsuleCollider>() != null &&
                   prefab.GetComponent<ThiefNavigator>() != null;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
