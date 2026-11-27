using CouchGuys.Player;
using FishNet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CouchGuys.ProceduralGeneration
{
    [RequireComponent(typeof(JumpingPillow))]
    [DisallowMultipleComponent]
    public sealed class JumpingPillowRewards : MonoBehaviour
    {
        [Header("Wheelie Reward")]
        [SerializeField] private bool m_rewardsEnabled = true;
        [SerializeField] private WheelieSquadReward m_cubePrefab;
        [Tooltip("Chance on each scheduled server check while the pillow has no active cube.")]
        [SerializeField, Range(0f, 100f)] private float m_spawnChancePercent = 5f;
        [SerializeField, Min(.1f)] private float m_spawnCheckInterval = 15f;

        [Header("Placement")]
        [SerializeField, Min(.1f)] private float m_spawnHeightAboveSurface = 2.5f;
        [SerializeField, Min(0f)] private float m_horizontalSpawnRadius = 1.5f;
        [SerializeField, Min(.05f)] private float m_cubeClearance = .4f;
        private readonly Collider[] m_blockers = new Collider[1];
        private JumpingPillow m_pillow;
        private WheelieSquadReward m_activeCube;
        private float m_nextSpawnCheck;

        public float SpawnChancePercent => m_spawnChancePercent;
        public float SpawnCheckInterval => m_spawnCheckInterval;
        public WheelieSquadReward ActiveCube => m_activeCube;
        public WheelieSquadReward CubePrefab => m_cubePrefab;

        private void Awake() => m_pillow = GetComponent<JumpingPillow>();
        private void OnEnable() => m_nextSpawnCheck = Time.time + m_spawnCheckInterval;

        private void Update()
        {
            if (!m_rewardsEnabled || m_cubePrefab == null || m_pillow.SurfaceCollider == null ||
                Time.time < m_nextSpawnCheck || InstanceFinder.ServerManager == null ||
                !InstanceFinder.ServerManager.Started) return;

            m_nextSpawnCheck = Time.time + m_spawnCheckInterval;
            if (m_activeCube != null || m_spawnChancePercent <= 0f ||
                (m_spawnChancePercent < 100f && Random.value * 100f >= m_spawnChancePercent)) return;

            SpawnCube();
        }

        private void SpawnCube()
        {
            var surface = m_pillow.SurfaceCollider;
            Bounds bounds = surface.bounds;
            var physicsScene = gameObject.scene.GetPhysicsScene();
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * m_horizontalSpawnRadius;
                var origin = new Vector3(bounds.center.x + offset.x, bounds.max.y + 1f, bounds.center.z + offset.y);
                if (!surface.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, bounds.size.y + 2f) ||
                    hit.normal.y < m_pillow.MinimumUpNormal) continue;

                Vector3 position = hit.point + Vector3.up * m_spawnHeightAboveSurface;
                if (physicsScene.OverlapBox(position, Vector3.one * m_cubeClearance, m_blockers,
                    Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) != 0) continue;

                var cube = Instantiate(m_cubePrefab, position, Quaternion.identity);
                SceneManager.MoveGameObjectToScene(cube.gameObject, gameObject.scene);
                cube.Initialise(transform.position.y);
                InstanceFinder.ServerManager.Spawn(cube.NetworkObject);
                m_activeCube = cube;
                return;
            }
        }

        private void OnDisable()
        {
            if (m_activeCube != null && m_activeCube.IsServerInitialized && m_activeCube.IsSpawned)
                m_activeCube.ServerManager.Despawn(m_activeCube.NetworkObject);
            m_activeCube = null;
        }
    }
}
