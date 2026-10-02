using CouchGuys.Gameplay.Couch;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace CouchGuys.Editor
{
    /// <summary>
    /// Builds the temporary network couch prefab.
    /// </summary>
    public static class CouchPrototypeBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Couch.prefab";

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabAfterImport()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                EditorApplication.delayCall += BuildCouchPrototype;
            }
        }

        [MenuItem("Couch Guys/Build Couch Prototype")]
        public static void BuildCouchPrototype()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BuildPrefab();
            SteamMultiplayerPrefabBuilder.BuildSteamMultiplayerAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            VerifyPrefab();
            Debug.Log($"Couch Guys couch prototype built successfully at {PrefabPath}.");
        }

        public static void BuildFromCommandLine()
        {
            BuildCouchPrototype();
        }

        private static void BuildPrefab()
        {
            GameObject couch = new GameObject("Couch");
            try
            {
                couch.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                couch.transform.localScale = Vector3.one;

                Rigidbody rigidbody = couch.AddComponent<Rigidbody>();
                rigidbody.mass = 35f;
                rigidbody.linearDamping = 0.4f;
                rigidbody.angularDamping = 1.5f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.maxAngularVelocity = 10f;

                BoxCollider collider = couch.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.45f, 0f);
                collider.size = new Vector3(2.1f, 0.9f, 0.9f);

                Transform visual = CreateChild(couch.transform, "Visual", Vector3.zero);
                CreateVisualCube(visual, "Base", new Vector3(0f, 0.25f, 0f), new Vector3(2.1f, 0.45f, 0.9f));
                CreateVisualCube(visual, "Back", new Vector3(0f, 0.62f, 0.35f), new Vector3(2.1f, 0.55f, 0.2f));
                CreateVisualCube(visual, "LeftArm", new Vector3(-0.95f, 0.52f, 0f), new Vector3(0.2f, 0.55f, 0.9f));
                CreateVisualCube(visual, "RightArm", new Vector3(0.95f, 0.52f, 0f), new Vector3(0.2f, 0.55f, 0.9f));

                Transform pointsRoot = CreateChild(couch.transform, "CarryPoints", Vector3.zero);
                CouchCarryPoint[] points =
                {
                    CreateCarryPoint(pointsRoot, "CarryPointFrontLeft", new Vector3(-0.85f, 0.35f, -0.58f), 0),
                    CreateCarryPoint(pointsRoot, "CarryPointFrontRight", new Vector3(0.85f, 0.35f, -0.58f), 1),
                    CreateCarryPoint(pointsRoot, "CarryPointRearLeft", new Vector3(-0.85f, 0.35f, 0.58f), 2),
                    CreateCarryPoint(pointsRoot, "CarryPointRearRight", new Vector3(0.85f, 0.35f, 0.58f), 3)
                };

                NetworkObject networkObject = couch.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = couch.AddComponent<NetworkTransform>();
                CouchCarryController carryController = couch.AddComponent<CouchCarryController>();
                SetObjectReference(carryController, "m_rigidbody", rigidbody);
                SetObjectArray(carryController, "m_carryPoints", points);
                ConfigureNetworkComponents(networkObject, networkTransform, carryController);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(couch, PrefabPath, out bool savedSuccessfully);
                if (!savedSuccessfully || prefab == null)
                {
                    throw new UnityException($"Failed to save the Couch prefab at {PrefabPath}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(couch);
            }
        }

        private static void ConfigureNetworkComponents(
            NetworkObject networkObject,
            NetworkTransform networkTransform,
            CouchCarryController carryController)
        {
            SerializedObject serialisedTransform = new SerializedObject(networkTransform);
            serialisedTransform.FindProperty("_componentConfiguration").enumValueIndex =
                (int)NetworkTransform.ComponentConfigurationType.Rigidbody;
            serialisedTransform.FindProperty("_clientAuthoritative").boolValue = false;
            serialisedTransform.FindProperty("_synchronizePosition").boolValue = true;
            serialisedTransform.FindProperty("_synchronizeRotation").boolValue = true;
            serialisedTransform.FindProperty("_synchronizeScale").boolValue = false;
            serialisedTransform.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serialisedNetworkObject = new SerializedObject(networkObject);
            SerializedProperty behaviours = serialisedNetworkObject.FindProperty("NetworkBehaviours");
            behaviours.arraySize = 2;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue = networkTransform;
            behaviours.GetArrayElementAtIndex(1).objectReferenceValue = carryController;
            serialisedNetworkObject.ApplyModifiedPropertiesWithoutUndo();

            SetNetworkBehaviourReferences(networkTransform, networkObject, 0);
            SetNetworkBehaviourReferences(carryController, networkObject, 1);
        }

        private static CouchCarryPoint CreateCarryPoint(Transform parent, string name, Vector3 localPosition, int index)
        {
            Transform pointTransform = CreateChild(parent, name, localPosition);
            CouchCarryPoint point = pointTransform.gameObject.AddComponent<CouchCarryPoint>();
            SerializedObject serialisedPoint = new SerializedObject(point);
            serialisedPoint.FindProperty("m_pointIndex").intValue = index;
            serialisedPoint.ApplyModifiedPropertiesWithoutUndo();
            return point;
        }

        private static void CreateVisualCube(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = localScale;
            Object.DestroyImmediate(cube.GetComponent<Collider>());
        }

        private static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            return child.transform;
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serialisedObject = new SerializedObject(target);
            serialisedObject.FindProperty(propertyName).objectReferenceValue = value;
            serialisedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(Object target, string propertyName, Object[] values)
        {
            SerializedObject serialisedObject = new SerializedObject(target);
            SerializedProperty property = serialisedObject.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }

            serialisedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetNetworkBehaviourReferences(NetworkBehaviour behaviour, NetworkObject networkObject, int componentIndex)
        {
            SerializedObject serialisedBehaviour = new SerializedObject(behaviour);
            serialisedBehaviour.FindProperty("_componentIndexCache").intValue = componentIndex;
            serialisedBehaviour.FindProperty("_addedNetworkObject").objectReferenceValue = networkObject;
            serialisedBehaviour.FindProperty("_networkObjectCache").objectReferenceValue = networkObject;
            serialisedBehaviour.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void VerifyPrefab()
        {
            GameObject couch = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            CouchCarryController controller = couch != null ? couch.GetComponent<CouchCarryController>() : null;
            if (couch == null || couch.transform.localScale != Vector3.one ||
                couch.GetComponent<Rigidbody>() == null || couch.GetComponent<BoxCollider>() == null ||
                couch.GetComponent<NetworkObject>() == null || couch.GetComponent<NetworkTransform>() == null ||
                controller == null || couch.GetComponentsInChildren<CouchCarryPoint>(true).Length != 4)
            {
                throw new UnityException("Couch prefab verification failed: required hierarchy or configuration is invalid.");
            }
        }
    }
}
