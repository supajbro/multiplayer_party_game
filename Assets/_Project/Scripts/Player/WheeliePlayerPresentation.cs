using UnityEngine;
using CouchGuys.Gameplay.Weapons;

namespace CouchGuys.Player
{
    // Cosmetic movement only. The normal player controller and NetworkTransform own locomotion.
    [DefaultExecutionOrder(180)]
    public sealed class WheeliePlayerPresentation : MonoBehaviour
    {
        [SerializeField] private Transform m_wheel;
        [SerializeField] private Transform m_waist;
        [SerializeField, Min(0.01f)] private float m_wheelRadius = 0.22f;
        [SerializeField] private Vector3 m_wheelAxis = Vector3.up;
        [SerializeField, Range(0f, 20f)] private float m_movementLean = 5f;
        [SerializeField] private PlayerWeaponController m_weapon;
        [SerializeField] private PlayerHealth m_health;
        [SerializeField] private PlayerCouchCarrier m_carrier;
        [SerializeField] private Camera m_camera;
        [SerializeField] private Transform m_rightUpperArm, m_rightElbow, m_rightForearm, m_rightWrist;
        [SerializeField] private Transform m_leftUpperArm, m_leftElbow, m_leftForearm, m_leftWrist;
        private Transform[] m_arms;
        private Quaternion[] m_armRest;
        private Quaternion m_wheelRest, m_waistRest;
        private Vector3 m_previousPosition;
        private float m_wheelAngle;

        private void Awake()
        {
            if (m_wheel != null) m_wheelRest = m_wheel.localRotation;
            if (m_waist != null) m_waistRest = m_waist.localRotation;
            m_arms = new[] { m_rightUpperArm, m_rightForearm, m_leftUpperArm, m_leftForearm };
            m_armRest = new Quaternion[m_arms.Length];
            for (int i = 0; i < m_arms.Length; i++) if (m_arms[i] != null) m_armRest[i] = m_arms[i].localRotation;
        }

        private void OnEnable() => m_previousPosition = transform.position;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 delta = Vector3.ProjectOnPlane(transform.position - m_previousPosition, Vector3.up);
            m_previousPosition = transform.position;
            if (dt <= 0f || delta.magnitude > 20f * dt) return;
            float speed = Vector3.Dot(delta, transform.forward) / dt;
            m_wheelAngle = Mathf.Repeat(m_wheelAngle + speed * dt / m_wheelRadius * Mathf.Rad2Deg, 360f);
            if (m_wheel != null) m_wheel.localRotation = m_wheelRest * Quaternion.AngleAxis(m_wheelAngle, m_wheelAxis);
            if (m_waist != null)
                m_waist.localRotation = Quaternion.Slerp(m_waist.localRotation,
                    m_waistRest * Quaternion.Euler(Mathf.Clamp(speed, -1f, 1f) * m_movementLean, 0f, 0f),
                    1f - Mathf.Exp(-10f * dt));
            bool alive = m_health == null || m_health.IsAlive && !m_health.IsKnockedDown;
            bool carrying = alive && m_carrier != null && m_carrier.IsCarrying;
            bool armed = alive && m_weapon != null && m_weapon.HasEquippedWeapon;
            Vector3 direction = m_camera != null && m_camera.enabled ? m_camera.transform.forward : transform.forward;
            Vector3 target = transform.position + Vector3.up * 1.4f + direction * 30f;
            AimArm(0, m_rightElbow, carrying ? transform.TransformPoint(new Vector3(.65f, 1.3f, 1.2f)) : target, carrying || armed, dt);
            AimArm(1, m_rightWrist, carrying ? transform.TransformPoint(new Vector3(.65f, 1.3f, 1.2f)) : target, carrying || armed, dt);
            bool twoHanded = armed && m_weapon.EquippedWeapon != PlayerWeaponType.Pistol;
            AimArm(2, m_leftElbow, carrying ? transform.TransformPoint(new Vector3(-.65f, 1.3f, 1.2f)) : target, carrying || twoHanded, dt);
            AimArm(3, m_leftWrist, carrying ? transform.TransformPoint(new Vector3(-.65f, 1.3f, 1.2f)) : target, carrying || twoHanded, dt);
        }

        private void AimArm(int index, Transform child, Vector3 target, bool active, float dt)
        {
            Transform arm = m_arms[index];
            if (arm == null || child == null) return;
            Quaternion rest = arm.parent.rotation * m_armRest[index];
            Quaternion desired = active ? Quaternion.FromToRotation(rest * child.localPosition.normalized,
                (target - arm.position).normalized) * rest : rest;
            arm.rotation = Quaternion.Slerp(arm.rotation, desired, 1f - Mathf.Exp(-14f * dt));
        }
    }
}
