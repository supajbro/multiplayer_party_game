using System.Collections.Generic;
using CouchGuys.Player;
using FishNet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Observes real server-side bounce motion; no client spawn requests or per-frame random rolls.</summary>
    [RequireComponent(typeof(JumpingPillow))]
    [DisallowMultipleComponent]
    public sealed class JumpingPillowRewards : MonoBehaviour
    {
        [Header("Wheelie Reward")]
        [SerializeField] private bool m_rewardsEnabled = true;
        [SerializeField] private WheelieSquadReward m_cubePrefab;
        [Tooltip("Chance once per qualifying bounce. Zero disables rewards; 100 guarantees a roll succeeds.")]
        [SerializeField, Range(0f, 100f)] private float m_spawnChancePercent = 8f;
        [SerializeField, Min(0f)] private float m_spawnCooldown = 60f;
        [Header("Jump Height")]
        [SerializeField, Min(.1f)] private float m_minimumJumpHeight = 1.25f;
        [SerializeField, Min(.1f)] private float m_fullBonusJumpHeight = 4f;
        [SerializeField, Min(1f)] private float m_highJumpChanceMultiplier = 1.5f;
        [Tooltip("Fraction of the predicted apex used for placement. Kept below the player's reachable height.")]
        [SerializeField, Range(.3f, .95f)] private float m_cubeHeightFraction = .85f;
        [SerializeField, Min(0f)] private float m_horizontalSpawnRadius = 1.5f;
        [SerializeField, Min(.05f)] private float m_cubeClearance = .4f;
        [Header("Bounce Detection")]
        [SerializeField, Min(.02f)] private float m_observationInterval = .05f;
        [SerializeField, Min(.05f)] private float m_contactTolerance = .45f;
        [SerializeField, Min(.1f)] private float m_maximumFlightDuration = 6f;
        private sealed class Bounce
        {
            public CharacterController Capsule;
            public ThirdPersonPlayerController Movement;
            public float PreviousFeet, PreviousTime, ContactTime, ContactHeight;
            public bool Armed, Rolled;
        }
        private readonly Dictionary<PlayerHealth, Bounce> m_bounces = new();
        private readonly List<PlayerHealth> m_expired = new();
        private readonly Collider[] m_blockers = new Collider[1];
        private JumpingPillow m_pillow;
        private WheelieSquadReward m_activeCube;
        private float m_nextObservation, m_nextSpawn;
        public float SpawnChancePercent => m_spawnChancePercent;
        public WheelieSquadReward ActiveCube => m_activeCube;
        public WheelieSquadReward CubePrefab => m_cubePrefab;

        private void Awake() => m_pillow = GetComponent<JumpingPillow>();
        public float ChanceForHeight(float height)
        {
            if (height < m_minimumJumpHeight) return 0f;
            float bonus = Mathf.InverseLerp(m_minimumJumpHeight, Mathf.Max(m_minimumJumpHeight + .01f, m_fullBonusJumpHeight), height);
            return Mathf.Clamp(m_spawnChancePercent * Mathf.Lerp(1f, m_highJumpChanceMultiplier, bonus), 0f, 100f);
        }

        private void Update()
        {
            if (!m_rewardsEnabled || m_cubePrefab == null || InstanceFinder.ServerManager == null ||
                !InstanceFinder.ServerManager.Started || Time.time < m_nextObservation || m_pillow.SurfaceCollider == null) return;
            m_nextObservation = Time.time + m_observationInterval;
            foreach (PlayerHealth player in PlayerHealth.Instances)
            {
                if (player == null || !player.IsSpawned || !player.IsServerInitialized || !player.IsAlive ||
                    player.IsAiTeammate || !player.Owner.IsValid || !player.Owner.IsActive || player.gameObject.scene != gameObject.scene ||
                    !TrySurface(player.transform.position, out RaycastHit surface)) continue;
                if (!m_bounces.TryGetValue(player, out Bounce bounce))
                {
                    bounce = new Bounce { Capsule = player.GetComponent<CharacterController>(), Movement = player.GetComponent<ThirdPersonPlayerController>(),
                        PreviousTime = Time.time, ContactTime = float.NegativeInfinity };
                    m_bounces.Add(player, bounce);
                    bounce.PreviousFeet = Feet(player, bounce.Capsule);
                }
                if (bounce.Capsule == null || !bounce.Capsule.enabled || bounce.Movement == null ||
                    (player.TryGetComponent(out PlayerTruckInteractor truck) && truck.CurrentTruck != null) ||
                    (player.TryGetComponent(out PlayerHoverboardInteractor board) && board.IsMounted))
                { bounce.Armed = false; continue; }
                float feet = Feet(player, bounce.Capsule);
                float elapsed = Time.time - bounce.PreviousTime;
                float verticalSpeed = elapsed > .001f ? (feet - bounce.PreviousFeet) / elapsed : 0f;
                bounce.PreviousFeet = feet; bounce.PreviousTime = Time.time;
                float height = feet - surface.point.y;
                if (height >= -m_contactTolerance && height <= m_contactTolerance)
                {
                    // Only a return to the surface arms the next bounce. Airborne observers never reroll.
                    bounce.Armed = true; bounce.Rolled = false; bounce.ContactTime = Time.time; bounce.ContactHeight = surface.point.y;
                }
                if (!bounce.Armed || bounce.Rolled || Time.time - bounce.ContactTime > m_maximumFlightDuration ||
                    height < m_minimumJumpHeight || verticalSpeed <= 0f) continue;
                bounce.Rolled = true;
                float gravity = Mathf.Max(.01f, -bounce.Movement.Gravity);
                float apex = Mathf.Clamp(feet - bounce.ContactHeight + verticalSpeed * verticalSpeed / (2f * gravity),
                    height, Mathf.Max(height, m_pillow.BounceHeight * 1.15f));
                if (m_activeCube != null || Time.time < m_nextSpawn || Random.value * 100f >= ChanceForHeight(apex)) continue;
                SpawnCube(player, bounce.Capsule, apex);
            }
            m_expired.Clear();
            foreach (var pair in m_bounces)
                if (pair.Key == null || !pair.Key.IsSpawned || Time.time - pair.Value.PreviousTime > m_maximumFlightDuration)
                    m_expired.Add(pair.Key);
            foreach (var player in m_expired) m_bounces.Remove(player);
        }

        private void SpawnCube(PlayerHealth player, CharacterController capsule, float apex)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * m_horizontalSpawnRadius;
                Vector3 sample = player.transform.position + new Vector3(offset.x, 0f, offset.y);
                if (!TrySurface(sample, out RaycastHit surface)) continue;
                float bodyHeight = capsule.height * Mathf.Abs(capsule.transform.lossyScale.y);
                Vector3 position = surface.point + Vector3.up * (apex * m_cubeHeightFraction + bodyHeight * .7f);
                if (gameObject.scene.GetPhysicsScene().OverlapBox(position, Vector3.one * m_cubeClearance, m_blockers,
                    Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) != 0) continue;
                var cube = Instantiate(m_cubePrefab, position, Quaternion.identity);
                SceneManager.MoveGameObjectToScene(cube.gameObject, gameObject.scene);
                cube.Initialise(transform.position.y);
                InstanceFinder.ServerManager.Spawn(cube.NetworkObject);
                m_activeCube = cube; m_nextSpawn = Time.time + m_spawnCooldown;
                return;
            }
        }
        private bool TrySurface(Vector3 point, out RaycastHit hit)
        {
            Bounds bounds = m_pillow.SurfaceCollider.bounds;
            return m_pillow.SurfaceCollider.Raycast(new Ray(new Vector3(point.x, bounds.max.y + 1f, point.z), Vector3.down),
                out hit, bounds.size.y + 2f) && hit.normal.y >= m_pillow.MinimumUpNormal;
        }
        private static float Feet(PlayerHealth player, CharacterController capsule) => capsule != null
            ? capsule.transform.TransformPoint(capsule.center).y - capsule.height * Mathf.Abs(capsule.transform.lossyScale.y) * .5f
            : player.transform.position.y;
        private void OnDisable()
        {
            m_bounces.Clear();
            if (m_activeCube != null && m_activeCube.IsServerInitialized && m_activeCube.IsSpawned)
                m_activeCube.ServerManager.Despawn(m_activeCube.NetworkObject);
            m_activeCube = null;
        }
    }
}
