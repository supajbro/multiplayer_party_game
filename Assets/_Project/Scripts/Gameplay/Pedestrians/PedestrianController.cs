using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Pedestrians
{
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class PedestrianController : NetworkBehaviour
    {
        public enum MovementState : byte { Idle, Walking, Frightened, Returning, Dead }
        private readonly SyncVar<MovementState> m_state = new SyncVar<MovementState>();
        private readonly List<Vector3> m_escapeTrail = new List<Vector3>(256);
        private readonly List<int> m_returnCandidates = new List<int>();
        private readonly List<Vector3> m_returnPath = new List<Vector3>();
        private readonly RaycastHit[] m_hits = new RaycastHit[12];
        private NavMeshPath m_navPath;
        [SerializeField] private Animator m_animator;
        [SerializeField] private PedestrianRagdoll m_ragdoll;
        private PedestrianManager m_manager;
        private SidewalkGraph m_graph;
        private PedestrianSettings m_settings;
        private Rigidbody m_body;
        private CapsuleCollider m_collider;
        private int m_previous, m_target, m_returnNode, m_returnIndex, m_trailHomeNode = -1;
        private int m_returnCandidateIndex;
        private float m_calmAt, m_despawnAt, m_nextThink;
        private Vector3 m_fleeDirection, m_fleeTarget;
        private Vector3 m_lastVisualPosition;
        private float m_lastMotionTime;
        private float m_health;
        private float m_nextVehicleImpactTime;
        private bool m_initialised;
        private bool m_findingReturn;
        private Vector3 m_returnFrom;
        private NavMeshHit m_returnStart;
        private bool m_returnOnMesh;
        private static readonly int SpeedHash = Animator.StringToHash("Speed");

        private void Awake()
        {
            m_body = GetComponent<Rigidbody>();
            m_collider = GetComponent<CapsuleCollider>();
            // Navigation owns this kinematic root. A solid collider would give it
            // infinite effective mass and let one pedestrian stop a moving truck.
            m_collider.isTrigger = true;
            m_lastVisualPosition = transform.position;
            m_lastMotionTime = Time.time;
            m_state.OnChange += StateChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) m_collider.enabled = false;
            ApplyState();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_collider.enabled = true;
        }

        public override void OnStopNetwork()
        {
            m_initialised = false;
            base.OnStopNetwork();
        }

        internal void InitialiseServer(PedestrianManager manager, SidewalkGraph graph,
            PedestrianSettings settings, int previous, int target)
        {
            m_manager = manager;
            m_graph = graph;
            m_settings = settings;
            m_health = Mathf.Max(1f, settings.MaximumHealth);
            m_previous = previous;
            m_target = target;
            m_initialised = true;
            m_nextThink = Time.time + Random.value * Mathf.Max(0.05f, settings.ThinkInterval);
            m_state.Value = MovementState.Walking;
        }

        public void FrightenServer(Vector3 origin)
        {
            if (!IsServerInitialized || !m_initialised || m_state.Value == MovementState.Dead) return;
            bool entering = m_state.Value != MovementState.Frightened;
            if (m_state.Value == MovementState.Walking || m_state.Value == MovementState.Idle)
            {
                m_escapeTrail.Clear();
                m_escapeTrail.Add(transform.position);
                m_trailHomeNode = -1;
            }
            else if (m_state.Value == MovementState.Returning && !m_findingReturn)
            {
                // Preserve the remaining safe route if shooting interrupts recovery.
                m_escapeTrail.Clear();
                for (int i = m_returnPath.Count - 1; i >= m_returnIndex; i--)
                    m_escapeTrail.Add(m_returnPath[i]);
                m_escapeTrail.Add(transform.position);
                m_trailHomeNode = m_returnNode;
            }
            m_findingReturn = false;
            // Refresh the timer for every nearby shot; change direction at the AI rate.
            m_calmAt = Time.time + Mathf.Max(0.1f, m_settings.FrightenedDuration);
            if (entering || Time.time >= m_nextThink)
            {
                Vector3 away = Vector3.ProjectOnPlane(transform.position - origin, Vector3.up);
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
                m_fleeDirection = Quaternion.Euler(0f, Random.Range(-55f, 55f), 0f) * away.normalized;
                m_fleeTarget = transform.position;
                m_nextThink = Time.time + Random.value * Mathf.Max(0.05f, m_settings.ThinkInterval);
            }
            m_state.Value = MovementState.Frightened;
        }

        public void ApplyVehicleImpactServer(float speed)
        {
            if (!IsServerInitialized || !m_initialised || m_state.Value == MovementState.Dead ||
                speed < Mathf.Max(0.1f, m_settings.LethalVehicleSpeed)) return;
            DieServer();
        }

        public void ApplyBulletDamageServer(float damage)
        {
            if (!IsServerInitialized || !m_initialised || m_state.Value == MovementState.Dead || damage <= 0f) return;
            m_health = Mathf.Max(0f, m_health - damage);
            if (m_health <= 0f) DieServer();
        }

        private void DieServer()
        {
            m_state.Value = MovementState.Dead;
            m_despawnAt = Time.time + Mathf.Max(0.1f, m_settings.CorpseDuration);
        }

        private void OnTriggerEnter(Collider other) => TryVehicleImpact(other);

        private void OnTriggerStay(Collider other) => TryVehicleImpact(other);

        private void TryVehicleImpact(Collider other)
        {
            if (!IsServerInitialized || !m_initialised || m_state.Value == MovementState.Dead ||
                Time.time < m_nextVehicleImpactTime || other.isTrigger) return;
            Rigidbody vehicleBody = other.attachedRigidbody;
            if (vehicleBody == null || vehicleBody.isKinematic) return;
            var van = other.GetComponentInParent<CouchGuys.Gameplay.Vehicles.VanController>();
            if (van == null || van.IsDestroyed) return;
            Vector3 velocity = Vector3.ProjectOnPlane(vehicleBody.linearVelocity, Vector3.up);
            float speed = velocity.magnitude;
            if (speed < 0.1f) return;
            m_nextVehicleImpactTime = Time.time + 0.5f;
            // A human-sized impulse gives the truck a bump, capped at 8% speed loss.
            float impactMass = Mathf.Min(75f, vehicleBody.mass * 0.08f);
            vehicleBody.AddForceAtPosition(-velocity * impactMass,
                transform.position + Vector3.up * 0.7f, ForceMode.Impulse);
            ApplyVehicleImpactServer(speed);
        }

        public static bool IsBulletHit(Collider collider)
        {
            if (collider == null) return false;
            if (!collider.isTrigger) return true;
            var pedestrian = collider.GetComponentInParent<PedestrianController>();
            return pedestrian != null && pedestrian.m_state.Value != MovementState.Dead;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || !m_initialised) return;
            if (m_state.Value == MovementState.Dead)
            {
                if (Time.time >= m_despawnAt) m_manager.Despawn(this);
                return;
            }
            if (m_state.Value == MovementState.Frightened && Time.time >= m_calmAt && Time.time >= m_nextThink)
                BeginReturn();
            if (m_state.Value == MovementState.Walking) Walk(Time.fixedDeltaTime);
            else if (m_state.Value == MovementState.Frightened)
            {
                if (Time.time >= m_nextThink)
                {
                    m_nextThink = Time.time + Mathf.Max(0.05f, m_settings.ThinkInterval);
                    ChooseFleeStep();
                }
                Move(m_fleeTarget, Mathf.Max(0.1f, m_settings.RunningSpeed) * Time.fixedDeltaTime);
            }
            else if (m_state.Value == MovementState.Returning) Return(Time.fixedDeltaTime);
        }

        private void Walk(float dt)
        {
            float budget = Mathf.Max(0.1f, m_settings.WalkingSpeed) * dt;
            for (int step = 0; step < 16 && budget > 0f; step++)
            {
                Vector3 target = m_graph.Nodes[m_target].Position;
                float distance = Vector3.Distance(transform.position, target);
                Move(target, budget);
                budget -= distance;
                if (budget < 0f) break;
                int next = m_graph.ChooseNext(m_target, m_previous);
                if (next < 0) { m_state.Value = MovementState.Idle; return; }
                m_previous = m_target;
                m_target = next;
            }
        }

        private void Move(Vector3 target, float distance)
        {
            Vector3 position = Vector3.MoveTowards(transform.position, target, distance);
            Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
            // Position follows the exact polyline. Rotation alone is smoothed so bends
            // cannot steer a pedestrian off the sidewalk or cut a building corner.
            if (direction.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), 540f * Time.fixedDeltaTime);
            transform.position = position;
        }

        private void ChooseFleeStep()
        {
            m_fleeTarget = transform.position;
            if (m_escapeTrail.Count >= 255) return; // Retain a bounded, traversable fallback home.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Vector3 direction = Quaternion.Euler(0f, attempt == 0 ? 0f : Random.Range(-30f, 30f), 0f)
                    * m_fleeDirection;
                float stride = Mathf.Max(0.1f, m_settings.RunningSpeed) * Mathf.Max(0.05f, m_settings.ThinkInterval);
                Vector3 candidate = transform.position + direction * stride;
                if (!TryGround(candidate, out candidate) ||
                    Mathf.Abs(candidate.y - transform.position.y) > 0.8f || !ClearSegment(transform.position, candidate)) continue;
                if ((transform.position - m_escapeTrail[m_escapeTrail.Count - 1]).sqrMagnitude > 0.1f)
                    m_escapeTrail.Add(transform.position);
                m_fleeTarget = candidate;
                break;
            }
        }

        private bool TryGround(Vector3 position, out Vector3 grounded)
        {
            grounded = position;
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 1.5f, Vector3.down,
                m_hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = m_hits[i];
                if (hit.collider.attachedRigidbody != null || hit.normal.y < 0.65f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                grounded = hit.point + Vector3.up * 0.06f;
            }
            return nearest < float.PositiveInfinity;
        }

        private bool ClearSegment(Vector3 from, Vector3 to)
        {
            Vector3 offset = to - from;
            if (offset.sqrMagnitude < 0.0001f) return true;
            int count = Physics.CapsuleCastNonAlloc(from + Vector3.up * 0.4f,
                from + Vector3.up * 1.35f, 0.25f, offset.normalized, m_hits,
                offset.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (m_hits[i].collider.attachedRigidbody != m_body &&
                    !m_hits[i].collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        private void BeginReturn()
        {
            m_navPath ??= new NavMeshPath();
            m_returnPath.Clear();
            m_returnCandidates.Clear();
            m_returnCandidates.AddRange(m_graph.SpawnNodes);
            Vector3 from = transform.position;
            m_returnCandidates.Sort((a, b) => (m_graph.Nodes[a].Position - from).sqrMagnitude
                .CompareTo((m_graph.Nodes[b].Position - from).sqrMagnitude));
            m_returnFrom = from;
            m_returnCandidateIndex = 0;
            // Reuse the region's existing NavMesh only for recovery. Search closest
            // candidates first, spreading unreachable-path work over AI updates.
            m_returnOnMesh = NavMesh.SamplePosition(from, out m_returnStart, 1f, NavMesh.AllAreas);
            m_findingReturn = true;
            m_state.Value = MovementState.Returning;
            FindReturnStep();
        }

        private void FindReturnStep()
        {
            m_nextThink = Time.time + Mathf.Max(0.05f, m_settings.ThinkInterval);
            Vector3 from = m_returnFrom;
            float knownHomeDistance = m_escapeTrail.Count > 0
                ? (m_escapeTrail[0] - from).sqrMagnitude : float.PositiveInfinity;
            for (int attempt = 0; attempt < 2 && m_returnCandidateIndex < m_returnCandidates.Count; attempt++)
            {
                int node = m_returnCandidates[m_returnCandidateIndex++];
                Vector3 end = m_graph.Nodes[node].Position;
                // The stored trail already proves this sidewalk point is reachable.
                // More distant candidates cannot improve the closest return location.
                if ((end - from).sqrMagnitude > knownHomeDistance + 0.6f)
                {
                    m_returnCandidateIndex = m_returnCandidates.Count;
                    break;
                }
                if (DirectGroundPath(from, end)) m_returnPath.Add(end);
                else if (m_returnOnMesh && NavMesh.SamplePosition(end, out NavMeshHit finish, 0.5f, NavMesh.AllAreas) &&
                    ClearSegment(from, m_returnStart.position) && ClearSegment(finish.position, end) &&
                    NavMesh.CalculatePath(m_returnStart.position, finish.position, NavMesh.AllAreas, m_navPath) &&
                    m_navPath.status == NavMeshPathStatus.PathComplete)
                {
                    m_returnPath.AddRange(m_navPath.corners);
                    m_returnPath.Add(end);
                }
                else continue;
                m_returnNode = node;
                StartReturning();
                return;
            }
            if (m_returnCandidateIndex < m_returnCandidates.Count) return;
            // No NavMesh required: retrace the validated escape trail to the sidewalk.
            for (int i = m_escapeTrail.Count - 1; i >= 0; i--) m_returnPath.Add(m_escapeTrail[i]);
            m_returnNode = m_trailHomeNode;
            StartReturning();
        }

        private bool DirectGroundPath(Vector3 from, Vector3 end)
        {
            if (!ClearSegment(from, end)) return false;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, end) / 0.75f));
            Vector3 previous = from;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 sample = Vector3.Lerp(from, end, i / (float)steps);
                if (!TryGround(sample, out Vector3 ground) || Mathf.Abs(ground.y - sample.y) > 0.3f ||
                    Mathf.Abs(ground.y - previous.y) > 0.8f) return false;
                previous = ground;
            }
            return true;
        }

        private void StartReturning()
        {
            m_findingReturn = false;
            m_returnIndex = 0;
            m_state.Value = MovementState.Returning;
        }

        private void Return(float dt)
        {
            if (m_findingReturn)
            {
                if (Time.time >= m_nextThink) FindReturnStep();
                return;
            }
            if (m_returnIndex < m_returnPath.Count)
            {
                Vector3 target = m_returnPath[m_returnIndex];
                Move(target, Mathf.Max(0.1f, m_settings.RunningSpeed) * dt);
                if ((transform.position - target).sqrMagnitude < 0.0001f) m_returnIndex++;
                return;
            }
            if (m_returnNode >= 0)
            {
                m_previous = m_returnNode;
                m_target = m_graph.ChooseNext(m_returnNode, -1);
            }
            m_escapeTrail.Clear();
            m_state.Value = MovementState.Walking;
        }

        private void StateChanged(MovementState oldState, MovementState next, bool asServer)
        {
            if (next == MovementState.Frightened) m_lastMotionTime = Time.time;
            ApplyState();
        }

        private void ApplyState()
        {
            if (m_state.Value != MovementState.Dead) return;
            m_collider.enabled = false;
            m_ragdoll?.Fall(transform.forward * 2f);
        }

        private void Update()
        {
            if (m_animator == null || !m_animator.enabled || m_state.Value == MovementState.Dead) return;
            float speed = m_state.Value == MovementState.Walking ? 0.5f :
                m_state.Value == MovementState.Frightened || m_state.Value == MovementState.Returning ? 1f : 0f;
            if ((transform.position - m_lastVisualPosition).sqrMagnitude >= 0.000001f)
                m_lastMotionTime = Time.time;
            if (Time.time - m_lastMotionTime > 0.2f) speed = 0f;
            m_animator.SetFloat(SpeedHash, speed, 0.08f, Time.deltaTime);
            m_lastVisualPosition = transform.position;
        }
    }
}
