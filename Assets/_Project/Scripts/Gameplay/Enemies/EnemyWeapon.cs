using System.Collections.Generic;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Server-authoritative hitscan weapon with replicated placeholder tracers.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyWeapon : NetworkBehaviour
    {
        [Header("Weapon")]
        [SerializeField] private EnemyWeaponType m_weaponType = EnemyWeaponType.Pistol;
        [SerializeField] private Transform m_muzzle;
        [SerializeField, Min(0.05f)] private float m_fireInterval = 0.8f;
        [SerializeField, Min(1f)] private float m_range = 30f;
        [SerializeField, Min(1)] private int m_pelletsPerShot = 1;
        [SerializeField, Min(0f)] private float m_spreadAngle = 2f;
        [SerializeField, Min(0f)] private float m_damagePerPellet = 20f;
        [SerializeField, Min(0f)] private float m_knockbackForce = 5f;
        [SerializeField, Min(0f)] private float m_preferredRange = 12f;
        [SerializeField] private LayerMask m_hitMask = ~0;

        [Header("Bullet Visuals")]
        [Tooltip("Travel time of the visual cube. Damage remains instant and server-authoritative.")]
        [SerializeField, Min(0.01f)] private float m_tracerDuration = 0.16f;
        [SerializeField, Min(0.005f)] private float m_tracerWidth = 0.025f;
        [SerializeField] private Color m_tracerColour = new Color(1f, 0.72f, 0.12f, 1f);

        private readonly Dictionary<PlayerHealth, float> m_damageByPlayer = new();
        private readonly List<Vector3> m_tracerEnds = new(12);
        private PlayerHealth m_target;
        private Material m_tracerMaterial;
        private ThiefAnimationDriver m_animationDriver;
        private float m_nextFireTime;

        public EnemyWeaponType WeaponType => m_weaponType;
        public float PreferredRange => m_preferredRange;

        private void Awake()
        {
            m_animationDriver = GetComponentInChildren<ThiefAnimationDriver>(true);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                m_tracerMaterial = new Material(shader);
            }
        }

        private void OnDestroy()
        {
            if (m_tracerMaterial != null)
            {
                Destroy(m_tracerMaterial);
            }
        }

        public void SetTargetServer(PlayerHealth target)
        {
            if (!IsServerInitialized && NetworkObject != null && NetworkObject.IsSpawned)
            {
                return;
            }

            m_target = target;
        }

        private void Update()
        {
            if (!IsServerInitialized || Time.time < m_nextFireTime || !CanShootTarget())
            {
                return;
            }

            FireServer();
            m_nextFireTime = Time.time + m_fireInterval;
        }

        private bool CanShootTarget()
        {
            if (m_target == null || !m_target.IsAlive)
            {
                return false;
            }

            Vector3 origin = GetMuzzlePosition();
            Vector3 offset = m_target.AimPoint - origin;
            return offset.sqrMagnitude <= m_range * m_range;
        }

        [Server]
        private void FireServer()
        {
            Vector3 origin = GetMuzzlePosition();
            Vector3 aimDirection = (m_target.AimPoint - origin).normalized;
            m_damageByPlayer.Clear();
            m_tracerEnds.Clear();

            for (int pelletIndex = 0; pelletIndex < m_pelletsPerShot; pelletIndex++)
            {
                Vector3 direction = ApplySpread(aimDirection, m_spreadAngle);
                Vector3 end = origin + direction * m_range;
                RaycastHit[] hits = Physics.RaycastAll(
                    origin,
                    direction,
                    m_range,
                    m_hitMask,
                    QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
                {
                    RaycastHit hit = hits[hitIndex];
                    if (hit.collider.transform.root == transform.root)
                    {
                        continue;
                    }

                    end = hit.point;
                    PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();
                    if (health != null && health.IsAlive)
                    {
                        m_damageByPlayer.TryGetValue(health, out float accumulatedDamage);
                        m_damageByPlayer[health] = accumulatedDamage + m_damagePerPellet;
                    }

                    break;
                }

                m_tracerEnds.Add(end);
            }

            foreach (KeyValuePair<PlayerHealth, float> damage in m_damageByPlayer)
            {
                Vector3 impactDirection = Vector3.ProjectOnPlane(
                    damage.Key.transform.position - transform.position,
                    Vector3.up).normalized;
                damage.Key.ApplyDamageServer(damage.Value, impactDirection, m_knockbackForce);
            }

            ShowShotObserversRpc(origin, m_tracerEnds.ToArray());
        }

        [ObserversRpc(RunLocally = true)]
        private void ShowShotObserversRpc(Vector3 origin, Vector3[] ends)
        {
            m_animationDriver?.TriggerShoot();
            if (ends == null)
            {
                return;
            }

            for (int index = 0; index < ends.Length; index++)
            {
                GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bullet.name = $"{m_weaponType}BulletVisual";
                if (bullet.TryGetComponent(out Collider bulletCollider))
                {
                    Destroy(bulletCollider);
                }

                bullet.AddComponent<EnemyBulletVisual>().Initialise(
                    origin,
                    ends[index],
                    m_tracerDuration,
                    m_tracerWidth,
                    m_tracerMaterial,
                    m_tracerColour);
            }
        }

        private Vector3 GetMuzzlePosition()
        {
            return m_muzzle != null ? m_muzzle.position : transform.position + Vector3.up * 1.1f;
        }

        private static Vector3 ApplySpread(Vector3 forward, float spreadAngle)
        {
            if (spreadAngle <= 0f)
            {
                return forward;
            }

            Quaternion aimRotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector2 spread = Random.insideUnitCircle * Mathf.Tan(spreadAngle * Mathf.Deg2Rad);
            return aimRotation * new Vector3(spread.x, spread.y, 1f).normalized;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            m_fireInterval = Mathf.Max(0.05f, m_fireInterval);
            m_range = Mathf.Max(1f, m_range);
            m_pelletsPerShot = Mathf.Max(1, m_pelletsPerShot);
            m_damagePerPellet = Mathf.Max(0f, m_damagePerPellet);
            m_knockbackForce = Mathf.Max(0f, m_knockbackForce);
        }
#endif
    }
}
