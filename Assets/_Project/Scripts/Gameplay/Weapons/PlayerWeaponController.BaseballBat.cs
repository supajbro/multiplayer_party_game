using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public sealed partial class PlayerWeaponController
    {
        [Header("Baseball Bat")]
        [SerializeField] private BaseballBatAssets m_baseballBatAssets;
        private readonly HashSet<Component> m_batVictims = new();
        private float m_batStartedAt = float.NegativeInfinity;
        private float m_batSampleTime;
        private Vector3 m_batForward;
        private int m_batSlot;
        private bool m_batSwinging;
        private BaseballBatPresentation BatPresentation =>
            m_weaponModels[(int)PlayerWeaponType.BaseballBat]?.GetComponent<BaseballBatPresentation>();

        [ServerRpc]
        private void RequestBatSwingServerRpc()
        {
            TryBeginBatSwingServer();
        }

        private bool TryBeginBatSwingServer()
        {
            int index = (int)PlayerWeaponType.BaseballBat;
            if (!CanHandleWeaponServer || m_equippedIndex.Value != index ||
                GetInventoryWeaponIndex(SelectedHotbarSlot) != index || m_baseballBatAssets == null ||
                m_batSwinging || Time.time < m_nextServerFireTime[index] ||
                m_reloadingIndex >= 0 || Time.time < m_switchCompleteAt) return false;
            m_nextServerFireTime[index] = Time.time + BaseballBatAssets.Cooldown;
            m_batStartedAt = Time.time;
            m_batSampleTime = BaseballBatAssets.ActiveStart;
            m_batForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            m_batSlot = SelectedHotbarSlot;
            m_batVictims.Clear();
            m_batSwinging = true;
            PlayBatSwingObserversRpc();
            return true;
        }

        private void UpdateBatSwingServer()
        {
            if (!IsServerInitialized || !m_batSwinging) return;
            if (!CanHandleWeaponServer || m_equippedIndex.Value != (int)PlayerWeaponType.BaseballBat ||
                SelectedHotbarSlot != m_batSlot)
            {
                m_batSwinging = false;
                return;
            }
            float elapsed = Time.time - m_batStartedAt;
            if (elapsed >= BaseballBatAssets.ActiveStart && m_batSampleTime <= BaseballBatAssets.ActiveEnd)
            {
                float until = Mathf.Min(elapsed, BaseballBatAssets.ActiveEnd);
                // Subdivide even a slow server frame, so the active arc cannot tunnel past a target.
                int steps = Mathf.Max(1, Mathf.CeilToInt((until - m_batSampleTime) / .008f));
                for (int i = 0; i <= steps; i++)
                    SampleBatSweep(Mathf.Lerp(m_batSampleTime, until, i / (float)steps));
                m_batSampleTime = until >= BaseballBatAssets.ActiveEnd ? float.PositiveInfinity : until;
            }
            if (elapsed >= BaseballBatAssets.SwingDuration) m_batSwinging = false;
        }

        private void SampleBatSweep(float time)
        {
            // Match the authored shoulder-to-front-to-left motion, including its raised wind-up.
            float progress = Mathf.SmoothStep(0, 1, time <= .3f ?
                Mathf.InverseLerp(.2f, .3f, time) : Mathf.InverseLerp(.3f, .4f, time));
            Vector3 localDirection = time <= .3f ?
                Vector3.Slerp(new Vector3(.42f,.84f,-.35f), new Vector3(0,.06f,1), progress) :
                Vector3.Slerp(new Vector3(0,.06f,1), new Vector3(-.96f,.1f,.25f), progress);
            Vector3 direction = Quaternion.LookRotation(m_batForward, Vector3.up) * localDirection.normalized;
            Vector3 origin = transform.position + Vector3.up * 1.05f;
            Vector3 start = origin + direction * .25f;
            Vector3 end = origin + direction * Mathf.Max(.25f, m_baseballBatAssets.Range - m_baseballBatAssets.SweepRadius);
            foreach (Collider collider in Physics.OverlapCapsule(start, end, m_baseballBatAssets.SweepRadius,
                         m_hitMask, QueryTriggerInteraction.Collide))
            {
                Component target = RocketProjectile.FindDamageTarget(collider);
                if (target == null || target == m_health || target.transform.IsChildOf(transform) ||
                    m_batVictims.Contains(target)) continue;
                Vector3 point = collider.ClosestPoint(origin + direction * m_baseballBatAssets.Range);
                if (!BatHasLineOfSight(origin, point, target)) continue;
                m_batVictims.Add(target); // Mark before damage callbacks can change the target's colliders.
                Vector3 away = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
                if (away.sqrMagnitude < .001f) away = m_batForward;
                away.Normalize();
                ApplyBatHitServer(target, away, point);
                BatImpactObserversRpc(point, -away);
            }
        }

        private bool BatHasLineOfSight(Vector3 origin, Vector3 point, Component target)
        {
            Vector3 offset = point - origin;
            // Inspect all hits: a fixed-capacity buffer must never omit a blocking wall.
            foreach (RaycastHit hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude,
                         m_hitMask, QueryTriggerInteraction.Collide))
            {
                if (hit.collider.transform.IsChildOf(transform) || !PedestrianController.IsBulletHit(hit.collider)) continue;
                if (RocketProjectile.FindDamageTarget(hit.collider) != target) return false;
            }
            return true;
        }

        private void ApplyBatHitServer(Component target, Vector3 away, Vector3 point)
        {
            BaseballBatAssets settings = m_baseballBatAssets;
            switch (target)
            {
                case DeliveryNPC dispatcher:
                    dispatcher.ReactToAttackServer(away, settings.NpcKnockback);
                    break;
                case PlayerHealth player:
                    player.ApplyBatImpactServer(settings.Damage, away, settings.PlayerKnockback, settings.PlayerLift);
                    break;
                case ThiefHealth thief:
                    thief.ApplyBatImpactServer(settings.Damage, away, settings.NpcKnockback, settings.NpcLift, m_health);
                    if (Owner.IsValid) ConfirmHostileHitTargetRpc(Owner, !thief.IsAlive);
                    break;
                case PedestrianController pedestrian:
                    pedestrian.ApplyBatImpactServer(settings.Damage, away * settings.NpcKnockback + Vector3.up * settings.NpcLift);
                    break;
                case MagneticHoverboardController board:
                    board.ApplyBulletDamageServer(settings.Damage);
                    board.ApplyBatLaunchServer(away * settings.VehicleKnockback + Vector3.up * settings.VehicleLift,
                        Vector3.Cross(Vector3.up, away) * settings.VehicleSpin);
                    break;
                case TruckController truck:
                    truck.ApplyBulletDamageServer(settings.Damage);
                    truck.ApplyBatLaunchServer(away * settings.VehicleKnockback + Vector3.up * settings.VehicleLift,
                        Vector3.Cross(Vector3.up, away) * settings.VehicleSpin);
                    break;
                case TrafficVehicleController car:
                    car.ApplyBulletDamageServer(settings.Damage);
                    car.ApplyBatLaunchServer(away * settings.VehicleKnockback + Vector3.up * settings.VehicleLift,
                        Vector3.Cross(Vector3.up, away) * settings.VehicleSpin);
                    break;
            }
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayBatSwingObserversRpc()
        {
            float age = IsServerInitialized ? 0f : Mathf.Min(.18f, TimeManager.HalfRoundTripTime * .001f);
            m_animationDriver?.PlayBaseballBatSwing(age);
            BatPresentation?.PlaySwing(age);
            if (IsOwner) LocalShotFired?.Invoke(PlayerWeaponType.BaseballBat);
        }

        [ObserversRpc(RunLocally = true)]
        private void BatImpactObserversRpc(Vector3 point, Vector3 normal)
        {
            m_effects?.PlayMeleeImpact(point, normal);
            BatPresentation?.PlayImpact();
        }
    }
}
