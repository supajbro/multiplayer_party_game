using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Depot;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Object;
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
        private NetworkObject m_couch;
        private bool m_targetWasCarrier;
        private float m_refreshAt;
        private RogueVehicleMotor m_motor;
        private float m_structureDamageAt;
        [SerializeField] private ThreatTelegraph m_telegraph;
        private Vector3 m_chargeDestination;
        public bool CanDamagePlayers => m_telegraph == null ||
            (m_telegraph.Phase == ThreatAttackPhase.Committed && m_telegraph.Remaining > 0f);

        private void Awake()
        {
            m_vehicle = GetComponent<TrafficVehicleController>();
            m_body = GetComponent<Rigidbody>();
            m_motor = new RogueVehicleMotor(m_body);
            m_telegraph ??= GetComponent<ThreatTelegraph>();
        }

        internal void Initialise(RogueCarSettings settings, NetworkObject couch = null)
        {
            m_settings = settings;
            m_couch = couch;
            m_target = null;
            m_refreshAt = 0f;
            m_vehicle = GetComponent<TrafficVehicleController>();
            m_body = GetComponent<Rigidbody>();
            m_motor = new RogueVehicleMotor(m_body);
        }

        private static bool IsLiving(PlayerHealth player) => player != null &&
            player.IsEnemyTargetable;

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
            if (m_settings == null || !m_vehicle.IsServerInitialized) return;
            if (m_vehicle.IsDestroyed || m_vehicle.IsBatLaunched)
            { m_telegraph?.CancelServer(); return; }
            if (m_telegraph != null && UpdateChargeServer()) return;
            if (Time.time >= m_refreshAt || !IsLiving(m_target) ||
                (m_targetWasCarrier && !IsCarrier(m_target))) RefreshTarget();

            Transform target = m_target != null ? m_target.transform
                : m_couch != null && m_couch.IsSpawned ? m_couch.transform : null;
            if (target == null)
            {
                m_motor.Stop(m_settings);
                m_telegraph?.CancelServer();
                return;
            }
            if (DepotSafeZone.Contains(target.position) || DepotSafeZone.Contains(transform.position))
            { m_motor.Stop(m_settings, true); m_telegraph?.CancelServer(); return; }
            Vector3 offset = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
            if (m_telegraph != null && offset.magnitude <= m_settings.ChargeRange &&
                Vector3.Angle(transform.forward, offset) <= 22f &&
                !DepotSafeZone.Intersects(transform.position, target.position))
            {
                m_motor.Stop(m_settings, true);
                // The chassis has already lined up. Keep this bearing throughout the
                // stationary warning so a sidestep cannot swivel the charge behind it.
                m_telegraph.PrepareServer(null, target.position, m_settings.ChargeWindup);
                return;
            }
            float steering = m_motor.Drive(m_settings, target.position,
                m_settings.MaxSpeed * (m_telegraph != null ? m_settings.ApproachSpeedMultiplier : 1f),
                0f, Time.fixedDeltaTime);
            m_vehicle.UpdateRogueImpactsServer(steering * 32f);
        }

        private bool UpdateChargeServer()
        {
            if (m_telegraph.Phase == ThreatAttackPhase.Committed)
            {
                if (m_telegraph.Remaining <= 0f || DepotSafeZone.Intersects(transform.position,
                    transform.position + m_body.linearVelocity * Time.fixedDeltaTime))
                {
                    m_motor.Stop(m_settings, true);
                    m_telegraph.RecoverServer(m_settings.ChargeRecovery);
                }
                else
                {
                    float steering = m_motor.Drive(m_settings, m_chargeDestination, m_settings.MaxSpeed,
                        0f, Time.fixedDeltaTime, true, true, m_settings.ChargeTurnSpeed);
                    m_vehicle.UpdateRogueImpactsServer(steering * 32f);
                }
                return true;
            }
            if (m_telegraph.IsRecovering)
            { m_motor.Stop(m_settings, true); return true; }
            if (!m_telegraph.IsPreparing) return false;
            m_motor.Stop(m_settings, true);
            if ((m_target != null && !IsLiving(m_target)) ||
                DepotSafeZone.Intersects(transform.position, m_telegraph.AimPoint))
            { m_telegraph.RecoverServer(m_settings.ChargeRecovery); return true; }
            if (m_telegraph.Phase == ThreatAttackPhase.Preparing &&
                m_telegraph.Remaining <= Mathf.Min(m_settings.ChargeAimCommitLead, m_settings.ChargeWindup))
                m_telegraph.LockServer(m_telegraph.AimPoint);
            if (m_telegraph.Remaining > 0f) return true;
            Vector3 direction = Vector3.ProjectOnPlane(m_telegraph.AimPoint - transform.position, Vector3.up).normalized;
            m_chargeDestination = transform.position + direction * (m_settings.MaxSpeed * m_settings.ChargeDuration + 10f);
            m_telegraph.CommitServer(m_settings.ChargeDuration);
            return true;
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (m_settings == null || !m_vehicle.IsServerInitialized || m_vehicle.IsDestroyed ||
                Time.time < m_structureDamageAt || collision.collider.isTrigger ||
                collision.collider.GetComponentInParent<PlayerHealth>() != null ||
                collision.collider.GetComponentInParent<ThiefHealth>() != null) return;
            // The truck owns this special collision on the server. Do not let
            // callback order destroy/freeze the rogue car before it can be launched.
            if (collision.collider.GetComponentInParent<GetawayTruckController>() != null) return;
            // TrafficVehicleController chooses exactly one wreck for a car collision.
            if (collision.collider.GetComponentInParent<TrafficVehicleController>() != null) return;
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

        private void OnDisable()
        {
            if (m_vehicle != null && m_vehicle.IsServerInitialized) m_telegraph?.CancelServer();
        }
    }
}
