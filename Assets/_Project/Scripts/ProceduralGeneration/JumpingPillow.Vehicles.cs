using System.Collections.Generic;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Gameplay.Sled;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class JumpingPillow
    {
        [Header("Vehicle Bounce")]
        [SerializeField] private bool m_vehicleBounceEnabled = true;
        [SerializeField, Min(.1f)] private float m_vehicleBounceHeight = 2.5f;
        [SerializeField, Min(.05f)] private float m_vehicleBounceCooldown = .8f;
        [SerializeField, Min(0f)] private float m_vehicleMaximumUpwardSpeed = .5f;
        [SerializeField, Min(.01f)] private float m_vehicleContactDistance = 1f;
        [Tooltip("Allows ground-following distant traffic to enter full physics when crossing the raised pillow.")]
        [SerializeField, Min(0f)] private float m_vehicleMaximumPenetration = 1.5f;
        [SerializeField, Range(0f, 1f)] private float m_vehicleMinimumUpNormal = .5f;
        [SerializeField, Min(.1f)] private float m_vehicleDeformationRadius = 2.5f;
        [SerializeField, Min(0f)] private float m_vehicleDepression = .35f;

        private Collider[] m_vehicleQuery = new Collider[64];
        private readonly HashSet<Rigidbody> m_seenBodies = new();
        private readonly Dictionary<Rigidbody, float> m_nextVehicleBounce = new();
        private readonly Dictionary<Rigidbody, float> m_nextVehicleBounceSound = new();
        private readonly Dictionary<Rigidbody, Vector3> m_previousVehiclePositions = new();
        private readonly List<Rigidbody> m_expiredBodies = new();
        private readonly List<Load> m_vehicleLoads = new(16);
        private float m_nextVehicleCleanup;

        public float VehicleBounceHeight => m_vehicleBounceHeight;

        private void FixedUpdate()
        {
            m_vehicleLoads.Clear();
            if (m_surfaceCollider == null || !m_surfaceCollider.enabled) return;
            Bounds bounds = m_surfaceCollider.bounds;
            Vector3 half = bounds.extents + new Vector3(0f, m_vehicleContactDistance, 0f);
            var physics = gameObject.scene.GetPhysicsScene();
            int count = physics.OverlapBox(bounds.center, half, m_vehicleQuery, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            while (count == m_vehicleQuery.Length && m_vehicleQuery.Length < 256)
            {
                m_vehicleQuery = new Collider[m_vehicleQuery.Length * 2];
                count = physics.OverlapBox(bounds.center, half, m_vehicleQuery, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            }
            m_seenBodies.Clear();
            for (int i = 0; i < count; i++)
            {
                var other = m_vehicleQuery[i];
                Rigidbody body = other.attachedRigidbody;
                if (body == null || m_seenBodies.Contains(body)) continue;
                var traffic = body.GetComponent<TrafficVehicleController>();
                var truck = body.GetComponent<TruckController>();
                var board = body.GetComponent<MagneticHoverboardController>();
                var sled = body.GetComponent<SledController>();
                if (traffic == null && truck == null && board == null && sled == null) continue;
                if ((traffic != null && traffic.IsDestroyed) || (truck != null && truck.IsDestroyed) || (board != null && board.IsDestroyed)) continue;
                if (!TryVehicleContact(other.bounds, out var hit)) continue;
                m_seenBodies.Add(body);
                float observedUpSpeed = m_previousVehiclePositions.TryGetValue(body, out var previousPosition)
                    ? (body.position.y - previousPosition.y) / Time.fixedDeltaTime : body.linearVelocity.y;
                m_previousVehiclePositions[body] = body.position;
                m_vehicleLoads.Add(new Load { Point = hit.point, Radius = m_vehicleDeformationRadius, Depth = m_vehicleDepression });
                // Client replicas do not apply the impulse. Hear the replicated upward launch locally,
                // including when the local player is riding or standing on the vehicle.
                var replica = body.GetComponent<FishNet.Object.NetworkObject>();
                if (m_vehicleBounceEnabled && hit.normal.y >= m_vehicleMinimumUpNormal &&
                    replica != null && replica.GetIsNetworked() && !replica.IsServerInitialized &&
                    observedUpSpeed > Mathf.Max(1f, m_vehicleMaximumUpwardSpeed))
                    PlayVehicleBounceSound(body);
                if (!m_vehicleBounceEnabled || hit.normal.y < m_vehicleMinimumUpNormal ||
                    body.linearVelocity.y > m_vehicleMaximumUpwardSpeed ||
                    (m_nextVehicleBounce.TryGetValue(body, out float next) && Time.time < next)) continue;

                // Only the existing authoritative simulation may launch a networked vehicle.
                // Kinematic client replicas still contribute local visual loads above.
                var networkObject = replica;
                bool networked = networkObject != null && networkObject.GetIsNetworked();
                bool server = networked && networkObject.IsServerInitialized;
                if (networked && !server) continue;
                if (body.isKinematic && !(traffic != null && server && traffic.IsDistantSimulation)) continue;
                float speed = Mathf.Sqrt(2f * Mathf.Max(.01f, -Physics.gravity.y) * Mathf.Max(.1f, m_vehicleBounceHeight));
                Vector3 impulse = Vector3.up * Mathf.Max(0f, speed - body.linearVelocity.y);
                if (networked && traffic != null) traffic.ApplyBatLaunchServer(impulse, Vector3.zero);
                else if (networked && truck != null) truck.ApplyBatLaunchServer(impulse, Vector3.zero);
                else if (networked && board != null) board.ApplyBatLaunchServer(impulse, Vector3.zero);
                else body.AddForce(impulse, ForceMode.VelocityChange);
                m_nextVehicleBounce[body] = Time.time + Mathf.Max(.05f, m_vehicleBounceCooldown);
                AddVisualImpact(hit.point, speed, m_vehicleDeformationRadius);
                PlayVehicleBounceSound(body);
            }
            if (Time.time >= m_nextVehicleCleanup)
            {
                m_nextVehicleCleanup = Time.time + 5f;
                m_expiredBodies.Clear();
                foreach (var item in m_nextVehicleBounce)
                    if (item.Key == null || !item.Key.gameObject.activeInHierarchy || Time.time >= item.Value) m_expiredBodies.Add(item.Key);
                foreach (var body in m_expiredBodies) m_nextVehicleBounce.Remove(body);
                m_expiredBodies.Clear();
                foreach (var item in m_nextVehicleBounceSound)
                    if (item.Key == null || Time.time >= item.Value) m_expiredBodies.Add(item.Key);
                foreach (var body in m_expiredBodies) m_nextVehicleBounceSound.Remove(body);
                m_expiredBodies.Clear();
                foreach (var item in m_previousVehiclePositions)
                    if (item.Key == null || !m_seenBodies.Contains(item.Key)) m_expiredBodies.Add(item.Key);
                foreach (var body in m_expiredBodies) m_previousVehiclePositions.Remove(body);
            }
        }

        private void PlayVehicleBounceSound(Rigidbody body)
        {
            if (m_nextVehicleBounceSound.TryGetValue(body, out float next) && Time.time < next) return;
            m_nextVehicleBounceSound[body] = Time.time + Mathf.Max(.05f, m_vehicleBounceCooldown);
            PlayBounceSound();
        }

        private bool TryVehicleContact(Bounds bounds, out RaycastHit hit)
        {
            Vector3 bottom = bounds.center; bottom.y = bounds.min.y;
            if (TrySurfaceContact(bottom, m_vehicleContactDistance, out hit, m_vehicleMaximumPenetration)) return true;
            // Long chassis can cross the edge before its centre reaches the pillow.
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    if (TrySurfaceContact(bottom + new Vector3(x * bounds.extents.x * .8f, 0f, z * bounds.extents.z * .8f),
                        m_vehicleContactDistance, out hit, m_vehicleMaximumPenetration)) return true;
            hit = default;
            return false;
        }
    }
}
