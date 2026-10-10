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
        private InputAction m_shootAction;
        private InputAction m_reloadAction;
        private InputAction m_pistolAction;
        private InputAction m_assaultRifleAction;
        private InputAction m_shotgunAction;
        private InputAction m_teleportToDeliveryAction;
        private InputActionAsset m_runtimeInputActions;
        private bool m_gameplaySuppressed;

        public Vector2 Move => !m_gameplaySuppressed ? m_moveAction?.ReadValue<Vector2>() ?? Vector2.zero : Vector2.zero;
        public Vector2 Look => !m_gameplaySuppressed ? m_lookAction?.ReadValue<Vector2>() ?? Vector2.zero : Vector2.zero;
        public bool JumpPressedThisFrame => !m_gameplaySuppressed && (m_jumpAction?.WasPressedThisFrame() ?? false);
        public bool SprintHeld => !m_gameplaySuppressed && (m_sprintAction?.IsPressed() ?? false);
        public bool InteractPressedThisFrame => !m_gameplaySuppressed && (m_interactAction?.WasPressedThisFrame() ?? false);
        public bool MapPressedThisFrame => !m_gameplaySuppressed && (m_mapAction?.WasPressedThisFrame() ?? false);
        public bool ShootHeld => !m_gameplaySuppressed && (m_shootAction?.IsPressed() ?? false);
        public bool ShootPressedThisFrame => !m_gameplaySuppressed && (m_shootAction?.WasPressedThisFrame() ?? false);
        public bool ReloadPressedThisFrame => !m_gameplaySuppressed && (m_reloadAction?.WasPressedThisFrame() ?? false);
        public int HotbarSelectionPressedThisFrame
        {
            get
            {
                if (m_gameplaySuppressed) return -1;
                if (m_pistolAction?.WasPressedThisFrame() ?? false) return 0;
                if (m_assaultRifleAction?.WasPressedThisFrame() ?? false) return 1;
                if (m_shotgunAction?.WasPressedThisFrame() ?? false) return 2;
                if (Keyboard.current?.digit4Key.wasPressedThisFrame ?? false) return 3;
                if (Keyboard.current?.digit5Key.wasPressedThisFrame ?? false) return 4;
                return -1;
            }
        }
        public int WeaponSelectionPressedThisFrame => HotbarSelectionPressedThisFrame < 3
            ? HotbarSelectionPressedThisFrame
            : -1;
        public bool TeleportToDeliveryPressedThisFrame =>
            !m_gameplaySuppressed && (m_teleportToDeliveryAction?.WasPressedThisFrame() ?? false);

        public void SetGameplaySuppressed(bool suppressed)
        {
            m_gameplaySuppressed = suppressed;
        }

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
            m_shootAction = m_playerMap.FindAction("Shoot", true);
            m_reloadAction = m_playerMap.FindAction("Reload", true);
            m_pistolAction = m_playerMap.FindAction("Equip Pistol", true);
            m_assaultRifleAction = m_playerMap.FindAction("Equip Assault Rifle", true);
            m_shotgunAction = m_playerMap.FindAction("Equip Shotgun", true);
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
