using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Vehicles;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Server-only swept projectile simulation; clients receive transforms and one VFX event.</summary>
    public sealed class RocketProjectile : NetworkBehaviour
    {
        [SerializeField] private RocketExplosionVfx m_explosionPrefab;
        private PlayerHealth m_shooter;
        private PlayerWeaponController m_weaponOwner;
        private WeaponDefinition m_weapon;
        private LayerMask m_mask;
        private Vector3 m_velocity;
        private Vector3 m_launchOrigin;
        private float m_expiresAt;
        private bool m_firstStep = true;
        private bool m_exploded;
        private readonly Dictionary<Component, float> m_distances = new();

        public void InitializeServer(PlayerWeaponController owner, PlayerHealth shooter,
            WeaponDefinition weapon, LayerMask mask)
        {
            m_weaponOwner = owner;
            m_shooter = shooter;
            m_weapon = weapon;
            m_mask = mask;
            m_velocity = transform.forward * weapon.BulletSpeed;
            m_launchOrigin = shooter != null ? shooter.AimPoint : transform.position;
            m_expiresAt = Time.time + weapon.BulletLifetime;
        }

        private bool IsCollisionTarget(Collider collider) => collider != null &&
            collider.GetComponentInParent<RocketProjectile>() != this &&
            (m_shooter == null || collider.GetComponentInParent<PlayerHealth>() != m_shooter) &&
            PedestrianController.IsBulletHit(collider);

        private bool Sweep(Vector3 origin, Vector3 delta, out RaycastHit nearest)
        {
            nearest = default;
            float distance = delta.magnitude;
            if (distance < .00001f) return false;
            float closest = float.PositiveInfinity;
            // All hits avoids dropping a wall behind the owner's many colliders in a fixed buffer.
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, m_weapon.ProjectileCollisionRadius,
                         delta / distance, distance, m_mask, QueryTriggerInteraction.Collide))
            {
                if (!IsCollisionTarget(hit.collider) || hit.distance >= closest) continue;
                nearest = hit;
                closest = hit.distance;
            }
            return closest < float.PositiveInfinity;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized || m_weapon == null || m_exploded) return;
            if (m_firstStep)
            {
                m_firstStep = false;
                // Also sweep from the player to the muzzle: never spawn through a nearby wall.
                if (Sweep(m_launchOrigin, transform.position - m_launchOrigin, out RaycastHit blocked))
                {
                    ExplodeServer(blocked.point, blocked.collider);
                    return;
                }
            }
            // Sphere casts do not report starting overlaps.
            foreach (Collider collider in Physics.OverlapSphere(transform.position,
                         m_weapon.ProjectileCollisionRadius, m_mask, QueryTriggerInteraction.Collide))
            {
                if (!IsCollisionTarget(collider)) continue;
                ExplodeServer(transform.position, collider);
                return;
            }
            if (Time.time >= m_expiresAt)
            {
                ExplodeServer(transform.position, null);
                return;
            }
            Vector3 delta = m_velocity * Mathf.Min(Time.fixedDeltaTime, m_expiresAt - Time.time);
            if (Sweep(transform.position, delta, out RaycastHit hit))
            {
                ExplodeServer(hit.point, hit.collider);
                return;
            }
            transform.position += delta;
        }

        public static float DamageAtDistance(float maximum, float radius, float distance) =>
            radius > 0f ? Mathf.Max(0f, maximum) * Mathf.Clamp01(1f - Mathf.Max(0f, distance) / radius) : 0f;

        public static Component FindDamageTarget(Collider collider)
        {
            // Match the existing hitscan target priority, deduplicating compound hurtboxes by behaviour.
            if (collider == null) return null;
            Component target = collider.GetComponentInParent<PedestrianController>();
            if (target == null) target = collider.GetComponentInParent<PlayerHealth>();
            if (target == null) target = collider.GetComponentInParent<ThiefHealth>();
            if (target == null) target = collider.GetComponentInParent<TruckController>();
            if (target == null) target = collider.GetComponentInParent<TrafficVehicleController>();
            return target;
        }

        [Server]
        private void ExplodeServer(Vector3 position, Collider directCollider)
        {
            if (m_exploded) return;
            m_exploded = true; // Set before any damage callback can trigger death/drop/despawn behaviour.
            Component direct = FindDamageTarget(directCollider);
            m_distances.Clear();
            foreach (Collider collider in Physics.OverlapSphere(position, m_weapon.ExplosionRadius,
                         m_mask, QueryTriggerInteraction.Collide))
            {
                Component target = FindDamageTarget(collider);
                if (target == null) continue;
                float distance = Vector3.Distance(position, collider.ClosestPoint(position));
                if (!m_distances.TryGetValue(target, out float previous) || distance < previous)
                    m_distances[target] = distance;
            }
            if (direct != null) m_distances[direct] = 0f;
            foreach (var entry in m_distances)
            {
                if (entry.Key == null) continue;
                float damage = DamageAtDistance(m_weapon.ExplosionDamage, m_weapon.ExplosionRadius, entry.Value);
                if (entry.Key == direct) damage += m_weapon.DamagePerPellet;
                if (damage <= 0f) continue;
                Vector3 direction = Vector3.ProjectOnPlane(entry.Key.transform.position - position, Vector3.up);
                if (direction.sqrMagnitude < .001f) direction = Vector3.ProjectOnPlane(m_velocity, Vector3.up);
                if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
                direction.Normalize();
                float strength = Mathf.Lerp(.35f, 1f, Mathf.Clamp01(1f - entry.Value / m_weapon.ExplosionRadius));
                float force = m_weapon.Knockback * strength;
                float lift = m_weapon.ExplosionUpwardForce * strength;
                switch (entry.Key)
                {
                    case PlayerHealth player: player.ApplyExplosionDamageServer(damage, direction, force, lift); break;
                    case ThiefHealth thief:
                        thief.ApplyExplosionDamageServer(damage, direction, force, lift, m_shooter);
                        m_weaponOwner?.ConfirmRocketHitServer(!thief.IsAlive);
                        break;
                    case PedestrianController pedestrian: pedestrian.ApplyBulletDamageServer(damage); break;
                    case TruckController truck: truck.ApplyBulletDamageServer(damage); break;
                    case TrafficVehicleController traffic: traffic.ApplyBulletDamageServer(damage); break;
                }
            }
            Vector3 ground = position;
            if (Physics.Raycast(position + Vector3.up * .2f, Vector3.down, out RaycastHit groundHit,
                    15f, m_mask, QueryTriggerInteraction.Ignore)) ground = groundHit.point;
            PlayExplosionObserversRpc(position, ground, m_weapon.ExplosionRadius);
            Despawn(NetworkObject);
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayExplosionObserversRpc(Vector3 position, Vector3 ground, float radius)
        {
            if (NetworkManager.IsClientStarted && m_explosionPrefab != null)
                RocketExplosionVfx.Play(m_explosionPrefab, position, ground, radius);
        }
    }
}
