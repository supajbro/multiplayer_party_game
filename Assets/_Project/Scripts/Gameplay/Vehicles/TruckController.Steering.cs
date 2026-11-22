using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    public sealed partial class TruckController
    {
        private void SimulateSteering(float forwardSpeed, float dt)
        {
            float speed = Mathf.Abs(forwardSpeed);
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(.01f, m_settings.MaxForwardSpeed / 3.6f));
            float steeringScale = Mathf.Lerp(1f, m_settings.HighSpeedSteeringMultiplier, speed01);
            float fullSteering = m_settings.SteeringSensitivity * steeringScale;
            m_steering = Mathf.MoveTowards(m_steering, m_driveInput.x * fullSteering,
                m_settings.SteeringResponseSpeed * m_settings.SteeringSensitivity * dt);

            float targetYaw = 0f;
            if (speed > .2f)
            {
                float authority = Mathf.Clamp01(m_settings.SteeringSpeedCurve != null
                    ? m_settings.SteeringSpeedCurve.Evaluate(speed01) : speed01);
                targetYaw = m_steering * Mathf.Sign(forwardSpeed) * authority * Mathf.Deg2Rad;
                if (m_settings.MinimumTurningRadius > 0f)
                {
                    // Scale the radius limit with input so partial steering still
                    // produces a wider turn than full steering at low speed.
                    float steeringFraction = Mathf.Clamp01(Mathf.Abs(m_steering) / Mathf.Max(.001f, fullSteering));
                    float maximumYaw = speed / m_settings.MinimumTurningRadius * steeringFraction;
                    targetYaw = Mathf.Clamp(targetYaw, -maximumYaw, maximumYaw);
                }
            }

            // MoveRotation steers the pose without removing collision angular
            // velocity. Control yaw itself, including when stationary or unoccupied,
            // and leave pitch/roll to the existing hover/anti-roll stabilisation.
            Vector3 angular = m_rigidbody.angularVelocity;
            float currentYaw = Vector3.Dot(angular, Vector3.up);
            float blend = 1f - Mathf.Exp(-Mathf.Max(.1f, m_settings.YawControlResponse) * Mathf.Max(0f, dt));
            float nextYaw = Mathf.Lerp(currentYaw, targetYaw, blend);
            m_rigidbody.angularVelocity = angular + Vector3.up * (nextYaw - currentYaw);
        }
    }
}
