using UnityEngine;
using UnityEngine.InputSystem;

namespace CouchGuys.Input
{
    /// <summary>
    /// Owns the local player's input action map and exposes intent to gameplay systems.
    /// Keeping device input here makes movement and camera logic independent of bindings.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        private const string PlayerMapName = "Player";

        [Header("Input Actions")]
        [SerializeField] private InputActionAsset m_inputActions;

        private InputActionMap m_playerMap;
        private InputAction m_moveAction;
        private InputAction m_lookAction;
        private InputAction m_jumpAction;
        private InputAction m_sprintAction;
        private InputAction m_interactAction;
        private InputAction m_mapAction;
        private InputAction m_teleportToDeliveryAction;
        private InputActionAsset m_runtimeInputActions;

        public Vector2 Move => m_moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        public Vector2 Look => m_lookAction?.ReadValue<Vector2>() ?? Vector2.zero;
        public bool JumpPressedThisFrame => m_jumpAction?.WasPressedThisFrame() ?? false;
        public bool SprintHeld => m_sprintAction?.IsPressed() ?? false;
        public bool InteractPressedThisFrame => m_interactAction?.WasPressedThisFrame() ?? false;
        public bool MapPressedThisFrame => m_mapAction?.WasPressedThisFrame() ?? false;
        public bool TeleportToDeliveryPressedThisFrame =>
            m_teleportToDeliveryAction?.WasPressedThisFrame() ?? false;

        private void Awake()
        {
            if (m_inputActions != null)
            {
                // Each network player needs an independent action-map instance. Disabling a
                // remote player's reader must never disable the local owner's shared asset.
                m_runtimeInputActions = Instantiate(m_inputActions);
                m_inputActions = m_runtimeInputActions;
            }

            CacheActions();
        }

        private void OnEnable()
        {
            CacheActions();
            m_playerMap?.Enable();
        }

        private void OnDisable()
        {
            m_playerMap?.Disable();
        }

        private void OnDestroy()
        {
            if (m_runtimeInputActions != null)
            {
                Destroy(m_runtimeInputActions);
            }
        }

        private void CacheActions()
        {
            if (m_inputActions == null || m_playerMap != null)
            {
                return;
            }

            m_playerMap = m_inputActions.FindActionMap(PlayerMapName, true);
            m_moveAction = m_playerMap.FindAction("Move", true);
            m_lookAction = m_playerMap.FindAction("Look", true);
            m_jumpAction = m_playerMap.FindAction("Jump", true);
            m_sprintAction = m_playerMap.FindAction("Sprint", true);
            m_interactAction = m_playerMap.FindAction("Interact", true);
            m_mapAction = m_playerMap.FindAction("Map", true);
            m_teleportToDeliveryAction = m_playerMap.FindAction("Teleport To Delivery", true);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_inputActions == null)
            {
                return;
            }

            m_inputActions.FindActionMap(PlayerMapName, true);
        }
#endif
    }
}
