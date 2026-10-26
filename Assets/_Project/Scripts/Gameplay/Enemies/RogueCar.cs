using CouchGuys.Gameplay.Traffic;
using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Dedicated Rogue Car prefab controller. Traffic owns replication and combat.</summary>
    [RequireComponent(typeof(TrafficVehicleController))]
    [DisallowMultipleComponent]
    public sealed class RogueCar : MonoBehaviour
    {
        private TrafficVehicleController m_vehicle;
        private Rigidbody m_body;
        [SerializeField, Tooltip("Default tuning; the active Hard delivery settings take precedence when spawned.")]
        private RogueCarSettings m_settings;
        private PlayerHealth m_target;
        private bool m_targetWasCarrier;
        private float m_refreshAt;
        private float m_stuckTime;
        private float m_reverseUntil;
        private float m_reverseSteering = 1f;
        private Vector3 m_progressPosition;
        private float m_structureDamageAt;

        private void Awake()
        {
            m_vehicle = GetComponent<TrafficVehicleController>();
            m_body = GetComponent<Rigidbody>();
        }

        internal void Initialise(RogueCarSettings settings)
        {
            m_settings = settings;
            m_vehicle = GetComponent<TrafficVehicleController>();
            m_body = GetComponent<Rigidbody>();
            m_progressPosition = m_body.position;
        }

        private static bool IsLiving(PlayerHealth player) => player != null &&
            player.isActiveAndEnabled && player.IsAlive && player.IsSpawned;

        private static bool IsCarrier(PlayerHealth player) =>
            player.TryGetComponent(out PlayerCouchCarrier carrier) && carrier.CarriedCouch != null;

        private void RefreshTarget()
        {
            PlayerHealth nearest = null;
            bool foundCarrier = false;
            float distance = float.PositiveInfinity;
            foreach (PlayerHealth player in PlayerHealth.Instances)
            {
                if (!IsLiving(player)) continue;
                bool carrier = IsCarrier(player);
                float candidateDistance = (player.transform.position - transform.position).sqrMagnitude;
                if ((!foundCarrier && carrier) || (carrier == foundCarrier && candidateDistance < distance))
                {
                    nearest = player;
                    distance = candidateDistance;
                    foundCarrier = carrier;
                }
            }
            m_target = nearest;
            m_targetWasCarrier = foundCarrier;
            m_refreshAt = Time.time + Mathf.Max(0.05f, m_settings.TargetRefreshInterval);
        }

        private void FixedUpdate()
        {
            if (m_settings == null || !m_vehicle.IsServerInitialized || m_vehicle.IsDestroyed) return;
            if (Time.time >= m_refreshAt || !IsLiving(m_target) ||
                (m_targetWasCarrier && !IsCarrier(m_target))) RefreshTarget();

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float speed = Vector3.Dot(m_body.linearVelocity, forward);
            if (m_target == null)
            {
                m_body.AddForce(-Vector3.ProjectOnPlane(m_body.linearVelocity, Vector3.up) *
                    Mathf.Max(0f, m_settings.Acceleration), ForceMode.Acceleration);
                return;
            }

            if ((m_body.position - m_progressPosition).sqrMagnitude > 0.64f)
            {
                m_progressPosition = m_body.position;
                m_stuckTime = 0f;
            }
            else if (Time.time >= m_reverseUntil)
            {
                m_stuckTime += Time.fixedDeltaTime;
                if (m_stuckTime >= Mathf.Max(0.1f, m_settings.StuckDetectionTime))
                {
                    m_reverseUntil = Time.time + 1.6f;
                    m_stuckTime = 0f;
                    m_reverseSteering = -m_reverseSteering;
                }
            }

            Vector3 toTarget = Vector3.ProjectOnPlane(m_target.transform.position - m_body.position, Vector3.up);
            float angle = Vector3.SignedAngle(forward, toTarget, Vector3.up);
            bool reversing = Time.time < m_reverseUntil;
            float steering = reversing ? m_reverseSteering : Mathf.Clamp(angle / 35f, -1f, 1f);
            float desiredSpeed = reversing ? -Mathf.Max(0.1f, m_settings.ReverseSpeed) :
                Mathf.Max(0.1f, m_settings.MaxSpeed) * Mathf.Lerp(1f, 0.35f, Mathf.Abs(angle) / 180f);
            // Propel along the chassis, never directly towards the target through a wall.
            float acceleration = Mathf.Max(0f, m_settings.Acceleration);
            m_body.AddForce(forward * Mathf.Clamp((desiredSpeed - speed) / Time.fixedDeltaTime,
                -acceleration, acceleration), ForceMode.Acceleration);
            Vector3 lateral = Vector3.Cross(Vector3.up, forward);
            m_body.AddForce(-lateral * Vector3.Dot(m_body.linearVelocity, lateral) * 7f, ForceMode.Acceleration);

            float yawLimit = Mathf.Min(Mathf.Max(0f, m_settings.TurnSpeed),
                Mathf.Abs(speed) / Mathf.Max(0.1f, m_settings.MinTurningRadius) * Mathf.Rad2Deg);
            float yaw = steering * Mathf.Sign(speed) * yawLimit;
            // Torque limits angular acceleration; the radius cap prevents turning in place.
            Vector3 angularVelocity = m_body.angularVelocity;
            float yawRadians = yaw * Mathf.Deg2Rad;
            m_body.AddTorque(Vector3.up * Mathf.Clamp((yawRadians - angularVelocity.y) / Time.fixedDeltaTime,
                -8f, 8f), ForceMode.Acceleration);
            m_vehicle.UpdateRogueImpactsServer(steering * 32f);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (m_settings == null || !m_vehicle.IsServerInitialized || m_vehicle.IsDestroyed ||
                Time.time < m_structureDamageAt || collision.collider.isTrigger ||
                collision.collider.GetComponentInParent<PlayerHealth>() != null ||
                collision.collider.GetComponentInParent<ThiefHealth>() != null) return;
            float impactSpeed = 0f;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                // Exclude road/terrain contact, including landings and road seams.
                if (Mathf.Abs(contact.normal.y) > 0.65f) continue;
                impactSpeed = Mathf.Max(impactSpeed, Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal)));
            }
            if (impactSpeed <= 0f || impactSpeed < m_settings.MinimumDamagingCollisionSpeed) return;
            m_structureDamageAt = Time.time + 0.65f;
            m_vehicle.ApplyBulletDamageServer(impactSpeed * Mathf.Max(0f, m_settings.StructureCollisionDamageMultiplier));
        }
    }
}
