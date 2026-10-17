using System.Collections.Generic;
using CouchGuys.Player;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.ProceduralGeneration;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class VanController : NetworkBehaviour
    {
        public static readonly HashSet<VanController> ActiveVans = new();

        [Header("Configuration")]
        [SerializeField] private VanSettings m_settings;
        [Header("References")]
        [SerializeField] private Rigidbody m_rigidbody;
        [SerializeField] private Transform m_visualModel;
        [SerializeField] private Transform m_cameraTarget;
        [SerializeField] private Transform m_driverSeat;
        [SerializeField] private Transform[] m_exitPoints;
        [SerializeField] private Transform[] m_wheels;
        [SerializeField] private Renderer m_destroyedRenderer;
        [SerializeField] private Transform m_leftDoorPivot;
        [SerializeField] private Transform m_rightDoorPivot;
        [SerializeField] private Transform m_rearDoorInteractionPoint;
        [SerializeField] private Transform m_cargoLoadingPoint;
        [SerializeField] private Transform[] m_cargoPoints;

        private readonly SyncVar<float> m_fuel = new();
        private readonly SyncVar<float> m_health = new();
        private readonly SyncVar<NetworkObject> m_driver = new();
        private readonly SyncVar<bool> m_destroyed = new();
        private readonly SyncVar<bool> m_rearDoorsOpen = new();
        private readonly SyncList<NetworkObject> m_loadedCouches = new();
        private Vector2 m_driveInput;
        private float m_steering;
        private float m_nextCollisionDamageTime;
        private readonly Dictionary<int, float> m_nextCharacterImpactTime = new();
        private float m_lastInputTime;
        private Vector3[] m_wheelBasePositions;
        private Quaternion[] m_wheelBaseRotations;
        private Quaternion m_visualBaseRotation;
        private Material m_runtimeBodyMaterial;
        private PhysicsMaterial m_runtimePhysicsMaterial;
        private Color m_originalBodyColour = Color.white;
        private Quaternion m_leftDoorClosedRotation;
        private Quaternion m_rightDoorClosedRotation;
        private float m_doorOpenProgress;
        private float m_doorStateChangedServerTime;
        private readonly Collider[] m_clearanceHits = new Collider[32];

        public VanSettings Settings => m_settings;
        public float Fuel => m_fuel.Value; public float Health => m_health.Value;
        public float FuelFraction => m_settings != null ? m_fuel.Value / m_settings.MaxFuel : 0f;
        public float HealthFraction => m_settings != null ? m_health.Value / m_settings.MaxHealth : 0f;
        public bool IsDestroyed => m_destroyed.Value; public bool IsOccupied => m_driver.Value != null;
        public NetworkObject Driver => m_driver.Value; public Transform CameraTarget => m_cameraTarget != null ? m_cameraTarget : transform;
        public float SpeedKph => m_rigidbody != null ? m_rigidbody.linearVelocity.magnitude * 3.6f : 0f;
        public bool RearDoorsOpen => m_rearDoorsOpen.Value;
        public float RearDoorOpenProgress => m_doorOpenProgress;
        public Transform RearDoorInteractionPoint => m_rearDoorInteractionPoint != null ? m_rearDoorInteractionPoint : transform;
        public Transform CargoLoadingPoint => m_cargoLoadingPoint != null ? m_cargoLoadingPoint : RearDoorInteractionPoint;
        public int LoadedCouchCount
        {
            get { int count = 0; for (int i = 0; i < m_loadedCouches.Count; i++) if (m_loadedCouches[i] != null) count++; return count; }
        }

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            if (m_settings != null) m_rigidbody.mass = m_settings.VehicleMass;
            ConfigureArcadeColliderFriction();
            if (m_visualModel != null) m_visualBaseRotation = m_visualModel.localRotation;
            if (m_destroyedRenderer != null)
            {
                m_runtimeBodyMaterial = m_destroyedRenderer.material;
                m_originalBodyColour = m_runtimeBodyMaterial.color;
            }
            m_health.OnChange += OnHealthChanged;
            if (m_leftDoorPivot != null) m_leftDoorClosedRotation = m_leftDoorPivot.localRotation;
            if (m_rightDoorPivot != null) m_rightDoorClosedRotation = m_rightDoorPivot.localRotation;
            if (m_wheels != null)
            {
                m_wheelBasePositions = new Vector3[m_wheels.Length];
                m_wheelBaseRotations = new Quaternion[m_wheels.Length];
                for (int i = 0; i < m_wheels.Length; i++) if (m_wheels[i] != null)
                { m_wheelBasePositions[i] = m_wheels[i].localPosition; m_wheelBaseRotations[i] = m_wheels[i].localRotation; }
            }
        }

        private void OnEnable() => ActiveVans.Add(this);
        private void OnDisable() => ActiveVans.Remove(this);
        private void OnDestroy()
        {
            m_health.OnChange -= OnHealthChanged;
            if (m_runtimeBodyMaterial != null) Destroy(m_runtimeBodyMaterial);
            if (m_runtimePhysicsMaterial != null) Destroy(m_runtimePhysicsMaterial);
        }

        private void ConfigureArcadeColliderFriction()
        {
            // The placeholder is a flat-bottomed cube rather than a wheeled mesh. Default
            // static friction can exceed the Light Van's configured acceleration and pin it
            // to the road. Physical contact stays slippery while lateral grip remains under
            // the explicit VanSettings.Traction calculation below.
            m_runtimePhysicsMaterial = new PhysicsMaterial("Van Arcade Contact")
            {
                dynamicFriction = 0.03f,
                staticFriction = 0f,
                bounciness = 0.02f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
                colliders[index].sharedMaterial = m_runtimePhysicsMaterial;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_nextCollisionDamageTime = 0f;
            m_nextCharacterImpactTime.Clear();
            m_fuel.Value = m_settings.MaxFuel;
            m_health.Value = m_settings.MaxHealth;
            m_destroyed.Value = false;
            m_rearDoorsOpen.Value = false;
            m_loadedCouches.Clear();
            int capacity = Mathf.Min(m_settings.CargoCapacity, m_cargoPoints != null ? m_cargoPoints.Length : 0);
            for (int index = 0; index < capacity; index++) m_loadedCouches.Add(null);
            m_rigidbody.isKinematic = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) m_rigidbody.isKinematic = true;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || m_settings == null || m_rigidbody.isKinematic) return;
            if (Time.time - m_lastInputTime > 0.35f) m_driveInput = Vector2.zero;
            SimulateDriving(Time.fixedDeltaTime);
            ConsumeFuel(Time.fixedDeltaTime);
        }

        private void LateUpdate() => UpdateVisuals(Time.deltaTime);

        [Server]
        public bool TryEnterServer(PlayerVanInteractor player)
        {
            if (player == null || IsOccupied || IsDestroyed || m_settings == null) return false;
            if ((player.transform.position - transform.position).sqrMagnitude > m_settings.InteractionRange * m_settings.InteractionRange) return false;
            if (m_rearDoorsOpen.Value && !m_settings.AllowDrivingWithDoorsOpen)
            {
                // Loaded couches have their colliders disabled while secured, so this
                // still refuses entry when a loose couch/player is blocking the doors.
                if (IsDoorwayObstructed()) return false;
                SetRearDoorsServer(false);
            }
            m_driver.Value = player.NetworkObject;
            m_driveInput = Vector2.zero;
            return true;
        }

        [Server]
        public void SetInputServer(PlayerVanInteractor player, Vector2 input)
        {
            if (player == null || m_driver.Value != player.NetworkObject || IsDestroyed) return;
            m_driveInput = Vector2.ClampMagnitude(input, 1f);
            m_lastInputTime = Time.time;
        }

        [Server]
        public void LeaveServer(PlayerVanInteractor player)
        {
            if (player != null && m_driver.Value == player.NetworkObject)
            { m_driver.Value = null; m_driveInput = Vector2.zero; }
        }

        public Transform GetCargoPoint(int slot) => m_cargoPoints != null && slot >= 0 && slot < m_cargoPoints.Length
            ? m_cargoPoints[slot] : null;

        public bool IsPlayerNearRear(Transform player)
        {
            if (player == null || m_settings == null) return false;
            return (player.position - RearDoorInteractionPoint.position).sqrMagnitude <=
                m_settings.DoorInteractionRange * m_settings.DoorInteractionRange;
        }

        [Server]
        public void InteractWithRearServer(PlayerVanInteractor player, PlayerCouchCarrier carrier)
        {
            if (player == null || m_settings == null || IsDestroyed || IsOccupied || !IsPlayerNearRear(player.transform)) return;
            if (!m_rearDoorsOpen.Value)
            {
                SetRearDoorsServer(true);
                return;
            }

            bool doorsReady = Time.time >= m_doorStateChangedServerTime + m_settings.DoorAnimationDuration * 0.8f;
            CouchCarryController carriedCouch = carrier != null ? carrier.GetCarriedCouchServer() : null;
            if (doorsReady && carriedCouch != null)
            {
                if (LoadedCouchCount >= m_loadedCouches.Count)
                {
                    player.ShowVehicleFeedbackServer("Van cargo is full");
                    return;
                }
                if (TryLoadCouchServer(player, carriedCouch)) return;
            }
            if (doorsReady && carriedCouch == null && LoadedCouchCount > 0)
            {
                TryUnloadFirstCouchServer(player);
                return;
            }
            if (!IsDoorwayObstructed()) SetRearDoorsServer(false);
            else player.ShowVehicleFeedbackServer("Rear doorway is obstructed");
        }

        [Server]
        private void SetRearDoorsServer(bool open)
        {
            if (m_rearDoorsOpen.Value == open) return;
            m_rearDoorsOpen.Value = open;
            m_doorStateChangedServerTime = Time.time;
        }

        [Server]
        private bool TryLoadCouchServer(PlayerVanInteractor player, CouchCarryController couch)
        {
            if (couch == null || couch.IsLoaded || LoadedCouchCount >= m_loadedCouches.Count) return false;
            float rangeSquared = m_settings.CouchLoadingRange * m_settings.CouchLoadingRange;
            if ((player.transform.position - CargoLoadingPoint.position).sqrMagnitude > rangeSquared ||
                (couch.transform.position - CargoLoadingPoint.position).sqrMagnitude > rangeSquared) return false;
            int slot = -1;
            for (int index = 0; index < m_loadedCouches.Count; index++)
                if (m_loadedCouches[index] == null) { slot = index; break; }
            if (slot < 0 || GetCargoPoint(slot) == null ||
                !couch.LoadIntoVanServer(this, slot, m_settings.CouchLoadingDuration)) return false;
            m_loadedCouches[slot] = couch.NetworkObject;
            return true;
        }

        [Server]
        private bool TryUnloadFirstCouchServer(PlayerVanInteractor player)
        {
            for (int slot = 0; slot < m_loadedCouches.Count; slot++)
            {
                NetworkObject couchObject = m_loadedCouches[slot];
                if (couchObject == null || !couchObject.TryGetComponent(out CouchCarryController couch)) continue;
                if (!TryGetSafeCouchUnloadPosition(couch, slot, out Vector3 position, out Quaternion rotation))
                {
                    player?.ShowVehicleFeedbackServer("Clear the area behind the van");
                    return false;
                }
                m_loadedCouches[slot] = null;
                couch.UnloadFromVanServer(position, rotation, m_settings.CouchUnloadingDuration);
                return true;
            }
            return false;
        }

        [Server]
        private void RecoverLoadedCouchesServer()
        {
            for (int slot = 0; slot < m_loadedCouches.Count; slot++)
            {
                NetworkObject couchObject = m_loadedCouches[slot];
                if (couchObject == null || !couchObject.TryGetComponent(out CouchCarryController couch)) continue;
                Vector3 position = CargoLoadingPoint.position + transform.right * ((slot % 2 == 0 ? 1f : -1f) * (1.5f + slot * .4f));
                if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f, ~0, QueryTriggerInteraction.Ignore))
                    position.y = ground.point.y + .05f;
                m_loadedCouches[slot] = null;
                couch.UnloadFromVanServer(position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), m_settings.CouchUnloadingDuration);
            }
        }

        private bool TryGetSafeCouchUnloadPosition(CouchCarryController couch, int slot, out Vector3 position, out Quaternion rotation)
        {
            Vector3 outward = CargoLoadingPoint.position - transform.position;
            outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            if (outward.sqrMagnitude < 0.1f) outward = -transform.forward;
            position = CargoLoadingPoint.position + outward * (1.25f + slot * .35f);
            if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f, ~0, QueryTriggerInteraction.Ignore))
                position.y = ground.point.y + .05f;
            rotation = Quaternion.LookRotation(transform.right, Vector3.up);
            int count = Physics.OverlapBoxNonAlloc(position + Vector3.up * .45f, new Vector3(1.1f, .45f, .5f),
                m_clearanceHits, rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider hit = m_clearanceHits[index];
                if (hit == null || hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(couch.transform)) continue;
                return false;
            }
            return true;
        }

        private bool IsDoorwayObstructed()
        {
            Vector3 outward = CargoLoadingPoint.position - transform.position;
            float distance = outward.magnitude;
            if (distance < 0.1f) return false;
            Vector3 centre = CargoLoadingPoint.position - outward.normalized * Mathf.Min(.55f, distance * .4f);
            int count = Physics.OverlapBoxNonAlloc(centre, new Vector3(.85f, .9f, .35f), m_clearanceHits,
                transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                Collider hit = m_clearanceHits[index];
                if (hit != null && !hit.transform.IsChildOf(transform)) return true;
            }
            return false;
        }

        [Server]
        public void ApplyBulletDamageServer(float rawDamage)
        {
            if (rawDamage > 0f && !IsDestroyed) ApplyDamageServer(rawDamage * m_settings.BulletDamageMultiplier);
        }

        [Server]
        public void RefuelServer(float amount) => m_fuel.Value = Mathf.Clamp(m_fuel.Value + Mathf.Max(0f, amount), 0f, m_settings.MaxFuel);

        [Server]
        private void ApplyDamageServer(float damage)
        {
            m_health.Value = Mathf.Max(0f, m_health.Value - Mathf.Max(0f, damage));
            if (m_health.Value > 0f) return;
            m_destroyed.Value = true;
            m_driveInput = Vector2.zero;
            RecoverLoadedCouchesServer();
            NetworkObject previousDriver = m_driver.Value;
            m_driver.Value = null;
            if (previousDriver != null && previousDriver.TryGetComponent(out PlayerVanInteractor interactor)) interactor.ForceExitServer(this);
            SetDestroyedObserversRpc();
        }

        [ObserversRpc(RunLocally = true)]
        private void SetDestroyedObserversRpc()
        {
            if (m_runtimeBodyMaterial != null) m_runtimeBodyMaterial.color = new Color(0.18f, 0.18f, 0.18f);
        }

        private void OnHealthChanged(float previous, float next, bool asServer)
        {
            if (m_runtimeBodyMaterial == null || m_settings == null || m_destroyed.Value) return;
            float criticalProgress = Mathf.InverseLerp(
                m_settings.CriticalHealthThreshold, 0f, next / m_settings.MaxHealth);
            m_runtimeBodyMaterial.color = Color.Lerp(m_originalBodyColour, new Color(.32f, .26f, .22f), criticalProgress);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServerInitialized || IsDestroyed || m_settings == null ||
                collision.contactCount == 0)
                return;

            PlayerHealth player = collision.collider.GetComponentInParent<PlayerHealth>();
            ThiefHealth thief = collision.collider.GetComponentInParent<ThiefHealth>();
            if (player != null || thief != null)
            {
                ApplyCharacterImpactServer(collision, player, thief);
                return;
            }

            if (Time.time < m_nextCollisionDamageTime || IsGroundSurface(collision.collider))
                return;

            float severity = GetStrongestObstacleImpactSpeed(collision);
            if (severity < m_settings.MinimumCollisionDamageSpeed) return;
            float damage = Mathf.Min(
                (severity - m_settings.MinimumCollisionDamageSpeed) *
                m_settings.CollisionDamageMultiplier,
                m_settings.MaximumCollisionDamage);
            if (damage <= 0f) return;
            m_nextCollisionDamageTime = Time.time + m_settings.CollisionDamageCooldown;
            ApplyDamageServer(damage);
        }

        [Server]
        private void ApplyCharacterImpactServer(
            Collision collision,
            PlayerHealth player,
            ThiefHealth thief)
        {
            Object victim = player != null ? (Object)player : thief;
            if (victim == null) return;

            int victimId = victim.GetInstanceID();
            if (m_nextCharacterImpactTime.TryGetValue(victimId, out float nextImpactTime) &&
                Time.time < nextImpactTime)
                return;

            Vector3 vanPlanarVelocity = Vector3.ProjectOnPlane(
                m_rigidbody.linearVelocity, Vector3.up);
            float impactSpeed = vanPlanarVelocity.magnitude;
            if (impactSpeed < 0.1f) return;

            m_nextCharacterImpactTime[victimId] =
                Time.time + m_settings.CollisionDamageCooldown;
            float damage = Mathf.Max(0f,
                impactSpeed - m_settings.MinimumCharacterImpactSpeed) *
                m_settings.CharacterImpactDamageMultiplier;
            float fullSpeed = Mathf.Max(
                m_settings.MinimumCharacterImpactSpeed + 0.1f,
                m_settings.MaxForwardSpeed / 3.6f);
            float impactScale = Mathf.Lerp(0.2f, 1.5f,
                Mathf.InverseLerp(0f, fullSpeed, impactSpeed));
            float knockback = m_settings.CharacterKnockbackForce * impactScale;
            float upwardKnockback = m_settings.CharacterKnockbackUpwardForce * impactScale;
            Transform victimTransform = player != null ? player.transform : thief.transform;
            Vector3 direction = vanPlanarVelocity.sqrMagnitude > 0.01f
                ? vanPlanarVelocity.normalized
                : (victimTransform.position - transform.position).normalized;

            if (player != null)
                player.ApplyVehicleImpactServer(damage, direction, knockback, upwardKnockback);
            else
                thief.ApplyVehicleImpactServer(damage, direction, knockback, upwardKnockback);
        }

        private static float GetStrongestObstacleImpactSpeed(Collision collision)
        {
            float strongestImpact = 0f;
            Vector3 relativeVelocity = collision.relativeVelocity;
            for (int index = 0; index < collision.contactCount; index++)
            {
                Vector3 normal = collision.GetContact(index).normal;
                // Ground/slope normals point mostly upward. Only the velocity driven
                // into a substantially lateral face represents an obstacle crash.
                if (Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.65f) continue;
                strongestImpact = Mathf.Max(strongestImpact,
                    Mathf.Abs(Vector3.Dot(relativeVelocity, normal)));
            }
            return strongestImpact;
        }

        private static bool IsGroundSurface(Collider collider)
        {
            if (collider == null) return false;
            if (collider is TerrainCollider ||
                collider.GetComponentInParent<Terrain>() != null ||
                collider.GetComponentInParent<GeneratedTerrainMesh>() != null)
                return true;

            // Generated and authored road pieces currently share the Default layer,
            // so retain a conservative fallback for their established naming scheme.
            string objectName = collider.name.ToLowerInvariant();
            return objectName.StartsWith("terrain") ||
                   objectName.StartsWith("ground") ||
                   objectName.StartsWith("road_") ||
                   objectName.StartsWith("roadtile") ||
                   objectName.Contains("roadsurface") ||
                   objectName.Contains("roadcollider") ||
                   objectName.StartsWith("sidewalk") ||
                   objectName.StartsWith("driveway");
        }

        public bool TryGetExitPosition(NetworkObject player, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (m_exitPoints != null) foreach (Transform point in m_exitPoints)
            {
                if (point == null) continue;
                Collider[] hits = Physics.OverlapSphere(point.position, m_settings.ExitClearanceRadius, ~0, QueryTriggerInteraction.Ignore);
                bool clear = true;
                foreach (Collider hit in hits) if (!hit.transform.IsChildOf(transform) && (player == null || !hit.transform.IsChildOf(player.transform))) { clear = false; break; }
                if (clear) { position = point.position; return true; }
            }
            position = transform.position + transform.right * 2.2f + Vector3.up * 0.2f;
            return false;
        }

        private void SimulateDriving(float dt)
        {
            Vector3 localVelocity = transform.InverseTransformDirection(m_rigidbody.linearVelocity);
            float forwardSpeed = localVelocity.z;
            float throttle = m_driveInput.y;
            bool powered = IsOccupied && !IsDestroyed && m_fuel.Value > 0f &&
                (!m_rearDoorsOpen.Value || m_settings.AllowDrivingWithDoorsOpen);
            if (powered && Mathf.Abs(throttle) > 0.01f)
            {
                bool braking = (forwardSpeed > 0.5f && throttle < 0f) || (forwardSpeed < -0.5f && throttle > 0f);
                float acceleration = braking ? m_settings.BrakingForce : throttle > 0f ? m_settings.Acceleration : m_settings.ReverseAcceleration;
                float slope = Mathf.Clamp01(Vector3.Dot(transform.forward * Mathf.Sign(throttle), Vector3.up));
                float slopePower = Mathf.Lerp(1f, m_settings.UphillPowerMultiplier, slope);
                m_rigidbody.AddForce(transform.forward * Mathf.Sign(throttle) * acceleration * slopePower, ForceMode.Acceleration);
            }
            else
            {
                Vector3 planar = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up);
                m_rigidbody.AddForce(-planar * m_settings.NaturalDeceleration, ForceMode.Acceleration);
            }

            float maxForward = m_settings.MaxForwardSpeed / 3.6f, maxReverse = m_settings.MaxReverseSpeed / 3.6f;
            localVelocity.z = Mathf.Clamp(localVelocity.z, -maxReverse, maxForward);
            Vector3 lateral = transform.right * localVelocity.x;
            m_rigidbody.AddForce(-lateral * (2f + m_settings.Traction * 10f), ForceMode.Acceleration);
            Vector3 rebuilt = transform.TransformDirection(new Vector3(localVelocity.x, 0f, localVelocity.z));
            m_rigidbody.linearVelocity = new Vector3(rebuilt.x, m_rigidbody.linearVelocity.y, rebuilt.z);

            float speed01 = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / maxForward);
            float steeringScale = Mathf.Lerp(1f, m_settings.HighSpeedSteeringMultiplier, speed01);
            float desiredSteering = m_driveInput.x * m_settings.SteeringSensitivity * steeringScale;
            m_steering = Mathf.MoveTowards(m_steering, desiredSteering, m_settings.SteeringResponseSpeed * m_settings.SteeringSensitivity * dt);
            if (Mathf.Abs(forwardSpeed) > 0.2f)
            {
                float direction = Mathf.Sign(forwardSpeed);
                m_rigidbody.MoveRotation(m_rigidbody.rotation * Quaternion.Euler(0f, m_steering * direction * speed01 * dt, 0f));
            }
            m_rigidbody.AddTorque(Vector3.Cross(transform.up, Vector3.up) * m_settings.AntiRollStrength, ForceMode.Acceleration);
        }

        private void ConsumeFuel(float dt)
        {
            if (!IsOccupied || IsDestroyed || m_fuel.Value <= 0f) return;
            float speed01 = Mathf.Clamp01(SpeedKph / m_settings.MaxForwardSpeed);
            float rate = m_settings.FuelConsumptionIdle + m_settings.FuelConsumptionDriving * speed01 +
                m_settings.AccelerationFuelMultiplier * Mathf.Max(0f, m_driveInput.y);
            m_fuel.Value = Mathf.Max(0f, m_fuel.Value - rate * dt);
        }

        private void UpdateVisuals(float dt)
        {
            if (m_settings == null) return;
            float blend = 1f - Mathf.Exp(-m_settings.VisualSmoothing * dt);
            float doorStep = dt / Mathf.Max(.05f, m_settings.DoorAnimationDuration);
            m_doorOpenProgress = Mathf.MoveTowards(m_doorOpenProgress, m_rearDoorsOpen.Value ? 1f : 0f, doorStep);
            float easedDoor = Mathf.SmoothStep(0f, 1f, m_doorOpenProgress);
            if (m_leftDoorPivot != null)
                m_leftDoorPivot.localRotation = m_leftDoorClosedRotation * Quaternion.Euler(0f, 0f, m_settings.DoorOpenAngle * easedDoor);
            if (m_rightDoorPivot != null)
                m_rightDoorPivot.localRotation = m_rightDoorClosedRotation * Quaternion.Euler(0f, 0f, -m_settings.DoorOpenAngle * easedDoor);
            if (m_visualModel != null)
            {
                float pitch = -m_driveInput.y * m_settings.BodyPitchAmount;
                float roll = -m_driveInput.x * Mathf.Clamp01(SpeedKph / m_settings.MaxForwardSpeed) * m_settings.BodyTiltAmount;
                m_visualModel.localRotation = Quaternion.Slerp(m_visualModel.localRotation, m_visualBaseRotation * Quaternion.Euler(pitch, 0f, roll), blend);
            }
            if (m_wheels == null) return;
            float spin = SpeedKph * m_settings.WheelRotationMultiplier * dt;
            for (int i = 0; i < m_wheels.Length; i++) if (m_wheels[i] != null)
            {
                Quaternion steer = i < 2 ? Quaternion.Euler(0f, m_driveInput.x * m_settings.MaxWheelSteeringAngle, 0f) : Quaternion.identity;
                m_wheels[i].localRotation = m_wheelBaseRotations[i] * steer * Quaternion.Euler(spin, 0f, 0f);
            }
        }
    }
}
