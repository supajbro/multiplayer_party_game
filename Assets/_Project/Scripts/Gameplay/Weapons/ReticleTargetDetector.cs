using CouchGuys.Gameplay.Enemies;
using CouchGuys.Player;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public enum ReticleTargetType
    {
        Neutral,
        Enemy,
        Teammate
    }

    [DisallowMultipleComponent]
    public sealed class ReticleTargetDetector : MonoBehaviour
    {
        private const int HitBufferSize = 16;

        [SerializeField] private Camera m_aimCamera;
        [SerializeField] private PlayerHealth m_ownerHealth;
        [SerializeField] private LayerMask m_hitMask = ~0;
        [SerializeField, Min(1f)] private float m_fallbackRange = 60f;

        private readonly RaycastHit[] m_hits = new RaycastHit[HitBufferSize];

        private void Awake()
        {
            m_aimCamera ??= GetComponentInChildren<Camera>(true);
            m_ownerHealth ??= GetComponent<PlayerHealth>();
        }

        public ReticleTargetType Detect(float weaponRange)
        {
            if (m_aimCamera == null) return ReticleTargetType.Neutral;
            Ray ray = m_aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(ray, m_hits,
                weaponRange > 0f ? weaponRange : m_fallbackRange, m_hitMask,
                QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            Collider nearest = null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = m_hits[index];
                if (hit.collider == null || hit.distance >= nearestDistance) continue;
                PlayerHealth player = hit.collider.GetComponentInParent<PlayerHealth>();
                if (player == m_ownerHealth) continue;
                nearestDistance = hit.distance;
                nearest = hit.collider;
            }

            if (nearest == null) return ReticleTargetType.Neutral;
            ThiefHealth thief = nearest.GetComponentInParent<ThiefHealth>();
            if (thief != null && thief.IsAlive) return ReticleTargetType.Enemy;
            PlayerHealth teammate = nearest.GetComponentInParent<PlayerHealth>();
            return teammate != null && teammate != m_ownerHealth && teammate.IsAlive
                ? ReticleTargetType.Teammate
                : ReticleTargetType.Neutral;
        }
    }
}
