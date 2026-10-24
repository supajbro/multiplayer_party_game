using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    /// <summary>Visual wheel motion from replicated movement and the existing AI steering.</summary>
    public sealed class TrafficWheelAnimator : MonoBehaviour
    {
        [SerializeField] private Transform[] m_frontWheels;
        [SerializeField] private Transform[] m_rearWheels;
        [SerializeField, Min(.01f)] private float m_radius = .36f;
        private Quaternion[] m_frontRest, m_rearRest;
        private Vector3 m_previousPosition;
        private float m_roll, m_steering, m_targetSteering;

        private void Awake()
        {
            m_frontRest = Capture(m_frontWheels);
            m_rearRest = Capture(m_rearWheels);
        }

        private void OnEnable() => m_previousPosition = transform.position;
        public void SetSteering(float degrees) => m_targetSteering = degrees;

        private static Quaternion[] Capture(Transform[] wheels)
        {
            Quaternion[] result = new Quaternion[wheels?.Length ?? 0];
            for (int i = 0; i < result.Length; i++)
                result[i] = wheels[i] != null ? wheels[i].localRotation : Quaternion.identity;
            return result;
        }

        private void LateUpdate()
        {
            Vector3 movement = transform.position - m_previousPosition;
            m_previousPosition = transform.position;
            // Ignore spawn/teleport jumps. Position deltas also work on kinematic clients.
            if (movement.sqrMagnitude < 25f)
                m_roll = (m_roll + Vector3.Dot(movement, transform.forward) / m_radius * Mathf.Rad2Deg) % 360f;
            m_steering = Mathf.Lerp(m_steering, m_targetSteering, 1f - Mathf.Exp(-12f * Time.deltaTime));
            Apply(m_frontWheels, m_frontRest, m_steering);
            Apply(m_rearWheels, m_rearRest, 0f);
        }

        private void Apply(Transform[] wheels, Quaternion[] rest, float steering)
        {
            for (int i = 0; i < rest.Length; i++)
            {
                if (wheels[i] == null) continue;
                // FBX wheel pivots have Unity-aligned axes; meshes are children of the pivots.
                wheels[i].localRotation = rest[i] * Quaternion.Euler(0f, steering, 0f) * Quaternion.Euler(m_roll, 0f, 0f);
            }
        }
    }
}
