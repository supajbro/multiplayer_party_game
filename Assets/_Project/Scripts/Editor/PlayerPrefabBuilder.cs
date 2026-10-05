using CouchGuys.CameraSystem;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Input;
using CouchGuys.Networking;
using CouchGuys.Player;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CouchGuys.Editor
{
    /// <summary>
    /// Creates the canonical Player prefab with all required references and dimensions.
    /// </summary>
    public static class PlayerPrefabBuilder
    {
        private const string InputActionsPath = "Assets/_Project/Input/PlayerControls.inputactions";
        private const string PrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        [InitializeOnLoadMethod]
        private static void BuildMissingPrefabAfterImport()
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (playerPrefab == null)
            {
                EditorApplication.delayCall += BuildPlayerPrefab;
            }
        }

        [MenuItem("Couch Guys/Build Player Prefab")]
        public static void BuildPlayerPrefab()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
            {
                throw new UnityException($"Could not load input actions at {InputActionsPath}.");
            }

            GameObject player = new GameObject("Player");
            try
            {
                player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                player.transform.localScale = Vector3.one;

                CharacterController characterController = player.AddComponent<CharacterController>();
                characterController.height = 1.8f;
                characterController.radius = 0.3f;
                characterController.center = new Vector3(0f, 0.9f, 0f);
                characterController.slopeLimit = 50f;
                characterController.stepOffset = 0.3f;
                characterController.skinWidth = 0.08f;
                characterController.minMoveDistance = 0f;

                PlayerInputReader inputReader = player.AddComponent<PlayerInputReader>();
                SetObjectReference(inputReader, "m_inputActions", inputActions);

                GameObject visual = CreateChild(player.transform, "Visual", Vector3.zero);
                Animator animator = CreatePlayerVisual(visual.transform, characterController.height);

                GameObject cameraTarget = CreateChild(player.transform, "CameraTarget", new Vector3(0f, 1.5f, 0f));
                GameObject cameraRig = CreateChild(player.transform, "CameraRig", Vector3.zero);
                ThirdPersonCameraController cameraController = cameraRig.AddComponent<ThirdPersonCameraController>();

                GameObject cameraObject = CreateChild(cameraRig.transform, "PlayerCamera", Vector3.zero);
                cameraObject.tag = "MainCamera";
                UnityEngine.Camera playerCamera = cameraObject.AddComponent<UnityEngine.Camera>();
                playerCamera.fieldOfView = 65f;
                playerCamera.nearClipPlane = 0.1f;
                playerCamera.farClipPlane = 1000f;
                cameraObject.AddComponent<AudioListener>();

                SetObjectReference(cameraController, "m_cameraTarget", cameraTarget.transform);
                SetObjectReference(cameraController, "m_input", inputReader);

                ThirdPersonPlayerController playerController = player.AddComponent<ThirdPersonPlayerController>();
                SetObjectReference(playerController, "m_input", inputReader);
                SetObjectReference(playerController, "m_cameraTransform", cameraObject.transform);

                PlayerCouchCarrier couchCarrier = player.AddComponent<PlayerCouchCarrier>();
                SetObjectReference(couchCarrier, "m_input", inputReader);
                SetObjectReference(couchCarrier, "m_playerController", playerController);

                CouchGuyAnimationDriver animationDriver = player.AddComponent<CouchGuyAnimationDriver>();
                SetObjectReference(animationDriver, "m_animator", animator);
                SetObjectReference(animationDriver, "m_playerController", playerController);
                SetObjectReference(animationDriver, "m_couchCarrier", couchCarrier);

                DebugCouchBotController debugBotController = player.AddComponent<DebugCouchBotController>();
                debugBotController.enabled = false;

                EnsureNetworkConfiguration(player);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, PrefabPath, out bool savedSuccessfully);
                if (!savedSuccessfully || prefab == null)
                {
                    throw new UnityException($"Failed to save the Player prefab at {PrefabPath}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(player);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            VerifyPlayerPrefab();
            Debug.Log($"Couch Guys player prefab built successfully at {PrefabPath}.");
        }

        public static void BuildFromCommandLine()
        {
            BuildPlayerPrefab();
        }

        /// <summary>
        /// Adds the network identity, root transform synchronisation, and local ownership gate.
        /// Safe to call on an existing prefab root.
        /// </summary>
        public static void EnsureNetworkConfiguration(GameObject player)
        {
            NetworkObject networkObject = player.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                networkObject = player.AddComponent<NetworkObject>();
            }

            NetworkTransform networkTransform = player.GetComponent<NetworkTransform>();
            if (networkTransform == null)
            {
                networkTransform = player.AddComponent<NetworkTransform>();
            }

            SerializedObject serialisedTransform = new SerializedObject(networkTransform);
            serialisedTransform.FindProperty("_componentConfiguration").enumValueIndex =
                (int)NetworkTransform.ComponentConfigurationType.CharacterController;
            serialisedTransform.FindProperty("_clientAuthoritative").boolValue = true;
            serialisedTransform.FindProperty("_synchronizePosition").boolValue = true;
            serialisedTransform.FindProperty("_synchronizeRotation").boolValue = true;
            serialisedTransform.FindProperty("_synchronizeScale").boolValue = false;
            serialisedTransform.ApplyModifiedPropertiesWithoutUndo();

            NetworkPlayerOwnership ownership = player.GetComponent<NetworkPlayerOwnership>();
            if (ownership == null)
            {
                ownership = player.AddComponent<NetworkPlayerOwnership>();
            }

            SetObjectReference(ownership, "m_input", player.GetComponent<PlayerInputReader>());
            SetObjectReference(ownership, "m_playerController", player.GetComponent<ThirdPersonPlayerController>());
            SetObjectReference(ownership, "m_cameraController", player.GetComponentInChildren<ThirdPersonCameraController>(true));
            SetObjectReference(ownership, "m_playerCamera", player.GetComponentInChildren<UnityEngine.Camera>(true));
            SetObjectReference(ownership, "m_audioListener", player.GetComponentInChildren<AudioListener>(true));
            SetObjectReference(ownership, "m_playerRenderer", player.GetComponentInChildren<Renderer>(true));

            PlayerCouchCarrier couchCarrier = player.GetComponent<PlayerCouchCarrier>();
            if (couchCarrier == null)
            {
                couchCarrier = player.AddComponent<PlayerCouchCarrier>();
            }

            SetObjectReference(couchCarrier, "m_input", player.GetComponent<PlayerInputReader>());
            SetObjectReference(couchCarrier, "m_playerController", player.GetComponent<ThirdPersonPlayerController>());
            RepairLegacyCarryDistances(couchCarrier);

            if (player.GetComponent<NeighbourhoodMap>() == null)
            {
                player.AddComponent<NeighbourhoodMap>();
            }

            CouchGuyAnimationDriver animationDriver = player.GetComponent<CouchGuyAnimationDriver>();
            if (animationDriver == null)
            {
                animationDriver = player.AddComponent<CouchGuyAnimationDriver>();
            }

            SetObjectReference(animationDriver, "m_animator", player.GetComponentInChildren<Animator>(true));
            SetObjectReference(animationDriver, "m_playerController", player.GetComponent<ThirdPersonPlayerController>());
            SetObjectReference(animationDriver, "m_couchCarrier", couchCarrier);

            DebugCouchBotController debugBotController = player.GetComponent<DebugCouchBotController>();
            if (debugBotController == null)
            {
                debugBotController = player.AddComponent<DebugCouchBotController>();
            }

            debugBotController.enabled = false;

            SerializedObject serialisedNetworkObject = new SerializedObject(networkObject);
            SerializedProperty behaviours = serialisedNetworkObject.FindProperty("NetworkBehaviours");
            if (behaviours == null)
            {
                throw new UnityException("FishNet NetworkObject behaviour list was not found.");
            }

            behaviours.arraySize = 3;
            behaviours.GetArrayElementAtIndex(0).objectReferenceValue = networkTransform;
            behaviours.GetArrayElementAtIndex(1).objectReferenceValue = ownership;
            behaviours.GetArrayElementAtIndex(2).objectReferenceValue = couchCarrier;
            serialisedNetworkObject.ApplyModifiedPropertiesWithoutUndo();

            SetNetworkBehaviourReferences(networkTransform, networkObject, 0);
            SetNetworkBehaviourReferences(ownership, networkObject, 1);
            SetNetworkBehaviourReferences(couchCarrier, networkObject, 2);
            EditorUtility.SetDirty(networkObject);
            EditorUtility.SetDirty(networkTransform);
            EditorUtility.SetDirty(ownership);
            EditorUtility.SetDirty(couchCarrier);
        }

        private static void RepairLegacyCarryDistances(PlayerCouchCarrier couchCarrier)
        {
            SerializedObject serialisedCarrier = new SerializedObject(couchCarrier);
            SerializedProperty comfortableDistance =
                serialisedCarrier.FindProperty("m_comfortableCarryDistance");
            SerializedProperty maximumDistance =
                serialisedCarrier.FindProperty("m_maximumCarrySeparation");
            if (comfortableDistance.floatValue <= 0.5f && maximumDistance.floatValue <= 0.5f)
            {
                comfortableDistance.floatValue = 0.65f;
                maximumDistance.floatValue = 1f;
                serialisedCarrier.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetNetworkBehaviourReferences(NetworkBehaviour behaviour, NetworkObject networkObject, int componentIndex)
        {
            SerializedObject serialisedBehaviour = new SerializedObject(behaviour);
            serialisedBehaviour.FindProperty("_componentIndexCache").intValue = componentIndex;
            serialisedBehaviour.FindProperty("_addedNetworkObject").objectReferenceValue = networkObject;
            serialisedBehaviour.FindProperty("_networkObjectCache").objectReferenceValue = networkObject;
            serialisedBehaviour.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateChild(Transform parent, string name, Vector3 localPosition)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            return child;
        }

        private static Animator CreatePlayerVisual(Transform visualRoot, float targetVisualHeight)
        {
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Capsule";
            capsule.transform.SetParent(visualRoot, false);
            capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            capsule.transform.localRotation = Quaternion.identity;
            capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Object.DestroyImmediate(capsule.GetComponent<Collider>());

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                CouchGuyAnimatorControllerBuilder.ModelPath);
            if (modelAsset == null)
            {
                return null;
            }

            capsule.SetActive(false);

            GameObject model = PrefabUtility.InstantiatePrefab(modelAsset, visualRoot) as GameObject;
            if (model == null)
            {
                throw new UnityException("Failed to instantiate the imported Couch Guy model.");
            }

            model.name = "CouchGuy";
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            FitVisualToController(model, targetVisualHeight);

            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }

            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                CouchGuyAnimatorControllerBuilder.ControllerPath);
            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning(
                    "Couch Guy model found, but its Animator Controller is missing. Run " +
                    "Couch Guys > Animation > Build Couch Guy Animator Controller, then rebuild the Player prefab.");
            }

            return animator;
        }

        private static void FitVisualToController(GameObject model, float targetVisualHeight)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new UnityException("The imported Couch Guy model contains no renderers.");
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            if (bounds.size.y <= 0.001f)
            {
                throw new UnityException("The imported Couch Guy model has invalid vertical bounds.");
            }

            float uniformScale = targetVisualHeight / bounds.size.y;
            model.transform.localScale = Vector3.one * uniformScale;

            // Recalculate after scaling, then place the hover ring just above ground level.
            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            const float groundClearance = 0.02f;
            model.transform.localPosition += Vector3.up * (groundClearance - bounds.min.y);
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serialisedObject = new SerializedObject(target);
            SerializedProperty property = serialisedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new UnityException($"Missing serialised property {propertyName} on {target.GetType().Name}.");
            }

            property.objectReferenceValue = value;
            serialisedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void VerifyPlayerPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new UnityException("Player prefab verification failed: asset is missing.");
            }

            CharacterController characterController = prefab.GetComponent<CharacterController>();
            Transform visual = prefab.transform.Find("Visual");
            Transform capsule = prefab.transform.Find("Visual/Capsule");
            Transform couchGuy = prefab.transform.Find("Visual/CouchGuy");
            Transform cameraTarget = prefab.transform.Find("CameraTarget");
            Transform playerCamera = prefab.transform.Find("CameraRig/PlayerCamera");
            bool hasValidVisual = visual != null &&
                capsule != null && capsule.GetComponent<Collider>() == null &&
                ((couchGuy != null && !capsule.gameObject.activeSelf) ||
                 (couchGuy == null && capsule.gameObject.activeSelf));

            if (prefab.transform.localScale != Vector3.one ||
                characterController == null ||
                !Mathf.Approximately(characterController.height, 1.8f) ||
                !Mathf.Approximately(characterController.radius, 0.3f) ||
                !hasValidVisual ||
                cameraTarget == null ||
                playerCamera == null ||
                prefab.GetComponent<PlayerInputReader>() == null ||
                prefab.GetComponent<ThirdPersonPlayerController>() == null ||
                prefab.GetComponentInChildren<ThirdPersonCameraController>(true) == null ||
                prefab.GetComponent<NetworkObject>() == null ||
                prefab.GetComponent<NetworkTransform>() == null ||
                prefab.GetComponent<NetworkPlayerOwnership>() == null ||
                prefab.GetComponent<PlayerCouchCarrier>() == null ||
                prefab.GetComponent<NeighbourhoodMap>() == null ||
                prefab.GetComponent<CouchGuyAnimationDriver>() == null ||
                prefab.GetComponent<DebugCouchBotController>() == null ||
                prefab.GetComponent<DebugCouchBotController>().enabled)
            {
                throw new UnityException("Player prefab verification failed: required hierarchy or configuration is invalid.");
            }

            SerializedObject inputReader = new SerializedObject(prefab.GetComponent<PlayerInputReader>());
            SerializedObject playerController = new SerializedObject(prefab.GetComponent<ThirdPersonPlayerController>());
            SerializedObject cameraController = new SerializedObject(prefab.GetComponentInChildren<ThirdPersonCameraController>(true));

            if (inputReader.FindProperty("m_inputActions").objectReferenceValue == null ||
                playerController.FindProperty("m_input").objectReferenceValue == null ||
                playerController.FindProperty("m_cameraTransform").objectReferenceValue == null ||
                cameraController.FindProperty("m_cameraTarget").objectReferenceValue == null ||
                cameraController.FindProperty("m_input").objectReferenceValue == null)
            {
                throw new UnityException("Player prefab verification failed: one or more object references are missing.");
            }
        }
    }
}
