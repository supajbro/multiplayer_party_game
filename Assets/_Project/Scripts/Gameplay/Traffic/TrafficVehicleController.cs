using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleAudioEmitter))]
    public sealed class TrafficVehicleController : NetworkBehaviour
    {
        private readonly SyncVar<float> m_health = new SyncVar<float>();
        private readonly SyncVar<bool> m_destroyed = new SyncVar<bool>();
        private readonly SyncVar<float> m_wheelSteering = new();
        private readonly Dictionary<int, float> m_nextImpactTime = new Dictionary<int, float>(8);
        private readonly Dictionary<int, float> m_lastSplatContactTime = new Dictionary<int, float>(8);
        private readonly Collider[] m_explosionHits = new Collider[32];
        private readonly RaycastHit[] m_obstacleHits = new RaycastHit[8];
        private readonly RaycastHit[] m_groundHits = new RaycastHit[8];
        private readonly Collider[] m_intersectionHits = new Collider[8];
        private readonly Collider[] m_impactHits = new Collider[32];
        private readonly HashSet<int> m_explosionVictims = new HashSet<int>();

        [SerializeField] private CarSettings m_settings;
        [SerializeField] private Rigidbody m_rigidbody;
        [SerializeField] private Renderer[] m_renderers;
        [SerializeField] private Transform m_visualAnchor;
        [SerializeField] private GameObject[] m_visualPrefabs;
        [SerializeField] private GameObject[] m_visualVariants;

        private TrafficRoute m_route;
        private TrafficManager m_manager;
        private int m_waypointIndex;
        private float m_nextObstacleCheck;
        private bool m_pathObstructed;
        private float m_despawnAt = float.PositiveInfinity;
        private MaterialPropertyBlock m_propertyBlock;
        private bool m_serverDriving;
        private bool m_warnedStationary;
        private float m_stationaryCheckAt;
        private Vector3 m_drivingStartPosition;
        private PhysicsMaterial m_runtimeTyreMaterial;
        private GameObject m_visualInstance;
        private BoxCollider m_bodyCollider;
        private Vector3 m_impactVelocity;
        private VehicleAudioEmitter m_audio;
        private RogueCarSettings m_rogueSettings;

        internal void InitialiseRogueServer(RogueCarSettings settings, TrafficManager manager, int visualVariant)
        {
            InitialiseServer(null, manager, visualVariant);
            m_rogueSettings = settings;
            m_health.Value = Mathf.Max(1f, settings.MaxHealth);
            RogueCar rogue = GetComponent<RogueCar>();
            if (rogue == null) rogue = gameObject.AddComponent<RogueCar>();
            rogue.Initialise(settings);
        }

        internal void UpdateRogueImpactsServer(float steering)
        {
            if (!IsServerInitialized || m_destroyed.Value || m_rogueSettings == null) return;
            m_wheelSteering.Value = steering;
            m_impactVelocity = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up);
            CheckCharacterImpacts();
        }

        public bool IsDestroyed => m_destroyed.Value;
        public float CurrentHealth => m_health.Value;
        public int VisualVariantCount => m_visualVariants != null && m_visualVariants.Length > 0
            ? m_visualVariants.Length
            : m_visualPrefabs?.Length ?? 0;

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            m_audio = GetComponent<VehicleAudioEmitter>();
            ConfigureGroundContact();
            ApplyVisualVariant(0);
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
            m_lastSplatContactTime.Clear();
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
            m_audio.SetEngineRunning(!IsDestroyed);
        }

        public override void OnStopClient()
        {
            m_audio.StopLocal();
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            m_serverDriving = false;
            base.OnStopServer();
        }

        internal void InitialiseServer(TrafficRoute route, TrafficManager manager, int visualVariant)
        {
            m_route = route;
            m_manager = manager;
            m_waypointIndex = route != null && route.Waypoints.Length > 1 ? 1 : 0;
            m_serverDriving = route != null && route.Waypoints.Length > 1;
            SetVisualVariantObserversRpc(visualVariant);
            if (m_serverDriving && m_rigidbody != null)
            {
                m_drivingStartPosition = m_rigidbody.position;
                m_stationaryCheckAt = Time.time + 2f;
                m_warnedStationary = false;
                m_rigidbody.isKinematic = false;
                m_rigidbody.WakeUp();
            }
        }

        [ObserversRpc(BufferLast = true, RunLocally = true)]
        private void SetVisualVariantObserversRpc(int visualVariant)
        {
            ApplyVisualVariant(visualVariant);
        }

        private void ApplyVisualVariant(int visualVariant)
        {
            if (m_visualVariants != null && m_visualVariants.Length > 0)
            {
                int selectedIndex = Mathf.Abs(visualVariant) % m_visualVariants.Length;
                for (int variantIndex = 0; variantIndex < m_visualVariants.Length; variantIndex++)
                {
                    GameObject variant = m_visualVariants[variantIndex];
                    if (variant != null) variant.SetActive(variantIndex == selectedIndex);
                }

                m_visualInstance = m_visualVariants[selectedIndex];
                m_renderers = m_visualInstance != null
                    ? m_visualInstance.GetComponentsInChildren<Renderer>(true)
                    : System.Array.Empty<Renderer>();
                ApplyDestroyedVisual(m_destroyed.Value);
                return;
            }

            if (m_visualPrefabs == null || m_visualPrefabs.Length == 0) return;
            if (m_visualAnchor == null)
            {
                GameObject anchor = new GameObject("VisualAnchor");
                anchor.transform.SetParent(transform, false);
                m_visualAnchor = anchor.transform;
                if (m_renderers != null)
                {
                    for (int rendererIndex = 0; rendererIndex < m_renderers.Length; rendererIndex++)
                        if (m_renderers[rendererIndex] != null)
                            m_renderers[rendererIndex].gameObject.SetActive(false);
                }
            }

            int index = Mathf.Abs(visualVariant) % m_visualPrefabs.Length;
            GameObject visualPrefab = m_visualPrefabs[index];
            if (visualPrefab == null) return;
            if (m_visualInstance != null)
            {
                m_visualInstance.SetActive(false);
                Destroy(m_visualInstance);
            }

            m_visualInstance = Instantiate(visualPrefab, m_visualAnchor, false);
            m_visualInstance.name = visualPrefab.name;
            m_renderers = m_visualInstance.GetComponentsInChildren<Renderer>(true);
            ApplyDestroyedVisual(m_destroyed.Value);
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || !m_serverDriving || m_destroyed.Value || m_settings == null ||
                m_route == null || m_route.Waypoints.Length < 2) return;
            if (m_rigidbody.isKinematic)
            {
                m_rigidbody.isKinematic = false;
            }
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
            m_wheelSteering.Value = Mathf.Clamp(turnAngle, -32f, 32f);
            float turnSpeedScale = Mathf.Lerp(1f, 0.42f, Mathf.InverseLerp(8f, 70f, Mathf.Abs(turnAngle)));
            if (Time.time >= m_nextObstacleCheck)
            {
                m_nextObstacleCheck = Time.time + 0.12f;
                m_pathObstructed = CheckPathObstructed();
            }

            Vector3 planarVelocity = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, groundNormal);
            float forwardSpeed = Vector3.Dot(m_rigidbody.linearVelocity, desiredForward);
            float targetSpeed = m_pathObstructed ? 0f : m_settings.MaximumDrivingSpeed * turnSpeedScale;
            // Coverage routes may turn around at a dead end. Stop before rotating
            // rather than carrying the car forwards off the road.
            if (Mathf.Abs(turnAngle) > 100f) targetSpeed = 0f;
            float acceleration = forwardSpeed < targetSpeed
                ? m_settings.Acceleration
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

            // Capture travel before the solver can deflect or stop the chassis.
            m_impactVelocity = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up);
            CheckCharacterImpacts();
        }

        private void CheckCharacterImpacts()
        {
            if (m_bodyCollider == null || m_impactVelocity.sqrMagnitude < 0.01f) return;
            Vector3 travel = m_impactVelocity * Time.fixedDeltaTime;
            Vector3 localTravel = Quaternion.Inverse(transform.rotation) * travel;
            Vector3 halfExtents = Vector3.Scale(m_bodyCollider.size, transform.lossyScale) * 0.5f;
            halfExtents += new Vector3(Mathf.Abs(localTravel.x), Mathf.Abs(localTravel.y),
                Mathf.Abs(localTravel.z)) * 0.5f + Vector3.one * 0.05f;
            // A short swept overlap also catches CharacterControllers without relying
            // on Rigidbody collision callbacks. Reuse the collision damage cooldown.
            int count = Physics.OverlapBoxNonAlloc(
                transform.TransformPoint(m_bodyCollider.center) + travel * 0.5f,
                halfExtents, m_impactHits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider hit = m_impactHits[index];
                if (hit == null || hit.attachedRigidbody == m_rigidbody) continue;
                ApplyCharacterImpact(hit, m_impactVelocity, m_impactVelocity.magnitude);
            }
        }

        private void ConfigureGroundContact()
        {
            BoxCollider bodyCollider = GetComponent<BoxCollider>();
            m_bodyCollider = bodyCollider;
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
            if (m_visualInstance != null && m_visualInstance.TryGetComponent(out TrafficWheelAnimator wheels))
                wheels.SetSteering(IsDestroyed ? 0f : m_wheelSteering.Value);
            if ((m_serverDriving || m_rogueSettings != null) && m_destroyed.Value && Time.time >= m_despawnAt)
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

        private bool CheckPathObstructed()
        {
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
                // Players are impact targets, never a reason to yield or brake.
                if (hit.GetComponentInParent<PlayerHealth>() != null) continue;
                if (hit.GetComponentInParent<TrafficVehicleController>() != null ||
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
            if (!IsServerInitialized || (!m_serverDriving && m_rogueSettings == null) || m_destroyed.Value || m_settings == null) return;
            Vector3 velocity = m_impactVelocity.sqrMagnitude > 0.01f
                ? m_impactVelocity
                : Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up);
            ApplyCharacterImpact(collision.collider, velocity,
                Mathf.Max(collision.relativeVelocity.magnitude, velocity.magnitude));
        }

        private void ApplyCharacterImpact(Collider collider, Vector3 velocity, float speed)
        {
            PlayerHealth player = collider.GetComponentInParent<PlayerHealth>();
            ThiefHealth thief = collider.GetComponentInParent<ThiefHealth>();
            Object victim = player != null ? (Object)player : thief;
            if (victim == null) return;
            // The swept overlap and collision callback can both report the same hit.
            // Track continued contact independently of the damage cooldown so a car
            // pushing one thief does not emit another splat every cooldown interval.
            bool newContact = true;
            if (thief != null)
            {
                int thiefId = thief.GetInstanceID();
                newContact = !m_lastSplatContactTime.TryGetValue(thiefId, out float lastContact) ||
                    Time.fixedTime - lastContact > Time.fixedDeltaTime * 1.5f;
                m_lastSplatContactTime[thiefId] = Time.fixedTime;
            }
            float minimumImpactSpeed = player != null
                ? Mathf.Min(0.75f, m_settings.MinimumCollisionSpeed)
                : m_settings.MinimumCollisionSpeed;
            if (speed < minimumImpactSpeed) return;
            int id = victim.GetInstanceID();
            if (m_nextImpactTime.TryGetValue(id, out float nextTime) && Time.time < nextTime) return;
            m_nextImpactTime[id] = Time.time + m_settings.CollisionDamageCooldown;

            float scale = Mathf.Clamp(speed / Mathf.Max(0.1f,
                m_rogueSettings != null ? m_rogueSettings.MaxSpeed : m_settings.MaximumDrivingSpeed), 0.35f, 1.5f);
            Vector3 direction = velocity.sqrMagnitude > 0.01f
                ? velocity.normalized
                : transform.forward;
            if (player != null)
                player.ApplyVehicleImpactServer((m_rogueSettings != null ?
                    m_rogueSettings.PlayerCollisionDamage : m_settings.CollisionDamage) * scale, direction,
                    (m_rogueSettings != null ? m_rogueSettings.PlayerKnockbackForce :
                    m_settings.CollisionKnockbackStrength) * scale, 0f, true);
            else
            {
                bool alive = thief.IsAlive;
                thief.ApplyVehicleImpactServer(m_settings.CollisionDamage * scale, direction,
                    m_settings.CollisionKnockbackStrength * scale, 0f, true);
                if (alive && newContact && velocity.sqrMagnitude > 0.01f)
                    PlaySplatServer(collider.ClosestPoint(transform.position));
            }
        }

        [Server]
        internal void PlaySplatServer(Vector3 position) => PlaySplatObserversRpc(position);

        // Client-only execution: the host hears the observer event exactly once.
        [ObserversRpc]
        private void PlaySplatObserversRpc(Vector3 position) => m_audio.PlaySplat(position);

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
            if (IsClientInitialized) m_audio.SetEngineRunning(!next);
        }

        private void ApplyDestroyedVisual(bool destroyed)
        {
            if (destroyed) SetVisualColour(new Color(0.12f, 0.12f, 0.12f));
        }

        private void SetVisualColour(Color colour)
        {
            m_propertyBlock ??= new MaterialPropertyBlock();
            if (m_renderers == null) return;
            for (int index = 0; index < m_renderers.Length; index++)
            {
                Renderer renderer = m_renderers[index];
                if (renderer == null) continue;
                Material material = renderer.sharedMaterial;
                Color original = material != null && material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor") : Color.white;
                Color tinted = original * colour;
                tinted.a = original.a;
                m_propertyBlock.SetColor("_BaseColor", tinted);
                renderer.SetPropertyBlock(m_propertyBlock);
            }
        }
    }
}
