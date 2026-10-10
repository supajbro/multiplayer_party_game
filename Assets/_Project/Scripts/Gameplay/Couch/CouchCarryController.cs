using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Component.Transforming;
using UnityEngine;

namespace CouchGuys.Gameplay.Couch
{
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
        private const float SpeedLimitTransitionRate = 5f;
        private const float MaximumAngularVelocity = 7f;
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
        private readonly PlayerCouchCarrier[] m_serverOccupants = new PlayerCouchCarrier[MaximumCarryPoints];
        private readonly Vector3[] m_debugHorizontalForces = new Vector3[MaximumCarryPoints];
        private readonly Vector3[] m_debugLiftForces = new Vector3[MaximumCarryPoints];
        private readonly Vector3[] m_debugMovementIntents = new Vector3[MaximumCarryPoints];
        private Vector3 m_groundNormal = Vector3.up;
        private float m_lastGroundContactTime = float.NegativeInfinity;
        private float m_currentMaximumMovementSpeed;
        private int m_previousCarrierCount;
        private GameObject m_modelInstance;
        private int m_appliedModelVariantIndex = -1;

        public Rigidbody CouchRigidbody => m_rigidbody;
        public float Weight => CouchWeight;
        public float MaxCarryDistance => m_settings != null
            ? m_settings.MaxCarryDistance
            : DefaultMaxCarryDistance;
        public int ActiveCarrierCount => CountActiveCarriers();
        public int MovingCarrierCount => m_movingCarrierCount.Value;
        public float CurrentCooperationEfficiency => m_cooperationEfficiency.Value;
        public Vector3 CurrentVelocity => m_rigidbody != null ? m_rigidbody.linearVelocity : Vector3.zero;
        public int ModelVariantIndex => m_modelVariantIndex.Value;
        internal int ServerCarrierCount => IsServerInitialized ? CountServerCarriers() : 0;

        internal int ValidServerCarrierCount
        {
            get
            {
                if (!IsServerInitialized) return 0;
                int count = 0;
                for (int index = 0; index < MaximumCarryPoints; index++)
                {
                    PlayerCouchCarrier carrier = m_serverOccupants[index];
                    if (carrier == null || !carrier.IsSpawned) continue;
                    PlayerHealth health = carrier.GetComponent<PlayerHealth>();
                    if (health == null || health.IsAlive) count++;
                }
                return count;
            }
        }

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            m_modelVariantIndex.OnChange += OnModelVariantChanged;
            ApplyWeightAndStability();
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
            if (!IsServerInitialized)
            {
                m_rigidbody.isKinematic = true;
            }
        }

        private void OnDestroy()
        {
            m_modelVariantIndex.OnChange -= OnModelVariantChanged;
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
            if (!IsServerInitialized || m_rigidbody == null || m_rigidbody.isKinematic)
            {
                return;
            }

            int carrierCount = CountServerCarriers();
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
                PlayerCouchCarrier carrier = m_serverOccupants[index];
                CouchCarryPoint carryPoint = GetPoint(index);
                if (carrier == null)
                {
                    if (IsPointOccupied(index))
                    {
                        SetOccupantObjectId(index, -1);
                    }

                    continue;
                }

                if (!carrier.IsSpawned || carryPoint == null)
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
                Vector3 horizontalForce =
                    (horizontalError * CarryForce - horizontalVelocity * CarryDamping) *
                    perCarrierEfficiency * cooperationForceScale;
                horizontalForce = Vector3.ClampMagnitude(horizontalForce, MaximumHorizontalForce);

                float heightError = targetPosition.y - pointPosition.y;
                float liftForce = supportPerCarrier + Mathf.Max(0f, heightError) * LiftForce -
                    pointVelocity.y * LiftDamping;
                liftForce = Mathf.Clamp(
                    liftForce,
                    0f,
                    MaximumLiftForce);
                Vector3 verticalForce = Vector3.up * liftForce;

                Vector3 applicationPoint = Vector3.Lerp(
                    m_rigidbody.worldCenterOfMass,
                    pointPosition,
                    RotationInfluence);
                m_rigidbody.AddForceAtPosition(horizontalForce, applicationPoint, ForceMode.Force);
                m_rigidbody.AddForceAtPosition(verticalForce, pointPosition, ForceMode.Force);

                m_debugHorizontalForces[index] = horizontalForce;
                m_debugLiftForces[index] = verticalForce;
                m_debugMovementIntents[index] = movementIntent;
            }

            float configuredSpeed = carrierCount > 0 ? GetConfiguredMoveSpeed(carrierCount) : 0f;
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
            Vector3 desiredVelocity = Vector3.ClampMagnitude(averageIntent, 1f) *
                                      m_currentMaximumMovementSpeed;
            Vector3 horizontalCouchVelocity = Vector3.ProjectOnPlane(
                m_rigidbody.linearVelocity,
                Vector3.up);
            Vector3 velocityChange = Vector3.MoveTowards(
                horizontalCouchVelocity,
                desiredVelocity,
                MaximumMovementAcceleration * Time.fixedDeltaTime) - horizontalCouchVelocity;
            Vector3 movementAcceleration = velocityChange / Time.fixedDeltaTime;
            if (carrierCount > 0)
            {
                m_rigidbody.AddForce(movementAcceleration, ForceMode.Acceleration);
            }

            ApplySlopeForces();

            LimitHorizontalVelocity(carrierCount > 0 ? m_currentMaximumMovementSpeed : 0f);
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
            if (!IsServerInitialized || carrier == null || !carrier.IsSpawned ||
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
            SetOccupantObjectId(pointIndex, carrier.NetworkObject.ObjectId);
            carrier.SetCarriedCouchServer(this, pointIndex);
            return true;
        }

        internal PlayerCouchCarrier GetFirstHumanCarrierServer(PlayerCouchCarrier excludedCarrier)
        {
            if (!IsServerInitialized)
            {
                return null;
            }

            for (int index = 0; index < MaximumCarryPoints; index++)
            {
                PlayerCouchCarrier carrier = m_serverOccupants[index];
                if (carrier != null && carrier != excludedCarrier && carrier.Owner.IsValid)
                {
                    return carrier;
                }
            }

            return null;
        }

        internal void ReleasePointServer(int pointIndex, PlayerCouchCarrier expectedCarrier)
        {
            if (!IsServerInitialized || !IsValidPointIndex(pointIndex))
            {
                return;
            }

            PlayerCouchCarrier occupant = m_serverOccupants[pointIndex];
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
                PlayerCouchCarrier occupant = m_serverOccupants[index];
                m_serverOccupants[index] = null;
                SetOccupantObjectId(index, -1);
                if (occupant != null)
                {
                    occupant.ClearCarriedCouchServer(this, index);
                }
            }
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
                ? m_serverOccupants[pointIndex]
                : null;
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
                PlayerCouchCarrier carrier = m_serverOccupants[index];
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

        private float GetConfiguredMoveSpeed(int carrierCount)
        {
            int clampedCount = Mathf.Clamp(carrierCount, 1, MaximumCarryPoints);
            return m_settings != null
                ? m_settings.GetMoveSpeed(clampedCount)
                : s_defaultMoveSpeeds[clampedCount - 1];
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

        private void ApplyWeightAndStability()
        {
            if (m_rigidbody == null)
            {
                return;
            }

            m_rigidbody.mass = CouchWeight;
            m_rigidbody.maxAngularVelocity = MaximumAngularVelocity;
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
