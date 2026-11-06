using CouchGuys.CameraSystem;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Input;
using CouchGuys.Networking;
using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Player
{
    [DefaultExecutionOrder(-30)]
    [RequireComponent(typeof(NetworkObject), typeof(PlayerInputReader), typeof(CharacterController))]
    public sealed class PlayerHoverboardInteractor : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private ThirdPersonPlayerController m_controller;
        [SerializeField] private CharacterController m_capsule;
        [SerializeField] private ThirdPersonCameraController m_camera;
        [SerializeField] private NetworkTransform m_networkTransform;
        [SerializeField] private CapsuleCollider m_driverHitbox;
        private MagneticHoverboardController m_hitboxBoard;
        private readonly SyncVar<NetworkObject> m_board = new();
        private MagneticHoverboardController m_nearby;
        private float m_nextInput, m_nextInteraction, m_nextServerInteraction;
        private bool m_magnet, m_applied, m_exitPending;
        private string m_feedback;
        private float m_feedbackUntil;
        public MagneticHoverboardController CurrentBoard => m_board.Value != null ? m_board.Value.GetComponent<MagneticHoverboardController>() : null;
        public bool IsMounted => m_board.Value != null;
        public bool CanUseMagnet => IsSpawned && !GameplaySessionController.IsServerGameOver &&
            TryGetComponent(out PlayerHealth h) && h.IsAlive && !h.IsKnockedDown && !h.HasReviveInteraction &&
            !(TryGetComponent(out PlayerTruckInteractor t) && t.CurrentTruck != null) && !IsMounted;
        public bool CanInteract => CanUseMagnet && !(TryGetComponent(out PlayerCouchCarrier c) && c.IsCarrying);
        public bool CanMount => CanInteract;
        public Vector3 LoadingOrigin => m_capsule != null ? transform.TransformPoint(m_capsule.center) : transform.position + Vector3.up;
        public string InteractionPrompt
        {
            get
            {
                if (Time.time < m_feedbackUntil) return m_feedback;
                if (IsMounted) return "Press E to Dismount Hoverboard";
                if (!HasPriorityWorldInteraction()) return null;
                return m_magnet ? m_nearby.Couch != null ? "Press E to Release Couch" :
                    TryGetComponent(out PlayerCouchCarrier carrier) && carrier.IsCarrying ? "Press E to Load Couch" : "Press E to Attach Couch" : "Press E to Drive Hoverboard";
            }
        }
        private void Awake()
        {
            m_input ??= GetComponent<PlayerInputReader>(); m_controller ??= GetComponent<ThirdPersonPlayerController>();
            m_capsule ??= GetComponent<CharacterController>(); m_networkTransform ??= GetComponent<NetworkTransform>();
            m_camera ??= GetComponentInChildren<ThirdPersonCameraController>(true);
            m_board.OnChange += OnBoardChanged;
        }
        private void OnDestroy() => m_board.OnChange -= OnBoardChanged;
        public override void OnStartClient() { base.OnStartClient(); ApplyState(); }
        public override void OnStopServer() { ReleaseServer(true); base.OnStopServer(); }
        public override void OnStopClient() { if (m_applied) RestoreControls(); base.OnStopClient(); }

        // Queried by the existing truck/couch interaction pipeline before consuming E.
        public bool HasPriorityWorldInteraction()
        {
            if (IsMounted) return true;
            m_nearby = null;
            if (!IsOwner || !CanUseMagnet) return false;
            PlayerCouchCarrier carrier = GetComponent<PlayerCouchCarrier>();
            var carried = carrier != null ? carrier.CarriedCouch : null;
            float closest = float.PositiveInfinity;
            foreach (MagneticHoverboardController board in MagneticHoverboardController.ActiveBoards)
            {
                if (board == null || !board.IsSpawned || board.Settings == null || board.Driver != null || board.Speed > board.Settings.MagnetMaximumSpeed) continue;
                bool magnet = board.IsNearMagnet(LoadingOrigin,carried) && (carried != null ?
                    board.Couch == null && carried.ActiveCarrierCount == 1 && !carried.IsSled && !carried.IsLoaded &&
                    carried.IsPickupAvailable && board.IsCouchInLoadingRange(carried) : board.Couch != null || board.FindAvailableCouch() != null);
                if (carried != null && !magnet) continue;
                float distance = Vector3.Distance(transform.position, magnet ? board.MagnetPosition : board.MountPosition);
                if (!magnet && distance > board.Settings.MountRange || distance >= closest) continue;
                closest = distance; m_nearby = board; m_magnet = magnet;
            }
            return m_nearby != null;
        }
        private void Update()
        {
            if (!IsOwner || m_input == null) return;
            if (TryGetComponent(out PlayerHealth h) && (!h.IsAlive || h.IsKnockedDown)) return;
            MagneticHoverboardController current = CurrentBoard;
            if (current != null && Time.unscaledTime >= m_nextInput)
            {
                m_nextInput = Time.unscaledTime + .05f;
                SendInputServerRpc(m_input.VehicleMove);
            }
            if (Time.time < m_nextInteraction || !m_input.VehicleInteractPressedThisFrame) return;
            if (current != null)
            {
                if (!m_input.TryConsumeInteractPress(true)) return;
                m_nextInteraction = Time.time + current.Settings.InteractionCooldown; RequestExitServerRpc();
            }
            else if (HasPriorityWorldInteraction())
            {
                if (!m_input.TryConsumeInteractPress(true)) return;
                m_nextInteraction = Time.time + m_nearby.Settings.InteractionCooldown;
                RequestInteractionServerRpc(m_nearby.NetworkObject, m_magnet);
            }
        }
        [ServerRpc] private void SendInputServerRpc(Vector2 input)
        {
            if (TryGetComponent(out PlayerHealth h) && h.IsAlive && !h.IsKnockedDown) CurrentBoard?.SetInputServer(NetworkObject,input);
        }
        [ServerRpc] private void RequestInteractionServerRpc(NetworkObject boardObject, bool magnet)
        {
            if (Time.time < m_nextServerInteraction || (magnet ? !CanUseMagnet : !CanMount) || boardObject == null || !boardObject.TryGetComponent(out MagneticHoverboardController board)) return;
            m_nextServerInteraction = Time.time + board.Settings.InteractionCooldown;
            if (magnet)
            {
                if (!board.InteractMagnetServer(this, out string reason)) FeedbackTargetRpc(Owner,reason);
                else FeedbackTargetRpc(Owner,string.Empty);
            }
            else if (board.TryMountServer(this))
            {
                m_board.Value = boardObject; m_exitPending = false; ApplyState();
            }
        }
        [ServerRpc] private void RequestExitServerRpc()
        {
            MagneticHoverboardController board=CurrentBoard;
            if (board==null || Time.time < m_nextServerInteraction) return;
            m_nextServerInteraction=Time.time+board.Settings.InteractionCooldown; ReleaseServer(false);
        }
        [Server] public void ReleaseServer(bool forced)
        {
            MagneticHoverboardController board = CurrentBoard;
            if (board == null) return;
            bool safe = board.TryGetDriverExit(m_capsule,out Vector3 position,out Quaternion rotation);
            if (!safe && !forced) { FeedbackTargetRpc(Owner,"Dismount area blocked"); return; }
            if (!safe) { position=transform.position+Vector3.up*.5f; rotation=transform.rotation; }
            board.ReleaseDriverServer(NetworkObject);
            m_board.Value=null; ApplyState();
            m_controller.Teleport(position,rotation);
            m_networkTransform.Teleport();
            ExitObserversRpc(position,rotation);
        }
        [TargetRpc] private void FeedbackTargetRpc(NetworkConnection owner,string message)
        { m_feedback=message; m_feedbackUntil=string.IsNullOrEmpty(message)?0f:Time.time+2f; }
        [ObserversRpc(RunLocally=true)] private void ExitObserversRpc(Vector3 position,Quaternion rotation)
        {
            m_exitPending=true;
            RestoreControls(); m_controller.Teleport(position,rotation);
            if (IsOwner || IsServerInitialized) m_networkTransform.Teleport();
        }
        private void OnBoardChanged(NetworkObject previous,NetworkObject next,bool asServer)
        {
            if (next!=null) m_exitPending=false;
            ApplyState();
        }
        private void ApplyState()
        {
            MagneticHoverboardController board=CurrentBoard;
            if (board == null) { if (m_applied) RestoreControls(); return; }
            m_applied=true;
            m_controller.SetExternalControlPaused(true); m_capsule.enabled=false;
            SetDriverHitbox(board);
            m_networkTransform.SetSynchronizePosition(false); m_networkTransform.SetSynchronizeRotation(false);
            GetComponent<CouchGuyAnimationDriver>()?.SetVehicleSeated(true,true);
            GetComponent<CouchGuyAnimationDriver>()?.SetHoverboardGrips(board.LeftGrip,board.RightGrip);
            if (TryGetComponent(out RobotHoverVFX vfx)) vfx.enabled=false;
            if (!IsOwner) return;
            m_input.SetGameplaySuppressed(true);
            MagneticHoverboardSettings s=board.Settings;
            m_camera.FollowVehicle(board.CameraTarget,s.CameraDistance,s.CameraHeight,s.CameraSensitivity,-15,60,s.CameraSmoothing,s.CameraSmoothing,.3f);
        }
        private void RestoreControls()
        {
            m_applied=false;
            SetDriverHitbox(null);
            m_controller.SetExternalControlPaused(false);
            // Health owns the capsule while knocked down / ragdolled.
            bool healthy=!TryGetComponent(out PlayerHealth h) || h.IsAlive && !h.IsKnockedDown;
            m_capsule.enabled=healthy;
            m_networkTransform.SetSynchronizePosition(true); m_networkTransform.SetSynchronizeRotation(true);
            GetComponent<CouchGuyAnimationDriver>()?.SetVehicleSeated(false,false);
            GetComponent<CouchGuyAnimationDriver>()?.SetHoverboardGrips(null,null);
            if (TryGetComponent(out RobotHoverVFX vfx)) vfx.enabled=healthy;
            if (IsOwner) { m_input.SetGameplaySuppressed(false); m_camera.FollowPlayer(); }
        }
        private void LateUpdate()
        {
            MagneticHoverboardController board=CurrentBoard;
            if (m_exitPending) { if(board==null)m_exitPending=false; return; }
            if (board!=null && board.DriverAnchor!=null)
                transform.SetPositionAndRotation(board.DriverAnchor.position,board.DriverAnchor.rotation);
        }
        private void SetDriverHitbox(MagneticHoverboardController board)
        {
            if (m_driverHitbox == null || m_hitboxBoard == board) return;
            if (m_hitboxBoard != null)
                foreach (Collider collider in m_hitboxBoard.GetComponentsInChildren<Collider>(true))
                    Physics.IgnoreCollision(m_driverHitbox,collider,false);
            m_hitboxBoard=board;
            m_driverHitbox.enabled=board!=null;
            if (board!=null)
                foreach (Collider collider in board.GetComponentsInChildren<Collider>(true))
                    Physics.IgnoreCollision(m_driverHitbox,collider,true);
        }
    }
}
