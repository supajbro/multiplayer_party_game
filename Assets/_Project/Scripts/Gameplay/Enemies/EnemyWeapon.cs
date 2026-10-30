using System.Collections.Generic;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using CouchGuys.Gameplay.Vehicles;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    [DisallowMultipleComponent]
    public sealed class EnemyWeapon : NetworkBehaviour
    {
        [SerializeField] private ThiefSettings m_settings;
        [SerializeField] private Transform m_muzzle;
        private EnemyWeaponType m_weaponType => m_settings != null
            ? m_settings.Combat.WeaponType : EnemyWeaponType.Pistol;
        private float m_fireInterval => m_settings != null
            ? m_settings.Combat.FireInterval : 0.8f;
        private float m_range => m_settings != null
            ? m_settings.Combat.Range : 30f;
        private int m_pelletsPerShot => m_settings != null
            ? m_settings.Combat.PelletsPerShot : 1;
        private float m_spreadAngle => m_settings != null
            ? m_settings.Combat.SpreadAngle : 5f;
        private float m_damagePerPellet => m_settings != null
            ? m_settings.Combat.DamagePerPellet : 20f;
        private float m_knockbackForce => m_settings != null
            ? m_settings.Combat.KnockbackForce : 5f;
        private float m_preferredRange => m_settings != null
            ? m_settings.Combat.PreferredRange : 12f;
        private LayerMask m_hitMask => m_settings != null
            ? m_settings.Combat.HitMask : ~0;
        private float m_missChance => m_settings != null
            ? m_settings.Combat.MissChance : 40f;
        private float m_baseAccuracy => m_settings != null
            ? m_settings.Combat.BaseAccuracy : 0.72f;
        private float m_movingAccuracyPenalty => m_settings != null
            ? m_settings.Combat.MovingAccuracyPenalty : 0.2f;
        private float m_distanceAccuracyPenalty => m_settings != null
            ? m_settings.Combat.DistanceAccuracyPenalty : 0.35f;
        private float m_reactionTime => m_settings != null
            ? m_settings.Combat.ReactionTime : 0.65f;
        private float m_aimRotationSpeed => m_settings != null
            ? m_settings.Combat.AimRotationSpeed : 150f;
        private int m_minBurstShots => m_settings != null
            ? m_settings.Combat.MinBurstShots : 1;
        private int m_maxBurstShots => m_settings != null
            ? m_settings.Combat.MaxBurstShots : 3;
        private float m_burstCooldown => m_settings != null
            ? m_settings.Combat.BurstCooldown : 1.1f;
        private float m_requiredAimAngle => m_settings != null
            ? m_settings.Combat.RequiredAimAngle : 16f;
        private float m_tracerDuration => m_settings != null
            ? m_settings.Combat.TracerDuration : 0.16f;
        private float m_tracerWidth => m_settings != null
            ? m_settings.Combat.TracerWidth : 0.025f;
        private Color m_tracerColour => m_settings != null
            ? m_settings.Combat.TracerColour : new(1f, 0.72f, 0.12f, 1f);

        private readonly Dictionary<Component, RaycastHit> m_bloodHits = new(8);
        private readonly Dictionary<PlayerHealth, float> m_damageByPlayer = new();
        private readonly Dictionary<TruckController, float> m_damageByTruck = new();
        private readonly Dictionary<CouchGuys.Gameplay.Pedestrians.PedestrianController, float> m_damageByPedestrian = new();
        private readonly List<Vector3> m_tracerEnds = new(12);
        private readonly List<Collider> m_targetColliders = new(8);
        private readonly RaycastHit[] m_hits = new RaycastHit[24];
        private PlayerHealth m_target;
        private TruckController m_targetTruck;
        private Vector3 TargetAimPoint => m_targetTruck != null
            ? m_targetTruck.CameraTarget.position : m_target.AimPoint;
        private Material m_tracerMaterial;
        private ThiefAnimationDriver m_animationDriver;
        private NavMeshAgent m_agent;
        private WeaponAudioEmitter m_audio;
        private Vector3 m_aimDirection;
        private float m_nextFireTime;
        private float m_reactionEndsAt;
        private int m_burstShotsRemaining;
        private bool m_rifleBurstActive;

        public EnemyWeaponType WeaponType => m_weaponType;
        public float PreferredRange => m_preferredRange;

        private void Awake()
        {
            m_animationDriver = GetComponentInChildren<ThiefAnimationDriver>(true);
            m_agent = GetComponent<NavMeshAgent>();
            m_audio = GetComponent<WeaponAudioEmitter>();
            if (m_audio == null) m_audio = gameObject.AddComponent<WeaponAudioEmitter>();
            m_aimDirection = transform.forward;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) m_tracerMaterial = new Material(shader);
        }

        private void OnDestroy()
        {
            if (m_tracerMaterial != null) Destroy(m_tracerMaterial);
        }

        public void SetTargetServer(PlayerHealth target)
        {
            if (!IsServerInitialized && NetworkObject != null && NetworkObject.IsSpawned) return;
            if (m_target != target || m_targetTruck != null)
            {
                EndRifleBurstServer();
                m_reactionEndsAt = Time.time + m_reactionTime;
                m_burstShotsRemaining = Random.Range(m_minBurstShots, m_maxBurstShots + 1);
            }
            m_target = target;
            m_targetTruck = null;
        }

        public void SetTruckTargetServer(TruckController truck)
        {
            if (!IsServerInitialized) return;
            if (m_targetTruck != truck)
            {
                SetTargetServer(null);
                m_reactionEndsAt = Time.time + m_reactionTime;
                m_burstShotsRemaining = Random.Range(m_minBurstShots, m_maxBurstShots + 1);
            }
            m_targetTruck = truck;
        }

        private void Update()
        {
            if (!IsServerInitialized) return;
            if (m_targetTruck != null ? m_targetTruck.IsDestroyed : m_target == null || !m_target.IsAlive)
            {
                EndRifleBurstServer();
                return;
            }
            Vector3 desired = (TargetAimPoint - GetMuzzlePosition()).normalized;
            m_aimDirection = Vector3.RotateTowards(m_aimDirection, desired,
                m_aimRotationSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
            if (Time.time < m_reactionEndsAt || Time.time < m_nextFireTime || !CanShootTarget(desired)) return;

            FireServer();
            m_burstShotsRemaining--;
            if (m_burstShotsRemaining <= 0)
            {
                EndRifleBurstServer();
                m_burstShotsRemaining = Random.Range(m_minBurstShots, m_maxBurstShots + 1);
                m_nextFireTime = Time.time + m_burstCooldown;
            }
            else m_nextFireTime = Time.time + m_fireInterval;
        }

        private bool CanShootTarget(Vector3 desired)
        {
            Vector3 origin = GetMuzzlePosition();
            Vector3 offset = TargetAimPoint - origin;
            if (offset.sqrMagnitude > m_range * m_range ||
                Vector3.Angle(m_aimDirection, desired) > m_requiredAimAngle) return false;
            int count = Physics.RaycastNonAlloc(origin, desired, m_hits, m_range, m_hitMask, QueryTriggerInteraction.Ignore);
            SortHits(count);
            for (int i = 0; i < count; i++)
            {
                if (m_hits[i].collider.transform.root == transform.root) continue;
                PlayerHealth hitPlayer = m_hits[i].collider.GetComponentInParent<PlayerHealth>();
                if (m_target != null && hitPlayer == m_target) return true;
                TruckController hitTruck = m_hits[i].collider.GetComponentInParent<TruckController>();
                return hitTruck != null && (m_targetTruck != null
                    ? hitTruck == m_targetTruck : m_target != null && hitTruck.Driver == m_target.NetworkObject);
            }
            return false;
        }

        [Server]
        private void FireServer()
        {
            Vector3 origin = GetMuzzlePosition();
            CouchGuys.Gameplay.Pedestrians.PedestrianManager.ReportGunshotServer(origin);
            float distance01 = Mathf.Clamp01(Vector3.Distance(origin, TargetAimPoint) / m_range);
            bool moving = m_agent != null && m_agent.enabled && m_agent.velocity.sqrMagnitude > 0.2f;
            float accuracy = Mathf.Clamp01(m_baseAccuracy - distance01 * m_distanceAccuracyPenalty -
                (moving ? m_movingAccuracyPenalty : 0f));
            float spread = m_spreadAngle * Mathf.Lerp(1.8f, 0.25f, accuracy);
            bool miss = Random.value * 100f < Mathf.Clamp(m_missChance, 0f, 100f) || m_missChance >= 100f;
            Vector3 shotDirection = miss ? GetMissDirection(origin, spread) : m_aimDirection;

            m_damageByPlayer.Clear();
            m_damageByTruck.Clear();
            m_damageByPedestrian.Clear();
            m_bloodHits.Clear();
            m_tracerEnds.Clear();
            for (int pellet = 0; pellet < m_pelletsPerShot; pellet++)
            {
                Vector3 direction = ApplySpread(shotDirection, spread);
                Vector3 end = origin + direction * m_range;
                int count = Physics.RaycastNonAlloc(origin, direction, m_hits, m_range, m_hitMask, QueryTriggerInteraction.Collide);
                SortHits(count);
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = m_hits[i];
                    if (!CouchGuys.Gameplay.Pedestrians.PedestrianController.IsBulletHit(hit.collider) ||
                        hit.collider.transform.root == transform.root) continue;
                    end = hit.point;
                    var pedestrian = hit.collider.GetComponentInParent<CouchGuys.Gameplay.Pedestrians.PedestrianController>();
                    if (pedestrian != null)
                    {
                        if (!m_bloodHits.ContainsKey(pedestrian)) m_bloodHits.Add(pedestrian, hit);
                        m_damageByPedestrian.TryGetValue(pedestrian, out float total);
                        m_damageByPedestrian[pedestrian] = total + m_damagePerPellet;
                        break;
                    }
                    PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();
                    if (health != null && health.IsAlive)
                    {
                        m_damageByPlayer.TryGetValue(health, out float damage);
                        m_damageByPlayer[health] = damage + m_damagePerPellet;
                    }
                    else
                    {
                        TruckController truck = hit.collider.GetComponentInParent<TruckController>();
                        if (truck != null)
                        {
                            m_damageByTruck.TryGetValue(truck, out float damage);
                            m_damageByTruck[truck] = damage + m_damagePerPellet;
                        }
                    }
                    break;
                }
                m_tracerEnds.Add(end);
            }

            foreach (KeyValuePair<PlayerHealth, float> damage in m_damageByPlayer)
            {
                Vector3 impact = Vector3.ProjectOnPlane(damage.Key.transform.position - transform.position, Vector3.up).normalized;
                damage.Key.ApplyDamageServer(damage.Value, impact, m_knockbackForce);
            }
            foreach (KeyValuePair<TruckController, float> damage in m_damageByTruck)
                damage.Key.ApplyBulletDamageServer(damage.Value);
            foreach (var damage in m_damageByPedestrian)
            {
                RaycastHit bloodHit = m_bloodHits[damage.Key];
                damage.Key.ApplyBulletDamageServer(damage.Value, bloodHit.point, (bloodHit.point - origin).normalized);
            }
            if (m_weaponType == EnemyWeaponType.AssaultRifle)
                m_rifleBurstActive = true;
            ShowShotObserversRpc(origin, m_tracerEnds.ToArray());
        }

        private Vector3 GetMissDirection(Vector3 origin, float spread)
        {
            // Aim outside the target's bounding sphere, allowing room for pellet spread.
            Transform targetRoot = m_targetTruck != null ? m_targetTruck.transform : m_target.transform;
            if (m_target != null && m_target.TryGetComponent(out PlayerTruckInteractor interactor) && interactor.CurrentTruck != null)
                targetRoot = interactor.CurrentTruck.transform;
            targetRoot.GetComponentsInChildren<Collider>(m_targetColliders);
            Bounds bounds = new Bounds(TargetAimPoint, Vector3.zero);
            foreach (Collider collider in m_targetColliders)
                if (collider.enabled && !collider.isTrigger) bounds.Encapsulate(collider.bounds);
            Vector3 offset = bounds.center - origin;
            float radius = bounds.extents.magnitude;
            float angle = Mathf.Asin(Mathf.Clamp01(radius / Mathf.Max(offset.magnitude, 0.01f))) * Mathf.Rad2Deg;
            angle += spread + Random.Range(2f, 6f);
            if (Random.value < 0.5f) angle = -angle;
            return Quaternion.LookRotation(offset.normalized) * Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
        }

        private void SortHits(int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit value = m_hits[i];
                int j = i - 1;
                while (j >= 0 && m_hits[j].distance > value.distance) { m_hits[j + 1] = m_hits[j]; j--; }
                m_hits[j + 1] = value;
            }
        }

        [ObserversRpc(RunLocally = true)]
        private void ShowShotObserversRpc(Vector3 origin, Vector3[] ends)
        {
            m_animationDriver?.TriggerShoot();
            m_audio?.PlayShot((PlayerWeaponType)m_weaponType, origin, m_muzzle);
            if (ends == null) return;
            for (int i = 0; i < ends.Length; i++)
            {
                GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bullet.name = $"{m_weaponType}BulletVisual";
                if (bullet.TryGetComponent(out Collider collider)) Destroy(collider);
                bullet.AddComponent<EnemyBulletVisual>().Initialise(
                    origin, ends[i], m_tracerDuration, m_tracerWidth, m_tracerMaterial, m_tracerColour);
            }
        }

        [Server]
        private void EndRifleBurstServer()
        {
            if (!m_rifleBurstActive)
                return;

            m_rifleBurstActive = false;
            PlayRifleStopObserversRpc(GetMuzzlePosition());
        }

        [ObserversRpc(RunLocally = true)]
        private void PlayRifleStopObserversRpc(Vector3 origin) => m_audio?.PlayRifleStop(origin);

        private Vector3 GetMuzzlePosition() =>
            m_muzzle != null ? m_muzzle.position : transform.position + Vector3.up * 1.1f;

        private static Vector3 ApplySpread(Vector3 forward, float angle)
        {
            if (angle <= 0f) return forward;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector2 spread = Random.insideUnitCircle * Mathf.Tan(angle * Mathf.Deg2Rad);
            return rotation * new Vector3(spread.x, spread.y, 1f).normalized;
        }

    }
}
