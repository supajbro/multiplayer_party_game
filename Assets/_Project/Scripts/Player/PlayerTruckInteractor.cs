using CouchGuys.CameraSystem;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Input;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Owner input and server-validated player/truck occupancy bridge.</summary>
    [RequireComponent(typeof(PlayerInputReader), typeof(ThirdPersonPlayerController), typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "CouchGuys.Player", sourceAssembly: "Assembly-CSharp", sourceClassName: "PlayerVanInteractor")]
    public sealed class PlayerTruckInteractor : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private ThirdPersonPlayerController m_controller;
        [SerializeField] private CharacterController m_characterController;
        [SerializeField] private Transform m_visualRoot;
        [SerializeField] private ThirdPersonCameraController m_cameraController;
        [SerializeField] private Camera m_playerCamera;

        private readonly SyncVar<NetworkObject> m_currentTruckObject = new();
        private readonly SyncVar<int> m_currentSeatIndex = new(-1);
        private TruckController m_nearbyTruck;
        private TruckController m_rearInteractionTruck;
        private float m_holdProgress;
        private float m_nextInteractionTime;
        private float m_nextInputSendTime;
        private float m_nextHornRequestTime;
        private float m_originalFieldOfView;
        private string m_worldInteractionPrompt;
        private string m_vehicleFeedback;
        private float m_vehicleFeedbackUntil;
        private CouchGuyAnimationDriver m_animationDriver;
        private RobotHoverVFX m_hoverVfx;
        private bool m_exitPending;

        public TruckController CurrentTruck => m_currentTruckObject.Value != null &&
            m_currentTruckObject.Value.TryGetComponent(out TruckController truck) ? truck : null;
        public TruckController NearbyTruck => m_nearbyTruck;
        public int CurrentSeatIndex => m_currentSeatIndex.Value;
        public bool IsDriver => CurrentTruck != null && m_currentSeatIndex.Value == 0;
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
            m_currentTruckObject.OnChange += OnCurrentTruckChanged;
            m_currentSeatIndex.OnChange += OnCurrentSeatIndexChanged;
        }

        private void OnDestroy()
        {
            m_currentTruckObject.OnChange -= OnCurrentTruckChanged;
            m_currentSeatIndex.OnChange -= OnCurrentSeatIndexChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyVehicleState(CurrentTruck);
        }

        public override void OnStopServer()
        {
            TruckController truck = CurrentTruck;
            if (truck != null) truck.LeaveServer(this);
            m_currentSeatIndex.Value = -1;
            m_currentTruckObject.Value = null;
            base.OnStopServer();
        }

        private void Update()
        {
            if (!IsOwner || m_input == null) return;
            if (TryGetComponent(out PlayerHealth health) && (health.IsKnockedDown || health.HasReviveInteraction))
            {
                m_holdProgress = 0f;
                m_worldInteractionPrompt = string.Empty;
                return;
            }
            TruckController current = CurrentTruck;
            if (current != null)
            {
                m_nearbyTruck = null;
                if (m_currentSeatIndex.Value == 0)
                {
                    SendDrivingInput(current);
                    if (m_input.VehicleHornPressedThisFrame && Time.time >= m_nextHornRequestTime)
                    {
                        m_nextHornRequestTime = Time.time + current.HornCooldown;
                        RequestHornServerRpc();
                    }
                }
                UpdateHold(current.Settings.ExitHoldDuration, RequestExitServerRpc);
            }
            else
            {
                FindRearInteractionTruck();
                if (m_rearInteractionTruck != null)
                {
                    m_nearbyTruck = null;
                    m_holdProgress = 0f;
                    m_worldInteractionPrompt = GetRearPrompt(m_rearInteractionTruck);
                    if (Time.time >= m_nextInteractionTime && m_input.VehicleInteractPressedThisFrame)
                    {
                        m_nextInteractionTime = Time.time + m_rearInteractionTruck.Settings.InteractionCooldown;
                        RequestRearInteractionServerRpc(m_rearInteractionTruck.NetworkObject);
                    }
                }
                else
                {
                    m_worldInteractionPrompt = string.Empty;
                    FindNearbyTruck();
                }
                if (m_rearInteractionTruck == null && m_nearbyTruck != null && Time.time >= m_nextInteractionTime)
                    UpdateHold(m_nearbyTruck.Settings.EnterHoldDuration, RequestNearbyEntry);
                else if (m_rearInteractionTruck == null) m_holdProgress = 0f;
            }
        }

        public bool HasPriorityWorldInteraction()
        {
            if (!IsOwner || CurrentTruck != null) return false;
            FindRearInteractionTruck();
            return m_rearInteractionTruck != null;
        }

        private void FindRearInteractionTruck()
        {
            m_rearInteractionTruck = null;
            float closest = float.PositiveInfinity;
            foreach (TruckController truck in TruckController.ActiveTrucks)
            {
                if (truck == null || truck.Settings == null || truck.IsDestroyed || truck.IsOccupied || !truck.IsPlayerNearRear(transform)) continue;
                float distance = (truck.RearDoorInteractionPoint.position - transform.position).sqrMagnitude;
                if (distance < closest) { closest = distance; m_rearInteractionTruck = truck; }
            }
        }

        private string GetRearPrompt(TruckController truck)
        {
            if (!truck.RearDoorsOpen) return "Press E to Open Doors";
            PlayerCouchCarrier carrier = GetComponent<PlayerCouchCarrier>();
            CouchGuys.Gameplay.Couch.CouchCarryController couch = carrier != null ? carrier.CarriedCouch : null;
            if (couch != null && truck.IsCouchInLoadingRange(couch))
                return "Press E to Load Couch";
            if (couch == null && truck.LoadedCouchCount > 0) return "Press E to Unload Couch";
            return "Press E to Close Doors";
        }

        private void SendDrivingInput(TruckController current)
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
            m_nextInteractionTime = Time.time + (CurrentTruck != null ? CurrentTruck.Settings.InteractionCooldown : m_nearbyTruck.Settings.InteractionCooldown);
            completed?.Invoke();
        }

        private void RequestNearbyEntry()
        {
            if (m_nearbyTruck != null) RequestEnterServerRpc(m_nearbyTruck.NetworkObject);
        }

        private void FindNearbyTruck()
        {
            m_nearbyTruck = null;
            float closest = float.PositiveInfinity;
            foreach (TruckController truck in TruckController.ActiveTrucks)
            {
                if (truck == null || truck.Settings == null || !truck.HasAvailableSeat) continue;
                int seat = truck.FindEntrySeat(transform.position);
                if (seat < 0) continue;
                float distance = (truck.GetDoorPoint(seat).position - transform.position).sqrMagnitude;
                if (distance < closest) { closest = distance; m_nearbyTruck = truck; }
            }
        }

        [ServerRpc]
        private void RequestRearInteractionServerRpc(NetworkObject truckObject)
        {
            if (TryGetComponent(out PlayerHealth health) && health.IsKnockedDown) return;
            if (CurrentTruck != null || truckObject == null || !truckObject.TryGetComponent(out TruckController truck)) return;
            truck.InteractWithRearServer(this, GetComponent<PlayerCouchCarrier>());
        }

        [ServerRpc]
        private void RequestEnterServerRpc(NetworkObject truckObject)
        {
            if (CurrentTruck != null || truckObject == null || !truckObject.TryGetComponent(out TruckController truck)) return;
            if (TryGetComponent(out PlayerHealth health) && (!health.IsAlive || health.IsKnockedDown)) return;
            if (TryGetComponent(out PlayerCouchCarrier carrier) && carrier.IsCarrying) return;
            if (!truck.TryEnterServer(this, out int seatIndex)) return;
            m_currentSeatIndex.Value = seatIndex;
            m_currentTruckObject.Value = truckObject;
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
        private void RequestExitServerRpc() => ExitServer(CurrentTruck);

        [ServerRpc]
        private void RequestHornServerRpc()
        {
            if (m_currentSeatIndex.Value == 0) CurrentTruck?.HornServer(this);
        }

        [ServerRpc]
        private void SendDrivingInputServerRpc(NetworkObject truckObject, Vector2 input)
        {
            if (TryGetComponent(out PlayerHealth health) && health.IsKnockedDown) return;
            if (truckObject != null && truckObject == m_currentTruckObject.Value && truckObject.TryGetComponent(out TruckController truck))
                truck.SetInputServer(this, input);
        }

        [Server]
        internal void DetachDownedServer()
        {
            TruckController truck = CurrentTruck;
            if (truck == null) return;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            truck.LeaveServer(this);
            m_currentTruckObject.Value = null;
            m_currentSeatIndex.Value = -1;
            ApplyExitObserversRpc(position, rotation);
        }

        [Server]
        internal void ForceExitServer(TruckController truck) => ExitServer(truck);

        [Server]
        private void ExitServer(TruckController truck)
        {
            if (truck == null || CurrentTruck != truck) return;
            if (!truck.TryGetExitPosition(NetworkObject, out Vector3 exitPosition, out Quaternion exitRotation))
            {
                ShowVehicleFeedbackServer("Door exit is blocked");
                return;
            }
            truck.LeaveServer(this);
            m_currentTruckObject.Value = null;
            m_currentSeatIndex.Value = -1;
            m_controller.Teleport(exitPosition, exitRotation);
            ApplyExitObserversRpc(exitPosition, exitRotation);
        }

        [ObserversRpc(RunLocally = true)]
        private void ApplyExitObserversRpc(Vector3 position, Quaternion rotation)
        {
            // RPCs can arrive before the occupancy SyncVars. Do not snap back into the seat meanwhile.
            m_exitPending = true;
            m_controller.Teleport(position, rotation);
        }

        private void OnCurrentTruckChanged(NetworkObject previous, NetworkObject next, bool asServer)
        {
            TruckController truck = next != null && next.TryGetComponent(out TruckController value) ? value : null;
            if (truck != null) m_exitPending = false;
            ApplyVehicleState(truck);
        }

        private void OnCurrentSeatIndexChanged(int previous, int next, bool asServer)
        {
            ApplyVehicleState(CurrentTruck);
        }

        private void LateUpdate()
        {
            TruckController truck = CurrentTruck;
            if (m_exitPending)
            {
                if (truck == null) m_exitPending = false;
                return;
            }
            if (truck == null) return;
            Transform seat = truck.GetSeatAnchor(m_currentSeatIndex.Value);
            if (seat != null) transform.SetPositionAndRotation(seat.position + truck.transform.rotation * truck.SeatedRootOffset,
                truck.transform.rotation);
        }

        private void ApplyVehicleState(TruckController truck)
        {
            bool seated = truck != null;
            if (m_visualRoot != null) m_visualRoot.gameObject.SetActive(true);
            m_controller?.SetExternalControlPaused(seated);
            if (m_characterController != null) m_characterController.enabled = !seated;
            m_animationDriver?.SetVehicleSeated(seated, m_currentSeatIndex.Value == 0);
            if (m_hoverVfx != null) m_hoverVfx.enabled = !seated;
            if (!IsOwner) return;
            m_input?.SetGameplaySuppressed(seated);
            if (seated)
            {
                m_cameraController?.FollowVehicle(truck.CameraTarget,
                    truck.Settings.CameraDistance, truck.Settings.CameraHeight,
                    truck.Settings.CameraLookSensitivity,
                    truck.Settings.CameraMinimumPitch, truck.Settings.CameraMaximumPitch,
                    truck.Settings.CameraFollowSmoothness, truck.Settings.CameraRotationSmoothness,
                    truck.Settings.CameraCollisionRadius);
                if (m_playerCamera != null) m_playerCamera.fieldOfView = truck.Settings.CameraFieldOfView;
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
