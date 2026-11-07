using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        // These times match the existing rig's authored FBX clips, in seconds.
        public const float JabDuration = .4f;
        public const float CrossDuration = 14f / 30f;
        public const float JabImpactTime = .18f;
        public const float CrossImpactTime = .2f;

        private byte m_punchStage;
        private float m_punchStartedAt;
        private float m_nextServerPunchTime;
        private bool m_crossQueued;
        private bool m_punchImpactApplied;

        private bool CanPunchServer => CanHandleWeaponServer && m_equippedIndex.Value < 0 &&
            EquippedHealingItem == null && Time.time >= m_switchCompleteAt;

        [ServerRpc]
        private void RequestPunchServerRpc()
        {
            // Advance first so requests arriving on recovery boundaries see the current state.
            UpdatePunchServer();
            if (!CanPunchServer || Time.time < m_nextServerPunchTime) return;
            if (m_punchStage == 0) BeginPunchServer(1);
            else if (m_punchStage == 1)
            {
                if (Time.time < m_punchStartedAt + JabDuration) m_crossQueued = true;
                else BeginPunchServer(2);
            }
        }

        private void BeginPunchServer(byte stage)
        {
            m_punchStage = stage;
            m_punchStartedAt = Time.time;
            m_crossQueued = false;
            m_punchImpactApplied = false;
            PlayPunchObserversRpc(stage, TimeManager.Tick);
        }

        private void UpdatePunchServer()
        {
            if (!IsServerInitialized || m_punchStage == 0) return;
            if (!CanPunchServer)
            {
                // An interrupted cross cannot bypass its recovery cooldown.
                if (m_punchStage == 2) m_nextServerPunchTime = Time.time + m_punchCooldown;
                EndPunchServer();
                return;
            }
            float elapsed = Time.time - m_punchStartedAt;
            bool cross = m_punchStage == 2;
            if (!m_punchImpactApplied && elapsed >= (cross ? CrossImpactTime : JabImpactTime))
            {
                m_punchImpactApplied = true; // Commit before damage callbacks can change colliders/state.
                ApplyPunchImpactServer(cross);
            }
            if (cross)
            {
                if (elapsed < CrossDuration) return;
                m_nextServerPunchTime = Time.time + m_punchCooldown;
                EndPunchServer();
            }
            else if (elapsed >= JabDuration)
            {
                if (m_crossQueued) BeginPunchServer(2);
                else if (elapsed > JabDuration + m_punchComboWindow) EndPunchServer();
            }
        }

        private void EndPunchServer()
        {
            m_punchStage = 0;
            m_crossQueued = false;
            PlayPunchObserversRpc(0, TimeManager.Tick);
        }

        private void ApplyPunchImpactServer(bool cross)
        {
            // Preserve the existing range, facing cone, occlusion and nearest-target selection.
            // Resolve only at impact; compound colliders can select a target only once.
            Vector3 origin = transform.position + Vector3.up;
            Component nearest = null;
            Vector3 point = origin;
            float distance = m_punchRange;
            foreach (Collider collider in Physics.OverlapSphere(origin, m_punchRange, m_hitMask,
                         QueryTriggerInteraction.Ignore))
            {
                Component target = RocketProjectile.FindDamageTarget(collider);
                if (target == null || target == m_health || target.transform.IsChildOf(transform)) continue;
                if (target is PlayerHealth player && !player.IsAlive ||
                    target is ThiefHealth thief && !thief.IsAlive ||
                    target is PedestrianController pedestrian && !pedestrian.IsAlive) continue;
                Vector3 candidate = collider.ClosestPoint(origin);
                Vector3 offset = candidate - origin;
                if (offset.magnitude > distance || Vector3.Dot(transform.forward, offset.normalized) < .35f) continue;
                if (!BatHasLineOfSight(origin, candidate, target)) continue;
                nearest = target;
                distance = offset.magnitude;
                point = candidate;
            }
            if (nearest == null) return;
            float damage = m_punchDamage * (cross ? m_crossDamageMultiplier : 1f);
            float force = m_punchKnockback * (cross ? m_crossKnockbackMultiplier : 1f);
            Vector3 direction = transform.forward;
            switch (nearest)
            {
                case PlayerHealth player:
                    player.ApplyDamageWithFullKnockbackServer(damage, direction, force);
                    break;
                case ThiefHealth thief:
                    if (cross) thief.ApplyBatImpactServer(damage, direction, force, m_crossThiefLift, m_health);
                    else thief.ApplyDamageServer(damage, direction, force, m_health);
                    if (Owner.IsValid) ConfirmHostileHitTargetRpc(Owner, !thief.IsAlive);
                    break;
                case PedestrianController pedestrian:
                    pedestrian.ApplyBatImpactServer(damage, direction * force);
                    break;
                case MagneticHoverboardController board:
                    board.ApplyBulletDamageServer(damage);
                    board.ApplyBatLaunchServer(direction * force, Vector3.zero);
                    break;
                case TruckController truck:
                    truck.ApplyBulletDamageServer(damage);
                    truck.ApplyBatLaunchServer(direction * force, Vector3.zero);
                    break;
                case Traffic.TrafficVehicleController car:
                    car.ApplyBulletDamageServer(damage);
                    car.ApplyBatLaunchServer(direction * force, Vector3.zero);
                    break;
            }
            PunchImpactObserversRpc(point, -direction);
        }

        // Buffered state plus server tick lets newly observing clients seek into the correct clip,
        // and prevents an old buffered punch from replaying after it has finished.
        [ObserversRpc(RunLocally = true, BufferLast = true)]
        private void PlayPunchObserversRpc(byte stage, uint startedTick)
        {
            if (stage == 0) { m_animationDriver?.StopPunch(); return; }
            float age = IsServerInitialized ? 0f : (float)TimeManager.TimePassed(startedTick);
            float duration = stage == 2 ? CrossDuration : JabDuration;
            if (age >= duration) return;
            m_animationDriver?.PlayPunch(stage == 2, age);
            m_faceExpression ??= GetComponent<RobotFaceExpressionController>();
            m_faceExpression?.NotifyPunch(duration - age);
            m_hoverVfx ??= GetComponent<RobotHoverVFX>();
            m_hoverVfx?.NotifyPunch(stage == 2, age);
        }

        [ObserversRpc(RunLocally = true)]
        private void PunchImpactObserversRpc(Vector3 point, Vector3 normal) => m_effects?.PlayMeleeImpact(point, normal);
    }
}
