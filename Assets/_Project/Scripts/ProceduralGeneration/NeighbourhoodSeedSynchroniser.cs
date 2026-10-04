using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>
    /// Synchronises only the host-selected seed. Every peer then builds the same static layout locally.
    /// Add this and a NetworkObject beside the generator in multiplayer scenes.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NeighbourhoodGenerator))]
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodSeedSynchroniser : NetworkBehaviour
    {
        private readonly SyncVar<int> m_authoritativeSeed = new SyncVar<int>();
        [SerializeField] private NeighbourhoodGenerator m_generator;

        private int? m_appliedSeed;

        public int AuthoritativeSeed => m_authoritativeSeed.Value;

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
            m_authoritativeSeed.OnChange += OnSeedChanged;
        }

        private void OnDestroy()
        {
            m_authoritativeSeed.OnChange -= OnSeedChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            int seed = m_generator.SelectSeed();
            m_authoritativeSeed.Value = seed;
            ApplySeed(seed);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplySeed(m_authoritativeSeed.Value);
        }

        private void OnSeedChanged(int previous, int next, bool asServer)
        {
            ApplySeed(next);
        }

        private void ApplySeed(int seed)
        {
            if (m_generator == null || (m_appliedSeed.HasValue && m_appliedSeed.Value == seed))
            {
                return;
            }

            m_appliedSeed = seed;
            m_generator.Generate(seed);
        }
    }
}
