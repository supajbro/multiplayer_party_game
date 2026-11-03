using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Vehicles
{
    public enum GetawayTruckState : byte { Waiting, Arriving, Approaching, Parked, Loading, Escaping, Departing, Destroyed, Parking }

    [RequireComponent(typeof(TruckController))]
    [DisallowMultipleComponent]
    public sealed class GetawayTruckController : NetworkBehaviour
    {
        [SerializeField] private GetawayTruckSettings m_settings;
        [SerializeField] private RocketExplosionVfx m_explosion;
        [SerializeField] private Renderer m_bodyRenderer;
        [SerializeField] private Color m_bodyColour = new Color(.35f, .08f, .12f);
        private readonly SyncVar<GetawayTruckState> m_state = new(GetawayTruckState.Waiting);
        private TruckController m_truck;
        private Rigidbody m_body;
        private RogueVehicleMotor m_motor;
        private DeliveryManager m_delivery;
        private CouchCarryController m_couch;
        private GetawayRoadRouter m_roads;
        private CouchClearancePath m_carryPlanner;
        private bool m_reachesRear;
        private readonly SyncVar<float> m_maximumHealth = new();
        private readonly GetawayRouteProgress m_routeProgress = new();
        private readonly GetawayRouteProgress m_carryProgress = new();
        private readonly HashSet<GridCoordinate> m_rejectedParkingRoads = new();
        private float m_parkingStarted;
        private bool m_repositioning;
        private Vector3[] m_carryCorners;
        private int m_carryCorner;
        private float m_nextCarryPath;
        private List<Vector3> m_route;
        private int m_waypoint;
        private GridCoordinate? m_approachRoad;
        private float m_nextDecision;
        private float m_nextRoadCheck;
        private float m_escapeStarted;
        private float m_departureStarted;
        private float m_destroyedAt;
        private bool m_departure;
        private bool m_stolen;

        public GetawayTruckState State => m_state.Value;
        public float MaximumHealth => m_maximumHealth.Value > 0f ? m_maximumHealth.Value :
            m_settings != null ? m_settings.Health : m_truck.Settings.MaxHealth;
        public bool IsAvailable => !m_departure && m_truck != null && !m_truck.IsDestroyed &&
            State != GetawayTruckState.Escaping && State != GetawayTruckState.Loading;
        public Vector3 CarryDestination => m_truck.CargoLoadingPoint.position;

        private void Awake()
        {
            m_carryPlanner = new CouchClearancePath();
            m_truck = GetComponent<TruckController>();
            m_body = GetComponent<Rigidbody>();
            m_motor = new RogueVehicleMotor(m_body);
            if (m_bodyRenderer == null) return;
            var properties = new MaterialPropertyBlock();
            m_bodyRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", m_bodyColour);
            properties.SetColor("_Color", m_bodyColour);
            m_bodyRenderer.SetPropertyBlock(properties);
        }

        internal void ConfigureBeforeSpawn(GetawayTruckSettings settings) => m_settings = settings;

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_maximumHealth.Value = m_settings.Health;
        }

        private Vector3 RearLoadingTarget()
        {
            Bounds bounds = m_couch != null ? m_couch.CargoBounds : new Bounds(Vector3.up * .45f, new Vector3(2.1f, .9f, .9f));
            Quaternion rotation = m_couch != null ? m_couch.transform.rotation : Quaternion.identity;
            Vector3 scale = m_couch != null ? m_couch.transform.lossyScale : Vector3.one;
            Vector3 half = Vector3.Scale(bounds.extents, scale);
            float length = Mathf.Abs(Vector3.Dot(rotation * Vector3.right, transform.forward) * half.x) +
                Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, transform.forward) * half.z);
            return CarryDestination - transform.forward * (length + m_settings.LoadingDistance * .8f);
        }

        internal bool TryGetCarryDestination(out Vector3 destination)
        {
            destination = State == GetawayTruckState.Parked || State == GetawayTruckState.Parking
                ? RearLoadingTarget() : transform.position;
            return IsAvailable;
        }

        private void RefreshCarryPath(CouchCarryController couch, bool force = false)
        {
            if (!force && Time.time < m_nextCarryPath) return;
            m_nextCarryPath = Time.time + m_settings.DrivingSettings.TargetRefreshInterval;
            m_carryCorners = null;
            m_reachesRear = false;
            m_carryCorner = 1;
            if (!TryGetCarryDestination(out Vector3 target)) return;
            bool rear = State == GetawayTruckState.Parked || State == GetawayTruckState.Parking;
            float radius = rear ? m_settings.LoadingDistance : m_settings.CarrierNavigationSearchRadius;
            if (!rear && !NavMesh.SamplePosition(target, out _, radius, NavMesh.AllAreas) && m_roads != null)
            {
                GeneratedRoad road = NearestInteriorRoad(target);
                if (road != null) target = road.transform.position;
            }
            m_carryPlanner ??= new CouchClearancePath();
            if (!m_carryPlanner.TryBuild(couch, target, m_settings, radius, !rear, out m_carryCorners, out bool complete)) return;
            m_reachesRear = rear && complete && RearPoseInRange(couch, m_carryCorners);
        }

        private bool RearPoseInRange(CouchCarryController couch, Vector3[] path)
        {
            if (path == null || path.Length == 0) return false;
            Vector3 root = path[path.Length - 1] + Vector3.up * (couch.transform.position.y - path[0].y);
            Vector3 scale = couch.transform.lossyScale;
            Vector3 local = Quaternion.Inverse(couch.transform.rotation) * (CarryDestination - root);
            local = new Vector3(local.x / scale.x, local.y / scale.y, local.z / scale.z);
            Bounds bounds = couch.CargoBounds;
            if (bounds.size.sqrMagnitude < .01f) bounds = new Bounds(Vector3.up * .45f, new Vector3(2.1f, .9f, .9f));
            Vector3 nearest = root + couch.transform.rotation * Vector3.Scale(bounds.ClosestPoint(local), scale);
            return Vector3.Distance(nearest, CarryDestination) <= m_settings.LoadingDistance &&
                transform.InverseTransformPoint(root).z < transform.InverseTransformPoint(CarryDestination).z &&
                m_carryPlanner.LoadingEntranceClear(couch, root, CarryDestination);
        }

        internal Vector3 GetCarryMovementIntent(CouchCarryController couch)
        {
            if (couch == null || !IsAvailable) return Vector3.zero;
            RefreshCarryPath(couch);
            if (m_carryCorners == null || m_carryCorners.Length < 2) return Vector3.zero;
            Vector3 position = couch.transform.position;
            while (m_carryCorner < m_carryCorners.Length - 1 &&
                Vector3.ProjectOnPlane(m_carryCorners[m_carryCorner] - position, Vector3.up).sqrMagnitude < .16f)
                m_carryCorner++;
            Vector3 offset = Vector3.ProjectOnPlane(m_carryCorners[m_carryCorner] - position, Vector3.up);
            return offset.sqrMagnitude > .04f ? offset.normalized : Vector3.zero;
        }
        internal void InitialiseServer(DeliveryManager delivery, CouchCarryController couch,
            GetawayRoadRouter roads, bool replacement)
        {
            m_delivery = delivery;
            m_couch = couch;
            m_roads = roads.ForVehicle(transform, m_settings.DrivingClearance, m_settings.DrivingClearanceHeight);
            m_truck.SetGetawayHealthServer(m_settings.Health);
            m_routeProgress.Reset(Time.time);
            m_carryProgress.Reset(Time.time);
            if (!replacement) return;
            GeneratedRoad entry = NearestInteriorRoad(transform.position);
            if (entry != null && m_roads.TryRoute(transform.position, entry.Coordinate, out var route))
            { SetRoute(route); m_state.Value = GetawayTruckState.Arriving; }
        }

        private GeneratedRoad NearestInteriorRoad(Vector3 target, bool parkingCandidate = false)
        {
            GeneratedRoad best = null;
            float distance = float.PositiveInfinity;
            foreach (GeneratedRoad road in m_roads.Roads)
            {
                if (m_delivery.GeneratorIsTunnelApproach(road.Coordinate) ||
                    (parkingCandidate && m_rejectedParkingRoads.Contains(road.Coordinate))) continue;
                if (parkingCandidate && m_repositioning &&
                    Vector3.Distance(road.transform.position, transform.position) < m_settings.ParkingRepositionDistance) continue;
                float candidate = (road.transform.position - target).sqrMagnitude;
                if (candidate < distance) { best = road; distance = candidate; }
            }
            return best;
        }

        internal void DepartServer()
        {
            if (m_departure) return;
            m_departure = true;
            m_couch = null;
            m_departureStarted = Time.time;
            if (m_truck.IsDestroyed) return;
            m_state.Value = GetawayTruckState.Departing;
            m_routeProgress.Reset(Time.time);
            m_truck.SetGetawayDoorsServer(false);
            ChooseExit(false);
        }

        internal void NotifyCouchStolenServer() { m_stolen = true; m_nextDecision = 0f; }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (State == GetawayTruckState.Destroyed && m_bodyRenderer != null) m_bodyRenderer.SetPropertyBlock(null);
        }

        public override void OnStopServer()
        {
            m_delivery?.NotifyGetawayDestroyedServer(this);
            base.OnStopServer();
        }

        [Server]
        public void NotifyDestroyedServer()
        {
            if (State == GetawayTruckState.Destroyed) return;
            m_state.Value = GetawayTruckState.Destroyed;
            m_destroyedAt = Time.time;
            m_route = null;
            m_body.isKinematic = false;
            m_body.linearVelocity = Vector3.zero;
            m_body.angularVelocity = Vector3.zero;
            ExplodeObserversRpc();
            m_delivery?.NotifyGetawayDestroyedServer(this);
        }

        [ObserversRpc(RunLocally = true)]
        private void ExplodeObserversRpc()
        {
            if (m_bodyRenderer != null) m_bodyRenderer.SetPropertyBlock(null);
            if (NetworkManager.IsClientStarted && m_explosion != null)
                RocketExplosionVfx.Play(m_explosion, transform.position + Vector3.up, transform.position, 5f);
        }

        private void Update()
        {
            if (!IsServerInitialized || m_roads == null) return;
            if (m_truck.IsDestroyed)
            {
                if (Time.time >= m_destroyedAt + m_settings.WreckLifetime) Despawn(NetworkObject);
                return;
            }
            if (m_departure)
            {
                if (Time.time >= m_departureStarted + m_settings.DepartureTimeout) { Despawn(NetworkObject); return; }
                if (Time.time >= m_nextDecision && m_route == null)
                { m_nextDecision = Time.time + m_settings.DecisionInterval; ChooseExit(false); }
                return;
            }
            if (m_delivery == null || m_couch == null || !m_couch.IsSpawned || m_delivery.ActiveCouch != m_couch.NetworkObject)
            { DepartServer(); return; }
            if (State == GetawayTruckState.Loading)
            {
                if (!m_couch.IsLoaded || m_couch.LoadedTruckObject != NetworkObject)
                { m_state.Value = GetawayTruckState.Waiting; return; }
                if (!m_couch.IsCargoTransitioning)
                {
                    m_escapeStarted = Time.time;
                    m_state.Value = GetawayTruckState.Escaping;
                    m_routeProgress.Reset(Time.time);
                    ChooseExit(true);
                }
                return;
            }
            if (State == GetawayTruckState.Escaping)
            {
                if (!m_couch.IsLoaded || m_couch.LoadedTruckObject != NetworkObject)
                { m_route = null; m_state.Value = GetawayTruckState.Waiting; }
                else if (m_route == null && Time.time >= m_nextDecision)
                { m_nextDecision = Time.time + m_settings.DecisionInterval; ChooseExit(true); }
                return;
            }
            if (Time.time < m_nextDecision) return;
            m_nextDecision = Time.time + m_settings.DrivingSettings.TargetRefreshInterval;
            if (State == GetawayTruckState.Arriving) return;
            if (State == GetawayTruckState.Parking)
            {
                if (Time.time - m_parkingStarted >= m_settings.ParkedRecoveryTimeout) { RejectParking(); return; }
                if (Time.time - m_parkingStarted < m_settings.ParkingSettleTime || m_truck.SpeedKph > 1f) return;
                m_body.isKinematic = true;
                RefreshCarryPath(m_couch, true);
                if (m_reachesRear && ParkingSpaceClear())
                {
                    m_state.Value = GetawayTruckState.Parked;
                    m_carryProgress.Reset(Time.time);
                    m_truck.SetGetawayDoorsServer(true);
                }
                else RejectParking();
                return;
            }
            bool thiefCarrying = m_couch.ThiefCarrierCountServer() > 0;
            if (thiefCarrying) m_stolen = true;
            if (!thiefCarrying && m_couch.HasHumanCarrierServer()) m_stolen = false;
            if (!m_stolen)
            {
                m_route = null;
                m_approachRoad = null;
                m_repositioning = false;
                m_rejectedParkingRoads.Clear();
                m_state.Value = GetawayTruckState.Waiting;
                return;
            }
            if (State == GetawayTruckState.Parked)
            {
                Vector3 local = transform.InverseTransformPoint(m_couch.transform.position);
                Vector3 entrance = transform.InverseTransformPoint(CarryDestination);
                if (!m_couch.HasHumanCarrierServer() && m_couch.IsPickupAvailable &&
                    m_truck.IsCouchInLoadingRange(m_couch, m_settings.LoadingDistance) &&
                    local.z <= entrance.z + .5f && Mathf.Abs(local.x - entrance.x) <= m_settings.LoadingDistance &&
                    m_truck.RearDoorOpenProgress >= .95f && m_truck.SpeedKph < 1f &&
                    m_carryPlanner.LoadingEntranceClear(m_couch, m_couch.transform.position, CarryDestination) &&
                    m_truck.LoadGetawayCouchServer(m_couch, m_settings.LoadingDistance))
                { m_state.Value = GetawayTruckState.Loading; return; }
                RefreshCarryPath(m_couch);
                if (!m_reachesRear) { RejectParking(); return; }
                if (thiefCarrying && !m_couch.HasHumanCarrierServer())
                {
                    m_carryProgress.Observe(RemainingLength(m_couch.transform.position, m_carryCorners, m_carryCorner),
                        Time.time, m_settings.RouteProgressDistance);
                    if (m_carryProgress.TimedOut(Time.time, m_settings.ParkedRecoveryTimeout)) { RejectParking(); return; }
                }
                else m_carryProgress.Reset(Time.time);
                return;
            }
            RefreshApproach();
        }

        private void RefreshApproach()
        {
            // Reuse connected roads to get near the live couch target. The motor is the
            // rogue car motor, with truck tuning; it never rotates the body in place.
            if (m_repositioning && m_approachRoad.HasValue && m_route != null) return;
            GeneratedRoad road = NearestInteriorRoad(m_couch.transform.position, true);
            if (road == null) return;
            if (State == GetawayTruckState.Approaching && m_approachRoad == road.Coordinate && m_route != null) return;
            m_roads.RefreshClearance();
            if (!m_roads.TryRoute(transform.position, road.Coordinate, out var route)) return;
            m_approachRoad = road.Coordinate;
            if (State == GetawayTruckState.Waiting) m_routeProgress.Reset(Time.time);
            SetRoute(route);
            m_truck.SetGetawayDoorsServer(false);
            m_state.Value = GetawayTruckState.Approaching;
        }

        private void Park()
        {
            m_route = null;
            m_state.Value = GetawayTruckState.Parking;
            m_parkingStarted = Time.time;
            m_nextCarryPath = 0f;
        }

        private bool ParkingSpaceClear()
        {
            foreach (Collider hit in Physics.OverlapBox(transform.position + Vector3.up *
                (m_settings.ParkingClearance.y + .25f), m_settings.ParkingClearance,
                transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(m_couch.transform) ||
                    TruckController.IsGroundSurface(hit) || hit.GetComponentInParent<CouchGuys.Player.PlayerHealth>() != null ||
                    hit.GetComponentInParent<CouchGuys.Gameplay.Enemies.ThiefHealth>() != null) continue;
                return false;
            }
            return true;
        }

        private void RejectParking()
        {
            GeneratedRoad road = m_roads.Nearest(transform.position);
            if (road != null) m_rejectedParkingRoads.Add(road.Coordinate);
            m_repositioning = true;
            m_approachRoad = null;
            m_route = null;
            m_state.Value = GetawayTruckState.Approaching;
            m_routeProgress.Reset(Time.time);
            m_truck.SetGetawayDoorsServer(false);
            RefreshApproach();
        }

        private void ChooseExit(bool escaping)
        {
            m_roads.RefreshClearance();
            List<Vector3> best = null;
            float bestLength = float.PositiveInfinity;
            List<Vector3> longest = null;
            float longestLength = -1f;
            List<Vector3> fallback = null;
            float fallbackLength = float.PositiveInfinity;
            foreach (TrafficRoadPortal portal in m_roads.Portals)
            {
                if (!m_roads.TryForwardRoute(transform.position, transform.forward, portal.Coordinate, out var route))
                {
                    if (m_roads.TryRoute(transform.position, portal.Coordinate, out var reverseRoute))
                    {
                        float distance = GetawayRoadRouter.Length(transform.position, reverseRoute);
                        if (distance < fallbackLength) { fallback = reverseRoute; fallbackLength = distance; }
                    }
                    continue;
                }
                float length = GetawayRoadRouter.Length(transform.position, route);
                if (length > longestLength) { longest = route; longestLength = length; }
                if (escaping && length < m_settings.MinimumEscapeRouteLength) continue;
                if (length < bestLength) { bestLength = length; best = route; }
            }
            if (escaping && best == null)
            {
                foreach (GeneratedRoad road in m_roads.Roads)
                {
                    if (m_delivery.GeneratorIsTunnelApproach(road.Coordinate) ||
                        !m_roads.TryForwardRoute(transform.position, transform.forward, road.Coordinate, out var outbound)) continue;
                    foreach (TrafficRoadPortal portal in m_roads.Portals)
                    {
                        Vector3 heading = outbound.Count > 1 ? outbound[outbound.Count - 1] - outbound[outbound.Count - 2] : transform.forward;
                        if (!m_roads.TryForwardRoute(road.transform.position, heading, portal.Coordinate, out var inbound)) continue;
                        float length = GetawayRoadRouter.Length(transform.position, outbound) + GetawayRoadRouter.Length(road.transform.position, inbound);
                        if (length < m_settings.MinimumEscapeRouteLength || length >= bestLength) continue;
                        best = new List<Vector3>(outbound);
                        for (int i = 1; i < inbound.Count; i++) best.Add(inbound[i]);
                        bestLength = length;
                    }
                }
            }
            SetRoute(best ?? longest ?? fallback);
        }

        private void SetRoute(List<Vector3> route)
        {
            m_route = route;
            m_waypoint = 0;
            m_routeProgress.Replan();
        }

        private static float RemainingLength(Vector3 position, IReadOnlyList<Vector3> route, int index)
        {
            if (route == null || index >= route.Count) return float.PositiveInfinity;
            float length = Vector3.Distance(position, route[index]);
            for (int i = index + 1; i < route.Count; i++) length += Vector3.Distance(route[i - 1], route[i]);
            return length;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || m_roads == null || m_settings.DrivingSettings == null) return;
            if (m_truck.IsDestroyed) { m_motor.Stop(m_settings.DrivingSettings); return; }
            bool driving = State == GetawayTruckState.Arriving || State == GetawayTruckState.Approaching ||
                State == GetawayTruckState.Escaping || State == GetawayTruckState.Departing;
            if (!driving)
            { m_motor.Stop(m_settings.DrivingSettings, true); m_routeProgress.Reset(Time.time); return; }
            if (m_route == null || m_route.Count == 0)
            {
                m_motor.Stop(m_settings.DrivingSettings);
                if (m_routeProgress.TimedOut(Time.time, m_settings.StuckRecoveryTimeout)) m_truck.DisableGetawayServer();
                return;
            }
            if (State == GetawayTruckState.Approaching && !m_repositioning &&
                Vector3.ProjectOnPlane(m_couch.transform.position - m_body.position, Vector3.up).magnitude <= m_settings.ParkingDistance)
            { Park(); m_motor.Stop(m_settings.DrivingSettings); return; }
            float radius = m_waypoint == m_route.Count - 1 ? m_settings.WaypointRadius :
                Mathf.Max(m_settings.WaypointRadius, m_settings.DrivingSettings.MinTurningRadius * .65f);
            while (GetawayRouteFollower.ReachedWaypoint(m_route, m_waypoint, m_body.position, radius, m_settings.WaypointPassDistance))
            {
                if (++m_waypoint < m_route.Count) continue;
                if (State == GetawayTruckState.Escaping && Time.time < m_escapeStarted + m_settings.MinimumChaseTime)
                { m_waypoint--; m_motor.Stop(m_settings.DrivingSettings); return; }
                m_route = null;
                m_motor.Stop(m_settings.DrivingSettings);
                if (State == GetawayTruckState.Departing) { Despawn(NetworkObject); return; }
                if (State == GetawayTruckState.Escaping)
                {
                    if (m_couch != null && m_couch.IsLoaded && m_couch.LoadedTruckObject == NetworkObject)
                        m_delivery.GetawayEscapedServer(this, m_couch);
                    return;
                }
                if (State == GetawayTruckState.Arriving) m_state.Value = GetawayTruckState.Waiting;
                else Park();
                return;
            }
            float speed = State == GetawayTruckState.Escaping || State == GetawayTruckState.Departing
                ? m_settings.EscapeSpeed : m_settings.ApproachSpeed;
            if (Time.time >= m_nextRoadCheck)
            {
                m_nextRoadCheck = Time.time + m_settings.DecisionInterval;
                // Check from the next road centre as well as the actual chassis position:
                // a closure can be farther along this link than the steering look-ahead.
                bool clear = m_roads.SegmentClear(m_body.position, m_route[m_waypoint]);
                if (clear && m_waypoint + 1 < m_route.Count)
                    clear = m_roads.SegmentClear(m_route[m_waypoint], m_route[m_waypoint + 1]);
                if (!clear)
                {
                    m_motor.Stop(m_settings.DrivingSettings, true);
                    GeneratedRoad arrival = State == GetawayTruckState.Arriving ? m_roads.Nearest(m_route[m_route.Count - 1]) : null;
                    m_route = null;
                    if (State == GetawayTruckState.Escaping || State == GetawayTruckState.Departing)
                        ChooseExit(State == GetawayTruckState.Escaping);
                    else if (State == GetawayTruckState.Approaching) RefreshApproach();
                    else if (arrival != null)
                    {
                        m_roads.RefreshClearance();
                        if (m_roads.TryRoute(m_body.position, arrival.Coordinate, out var arrivalRoute)) SetRoute(arrivalRoute);
                    }
                    return;
                }
            }
            Vector3 target = GetawayRouteFollower.SteeringTarget(m_route, m_waypoint, m_body.position, m_settings.LookAheadDistance);
            float remaining = Vector3.ProjectOnPlane(m_route[m_waypoint] - m_body.position, Vector3.up).magnitude;
            if (m_waypoint + 1 < m_route.Count)
            {
                Vector3 incoming = m_waypoint > 0 ? m_route[m_waypoint] - m_route[m_waypoint - 1] : transform.forward;
                float bend = Vector3.Angle(incoming, m_route[m_waypoint + 1] - m_route[m_waypoint]);
                float cornerSpeed = Mathf.Lerp(speed, m_settings.CornerSpeed, Mathf.InverseLerp(5f, 60f, bend));
                speed = Mathf.Min(speed, Mathf.Sqrt(cornerSpeed * cornerSpeed + 2f * m_settings.DrivingSettings.Acceleration * remaining));
            }
            float stopping = m_waypoint == m_route.Count - 1 ? m_settings.WaypointRadius * .5f : 0f;
            bool escaping = State == GetawayTruckState.Escaping || State == GetawayTruckState.Departing;
            m_motor.Drive(m_settings.DrivingSettings, target, speed, stopping, Time.fixedDeltaTime, escaping);
            m_routeProgress.Observe(RemainingLength(m_body.position, m_route, m_waypoint), Time.time, m_settings.RouteProgressDistance);
            if (m_routeProgress.TimedOut(Time.time, m_settings.StuckRecoveryTimeout)) m_truck.DisableGetawayServer();
        }
    }
}
