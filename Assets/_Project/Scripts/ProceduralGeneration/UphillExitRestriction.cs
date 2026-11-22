using System.Collections.Generic;
using CouchGuys.Gameplay.Sled;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    [DefaultExecutionOrder(10000), DisallowMultipleComponent]
    public sealed class UphillExitRestriction : MonoBehaviour
    {
        [SerializeField] private NetworkObject m_allowedVehiclePrefab;
        [SerializeField, Min(.1f)] private float m_playerSlideSpeed = 2.5f;
        [SerializeField, Min(.1f)] private float m_vehicleSlideSpeed = 3f;
        [Tooltip("Maximum initial uphill speed before traction is lost.")]
        [SerializeField, Min(.1f)] private float m_initialClimbSpeed = 6f;
        [Tooltip("How quickly uphill movement turns into a downhill slide, in units per second squared.")]
        [SerializeField, Min(.1f)] private float m_slideAcceleration = 4f;
        [Tooltip("Distance into the hill at which all uphill traction is lost. Limited to the first quarter of the route.")]
        [SerializeField, Min(.1f)] private float m_tractionLossDistance = 12f;
        [SerializeField, Min(1f)] private float m_summitOverrun = 40f;
        [SerializeField, HideInInspector] private NeighbourhoodGenerator m_generator;
        [SerializeField, HideInInspector] private Vector3 m_start;
        [SerializeField, HideInInspector] private Vector3 m_uphill;
        [SerializeField, HideInInspector] private float m_length;
        [SerializeField, HideInInspector] private float m_halfWidth;
        [SerializeField, HideInInspector] private float m_roadHalfWidth = 4f;
        [SerializeField, HideInInspector] private bool m_configured;

        private static readonly List<UphillExitRestriction> s_active = new();
        private readonly Dictionary<Rigidbody, Collider[]> m_bodyColliders = new();
        private readonly Dictionary<Rigidbody, float> m_bodySlideLimits = new();
        private bool Ready => m_configured && (m_generator == null || m_generator.IsGenerationReady);

        private void OnEnable()
        {
            if (!s_active.Contains(this)) s_active.Add(this);
        }
        private void OnDisable() => s_active.Remove(this);

        public void Initialise(NeighbourhoodGenerator generator, Vector3 start, Vector3 summit, float halfWidth,
            float roadHalfWidth = 4f)
        {
            m_generator = generator;
            m_start = start;
            Vector3 direction = Vector3.ProjectOnPlane(summit - start, Vector3.up);
            m_length = direction.magnitude;
            m_uphill = direction.normalized;
            m_halfWidth = Mathf.Max(1f, halfWidth);
            m_roadHalfWidth = Mathf.Max(1f, roadHalfWidth);
            m_configured = m_length > .1f;
        }

        public static void RemoveGenerator(NeighbourhoodGenerator generator)
        {
            for (int i = s_active.Count - 1; i >= 0; i--)
                if (s_active[i] != null && s_active[i].m_generator == generator)
                {
                    s_active[i].m_configured = false;
                    s_active.RemoveAt(i);
                }
        }

        public bool AllowsVehicle(TruckController truck) => truck != null &&
            m_allowedVehiclePrefab != null && truck.NetworkObject != null &&
            m_allowedVehiclePrefab.AssetPathHash != 0 &&
            truck.NetworkObject.AssetPathHash == m_allowedVehiclePrefab.AssetPathHash;

        public static bool AllowsExitVehicle(TruckController truck)
        {
            var configuration = UphillExitRestrictionConfiguration.Load();
            return configuration != null && configuration.Prefab != null && configuration.Prefab.AllowsVehicle(truck);
        }

        private Vector3 LocalPlanar(Vector3 position)
        {
            Vector3 offset = position - m_start;
            return new Vector3(Vector3.Dot(offset, Vector3.Cross(Vector3.up, m_uphill)), 0f,
                Vector3.Dot(offset, m_uphill));
        }

        public bool IntersectsRoute(Vector3 position, Vector3 destination, float radius)
            => IntersectsRoute(position, destination, new Vector2(radius, radius));

        private bool IntersectsRoute(Vector3 position, Vector3 destination, Vector2 padding)
        {
            if (!Ready) return false;
            var bounds = new Bounds(new Vector3(0f, 0f, (m_length + m_summitOverrun) * .5f),
                new Vector3((m_halfWidth + padding.x) * 2f, 2f, m_length + m_summitOverrun + padding.y * 2f));
            Vector3 from = LocalPlanar(position);
            Vector3 to = LocalPlanar(destination);
            if (bounds.Contains(from) || bounds.Contains(to)) return true;
            Vector3 delta = to - from;
            return delta.sqrMagnitude > .000001f && bounds.IntersectRay(new Ray(from, delta), out float distance) &&
                distance <= delta.magnitude;
        }

        private float AdvanceSlideLimit(float limit, float slideSpeed, float progress, float deltaTime)
        {
            // Bound initial momentum so jumps, grapples and fast vehicles cannot cross
            // the whole hill before losing traction. Never change the actor's position.
            float distance = Mathf.Min(m_tractionLossDistance, m_length * .25f);
            float traction = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / distance));
            float initialLimit = Mathf.Lerp(m_initialClimbSpeed, -slideSpeed, traction);
            // Also bound a single swept step, including unusually long frames.
            if (deltaTime > 0f)
                initialLimit = Mathf.Min(initialLimit, Mathf.Max(0f, distance - progress) / deltaTime);
            return Mathf.MoveTowards(Mathf.Min(limit, initialLimit), -slideSpeed,
                m_slideAcceleration * Mathf.Max(0f, deltaTime));
        }

        public bool ConstrainPlayerMotion(Vector3 position, Vector3 displacement, float radius, float deltaTime,
            ref float slideLimit, out Vector3 destination)
        {
            destination = position + displacement;
            if (!IntersectsRoute(position, destination, radius)) return false;
            slideLimit = AdvanceSlideLimit(slideLimit, m_playerSlideSpeed, LocalPlanar(position).z, deltaTime);
            float advance = Vector3.Dot(displacement, m_uphill);
            displacement -= m_uphill * Mathf.Max(0f, advance - slideLimit * Mathf.Max(0f, deltaTime));
            // Airborne movement obeys the same traction rule; jumping cannot reset it.
            displacement.y = Mathf.Min(0f, displacement.y);
            destination = position + displacement;
            return true;
        }

        public static bool ConstrainPlayer(Transform player, Vector3 displacement, float radius, float deltaTime,
            ref float slideLimit, out Vector3 destination)
        {
            destination = player.position + displacement;
            foreach (var restriction in s_active)
                if (restriction != null && restriction.ConstrainPlayerMotion(player.position, displacement, radius,
                        deltaTime, ref slideLimit, out destination)) return true;
            slideLimit = float.PositiveInfinity;
            destination = player.position + displacement;
            return false;
        }

        private Vector2 BodyPadding(Rigidbody body)
        {
            if (!m_bodyColliders.TryGetValue(body, out var colliders))
            {
                colliders = body.GetComponentsInChildren<Collider>();
                m_bodyColliders[body] = colliders;
            }
            var bounds = new Bounds(body.position, Vector3.zero);
            foreach (var collider in colliders)
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger)
                    bounds.Encapsulate(collider.bounds);
            Vector3 minimum = bounds.min - body.position;
            Vector3 maximum = bounds.max - body.position;
            Vector3 extent = new Vector3(Mathf.Max(Mathf.Abs(minimum.x), Mathf.Abs(maximum.x)), 0f,
                Mathf.Max(Mathf.Abs(minimum.z), Mathf.Abs(maximum.z)));
            Vector3 across = Vector3.Cross(Vector3.up, m_uphill);
            return new Vector2(Mathf.Max(.5f, Mathf.Abs(across.x) * extent.x + Mathf.Abs(across.z) * extent.z),
                Mathf.Max(.5f, Mathf.Abs(m_uphill.x) * extent.x + Mathf.Abs(m_uphill.z) * extent.z));
        }

        public bool ConstrainBody(Rigidbody body, float deltaTime)
        {
            if (body.TryGetComponent(out TruckController truck) && AllowsVehicle(truck))
            {
                if (deltaTime > 0f && truck.IsServerInitialized && !truck.IsDestroyed && !truck.IsBatLaunched)
                    FollowExitRoad(truck, body, deltaTime);
                return false;
            }
            Vector2 padding = BodyPadding(body);
            Vector3 velocity = body.isKinematic ? Vector3.zero : body.linearVelocity;
            if (!IntersectsRoute(body.position, body.position + velocity * deltaTime, padding))
            {
                m_bodySlideLimits.Remove(body);
                return false;
            }
            if (!m_bodySlideLimits.TryGetValue(body, out float limit)) limit = m_initialClimbSpeed;
            limit = AdvanceSlideLimit(limit, m_vehicleSlideSpeed, LocalPlanar(body.position).z, deltaTime);
            m_bodySlideLimits[body] = limit;
            if (body.isKinematic)
            {
                if (deltaTime > 0f)
                    body.MovePosition(body.position + m_uphill * (Mathf.Min(0f, limit) * deltaTime));
            }
            else
            {
                velocity -= m_uphill * Mathf.Max(0f, Vector3.Dot(velocity, m_uphill) - limit);
                velocity.y = Mathf.Min(0f, velocity.y);
                body.linearVelocity = velocity;
            }
            return true;
        }

        private void FollowExitRoad(TruckController truck, Rigidbody body, float deltaTime)
        {
            if (body.isKinematic || !truck.IsHoverTruck ||
                !IntersectsRoute(body.position, body.position, 0f) ||
                Mathf.Abs(LocalPlanar(body.position).x) > m_roadHalfWidth + 1f) return;
            if (!TryRoadHeight(truck, body.position, out float groundHeight)) return;
            float gap = body.position.y - groundHeight;
            if (gap > truck.HoverSettings.ProbeRange || gap < -1f) return;
            Vector3 next = body.position + Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up) * deltaTime;
            if (!TryRoadHeight(truck, next, out float nextHeight)) return;
            Vector3 velocity = body.linearVelocity;
            float groundVelocity = (nextHeight - groundHeight) / deltaTime;
            velocity.y = groundVelocity + (truck.HoverSettings.HoverHeight - gap) * truck.HoverSettings.Damping;
            body.linearVelocity = velocity;
        }

        private bool TryRoadHeight(TruckController truck, Vector3 position, out float height)
        {
            if (m_generator != null)
            {
                height = m_generator.SampleTerrainHeight(position);
                return true;
            }
            height = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 200f, Vector3.down, 400f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(truck.transform) || hit.normal.y < .45f) continue;
                var occupant = hit.collider.GetComponentInParent<PlayerTruckInteractor>();
                if (occupant != null && occupant.CurrentTruck == truck) continue;
                height = Mathf.Max(height, hit.point.y);
            }
            return !float.IsNegativeInfinity(height);
        }

        private void FixedUpdate()
        {
            if (!Ready || !InstanceFinder.IsServerStarted) return;
            foreach (var networkObject in InstanceFinder.ServerManager.Objects.Spawned.Values)
            {
                if (networkObject == null || !networkObject.IsSpawned) continue;
                if (TryGetVehicleBody(networkObject, out Rigidbody body)) ConstrainBody(body, Time.fixedDeltaTime);
            }
        }

        private static bool TryGetVehicleBody(NetworkObject networkObject, out Rigidbody body)
        {
            body = networkObject.GetComponent<Rigidbody>();
            return body != null && (networkObject.TryGetComponent<TruckController>(out _) ||
                networkObject.TryGetComponent<MagneticHoverboardController>(out _) ||
                networkObject.TryGetComponent<SledController>(out _) || networkObject.TryGetComponent<TrafficVehicleController>(out _));
        }

        private void LateUpdate()
        {
            if (!Ready || !InstanceFinder.IsServerStarted) return;
            foreach (var networkObject in InstanceFinder.ServerManager.Objects.Spawned.Values)
            {
                if (networkObject == null || !networkObject.IsSpawned) continue;
                if (TryGetVehicleBody(networkObject, out Rigidbody body)) ConstrainBody(body, 0f);
            }
        }
    }
}
