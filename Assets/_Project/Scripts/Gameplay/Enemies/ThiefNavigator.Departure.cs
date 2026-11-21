using CouchGuys.Gameplay.Depot;
using System.Collections.Generic;
using CouchGuys.Player;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    public sealed partial class ThiefNavigator
    {
        private enum DeparturePhase : byte { None, Chase, Frustration, Retreat }

        private readonly SyncVar<DeparturePhase> m_departurePhase = new();
        private readonly Dictionary<NetworkConnection, float> m_hiddenReports = new();
        private readonly ThiefSettings.DepartureSettings m_defaultDeparture = new();
        private ThiefSettings.DepartureSettings Departure => m_settings != null
            ? m_settings.Departure : m_defaultDeparture;
        private PlayerHealth m_departureTarget;
        private float m_departureDeadline;
        private float m_lastTargetSightTime;
        private float m_nextVisibilityReport;
        private float m_nextRetreatPath;
        private Renderer[] m_departureRenderers;

        public bool IsDeparting => m_departurePhase.Value != DeparturePhase.None;
        public bool IsFrustrated => m_departurePhase.Value == DeparturePhase.Frustration;
        public bool CanAttack => m_speaker == null && !DepotSafeZone.Contains(transform.position) && !IsCarrying && !IsRidingHoverboard && (!IsDeparting || m_departurePhase.Value == DeparturePhase.Chase);
        public float FrustrationDuration => Mathf.Max(0.1f, Departure.FrustrationDuration);

        private void ResetDepartureServer()
        {
            m_departurePhase.Value = DeparturePhase.None;
            m_departureTarget = null;
            m_hiddenReports.Clear();
            m_nextRetreatPath = 0f;
            if (m_agent != null)
            {
                m_agent.speed = m_normalNavigationSpeed;
                m_agent.updateRotation = m_normalUpdateRotation;
            }
        }

        [Server]
        public void BeginDepartureServer()
        {
            ReleaseHoverboardServer();
            if (IsDeparting || (m_health != null && !m_health.IsAlive)) return;
            m_departureTarget = m_grappler != null ? m_grappler.Target : m_target;
            bool pursuing = !IsAlerting && m_departureTarget != null &&
                m_departureTarget.IsEnemyTargetable && m_agent != null &&
                m_agent.enabled && m_agent.isOnNavMesh && !m_agent.isStopped &&
                m_agent.hasPath && m_agent.remainingDistance > m_agent.stoppingDistance;
            ReleaseCarriedCouchServer();
            SetConstrainedCarryMovement(false);
            m_alerting.Value = false;
            m_retaliationTarget = null;
            m_hasDestination = false;
            m_nextPathTime = 0f;
            if (pursuing && Departure.ChaseDuration > 0f)
            {
                m_departurePhase.Value = DeparturePhase.Chase;
                m_departureDeadline = Time.time + Departure.ChaseDuration;
                m_lastTargetSightTime = Time.time;
            }
            else BeginFrustrationServer();
        }

        private bool UpdateDepartureServer()
        {
            if (!IsDeparting) return false;
            if (m_departurePhase.Value == DeparturePhase.Chase)
            {
                bool validTarget = m_departureTarget != null && m_departureTarget.IsEnemyTargetable;
                if (validTarget && CanSeeDepartureTarget()) m_lastTargetSightTime = Time.time;
                if (!validTarget || Time.time >= m_departureDeadline ||
                    Time.time - m_lastTargetSightTime >= Mathf.Max(0.1f, Departure.LostSightDuration))
                    BeginFrustrationServer();
                else UpdateFightObjective(m_departureTarget);
                return true;
            }

            m_weapon?.SetTargetServer(null);
            if (m_agent == null || !m_agent.enabled || !m_agent.isOnNavMesh) return true;
            if (IsFrustrated)
            {
                m_agent.isStopped = true;
                if (Time.time < m_departureDeadline) return true;
                m_departurePhase.Value = DeparturePhase.Retreat;
                m_agent.speed = m_normalNavigationSpeed * Mathf.Max(0.1f, Departure.RetreatSpeedMultiplier);
                m_agent.updateRotation = true;
                m_agent.isStopped = false;
            }

            if (Time.time >= m_nextRetreatPath)
            {
                m_nextRetreatPath = Time.time + 1f;
                FindRetreatPath();
            }
            if (IsFarFromEveryPlayer() && IsHiddenFromEveryObserver()) Despawn(NetworkObject);
            return true;
        }

        private void BeginFrustrationServer()
        {
            m_departurePhase.Value = DeparturePhase.Frustration;
            m_departureDeadline = Time.time + FrustrationDuration;
            m_weapon?.SetTargetServer(null);
            m_grappler?.StopForDepartureServer();
            m_target = null;
            m_hasDestination = false;
            if (m_agent != null && m_agent.enabled && m_agent.isOnNavMesh)
            {
                m_agent.ResetPath();
                m_agent.isStopped = true;
            }
        }

        private bool CanSeeDepartureTarget()
        {
            Vector3 origin = transform.position + Vector3.up * 1.3f;
            Vector3 offset = m_departureTarget.AimPoint - origin;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, offset.normalized,
                         offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) ||
                    hit.transform.IsChildOf(m_departureTarget.transform)) continue;
                return false;
            }
            return true;
        }

        private void FindRetreatPath()
        {
            PlayerHealth nearest = null;
            float nearestSquared = float.PositiveInfinity;
            foreach (PlayerHealth player in PlayerHealth.Instances)
            {
                if (player == null || !player.IsSpawned) continue;
                float distance = (player.transform.position - transform.position).sqrMagnitude;
                if (distance < nearestSquared) { nearestSquared = distance; nearest = player; }
            }
            Vector3 away = nearest != null
                ? Vector3.ProjectOnPlane(transform.position - nearest.transform.position, Vector3.up)
                : transform.forward;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            float bestScore = float.NegativeInfinity;
            Vector3 best = transform.position;
            // Include side routes so a wall or road end does not strand a departing thief.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector3 direction = Quaternion.AngleAxis(attempt * 30f, Vector3.up) * away.normalized;
                Vector3 candidate = transform.position + direction * Mathf.Max(1f, Departure.RetreatDistance);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, m_targetNavMeshSampleRadius,
                        m_agent.areaMask) || !HasPath(hit.position)) continue;
                float score = float.PositiveInfinity;
                foreach (PlayerHealth player in PlayerHealth.Instances)
                    if (player != null && player.IsSpawned)
                        score = Mathf.Min(score, (hit.position - player.transform.position).sqrMagnitude);
                if (float.IsPositiveInfinity(score)) score = (hit.position - transform.position).sqrMagnitude;
                if (score <= bestScore) continue;
                bestScore = score;
                best = hit.position;
            }
            if (float.IsNegativeInfinity(bestScore)) return;
            m_agent.stoppingDistance = 0.25f;
            m_agent.isStopped = false;
            m_agent.SetDestination(best);
        }

        private bool IsFarFromEveryPlayer()
        {
            float minimumSquared = Mathf.Pow(Mathf.Max(1f, Departure.DespawnDistance), 2f);
            foreach (PlayerHealth player in PlayerHealth.Instances)
                if (player != null && player.IsSpawned &&
                    (player.transform.position - transform.position).sqrMagnitude < minimumSquared)
                    return false;
            return true;
        }

        private bool IsHiddenFromEveryObserver()
        {
            foreach (NetworkConnection observer in NetworkObject.Observers)
                if (!m_hiddenReports.TryGetValue(observer, out float reportedAt) ||
                    Time.time - reportedAt > 1.5f) return false;
            return true;
        }

        private void LateUpdate()
        {
            UpdateHoverboardPose();
            if (!IsClientInitialized || m_departurePhase.Value != DeparturePhase.Retreat ||
                Time.unscaledTime < m_nextVisibilityReport) return;
            m_nextVisibilityReport = Time.unscaledTime + 0.4f;
            m_departureRenderers ??= GetComponentsInChildren<Renderer>(true);
            bool visible = false;
            foreach (Camera camera in Camera.allCameras)
            {
                if (!camera.isActiveAndEnabled || camera.cameraType != CameraType.Game ||
                    camera.targetTexture != null) continue;
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                foreach (Renderer renderer in m_departureRenderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                    if (GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) { visible = true; break; }
                }
                if (visible) break;
            }
            ReportDepartureVisibilityServerRpc(visible);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportDepartureVisibilityServerRpc(bool visible, NetworkConnection sender = null)
        {
            if (sender == null || !NetworkObject.Observers.Contains(sender)) return;
            if (visible) m_hiddenReports.Remove(sender);
            else m_hiddenReports[sender] = Time.time;
        }
    }
}
