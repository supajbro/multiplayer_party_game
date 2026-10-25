using UnityEngine;
using CouchGuys.Gameplay.Vehicles;

namespace CouchGuys.ProceduralGeneration
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class StreetlightPlaceholder : MonoBehaviour
    {
        [SerializeField] private Light m_light;
        [SerializeField, Min(0.1f)] private float m_minimumTruckSpeed = 1f;
        private Rigidbody m_body;
        public bool IsKnockedDown { get; private set; }

        private void Awake()
        {
            m_body = GetComponent<Rigidbody>();
            m_body.isKinematic = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsKnockedDown || collision.rigidbody == null) return;
            TruckController truck = collision.rigidbody.GetComponent<TruckController>();
            // Trucks simulate on the server. Clients only receive the resulting light poses.
            if (truck == null || !truck.IsServerInitialized ||
                collision.relativeVelocity.sqrMagnitude < m_minimumTruckSpeed * m_minimumTruckSpeed) return;

            IsKnockedDown = true;
            m_body.isKinematic = false;
            Vector3 velocity = collision.rigidbody.linearVelocity;
            // The solver may already have stopped the truck against the anchored pole.
            if (velocity.sqrMagnitude < m_minimumTruckSpeed * m_minimumTruckSpeed)
                velocity = Vector3.ProjectOnPlane(transform.position - truck.transform.position,
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
