using CouchGuys.CameraSystem;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Input;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Owner input and server-validated player/van occupancy bridge.</summary>
    [RequireComponent(typeof(PlayerInputReader), typeof(ThirdPersonPlayerController), typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class PlayerVanInteractor : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private ThirdPersonPlayerController m_controller;
        [SerializeField] private CharacterController m_characterController;
        [SerializeField] private Transform m_visualRoot;
        [SerializeField] private ThirdPersonCameraController m_cameraController;
        [SerializeField] private Camera m_playerCamera;

        private readonly SyncVar<NetworkObject> m_currentVanObject = new();
        private readonly SyncVar<int> m_currentSeatIndex = new(-1);
        private VanController m_nearbyVan;
        private VanController m_rearInteractionVan;
        private float m_holdProgress;
        private float m_nextInteractionTime;
        private float m_nextInputSendTime;
        private float m_originalFieldOfView;
        private string m_worldInteractionPrompt;
        private string m_vehicleFeedback;
        private float m_vehicleFeedbackUntil;
        private CouchGuyAnimationDriver m_animationDriver;
        private RobotHoverVFX m_hoverVfx;

        public VanController CurrentVan => m_currentVanObject.Value != null &&
            m_currentVanObject.Value.TryGetComponent(out VanController van) ? van : null;
        public VanController NearbyVan => m_nearbyVan;
        public int CurrentSeatIndex => m_currentSeatIndex.Value;
        public bool IsDriver => CurrentVan != null && m_currentSeatIndex.Value == 0;
        public float HoldProgress => m_holdProgress;
        public string WorldInteractionPrompt => Time.unscaledTime < m_vehicleFeedbackUntil
            ? m_vehicleFeedback : m_worldInteractionPrompt;

        private void Awake()
        {
            m_input ??= GetComponent<PlayerInputReader>();
            m_controller ??= GetComponent<ThirdPersonPlayerController>();
            m_characterController ??= GetComponent<CharacterController>();
            m_visualRoot ??= transform.Find("Visual");
            m_cameraController ??= GetComponentInChildren<ThirdPersonCameraController>(true);
            m_playerCamera ??= GetComponentInChildren<Camera>(true);
            m_animationDriver = GetComponent<CouchGuyAnimationDriver>();
            m_hoverVfx = GetComponent<RobotHoverVFX>();
            if (m_playerCamera != null) m_originalFieldOfView = m_playerCamera.fieldOfView;
            m_currentVanObject.OnChange += OnCurrentVanChanged;
            m_currentSeatIndex.OnChange += OnCurrentSeatIndexChanged;
        }

        private void OnDestroy()
        {
            m_currentVanObject.OnChange -= OnCurrentVanChanged;
            m_currentSeatIndex.OnChange -= OnCurrentSeatIndexChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyVehicleState(CurrentVan);
        }

        public override void OnStopServer()
        {
            VanController van = CurrentVan;
            if (van != null) van.LeaveServer(this);
            m_currentSeatIndex.Value = -1;
            m_currentVanObject.Value = null;
            base.OnStopServer();
        }

        private void Update()
        {
            if (!IsOwner || m_input == null) return;
            VanController current = CurrentVan;
            if (current != null)
            {
                m_nearbyVan = null;
                if (m_currentSeatIndex.Value == 0) SendDrivingInput(current);
                UpdateHold(current.Settings.ExitHoldDuration, RequestExitServerRpc);
            }
            else
            {
                FindRearInteractionVan();
                if (m_rearInteractionVan != null)
                {
                    m_nearbyVan = null;
                    m_holdProgress = 0f;
                    m_worldInteractionPrompt = GetRearPrompt(m_rearInteractionVan);
                    if (Time.time >= m_nextInteractionTime && m_input.VehicleInteractPressedThisFrame)
                    {
                        m_nextInteractionTime = Time.time + m_rearInteractionVan.Settings.InteractionCooldown;
                        RequestRearInteractionServerRpc(m_rearInteractionVan.NetworkObject);
                    }
                }
                else
                {
                    m_worldInteractionPrompt = string.Empty;
                    FindNearbyVan();
                }
                if (m_rearInteractionVan == null && m_nearbyVan != null && Time.time >= m_nextInteractionTime)
                    UpdateHold(m_nearbyVan.Settings.EnterHoldDuration, RequestNearbyEntry);
                else if (m_rearInteractionVan == null) m_holdProgress = 0f;
            }
        }

        public bool HasPriorityWorldInteraction()
        {
            if (!IsOwner || CurrentVan != null) return false;
            FindRearInteractionVan();
            return m_rearInteractionVan != null;
        }

        private void FindRearInteractionVan()
        {
            m_rearInteractionVan = null;
            float closest = float.PositiveInfinity;
            foreach (VanController van in VanController.ActiveVans)
            {
                if (van == null || van.Settings == null || van.IsDestroyed || van.IsOccupied || !van.IsPlayerNearRear(transform)) continue;
                float distance = (van.RearDoorInteractionPoint.position - transform.position).sqrMagnitude;
                if (distance < closest) { closest = distance; m_rearInteractionVan = van; }
            }
        }

        private string GetRearPrompt(VanController van)
        {
            if (!van.RearDoorsOpen) return "Press E to Open Doors";
            PlayerCouchCarrier carrier = GetComponent<PlayerCouchCarrier>();
            CouchGuys.Gameplay.Couch.CouchCarryController couch = carrier != null ? carrier.CarriedCouch : null;
            float loadingRange = van.Settings.CouchLoadingRange;
            if (couch != null &&
                (couch.transform.position - van.CargoLoadingPoint.position).sqrMagnitude <= loadingRange * loadingRange)
                return "Press E to Load Couch";
            if (couch == null && van.LoadedCouchCount > 0) return "Press E to Unload Couch";
            return "Press E to Close Doors";
        }

        private void SendDrivingInput(VanController current)
        {
            if (Time.unscaledTime < m_nextInputSendTime) return;
            m_nextInputSendTime = Time.unscaledTime + 0.05f;
            SendDrivingInputServerRpc(current.NetworkObject, m_input.VehicleMove);
        }

        private void UpdateHold(float duration, System.Action completed)
        {
            if (!m_input.VehicleInteractHeld) { m_holdProgress = 0f; return; }
            m_holdProgress += Time.unscaledDeltaTime;
            if (m_holdProgress < duration) return;
            m_holdProgress = 0f;
            m_nextInteractionTime = Time.time + (CurrentVan != null ? CurrentVan.Settings.InteractionCooldown : m_nearbyVan.Settings.InteractionCooldown);
            completed?.Invoke();
        }

        private void RequestNearbyEntry()
        {
            if (m_nearbyVan != null) RequestEnterServerRpc(m_nearbyVan.NetworkObject);
        }

        private void FindNearbyVan()
        {
            m_nearbyVan = null;
            float closest = float.PositiveInfinity;
            foreach (VanController van in VanController.ActiveVans)
            {
                if (van == null || van.Settings == null || !van.HasAvailableSeat) continue;
                float distance = (van.transform.position - transform.position).sqrMagnitude;
                float range = van.Settings.InteractionRange;
                if (distance <= range * range && distance < closest) { closest = distance; m_nearbyVan = van; }
            }
        }

        [ServerRpc]
        private void RequestRearInteractionServerRpc(NetworkObject vanObject)
        {
            if (CurrentVan != null || vanObject == null || !vanObject.TryGetComponent(out VanController van)) return;
            van.InteractWithRearServer(this, GetComponent<PlayerCouchCarrier>());
        }

        [ServerRpc]
        private void RequestEnterServerRpc(NetworkObject vanObject)
        {
            if (CurrentVan != null || vanObject == null || !vanObject.TryGetComponent(out VanController van)) return;
            if (TryGetComponent(out PlayerHealth health) && (!health.IsAlive || health.IsKnockedDown)) return;
            if (TryGetComponent(out PlayerCouchCarrier carrier) && carrier.IsCarrying) return;
            if (!van.TryEnterServer(this, out int seatIndex)) return;
            m_currentSeatIndex.Value = seatIndex;
            m_currentVanObject.Value = vanObject;
        }

        [Server]
        internal void ShowVehicleFeedbackServer(string message)
        {
            if (Owner != null) ShowVehicleFeedbackTargetRpc(Owner, message);
        }

        [TargetRpc]
        private void ShowVehicleFeedbackTargetRpc(NetworkConnection connection, string message)
        {
            m_vehicleFeedback = message;
            m_vehicleFeedbackUntil = Time.unscaledTime + 1.75f;
        }

        [ServerRpc]
        private void RequestExitServerRpc() => ExitServer(CurrentVan);

        [ServerRpc]
        private void SendDrivingInputServerRpc(NetworkObject vanObject, Vector2 input)
        {
            if (vanObject != null && vanObject == m_currentVanObject.Value && vanObject.TryGetComponent(out VanController van))
                van.SetInputServer(this, input);
        }

        [Server]
        internal void ForceExitServer(VanController van) => ExitServer(van);

        [Server]
        private void ExitServer(VanController van)
        {
            if (van == null || CurrentVan != van) return;
            van.TryGetExitPosition(NetworkObject, out Vector3 exitPosition, out Quaternion exitRotation);
            van.LeaveServer(this);
            m_currentVanObject.Value = null;
            m_currentSeatIndex.Value = -1;
            m_controller.Teleport(exitPosition, exitRotation);
        }

        private void OnCurrentVanChanged(NetworkObject previous, NetworkObject next, bool asServer)
        {
            VanController van = next != null && next.TryGetComponent(out VanController value) ? value : null;
            ApplyVehicleState(van);
        }

        private void OnCurrentSeatIndexChanged(int previous, int next, bool asServer)
        {
            ApplyVehicleState(CurrentVan);
        }

        private void LateUpdate()
        {
            VanController van = CurrentVan;
            if (van == null) return;
            Transform seat = van.GetSeatAnchor(m_currentSeatIndex.Value);
            if (seat != null) transform.SetPositionAndRotation(seat.position, seat.rotation);
        }

        private void ApplyVehicleState(VanController van)
        {
            bool seated = van != null;
            if (m_visualRoot != null) m_visualRoot.gameObject.SetActive(true);
            m_controller?.SetExternalControlPaused(seated);
            if (m_characterController != null) m_characterController.enabled = !seated;
            m_animationDriver?.SetVehicleSeated(seated, m_currentSeatIndex.Value == 0);
            if (m_hoverVfx != null) m_hoverVfx.enabled = !seated;
            if (!IsOwner) return;
            m_input?.SetGameplaySuppressed(seated);
            if (seated)
            {
                m_cameraController?.FollowVehicle(van.CameraTarget, van.Settings.CameraFollowOffset,
                    van.Settings.CameraFollowSmoothness, van.Settings.CameraRotationSmoothness,
                    van.Settings.CameraCollisionRadius);
                if (m_playerCamera != null) m_playerCamera.fieldOfView = van.Settings.CameraFieldOfView;
            }
            else
            {
                m_cameraController?.FollowPlayer();
                if (m_playerCamera != null) m_playerCamera.fieldOfView = m_originalFieldOfView;
                m_nextInteractionTime = Time.time + 0.6f;
            }
        }
    }
}
