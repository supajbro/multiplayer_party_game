using System.Collections.Generic;
using CouchGuys.Player;
using CouchGuys.ProceduralGeneration;
using FishNet;
using UnityEngine;

namespace CouchGuys.Gameplay.Pedestrians
{
    [DisallowMultipleComponent]
    public sealed class PedestrianManager : MonoBehaviour
    {
        private static readonly List<PedestrianManager> s_managers = new List<PedestrianManager>();
        private readonly List<PedestrianController> m_active = new List<PedestrianController>();
        private readonly SidewalkGraph m_graph = new SidewalkGraph();
        [SerializeField] private PedestrianSettings m_settings;
        private NeighbourhoodGenerator m_generator;
        private bool m_built;
        private float m_nextPopulationUpdate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_managers.Clear();

        private void Awake()
        {
            m_generator = GetComponent<NeighbourhoodGenerator>();
            m_settings ??= Resources.Load<PedestrianSettings>("Pedestrians/PedestrianSettings");
        }

        private void OnEnable()
        {
            s_managers.Add(this);
            if (m_generator != null)
            {
                m_generator.RegionClearing += ClearRegion;
                m_generator.RegionGenerated += RegionGenerated;
            }
        }

        private void OnDisable()
        {
            s_managers.Remove(this);
            if (m_generator != null)
            {
                m_generator.RegionClearing -= ClearRegion;
                m_generator.RegionGenerated -= RegionGenerated;
            }
            ClearRegion(m_generator);
        }

        private void RegionGenerated(NeighbourhoodGenerator generator) => m_built = false;

        private void Update()
        {
            if (!ServerStarted || m_settings == null || m_generator == null ||
                !m_generator.IsGenerationReady || m_generator.SelectedRegion == null ||
                m_generator.SelectedRegion.RegionId != "suburbs") return;
            if (!m_built)
            {
                m_graph.Build(m_generator.PedestrianSidewalks, m_generator.SamplePedestrianSidewalk);
                m_built = true;
                if (m_graph.SpawnNodes.Count == 0)
                    Debug.LogWarning("Pedestrians: no continuous sidewalk loops in this region.", this);
            }
            if (Time.time < m_nextPopulationUpdate) return;
            m_nextPopulationUpdate = Time.time + 0.5f;
            for (int i = m_active.Count - 1; i >= 0; i--)
            {
                PedestrianController npc = m_active[i];
                if (npc == null || !npc.IsSpawned) { m_active.RemoveAt(i); continue; }
                if (DistanceToClosestPlayer(npc.transform.position) >
                    Mathf.Max(m_settings.ActivationDistance + 5f, m_settings.DespawnDistance) ||
                    m_active.Count > Mathf.Max(0, m_settings.Population)) Despawn(npc);
            }
            if (m_active.Count >= Mathf.Max(0, m_settings.Population) ||
                m_graph.SpawnNodes.Count == 0 || m_settings.Characters.Length == 0) return;
            // Bounded work, distributed across the region and relevant to every player.
            for (int attempt = 0; attempt < 32; attempt++)
            {
                int node = m_graph.SpawnNodes[Random.Range(0, m_graph.SpawnNodes.Count)];
                Vector3 position = m_graph.Nodes[node].Position;
                float distance = DistanceToClosestPlayer(position);
                if (distance < m_settings.MinimumPlayerSpawnDistance || distance > m_settings.ActivationDistance)
                    continue;
                bool occupied = false;
                foreach (PedestrianController npc in m_active)
                    if ((npc.transform.position - position).sqrMagnitude < 4f) { occupied = true; break; }
                if (occupied) continue;
                var prefab = m_settings.Characters[Random.Range(0, m_settings.Characters.Length)];
                if (prefab == null) return;
                int next = m_graph.ChooseNext(node, -1);
                Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(
                    m_graph.Nodes[next].Position - position, Vector3.up));
                var instance = Instantiate(prefab, position, facing);
                InstanceFinder.ServerManager.Spawn(instance);
                PedestrianController controller = instance.GetComponent<PedestrianController>();
                controller.InitialiseServer(this, m_graph, m_settings, node, next);
                m_active.Add(controller);
                break;
            }
        }

        public static void ReportGunshotServer(Vector3 origin)
        {
            if (!ServerStarted) return;
            foreach (PedestrianManager manager in s_managers)
            {
                if (manager.m_settings == null) continue;
                float radius = Mathf.Max(0f, manager.m_settings.GunshotDetectionRadius);
                foreach (PedestrianController npc in manager.m_active)
                    if (npc != null && (npc.transform.position - origin).sqrMagnitude <= radius * radius)
                        npc.FrightenServer(origin);
            }
        }

        internal void Despawn(PedestrianController npc)
        {
            m_active.Remove(npc);
            if (ServerStarted && npc != null && npc.IsSpawned)
                InstanceFinder.ServerManager.Despawn(npc.NetworkObject);
        }

        private void ClearRegion(NeighbourhoodGenerator generator)
        {
            for (int i = m_active.Count - 1; i >= 0; i--) Despawn(m_active[i]);
            m_active.Clear();
            m_built = false;
        }

        private static float DistanceToClosestPlayer(Vector3 position)
        {
            float nearest = float.PositiveInfinity;
            foreach (PlayerHealth player in PlayerHealth.Instances)
                if (player != null && player.IsSpawned && player.IsAlive)
                    nearest = Mathf.Min(nearest, (player.transform.position - position).sqrMagnitude);
            return Mathf.Sqrt(nearest);
        }

        private static bool ServerStarted => InstanceFinder.NetworkManager != null &&
            InstanceFinder.NetworkManager.ServerManager.Started;
    }
}
