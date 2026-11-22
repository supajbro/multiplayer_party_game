using System.Collections.Generic;
using FishNet.Component.Transforming;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    public sealed partial class TrafficVehicleController
    {
        private bool m_isDistantSimulation;
        private float m_nextDistantUpdate, m_lastDistantUpdate, m_fullPhysicsUntil;
        private Vector3 m_distantVelocity;
        private NetworkTransform m_networkTransform;
        private CollisionDetectionMode m_defaultCollisionMode;
        private float m_defaultLinearDamping, m_defaultAngularDamping, m_defaultMaxAngularVelocity;
        private RigidbodyConstraints m_defaultConstraints;
        private TrafficSettings m_simulationSettings;
        private byte m_appliedNetworkInterval;

        public bool IsDistantSimulation => m_isDistantSimulation;
        internal float DrivingSpeed => m_settings != null ? m_settings.MaximumDrivingSpeed : 13f;
        internal bool CanPool => !IsDestroyed && !IsBatLaunched && m_rogueSettings == null;
        internal bool HasRecentInteraction => Time.time < m_fullPhysicsUntil;

        private void CaptureSimulationDefaults()
        {
            m_networkTransform = GetComponent<NetworkTransform>();
            m_defaultCollisionMode = m_rigidbody.collisionDetectionMode;
            m_defaultLinearDamping = m_rigidbody.linearDamping;
            m_defaultAngularDamping = m_rigidbody.angularDamping;
            m_defaultMaxAngularVelocity = m_rigidbody.maxAngularVelocity;
            m_defaultConstraints = m_rigidbody.constraints;
        }

        private void ResetSimulationServer()
        {
            m_isDistantSimulation = false;
            m_fullPhysicsUntil = 0f;
            m_appliedNetworkInterval = 0;
            m_simulationSettings = null;
            m_route = null;
            m_manager = null;
            m_rogueSettings = null;
            m_serverDriving = false;
            m_pathObstructed = false;
            m_nextObstacleCheck = 0f;
            m_nextImpactTime.Clear();
            m_wheelSteering.Value = 0f;
            m_rigidbody.isKinematic = false;
            m_rigidbody.detectCollisions = true;
            m_rigidbody.useGravity = true;
            m_rigidbody.constraints = m_defaultConstraints;
            m_rigidbody.collisionDetectionMode = m_defaultCollisionMode;
            m_rigidbody.linearDamping = m_defaultLinearDamping;
            m_rigidbody.angularDamping = m_defaultAngularDamping;
            m_rigidbody.maxAngularVelocity = m_defaultMaxAngularVelocity;
            m_rigidbody.linearVelocity = Vector3.zero;
            m_rigidbody.angularVelocity = Vector3.zero;
            SetVisualColour(Color.white);
        }

        internal void RefreshSimulationServer(IReadOnlyList<TrafficObserver> observers, TrafficSettings settings)
        {
            if (!IsServerInitialized || !m_serverDriving || m_settings == null) return;
            m_simulationSettings = settings;
            bool protectedPhysics = IsDestroyed || IsBatLaunched || m_rogueSettings != null ||
                Time.time < m_fullPhysicsUntil || (!m_isDistantSimulation &&
                (Mathf.Abs(m_rigidbody.linearVelocity.y) > 2f || Vector3.Dot(transform.up, Vector3.up) < 0.8f));
            bool distant = !protectedPhysics && !TrafficSimulationPolicy.NeedsFullPhysics(
                m_rigidbody.position, Mathf.Max(DrivingSpeed, m_rigidbody.linearVelocity.magnitude),
                m_isDistantSimulation, observers, settings);
            SetDistantSimulation(distant);
        }

        private void KeepFullPhysicsForInteraction()
        {
            m_fullPhysicsUntil = Time.time + 5f;
            SetDistantSimulation(false);
        }

        private void SetDistantSimulation(bool distant)
        {
            byte interval = distant ? m_simulationSettings.DistantNetworkInterval :
                (m_simulationSettings != null ? m_simulationSettings.NearbyNetworkInterval : (byte)1);
            // Apply after spawn callbacks as well as on transitions, so pooled cars
            // restore the nearby interval even when the new trip starts nearby.
            if (m_networkTransform != null && m_networkTransform.IsServerInitialized && m_appliedNetworkInterval != interval)
            {
                m_networkTransform.SetInterval(interval);
                m_appliedNetworkInterval = interval;
            }
            if (m_isDistantSimulation == distant) return;
            m_isDistantSimulation = distant;
            if (distant)
            {
                m_distantVelocity = transform.forward * DrivingSpeed;
                m_rigidbody.linearVelocity = Vector3.zero;
                m_rigidbody.angularVelocity = Vector3.zero;
                m_rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                m_rigidbody.isKinematic = true;
                // Keep the chassis queryable for long-range weapons. No per-step probes
                // or tyre forces run, and kinematic cars don't collide with each other.
                m_lastDistantUpdate = Time.time;
                m_nextDistantUpdate = Time.time + m_simulationSettings.DistantUpdateInterval *
                    (0.5f + Mathf.Abs(GetInstanceID() % 100) * 0.005f);
            }
            else
            {
                m_rigidbody.isKinematic = false;
                m_rigidbody.collisionDetectionMode = m_defaultCollisionMode;
                m_rigidbody.linearVelocity = m_distantVelocity;
                m_rigidbody.angularVelocity = Vector3.zero;
                m_rigidbody.WakeUp();
                m_pathObstructed = false;
                m_nextObstacleCheck = 0f;
                m_drivingStartPosition = m_rigidbody.position;
                m_stationaryCheckAt = Time.time + 2f;
                m_warnedStationary = false;
            }
        }

        private void UpdateDistantRouteServer()
        {
            if (Time.time < m_nextDistantUpdate || m_route == null || m_settings == null) return;
            float elapsed = Mathf.Min(0.5f, Time.time - m_lastDistantUpdate);
            m_lastDistantUpdate = Time.time;
            m_nextDistantUpdate = Time.time + m_simulationSettings.DistantUpdateInterval;
            if (!AdvanceDistantRoute(elapsed)) m_manager?.DespawnVehicle(this);
        }

        // Waypoints already include road elevation and surface clearance. The car's
        // rounded tyre contacts have their bottom at the root, so no spawn lift is
        // retained here. This avoids hovering and a large drop when physics resumes.
        private bool AdvanceDistantRoute(float elapsed)
        {
            Vector3 position = m_rigidbody.position;
            float remaining = DrivingSpeed * Mathf.Max(0f, elapsed);
            while (m_waypointIndex < m_route.Waypoints.Length)
            {
                Vector3 target = m_route.Waypoints[m_waypointIndex];
                Vector3 delta = target - position;
                float distance = delta.magnitude;
                if (distance > 0.001f)
                {
                    m_distantVelocity = delta / distance * DrivingSpeed;
                    m_rigidbody.rotation = Quaternion.LookRotation(delta / distance, Vector3.up);
                }
                if (distance > remaining)
                {
                    position += delta / distance * remaining;
                    break;
                }
                position = target;
                remaining -= distance;
                m_waypointIndex++;
            }
            m_rigidbody.position = position;
            m_wheelSteering.Value = 0f;
            return m_waypointIndex < m_route.Waypoints.Length;
        }
    }
}
