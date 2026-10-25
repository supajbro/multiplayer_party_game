using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using CouchGuys.UI;
using System.Collections.Generic;

namespace CouchGuys.ProceduralGeneration
{
    public struct StreetlightPose
    {
        public int Index;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>
    /// Synchronises the host-selected layout and server-simulated street light poses.
    /// Add this and a NetworkObject beside the generator in multiplayer scenes.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NeighbourhoodGenerator))]
    [DisallowMultipleComponent]
    public sealed class NeighbourhoodSeedSynchroniser : NetworkBehaviour
    {
        private readonly SyncVar<int> m_authoritativeSeed = new SyncVar<int>();
        private readonly SyncVar<int> m_authoritativeRegionIndex = new SyncVar<int>(-1);
        [SerializeField] private NeighbourhoodGenerator m_generator;

        private int? m_appliedSeed;
        private int? m_appliedRegionIndex;
        private bool m_authoritativeSeedSelected;
        private bool m_selectingAuthoritativeState;
        private float m_nextStreetlightSync;
        private readonly List<StreetlightPose> m_streetlightPoses = new();

        private void Update()
        {
            if (!IsServerInitialized || m_generator == null || !m_generator.IsGenerationReady ||
                Time.unscaledTime < m_nextStreetlightSync) return;
            m_nextStreetlightSync = Time.unscaledTime + 0.1f;
            m_streetlightPoses.Clear();
            for (int i = 0; i < m_generator.GeneratedStreetlightCount; i++)
            {
                StreetlightPlaceholder light = m_generator.GetStreetlight(i);
                if (light == null || !light.IsKnockedDown) continue;
                m_streetlightPoses.Add(new StreetlightPose
                {
                    Index = i, Position = light.transform.position, Rotation = light.transform.rotation
                });
            }
            // Include resting lights so late joiners and clients still generating catch up.
            if (m_streetlightPoses.Count > 0)
                SyncStreetlights(m_authoritativeSeed.Value, m_authoritativeRegionIndex.Value,
                    m_streetlightPoses.ToArray());
        }

        [ObserversRpc]
        private void SyncStreetlights(int seed, int region, StreetlightPose[] poses)
        {
            if (IsServerInitialized || m_generator == null || !m_generator.IsGenerationReady ||
                m_appliedSeed != seed || m_appliedRegionIndex != region) return;
            foreach (StreetlightPose pose in poses)
                m_generator.GetStreetlight(pose.Index)?.ApplyNetworkPose(pose.Position, pose.Rotation);
        }

        public int AuthoritativeSeed => m_authoritativeSeed.Value;
        public int AuthoritativeRegionIndex => m_authoritativeRegionIndex.Value;

        private void Awake()
        {
            m_generator ??= GetComponent<NeighbourhoodGenerator>();
            m_authoritativeSeed.OnChange += OnSeedChanged;
            m_authoritativeRegionIndex.OnChange += OnRegionChanged;
        }

        private void OnDestroy()
        {
            m_authoritativeSeed.OnChange -= OnSeedChanged;
            m_authoritativeRegionIndex.OnChange -= OnRegionChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureAuthoritativeGeneration();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyState(m_authoritativeRegionIndex.Value, m_authoritativeSeed.Value);
        }

        private void OnSeedChanged(int previous, int next, bool asServer)
        {
            if (!m_selectingAuthoritativeState)
            {
                ApplyState(m_authoritativeRegionIndex.Value, next);
            }
        }

        private void OnRegionChanged(int previous, int next, bool asServer)
        {
            if (!m_selectingAuthoritativeState)
            {
                ApplyState(next, m_authoritativeSeed.Value);
            }
        }

        /// <summary>
        /// Ensures server-side terrain exists before a Player is instantiated. The first
        /// call selects the only authoritative seed; subsequent calls reuse it.
        /// </summary>
        public bool EnsureAuthoritativeGeneration()
        {
            if (!IsServerInitialized || m_generator == null)
            {
                return false;
            }

            if (!m_authoritativeSeedSelected)
            {
                m_selectingAuthoritativeState = true;
                m_authoritativeRegionIndex.Value = m_generator.SelectedRegionIndex;
                m_authoritativeSeed.Value = m_generator.SelectSeed();
                m_authoritativeSeedSelected = true;
                m_selectingAuthoritativeState = false;
            }

            ApplyState(m_authoritativeRegionIndex.Value, m_authoritativeSeed.Value);
            return m_generator.HasGeneratedNeighbourhood;
        }

        private void ApplyState(int regionIndex, int seed)
        {
            if (m_generator == null ||
                (m_appliedSeed.HasValue && m_appliedSeed.Value == seed &&
                 m_appliedRegionIndex.HasValue && m_appliedRegionIndex.Value == regionIndex))
            {
                return;
            }

            if (!m_generator.TrySelectRegion(regionIndex))
            {
                Debug.LogError($"Cannot generate synchronised region index {regionIndex}.", this);
                return;
            }

            m_appliedSeed = seed;
            m_appliedRegionIndex = regionIndex;
            if (GameLaunchContext.UseLoadingFlow)
                m_generator.GenerateAsync(seed);
            else
                m_generator.Generate(seed);
        }
    }
}
