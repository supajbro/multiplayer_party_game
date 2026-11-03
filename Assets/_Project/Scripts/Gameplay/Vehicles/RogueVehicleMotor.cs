using CouchGuys.Gameplay.Enemies;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.Gameplay.Vehicles
{
    // Shared by rogue cars and getaway trucks. This is the only chassis motion writer.
    internal sealed class RogueVehicleMotor
    {
        private readonly Rigidbody m_body;
        private Vector3 m_progressPosition;
        private float m_stuckTime;
        private float m_reverseRemaining;
        private float m_reverseSteering = 1f;
        private bool m_targetReverse;
        private bool m_turnaround;
        private bool m_turnReverse;
        private Vector3 m_turnHeading;
        private float m_turnSign;
        private float m_turnPhaseTime;
        private float m_turnTime;
        private bool m_followRoad;
        private float m_groundAlignment;
        private float m_groundDamping;
        private readonly RaycastHit[] m_groundHits = new RaycastHit[16];

        internal void ConfigureRoadContact(float alignment, float damping)
        {
            m_followRoad = true;
            m_groundAlignment = alignment;
            m_groundDamping = damping;
        }

        private bool TryRoadNormal(out Vector3 normal)
        {
            normal = Vector3.up;
            if (!m_followRoad) return false;
            // Use the body's physics scene so isolated physics tests sample the same road.
            int count = m_body.gameObject.scene.GetPhysicsScene().Raycast(m_body.position + Vector3.up * 2.5f,
                Vector3.down, m_groundHits, 3.6f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = m_groundHits[i];
                if (hit.collider.attachedRigidbody == m_body || hit.normal.y < .5f ||
                    !TruckController.IsGroundSurface(hit.collider) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                normal = hit.normal;
                found = true;
            }
            return found;
        }

        private Vector3 GroundAlignment(Vector3 normal) =>
            Vector3.Cross(m_body.rotation * Vector3.up, normal) * m_groundAlignment -
            Vector3.ProjectOnPlane(m_body.angularVelocity, normal) * m_groundDamping;

        internal RogueVehicleMotor(Rigidbody body)
        {
            m_body = body;
            m_progressPosition = body.position;
        }

        internal float Drive(RogueCarSettings settings, Vector3 target, float maximumSpeed,
            float stoppingDistance, float dt, bool forwardOnly = false)
        {
            if (m_body.isKinematic) m_body.isKinematic = false;
            m_reverseRemaining = Mathf.Max(0f, m_reverseRemaining - dt);
            bool grounded = TryRoadNormal(out Vector3 normal);
            Vector3 forward = Vector3.ProjectOnPlane(m_body.rotation * Vector3.forward, normal).normalized;
            Vector3 toTarget = Vector3.ProjectOnPlane(target - m_body.position, normal);
            float speed = Vector3.Dot(m_body.linearVelocity, forward);
            if (toTarget.magnitude <= stoppingDistance)
            {
                Stop(settings);
                return 0f;
            }
            if ((m_body.position - m_progressPosition).sqrMagnitude > .64f)
            {
                m_progressPosition = m_body.position;
                m_stuckTime = 0f;
            }
            else if (m_reverseRemaining <= 0f && !m_turnaround)
            {
                m_stuckTime += dt;
                if (m_stuckTime >= Mathf.Max(.1f, settings.StuckDetectionTime))
                {
                    m_reverseRemaining = 1.6f;
                    m_stuckTime = 0f;
                    m_reverseSteering = -m_reverseSteering;
                }
            }
            float angle = Vector3.SignedAngle(forward, toTarget, normal);
            if (forwardOnly && !m_turnaround && Mathf.Abs(angle) > 110f &&
                toTarget.magnitude < settings.MinTurningRadius * 3f)
            {
                m_turnaround = true;
                m_turnReverse = false;
                m_turnHeading = toTarget.normalized;
                m_turnSign = Mathf.Sign(angle);
                m_turnPhaseTime = 0f;
                m_turnTime = 0f;
                m_reverseRemaining = 0f;
                m_stuckTime = 0f;
            }
            if (m_turnaround)
            {
                m_turnPhaseTime += dt;
                m_turnTime += dt;
                float remainingAngle = Mathf.Abs(Vector3.SignedAngle(forward, m_turnHeading, normal));
                if (remainingAngle < 35f || m_turnTime >= settings.TurnaroundTimeout || !forwardOnly)
                    m_turnaround = false;
                else if (m_turnPhaseTime >= (m_turnReverse ? settings.TurnaroundReverseDuration : settings.TurnaroundForwardDuration))
                {
                    m_turnReverse = !m_turnReverse;
                    m_turnPhaseTime = 0f;
                }
            }
            if (forwardOnly) m_targetReverse = false;
            else if (settings.ReverseTowardsNearbyTargets)
            {
                if (Mathf.Abs(angle) > 110f && toTarget.magnitude < settings.MinTurningRadius * 2f) m_targetReverse = true;
                else if (Mathf.Abs(angle) < 75f || toTarget.magnitude > settings.MinTurningRadius * 3f) m_targetReverse = false;
            }
            bool stuckReverse = m_reverseRemaining > 0f;
            bool reversing = m_turnaround ? m_turnReverse : stuckReverse || m_targetReverse;
            float steering = m_turnaround ? m_turnSign * Mathf.Sign(speed) : stuckReverse ? m_reverseSteering : m_targetReverse
                ? Mathf.Clamp(-Vector3.SignedAngle(forward, -toTarget, normal) / 35f, -1f, 1f)
                : Mathf.Clamp(angle / 35f, -1f, 1f);
            float desiredSpeed = reversing ? -Mathf.Max(.1f, settings.ReverseSpeed) :
                Mathf.Min(settings.MaxSpeed, maximumSpeed) * Mathf.Lerp(1f, .35f, Mathf.Abs(angle) / 180f);
            if (m_turnaround && !reversing) desiredSpeed = Mathf.Min(desiredSpeed, settings.ReverseSpeed);
            if (!reversing && stoppingDistance > 0f)
                desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Sqrt(2f * settings.Acceleration *
                    Mathf.Max(0f, toTarget.magnitude - stoppingDistance)));
            float acceleration = Mathf.Max(0f, settings.Acceleration);
            m_body.AddForce(forward * Mathf.Clamp((desiredSpeed - speed) / dt,
                -acceleration, acceleration), ForceMode.Acceleration);
            Vector3 lateral = Vector3.Cross(normal, forward);
            m_body.AddForce(-lateral * Vector3.Dot(m_body.linearVelocity, lateral) * 7f, ForceMode.Acceleration);
            float yawLimit = Mathf.Min(Mathf.Max(0f, settings.TurnSpeed),
                Mathf.Abs(speed) / Mathf.Max(.1f, settings.MinTurningRadius) * Mathf.Rad2Deg);
            float yaw = steering * Mathf.Sign(speed) * yawLimit * Mathf.Deg2Rad;
            Vector3 angular = normal * Mathf.Clamp((yaw - Vector3.Dot(m_body.angularVelocity, normal)) / dt,
                -settings.AngularAcceleration, settings.AngularAcceleration);
            if (grounded)
            {
                angular += GroundAlignment(normal);
                // Same modest speed-dependent road pressure as traffic cars.
                m_body.AddForce(-normal * Mathf.Clamp(Mathf.Abs(speed) * .35f, 0f, 5f), ForceMode.Acceleration);
            }
            ApplyAngularAcceleration(angular, settings);
            return steering;
        }

        private void ApplyAngularAcceleration(Vector3 acceleration, RogueCarSettings settings)
        {
            if (!settings.CompensateSteeringInertia)
            {
                m_body.AddTorque(acceleration, ForceMode.Acceleration);
                return;
            }
            Quaternion frame = m_body.rotation * m_body.inertiaTensorRotation;
            Vector3 torque = frame * Vector3.Scale(m_body.inertiaTensor, Quaternion.Inverse(frame) * acceleration);
            m_body.AddTorque(torque, ForceMode.Force);
        }

        internal void Stop(RogueCarSettings settings, bool holdPosition = false)
        {
            m_stuckTime = 0f;
            m_reverseRemaining = 0f;
            m_targetReverse = false;
            m_turnaround = false;
            m_progressPosition = m_body.position;
            if (m_body.isKinematic) return;
            float response = holdPosition ? 1f / Mathf.Max(.001f, Time.fixedDeltaTime) : Mathf.Max(0f, settings.Acceleration);
            bool grounded = TryRoadNormal(out Vector3 normal);
            m_body.AddForce(-Vector3.ProjectOnPlane(m_body.linearVelocity, normal) * response, ForceMode.Acceleration);
            ApplyAngularAcceleration(grounded ? GroundAlignment(normal) - normal * Vector3.Dot(m_body.angularVelocity, normal) * response
                : -m_body.angularVelocity * response, settings);
        }
    }
}
