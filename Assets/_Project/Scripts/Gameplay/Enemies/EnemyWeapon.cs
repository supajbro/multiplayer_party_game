using System.Collections.Generic;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace CouchGuys.Gameplay.Enemies
{
    [DisallowMultipleComponent]
    public sealed class EnemyWeapon : NetworkBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private EnemyWeaponType m_weaponType = EnemyWeaponType.Pistol;
        [SerializeField] private Transform m_muzzle;
        [SerializeField, Min(0.05f)] private float m_fireInterval = 0.8f;
        [SerializeField, Min(1f)] private float m_range = 30f;
        [SerializeField, Min(1)] private int m_pelletsPerShot = 1;
        [SerializeField, Min(0f)] private float m_spreadAngle = 5f;
        [SerializeField, Min(0f)] private float m_damagePerPellet = 20f;
        [SerializeField, Min(0f)] private float m_knockbackForce = 5f;
        [SerializeField, Min(0f)] private float m_preferredRange = 12f;
        [SerializeField] private LayerMask m_hitMask = ~0;

        [Header("Accuracy and Pacing")]
        [SerializeField, Range(0f, 1f)] private float m_baseAccuracy = 0.72f;
        [SerializeField, Range(0f, 1f)] private float m_movingAccuracyPenalty = 0.2f;
        [SerializeField, Range(0f, 1f)] private float m_distanceAccuracyPenalty = 0.35f;
        [SerializeField, Min(0f)] private float m_reactionTime = 0.65f;
        [SerializeField, Min(1f)] private float m_aimRotationSpeed = 150f;
        [SerializeField, Min(1)] private int m_minBurstShots = 1;
        [SerializeField, Min(1)] private int m_maxBurstShots = 3;
        [SerializeField, Min(0f)] private float m_burstCooldown = 1.1f;
        [SerializeField, Range(0f, 45f)] private float m_requiredAimAngle = 16f;

        [Header("Bullet Visuals")]
        [SerializeField, Min(0.01f)] private float m_tracerDuration = 0.16f;
        [SerializeField, Min(0.005f)] private float m_tracerWidth = 0.025f;
        [SerializeField] private Color m_tracerColour = new(1f, 0.72f, 0.12f, 1f);

        private readonly Dictionary<PlayerHealth, float> m_damageByPlayer = new();
        private readonly List<Vector3> m_tracerEnds = new(12);
        private readonly RaycastHit[] m_hits = new RaycastHit[24];
        private PlayerHealth m_target;
        private Material m_tracerMaterial;
        private ThiefAnimationDriver m_animationDriver;
        private NavMeshAgent m_agent;
        private Vector3 m_aimDirection;
        private float m_nextFireTime;
        private float m_reactionEndsAt;
        private int m_burstShotsRemaining;

        public EnemyWeaponType WeaponType => m_weaponType;
        public float PreferredRange => m_preferredRange;

        private void Awake()
        {
            m_animationDriver = GetComponentInChildren<ThiefAnimationDriver>(true);
            m_agent = GetComponent<NavMeshAgent>();
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
            if (m_target != target)
            {
                m_reactionEndsAt = Time.time + m_reactionTime;
                m_burstShotsRemaining = Random.Range(m_minBurstShots, m_maxBurstShots + 1);
            }
            m_target = target;
        }

        private void Update()
        {
            if (!IsServerInitialized || m_target == null || !m_target.IsAlive) return;
            Vector3 desired = (m_target.AimPoint - GetMuzzlePosition()).normalized;
            m_aimDirection = Vector3.RotateTowards(m_aimDirection, desired,
                m_aimRotationSpeed * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
            if (Time.time < m_reactionEndsAt || Time.time < m_nextFireTime || !CanShootTarget(desired)) return;

            FireServer();
            m_burstShotsRemaining--;
            if (m_burstShotsRemaining <= 0)
            {
                m_burstShotsRemaining = Random.Range(m_minBurstShots, m_maxBurstShots + 1);
                m_nextFireTime = Time.time + m_burstCooldown;
            }
            else m_nextFireTime = Time.time + m_fireInterval;
        }

        private bool CanShootTarget(Vector3 desired)
        {
            Vector3 origin = GetMuzzlePosition();
            Vector3 offset = m_target.AimPoint - origin;
            if (offset.sqrMagnitude > m_range * m_range ||
                Vector3.Angle(m_aimDirection, desired) > m_requiredAimAngle) return false;
            int count = Physics.RaycastNonAlloc(origin, desired, m_hits, m_range, m_hitMask, QueryTriggerInteraction.Ignore);
            SortHits(count);
            for (int i = 0; i < count; i++)
            {
                if (m_hits[i].collider.transform.root == transform.root) continue;
                return m_hits[i].collider.GetComponentInParent<PlayerHealth>() == m_target;
            }
            return false;
        }

        [Server]
        private void FireServer()
        {
            Vector3 origin = GetMuzzlePosition();
            float distance01 = Mathf.Clamp01(Vector3.Distance(origin, m_target.AimPoint) / m_range);
            bool moving = m_agent != null && m_agent.enabled && m_agent.velocity.sqrMagnitude > 0.2f;
            float accuracy = Mathf.Clamp01(m_baseAccuracy - distance01 * m_distanceAccuracyPenalty -
                (moving ? m_movingAccuracyPenalty : 0f));
            float spread = m_spreadAngle * Mathf.Lerp(1.8f, 0.25f, accuracy);

            m_damageByPlayer.Clear();
            m_tracerEnds.Clear();
            for (int pellet = 0; pellet < m_pelletsPerShot; pellet++)
            {
                Vector3 direction = ApplySpread(m_aimDirection, spread);
                Vector3 end = origin + direction * m_range;
                int count = Physics.RaycastNonAlloc(origin, direction, m_hits, m_range, m_hitMask, QueryTriggerInteraction.Ignore);
                SortHits(count);
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = m_hits[i];
                    if (hit.collider.transform.root == transform.root) continue;
                    end = hit.point;
                    PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();
                    if (health != null && health.IsAlive)
                    {
                        m_damageByPlayer.TryGetValue(health, out float damage);
                        m_damageByPlayer[health] = damage + m_damagePerPellet;
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
            ShowShotObserversRpc(origin, m_tracerEnds.ToArray());
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

        private Vector3 GetMuzzlePosition() =>
            m_muzzle != null ? m_muzzle.position : transform.position + Vector3.up * 1.1f;

        private static Vector3 ApplySpread(Vector3 forward, float angle)
        {
            if (angle <= 0f) return forward;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector2 spread = Random.insideUnitCircle * Mathf.Tan(angle * Mathf.Deg2Rad);
            return rotation * new Vector3(spread.x, spread.y, 1f).normalized;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_fireInterval = Mathf.Max(0.05f, m_fireInterval);
            m_range = Mathf.Max(1f, m_range);
            m_pelletsPerShot = Mathf.Max(1, m_pelletsPerShot);
            m_minBurstShots = Mathf.Max(1, m_minBurstShots);
            m_maxBurstShots = Mathf.Max(m_minBurstShots, m_maxBurstShots);
        }
#endif
    }
}
