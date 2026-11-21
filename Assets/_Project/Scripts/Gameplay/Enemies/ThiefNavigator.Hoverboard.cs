using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    public sealed partial class ThiefNavigator
    {
        private readonly SyncVar<NetworkObject> m_hoverboard = new();
        private MagneticHoverboardController m_appliedHoverboard;
        private bool m_hoverboardApplied;
        private NetworkTransform m_rideNetworkTransform;
        public bool IsRidingHoverboard => m_hoverboard.Value != null;
        internal bool CanStealHoverboard => ((ICouchCarrierServer)this).IsCarrierAvailable &&
            !IsCarrying && !IsRidingHoverboard && (m_health == null || !m_health.IsBeingKnockedBack);

        private void UpdateHoverboardObjective(MagneticHoverboardController board)
        {
            if (board.IsDestroyed) return;
            if (board.Driver == null)
            {
                if (m_deliveryManager == null || m_deliveryManager.GetGetawayForCouch(board.Couch) == null) return;
                m_weapon?.SetTargetServer(null);
                if (board.TryMountThiefServer(this))
                {
                    SetConstrainedCarryMovement(false);
                    m_hoverboard.Value = board.NetworkObject;
                    ApplyHoverboardState();
                    m_deliveryManager.GetGetawayForCouch(board.Couch)?.NotifyCouchStolenServer();
                }
                else MoveTo(board.MountPosition, .25f);
                return;
            }
            if (board.HasThiefDriver)
            {
                UpdateFightObjective(GetClosestLivingTarget());
                return;
            }
            m_weapon?.SetHoverboardTargetServer(board);
            if (m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh) return;
            m_agent.stoppingDistance = Mathf.Max(.25f, (m_weapon != null ? m_weapon.PreferredRange : 1.25f) * .72f);
            Vector3 aim = Vector3.ProjectOnPlane(board.CameraTarget.position - transform.position, Vector3.up);
            if (aim.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(aim), m_aimTurnSpeed * Time.deltaTime);
            if (Time.time < m_nextPathTime) return;
            m_nextPathTime = Time.time + m_pathRefreshInterval;
            if (!m_hasDestination || Time.time >= m_nextRepositionTime ||
                (board.transform.position - m_lastTargetPosition).sqrMagnitude >= m_targetMovementThreshold * m_targetMovementThreshold)
                RecalculateCombatDestination(board.transform.position, board.transform);
            ApplyDestination();
        }

        private bool UpdateHoverboardRideServer()
        {
            if (!IsRidingHoverboard) return false;
            var board = m_hoverboard.Value.GetComponent<MagneticHoverboardController>();
            CouchCarryController couch = GetActiveCouch();
            if (board == null || board.IsDestroyed || board.Driver != NetworkObject || board.Couch != couch ||
                couch == null || IsDeparting || DepotSafeZone.Contains(couch.transform.position) ||
                m_health != null && (!m_health.IsAlive || m_health.IsBeingKnockedBack))
            {
                ReleaseHoverboardServer();
                return false;
            }
            m_weapon?.SetTargetServer(null);
            var getaway = m_deliveryManager != null ? m_deliveryManager.GetGetawayForCouch(couch) : null;
            if (getaway == null || !getaway.IsAvailable)
            {
                ReleaseHoverboardServer();
                return false;
            }
            Vector3 direction = getaway.GetCarryMovementIntent(couch);
            board.SetInputServer(NetworkObject, CalculateHoverboardInput(board, direction));
            return true;
        }

        internal static Vector2 CalculateHoverboardInput(MagneticHoverboardController board, Vector3 direction)
        {
            if (direction.sqrMagnitude < .001f) return Vector2.zero;
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(board.transform.forward, Vector3.up), direction, Vector3.up);
            bool reverse = Mathf.Abs(angle) > 110f;
            float steering = Mathf.Clamp(angle / 45f, -1f, 1f) * (reverse ? -1f : 1f);
            // Use the existing physical motor, at walking speed so tight carry paths remain manageable.
            float speed = Mathf.Lerp(3f, 1f, Mathf.Clamp01(Mathf.Abs(angle) / 90f));
            float throttle = reverse ? -speed / board.Settings.ReverseSpeed : speed / board.Settings.LoadedSpeed;
            return new Vector2(steering, Mathf.Clamp(throttle, -1f, 1f));
        }

        [Server]
        internal void ReleaseHoverboardServer()
        {
            if (!IsRidingHoverboard) return;
            var board = m_hoverboard.Value.GetComponent<MagneticHoverboardController>();
            if (board != null) board.ReleaseDriverServer(NetworkObject);
            m_hoverboard.Value = null;
            RestoreHoverboardState();
            // Never reattach the agent inside the board's cargo or chassis.
            if (board != null && m_agent != null)
            {
                Vector3 origin = board.MountPosition;
                transform.position = origin + board.transform.right * (board.Settings.DismountDistance + 1f);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 candidate = origin + Quaternion.Euler(0f, i * 90f, 0f) * board.transform.right * (board.Settings.DismountDistance + 1f);
                    if (!NavMesh.SamplePosition(candidate, out var hit, 2f, m_agent.areaMask) ||
                        !NpcImpactPhysics.TryStandingPosition(transform, m_carrierCollider, hit.position, out _)) continue;
                    transform.position = hit.position;
                    if (m_health == null || m_health.IsAlive && !m_health.IsBeingKnockedBack)
                    {
                        m_agent.enabled = true;
                        m_agent.Warp(hit.position);
                    }
                    break;
                }
            }
            m_hasDestination = false;
            m_nextPathTime = 0f;
            m_rideNetworkTransform?.Teleport();
        }

        private void OnHoverboardChanged(NetworkObject previous, NetworkObject next, bool asServer) => ApplyHoverboardState();

        private void ApplyHoverboardState()
        {
            var board = m_hoverboard.Value != null ? m_hoverboard.Value.GetComponent<MagneticHoverboardController>() : null;
            if (m_hoverboardApplied && board != null && board == m_appliedHoverboard) return;
            RestoreHoverboardState();
            if (board == null) return;
            m_appliedHoverboard = board;
            m_hoverboardApplied = true;
            m_agent.enabled = false;
            m_rideNetworkTransform ??= GetComponent<NetworkTransform>();
            m_rideNetworkTransform?.SetSynchronizePosition(false);
            m_rideNetworkTransform?.SetSynchronizeRotation(false);
            if (m_carrierCollider != null)
                foreach (var collider in board.GetComponentsInChildren<Collider>(true))
                    Physics.IgnoreCollision(m_carrierCollider, collider, true);
        }

        private void RestoreHoverboardState()
        {
            if (!m_hoverboardApplied) return;
            m_hoverboardApplied = false;
            if (m_carrierCollider != null && m_appliedHoverboard != null)
                foreach (var collider in m_appliedHoverboard.GetComponentsInChildren<Collider>(true))
                    Physics.IgnoreCollision(m_carrierCollider, collider, false);
            m_appliedHoverboard = null;
            m_rideNetworkTransform?.SetSynchronizePosition(true);
            m_rideNetworkTransform?.SetSynchronizeRotation(true);
        }

        private void UpdateHoverboardPose()
        {
            if (!IsRidingHoverboard || m_health != null && (!m_health.IsAlive || m_health.IsBeingKnockedBack)) return;
            ApplyHoverboardState(); // Also resolves the board when joining an existing ride.
            if (m_appliedHoverboard != null)
                transform.SetPositionAndRotation(m_appliedHoverboard.DriverAnchor.position, m_appliedHoverboard.DriverAnchor.rotation);
        }
    }
}
