using CouchGuys.ProceduralGeneration;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    public struct DispatcherRoamingPose
    {
        public bool Valid;
        public int Seed;
        public int Region;
        public uint Revision;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Snap;
    }

    /// <summary>Server-only waypoint decisions; clients interpolate the existing world object's pose SyncVar.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(DeliveryNPC))]
    public sealed class DispatcherRoaming : MonoBehaviour
    {
        public enum RoamingState : byte { Idle, Roaming, Returning }
        [Header("Timing (seconds)")]
        [SerializeField] private Vector2 m_roamingInterval = new(5f, 10f);
        [SerializeField] private Vector2 m_roamingDuration = new(45f, 75f);
        [SerializeField] private Vector2 m_pauseDuration = new(0.5f, 1.5f);
        [Header("Rolling")]
        [SerializeField, Min(.1f)] private float m_movementSpeed = .8f;
        [SerializeField, Min(.1f)] private float m_acceleration = 1.5f;
        [SerializeField, Min(1f)] private float m_turnSpeed = 150f;
        [SerializeField, Min(.1f)] private float m_clearance = .65f;
        [SerializeField] private LayerMask m_obstacleMask = ~0;
        [SerializeField, Min(1f)] private float m_blockedReturnTimeout = 5f;
        [SerializeField, Min(1f), Tooltip("Retry delay if a scheduled outing has no currently clear destination.")]
        private float m_blockedDestinationRetry = 5f;
        [SerializeField] private RoamingState m_state;

        private DeliveryNPC m_npc;
        private DeliveryManager m_manager;
        private NeighbourhoodSeedSynchroniser m_network;
        private NeighbourhoodGenerator m_generator;
        private DispatcherRoamingArea m_area;
        private Transform m_home;
        private Vector3[] m_trail = new Vector3[4];
        private Vector3[] m_nodes, m_route;
        private int[] m_parents, m_queue;
        private int m_routeCount, m_routeIndex;
        private readonly RaycastHit[] m_hits = new RaycastHit[16];
        private readonly Collider[] m_overlaps = new Collider[16];
        private Vector3 m_target, m_sentPosition, m_clientFrom, m_clientTo;
        private Quaternion m_sentRotation, m_clientFromRotation, m_clientToRotation;
        private float m_waitUntil, m_roamUntil, m_pauseUntil, m_speed, m_blockedAt = -1f;
        private float m_nextPublish, m_clientReceivedAt, m_nextRecovery;
        private int m_trailCount, m_visits, m_lastWaypoint = -1;
        private bool m_started, m_hasTarget, m_interrupted, m_sent, m_snap, m_received;
        private uint m_revision;

        public RoamingState State => m_state;

        private void OnEnable()
        {
            // Runtime-generated NPCs also need their cached links restored after a Play-mode script reload.
            var manager = GetComponent<DeliveryNPC>().Manager;
            if (manager != null) Initialise(manager);
        }

        private void OnValidate()
        {
            m_movementSpeed = Mathf.Max(.1f, m_movementSpeed);
            m_acceleration = Mathf.Max(.1f, m_acceleration);
            m_turnSpeed = Mathf.Max(1f, m_turnSpeed);
            m_clearance = Mathf.Max(.65f, m_clearance);
            m_blockedReturnTimeout = Mathf.Max(1f, m_blockedReturnTimeout);
            m_blockedDestinationRetry = Mathf.Max(1f, m_blockedDestinationRetry);
            m_roamingInterval = OrderedRange(m_roamingInterval);
            m_roamingDuration = OrderedRange(m_roamingDuration);
            m_pauseDuration = OrderedRange(m_pauseDuration);
        }

        private static Vector2 OrderedRange(Vector2 value) => new(Mathf.Max(0f, value.x), Mathf.Max(Mathf.Max(0f, value.x), value.y));

        public void Initialise(DeliveryManager manager)
        {
            m_npc = GetComponent<DeliveryNPC>();
            m_manager = manager;
            m_network = manager != null ? manager.GetComponent<NeighbourhoodSeedSynchroniser>() : null;
            m_generator = manager != null ? manager.GetComponent<NeighbourhoodGenerator>() : null;
            var area = GetComponentInParent<StartingArea>();
            m_home = area != null ? area.DeliveryNpcSpawnPoint : null;
            m_area = GetComponentInParent<DispatcherRoamingArea>();
            // A loop-erased path needs at most home + one entry per authored waypoint.
            int capacity = Mathf.Max(4, (m_area != null ? m_area.WaypointCount : 0) + 1);
            if (m_trail.Length != capacity) m_trail = new Vector3[capacity];
            int nodes = capacity + 1;
            if (m_nodes == null || m_nodes.Length != nodes)
            {
                m_nodes = new Vector3[nodes]; m_route = new Vector3[nodes];
                m_parents = new int[nodes]; m_queue = new int[nodes];
            }
            m_started = m_sent = m_received = false;
            m_interrupted = m_hasTarget = false; m_speed = 0f; m_trailCount = 0;
            m_state = RoamingState.Idle;
        }

        private void Update()
        {
            if (m_network == null || m_home == null || m_generator == null || !m_generator.IsGenerationReady) return;
            if (m_network.IsServerInitialized)
            {
                if (!m_started)
                {
                    m_started = true;
                    if ((transform.position - m_home.position).sqrMagnitude > .0001f)
                    {
                        // A reload mid-outing must return safely rather than teleport unconditionally.
                        m_trail[0] = m_home.position; m_trailCount = 1; BeginReturn();
                    }
                    else FinishHome();
                }
                if (!m_npc.TickAttackReactionServer(Time.deltaTime)) TickServer(Time.deltaTime);
                Publish();
            }
            else if (m_network.IsClientInitialized) TickClient();
        }

        private static float RandomRange(Vector2 range) => Random.Range(Mathf.Max(0f, range.x), Mathf.Max(range.x, range.y));

        private void TickServer(float dt)
        {
            if (m_manager.IsDispatcherInDialogue(m_npc))
            {
                // A conversation at home must not restart the long idle countdown.
                // Only an interrupted outing requires a return and a fresh cycle.
                if (m_state != RoamingState.Idle) m_interrupted = true;
                m_speed = 0f; m_blockedAt = -1f;
                if (m_manager.TryGetDispatcherLookTarget(m_npc, out var speaker))
                {
                    Vector3 direction = speaker.position - transform.position;
                    direction.y = 0f;
                    if (direction.sqrMagnitude > .0001f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(direction), m_turnSpeed * dt);
                }
                return;
            }
            if (m_interrupted)
            {
                m_interrupted = false;
                if (m_state == RoamingState.Idle) FinishHome();
                else BeginReturn();
            }
            if (m_state == RoamingState.Idle)
            {
                if (Time.time < m_waitUntil || m_area == null || m_area.WaypointCount == 0) return;
                m_trail[0] = m_home.position; m_trailCount = 1;
                m_visits = 0; m_lastWaypoint = -1;
                m_roamUntil = Time.time + RandomRange(m_roamingDuration);
                m_pauseUntil = 0f;
                if (ChooseWaypoint()) m_state = RoamingState.Roaming;
                else m_waitUntil = Time.time + m_blockedDestinationRetry;
                return;
            }
            if (m_state == RoamingState.Roaming)
            {
                if (Time.time >= m_roamUntil - ReturnBudget()) { BeginReturn(); return; }
                if (Time.time < m_pauseUntil) return;
                if (!m_hasTarget)
                {
                    if (!ChooseWaypoint()) { BeginReturn(); return; }
                }
                if (MoveToTarget(dt))
                {
                    RecordArrival(m_target);
                    if (m_state == RoamingState.Returning) return;
                    if (++m_routeIndex < m_routeCount)
                    {
                        m_target = m_route[m_routeIndex];
                        return;
                    }
                    m_visits++; m_hasTarget = false; m_speed = 0f;
                    m_pauseUntil = Time.time + RandomRange(m_pauseDuration);
                }
                else if (m_blockedAt >= 0f && Time.time - m_blockedAt > 1.5f) BeginReturn();
                return;
            }
            if (MoveToTarget(dt))
            {
                m_speed = 0f;
                if (m_trailCount > 0) m_trailCount--;
                if (m_trailCount > 0) m_target = m_trail[m_trailCount - 1];
                else
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, m_home.rotation, m_turnSpeed * dt);
                    if (Quaternion.Angle(transform.rotation, m_home.rotation) < .5f) FinishHome();
                }
            }
            if (m_blockedAt >= 0f && Time.time - m_blockedAt >= m_blockedReturnTimeout && Time.time >= m_nextRecovery)
            {
                m_nextRecovery = Time.time + 1f;
                // Replan around blockers; never teleport across furniture to recover.
                if (FindRoutes() && m_parents[1] >= 0)
                {
                    BuildRoute(1);
                    m_trailCount = m_routeCount;
                    for (int i = 0; i < m_routeCount; i++) m_trail[i] = m_route[m_routeCount - 1 - i];
                    m_target = m_trail[m_trailCount - 1];
                    m_blockedAt = -1f;
                }
            }
        }

        private bool ChooseWaypoint()
        {
            if (!FindRoutes()) return false;
            int count = m_area.WaypointCount, first = Random.Range(0, count);
            for (int i = 0; i < count; i++)
            {
                int index = (first + i) % count;
                Transform point = m_area.GetWaypoint(index);
                if (point == null || index == m_lastWaypoint || Vector3.SqrMagnitude(point.position - transform.position) < .25f) continue;
                if (m_parents[index + 2] < 0) continue;
                BuildRoute(index + 2);
                m_lastWaypoint = index; m_target = m_route[0]; m_hasTarget = true; m_blockedAt = -1f;
                return true;
            }
            return false;
        }

        // Tiny waypoint graph, evaluated only when choosing/recovering a route. Every edge
        // is checked against the full TV footprint, saved boundaries and live colliders.
        private bool FindRoutes()
        {
            if (m_area == null || m_home == null || m_nodes == null) return false;
            int count = m_area.WaypointCount + 2;
            if (count > m_nodes.Length) return false; // Reinitialise after editing points in Play mode.
            m_nodes[0] = transform.position; m_nodes[1] = m_home.position;
            for (int i = 2; i < count; i++)
            {
                var point = m_area.GetWaypoint(i - 2);
                m_nodes[i] = point != null ? point.position : transform.position;
            }
            for (int i = 0; i < count; i++) m_parents[i] = -1;
            int head = 0, tail = 1;
            m_queue[0] = 0; m_parents[0] = 0;
            while (head < tail)
            {
                int from = m_queue[head++];
                for (int to = 1; to < count; to++)
                {
                    if (m_parents[to] >= 0 || (to >= 2 && m_area.GetWaypoint(to - 2) == null)) continue;
                    if (!PositionClear(m_nodes[to]) || !HasFloor(m_nodes[to]) || !CanTravel(m_nodes[from], m_nodes[to])) continue;
                    m_parents[to] = from; m_queue[tail++] = to;
                }
            }
            return true;
        }

        private void BuildRoute(int destination)
        {
            m_routeCount = m_routeIndex = 0;
            for (int node = destination; node != 0; node = m_parents[node]) m_route[m_routeCount++] = m_nodes[node];
            System.Array.Reverse(m_route, 0, m_routeCount);
        }

        private void RecordArrival(Vector3 point)
        {
            for (int i = 0; i < m_trailCount; i++)
            {
                if ((m_trail[i] - point).sqrMagnitude > .0001f) continue;
                // Revisiting a point closes a loop; keep the earlier safe route home.
                m_trailCount = i + 1;
                return;
            }
            if (m_trailCount < m_trail.Length) m_trail[m_trailCount++] = point;
            else BeginReturn(); // Safe fallback if waypoints are added/moved during an outing.
        }

        private float ReturnBudget()
        {
            float distance = 0f;
            Vector3 from = transform.position;
            for (int i = m_trailCount - 1; i >= 0; i--) { distance += Vector3.Distance(from, m_trail[i]); from = m_trail[i]; }
            return distance / Mathf.Max(.1f, m_movementSpeed) + 2f;
        }

        private void BeginReturn()
        {
            m_state = RoamingState.Returning; m_speed = 0f; m_hasTarget = true; m_blockedAt = -1f;
            m_target = m_trailCount > 0 ? m_trail[m_trailCount - 1] : m_home.position;
        }

        private void FinishHome()
        {
            transform.SetPositionAndRotation(m_home.position, m_home.rotation);
            m_state = RoamingState.Idle; m_speed = 0f; m_hasTarget = false; m_blockedAt = -1f;
            m_sent = false; // Publish the exact canonical pose even when the last step was sub-millimetre.
            m_trailCount = 0; m_waitUntil = Time.time + RandomRange(m_roamingInterval);
        }

        private bool MoveToTarget(float dt)
        {
            Vector3 delta = m_target - transform.position;
            float distance = delta.magnitude;
            if (distance < .005f) return true;
            m_speed = Mathf.MoveTowards(m_speed, Mathf.Min(m_movementSpeed, Mathf.Sqrt(2f * m_acceleration * distance)), m_acceleration * dt);
            float step = Mathf.Min(distance, m_speed * dt);
            Vector3 next = transform.position + delta / distance * step;
            if (!CanTravel(transform.position, next) || !PositionClear(next))
            {
                m_speed = 0f;
                if (m_blockedAt < 0f) m_blockedAt = Time.time;
                return false;
            }
            m_blockedAt = -1f;
            Vector3 direction = delta; direction.y = 0f;
            if (direction.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), m_turnSpeed * dt);
            transform.position = next;
            return distance <= step + .005f;
        }

        private bool OwnCollider(Collider collider) => collider != null && collider.transform.IsChildOf(transform);
        // Cover the full 2.2 m model including the beacon, leaving 5 cm above the floor.
        private Vector3 ProbeCenter(Vector3 position) => position + Vector3.up * 1.15f;
        private Vector3 ProbeExtents => new(m_clearance, 1.1f, m_clearance);

        public bool CanTravel(Vector3 from, Vector3 to)
        {
            if (m_area == null || !m_area.AllowsSegment(from, to, m_clearance)) return false;
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .000001f) return PositionClear(to);
            if (!PositionClear(from) || !PositionClear(to)) return false;
            int count = Physics.BoxCastNonAlloc(ProbeCenter(from), ProbeExtents, delta.normalized, m_hits,
                Quaternion.identity, delta.magnitude, m_obstacleMask, QueryTriggerInteraction.Ignore);
            if (count == m_hits.Length) return false;
            for (int i = 0; i < count; i++) if (!OwnCollider(m_hits[i].collider)) return false;
            return true;
        }

        private bool PositionClear(Vector3 position)
        {
            int count = Physics.OverlapBoxNonAlloc(ProbeCenter(position), ProbeExtents, m_overlaps,
                Quaternion.identity, m_obstacleMask, QueryTriggerInteraction.Ignore);
            if (count == m_overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (!OwnCollider(m_overlaps[i])) return false;
            return true;
        }

        private bool HasFloor(Vector3 position) => Physics.Raycast(position + Vector3.up * .3f, Vector3.down,
            out var hit, .6f, m_obstacleMask, QueryTriggerInteraction.Ignore) && !OwnCollider(hit.collider) && hit.normal.y > .9f;

        private void Publish()
        {
            if (Time.unscaledTime < m_nextPublish) return;
            m_nextPublish = Time.unscaledTime + .1f;
            Vector3 position = m_home.InverseTransformPoint(transform.position);
            Quaternion rotation = Quaternion.Inverse(m_home.rotation) * transform.rotation;
            if (m_sent && !m_snap && (position - m_sentPosition).sqrMagnitude < .000001f && Quaternion.Angle(rotation, m_sentRotation) < .1f) return;
            m_network.PublishDispatcherPose(position, rotation, m_snap || !m_sent);
            m_sentPosition = position; m_sentRotation = rotation; m_sent = true; m_snap = false;
        }

        private void TickClient()
        {
            if (!m_network.TryGetDispatcherPose(out var pose)) return;
            if (!m_received || pose.Revision != m_revision)
            {
                m_clientFrom = m_home.InverseTransformPoint(transform.position);
                m_clientFromRotation = Quaternion.Inverse(m_home.rotation) * transform.rotation;
                m_clientTo = pose.Position; m_clientToRotation = pose.Rotation;
                if (!m_received || pose.Snap) { m_clientFrom = m_clientTo; m_clientFromRotation = m_clientToRotation; }
                m_clientReceivedAt = Time.unscaledTime; m_revision = pose.Revision; m_received = true;
            }
            float t = Mathf.Clamp01((Time.unscaledTime - m_clientReceivedAt) / .1f);
            transform.SetPositionAndRotation(m_home.TransformPoint(Vector3.Lerp(m_clientFrom, m_clientTo, t)),
                m_home.rotation * Quaternion.Slerp(m_clientFromRotation, m_clientToRotation, t));
        }
    }
}
