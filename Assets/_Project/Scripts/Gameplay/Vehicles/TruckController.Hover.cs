using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Pedestrians;
using CouchGuys.Gameplay.Traffic;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    public sealed partial class TruckController
    {
        [Header("Hover Truck")]
        [SerializeField] private HoverTruckSettings m_hoverSettings;
        [SerializeField] private TruckTurretSettings m_turretSettings;
        [SerializeField] private Transform[] m_hoverProbes = System.Array.Empty<Transform>();
        [SerializeField] private Transform[] m_hoverGlows = System.Array.Empty<Transform>();
        [SerializeField] private Transform[] m_turretYaw = System.Array.Empty<Transform>();
        [SerializeField] private Transform[] m_turretPitch = System.Array.Empty<Transform>();
        [SerializeField] private Transform[] m_turretMuzzles = System.Array.Empty<Transform>();
        [SerializeField] private Transform[] m_dockClamps = System.Array.Empty<Transform>();
        [SerializeField] private Transform[] m_turretLeftGrips = System.Array.Empty<Transform>();
        [SerializeField] private Transform[] m_turretRightGrips = System.Array.Empty<Transform>();
        [SerializeField] private Transform[] m_turretAuraTargets = System.Array.Empty<Transform>();
        private readonly SyncVar<Vector2> m_turretAim0 = new();
        private readonly SyncVar<Vector2> m_turretAim1 = new();
        private readonly SyncVar<Vector2> m_turretAim2 = new();
        private readonly SyncVar<Vector2> m_hoverDrive = new();
        private readonly Vector2[] m_requestedAim = new Vector2[3];
        private readonly float[] m_turretInputAt = new float[3];
        private readonly float[] m_nextTurretShot = new float[3];
        private readonly bool[] m_turretFire = new bool[3];
        private readonly float[] m_clampProgress = new float[4];
        private readonly List<TurretShot> m_turretShots = new();
        private readonly Dictionary<uint, HoverTruckBolt> m_boltVisuals = new();
        private uint m_nextShotId;
        private Vector3 m_visualBasePosition;

        private struct TurretShot
        {
            public uint Id;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Expires;
            public PlayerHealth Shooter;
        }

        public bool IsHoverTruck => m_hoverSettings != null && !IsGetawayTruck;
        public HoverTruckSettings HoverSettings => m_hoverSettings;

        public Transform GetTurretLeftGrip(int seatIndex) => TurretReference(m_turretLeftGrips, seatIndex);
        public Transform GetTurretRightGrip(int seatIndex) => TurretReference(m_turretRightGrips, seatIndex);
        public Transform GetTurretAuraTarget(int seatIndex) => TurretReference(m_turretAuraTargets, seatIndex);

        private Transform TurretReference(Transform[] references, int seatIndex)
        {
            int index = seatIndex - 1;
            return IsHoverTruck && index >= 0 && index < references.Length ? references[index] : null;
        }

        public Vector3 GetTurretAimDirection(int seatIndex, Ray cameraRay)
        {
            int index = seatIndex - 1;
            if (m_turretSettings == null || index < 0 || index >= m_turretMuzzles.Length || m_turretMuzzles[index] == null)
                return cameraRay.direction;
            float range = m_turretSettings.ProjectileSpeed * m_turretSettings.Lifetime;
            Vector3 target = cameraRay.GetPoint(range);
            float closest = range;
            foreach (RaycastHit hit in Physics.RaycastAll(cameraRay, range, m_turretSettings.HitMask, QueryTriggerInteraction.Collide))
            {
                if (IgnoreHoverCollider(hit.collider) || hit.distance >= closest) continue;
                if (hit.collider.isTrigger && RocketProjectile.FindDamageTarget(hit.collider) == null) continue;
                closest = hit.distance;
                target = hit.point;
            }
            return (target - m_turretMuzzles[index].position).normalized;
        }

        internal void SetTurretInputServer(PlayerTruckInteractor player, Vector3 direction, bool fire)
        {
            if (!IsServerInitialized || !IsHoverTruck || IsDestroyed || m_turretSettings == null || player == null) return;
            int index = GetSeatIndex(player.NetworkObject) - 1;
            if (index < 0 || index >= 3 || !IsFiniteDirection(direction)) return;
            if (player.TryGetComponent(out PlayerHealth health) && (!health.IsAlive || health.IsKnockedDown)) return;
            Vector3 local = (m_visualModel != null ? m_visualModel : transform).InverseTransformDirection(direction.normalized);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            m_requestedAim[index] = new Vector2(Mathf.Clamp(yaw, -m_turretSettings.YawLimit, m_turretSettings.YawLimit),
                Mathf.Clamp(pitch, m_turretSettings.MinimumPitch, m_turretSettings.MaximumPitch));
            m_turretFire[index] = fire;
            m_turretInputAt[index] = Time.time;
        }

        private static bool IsFiniteDirection(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && value.sqrMagnitude > .01f;

        private SyncVar<Vector2> TurretAim(int index) => index == 0 ? m_turretAim0 : index == 1 ? m_turretAim1 : m_turretAim2;

        private void TickHoverServer(float dt)
        {
            m_hoverDrive.Value = m_driveInput;
            Vector3 normal = Vector3.zero;
            float error = 0f;
            int supports = 0;
            foreach (Transform probe in m_hoverProbes)
            {
                if (probe == null) continue;
                float nearest = float.PositiveInfinity;
                RaycastHit ground = default;
                foreach (RaycastHit hit in Physics.RaycastAll(probe.position, Vector3.down,
                    m_hoverSettings.ProbeRange, m_hoverSettings.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    if (IgnoreHoverCollider(hit.collider) || hit.normal.y < .45f || hit.distance >= nearest) continue;
                    ground = hit;
                    nearest = hit.distance;
                }
                if (float.IsPositiveInfinity(nearest)) continue;
                float probeHeight = probe.position.y - transform.position.y;
                error += m_hoverSettings.HoverHeight + probeHeight - ground.distance;
                normal += ground.normal;
                supports++;
            }
            if (supports > 0)
            {
                m_groundNormal = normal.normalized;
                m_lastGroundContactTime = Time.time;
                float lift = -Physics.gravity.y + error / supports * m_hoverSettings.Stability -
                    m_rigidbody.linearVelocity.y * m_hoverSettings.Damping;
                m_rigidbody.AddForce(Vector3.up * Mathf.Clamp(lift, 0f, m_hoverSettings.MaximumLiftAcceleration), ForceMode.Acceleration);
                Vector3 tiltVelocity = Vector3.ProjectOnPlane(m_rigidbody.angularVelocity, Vector3.up);
                m_rigidbody.AddTorque(Vector3.Cross(transform.up, m_groundNormal) * m_settings.AntiRollStrength -
                    tiltVelocity * m_hoverSettings.AngularDamping, ForceMode.Acceleration);
            }
            TickTurretsServer(dt);
        }

        private bool IgnoreHoverCollider(Collider collider)
        {
            if (collider == null || collider.transform.IsChildOf(transform)) return true;
            if (collider.TryGetComponent(out HoverTruckBolt _)) return true;
            var couch = collider.GetComponentInParent<CouchCarryController>();
            if (couch != null && couch.LoadedTruckObject == NetworkObject) return true;
            var occupant = collider.GetComponentInParent<PlayerTruckInteractor>();
            return occupant != null && occupant.CurrentTruck == this;
        }

        private void TickTurretsServer(float dt)
        {
            if (m_turretSettings == null) return;
            for (int index = 0; index < 3; index++)
            {
                NetworkObject occupant = GetOccupant(index + 1);
                if (occupant == null || Time.time - m_turretInputAt[index] > .3f)
                { m_turretFire[index] = false; continue; }
                Vector2 current = TurretAim(index).Value;
                float step = m_turretSettings.TurnSpeed * dt;
                Vector2 next = new(Mathf.MoveTowardsAngle(current.x, m_requestedAim[index].x, step),
                    Mathf.MoveTowards(current.y, m_requestedAim[index].y, step));
                TurretAim(index).Value = next;
                ApplyTurretPose(index, next);
                if (!m_turretFire[index] || Time.time < m_nextTurretShot[index] ||
                    index >= m_turretMuzzles.Length || m_turretMuzzles[index] == null) continue;
                m_nextTurretShot[index] = Time.time + m_turretSettings.FireInterval;
                Transform muzzle = m_turretMuzzles[index];
                var shot = new TurretShot { Id = ++m_nextShotId, Position = muzzle.position,
                    Velocity = muzzle.forward * m_turretSettings.ProjectileSpeed + m_rigidbody.linearVelocity,
                    Expires = Time.time + m_turretSettings.Lifetime, Shooter = occupant.GetComponent<PlayerHealth>() };
                m_turretShots.Add(shot);
                FireTurretObserversRpc(shot.Id, shot.Position, shot.Velocity);
            }
        }

        private void TickTurretShotsServer(float dt)
        {
            for (int index = m_turretShots.Count - 1; index >= 0; index--)
            {
                TurretShot shot = m_turretShots[index];
                Vector3 travel = shot.Velocity * dt;
                RaycastHit nearest = default;
                float distance = float.PositiveInfinity;
                foreach (RaycastHit hit in Physics.SphereCastAll(shot.Position, m_turretSettings.CollisionRadius,
                    travel.normalized, travel.magnitude, m_turretSettings.HitMask, QueryTriggerInteraction.Collide))
                {
                    if (IgnoreHoverCollider(hit.collider) || hit.distance >= distance) continue;
                    if (hit.collider.isTrigger && RocketProjectile.FindDamageTarget(hit.collider) == null) continue;
                    nearest = hit;
                    distance = hit.distance;
                }
                if (!float.IsPositiveInfinity(distance))
                {
                    ApplyTurretDamageServer(nearest, shot);
                    HitTurretObserversRpc(shot.Id, nearest.point, nearest.normal, true);
                    m_turretShots.RemoveAt(index);
                }
                else if (Time.time >= shot.Expires)
                {
                    HitTurretObserversRpc(shot.Id, shot.Position, Vector3.up, false);
                    m_turretShots.RemoveAt(index);
                }
                else
                {
                    shot.Position += travel;
                    m_turretShots[index] = shot;
                }
            }
        }

        private void ClearTurretShotsServer()
        {
            foreach (TurretShot shot in m_turretShots)
                HitTurretObserversRpc(shot.Id, shot.Position, Vector3.up, false);
            m_turretShots.Clear();
        }

        private void ApplyTurretDamageServer(RaycastHit hit, TurretShot shot)
        {
            Component target = RocketProjectile.FindDamageTarget(hit.collider);
            Vector3 direction = shot.Velocity.normalized;
            float damage = m_turretSettings.Damage;
            float force = m_turretSettings.Knockback;
            switch (target)
            {
                case PlayerHealth player: player.ApplyDamageServer(damage, direction, force); break;
                case ThiefHealth thief: thief.ApplyBulletDamageServer(damage, direction, force, hit.point, shot.Shooter); break;
                case PedestrianController pedestrian: pedestrian.ApplyBulletDamageServer(damage, hit.point, direction); break;
                case TruckController truck: truck.ApplyBulletDamageServer(damage); break;
                case MagneticHoverboardController board: board.ApplyBulletDamageServer(damage); break;
                case TrafficVehicleController traffic: traffic.ApplyBulletDamageServer(damage); break;
            }
        }

        [ObserversRpc(RunLocally = true)]
        private void FireTurretObserversRpc(uint id, Vector3 position, Vector3 velocity)
        {
            if (!IsClientInitialized || m_turretSettings.ProjectilePrefab == null) return;
            var bolt = Instantiate(m_turretSettings.ProjectilePrefab, position, Quaternion.LookRotation(velocity));
            bolt.Launch(velocity, m_turretSettings.Lifetime);
            m_boltVisuals[id] = bolt;
        }

        [ObserversRpc(RunLocally = true)]
        private void HitTurretObserversRpc(uint id, Vector3 position, Vector3 normal, bool impact)
        {
            if (m_boltVisuals.Remove(id, out HoverTruckBolt bolt) && bolt != null) Destroy(bolt.gameObject);
            if (!IsClientInitialized || !impact || m_turretSettings.ImpactPrefab == null) return;
            GameObject effect = Instantiate(m_turretSettings.ImpactPrefab, position, Quaternion.LookRotation(normal));
            Destroy(effect, 2f);
        }

        private void ApplyTurretPose(int index, Vector2 aim, float step = float.PositiveInfinity)
        {
            if (m_turretYaw == null || index >= m_turretYaw.Length || m_turretYaw[index] == null) return;
            m_turretYaw[index].localRotation = Quaternion.RotateTowards(m_turretYaw[index].localRotation,
                Quaternion.Euler(0f, aim.x, 0f), step);
            if (index < m_turretPitch.Length && m_turretPitch[index] != null)
                m_turretPitch[index].localRotation = Quaternion.RotateTowards(m_turretPitch[index].localRotation,
                    Quaternion.Euler(-aim.y, 0f, 0f), step);
        }

        private void UpdateHoverVisuals(float dt)
        {
            if (!IsHoverTruck) return;
            float step = IsServerInitialized || m_turretSettings == null ? float.PositiveInfinity : m_turretSettings.TurnSpeed * 2f * dt;
            for (int index = 0; index < 3; index++) ApplyTurretPose(index, TurretAim(index).Value, step);
            foreach (Transform glow in m_hoverGlows)
                if (glow != null && glow.gameObject.activeSelf == IsDestroyed) glow.gameObject.SetActive(!IsDestroyed);
            if (IsDestroyed) return;
            float pulse = Mathf.Sin(Time.time * m_hoverSettings.BobFrequency * Mathf.PI * 2f);
            if (m_visualModel != null)
            {
                Vector2 drive = m_hoverDrive.Value;
                float blend = 1f - Mathf.Exp(-m_settings.VisualSmoothing * dt);
                m_visualModel.localPosition = m_visualBasePosition + Vector3.up * (pulse * m_hoverSettings.BobAmplitude);
                m_visualModel.localRotation = Quaternion.Slerp(m_visualModel.localRotation,
                    m_visualBaseRotation * Quaternion.Euler(-drive.y * m_settings.BodyPitchAmount, 0f,
                        -drive.x * m_hoverSettings.Banking), blend);
            }
            foreach (Transform glow in m_hoverGlows)
                if (glow != null) glow.localScale = new Vector3(1f, 1f + pulse * .08f, 1f);
            for (int slot = 0; slot < 4; slot++)
            {
                bool occupied = slot < m_loadedCouches.Count && m_loadedCouches[slot] != null;
                m_clampProgress[slot] = Mathf.MoveTowards(m_clampProgress[slot], occupied ? 1f : 0f,
                    dt / Mathf.Max(.05f, m_hoverSettings.ClampDuration));
                for (int side = 0; side < 2; side++)
                {
                    int clamp = slot * 2 + side;
                    if (m_dockClamps == null || clamp >= m_dockClamps.Length || m_dockClamps[clamp] == null) continue;
                    m_dockClamps[clamp].localRotation = Quaternion.Euler(0f, 0f,
                        (side == 0 ? -1f : 1f) * m_hoverSettings.ClampOpenAngle * (1f - m_clampProgress[slot]));
                }
            }
        }
    }
}
