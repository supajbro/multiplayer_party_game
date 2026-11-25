using CouchGuys.Gameplay.Weapons;
using UnityEngine;

namespace CouchGuys.Player
{
    /// <summary>Blends poses on the saved rig; movement comes from the existing NetworkTransform.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class WheeliePresentation : MonoBehaviour
    {
        [SerializeField] private WheelieController m_controller;
        [SerializeField] private PlayerHealth m_health;
        [SerializeField] private PlayerWeaponController m_weapon;
        [SerializeField] private Transform m_wheel, m_waist, m_torso, m_head;
        [SerializeField] private Transform m_rightUpperArm, m_rightForearm, m_leftUpperArm, m_leftForearm;
        [SerializeField] private Transform m_rightElbow, m_rightWrist, m_leftElbow, m_leftWrist;
        private Transform[] m_bones;
        private Quaternion[] m_rest;
        private Vector3 m_previousPosition, m_velocity;
        private float m_previousYaw, m_wheelAngle, m_aimBlend, m_recoil;

        private void Awake()
        {
            m_bones = new[] { m_wheel, m_waist, m_torso, m_head, m_rightUpperArm, m_rightForearm, m_leftUpperArm, m_leftForearm };
            m_rest = new Quaternion[m_bones.Length];
            for (int i = 0; i < m_bones.Length; i++) if (m_bones[i] != null) m_rest[i] = m_bones[i].localRotation;
            m_previousPosition = transform.position; m_previousYaw = transform.eulerAngles.y;
            m_weapon.ShotPresented += OnShot;
        }
        private void OnDestroy() { if (m_weapon != null) m_weapon.ShotPresented -= OnShot; }
        private void OnShot(PlayerWeaponType weapon) => m_recoil = m_controller.Settings.RecoilAngle;

        private void LateUpdate()
        {
            var settings = m_controller.Settings;
            if (settings == null) return;
            float dt = Mathf.Max(.0001f, Time.deltaTime);
            Vector3 delta = Vector3.ProjectOnPlane(transform.position - m_previousPosition, Vector3.up);
            m_previousPosition = transform.position;
            Vector3 velocity = delta / dt;
            // Ignore spawn/teleport discontinuities in wheel and leaning presentation.
            if (velocity.magnitude > settings.MovementSpeed * 4f) velocity = Vector3.zero;
            float blend = 1f - Mathf.Exp(-settings.PoseBlendSpeed * dt);
            Vector3 acceleration = (velocity - m_velocity) / dt;
            m_velocity = Vector3.Lerp(m_velocity, velocity, blend);
            float yaw = transform.eulerAngles.y;
            float turnRate = Mathf.DeltaAngle(m_previousYaw, yaw) / dt; m_previousYaw = yaw;
            bool alive = m_health.IsAlive && !m_health.IsKnockedDown;
            if (alive) m_wheelAngle = Mathf.Repeat(m_wheelAngle + velocity.magnitude * Mathf.Sign(Vector3.Dot(velocity, transform.forward)) * dt /
                Mathf.Max(.01f, settings.WheelRadius) * Mathf.Rad2Deg, 360f);
            if (m_wheel != null) m_wheel.localRotation = m_rest[0] * Quaternion.AngleAxis(m_wheelAngle, Vector3.up);
            float lean = alive ? Mathf.Clamp(Vector3.Dot(acceleration, transform.forward) * settings.AccelerationLean,
                -settings.MaximumLean, settings.MaximumLean) : 0f;
            float bank = alive ? Mathf.Clamp(-turnRate * settings.TurningLean, -settings.MaximumLean, settings.MaximumLean) : 0f;
            if (m_waist != null) m_waist.localRotation = Quaternion.Slerp(m_waist.localRotation, m_rest[1] * Quaternion.Euler(lean, 0f, bank), blend);
            bool aiming = alive && m_controller.Combat.Engaged;
            m_aimBlend = Mathf.Lerp(m_aimBlend, aiming ? 1f : 0f, blend);
            m_recoil = Mathf.MoveTowards(m_recoil, 0f, settings.RecoilRecovery * dt);
            Vector3 direction = m_controller.Combat.AimPoint - (m_head != null ? m_head.position : transform.position);
            Vector3 local = transform.InverseTransformDirection(direction.normalized);
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -40f, 40f);
            float headYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -55f, 55f);
            if (m_torso != null) m_torso.localRotation = Quaternion.Slerp(m_torso.localRotation,
                m_rest[2] * Quaternion.Euler((pitch * .2f - m_recoil) * m_aimBlend, 0f, 0f), blend);
            if (m_head != null)
            {
                Quaternion worldRest = m_head.parent.rotation * m_rest[3];
                Quaternion adjustment = Quaternion.AngleAxis(headYaw * settings.HeadAimWeight * m_aimBlend, transform.up) *
                    Quaternion.AngleAxis(pitch * settings.HeadAimWeight * m_aimBlend, transform.right);
                m_head.rotation = Quaternion.Slerp(m_head.rotation, adjustment * worldRest, blend);
            }
            AimArm(m_rightUpperArm, m_rightElbow, 4, m_controller.Combat.AimPoint, m_aimBlend, blend);
            AimArm(m_rightForearm, m_rightWrist, 5, m_controller.Combat.AimPoint, m_aimBlend, blend);
            bool twoHanded = m_weapon.HasEquippedWeapon && m_weapon.EquippedWeapon != PlayerWeaponType.Pistol;
            float leftBlend = twoHanded ? m_aimBlend : 0f;
            AimArm(m_leftUpperArm, m_leftElbow, 6, m_controller.Combat.AimPoint, leftBlend * .8f, blend);
            AimArm(m_leftForearm, m_leftWrist, 7, m_controller.Combat.AimPoint, leftBlend, blend);
            // Grip curls are authored into the saved prefab; no hierarchy is generated here.
        }

        private void AimArm(Transform arm, Transform child, int rest, Vector3 target, float amount, float smoothing)
        {
            if (arm == null || child == null) return;
            Quaternion baseWorld = arm.parent.rotation * m_rest[rest];
            Vector3 axis = child.localPosition.normalized;
            Vector3 aim = target - arm.position;
            Quaternion desired = aim.sqrMagnitude > .001f ? Quaternion.FromToRotation(baseWorld * axis, aim.normalized) * baseWorld : baseWorld;
            if (m_controller.Combat.Reloading) amount *= .35f;
            arm.rotation = Quaternion.Slerp(arm.rotation, Quaternion.Slerp(baseWorld, desired, amount), smoothing);
        }
    }
}
