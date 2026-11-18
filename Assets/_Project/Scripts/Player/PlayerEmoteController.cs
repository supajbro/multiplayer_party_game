using CouchGuys.Gameplay.Weapons;
using CouchGuys.Input;
using CouchGuys.Networking;
using CouchGuys.UI;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CouchGuys.Player
{
    [DefaultExecutionOrder(180)]
    [DisallowMultipleComponent]
    public sealed class PlayerEmoteController : NetworkBehaviour
    {
        public struct EmoteState
        {
            public byte Id; // Zero means stopped. One atomic state includes late-observer phase.
            public uint Sequence;
            public uint StartTick;
        }

        [SerializeField] private EmoteDefinition[] m_definitions;
        [SerializeField] private PlayerEmotePlayback m_playback;
        [SerializeField] private EmoteWheelView m_wheelPrefab;
        [SerializeField, Min(.01f)] private float m_movementTolerance = .035f;
        private readonly SyncVar<EmoteState> m_state = new();
        private PlayerInputReader m_input;
        private PlayerHealth m_health;
        private PlayerCouchCarrier m_carrier;
        private PlayerTruckInteractor m_truck;
        private PlayerHoverboardInteractor m_board;
        private ThirdPersonPlayerController m_motor;
        private PlayerWeaponController m_weapons;
        private RobotFaceExpressionController m_face;
        private CouchGuys.Gameplay.Healing.HealingItem m_healing;
        private EmoteWheelView m_wheel;
        private CursorLockMode m_previousLock;
        private bool m_previousCursorVisible;
        private bool m_stopRequested;
        private Vector3 m_serverOrigin;
        private Vector3 m_previousPosition;
        private Vector3 m_localOrigin;
        private EmoteState m_observed;
        private float m_nextStartAt;

        public bool IsEmoting => m_observed.Id != 0 && !m_stopRequested;
        public bool WheelOpen => m_wheel != null && m_wheel.gameObject.activeSelf;
        public EmoteDefinition FindDefinition(byte id)
        {
            if (m_definitions != null)
                foreach (var definition in m_definitions)
                    if (definition != null && definition.Id == id && definition.Duration > 0) return definition;
            return null;
        }

        private void Awake()
        {
            m_input = GetComponent<PlayerInputReader>();
            m_health = GetComponent<PlayerHealth>();
            m_carrier = GetComponent<PlayerCouchCarrier>();
            m_truck = GetComponent<PlayerTruckInteractor>();
            m_board = GetComponent<PlayerHoverboardInteractor>();
            m_motor = GetComponent<ThirdPersonPlayerController>();
            m_weapons = GetComponent<PlayerWeaponController>();
            m_face = GetComponent<RobotFaceExpressionController>();
            m_healing = GetComponent<CouchGuys.Gameplay.Healing.HealingItem>();
            m_previousPosition = transform.position;
            m_state.OnChange += StateChanged;
            if (m_health != null) m_health.DamageReceivedServer += InterruptServer;
        }

        private void OnDestroy()
        {
            m_state.OnChange -= StateChanged;
            if (m_health != null) m_health.DamageReceivedServer -= InterruptServer;
            CloseWheel();
            if (m_wheel != null) Destroy(m_wheel.gameObject);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Observe(m_state.Value);
        }

        public override void OnStopClient() { CloseWheel(); StopPresentation(); base.OnStopClient(); }
        public override void OnOwnershipClient(NetworkConnection previousOwner)
        {
            base.OnOwnershipClient(previousOwner);
            CloseWheel();
        }
        private void OnDisable() { CloseWheel(); StopPresentation(); }

        private bool Compatible => m_health != null && m_health.IsAlive && !m_health.IsKnockedDown &&
            !m_health.IsReturningToRing && !m_health.IsAiTeammate &&
            !(m_carrier != null && m_carrier.IsCarrying) &&
            !(m_truck != null && m_truck.CurrentTruck != null) && !(m_board != null && m_board.IsMounted) &&
            !(m_motor != null && (m_motor.MovementLocked || m_motor.ExternalControlPaused)) &&
            !(m_weapons != null && m_weapons.EmoteIncompatibleAction) &&
            !(m_healing != null && m_healing.IsUsing) && !GameplaySessionController.IsServerGameOver;

        private void Update()
        {
            if (IsServerInitialized && m_state.Value.Id != 0)
            {
                EmoteDefinition definition = FindDefinition(m_state.Value.Id);
                if (!Compatible || Vector3.Distance(transform.position, m_serverOrigin) > m_movementTolerance ||
                    definition == null || !definition.Loop && Age(m_state.Value) >= definition.Duration)
                    InterruptServer();
            }

            if (IsOwner && m_input != null && m_input.isActiveAndEnabled)
            {
                bool interrupt = !Compatible || m_input.EmoteInterruptedByInput ||
                    !m_input.CanContinueEmoteWheel || !Application.isFocused ||
                    IsEmoting && Vector3.Distance(transform.position, m_localOrigin) > m_movementTolerance;
                if (interrupt)
                {
                    CloseWheel();
                    RequestInterrupt();
                }
                else if (!WheelOpen && m_input.EmotePressedThisFrame && m_input.CanOpenEmoteWheel &&
                         Cursor.lockState == CursorLockMode.Locked && m_wheelPrefab != null)
                {
                    if (m_wheel == null) m_wheel = Instantiate(m_wheelPrefab);
                    m_previousLock = Cursor.lockState;
                    m_previousCursorVisible = Cursor.visible;
                    m_input.SetEmoteWheelOpen(true);
                    m_wheel.gameObject.SetActive(true);
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    Mouse.current?.WarpCursorPosition(new Vector2(Screen.width, Screen.height) * .5f);
                    m_wheel.Present(m_definitions, new Vector2(Screen.width, Screen.height) * .5f);
                }
                else if (WheelOpen)
                {
                    m_wheel.Present(m_definitions, Mouse.current?.position.ReadValue() ?? Vector2.zero);
                    if (!m_input.EmoteHeld)
                    {
                        int selected = m_wheel.Selected;
                        CloseWheel();
                        if (selected >= 0 && selected < m_definitions.Length && m_definitions[selected] != null)
                            RequestEmoteServerRpc(m_definitions[selected].Id);
                    }
                }
            }
            else CloseWheel();
            // Face LateUpdate runs before our pose pass, so publish expression during Update.
            EmoteDefinition faceDefinition = IsClientInitialized && IsEmoting && Compatible ? FindDefinition(m_observed.Id) : null;
            bool showFace = faceDefinition != null && (faceDefinition.Loop || Age(m_observed) < faceDefinition.Duration);
            m_face?.SetEmoteExpression(showFace ? faceDefinition.Expression : (RobotFaceExpression?)null);
            m_previousPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (!IsClientInitialized || !IsEmoting || !Compatible) return;
            EmoteDefinition definition = FindDefinition(m_observed.Id);
            float age = Age(m_observed);
            if (definition == null || !definition.Loop && age >= definition.Duration) return;
            m_playback.Sample(definition, age);
        }

        private float Age(EmoteState state) => (float)TimeManager.TimePassed(state.StartTick, false);

        [ServerRpc]
        private void RequestEmoteServerRpc(byte id)
        {
            if (!Compatible || FindDefinition(id) == null || Time.unscaledTime < m_nextStartAt ||
                Vector3.Distance(transform.position, m_previousPosition) > m_movementTolerance) return;
            m_nextStartAt = Time.unscaledTime + .25f;
            m_serverOrigin = transform.position;
            m_state.Value = new EmoteState { Id = id, Sequence = m_state.Value.Sequence + 1, StartTick = TimeManager.Tick };
        }

        [ServerRpc]
        private void StopEmoteServerRpc() => InterruptServer();

        public void InterruptServer()
        {
            if (!IsServerInitialized || m_state.Value.Id == 0) return;
            StopPresentation();
            m_state.Value = new EmoteState { Sequence = m_state.Value.Sequence + 1, StartTick = TimeManager.Tick };
        }

        // Local interruption hides the pose immediately; the server owns persistent state.
        public void RequestInterrupt()
        {
            if (!IsOwner || !IsEmoting) return;
            m_stopRequested = true;
            StopPresentation();
            StopEmoteServerRpc();
        }

        private void StateChanged(EmoteState previous, EmoteState next, bool asServer)
        {
            if (asServer && IsClientInitialized) return; // Hosts also receive the client callback.
            Observe(next);
        }

        private void Observe(EmoteState state)
        {
            if (state.Sequence == m_observed.Sequence && state.Id == m_observed.Id) return;
            StopPresentation();
            m_observed = state;
            m_stopRequested = false;
            m_localOrigin = transform.position;
        }

        private void StopPresentation()
        {
            m_playback?.RestorePose();
            m_face?.SetEmoteExpression(null);
        }

        private void CloseWheel()
        {
            if (!WheelOpen) return;
            m_wheel.gameObject.SetActive(false);
            m_input?.SetEmoteWheelOpen(false);
            // A menu which took control keeps its own cursor policy.
            if (m_input != null && m_input.CanContinueEmoteWheel && Cursor.lockState == CursorLockMode.None)
            {
                Cursor.lockState = m_previousLock;
                Cursor.visible = m_previousCursorVisible;
            }
        }
    }
}
