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

        [Header("Weight")]
        [SerializeField, Min(1f)] private float m_weight = 35f;

        [Header("Horizontal Carrying")]
        [SerializeField, Min(0f)] private float m_carryForce = 500f;
        [SerializeField, Min(0f)] private float m_movementForce = 320f;
        [SerializeField, Min(0f)] private float m_damping = 65f;
        [SerializeField, Min(0f)] private float m_maximumHorizontalForce = 700f;
        [SerializeField, Range(0f, 1f)] private float m_rotationInfluence = 0.85f;

        [Header("Vertical Support")]
        [SerializeField, Min(0f)] private float m_liftForce = 450f;
        [SerializeField, Min(0f)] private float m_liftDamping = 55f;
        [SerializeField, Min(0f)] private float m_maximumLiftForce = 350f;

        [Header("Carrier Scaling")]
        [SerializeField, Range(0.05f, 1f)] private float m_singleCarrierEfficiency = 0.45f;
        [SerializeField, Range(0f, 1f)] private float m_additionalCarrierEfficiency = 0.35f;
        [SerializeField, Min(0.1f)] private float m_maximumCarrierEfficiency = 1.25f;
        [SerializeField, Range(0f, 1f)] private float m_minimumConflictEfficiency = 0.45f;

        [Header("Stability")]
        [SerializeField, Min(0.1f)] private float m_maximumLinearVelocity = 7f;
        [SerializeField, Min(0.1f)] private float m_maximumAngularVelocity = 7f;

        [Header("Debugging")]
        [SerializeField] private bool m_showForceGizmos;
        [SerializeField, Min(0.0001f)] private float m_debugForceScale = 0.002f;

        private readonly SyncVar<int> m_frontLeftOccupant = new(-1);
        private readonly SyncVar<int> m_frontRightOccupant = new(-1);
        private readonly SyncVar<int> m_rearLeftOccupant = new(-1);
        private readonly SyncVar<int> m_rearRightOccupant = new(-1);
        private readonly SyncVar<float> m_cooperationEfficiency = new(1f);
        private readonly PlayerCouchCarrier[] m_serverOccupants = new PlayerCouchCarrier[MaximumCarryPoints];
        private readonly Vector3[] m_debugHorizontalForces = new Vector3[MaximumCarryPoints];
        private readonly Vector3[] m_debugLiftForces = new Vector3[MaximumCarryPoints];
        private readonly Vector3[] m_debugMovementIntents = new Vector3[MaximumCarryPoints];

        public Rigidbody CouchRigidbody => m_rigidbody;
        public float Weight => m_weight;
        public int ActiveCarrierCount => CountActiveCarriers();
        public float CurrentCooperationEfficiency => m_cooperationEfficiency.Value;
        public Vector3 CurrentVelocity => m_rigidbody != null ? m_rigidbody.linearVelocity : Vector3.zero;
        internal int ServerCarrierCount => IsServerInitialized ? CountServerCarriers() : 0;

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            ApplyWeightAndStability();
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
            if (!IsServerInitialized)
            {
                m_rigidbody.isKinematic = true;
            }
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || m_rigidbody == null || m_rigidbody.isKinematic)
            {
                return;
            }

            int carrierCount = CountServerCarriers();
            Vector3 combinedMovementIntent = CalculateCombinedMovementIntent(out float totalIntentMagnitude);
            float cooperationEfficiency = CalculateCooperationEfficiency(
                carrierCount,
                combinedMovementIntent,
                totalIntentMagnitude);
            m_cooperationEfficiency.Value = cooperationEfficiency;
            float totalCarrierEfficiency = CalculateTotalCarrierEfficiency(carrierCount);
            float perCarrierEfficiency = carrierCount > 0 ? totalCarrierEfficiency / carrierCount : 0f;
            float cooperationForceScale = Mathf.Lerp(
                m_minimumConflictEfficiency,
                1f,
                cooperationEfficiency);
            float weightForce = m_weight * Mathf.Abs(Physics.gravity.y);
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
                    (horizontalError * m_carryForce - horizontalVelocity * m_damping) *
                    perCarrierEfficiency * cooperationForceScale;
                horizontalForce = Vector3.ClampMagnitude(horizontalForce, m_maximumHorizontalForce);

                float heightError = targetPosition.y - pointPosition.y;
                float liftForce = supportPerCarrier + Mathf.Max(0f, heightError) * m_liftForce -
                    pointVelocity.y * m_liftDamping;
                liftForce = Mathf.Clamp(
                    liftForce,
                    0f,
                    m_maximumLiftForce);
                Vector3 verticalForce = Vector3.up * liftForce;

                Vector3 applicationPoint = Vector3.Lerp(
                    m_rigidbody.worldCenterOfMass,
                    pointPosition,
                    m_rotationInfluence);
                m_rigidbody.AddForceAtPosition(horizontalForce, applicationPoint, ForceMode.Force);
                m_rigidbody.AddForceAtPosition(verticalForce, pointPosition, ForceMode.Force);

                m_debugHorizontalForces[index] = horizontalForce;
                m_debugLiftForces[index] = verticalForce;
                m_debugMovementIntents[index] = movementIntent;
            }

            Vector3 movementForce = combinedMovementIntent *
                (m_movementForce * perCarrierEfficiency * cooperationForceScale);
            m_rigidbody.AddForce(movementForce, ForceMode.Force);

            LimitVelocity();
        }

        public float CalculateCarrierSpeedMultiplier(float singleCarrierMultiplier, float maximumCooperativeMultiplier)
        {
            int carrierCount = Mathf.Max(1, ActiveCarrierCount);
            float totalEfficiency = CalculateTotalCarrierEfficiency(carrierCount);
            float countProgress = Mathf.InverseLerp(
                m_singleCarrierEfficiency,
                m_maximumCarrierEfficiency,
                totalEfficiency);
            float cooperation = carrierCount > 1 ? CurrentCooperationEfficiency : 1f;
            float cooperativeProgress = countProgress * cooperation;
            float baseMultiplier = Mathf.Lerp(
                singleCarrierMultiplier,
                maximumCooperativeMultiplier,
                cooperativeProgress);

            float effectiveWeight = m_weight / Mathf.Max(0.05f, totalEfficiency);
            float weightMobility = 1f / (1f + effectiveWeight * 0.01f);
            float weightMultiplier = Mathf.Lerp(0.75f, 1f, weightMobility);
            return Mathf.Clamp(baseMultiplier * weightMultiplier, 0.25f, 1f);
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

            float additionalCarriers = Mathf.Pow(carrierCount - 1, 0.75f);
            return Mathf.Min(
                m_maximumCarrierEfficiency,
                m_singleCarrierEfficiency + m_additionalCarrierEfficiency * additionalCarriers);
        }

        private Vector3 CalculateCombinedMovementIntent(out float totalIntentMagnitude)
        {
            Vector3 combinedIntent = Vector3.zero;
            totalIntentMagnitude = 0f;
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
            }

            return combinedIntent;
        }

        private static float CalculateCooperationEfficiency(
            int carrierCount,
            Vector3 combinedMovementIntent,
            float totalIntentMagnitude)
        {
            return carrierCount <= 1 || totalIntentMagnitude <= 0.001f
                ? 1f
                : Mathf.Clamp01(combinedMovementIntent.magnitude / totalIntentMagnitude);
        }

        private void ApplyWeightAndStability()
        {
            if (m_rigidbody == null)
            {
                return;
            }

            m_rigidbody.mass = Mathf.Max(1f, m_weight);
            m_rigidbody.maxAngularVelocity = Mathf.Max(0.1f, m_maximumAngularVelocity);
        }

        private void LimitVelocity()
        {
            if (m_rigidbody.linearVelocity.sqrMagnitude > m_maximumLinearVelocity * m_maximumLinearVelocity)
            {
                m_rigidbody.linearVelocity = Vector3.ClampMagnitude(
                    m_rigidbody.linearVelocity,
                    m_maximumLinearVelocity);
            }
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

                if (!m_showForceGizmos || point.PointIndex < 0 || point.PointIndex >= MaximumCarryPoints)
                {
                    continue;
                }

                int index = point.PointIndex;
                Gizmos.color = new Color(1f, 0.25f, 0.15f);
                Gizmos.DrawRay(point.transform.position, m_debugHorizontalForces[index] * m_debugForceScale);
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(point.transform.position, m_debugLiftForces[index] * m_debugForceScale);
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(point.transform.position, m_debugMovementIntents[index] * 0.75f);
            }
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            m_weight = Mathf.Max(1f, m_weight);
            m_maximumCarrierEfficiency = Mathf.Max(m_singleCarrierEfficiency, m_maximumCarrierEfficiency);
            ApplyWeightAndStability();
        }
#endif
    }
}
