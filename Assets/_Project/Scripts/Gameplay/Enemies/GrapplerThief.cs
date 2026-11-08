using CouchGuys.Gameplay.Delivery;
using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Enemies
{
    /// <summary>Server combat policy; movement, health and owner physics use the existing thief/player systems.</summary>
    [DisallowMultipleComponent]
    public sealed class GrapplerThief : NetworkBehaviour
    {
        [SerializeField] private GrapplerThiefSettings m_settings;
        [SerializeField] private Transform m_gun;
        [SerializeField] private Transform m_muzzle;
        [SerializeField] private LineRenderer m_rope;
        private readonly SyncVar<NetworkObject> m_aimTarget = new();
        private readonly SyncVar<NetworkObject> m_heldTarget = new();
        private readonly RaycastHit[] m_hits = new RaycastHit[32];
        private ThiefHealth m_health;
        private ThiefAnimationDriver m_animation;
        private DeliveryManager m_delivery;
        private PlayerHealth m_target, m_held;
        private NetworkObject m_attackCouch;
        private float m_nextEvaluation, m_nextAttack, m_windupEnds, m_releaseAt, m_nextPull;
        private Vector3 m_missPoint;
        private float m_missUntil;
        public PlayerHealth Target => m_held != null ? m_held : m_target;
        public float PreferredDistance => m_settings.PreferredDistance;
        public bool IsHolding => m_held != null;

        private void Awake()
        {
            m_health = GetComponent<ThiefHealth>();
            m_animation = GetComponent<ThiefAnimationDriver>();
        }

        public override void OnStartServer()
        {
            m_delivery = FindFirstObjectByType<DeliveryManager>();
            m_nextEvaluation = 0f;
            m_nextAttack = Time.time + m_settings.AimWindup;
        }

        public static bool IsTargetable(PlayerHealth player) => player != null &&
            player.IsEnemyTargetable &&
            ((player.Owner.IsValid && player.Owner.IsActive) || player.IsAiTeammate ||
                (player.TryGetComponent(out DebugCouchBotController bot) && bot.IsAiTeammate)) &&
            !(player.TryGetComponent(out PlayerTruckInteractor truck) && truck.CurrentTruck != null);

        public static PlayerHealth FindTarget(Vector3 couchPosition)
        {
            PlayerHealth closest = null;
            float best = float.PositiveInfinity;
            foreach (PlayerHealth player in PlayerHealth.Instances)
            {
                if (!IsTargetable(player)) continue;
                float distance = (player.transform.position - couchPosition).sqrMagnitude;
                if (distance < best) { best = distance; closest = player; }
            }
            return closest;
        }

        private void Update()
        {
            if (!IsServerInitialized || m_settings == null) return;
            NetworkObject couch = m_delivery != null && TryGetComponent(out ThiefSpawnMember member) && m_delivery.TryGetDelivery(member.DeliveryId, out var job) ? job.Couch : null;
            if (!m_health.IsAlive || m_health.IsBeingKnockedBack || couch == null)
            {
                CancelAttack();
                m_target = null;
                m_aimTarget.Value = null;
                return;
            }
            if (m_attackCouch != couch)
            {
                CancelAttack();
                m_attackCouch = couch;
                m_nextEvaluation = 0f;
            }
            if (m_held != null && !IsTargetable(m_held)) CancelAttack();
            if (Time.time >= m_nextEvaluation || !IsTargetable(m_target))
            {
                m_nextEvaluation = Time.time + Mathf.Max(0.1f, m_settings.TargetEvaluationInterval);
                PlayerHealth target = FindTarget(couch.transform.position);
                if (target != m_target) m_windupEnds = 0f;
                m_target = target;
                m_aimTarget.Value = target != null ? target.NetworkObject : null;
            }
            if (m_held != null)
            {
                if (!IsTargetable(m_held) || !HasLineOfSight(m_held)) { CancelAttack(); return; }
                if (Time.time >= m_releaseAt)
                {
                    Vector3 velocity = m_settings.CalculateThrowVelocity(m_held.transform.position,
                        couch.transform.position, transform.forward,
                        Random.Range(-m_settings.LateralVariationDegrees, m_settings.LateralVariationDegrees));
                    m_held.ApplyEnemyGrappleThrowServer(velocity, m_settings.MomentumDuration, m_settings.AirDrag);
                    CancelAttack();
                }
                else if (Time.time >= m_nextPull)
                {
                    m_nextPull = Time.time + 0.1f;
                    Vector3 offset = m_muzzle.position - m_held.AimPoint;
                    m_held.ApplyGrapplePullServer(offset.normalized, m_settings.PullAcceleration,
                        Mathf.Min(m_settings.PullSpeed, Mathf.Max(0f, offset.magnitude - 2f) * 2f), 0.1f);
                }
                return;
            }
            if (!IsTargetable(m_target) || Time.time < m_nextAttack || !m_target.CanBeEnemyGrappled ||
                !HasLineOfSight(m_target) || Vector3.Angle(transform.forward,
                    Vector3.ProjectOnPlane(m_target.AimPoint - transform.position, Vector3.up)) > 16f)
            {
                m_windupEnds = 0f;
                return;
            }
            if (m_windupEnds == 0f) m_windupEnds = Time.time + m_settings.AimWindup;
            if (Time.time < m_windupEnds) return;
            // The same instant-hit ray used by the player grapple. Only the first non-self
            // collider can attach; cover, couches, vehicles and other enemies cause a miss.
            Vector3 direction = (m_target.AimPoint - m_muzzle.position).normalized;
            bool hit = Trace(m_muzzle.position, direction, m_settings.GrappleRange, out RaycastHit contact);
            m_nextAttack = Time.time + Mathf.Max(0.1f, m_settings.AttackCooldown);
            m_windupEnds = 0f;
            bool attached = hit && contact.collider.GetComponentInParent<PlayerHealth>() == m_target &&
                m_target.TryClaimEnemyGrappleServer(m_settings.HoldDuration + m_settings.PlayerRecoveryTime);
            ShowShotObserversRpc(hit ? contact.point : m_muzzle.position + direction * m_settings.GrappleRange, attached);
            if (!attached) return;
            m_held = m_target;
            m_heldTarget.Value = m_held.NetworkObject;
            m_releaseAt = Time.time + Mathf.Max(0.1f, m_settings.HoldDuration);
            m_nextPull = 0f;
            m_held.GetComponent<PlayerCouchCarrier>()?.ReleaseForDamageServer();
        }

        public bool HasLineOfSight(PlayerHealth player)
        {
            if (player == null || m_muzzle == null || m_settings == null) return false;
            Vector3 offset = player.AimPoint - m_muzzle.position;
            return offset.sqrMagnitude <= m_settings.GrappleRange * m_settings.GrappleRange &&
                Trace(m_muzzle.position, offset.normalized, offset.magnitude + 0.25f, out RaycastHit hit) &&
                hit.collider.GetComponentInParent<PlayerHealth>() == player;
        }

        public bool HasSightFrom(Vector3 origin, PlayerHealth target)
        {
            Vector3 offset = target.AimPoint - origin;
            return Trace(origin, offset.normalized, offset.magnitude + 0.25f, out RaycastHit hit) &&
                hit.collider.GetComponentInParent<PlayerHealth>() == target;
        }

        private bool Trace(Vector3 origin, Vector3 direction, float range, out RaycastHit nearest)
        {
            nearest = default;
            int count = Physics.RaycastNonAlloc(origin, direction, m_hits, range,
                m_settings.HitMask, QueryTriggerInteraction.Ignore);
            // Saturation is a miss, so crowded scenes cannot grapple through omitted cover.
            if (count == m_hits.Length) return false;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (m_hits[i].collider.transform.IsChildOf(transform) || m_hits[i].distance >= distance) continue;
                nearest = m_hits[i];
                distance = nearest.distance;
            }
            return distance < float.PositiveInfinity;
        }

        private void CancelAttack()
        {
            m_held = null;
            m_heldTarget.Value = null;
            m_windupEnds = 0f;
        }

        public override void OnStopServer()
        {
            CancelAttack();
            m_aimTarget.Value = null;
        }

        [ObserversRpc(RunLocally = true)]
        private void ShowShotObserversRpc(Vector3 point, bool attached)
        {
            m_animation?.TriggerShoot();
            m_missPoint = point;
            m_missUntil = attached ? 0f : Time.time + 0.18f;
        }

        private void LateUpdate()
        {
            if (m_settings == null || m_gun == null || m_rope == null) return;
            bool alive = m_health.IsAlive && !m_health.IsBeingKnockedBack;
            NetworkObject target = m_heldTarget.Value != null ? m_heldTarget.Value : m_aimTarget.Value;
            if (alive && target != null && target.TryGetComponent(out PlayerHealth player))
            {
                Vector3 aim = player.AimPoint - m_gun.position;
                if (aim.sqrMagnitude > 0.001f) m_gun.rotation = Quaternion.LookRotation(aim);
            }
            NetworkObject held = m_heldTarget.Value;
            m_rope.enabled = alive && ((held != null && held.IsSpawned) || Time.time < m_missUntil);
            if (!m_rope.enabled) return;
            m_rope.SetPosition(0, m_muzzle.position);
            m_rope.SetPosition(1, held != null && held.TryGetComponent(out PlayerHealth health)
                ? health.AimPoint : m_missPoint);
        }
    }
}
