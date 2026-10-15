using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class TrafficVehicleController : NetworkBehaviour
    {
        private readonly SyncVar<float> m_health = new SyncVar<float>();
        private readonly SyncVar<bool> m_destroyed = new SyncVar<bool>();
        private readonly Dictionary<int, float> m_nextImpactTime = new Dictionary<int, float>(8);
        private readonly Collider[] m_explosionHits = new Collider[32];
        private readonly RaycastHit[] m_obstacleHits = new RaycastHit[8];
        private readonly RaycastHit[] m_groundHits = new RaycastHit[8];
        private readonly Collider[] m_intersectionHits = new Collider[8];
        private readonly HashSet<int> m_explosionVictims = new HashSet<int>();

        [SerializeField] private CarSettings m_settings;
        [SerializeField] private Rigidbody m_rigidbody;
        [SerializeField] private Renderer[] m_renderers;

        private TrafficRoute m_route;
        private TrafficManager m_manager;
        private int m_waypointIndex;
        private float m_nextObstacleCheck;
        private bool m_pathObstructed;
        private bool m_playerBlockingPath;
        private float m_despawnAt = float.PositiveInfinity;
        private MaterialPropertyBlock m_propertyBlock;
        private bool m_serverDriving;
        private bool m_warnedStationary;
        private float m_stationaryCheckAt;
        private Vector3 m_drivingStartPosition;
        private PhysicsMaterial m_runtimeTyreMaterial;

        public bool IsDestroyed => m_destroyed.Value;
        public float CurrentHealth => m_health.Value;

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            ConfigureGroundContact();
            m_health.OnChange += OnHealthChanged;
            m_destroyed.OnChange += OnDestroyedChanged;
        }

        private void OnDestroy()
        {
            m_health.OnChange -= OnHealthChanged;
            m_destroyed.OnChange -= OnDestroyedChanged;
            if (m_runtimeTyreMaterial != null) Destroy(m_runtimeTyreMaterial);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_health.Value = m_settings != null ? m_settings.MaximumHealth : 120f;
            m_destroyed.Value = false;
            m_rigidbody.isKinematic = false;
            m_rigidbody.WakeUp();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) m_rigidbody.isKinematic = true;
            ApplyDestroyedVisual(m_destroyed.Value);
        }

        public override void OnStopServer()
        {
            m_serverDriving = false;
            base.OnStopServer();
        }

        internal void InitialiseServer(TrafficRoute route, TrafficManager manager)
        {
            m_route = route;
            m_manager = manager;
            m_waypointIndex = route != null && route.Waypoints.Length > 1 ? 1 : 0;
            m_serverDriving = route != null && route.Waypoints.Length > 1;
            if (m_serverDriving && m_rigidbody != null)
            {
                m_drivingStartPosition = m_rigidbody.position;
                m_stationaryCheckAt = Time.time + 2f;
                m_warnedStationary = false;
                m_rigidbody.isKinematic = false;
                m_rigidbody.WakeUp();
            }
        }

        private void FixedUpdate()
        {
            if (!m_serverDriving || m_destroyed.Value || m_settings == null ||
                m_route == null || m_route.Waypoints.Length < 2) return;
            if (m_rigidbody.isKinematic)
            {
                m_rigidbody.isKinematic = false;
            }
            m_rigidbody.WakeUp();
            if (m_waypointIndex >= m_route.Waypoints.Length)
            {
                m_manager?.DespawnVehicle(this);
                return;
            }

            Vector3 position = m_rigidbody.position;
            Vector3 target = m_route.Waypoints[m_waypointIndex];
            Vector3 flatTarget = Vector3.ProjectOnPlane(target - position, Vector3.up);
            float reachDistance = Mathf.Max(2.2f, m_settings.MaximumDrivingSpeed * 0.18f);
            while (flatTarget.sqrMagnitude < reachDistance * reachDistance)
            {
                m_waypointIndex++;
                if (m_waypointIndex >= m_route.Waypoints.Length)
                {
                    m_manager?.DespawnVehicle(this);
                    return;
                }
                target = m_route.Waypoints[m_waypointIndex];
                flatTarget = Vector3.ProjectOnPlane(target - position, Vector3.up);
            }

            Vector3 groundNormal = TryGetGroundNormal(out Vector3 sampledNormal)
                ? sampledNormal
                : Vector3.up;
            Vector3 desiredForward = Vector3.ProjectOnPlane(flatTarget.normalized, groundNormal).normalized;
            if (desiredForward.sqrMagnitude < 0.1f) desiredForward = transform.forward;
            float turnAngle = Vector3.SignedAngle(transform.forward, desiredForward, groundNormal);
            float turnSpeedScale = Mathf.Lerp(1f, 0.42f, Mathf.InverseLerp(8f, 70f, Mathf.Abs(turnAngle)));
            if (Time.time >= m_nextObstacleCheck)
            {
                m_nextObstacleCheck = Time.time + 0.12f;
                m_pathObstructed = CheckPathObstructed(out m_playerBlockingPath);
            }

            Vector3 planarVelocity = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, groundNormal);
            float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, desiredForward);
            float targetSpeed = m_pathObstructed ? 0f : m_settings.MaximumDrivingSpeed * turnSpeedScale;
            // Coverage routes may turn around at a dead end. Stop before rotating
            // rather than carrying the car forwards off the road.
            if (Mathf.Abs(turnAngle) > 100f) targetSpeed = 0f;
            float acceleration = forwardSpeed < targetSpeed
                ? m_settings.Acceleration
                : m_playerBlockingPath
                    ? m_settings.PlayerBrakingStrength
                    : m_settings.BrakingStrength;
            float speedError = targetSpeed - forwardSpeed;
            m_rigidbody.AddForce(desiredForward * Mathf.Clamp(speedError, -acceleration, acceleration),
                ForceMode.Acceleration);
            Vector3 roadRight = Vector3.Cross(groundNormal, desiredForward).normalized;
            m_rigidbody.AddForce(-roadRight * Vector3.Dot(planarVelocity, roadRight) * 7f,
                ForceMode.Acceleration);

            float steering = Mathf.Clamp(turnAngle, -m_settings.TurningSpeed * Time.fixedDeltaTime,
                m_settings.TurningSpeed * Time.fixedDeltaTime);
            if (Mathf.Abs(forwardSpeed) > 0.15f || Mathf.Abs(turnAngle) > 4f)
                m_rigidbody.MoveRotation(
                    Quaternion.AngleAxis(steering, groundNormal) * m_rigidbody.rotation);

            if (groundNormal != Vector3.up)
            {
                m_rigidbody.AddTorque(Vector3.Cross(transform.up, groundNormal) * 12f,
                    ForceMode.Acceleration);
                m_rigidbody.AddForce(-groundNormal * Mathf.Clamp(planarVelocity.magnitude * 0.35f, 0f, 5f),
                    ForceMode.Acceleration);
            }
        }

        private void ConfigureGroundContact()
        {
            BoxCollider bodyCollider = GetComponent<BoxCollider>();
            if (bodyCollider != null)
            {
                // Keep the square chassis clear of road-mesh seams. Rounded contacts
                // below carry the car across tile joins and elevation transitions.
                bodyCollider.center = new Vector3(0f, 0.9f, 0f);
                bodyCollider.size = new Vector3(1.78f, 1.08f, 3.75f);
            }

            SphereCollider[] contacts = GetComponents<SphereCollider>();
            if (contacts.Length < 4)
            {
                Vector3[] centres =
                {
                    new Vector3(-0.78f, 0.36f, 1.28f),
                    new Vector3(0.78f, 0.36f, 1.28f),
                    new Vector3(-0.78f, 0.36f, -1.28f),
                    new Vector3(0.78f, 0.36f, -1.28f)
                };
                for (int index = contacts.Length; index < centres.Length; index++)
                {
                    SphereCollider contact = gameObject.AddComponent<SphereCollider>();
                    contact.center = centres[index];
                    contact.radius = 0.36f;
                }
            }

            m_runtimeTyreMaterial = new PhysicsMaterial("Traffic Car Low-Friction Contact")
            {
                dynamicFriction = 0.08f,
                staticFriction = 0.05f,
                bounciness = 0.02f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            Collider[] colliders = GetComponents<Collider>();
            for (int index = 0; index < colliders.Length; index++)
                if (colliders[index] != null && !colliders[index].isTrigger)
                    colliders[index].sharedMaterial = m_runtimeTyreMaterial;
            // Keep the mass below the contact points so bumps and cambers do not
            // lever the lightweight procedural chassis onto its side.
            m_rigidbody.centerOfMass = new Vector3(0f, -0.25f, 0f);
        }

        private bool TryGetGroundNormal(out Vector3 normal)
        {
            normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * 2.5f,
                Vector3.down, m_groundHits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = m_groundHits[index];
                if (hit.collider == null || hit.collider.attachedRigidbody == m_rigidbody ||
                    hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform) ||
                    hit.distance >= nearestDistance)
                    continue;
                nearestDistance = hit.distance;
                normal = hit.normal;
                found = true;
            }
            return found;
        }

        private void Update()
        {
            if (m_serverDriving && m_destroyed.Value && Time.time >= m_despawnAt)
                m_manager?.DespawnVehicle(this);
            if (m_serverDriving && !m_destroyed.Value && !m_warnedStationary &&
                Time.time >= m_stationaryCheckAt &&
                (m_rigidbody.position - m_drivingStartPosition).sqrMagnitude < 0.0625f)
            {
                m_warnedStationary = true;
                Debug.LogWarning(
                    $"Traffic car failed to begin moving. kinematic={m_rigidbody.isKinematic}, " +
                    $"blocked={m_pathObstructed}, routePoints={m_route?.Waypoints.Length ?? 0}, " +
                    $"waypoint={m_waypointIndex}, speed={m_rigidbody.linearVelocity.magnitude:F2}.",
                    this);
            }
        }

        private bool CheckPathObstructed(out bool playerBlocking)
        {
            playerBlocking = false;
            bool obstacleFound = false;
            float speed = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up).magnitude;
            float distance = m_settings.SafeFollowingDistance + speed * 0.35f;
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * 0.55f,
                0.65f, transform.forward, m_obstacleHits, distance, ~0,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider hit = m_obstacleHits[index].collider;
                if (hit == null || hit.attachedRigidbody == m_rigidbody ||
                    hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hit.GetComponentInParent<PlayerHealth>() != null)
                {
                    playerBlocking = true;
                    obstacleFound = true;
                }
                else if (hit.GetComponentInParent<TrafficVehicleController>() != null ||
                         hit.GetComponentInParent<ThiefHealth>() != null)
                {
                    obstacleFound = true;
                }
            }

            if (obstacleFound) return true;

            if (m_route != null && m_waypointIndex > 0 &&
                m_waypointIndex + 1 < m_route.Waypoints.Length)
            {
                Vector3 incoming = m_route.Waypoints[m_waypointIndex] -
                                   m_route.Waypoints[m_waypointIndex - 1];
                Vector3 outgoing = m_route.Waypoints[m_waypointIndex + 1] -
                                   m_route.Waypoints[m_waypointIndex];
                if (Vector3.Angle(incoming, outgoing) > 12f)
                {
                    int nearbyCount = Physics.OverlapSphereNonAlloc(
                        m_route.Waypoints[m_waypointIndex], 4.5f, m_intersectionHits, ~0,
                        QueryTriggerInteraction.Ignore);
                    for (int index = 0; index < nearbyCount; index++)
                    {
                        TrafficVehicleController other = m_intersectionHits[index]
                            ?.GetComponentInParent<TrafficVehicleController>();
                        if (other != null && other != this &&
                            other.GetInstanceID() < GetInstanceID())
                            return true;
                    }
                }
            }
            return false;
        }

        [Server]
        public void ApplyBulletDamageServer(float damage)
        {
            ApplyDamageServer(damage);
        }

        [Server]
        private void ApplyDamageServer(float damage)
        {
            if (damage <= 0f || m_destroyed.Value) return;
            m_health.Value = Mathf.Max(0f, m_health.Value - damage);
            if (m_health.Value <= 0f) DestroyVehicleServer();
        }

        [Server]
        private void DestroyVehicleServer()
        {
            if (m_destroyed.Value) return;
            m_destroyed.Value = true;
            m_rigidbody.linearVelocity = Vector3.zero;
            m_rigidbody.angularVelocity = Vector3.zero;
            m_rigidbody.isKinematic = true;
            ApplyExplosionDamage();
            PlayExplosionObserversRpc();
            m_despawnAt = Time.time + 2.2f;
        }

        private void ApplyExplosionDamage()
        {
            m_explosionVictims.Clear();
            int count = Physics.OverlapSphereNonAlloc(transform.position, m_settings.ExplosionRadius,
                m_explosionHits, ~0, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider hit = m_explosionHits[index];
                if (hit == null) continue;
                PlayerHealth player = hit.GetComponentInParent<PlayerHealth>();
                if (player != null && m_explosionVictims.Add(player.GetInstanceID()))
                {
                    Vector3 direction = player.transform.position - transform.position;
                    player.ApplyDamageServer(m_settings.ExplosionDamage, direction,
                        m_settings.ExplosionKnockback);
                    continue;
                }
                ThiefHealth thief = hit.GetComponentInParent<ThiefHealth>();
                if (thief != null && m_explosionVictims.Add(thief.GetInstanceID()))
                {
                    Vector3 direction = thief.transform.position - transform.position;
                    thief.ApplyDamageServer(m_settings.ExplosionDamage, direction,
                        m_settings.ExplosionKnockback);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!m_serverDriving || m_destroyed.Value || m_settings == null) return;
            float speed = Mathf.Max(collision.relativeVelocity.magnitude,
                Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up).magnitude);
            PlayerHealth player = collision.collider.GetComponentInParent<PlayerHealth>();
            ThiefHealth thief = collision.collider.GetComponentInParent<ThiefHealth>();
            Object victim = player != null ? (Object)player : thief;
            if (victim == null) return;
            float minimumImpactSpeed = player != null
                ? Mathf.Min(0.75f, m_settings.MinimumCollisionSpeed)
                : m_settings.MinimumCollisionSpeed;
            if (speed < minimumImpactSpeed) return;
            int id = victim.GetInstanceID();
            if (m_nextImpactTime.TryGetValue(id, out float nextTime) && Time.time < nextTime) return;
            m_nextImpactTime[id] = Time.time + m_settings.CollisionDamageCooldown;

            float scale = Mathf.Clamp(speed / Mathf.Max(0.1f, m_settings.MaximumDrivingSpeed), 0.35f, 1.5f);
            Vector3 direction = m_rigidbody.linearVelocity.sqrMagnitude > 0.1f
                ? m_rigidbody.linearVelocity.normalized
                : transform.forward;
            if (player != null)
                player.ApplyDamageWithFullKnockbackServer(m_settings.CollisionDamage * scale, direction,
                    m_settings.CollisionKnockbackStrength * scale);
            else
                thief.ApplyDamageServer(m_settings.CollisionDamage * scale, direction,
                    m_settings.CollisionKnockbackStrength * scale);
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayExplosionObserversRpc()
        {
            ApplyDestroyedVisual(true);
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "TrafficCarExplosion";
            flash.transform.position = transform.position + Vector3.up * 0.6f;
            flash.transform.localScale = Vector3.one * 2.4f;
            Collider collider = flash.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = flash.GetComponent<Renderer>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = new Color(1f, 0.24f, 0.02f);
            renderer.material = material;
            Destroy(flash, 0.22f);
        }

        private void OnHealthChanged(float previous, float next, bool asServer)
        {
            if (m_settings == null || m_destroyed.Value) return;
            float damage = 1f - next / m_settings.MaximumHealth;
            SetVisualColour(Color.Lerp(Color.white, new Color(0.32f, 0.26f, 0.2f), damage * 0.65f));
        }

        private void OnDestroyedChanged(bool previous, bool next, bool asServer)
        {
            if (next) ApplyDestroyedVisual(true);
        }

        private void ApplyDestroyedVisual(bool destroyed)
        {
            if (destroyed) SetVisualColour(new Color(0.12f, 0.12f, 0.12f));
        }

        private void SetVisualColour(Color colour)
        {
            m_propertyBlock ??= new MaterialPropertyBlock();
            m_propertyBlock.SetColor("_BaseColor", colour);
            if (m_renderers == null) return;
            for (int index = 0; index < m_renderers.Length; index++)
                if (m_renderers[index] != null) m_renderers[index].SetPropertyBlock(m_propertyBlock);
        }
    }
}
