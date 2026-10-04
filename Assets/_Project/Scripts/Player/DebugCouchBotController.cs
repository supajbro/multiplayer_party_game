using CouchGuys.Gameplay.Couch;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>
    /// Drives an unowned network Player on the server for repeatable couch tests.
    /// Human input, special couch forces, and client-side bot simulation are deliberately avoided.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerCouchCarrier))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class DebugCouchBotController : MonoBehaviour
    {
        private static CouchCarryPoint[] s_cachedCarryPoints;
        private static float s_nextCarryPointCacheRefreshTime;

        private CharacterController m_characterController;
        private PlayerCouchCarrier m_carrier;
        private NetworkObject m_networkObject;
        private DebugCouchBotSettings m_settings;
        private CouchCarryPoint m_targetPoint;
        private Vector3 m_wanderDirection;
        private float m_verticalVelocity;
        private float m_nextTargetSearchTime;
        private float m_nextGrabAttemptTime;
        private float m_nextWanderDirectionTime;
        private int m_botIndex;
        private bool m_isInitialised;

        private void Awake()
        {
            ResolveReferences();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedCache()
        {
            s_cachedCarryPoints = null;
            s_nextCarryPointCacheRefreshTime = 0f;
        }

        internal void Initialise(DebugCouchBotSettings settings, int botIndex)
        {
            if (settings == null)
            {
                return;
            }

            ResolveReferences();
            m_settings = settings;
            m_botIndex = Mathf.Clamp(botIndex, 0, 3);
            m_isInitialised = true;
            enabled = true;
        }

        private void FixedUpdate()
        {
            if (!m_isInitialised || m_settings == null || m_networkObject == null ||
                !m_networkObject.IsServerInitialized || m_networkObject.Owner.IsValid)
            {
                return;
            }

            if (m_carrier.IsCarrying)
            {
                TickCarrying();
            }
            else
            {
                TickSeeking();
            }
        }

        private void TickSeeking()
        {
            if (!IsTargetValid())
            {
                m_targetPoint = null;
                if (Time.unscaledTime < m_nextTargetSearchTime)
                {
                    Move(Vector3.zero, m_settings.ApproachSpeed);
                    return;
                }

                m_nextTargetSearchTime = Time.unscaledTime + m_settings.TargetSearchInterval;
                m_targetPoint = FindBestTargetPoint();
            }

            if (m_targetPoint == null)
            {
                Move(Vector3.zero, m_settings.ApproachSpeed);
                return;
            }

            Vector3 standPosition = CalculateStandPosition(m_targetPoint);
            Vector3 offset = Vector3.ProjectOnPlane(standPosition - transform.position, Vector3.up);
            float stopDistance = Mathf.Max(0.08f, m_characterController.radius * 0.5f);
            Vector3 movement = offset.sqrMagnitude > stopDistance * stopDistance
                ? offset.normalized
                : Vector3.zero;
            Move(movement, m_settings.ApproachSpeed);

            if (offset.sqrMagnitude <= stopDistance * stopDistance &&
                Time.unscaledTime >= m_nextGrabAttemptTime)
            {
                bool grabbed = m_carrier.TryGrabPointForDebugBotServer(
                    m_targetPoint.Couch,
                    m_targetPoint.PointIndex);
                m_nextGrabAttemptTime = Time.unscaledTime + m_settings.RetryDelay;
                if (!grabbed)
                {
                    m_targetPoint = null;
                }
            }
        }

        private void TickCarrying()
        {
            CouchCarryController couch = m_carrier.GetCarriedCouchServer();
            if (couch == null)
            {
                m_carrier.SetDebugBotMovementIntentServer(Vector3.zero);
                return;
            }

            Vector3 movementIntent = CalculateMovementIntent(couch);
            Vector3 constrainedMovement = movementIntent;
            CouchCarryPoint carryPoint = couch.GetPoint(m_carrier.CarriedPointIndex);
            if (carryPoint != null)
            {
                constrainedMovement = ThirdPersonPlayerController.ApplyDirectionalResistance(
                    movementIntent,
                    transform.position,
                    carryPoint.transform.position,
                    m_carrier.ComfortableCarryDistance,
                    m_carrier.MaximumCarrySeparation);
            }

            m_carrier.SetDebugBotMovementIntentServer(movementIntent);
            float speedMultiplier = m_carrier.CalculateCarryingSpeedMultiplier(couch);
            Vector3 facingDirection = carryPoint != null
                ? Vector3.ProjectOnPlane(carryPoint.transform.position - transform.position, Vector3.up)
                : Vector3.zero;
            Vector3 attachmentCorrection = carryPoint != null
                ? ThirdPersonPlayerController.CalculateAttachmentCorrection(
                    transform.position,
                    carryPoint.transform.position,
                    m_carrier.MaximumCarrySeparation)
                : Vector3.zero;
            Move(
                constrainedMovement,
                m_settings.CarryingSpeed * speedMultiplier,
                facingDirection,
                attachmentCorrection);
        }

        private Vector3 CalculateMovementIntent(CouchCarryController couch)
        {
            PlayerCouchCarrier humanCarrier = couch.GetFirstHumanCarrierServer(m_carrier);
            Vector3 humanIntent = humanCarrier != null
                ? humanCarrier.GetServerMovementIntent()
                : Vector3.zero;

            Vector3 result = m_settings.Behaviour switch
            {
                DebugCouchBotSettings.BehaviourState.CooperateWithPlayer => humanIntent,
                DebugCouchBotSettings.BehaviourState.PullAgainstPlayer => -humanIntent,
                DebugCouchBotSettings.BehaviourState.HoldPosition => Vector3.zero,
                DebugCouchBotSettings.BehaviourState.RotateClockwise => CalculateTangentialDirection(couch, true),
                DebugCouchBotSettings.BehaviourState.RotateAnticlockwise => CalculateTangentialDirection(couch, false),
                DebugCouchBotSettings.BehaviourState.Wander => GetWanderDirection(),
                DebugCouchBotSettings.BehaviourState.MoveInConfiguredDirection => m_settings.ConfiguredDirection,
                _ => Vector3.zero
            };

            return Vector3.ClampMagnitude(Vector3.ProjectOnPlane(result, Vector3.up), 1f);
        }

        private Vector3 CalculateTangentialDirection(CouchCarryController couch, bool clockwise)
        {
            Vector3 radial = Vector3.ProjectOnPlane(
                transform.position - couch.CouchRigidbody.worldCenterOfMass,
                Vector3.up);
            if (radial.sqrMagnitude < 0.001f)
            {
                radial = transform.forward;
            }

            Vector3 tangent = Vector3.Cross(Vector3.up, radial.normalized);
            return clockwise ? tangent : -tangent;
        }

        private Vector3 GetWanderDirection()
        {
            if (Time.unscaledTime >= m_nextWanderDirectionTime || m_wanderDirection.sqrMagnitude < 0.01f)
            {
                Vector2 randomDirection = Random.insideUnitCircle.normalized;
                m_wanderDirection = new Vector3(randomDirection.x, 0f, randomDirection.y);
                m_nextWanderDirectionTime = Time.unscaledTime + m_settings.WanderDirectionInterval;
            }

            return m_wanderDirection;
        }

        private CouchCarryPoint FindBestTargetPoint()
        {
            CouchCarryPoint[] points = GetCachedCarryPoints();
            CouchCarryPoint bestPoint = null;
            float bestScore = float.PositiveInfinity;
            bool bestCouchHasCarrier = false;
            bool bestMatchesPreference = false;
            int configuredPreference = (int)m_settings.PreferredCarryPoint;
            int preferredIndex = configuredPreference >= 0
                ? (configuredPreference + m_botIndex) % CouchCarryController.MaximumCarryPoints
                : m_botIndex;

            foreach (CouchCarryPoint point in points)
            {
                if (point == null || !point.IsAvailable || point.Couch == null || !point.Couch.IsSpawned)
                {
                    continue;
                }

                float score = (point.transform.position - transform.position).sqrMagnitude;
                bool couchHasCarrier = point.Couch.ActiveCarrierCount > 0;
                bool matchesPreference = point.PointIndex == preferredIndex;
                if ((couchHasCarrier && !bestCouchHasCarrier) ||
                    (couchHasCarrier == bestCouchHasCarrier && matchesPreference && !bestMatchesPreference) ||
                    (couchHasCarrier == bestCouchHasCarrier && matchesPreference == bestMatchesPreference &&
                        score < bestScore))
                {
                    bestScore = score;
                    bestPoint = point;
                    bestCouchHasCarrier = couchHasCarrier;
                    bestMatchesPreference = matchesPreference;
                }
            }

            return bestPoint;
        }

        private CouchCarryPoint[] GetCachedCarryPoints()
        {
            if (s_cachedCarryPoints == null || Time.unscaledTime >= s_nextCarryPointCacheRefreshTime)
            {
                s_cachedCarryPoints = FindObjectsByType<CouchCarryPoint>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
                s_nextCarryPointCacheRefreshTime = Time.unscaledTime + m_settings.TargetSearchInterval;
            }

            return s_cachedCarryPoints;
        }

        private bool IsTargetValid()
        {
            return m_targetPoint != null && m_targetPoint.IsAvailable &&
                m_targetPoint.Couch != null && m_targetPoint.Couch.IsSpawned;
        }

        private Vector3 CalculateStandPosition(CouchCarryPoint point)
        {
            Vector3 awayFromCouch = Vector3.ProjectOnPlane(
                point.transform.position - point.Couch.CouchRigidbody.worldCenterOfMass,
                Vector3.up);
            if (awayFromCouch.sqrMagnitude < 0.001f)
            {
                awayFromCouch = -point.Couch.transform.forward;
            }

            float standOff = Mathf.Min(m_settings.PointStandOff, m_carrier.MaximumCarrySeparation);
            Vector3 standPosition = point.transform.position + awayFromCouch.normalized * standOff;
            standPosition.y = transform.position.y;
            return standPosition;
        }

        private void Move(
            Vector3 horizontalDirection,
            float speed,
            Vector3 facingDirection = default,
            Vector3 attachmentCorrection = default)
        {
            Vector3 direction = Vector3.ClampMagnitude(
                Vector3.ProjectOnPlane(horizontalDirection, Vector3.up),
                1f);
            Vector3 lookDirection = facingDirection.sqrMagnitude > 0.001f
                ? facingDirection.normalized
                : direction;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    m_settings.TurnSpeed * Time.fixedDeltaTime);
            }

            if (m_characterController.isGrounded && m_verticalVelocity < 0f)
            {
                m_verticalVelocity = -2f;
            }
            else
            {
                m_verticalVelocity = Mathf.Max(-50f, m_verticalVelocity + Physics.gravity.y * Time.fixedDeltaTime);
            }

            Vector3 velocity = direction * Mathf.Max(0f, speed) + Vector3.up * m_verticalVelocity;
            m_characterController.Move(velocity * Time.fixedDeltaTime + attachmentCorrection);
        }

        private void ResolveReferences()
        {
            m_characterController ??= GetComponent<CharacterController>();
            m_carrier ??= GetComponent<PlayerCouchCarrier>();
            m_networkObject ??= GetComponent<NetworkObject>();
        }
    }
}
