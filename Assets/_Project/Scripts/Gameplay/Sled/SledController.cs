using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Component.Transforming;
using UnityEngine;

namespace CouchGuys.Gameplay.Sled
{
    /// <summary>Runs the sled's single server-authoritative Rigidbody simulation.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class SledController : NetworkBehaviour
    {
        public const int MaximumGrabPoints = 4;

        [SerializeField] private Rigidbody m_rigidbody;
        [SerializeField] private SledGrabPoint[] m_grabPoints = new SledGrabPoint[MaximumGrabPoints];
        [SerializeField] private SledSettings m_settings;

        private const float SledMass = 75f;
        private const float MaximumGrabDistance = 1.35f;
        private const float DriveAccelerationPerCarrier = 5.5f;
        private const float BrakingAccelerationPerCarrier = 3.5f;
        private const float MaximumDriveAcceleration = 17f;
        private const float MaximumTorqueAcceleration = 15f;
        private const float MaximumSafeSpeed = 24f;
        private const float MaximumAngularVelocity = 8f;
        private const float HighSpeedSteeringStart = 5f;
        private const float HighSpeedSteeringMinimum = 0.28f;
        private const float ConflictMinimumPower = 0.22f;
        private const float GroundProbeDistance = 0.48f;
        private const float UprightStrength = 5f;
        private const float UprightDamping = 1.6f;
        private const float PlayerSpeedMultiplier = 0.82f;

        private readonly SyncVar<int> m_frontLeftOccupant = new(-1);
        private readonly SyncVar<int> m_frontRightOccupant = new(-1);
        private readonly SyncVar<int> m_rearLeftOccupant = new(-1);
        private readonly SyncVar<int> m_rearRightOccupant = new(-1);
        private readonly PlayerCouchCarrier[] m_serverOccupants = new PlayerCouchCarrier[MaximumGrabPoints];
        private Collider[] m_colliders;
        private PhysicsMaterial m_runtimeMaterial;

        public Rigidbody SledRigidbody => m_rigidbody;
        public float MaxGrabDistance => MaximumGrabDistance;
        public int ActiveCarrierCount => CountCarriers();

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            m_colliders = GetComponentsInChildren<Collider>(true);
            ConfigureBody();
            ConfigureFriction();
            m_rigidbody.isKinematic = true;
            if (m_settings == null) Debug.LogError("SledController requires SledSettings.", this);
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
            if (!IsServerInitialized) m_rigidbody.isKinematic = true;
        }

        public override void OnStopServer()
        {
            ReleaseAllOccupantsServer();
            base.OnStopServer();
        }

        private void OnDestroy()
        {
            if (m_runtimeMaterial != null) Destroy(m_runtimeMaterial);
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || m_rigidbody == null || m_rigidbody.isKinematic) return;

            int carrierCount = CountCarriers();
            Vector3 summedIntent = Vector3.zero;
            float summedMagnitude = 0f;
            int movingCount = 0;
            for (int index = 0; index < MaximumGrabPoints; index++)
            {
                PlayerCouchCarrier carrier = m_serverOccupants[index];
                SledGrabPoint point = GetPoint(index);
                if (carrier == null) continue;
                if (!carrier.IsValidServerCarrier || carrier.CarriedSled != this || point == null ||
                    Vector3.Distance(carrier.transform.position, point.transform.position) > MaximumGrabDistance + 0.5f)
                {
                    ReleasePointServer(index, carrier);
                    continue;
                }

                Vector3 intent = Vector3.ClampMagnitude(
                    Vector3.ProjectOnPlane(carrier.GetServerMovementIntent(), Vector3.up), 1f);
                summedIntent += intent;
                summedMagnitude += intent.magnitude;
                if (intent.sqrMagnitude > 0.01f) movingCount++;

                // Torque remains tied to the physical grip while translation is
                // calculated once below. This makes every corner responsive without
                // erasing the funny rotation produced by disagreeing players.
                Vector3 lever = Vector3.ProjectOnPlane(
                    point.transform.position - m_rigidbody.worldCenterOfMass, Vector3.up);
                Vector3 localForce = intent * (DriveAccelerationPerCarrier * SledMass);
                Vector3 torque = Vector3.Cross(lever, localForce) *
                    (m_settings != null ? m_settings.SteeringStrength : 1f);
                float speedSteering = Mathf.Lerp(1f, HighSpeedSteeringMinimum,
                    Mathf.InverseLerp(HighSpeedSteeringStart, MaximumSafeSpeed,
                        Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up).magnitude));
                torque = Vector3.ClampMagnitude(torque * speedSteering,
                    MaximumTorqueAcceleration * SledMass);
                m_rigidbody.AddTorque(torque, ForceMode.Force);
            }

            if (carrierCount > 0)
            {
                ApplyControlledTranslation(carrierCount, movingCount, summedIntent, summedMagnitude);
                ApplyMildUprightAssistance();
            }

            ApplySlidingFriction();
            LimitExtremeMotion();
        }

        private void ApplyControlledTranslation(
            int carrierCount, int movingCount, Vector3 summedIntent, float summedMagnitude)
        {
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, Vector3.up);
            if (movingCount == 0)
            {
                Vector3 braking = Vector3.ClampMagnitude(-horizontalVelocity,
                    BrakingAccelerationPerCarrier * carrierCount * Time.fixedDeltaTime);
                m_rigidbody.AddForce(braking / Time.fixedDeltaTime, ForceMode.Acceleration);
                return;
            }

            Vector3 averageIntent = summedIntent / movingCount;
            float agreement = summedMagnitude > 0.001f
                ? Mathf.Clamp01(summedIntent.magnitude / summedMagnitude)
                : 0f;
            float power = Mathf.Lerp(ConflictMinimumPower, 1f, agreement);
            float configuredSpeed = m_settings != null ? m_settings.GetSpeed(carrierCount) : 1.8f;
            Vector3 desiredVelocity = Vector3.ClampMagnitude(averageIntent, 1f) * configuredSpeed;

            // Only correct the component caused by player input. Perpendicular and
            // downhill momentum survive, and can exceed the configured drive speed.
            Vector3 direction = averageIntent.sqrMagnitude > 0.001f
                ? averageIntent.normalized
                : Vector3.zero;
            float currentAlongIntent = Vector3.Dot(horizontalVelocity, direction);
            float desiredAlongIntent = Vector3.Dot(desiredVelocity, direction);
            float delta = desiredAlongIntent - currentAlongIntent;
            float acceleration = Mathf.Clamp(delta / Time.fixedDeltaTime,
                -BrakingAccelerationPerCarrier * carrierCount,
                Mathf.Min(MaximumDriveAcceleration, DriveAccelerationPerCarrier * carrierCount) * power);
            m_rigidbody.AddForce(direction * acceleration, ForceMode.Acceleration);
        }

        private void ApplySlidingFriction()
        {
            if (!Physics.Raycast(m_rigidbody.worldCenterOfMass, Vector3.down,
                    out RaycastHit hit, GroundProbeDistance, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(transform)) return;

            Vector3 surfaceVelocity = Vector3.ProjectOnPlane(m_rigidbody.linearVelocity, hit.normal);
            if (surfaceVelocity.sqrMagnitude < 0.0025f) return;
            float friction = m_settings != null ? m_settings.SlidingFriction : 0.18f;
            // Small acceleration-based rolling resistance preserves gravity's
            // slope component instead of imposing an artificial speed cap.
            m_rigidbody.AddForce(-surfaceVelocity.normalized * friction * 3.5f,
                ForceMode.Acceleration);
        }

        private void ApplyMildUprightAssistance()
        {
            Vector3 correctionAxis = Vector3.Cross(transform.up, Vector3.up);
            Vector3 localAngular = m_rigidbody.angularVelocity;
            Vector3 torque = correctionAxis * UprightStrength -
                Vector3.ProjectOnPlane(localAngular, Vector3.up) * UprightDamping;
            m_rigidbody.AddTorque(torque, ForceMode.Acceleration);
        }

        private void LimitExtremeMotion()
        {
            Vector3 velocity = m_rigidbody.linearVelocity;
            Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (horizontal.magnitude > MaximumSafeSpeed)
            {
                horizontal = horizontal.normalized * MaximumSafeSpeed;
                m_rigidbody.linearVelocity = horizontal + Vector3.up * velocity.y;
            }
            m_rigidbody.maxAngularVelocity = MaximumAngularVelocity;
        }

        public SledGrabPoint GetPoint(int pointIndex) =>
            pointIndex >= 0 && pointIndex < m_grabPoints.Length ? m_grabPoints[pointIndex] : null;

        public bool IsPointOccupied(int pointIndex) => GetOccupantId(pointIndex) >= 0;

        [Server]
        internal bool TryGrabPointServer(PlayerCouchCarrier carrier, int pointIndex)
        {
            if (!IsServerInitialized || carrier == null || !carrier.IsAvailableForServerCarry ||
                pointIndex < 0 || pointIndex >= MaximumGrabPoints || GetPoint(pointIndex) == null ||
                IsPointOccupied(pointIndex)) return false;

            m_serverOccupants[pointIndex] = carrier;
            SetOccupantId(pointIndex, (int)carrier.NetworkObject.ObjectId);
            carrier.SetCarriedSledServer(this, pointIndex);
            return true;
        }

        [Server]
        internal void ReleasePointServer(int pointIndex, PlayerCouchCarrier expectedCarrier)
        {
            if (!IsServerInitialized || pointIndex < 0 || pointIndex >= MaximumGrabPoints) return;
            PlayerCouchCarrier carrier = m_serverOccupants[pointIndex];
            if (expectedCarrier != null && carrier != expectedCarrier) return;
            m_serverOccupants[pointIndex] = null;
            SetOccupantId(pointIndex, -1);
            carrier?.ClearCarriedSledServer(this, pointIndex);
        }

        [Server]
        internal void ReleaseAllOccupantsServer()
        {
            if (!IsServerInitialized) return;
            for (int index = 0; index < MaximumGrabPoints; index++)
                ReleasePointServer(index, m_serverOccupants[index]);
        }

        internal PlayerCouchCarrier GetFirstHumanCarrierServer(PlayerCouchCarrier except = null)
        {
            if (!IsServerInitialized) return null;
            for (int index = 0; index < MaximumGrabPoints; index++)
            {
                PlayerCouchCarrier carrier = m_serverOccupants[index];
                if (carrier != null && carrier != except && carrier.Owner.IsValid) return carrier;
            }
            return null;
        }

        internal PlayerCouchCarrier GetServerCarrier(int pointIndex) =>
            IsServerInitialized && pointIndex >= 0 && pointIndex < MaximumGrabPoints
                ? m_serverOccupants[pointIndex]
                : null;

        [Server]
        public void TeleportServer(Vector3 position, Quaternion rotation)
        {
            if (!IsServerInitialized) return;
            m_rigidbody.position = position;
            m_rigidbody.rotation = rotation;
            m_rigidbody.linearVelocity = Vector3.zero;
            m_rigidbody.angularVelocity = Vector3.zero;
            GetComponent<NetworkTransform>()?.Teleport();
        }

        internal void SetCarrierCollisionIgnored(Collider carrierCollider, bool ignored)
        {
            if (carrierCollider == null || m_colliders == null) return;
            foreach (Collider sledCollider in m_colliders)
                if (sledCollider != null && sledCollider != carrierCollider)
                    Physics.IgnoreCollision(carrierCollider, sledCollider, ignored);
        }

        public float CalculateCarrierMovementSpeedMultiplier() => PlayerSpeedMultiplier;

        private int CountCarriers()
        {
            int count = 0;
            for (int index = 0; index < MaximumGrabPoints; index++)
                if (m_serverOccupants[index] != null || IsPointOccupied(index)) count++;
            return count;
        }

        private int GetOccupantId(int index) => index switch
        {
            0 => m_frontLeftOccupant.Value,
            1 => m_frontRightOccupant.Value,
            2 => m_rearLeftOccupant.Value,
            3 => m_rearRightOccupant.Value,
            _ => -1
        };

        private void SetOccupantId(int index, int value)
        {
            switch (index)
            {
                case 0: m_frontLeftOccupant.Value = value; break;
                case 1: m_frontRightOccupant.Value = value; break;
                case 2: m_rearLeftOccupant.Value = value; break;
                case 3: m_rearRightOccupant.Value = value; break;
            }
        }

        private void ConfigureBody()
        {
            m_rigidbody.mass = SledMass;
            m_rigidbody.linearDamping = 0.08f;
            m_rigidbody.angularDamping = 0.7f;
            m_rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_rigidbody.centerOfMass = new Vector3(0f, 0.18f, 0f);
            m_rigidbody.maxDepenetrationVelocity = 7f;
            m_rigidbody.maxAngularVelocity = MaximumAngularVelocity;
        }

        private void ConfigureFriction()
        {
            m_runtimeMaterial = new PhysicsMaterial("Sled Sliding Material")
            {
                dynamicFriction = m_settings != null ? m_settings.SlidingFriction : 0.18f,
                staticFriction = m_settings != null ? Mathf.Min(0.35f, m_settings.SlidingFriction + 0.08f) : 0.26f,
                bounciness = 0.04f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            foreach (Collider collider in m_colliders)
                if (collider != null && !collider.isTrigger) collider.sharedMaterial = m_runtimeMaterial;
        }
    }
}
