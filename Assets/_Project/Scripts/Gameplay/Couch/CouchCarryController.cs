using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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

        [Header("Spring Physics")]
        [SerializeField, Min(0f)] private float m_carryForce = 500f;
        [SerializeField, Min(0f)] private float m_damping = 65f;
        [SerializeField, Min(0f)] private float m_maximumForce = 900f;
        [SerializeField, Range(0f, 1f)] private float m_rotationInfluence = 0.85f;

        private readonly SyncVar<int> m_frontLeftOccupant = new(-1);
        private readonly SyncVar<int> m_frontRightOccupant = new(-1);
        private readonly SyncVar<int> m_rearLeftOccupant = new(-1);
        private readonly SyncVar<int> m_rearRightOccupant = new(-1);
        private readonly PlayerCouchCarrier[] m_serverOccupants = new PlayerCouchCarrier[MaximumCarryPoints];

        public Rigidbody CouchRigidbody => m_rigidbody;

        private void Awake()
        {
            m_rigidbody ??= GetComponent<Rigidbody>();
            // No peer simulates before FishNet assigns authority. The server switches
            // this body back to dynamic in OnStartServer.
            m_rigidbody.isKinematic = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_rigidbody.isKinematic = false;
            ClearAllOccupantsServer();
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

            for (int index = 0; index < MaximumCarryPoints; index++)
            {
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

                float separation = Vector3.Distance(carrier.transform.position, carryPoint.transform.position);
                if (separation > carrier.MaximumCarrySeparation)
                {
                    ReleasePointServer(index, carrier);
                    continue;
                }

                Vector3 pointPosition = carryPoint.transform.position;
                Vector3 targetPosition = carrier.CalculateDesiredCarryPosition(m_rigidbody.worldCenterOfMass);
                Vector3 pointVelocity = m_rigidbody.GetPointVelocity(pointPosition);
                Vector3 force = (targetPosition - pointPosition) * m_carryForce - pointVelocity * m_damping;
                force = Vector3.ClampMagnitude(force, m_maximumForce);

                Vector3 applicationPoint = Vector3.Lerp(
                    m_rigidbody.worldCenterOfMass,
                    pointPosition,
                    m_rotationInfluence);
                m_rigidbody.AddForceAtPosition(force, applicationPoint, ForceMode.Force);
            }
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

        private void ClearAllOccupantsServer()
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
            }
        }
#endif
    }
}
