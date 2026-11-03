using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
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
        private RogueVehicleMotor m_motor;
        private float m_structureDamageAt;

        private void Awake()
        {
            m_vehicle = GetComponent<TrafficVehicleController>();
            m_body = GetComponent<Rigidbody>();
            m_motor = new RogueVehicleMotor(m_body);
        }

        internal void Initialise(RogueCarSettings settings)
        {
            m_settings = settings;
            m_vehicle = GetComponent<TrafficVehicleController>();
            m_body = GetComponent<Rigidbody>();
            m_motor = new RogueVehicleMotor(m_body);
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

            if (m_target == null)
            {
                m_motor.Stop(m_settings);
                return;
            }
            float steering = m_motor.Drive(m_settings, m_target.transform.position,
                m_settings.MaxSpeed, 0f, Time.fixedDeltaTime);
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
