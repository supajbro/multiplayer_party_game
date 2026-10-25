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
    [RequireComponent(typeof(VehicleAudioEmitter))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "CouchGuys.Gameplay.Vehicles", sourceAssembly: "Assembly-CSharp", sourceClassName: "VanController")]
    public sealed class TruckController : NetworkBehaviour
    {
        public const int MaximumSeats = 4;
        public static readonly HashSet<TruckController> ActiveTrucks = new();

        [Header("Configuration")]
        [SerializeField] private TruckSettings m_settings;
        [SerializeField, Min(0.1f)] private float m_hornCooldown = 0.5f;
        [Header("References")]
        [SerializeField] private Rigidbody m_rigidbody;
        [SerializeField] private Transform m_visualModel;
        [SerializeField] private Transform m_cameraTarget;
        [SerializeField] private Transform[] m_seatAnchors;
        [SerializeField] private Vector3 m_seatedRootOffset = new(0f, -.8f, 0f);
        [SerializeField] private Transform[] m_exitPoints;
        [SerializeField] private Transform[] m_shutterSlats;
        [SerializeField] private Transform m_shutterSpace;
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
        private readonly SyncVar<NetworkObject> m_frontPassenger = new();
        private readonly SyncVar<NetworkObject> m_rearPassengerLeft = new();
        private readonly SyncVar<NetworkObject> m_rearPassengerRight = new();
        private readonly SyncVar<bool> m_destroyed = new();
        private readonly SyncVar<bool> m_rearDoorsOpen = new();
        private readonly SyncList<NetworkObject> m_loadedCouches = new();
        private Vector2 m_driveInput;
        private float m_steering;
        private float m_nextCollisionDamageTime;
        private float m_nextHornTime;
        private VehicleAudioEmitter m_audio;
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
        private Vector3 m_groundNormal = Vector3.up;
        private float m_lastGroundContactTime = float.NegativeInfinity;

        public TruckSettings Settings => m_settings;
        public float HornCooldown => Mathf.Max(0.1f, m_hornCooldown);
        public float Fuel => m_fuel.Value; public float Health => m_health.Value;
        public float FuelFraction => m_settings != null ? m_fuel.Value / m_settings.MaxFuel : 0f;
        public float HealthFraction => m_settings != null ? m_health.Value / m_settings.MaxHealth : 0f;
        public bool IsDestroyed => m_destroyed.Value;
        public bool IsOccupied => OccupantCount > 0;
        public bool HasAvailableSeat => !IsDestroyed && OccupantCount < MaximumSeats;
        public NetworkObject Driver => m_driver.Value; public Transform CameraTarget => m_cameraTarget != null ? m_cameraTarget : transform;
        public int OccupantCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < MaximumSeats; index++)
                    if (GetOccupant(index) != null) count++;
                return count;
            }
        }
        public float SpeedKph => m_rigidbody != null ? m_rigidbody.linearVelocity.magnitude * 3.6f : 0f;
        public bool RearDoorsOpen => m_rearDoorsOpen.Value;
        public float RearDoorOpenProgress => m_doorOpenProgress;
        public Transform RearDoorInteractionPoint => m_rearDoorInteractionPoint != null ? m_rearDoorInteractionPoint : transform;
        public Transform CargoLoadingPoint => m_cargoLoadingPoint != null ? m_cargoLoadingPoint : RearDoorInteractionPoint;
        public Vector3 SeatedRootOffset => m_seatedRootOffset;
        public int LoadedCouchCount
        {
            get { int count = 0; for (int i = 0; i < m_loadedCouches.Count; i++) if (m_loadedCouches[i] != null) count++; return count; }
        }

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            m_audio = GetComponent<VehicleAudioEmitter>();
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

        private void OnEnable() => ActiveTrucks.Add(this);
        private void OnDisable() => ActiveTrucks.Remove(this);
        private void OnDestroy()
        {
            m_health.OnChange -= OnHealthChanged;
            if (m_runtimeBodyMaterial != null) Destroy(m_runtimeBodyMaterial);
            if (m_runtimePhysicsMaterial != null) Destroy(m_runtimePhysicsMaterial);
        }

        private void ConfigureArcadeColliderFriction()
        {
            // The placeholder is a flat-bottomed cube rather than a wheeled mesh. Default
            // static friction can exceed the Light Truck's configured acceleration and pin it
            // to the road. Physical contact stays slippery while lateral grip remains under
            // the explicit TruckSettings.Traction calculation below.
            m_runtimePhysicsMaterial = new PhysicsMaterial("Truck Arcade Contact")
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
            m_nextHornTime = 0f;
            m_nextCharacterImpactTime.Clear();
            m_fuel.Value = m_settings.MaxFuel;
            m_health.Value = m_settings.MaxHealth;
            m_destroyed.Value = false;
            m_rearDoorsOpen.Value = false;
            for (int index = 0; index < MaximumSeats; index++) SetOccupant(index, null);
            m_loadedCouches.Clear();
            int capacity = Mathf.Min(m_settings.CargoCapacity, m_cargoPoints != null ? m_cargoPoints.Length : 0);
            for (int index = 0; index < capacity; index++) m_loadedCouches.Add(null);
            m_rigidbody.isKinematic = false;
        }

        public override void OnStopServer()
        {
            ReleaseAllOccupantsServer();
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) m_rigidbody.isKinematic = true;
        }

        public override void OnStopClient()
        {
            m_audio.StopLocal();
            base.OnStopClient();
        }

        [Server]
        internal void HornServer(PlayerTruckInteractor player)
        {
            if (player == null || m_driver.Value != player.NetworkObject || IsDestroyed ||
                Time.time < m_nextHornTime) return;
            m_nextHornTime = Time.time + HornCooldown;
            PlayHornObserversRpc();
        }

        [ObserversRpc]
        private void PlayHornObserversRpc() => m_audio.PlayHorn();

        [Server]
        internal void PlaySplatServer(Vector3 position) => PlaySplatObserversRpc(position);

        [ObserversRpc]
        private void PlaySplatObserversRpc(Vector3 position) => m_audio.PlaySplat(position);

        private void FixedUpdate()
        {
            if (!IsServerInitialized || m_settings == null || m_rigidbody.isKinematic) return;
            if (Time.time - m_lastInputTime > 0.35f) m_driveInput = Vector2.zero;
            SimulateDriving(Time.fixedDeltaTime);
            ConsumeFuel(Time.fixedDeltaTime);
        }

        private void LateUpdate() => UpdateVisuals(Time.deltaTime);

        [Server]
        public bool TryEnterServer(PlayerTruckInteractor player, out int seatIndex)
        {
            seatIndex = -1;
            if (player == null || !HasAvailableSeat || m_settings == null ||
                GetSeatIndex(player.NetworkObject) >= 0) return false;
            seatIndex = FindEntrySeat(player.transform.position);
            if (seatIndex < 0) return false;
            if (m_rearDoorsOpen.Value && !m_settings.AllowDrivingWithDoorsOpen)
            {
                // Loaded couches have their colliders disabled while secured, so this
                // still refuses entry when a loose couch/player is blocking the doors.
                if (IsDoorwayObstructed()) return false;
                SetRearDoorsServer(false);
            }
            SetOccupant(seatIndex, player.NetworkObject);
            if (seatIndex == 0) m_driveInput = Vector2.zero;
            return true;
        }

        [Server]
        public void SetInputServer(PlayerTruckInteractor player, Vector2 input)
        {
            if (player == null || m_driver.Value != player.NetworkObject || IsDestroyed) return;
            m_driveInput = Vector2.ClampMagnitude(input, 1f);
            m_lastInputTime = Time.time;
        }

        [Server]
        public void LeaveServer(PlayerTruckInteractor player)
        {
            if (player == null) return;
            int seatIndex = GetSeatIndex(player.NetworkObject);
            if (seatIndex < 0) return;
            SetOccupant(seatIndex, null);
            if (seatIndex == 0) m_driveInput = Vector2.zero;
        }

        public Transform GetSeatAnchor(int seatIndex) =>
            m_seatAnchors != null && seatIndex >= 0 && seatIndex < m_seatAnchors.Length
                ? m_seatAnchors[seatIndex]
                : null;

        public Transform GetDoorPoint(int seatIndex) => m_exitPoints != null && seatIndex >= 0 &&
            seatIndex < m_exitPoints.Length ? m_exitPoints[seatIndex] : null;

        public int FindEntrySeat(Vector3 position)
        {
            if (m_settings == null || IsDestroyed) return -1;
            int nearest = -1;
            float closest = Mathf.Min(1.1f, m_settings.InteractionRange);
            closest *= closest;
            // Choose the actual nearest door first; an occupied door never redirects to another seat.
            for (int index = 0; index < MaximumSeats; index++)
            {
                Transform door = GetDoorPoint(index);
                if (door == null) continue;
                float distance = (position - door.position).sqrMagnitude;
                if (distance > closest) continue;
                closest = distance;
                nearest = index;
            }
            return nearest >= 0 && GetOccupant(nearest) == null ? nearest : -1;
        }

        public NetworkObject GetOccupant(int seatIndex) => seatIndex switch
        {
            0 => m_driver.Value,
            1 => m_frontPassenger.Value,
            2 => m_rearPassengerLeft.Value,
            3 => m_rearPassengerRight.Value,
            _ => null
        };

        private int GetSeatIndex(NetworkObject player)
        {
            if (player == null) return -1;
            for (int index = 0; index < MaximumSeats; index++)
                if (GetOccupant(index) == player) return index;
            return -1;
        }

        [Server]
        private void SetOccupant(int seatIndex, NetworkObject occupant)
        {
            switch (seatIndex)
            {
                case 0: m_driver.Value = occupant; break;
                case 1: m_frontPassenger.Value = occupant; break;
                case 2: m_rearPassengerLeft.Value = occupant; break;
                case 3: m_rearPassengerRight.Value = occupant; break;
            }
        }

        public Transform GetCargoPoint(int slot) => m_cargoPoints != null && slot >= 0 && slot < m_cargoPoints.Length
            ? m_cargoPoints[slot] : null;

        internal bool IsCouchInLoadingRange(CouchCarryController couch)
        {
            if (couch == null || m_settings == null) return false;
            Vector3 nearest = couch.transform.TransformPoint(couch.CargoBounds.ClosestPoint(
                couch.transform.InverseTransformPoint(CargoLoadingPoint.position)));
            return (nearest - CargoLoadingPoint.position).sqrMagnitude <= m_settings.CouchLoadingRange * m_settings.CouchLoadingRange;
        }

        public bool IsPlayerNearRear(Transform player)
        {
            if (player == null || m_settings == null) return false;
            return (player.position - RearDoorInteractionPoint.position).sqrMagnitude <=
                m_settings.DoorInteractionRange * m_settings.DoorInteractionRange;
        }

        [Server]
        public void InteractWithRearServer(PlayerTruckInteractor player, PlayerCouchCarrier carrier)
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
                    player.ShowVehicleFeedbackServer("Truck cargo is full");
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
        private bool TryLoadCouchServer(PlayerTruckInteractor player, CouchCarryController couch)
        {
            if (couch == null || couch.IsLoaded || !couch.IsPickupAvailable || LoadedCouchCount >= m_loadedCouches.Count) return false;
            float rangeSquared = m_settings.CouchLoadingRange * m_settings.CouchLoadingRange;
            if ((player.transform.position - CargoLoadingPoint.position).sqrMagnitude > rangeSquared ||
                !IsCouchInLoadingRange(couch)) return false;
            int slot = -1;
            for (int index = 0; index < m_loadedCouches.Count; index++)
                if (m_loadedCouches[index] == null) { slot = index; break; }
            if (slot < 0 || GetCargoPoint(slot) == null ||
                !couch.LoadIntoTruckServer(this, slot, m_settings.CouchLoadingDuration)) return false;
            m_loadedCouches[slot] = couch.NetworkObject;
            return true;
        }

        [Server]
        private bool TryUnloadFirstCouchServer(PlayerTruckInteractor player)
        {
            for (int slot = 0; slot < m_loadedCouches.Count; slot++)
            {
                NetworkObject couchObject = m_loadedCouches[slot];
                if (couchObject == null || !couchObject.TryGetComponent(out CouchCarryController couch)) continue;
                if (couch.IsCargoTransitioning) return false;
                GetCouchUnloadPose(couch, slot, out Vector3 position, out Quaternion rotation);
                m_loadedCouches[slot] = null;
                couch.UnloadFromTruckServer(position, rotation, m_settings.CouchUnloadingDuration);
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
                GetCouchUnloadPose(couch, slot, out Vector3 position, out Quaternion rotation);
                m_loadedCouches[slot] = null;
                couch.UnloadFromTruckServer(position, rotation, m_settings.CouchUnloadingDuration);
            }
        }

        internal void GetCouchUnloadPose(CouchCarryController couch, int slot, out Vector3 position, out Quaternion rotation)
        {
            Physics.SyncTransforms();
            Vector3 outward = Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized;
            if (outward.sqrMagnitude < .1f) outward = Vector3.back;
            Vector3 right = Vector3.Cross(outward, Vector3.up).normalized;
            Bounds bounds = couch.CargoBounds;
            Vector3 scale = couch.transform.lossyScale;
            Vector3 extents = Vector3.Scale(bounds.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Vector3 centreOffset = Vector3.Scale(bounds.center, scale);
            float spacing = Mathf.Max(extents.x, extents.z) * 2f + .25f;
            Vector3 rear = CargoLoadingPoint.position + outward * (Mathf.Max(extents.x, extents.z) + .75f + slot * .35f);
            rotation = Quaternion.LookRotation(right, Vector3.up);
            position = rear + Vector3.up * .1f;
            bool hasGroundedFallback = false;
            // Rear first, then a bounded search around the rear/side of the truck.
            for (int ring = 0; ring <= 5; ring++)
            {
                int samples = ring == 0 ? 1 : ring * 8;
                for (int sample = 0; sample < samples; sample++)
                {
                    float angle = sample * Mathf.PI * 2f / samples;
                    Vector3 candidate = rear + (outward * Mathf.Cos(angle) + right * Mathf.Sin(angle)) * (ring * spacing);
                    RaycastHit[] groundHits = Physics.RaycastAll(candidate + Vector3.up * 8f, Vector3.down,
                        40f, ~0, QueryTriggerInteraction.Ignore);
                    System.Array.Sort(groundHits, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (RaycastHit ground in groundHits)
                    {
                        if (ground.collider.transform.IsChildOf(transform) ||
                            ground.collider.GetComponentInParent<CouchCarryController>() != null ||
                            ground.collider.GetComponentInParent<PlayerTruckInteractor>() != null || ground.normal.y < .65f) continue;
                        for (int turn = 0; turn < 2; turn++)
                        {
                            Quaternion candidateRotation = Quaternion.LookRotation(
                                Vector3.ProjectOnPlane(turn == 0 ? right : outward, ground.normal), ground.normal);
                            Vector3 candidatePosition = ground.point + ground.normal * (extents.y + .06f) - candidateRotation * centreOffset;
                            if (!hasGroundedFallback)
                            {
                                position = candidatePosition;
                                rotation = candidateRotation;
                                hasGroundedFallback = true;
                            }
                            bool blocked = false;
                            // Allocating query deliberately avoids treating a saturated fixed buffer as clear.
                            foreach (Collider hit in Physics.OverlapBox(candidatePosition + candidateRotation * centreOffset,
                                extents + Vector3.one * .02f, candidateRotation, ~0, QueryTriggerInteraction.Ignore))
                            {
                                if (hit.transform.IsChildOf(couch.transform)) continue;
                                blocked = true;
                                break;
                            }
                            if (blocked) continue;
                            position = candidatePosition;
                            rotation = candidateRotation;
                            return;
                        }
                        break;
                    }
                }
            }
            // Never refuse an unload. The couch ghosts overlapping obstacles at the
            // grounded fallback until carried clear, then restores those collisions.
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
            ReleaseAllOccupantsServer();
            SetDestroyedObserversRpc();
        }

        [Server]
        private void ReleaseAllOccupantsServer()
        {
            NetworkObject[] occupants = new NetworkObject[MaximumSeats];
            for (int index = 0; index < MaximumSeats; index++)
            {
                occupants[index] = GetOccupant(index);
            }
            m_driveInput = Vector2.zero;
            for (int index = 0; index < occupants.Length; index++)
                if (occupants[index] != null && occupants[index].TryGetComponent(out PlayerTruckInteractor interactor))
                    interactor.ForceExitServer(this);
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

            var pedestrian = collision.collider.GetComponentInParent<CouchGuys.Gameplay.Pedestrians.PedestrianController>();
            if (pedestrian != null)
            {
                if (pedestrian.ApplyVehicleImpactServer(Mathf.Max(collision.relativeVelocity.magnitude,
                    Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up).magnitude)))
                    PlaySplatServer(collision.GetContact(0).point);
                return;
            }

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

        private void OnCollisionStay(Collision collision)
        {
            Vector3 bestNormal = Vector3.zero;
            float bestUp = 0.25f;
            for (int index = 0; index < collision.contactCount; index++)
            {
                Vector3 normal = collision.GetContact(index).normal;
                float up = Vector3.Dot(normal, Vector3.up);
                if (up > bestUp)
                {
                    bestUp = up;
                    bestNormal = normal;
                }
            }

            if (bestNormal.sqrMagnitude > 0.5f)
            {
                m_groundNormal = bestNormal.normalized;
                m_lastGroundContactTime = Time.time;
            }
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

            Vector3 truckPlanarVelocity = Vector3.ProjectOnPlane(
                m_rigidbody.linearVelocity, Vector3.up);
            float impactSpeed = truckPlanarVelocity.magnitude;
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
            Vector3 direction = truckPlanarVelocity.sqrMagnitude > 0.01f
                ? truckPlanarVelocity.normalized
                : (victimTransform.position - transform.position).normalized;

            if (player != null)
                player.ApplyVehicleImpactServer(damage, direction, knockback, upwardKnockback);
            else
            {
                bool alive = thief.IsAlive;
                thief.ApplyVehicleImpactServer(damage, direction, knockback, upwardKnockback);
                if (alive) PlaySplatServer(collision.GetContact(0).point);
            }
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
            position = default;
            rotation = Quaternion.identity;
            Transform door = GetDoorPoint(GetSeatIndex(player));
            if (door == null) return false;
            Vector3 outward = Vector3.ProjectOnPlane(door.forward, Vector3.up).normalized;
            rotation = Quaternion.LookRotation(outward, Vector3.up);
            CharacterController capsule = player != null ? player.GetComponent<CharacterController>() : null;
            float radius = capsule != null ? capsule.radius * Mathf.Max(player.transform.lossyScale.x, player.transform.lossyScale.z) : .35f;
            float height = capsule != null ? Mathf.Max(radius * 2f, capsule.height * player.transform.lossyScale.y) : 1.8f;
            Vector3 centre = capsule != null ? Vector3.Scale(capsule.center, player.transform.lossyScale) : Vector3.up * .9f;
            for (int step = 0; step < 5; step++)
            {
                Vector3 candidate = door.position + outward * (step * .3f);
                RaycastHit[] groundHits = Physics.RaycastAll(candidate + Vector3.up * 2f, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(groundHits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit ground in groundHits)
                {
                    if (ground.collider.transform.IsChildOf(transform) ||
                        (player != null && ground.collider.transform.IsChildOf(player.transform)) || ground.normal.y < .65f) continue;
                    candidate.y = ground.point.y + .04f + height * .5f - centre.y;
                    Vector3 middle = candidate + rotation * centre;
                    Collider[] hits = Physics.OverlapCapsule(middle + Vector3.up * (height * .5f - radius),
                        middle - Vector3.up * (height * .5f - radius), radius + .01f, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    foreach (Collider hit in hits)
                    {
                        if (player != null && hit.transform.IsChildOf(player.transform)) continue;
                        blocked = true;
                        break;
                    }
                    if (!blocked) { position = candidate; return true; }
                    break;
                }
            }
            return false;
        }

        private void SimulateDriving(float dt)
        {
            Vector3 localVelocity = transform.InverseTransformDirection(m_rigidbody.linearVelocity);
            float forwardSpeed = localVelocity.z;
            float throttle = m_driveInput.y;
            bool powered = m_driver.Value != null && !IsDestroyed && m_fuel.Value > 0f &&
                (!m_rearDoorsOpen.Value || m_settings.AllowDrivingWithDoorsOpen);
            if (powered && Mathf.Abs(throttle) > 0.01f)
            {
                bool braking = (forwardSpeed > 0.5f && throttle < 0f) || (forwardSpeed < -0.5f && throttle > 0f);
                float acceleration = braking ? m_settings.BrakingForce : throttle > 0f ? m_settings.Acceleration : m_settings.ReverseAcceleration;
                Vector3 groundNormal = Time.time - m_lastGroundContactTime <= 0.2f
                    ? m_groundNormal
                    : Vector3.up;
                Vector3 driveDirection = Vector3.ProjectOnPlane(
                    transform.forward * Mathf.Sign(throttle), groundNormal).normalized;
                if (driveDirection.sqrMagnitude < 0.5f)
                    driveDirection = transform.forward * Mathf.Sign(throttle);
                float uphill = Mathf.Clamp01(Vector3.Dot(driveDirection, Vector3.up) /
                                               Mathf.Sin(30f * Mathf.Deg2Rad));
                float slopePower = braking
                    ? 1f
                    : Mathf.Lerp(1f, m_settings.UphillPowerMultiplier, uphill);
                m_rigidbody.AddForce(driveDirection * acceleration * slopePower,
                    ForceMode.Acceleration);
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
            if (m_driver.Value == null || IsDestroyed || m_fuel.Value <= 0f) return;
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
            UpdateShutter(easedDoor);
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

        private void UpdateShutter(float progress)
        {
            if (m_shutterSlats == null || m_shutterSpace == null) return;
            const float bottom = 1.24f, rear = -3.87f, curveStart = 3.48f, radius = .27f, slatHeight = .145f;
            float vertical = curveStart - bottom;
            float arc = radius * Mathf.PI * .5f;
            float travel = vertical + arc + .25f - slatHeight * .5f;
            for (int i = 0; i < m_shutterSlats.Length; i++)
            {
                Transform slat = m_shutterSlats[i];
                if (slat == null) continue;
                float distance = (i + .5f) * slatHeight + progress * travel;
                float x = rear, z = bottom + distance, turn = 0f;
                if (distance > vertical && distance <= vertical + arc)
                {
                    turn = (distance - vertical) / radius;
                    x = rear + radius - radius * Mathf.Cos(turn);
                    z = curveStart + radius * Mathf.Sin(turn);
                }
                else if (distance > vertical + arc)
                {
                    turn = Mathf.PI * .5f;
                    x = rear + radius + distance - vertical - arc;
                    z = curveStart + radius;
                }
                // Authored shutter space uses Blender metres (+X front, +Z up).
                slat.SetPositionAndRotation(m_shutterSpace.TransformPoint(new Vector3(0f, z, x)),
                    m_shutterSpace.rotation * Quaternion.Euler(-turn * Mathf.Rad2Deg, 0f, 0f));
            }
        }
    }
}
