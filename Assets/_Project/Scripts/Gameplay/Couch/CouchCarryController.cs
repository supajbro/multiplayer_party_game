using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Component.Transforming;
using UnityEngine;
using System.Collections;
using CouchGuys.Gameplay.Vehicles;

namespace CouchGuys.Gameplay.Couch
{
    /// <summary>Server-only carrier contract shared by players and thieves.</summary>
    public interface ICouchCarrierServer
    {
        NetworkObject CarrierNetworkObject { get; }
        bool IsCarrierAvailable { get; }
        Vector3 CalculateDesiredCarryPosition(Vector3 couchCentre);
        Vector3 GetServerMovementIntent();
        void SetCarriedCouchServer(CouchCarryController couch, int pointIndex);
        void ClearCarriedCouchServer(CouchCarryController couch, int pointIndex);
    }

    /// <summary>
    /// Runs the couch's single authoritative Rigidbody simulation on the server.
    /// Every occupied point contributes an independent spring force.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class CouchCarryController : NetworkBehaviour
    {
        public const int MaximumCarryPoints = 4;

        [Header("References")]
        [SerializeField] private Rigidbody m_rigidbody;
        [SerializeField] private CouchCarryPoint[] m_carryPoints = new CouchCarryPoint[MaximumCarryPoints];
        [SerializeField] private Transform m_modelRoot;
        [SerializeField] private GameObject[] m_modelPrefabs;
        [SerializeField] private CouchCarrySettings m_settings;

        // The five designer controls live in CouchCarrySettings. The remaining
        // physical response is deliberately kept internal so each setting has one job.
        private const float CouchWeight = 35f;
        private const float CarryForce = 500f;
        private const float CarryDamping = 65f;
        private const float MaximumHorizontalForce = 700f;
        private const float RotationInfluence = 0.85f;
        private const float MinimumSlopeAngle = 3f;
        private const float MaximumSlopeAngle = 45f;
        private const float SlopeGravityInfluence = 0.75f;
        private const float SlopeRollingResistance = 0.04f;
        private const float LiftForce = 450f;
        private const float LiftDamping = 55f;
        private const float MaximumLiftForce = 350f;
        private const float SingleCarrierEfficiency = 0.35f;
        private const float MaximumCarrierEfficiency = 1f;
        private const float CarrierCountCurveExponent = 1.55f;
        private const float MinimumConflictEfficiency = 0.45f;
        private const float MaximumMovementAcceleration = 14f;
        private const float MaximumBrakingAcceleration = 8f;
        private const float MaximumReverseAcceleration = 18f;
        private const float SpeedLimitTransitionRate = 5f;
        private const float MaximumAngularVelocity = 9f;
        private const float CarriedUprightStrength = 18f;
        private const float CarriedUprightDamping = 5f;
        private const float MaximumCarriedLeanAngle = 12f;
        private const float MaximumStepHeight = 0.38f;
        private const float StepProbeDistance = 0.42f;
        private const float StepClearance = 0.08f;
        private const float StepLiftAcceleration = 22f;
        private const float MaximumStepUpSpeed = 2.4f;
        private const float FreeMaximumSpeed = 22f;
        private const float MaximumImpactReactionSpeed = 16f;
        private const float ImpactTorqueMultiplier = 0.22f;
        private const float VisualPositionFrequency = 7f;
        private const float VisualRotationFrequency = 5f;
        private const bool ShowForceGizmos = false;
        private const float DebugForceScale = 0.002f;
        private const float DefaultMaxCarryDistance = 1f;
        private static readonly float[] s_defaultMoveSpeeds = { 2.5f, 3.5f, 4.5f, 5.5f };

        private readonly SyncVar<int> m_frontLeftOccupant = new(-1);
        private readonly SyncVar<int> m_frontRightOccupant = new(-1);
        private readonly SyncVar<int> m_rearLeftOccupant = new(-1);
        private readonly SyncVar<int> m_rearRightOccupant = new(-1);
        private readonly SyncVar<int> m_movingCarrierCount = new();
        private readonly SyncVar<float> m_cooperationEfficiency = new(1f);
        private readonly SyncVar<int> m_modelVariantIndex = new(-1);
        private readonly SyncVar<NetworkObject> m_loadedVan = new();
        private readonly SyncVar<int> m_loadedCargoSlot = new(-1);
        private readonly SyncVar<bool> m_depotTransportActive = new();
        private readonly ICouchCarrierServer[] m_serverOccupants = new ICouchCarrierServer[MaximumCarryPoints];
        private readonly Vector3[] m_debugHorizontalForces = new Vector3[MaximumCarryPoints];
        private readonly Vector3[] m_debugLiftForces = new Vector3[MaximumCarryPoints];
        private readonly Vector3[] m_debugMovementIntents = new Vector3[MaximumCarryPoints];
        private Vector3 m_groundNormal = Vector3.up;
        private float m_lastGroundContactTime = float.NegativeInfinity;
        private float m_currentMaximumMovementSpeed;
        private int m_previousCarrierCount;
        private GameObject m_modelInstance;
        private int m_appliedModelVariantIndex = -1;
        private Collider[] m_colliders;
        private readonly RaycastHit[] m_stepHits = new RaycastHit[12];
        private Coroutine m_cargoTransition;
        private Bounds m_cargoBounds;
        private readonly System.Collections.Generic.List<Collider> m_cargoIgnoredColliders = new();
        private PhysicsMaterial m_runtimePhysicsMaterial;
        private Vector3 m_smoothedCarryVelocity;
        private Vector3 m_visualScale = Vector3.one;
        private Vector3 m_visualScaleVelocity;
        private Vector3 m_visualEuler;
        private Vector3 m_visualEulerVelocity;
        private Vector3 m_visualImpulseEuler;
        private Vector3 m_lastVisualPosition;
        private Vector3 m_visualWorldVelocity;
        private bool m_visualPositionInitialized;
        private bool m_wasCarried;
        private bool m_visualWasCarried;
        private Quaternion m_modelBaseRotation;
        private Vector3 m_modelBaseScale = Vector3.one;

        public Rigidbody CouchRigidbody => m_rigidbody;
        public float Weight => CouchWeight;
        public float MaxCarryDistance => m_settings != null
            ? m_settings.MaxCarryDistance
            : DefaultMaxCarryDistance;
        public int ActiveCarrierCount => CountActiveCarriers();
        public int MovingCarrierCount => m_movingCarrierCount.Value;
        public float CurrentCooperationEfficiency => m_cooperationEfficiency.Value;
        public Vector3 CurrentVelocity => IsServerInitialized && m_rigidbody != null
            ? m_rigidbody.linearVelocity
            : m_visualWorldVelocity;
        public int ModelVariantIndex => m_modelVariantIndex.Value;
        public bool IsLoaded => m_loadedVan.Value != null;
        public NetworkObject LoadedVanObject => m_loadedVan.Value;
        public int LoadedCargoSlot => m_loadedCargoSlot.Value;
        public bool IsPickupAvailable => !m_depotTransportActive.Value && m_cargoTransition == null;
        internal bool IsCargoTransitioning => m_cargoTransition != null;
        internal Bounds CargoBounds => m_cargoBounds;
        // Delivery completion still requires a player carrier; thieves contribute to
        // physics but cannot accidentally complete the job for the party.
        internal int ServerCarrierCount => IsServerInitialized ? CountPlayerCarriers() : 0;

        internal int ValidServerCarrierCount
        {
            get
            {
                if (!IsServerInitialized) return 0;
                int count = 0;
                for (int index = 0; index < MaximumCarryPoints; index++)
                {
                    ICouchCarrierServer carrier = m_serverOccupants[index];
                    if (carrier != null && carrier.IsCarrierAvailable) count++;
                }
                return count;
            }
        }

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            m_colliders = GetComponentsInChildren<Collider>(true);
            // Capture geometry before cargo disables the colliders (disabled bounds are empty).
            bool foundCargoBounds = false;
            foreach (Collider collider in m_colliders)
            {
                if (!collider.enabled || collider.isTrigger) continue;
                BoxCollider box = collider as BoxCollider;
                Bounds geometry = box != null ? new Bounds(box.center, box.size) : collider.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = geometry.center + Vector3.Scale(geometry.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 local = transform.InverseTransformPoint(box != null ? box.transform.TransformPoint(point) : point);
                    if (!foundCargoBounds) { m_cargoBounds = new Bounds(local, Vector3.zero); foundCargoBounds = true; }
                    else m_cargoBounds.Encapsulate(local);
                }
            }
            if (!foundCargoBounds) m_cargoBounds = new Bounds(Vector3.up * .45f, new Vector3(2.1f, .9f, .9f));
            if (m_modelRoot != null)
            {
                m_modelBaseRotation = m_modelRoot.localRotation;
                m_modelBaseScale = m_modelRoot.localScale;
                m_visualScale = m_modelBaseScale;
            }
            m_modelVariantIndex.OnChange += OnModelVariantChanged;
            m_loadedVan.OnChange += OnLoadedVanChanged;
            ApplyWeightAndStability();
            ApplyRuntimePhysicsConfiguration();
            ConfigureColliderMaterials();
            if (m_settings == null)
            {
                Debug.LogError("CouchCarryController requires one CouchCarrySettings asset.", this);
            }
            // No peer simulates before FishNet assigns authority. The server switches
            // this body back to dynamic in OnStartServer.
            m_rigidbody.isKinematic = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_rigidbody.isKinematic = false;
            ReleaseAllOccupantsServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyModelVariant(m_modelVariantIndex.Value);
            ApplyLoadedStateImmediately();
            if (!IsServerInitialized)
            {
                m_rigidbody.isKinematic = true;
            }
        }

        public override void OnStopServer()
        {
            ReleaseAllOccupantsServer();
            base.OnStopServer();
        }

        private void OnDestroy()
        {
            m_modelVariantIndex.OnChange -= OnModelVariantChanged;
            m_loadedVan.OnChange -= OnLoadedVanChanged;
            if (m_runtimePhysicsMaterial != null)
            {
                Destroy(m_runtimePhysicsMaterial);
            }
        }

        /// <summary>Selects the visual used by this couch. Carry points and physics stay on the shared root.</summary>
        [Server]
        public void SelectRandomModelVariantServer()
        {
            if (!IsServerInitialized || m_modelPrefabs == null || m_modelPrefabs.Length == 0)
            {
                return;
            }

            m_modelVariantIndex.Value = Random.Range(0, m_modelPrefabs.Length);
            ApplyModelVariant(m_modelVariantIndex.Value);
        }

        private void OnModelVariantChanged(int previousIndex, int nextIndex, bool asServer)
        {
            ApplyModelVariant(nextIndex);
        }

        private void ApplyModelVariant(int selectedIndex)
        {
            if (m_modelPrefabs == null || selectedIndex < 0 || selectedIndex >= m_modelPrefabs.Length ||
                m_modelPrefabs[selectedIndex] == null ||
                (m_appliedModelVariantIndex == selectedIndex && m_modelInstance != null))
            {
                return;
            }

            if (m_modelInstance != null)
            {
                Destroy(m_modelInstance);
            }

            Transform modelRoot = m_modelRoot != null ? m_modelRoot : transform;
            m_modelInstance = Instantiate(m_modelPrefabs[selectedIndex], modelRoot, false);
            m_modelInstance.name = m_modelPrefabs[selectedIndex].name;
            m_appliedModelVariantIndex = selectedIndex;
        }

        private void FixedUpdate()
        {
            if (IsServerInitialized) UpdateCargoCollisionRecovery();
            if (!IsServerInitialized || m_rigidbody == null || m_rigidbody.isKinematic)
            {
                return;
            }

            int carrierCount = CountServerCarriers();
            UpdateCarriedPhysicsState(carrierCount > 0);
            Vector3 combinedMovementIntent = CalculateCombinedMovementIntent(
                out float totalIntentMagnitude,
                out int movingCarrierCount);
            m_movingCarrierCount.Value = movingCarrierCount;
            float cooperationEfficiency = CalculateCooperationEfficiency(
                movingCarrierCount,
                combinedMovementIntent,
                totalIntentMagnitude);
            m_cooperationEfficiency.Value = cooperationEfficiency;
            float totalCarrierEfficiency = CalculateTotalCarrierEfficiency(carrierCount);
            float perCarrierEfficiency = carrierCount > 0 ? totalCarrierEfficiency / carrierCount : 0f;
            float cooperationForceScale = Mathf.Lerp(
                MinimumConflictEfficiency,
                1f,
                cooperationEfficiency);
            float configuredSpeed = carrierCount > 0 ? GetConfiguredMoveSpeed(carrierCount) : 0f;
            float weightForce = CouchWeight * Mathf.Abs(Physics.gravity.y);
            // Static support remains below gravity so the couch cannot float without
            // a positive grip-height error contributing spring lift.
            float totalSupportFraction = Mathf.Min(0.9f, totalCarrierEfficiency);
            float supportPerCarrier = carrierCount > 0
                ? weightForce * totalSupportFraction / carrierCount
                : 0f;

            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                ClearDebugForces(index);
                ICouchCarrierServer carrier = m_serverOccupants[index];
                CouchCarryPoint carryPoint = GetPoint(index);
                if (carrier == null)
                {
                    if (IsPointOccupied(index))
                    {
                        SetOccupantObjectId(index, -1);
                    }

                    continue;
                }

                if (!carrier.IsCarrierAvailable || carryPoint == null)
                {
                    ReleasePointServer(index, carrier);
                    continue;
                }

                Vector3 pointPosition = carryPoint.transform.position;
                Vector3 targetPosition = carrier.CalculateDesiredCarryPosition(m_rigidbody.worldCenterOfMass);
                Vector3 pointVelocity = m_rigidbody.GetPointVelocity(pointPosition);
                Vector3 movementIntent = carrier.GetServerMovementIntent();

                Vector3 horizontalError = Vector3.ProjectOnPlane(targetPosition - pointPosition, Vector3.up);
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(pointVelocity, Vector3.up);
                Vector3 horizontalForce = CalculateHorizontalCarryForce(
                    horizontalError,
                    horizontalVelocity,
                    movementIntent,
                    configuredSpeed,
                    perCarrierEfficiency,
                    cooperationForceScale);

                float heightError = targetPosition.y - pointPosition.y;
                float liftForce = supportPerCarrier + Mathf.Max(0f, heightError) * LiftForce -
                    pointVelocity.y * LiftDamping;
                liftForce = Mathf.Clamp(
                    liftForce,
                    0f,
                    MaximumLiftForce);
                Vector3 verticalForce = Vector3.up * liftForce;

                // Translation and rotation are applied explicitly. This preserves the
                // physical torque of an off-centre grip while ensuring that the same
                // world-space movement intent always contributes the same translation,
                // regardless of which corner is occupied or how the couch is rotated.
                m_rigidbody.AddForce(horizontalForce, ForceMode.Force);
                m_rigidbody.AddTorque(CalculateHorizontalCarryTorque(
                    m_rigidbody.worldCenterOfMass,
                    pointPosition,
                    horizontalForce), ForceMode.Force);
                m_rigidbody.AddForceAtPosition(verticalForce, pointPosition, ForceMode.Force);

                m_debugHorizontalForces[index] = horizontalForce;
                m_debugLiftForces[index] = verticalForce;
                m_debugMovementIntents[index] = movementIntent;
            }

            if (m_previousCarrierCount == 0 && carrierCount > 0)
            {
                // Acceleration still starts from the current Rigidbody velocity; only
                // initialise the cap so attaching never snaps an already-moving couch.
                m_currentMaximumMovementSpeed = configuredSpeed;
            }

            m_currentMaximumMovementSpeed = Mathf.MoveTowards(
                m_currentMaximumMovementSpeed,
                configuredSpeed,
                SpeedLimitTransitionRate * Time.fixedDeltaTime);
            m_previousCarrierCount = carrierCount;

            Vector3 averageIntent = movingCarrierCount > 0
                ? combinedMovementIntent / movingCarrierCount
                : Vector3.zero;
            Vector3 targetCarryVelocity = CalculateDesiredCarryVelocity(
                combinedMovementIntent,
                movingCarrierCount,
                m_currentMaximumMovementSpeed);
            float velocityResponse = targetCarryVelocity.sqrMagnitude > 0.01f ? 7f : 3.5f;
            m_smoothedCarryVelocity = Vector3.Lerp(
                m_smoothedCarryVelocity,
                targetCarryVelocity,
                1f - Mathf.Exp(-velocityResponse * Time.fixedDeltaTime));
            Vector3 desiredVelocity = m_smoothedCarryVelocity;
            Vector3 horizontalCouchVelocity = Vector3.ProjectOnPlane(
                m_rigidbody.linearVelocity,
                Vector3.up);
            float accelerationLimit = SelectAccelerationLimit(horizontalCouchVelocity, desiredVelocity);
            Vector3 velocityChange = Vector3.MoveTowards(
                horizontalCouchVelocity,
                desiredVelocity,
                accelerationLimit * Time.fixedDeltaTime) - horizontalCouchVelocity;
            Vector3 movementAcceleration = velocityChange / Time.fixedDeltaTime;
            if (carrierCount > 0)
            {
                m_rigidbody.AddForce(movementAcceleration, ForceMode.Acceleration);
                ApplyStepAssistance(averageIntent);
                ApplyCarriedUprightTorque();
            }

            ApplySlopeForces();

            LimitHorizontalVelocity(carrierCount > 0 ? m_currentMaximumMovementSpeed : 0f);
            LimitExtremeFreeMotion(carrierCount == 0);
        }

        private void LateUpdate()
        {
            if (m_modelRoot == null || IsLoaded)
            {
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            bool visuallyCarried = ActiveCarrierCount > 0;
            if (visuallyCarried != m_visualWasCarried)
            {
                m_visualWasCarried = visuallyCarried;
                m_visualImpulseEuler += new Vector3(
                    visuallyCarried ? -4f : 5f,
                    0f,
                    Random.Range(-4f, 4f));
            }
            if (!m_visualPositionInitialized)
            {
                m_lastVisualPosition = transform.position;
                m_visualPositionInitialized = true;
            }

            Vector3 measuredVelocity = (transform.position - m_lastVisualPosition) / dt;
            m_lastVisualPosition = transform.position;
            Vector3 acceleration = (measuredVelocity - m_visualWorldVelocity) / dt;
            m_visualWorldVelocity = Vector3.Lerp(m_visualWorldVelocity, measuredVelocity,
                1f - Mathf.Exp(-8f * dt));

            Vector3 localVelocity = transform.InverseTransformDirection(m_visualWorldVelocity);
            Vector3 localAcceleration = transform.InverseTransformDirection(acceleration);
            Vector3 targetEuler = new Vector3(
                Mathf.Clamp(-localVelocity.z * 1.1f - localAcceleration.z * 0.08f, -8f, 8f),
                0f,
                Mathf.Clamp(localVelocity.x * 1.25f + localAcceleration.x * 0.09f, -10f, 10f));
            targetEuler += m_visualImpulseEuler;
            m_visualImpulseEuler = Vector3.MoveTowards(m_visualImpulseEuler, Vector3.zero, 45f * dt);
            m_visualEuler = Vector3.SmoothDamp(
                m_visualEuler, targetEuler, ref m_visualEulerVelocity,
                1f / VisualRotationFrequency, Mathf.Infinity, dt);
            m_visualScale = Vector3.SmoothDamp(
                m_visualScale, m_modelBaseScale, ref m_visualScaleVelocity,
                1f / VisualPositionFrequency, Mathf.Infinity, dt);
            m_modelRoot.localRotation = m_modelBaseRotation * Quaternion.Euler(m_visualEuler);
            m_modelRoot.localScale = m_visualScale;
        }

        public float CalculateCarrierMovementSpeedMultiplier()
        {
            float carrierProgress = Mathf.InverseLerp(1f, MaximumCarryPoints, ActiveCarrierCount);
            return Mathf.Lerp(0.65f, 0.95f, carrierProgress);
        }

        public CouchCarryPoint GetPoint(int pointIndex)
        {
            return IsValidPointIndex(pointIndex) && m_carryPoints != null && pointIndex < m_carryPoints.Length
                ? m_carryPoints[pointIndex]
                : null;
        }

        /// <summary>
        /// Prevents an attached carrier from being displaced by the couch it is
        /// controlling. Other players and world objects continue to collide normally.
        /// </summary>
        internal void SetCarrierCollisionIgnored(Collider carrierCollider, bool ignored)
        {
            if (carrierCollider == null || m_colliders == null)
            {
                return;
            }

            for (int index = 0; index < m_colliders.Length; index++)
            {
                Collider couchCollider = m_colliders[index];
                if (couchCollider != null && couchCollider != carrierCollider)
                {
                    Physics.IgnoreCollision(carrierCollider, couchCollider, ignored);
                }
            }
        }

        public bool IsPointOccupied(int pointIndex)
        {
            return GetOccupantObjectId(pointIndex) >= 0;
        }

        public int GetOccupantObjectId(int pointIndex)
        {
            return pointIndex switch
            {
                0 => m_frontLeftOccupant.Value,
                1 => m_frontRightOccupant.Value,
                2 => m_rearLeftOccupant.Value,
                3 => m_rearRightOccupant.Value,
                _ => -1
            };
        }

        internal bool TryGrabPointServer(PlayerCouchCarrier carrier, int pointIndex)
        {
            return TryGrabPointServer((ICouchCarrierServer)carrier, pointIndex);
        }

        internal bool TryGrabPointServer(ICouchCarrierServer carrier, int pointIndex)
        {
            if (!IsServerInitialized || !IsPickupAvailable || IsLoaded || carrier == null || !carrier.IsCarrierAvailable ||
                !IsValidPointIndex(pointIndex) || GetPoint(pointIndex) == null || IsPointOccupied(pointIndex))
            {
                return false;
            }

            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                if (m_serverOccupants[index] == carrier)
                {
                    return false;
                }
            }

            m_serverOccupants[pointIndex] = carrier;
            SetOccupantObjectId(pointIndex, carrier.CarrierNetworkObject.ObjectId);
            carrier.SetCarriedCouchServer(this, pointIndex);
            return true;
        }

        /// <summary>Temporarily hands authoritative motion to the depot conveyor.</summary>
        [Server]
        public void SetDepotTransportActiveServer(bool active)
        {
            if (!IsServerInitialized || m_rigidbody == null) return;
            if (active) ReleaseAllOccupantsServer();
            m_depotTransportActive.Value = active;
            m_rigidbody.linearVelocity = Vector3.zero;
            m_rigidbody.angularVelocity = Vector3.zero;
            m_rigidbody.isKinematic = active;
        }

        internal PlayerCouchCarrier GetFirstHumanCarrierServer(PlayerCouchCarrier excludedCarrier)
        {
            if (!IsServerInitialized)
            {
                return null;
            }

            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                PlayerCouchCarrier carrier = m_serverOccupants[index] as PlayerCouchCarrier;
                if (carrier != null && carrier != excludedCarrier && carrier.Owner.IsValid)
                {
                    return carrier;
                }
            }

            return null;
        }

        internal PlayerCouchCarrier GetPlayerCarrierServer(int preferredPointIndex)
        {
            if (!IsServerInitialized) return null;
            int start = Mathf.Clamp(preferredPointIndex, 0, MaximumCarryPoints - 1);
            for (int offset = 0; offset < MaximumCarryPoints; offset++)
            {
                int index = (start + offset) % MaximumCarryPoints;
                if (m_serverOccupants[index] is PlayerCouchCarrier carrier &&
                    ((ICouchCarrierServer)carrier).IsCarrierAvailable)
                {
                    return carrier;
                }
            }
            return null;
        }

        internal void ReleasePointServer(int pointIndex, PlayerCouchCarrier expectedCarrier)
        {
            ReleasePointServer(pointIndex, (ICouchCarrierServer)expectedCarrier);
        }

        internal void ReleasePointServer(int pointIndex, ICouchCarrierServer expectedCarrier)
        {
            if (!IsServerInitialized || !IsValidPointIndex(pointIndex))
            {
                return;
            }

            ICouchCarrierServer occupant = m_serverOccupants[pointIndex];
            if (occupant == null || (expectedCarrier != null && occupant != expectedCarrier))
            {
                return;
            }

            m_serverOccupants[pointIndex] = null;
            SetOccupantObjectId(pointIndex, -1);
            occupant.ClearCarriedCouchServer(this, pointIndex);
        }

        internal void ReleaseAllOccupantsServer()
        {
            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                ICouchCarrierServer occupant = m_serverOccupants[index];
                m_serverOccupants[index] = null;
                SetOccupantObjectId(index, -1);
                if (occupant != null)
                {
                    occupant.ClearCarriedCouchServer(this, index);
                }
            }
        }

        [Server]
        internal bool LoadIntoVanServer(VanController van, int cargoSlot, float duration)
        {
            if (!IsServerInitialized || IsLoaded || !IsPickupAvailable || van == null || van.GetCargoPoint(cargoSlot) == null)
                return false;

            ReleaseAllOccupantsServer();
            // Start the transition before publishing the loaded SyncVars. On a host the
            // SyncVar callback runs immediately; seeing an active transition keeps it
            // from snapping the couch to the cargo point before the animation can play.
            AnimateLoadObserversRpc(van.NetworkObject, cargoSlot, Mathf.Max(0.05f, duration));
            m_loadedCargoSlot.Value = cargoSlot;
            m_loadedVan.Value = van.NetworkObject;
            return true;
        }

        [Server]
        internal void UnloadFromVanServer(Vector3 position, Quaternion rotation, float duration)
        {
            if (!IsServerInitialized || !IsLoaded) return;
            AnimateUnloadObserversRpc(position, rotation, Mathf.Max(0.05f, duration));
            m_loadedVan.Value = null;
            m_loadedCargoSlot.Value = -1;
        }

        [ObserversRpc(RunLocally = true)]
        private void AnimateLoadObserversRpc(NetworkObject vanObject, int cargoSlot, float duration)
        {
            if (vanObject == null || !vanObject.TryGetComponent(out VanController van)) return;
            Transform point = van.GetCargoPoint(cargoSlot);
            if (point == null) return;
            BeginCargoTransition(AnimateIntoCargo(point, duration, van.Settings.AutoPositionLoadedCouches));
        }

        [ObserversRpc(RunLocally = true)]
        private void AnimateUnloadObserversRpc(Vector3 position, Quaternion rotation, float duration)
        {
            BeginCargoTransition(AnimateOutOfCargo(position, rotation, duration));
        }

        private void BeginCargoTransition(IEnumerator transition)
        {
            if (m_cargoTransition != null) StopCoroutine(m_cargoTransition);
            m_cargoTransition = StartCoroutine(transition);
        }

        private IEnumerator AnimateIntoCargo(Transform cargoPoint, float duration, bool autoPosition)
        {
            SetCargoPhysics(true);
            transform.SetParent(null, true);
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            float elapsed = 0f;
            while (elapsed < duration && cargoPoint != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, cargoPoint.position, t),
                    autoPosition ? Quaternion.Slerp(startRotation, cargoPoint.rotation, t) : startRotation);
                yield return null;
            }
            if (cargoPoint != null) AttachToCargoPoint(cargoPoint, autoPosition);
            m_cargoTransition = null;
        }

        private IEnumerator AnimateOutOfCargo(Vector3 position, Quaternion rotation, float duration)
        {
            VanController sourceVan = m_loadedVan.Value != null
                ? m_loadedVan.Value.GetComponent<VanController>() : null;
            SetCargoPhysics(true);
            transform.SetParent(null, true);
            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, position, t),
                    Quaternion.Slerp(startRotation, rotation, t));
                yield return null;
            }
            // Players/vehicles can move into the destination during the animation.
            // Only the server chooses the final pose and restores simulation.
            if (IsServerInitialized)
            {
                if (sourceVan != null) sourceVan.GetCouchUnloadPose(this, 0, out position, out rotation);
                FinishCargoUnloadObserversRpc(position, rotation);
            }
            m_cargoTransition = null;
        }

        [ObserversRpc(RunLocally = true)]
        private void FinishCargoUnloadObserversRpc(Vector3 position, Quaternion rotation)
        {
            // A slow client may still be interpolating when the server finishes.
            if (!IsServerInitialized && m_cargoTransition != null)
            {
                StopCoroutine(m_cargoTransition);
                m_cargoTransition = null;
            }
            transform.SetPositionAndRotation(position, rotation);
            if (IsServerInitialized)
            {
                PrepareUnloadCollisionRecovery();
            }
            SetCargoPhysics(false);
            foreach (Collider hit in m_cargoIgnoredColliders) SetCargoCollisionIgnored(hit, true);
            if (IsServerInitialized) GetComponent<NetworkTransform>()?.Teleport();
        }

        private void PrepareUnloadCollisionRecovery()
        {
            Physics.SyncTransforms();
            // Only initially overlapping obstacles stay ghosted until carried clear.
            Bounds bounds = CargoBounds;
            Collider support = null;
            float nearestSupport = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(transform.TransformPoint(bounds.center), -transform.up,
                bounds.extents.y * Mathf.Abs(transform.lossyScale.y) + .2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < .65f || hit.distance >= nearestSupport) continue;
                support = hit.collider;
                nearestSupport = hit.distance;
            }
            foreach (Collider hit in Physics.OverlapBox(transform.TransformPoint(bounds.center),
                Vector3.Scale(bounds.extents, transform.lossyScale) + Vector3.one * .01f,
                transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit == support || hit.transform.IsChildOf(transform) || m_cargoIgnoredColliders.Contains(hit)) continue;
                m_cargoIgnoredColliders.Add(hit);
            }
        }

        private void SetCargoCollisionIgnored(Collider other, bool ignored)
        {
            if (other == null) return;
            foreach (Collider own in m_colliders)
                if (own != null && own.enabled) Physics.IgnoreCollision(own, other, ignored);
        }

        private void UpdateCargoCollisionRecovery()
        {
            if (m_cargoIgnoredColliders.Count == 0 || IsLoaded || m_cargoTransition != null) return;
            Bounds bounds = CargoBounds;
            Collider[] overlaps = Physics.OverlapBox(transform.TransformPoint(bounds.center),
                Vector3.Scale(bounds.extents, transform.lossyScale) + Vector3.one * .08f,
                transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int index = m_cargoIgnoredColliders.Count - 1; index >= 0; index--)
            {
                Collider hit = m_cargoIgnoredColliders[index];
                if (hit != null && System.Array.IndexOf(overlaps, hit) >= 0) continue;
                SetCargoCollisionIgnored(hit, false);
                m_cargoIgnoredColliders.RemoveAt(index);
            }
        }

        private void OnLoadedVanChanged(NetworkObject previous, NetworkObject next, bool asServer)
        {
            if (m_cargoTransition == null) ApplyLoadedStateImmediately();
        }

        private void ApplyLoadedStateImmediately()
        {
            NetworkObject vanObject = m_loadedVan.Value;
            if (vanObject != null && vanObject.TryGetComponent(out VanController van))
            {
                Transform point = van.GetCargoPoint(m_loadedCargoSlot.Value);
                if (point != null)
                {
                    SetCargoPhysics(true);
                    AttachToCargoPoint(point, van.Settings.AutoPositionLoadedCouches);
                    return;
                }
            }
            transform.SetParent(null, true);
            SetCargoPhysics(false);
        }

        private void AttachToCargoPoint(Transform point, bool autoPosition)
        {
            Quaternion retainedRotation = transform.rotation;
            transform.SetParent(point, true);
            transform.position = point.position;
            transform.rotation = autoPosition ? point.rotation : retainedRotation;
        }

        private void SetCargoPhysics(bool loaded)
        {
            if (m_rigidbody != null)
            {
                if (!m_rigidbody.isKinematic)
                {
                    m_rigidbody.linearVelocity = Vector3.zero;
                    m_rigidbody.angularVelocity = Vector3.zero;
                }
                m_rigidbody.isKinematic = loaded || !IsServerInitialized;
            }
            if (loaded && m_modelRoot != null)
            {
                m_visualEuler = Vector3.zero;
                m_visualEulerVelocity = Vector3.zero;
                m_visualScale = m_modelBaseScale;
                m_visualScaleVelocity = Vector3.zero;
                m_visualImpulseEuler = Vector3.zero;
                m_modelRoot.localRotation = m_modelBaseRotation;
                m_modelRoot.localScale = m_modelBaseScale;
            }
            m_visualPositionInitialized = false;
            m_visualWorldVelocity = Vector3.zero;
            if (m_colliders == null) return;
            for (int index = 0; index < m_colliders.Length; index++)
                if (m_colliders[index] != null) m_colliders[index].enabled = !loaded;
        }

        internal void TeleportServer(Vector3 position, Quaternion rotation)
        {
            if (!IsServerInitialized || m_rigidbody == null)
            {
                return;
            }

            m_rigidbody.position = position;
            m_rigidbody.rotation = rotation;
            m_rigidbody.linearVelocity = Vector3.zero;
            m_rigidbody.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(position, rotation);
            GetComponent<NetworkTransform>()?.Teleport();
        }

        internal PlayerCouchCarrier GetServerCarrier(int pointIndex)
        {
            return IsServerInitialized && IsValidPointIndex(pointIndex)
                ? m_serverOccupants[pointIndex] as PlayerCouchCarrier
                : null;
        }

        internal bool HasHumanCarrierServer()
        {
            if (!IsServerInitialized) return false;
            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                if (m_serverOccupants[index] is PlayerCouchCarrier) return true;
            }
            return false;
        }

        internal int ThiefCarrierCountServer()
        {
            if (!IsServerInitialized) return 0;
            int count = 0;
            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                if (m_serverOccupants[index] != null &&
                    m_serverOccupants[index] is not PlayerCouchCarrier)
                {
                    count++;
                }
            }
            return count;
        }

        private void SetOccupantObjectId(int pointIndex, int objectId)
        {
            switch (pointIndex)
            {
                case 0:
                    m_frontLeftOccupant.Value = objectId;
                    break;
                case 1:
                    m_frontRightOccupant.Value = objectId;
                    break;
                case 2:
                    m_rearLeftOccupant.Value = objectId;
                    break;
                case 3:
                    m_rearRightOccupant.Value = objectId;
                    break;
            }
        }

        private static bool IsValidPointIndex(int pointIndex)
        {
            return pointIndex >= 0 && pointIndex < MaximumCarryPoints;
        }

        private int CountActiveCarriers()
        {
            int count = 0;
            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                if (IsPointOccupied(index))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountServerCarriers()
        {
            int count = 0;
            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                if (m_serverOccupants[index] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountPlayerCarriers()
        {
            int count = 0;
            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                if (m_serverOccupants[index] is PlayerCouchCarrier) count++;
            }
            return count;
        }

        private float CalculateTotalCarrierEfficiency(int carrierCount)
        {
            if (carrierCount <= 0)
            {
                return 0f;
            }

            float carrierProgress = Mathf.InverseLerp(
                1f,
                MaximumCarryPoints,
                Mathf.Clamp(carrierCount, 1, MaximumCarryPoints));
            float curvedProgress = Mathf.Pow(carrierProgress, CarrierCountCurveExponent);
            return Mathf.Lerp(
                SingleCarrierEfficiency,
                MaximumCarrierEfficiency,
                curvedProgress);
        }

        private Vector3 CalculateCombinedMovementIntent(
            out float totalIntentMagnitude,
            out int movingCarrierCount)
        {
            Vector3 combinedIntent = Vector3.zero;
            totalIntentMagnitude = 0f;
            movingCarrierCount = 0;
            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                ICouchCarrierServer carrier = m_serverOccupants[index];
                if (carrier == null)
                {
                    continue;
                }

                Vector3 intent = carrier.GetServerMovementIntent();
                combinedIntent += intent;
                totalIntentMagnitude += intent.magnitude;
                if (intent.sqrMagnitude > 0.01f)
                {
                    movingCarrierCount++;
                }
            }

            return combinedIntent;
        }

        private static float CalculateCooperationEfficiency(
            int movingCarrierCount,
            Vector3 combinedMovementIntent,
            float totalIntentMagnitude)
        {
            return movingCarrierCount <= 1 || totalIntentMagnitude <= 0.001f
                ? 1f
                : Mathf.Clamp01(combinedMovementIntent.magnitude / totalIntentMagnitude);
        }

        private static Vector3 CalculateDesiredCarryVelocity(
            Vector3 combinedMovementIntent,
            int movingCarrierCount,
            float maximumSpeed)
        {
            if (movingCarrierCount <= 0 || maximumSpeed <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 averageIntent = combinedMovementIntent / movingCarrierCount;
            return Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(averageIntent, Vector3.up),
                1f) * maximumSpeed;
        }

        private static Vector3 CalculateHorizontalCarryForce(
            Vector3 horizontalGripError,
            Vector3 horizontalPointVelocity,
            Vector3 movementIntent,
            float configuredSpeed,
            float carrierEfficiency,
            float cooperationForceScale)
        {
            Vector3 targetPointVelocity = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(movementIntent, Vector3.up),
                1f) * Mathf.Max(0f, configuredSpeed);
            Vector3 relativePointVelocity =
                Vector3.ProjectOnPlane(horizontalPointVelocity, Vector3.up) - targetPointVelocity;
            Vector3 force =
                (Vector3.ProjectOnPlane(horizontalGripError, Vector3.up) * CarryForce -
                 relativePointVelocity * CarryDamping) *
                Mathf.Max(0f, carrierEfficiency) * Mathf.Clamp01(cooperationForceScale);
            return Vector3.ClampMagnitude(force, MaximumHorizontalForce);
        }

        private static Vector3 CalculateHorizontalCarryTorque(
            Vector3 centreOfMass,
            Vector3 carryPoint,
            Vector3 horizontalForce)
        {
            Vector3 lever = (carryPoint - centreOfMass) * RotationInfluence;
            return Vector3.Cross(
                lever,
                Vector3.ProjectOnPlane(horizontalForce, Vector3.up));
        }

        private float GetConfiguredMoveSpeed(int carrierCount)
        {
            int clampedCount = Mathf.Clamp(carrierCount, 1, MaximumCarryPoints);
            return m_settings != null
                ? m_settings.GetMoveSpeed(clampedCount)
                : s_defaultMoveSpeeds[clampedCount - 1];
        }

        internal float GetConfiguredCarrierMoveSpeedServer()
        {
            int carrierCount = IsServerInitialized
                ? CountServerCarriers()
                : ActiveCarrierCount;
            return carrierCount > 0 ? GetConfiguredMoveSpeed(carrierCount) : 0f;
        }

        private static float SelectAccelerationLimit(Vector3 currentVelocity, Vector3 desiredVelocity)
        {
            if (desiredVelocity.sqrMagnitude < 0.01f)
            {
                return MaximumBrakingAcceleration;
            }

            bool reversing = currentVelocity.sqrMagnitude > 0.25f &&
                Vector3.Dot(currentVelocity.normalized, desiredVelocity.normalized) < -0.2f;
            return reversing ? MaximumReverseAcceleration : MaximumMovementAcceleration;
        }

        private void UpdateCarriedPhysicsState(bool carried)
        {
            if (carried == m_wasCarried)
            {
                return;
            }

            m_wasCarried = carried;
            m_rigidbody.linearDamping = carried ? 0.65f : 0.28f;
            m_rigidbody.angularDamping = carried ? 2.2f : 0.8f;
            // Visual pickup/drop reactions are driven from synchronized occupancy in
            // LateUpdate so every peer sees the same state transition.
            if (!carried)
            {
                m_smoothedCarryVelocity = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up);
            }
        }

        private void ApplyCarriedUprightTorque()
        {
            Vector3 currentUp = m_rigidbody.rotation * Vector3.up;
            float tilt = Vector3.Angle(currentUp, Vector3.up);
            if (tilt <= MaximumCarriedLeanAngle)
            {
                return;
            }

            Vector3 correctionAxis = Vector3.Cross(currentUp, Vector3.up);
            Vector3 localAngularVelocity = Vector3.ProjectOnPlane(m_rigidbody.angularVelocity, Vector3.up);
            float excessTilt = Mathf.InverseLerp(MaximumCarriedLeanAngle, 50f, tilt);
            m_rigidbody.AddTorque(
                correctionAxis * (CarriedUprightStrength * excessTilt) -
                localAngularVelocity * CarriedUprightDamping,
                ForceMode.Acceleration);
        }

        private void ApplyStepAssistance(Vector3 movementIntent)
        {
            Vector3 direction = Vector3.ProjectOnPlane(movementIntent, Vector3.up);
            if (direction.sqrMagnitude < 0.04f || m_rigidbody.linearVelocity.y > MaximumStepUpSpeed)
            {
                return;
            }

            direction.Normalize();
            if (!TryGetColliderBounds(out Bounds bounds))
            {
                return;
            }

            float lowerProbeHeight = bounds.min.y + 0.06f;
            float horizontalExtent = Mathf.Abs(direction.x) * bounds.extents.x +
                                     Mathf.Abs(direction.z) * bounds.extents.z;
            Vector3 lowerOrigin = new(bounds.center.x, lowerProbeHeight, bounds.center.z);
            lowerOrigin += direction * (horizontalExtent + 0.02f);
            if (!TryGetExternalRayHit(lowerOrigin, direction, StepProbeDistance, out RaycastHit lowerHit) ||
                lowerHit.normal.y > 0.35f)
            {
                return;
            }

            Vector3 upperOrigin = lowerOrigin + Vector3.up * (MaximumStepHeight + StepClearance);
            if (TryGetExternalRayHit(upperOrigin, direction, StepProbeDistance, out _))
            {
                return;
            }

            Vector3 ledgeProbe = lowerHit.point + direction * 0.08f +
                                 Vector3.up * (MaximumStepHeight + StepClearance);
            if (!TryGetExternalRayHit(ledgeProbe, Vector3.down,
                    MaximumStepHeight + StepClearance, out RaycastHit topHit))
            {
                return;
            }

            float stepHeight = topHit.point.y - bounds.min.y;
            if (stepHeight < -0.02f || stepHeight > MaximumStepHeight)
            {
                return;
            }

            float liftScale = Mathf.InverseLerp(MaximumStepHeight, 0f, stepHeight) * 0.35f + 0.65f;
            m_rigidbody.AddForce(Vector3.up * (StepLiftAcceleration * liftScale), ForceMode.Acceleration);
        }

        private bool TryGetExternalRayHit(
            Vector3 origin,
            Vector3 direction,
            float distance,
            out RaycastHit nearestHit)
        {
            int count = Physics.RaycastNonAlloc(
                origin, direction, m_stepHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            nearestHit = default;
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = m_stepHits[index];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform) ||
                    hit.distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = hit.distance;
                nearestHit = hit;
                found = true;
            }
            return found;
        }

        private bool TryGetColliderBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            if (m_colliders == null)
            {
                return false;
            }

            for (int index = 0; index < m_colliders.Length; index++)
            {
                Collider collider = m_colliders[index];
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            return found;
        }

        private void ApplySlopeForces()
        {
            // OnCollisionStay is populated by the preceding physics step. This keeps
            // the authoritative simulation contact-driven without ground raycasts.
            if (Time.fixedTime - m_lastGroundContactTime > Time.fixedDeltaTime * 1.5f)
            {
                return;
            }

            float slopeAngle = Vector3.Angle(m_groundNormal, Vector3.up);
            float slopeProgress = Mathf.InverseLerp(
                MinimumSlopeAngle,
                MaximumSlopeAngle,
                slopeAngle);
            if (slopeProgress <= 0f)
            {
                return;
            }

            Vector3 gravityAlongSlope = Vector3.ProjectOnPlane(Physics.gravity, m_groundNormal);
            if (gravityAlongSlope.sqrMagnitude < 0.0001f)
            {
                return;
            }

            // Unity's normal gravity already supplies one downhill component. This
            // The additional component preserves real sliding while making
            // the couch's weight meaningful against the carrying force.
            m_rigidbody.AddForce(
                gravityAlongSlope * (m_rigidbody.mass * SlopeGravityInfluence * slopeProgress),
                ForceMode.Force);

            Vector3 slopeVelocity = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, m_groundNormal);
            if (slopeVelocity.sqrMagnitude > 0.0001f && SlopeRollingResistance > 0f)
            {
                float normalForce = m_rigidbody.mass * Mathf.Abs(Physics.gravity.y) *
                                    Mathf.Clamp01(m_groundNormal.y);
                m_rigidbody.AddForce(
                    -slopeVelocity.normalized * normalForce * SlopeRollingResistance,
                    ForceMode.Force);
            }
        }

        private void OnCollisionStay(Collision collision)
        {
            Vector3 bestGroundNormal = m_groundNormal;
            bool foundGround = false;
            for (int index = 0; index < collision.contactCount; index++)
            {
                Vector3 normal = collision.GetContact(index).normal;
                if (normal.y > 0.05f && (!foundGround || normal.y > bestGroundNormal.y))
                {
                    bestGroundNormal = normal;
                    foundGround = true;
                }
            }

            if (foundGround)
            {
                m_groundNormal = bestGroundNormal;
                m_lastGroundContactTime = Time.fixedTime;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServerInitialized || m_rigidbody == null || m_rigidbody.isKinematic ||
                collision.contactCount == 0)
            {
                return;
            }

            float impactSpeed = Mathf.Min(collision.relativeVelocity.magnitude, MaximumImpactReactionSpeed);
            if (impactSpeed < 2.25f)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            Vector3 lever = contact.point - m_rigidbody.worldCenterOfMass;
            Vector3 impactDirection = collision.relativeVelocity.sqrMagnitude > 0.001f
                ? collision.relativeVelocity.normalized
                : -contact.normal;
            Vector3 torque = Vector3.Cross(lever, impactDirection) *
                             (impactSpeed * ImpactTorqueMultiplier);
            m_rigidbody.AddTorque(torque, ForceMode.VelocityChange);
            PlayImpactReactionObserversRpc(impactSpeed, contact.normal);
        }

        [Server]
        internal void ApplyPlayerBounceServer(float landingSpeed, Vector3 playerVelocity)
        {
            if (!IsServerInitialized || IsLoaded || m_rigidbody == null || m_rigidbody.isKinematic)
            {
                return;
            }

            float strength = Mathf.Clamp(landingSpeed, 2f, 12f);
            Vector3 inheritedPlanarVelocity = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(playerVelocity, Vector3.up), 8f);
            m_rigidbody.AddForce(
                Vector3.down * (0.55f + strength * 0.1f) + inheritedPlanarVelocity * 0.05f,
                ForceMode.VelocityChange);
            PlayBounceReactionObserversRpc(strength);
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayBounceReactionObserversRpc(float strength)
        {
            float squash = Mathf.Lerp(0.08f, 0.18f, Mathf.InverseLerp(2f, 12f, strength));
            m_visualScaleVelocity += new Vector3(m_modelBaseScale.x * squash,
                -m_modelBaseScale.y * squash * 1.8f, m_modelBaseScale.z * squash);
            m_visualImpulseEuler += new Vector3(Random.Range(-2.5f, 2.5f), 0f,
                Random.Range(-3.5f, 3.5f));
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayImpactReactionObserversRpc(float speed, Vector3 normal)
        {
            float amount = Mathf.InverseLerp(2.25f, MaximumImpactReactionSpeed, speed);
            Vector3 localNormal = transform.InverseTransformDirection(normal);
            m_visualImpulseEuler += new Vector3(
                -localNormal.z * Mathf.Lerp(2f, 10f, amount),
                0f,
                localNormal.x * Mathf.Lerp(2f, 12f, amount));
        }

        private void ConfigureColliderMaterials()
        {
            m_runtimePhysicsMaterial = new PhysicsMaterial($"{name} Cartoon Couch Physics")
            {
                dynamicFriction = 0.18f,
                staticFriction = 0.22f,
                bounciness = 0.16f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };

            if (m_colliders == null)
            {
                return;
            }

            for (int index = 0; index < m_colliders.Length; index++)
            {
                if (m_colliders[index] != null && !m_colliders[index].isTrigger)
                {
                    m_colliders[index].sharedMaterial = m_runtimePhysicsMaterial;
                }
            }
        }

        private void ApplyWeightAndStability()
        {
            if (m_rigidbody == null)
            {
                return;
            }

            m_rigidbody.mass = CouchWeight;
            m_rigidbody.maxAngularVelocity = MaximumAngularVelocity;
            // Preserve positional/designer constraints but allow the couch to lean,
            // tip and tumble. Carry-time torque provides soft stability instead.
            m_rigidbody.constraints &= ~(RigidbodyConstraints.FreezeRotationX |
                                         RigidbodyConstraints.FreezeRotationZ);
        }

        private void ApplyRuntimePhysicsConfiguration()
        {
            if (m_rigidbody == null)
            {
                return;
            }

            // These native Rigidbody properties are unsafe to mutate from OnValidate
            // while Unity is integrating a prefab during asset import. Configure them
            // only after the live component has reached Awake.
            m_rigidbody.linearDamping = 0.28f;
            m_rigidbody.angularDamping = 0.8f;
            m_rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void LimitHorizontalVelocity(float maximumSpeed)
        {
            if (maximumSpeed <= 0f)
            {
                return;
            }

            Vector3 velocity = m_rigidbody.linearVelocity;
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (horizontalVelocity.sqrMagnitude <= maximumSpeed * maximumSpeed)
            {
                return;
            }

            Vector3 verticalVelocity = velocity - horizontalVelocity;
            m_rigidbody.linearVelocity = Vector3.ClampMagnitude(horizontalVelocity, maximumSpeed) +
                                         verticalVelocity;
        }

        private void LimitExtremeFreeMotion(bool isFree)
        {
            if (!isFree)
            {
                return;
            }

            m_rigidbody.linearVelocity = Vector3.ClampMagnitude(
                m_rigidbody.linearVelocity, FreeMaximumSpeed);
            m_rigidbody.angularVelocity = Vector3.ClampMagnitude(
                m_rigidbody.angularVelocity, MaximumAngularVelocity);
        }

        private void ClearDebugForces(int pointIndex)
        {
            m_debugHorizontalForces[pointIndex] = Vector3.zero;
            m_debugLiftForces[pointIndex] = Vector3.zero;
            m_debugMovementIntents[pointIndex] = Vector3.zero;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (m_carryPoints == null)
            {
                return;
            }

            foreach (CouchCarryPoint point in m_carryPoints)
            {
                if (point == null)
                {
                    continue;
                }

                Gizmos.color = point.IsAvailable ? Color.green : Color.red;
                Gizmos.DrawSphere(point.transform.position, 0.08f);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.4f, point.transform.position);

                if (!ShowForceGizmos || point.PointIndex < 0 || point.PointIndex >= MaximumCarryPoints)
                {
                    continue;
                }

                int index = point.PointIndex;
                Gizmos.color = new Color(1f, 0.25f, 0.15f);
                Gizmos.DrawRay(point.transform.position, m_debugHorizontalForces[index] * DebugForceScale);
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(point.transform.position, m_debugLiftForces[index] * DebugForceScale);
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(point.transform.position, m_debugMovementIntents[index] * 0.75f);
            }
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            ApplyWeightAndStability();
        }
#endif
    }
}
