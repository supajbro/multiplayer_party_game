using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Transient bat impulse state used by the existing vehicle motors.</summary>
    public sealed class BaseballBatVehicleLaunch
    {
        public bool Active { get; private set; }
        private float m_recoverAfter;
        private float m_angularDamping;

        public void Launch(Rigidbody body, Vector3 velocity, Vector3 spin)
        {
            if (body == null || body.isKinematic) return;
            if (!Active) m_angularDamping = body.angularDamping;
            Active = true;
            m_recoverAfter = Time.time + .6f;
            body.WakeUp();
            body.angularDamping = Mathf.Min(m_angularDamping, .2f);
            // VelocityChange is an impulse divided by mass, so heavy cars launch as clearly as light ones.
            body.AddForce(velocity, ForceMode.VelocityChange);
            body.AddTorque(spin, ForceMode.VelocityChange);
        }

        public bool Tick(Rigidbody body)
        {
            if (!Active || body == null) return false;
            if (Time.time < m_recoverAfter) return true;
            if (body.linearVelocity.y > 1f || !body.SweepTest(Vector3.down, out RaycastHit hit, .35f,
                    QueryTriggerInteraction.Ignore) || hit.normal.y < .5f) return true;
            if (Vector3.Dot(body.transform.up, Vector3.up) < .75f)
            {
                Vector3 axis = Vector3.Cross(body.transform.up, Vector3.up);
                if (axis.sqrMagnitude < .01f) axis = body.transform.forward;
                body.AddTorque(axis.normalized * 10f - body.angularVelocity * 2f, ForceMode.Acceleration);
                return true;
            }
            Active = false;
            body.angularDamping = m_angularDamping;
            return false;
        }
    }
}
