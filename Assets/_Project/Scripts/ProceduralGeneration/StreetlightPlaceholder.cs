using UnityEngine;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Gameplay.Sled;

namespace CouchGuys.ProceduralGeneration
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class StreetlightPlaceholder : MonoBehaviour
    {
        [SerializeField] private Light m_light;
        [SerializeField, Min(0.1f)] private float m_minimumTruckSpeed = 1f;
        [SerializeField, Min(0.1f), Tooltip("Minimum sled impact speed needed to knock over the pole.")]
        private float m_minimumSledSpeed = 0.1f;
        [SerializeField, Min(0f), Tooltip("Multiplier on the truck's final obstacle collision damage when hitting this pole. 0 disables damage; 1 uses normal damage.")]
        private float m_truckCollisionDamageMultiplier = 0.2f;
        private Rigidbody m_body;
        public bool IsKnockedDown { get; private set; }
        public float TruckCollisionDamageMultiplier => Mathf.Max(0f, m_truckCollisionDamageMultiplier);

        private void Awake()
        {
            m_body = GetComponent<Rigidbody>();
            m_body.isKinematic = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsKnockedDown || collision.rigidbody == null) return;
            TruckController truck = collision.rigidbody.GetComponent<TruckController>();
            SledController sled = collision.rigidbody.GetComponent<SledController>();
            // Vehicles simulate on the server. Clients only receive the resulting light poses.
            bool serverImpact = truck != null ? truck.IsServerInitialized :
                sled != null && sled.IsServerInitialized;
            float minimumSpeed = truck != null ? m_minimumTruckSpeed : m_minimumSledSpeed;
            if (!serverImpact || collision.relativeVelocity.sqrMagnitude < minimumSpeed * minimumSpeed) return;

            IsKnockedDown = true;
            m_body.isKinematic = false;
            Vector3 velocity = collision.rigidbody.linearVelocity;
            // The solver may already have stopped the vehicle against the anchored pole.
            if (velocity.sqrMagnitude < minimumSpeed * minimumSpeed)
                velocity = Vector3.ProjectOnPlane(transform.position - collision.rigidbody.position,
                    Vector3.up).normalized * collision.relativeVelocity.magnitude;
            Vector3 point = collision.GetContact(0).point;
            m_body.AddForceAtPosition(velocity * m_body.mass, point, ForceMode.Impulse);
            Vector3 direction = Vector3.ProjectOnPlane(velocity, Vector3.up).normalized;
            m_body.AddTorque(Vector3.Cross(Vector3.up, direction) * m_body.mass * 2f, ForceMode.Impulse);
        }

        public void ApplyNetworkPose(Vector3 position, Quaternion rotation)
        {
            IsKnockedDown = true;
            m_body.isKinematic = true;
            m_body.position = position;
            m_body.rotation = rotation;
        }

        public void Configure(bool enabled, Color colour, float intensity, float range)
        {
            if (m_light == null) m_light = GetComponentInChildren<Light>(true);
            if (m_light == null) return;
            m_light.enabled = enabled;
            m_light.color = colour;
            m_light.intensity = Mathf.Max(0f, intensity);
            m_light.range = Mathf.Max(0.1f, range);
        }
    }
}
