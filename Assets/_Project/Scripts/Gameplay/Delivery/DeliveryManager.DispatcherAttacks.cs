using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    public struct DispatcherAttackState
    {
        public bool Valid;
        public int Seed, Region;
        public uint AngryUntilTick, LineUntilTick, LineRevision;
        public string Line;
    }

    public sealed partial class DeliveryManager
    {
        // State (rather than transient RPCs) also supports late joiners and clients still generating.
        private readonly SyncVar<DispatcherAttackState> m_dispatcherAttack = new();
        private uint m_aggressiveLineRevision;

        [Server]
        internal void PublishDispatcherAttackServer(float duration, string line)
        {
            if (m_generator == null) return;
            var state = m_dispatcherAttack.Value;
            if (state.Seed != m_generator.CurrentSeed || state.Region != m_generator.SelectedRegionIndex)
                state = default;
            state.Valid = true;
            state.Seed = m_generator.CurrentSeed;
            state.Region = m_generator.SelectedRegionIndex;
            state.AngryUntilTick = TimeManager.Tick + TimeManager.TimeToTicks(Mathf.Max(.1f, duration));
            if (!string.IsNullOrWhiteSpace(line))
            {
                state.Line = line;
                state.LineRevision = ++m_aggressiveLineRevision;
                state.LineUntilTick = state.AngryUntilTick;
            }
            m_dispatcherAttack.Value = state;
        }

        internal void GetDispatcherAttackPresentation(out bool angry, out uint revision,
            out string line, out float remaining)
        {
            angry = false; revision = 0; line = null; remaining = 0f;
            var state = m_dispatcherAttack.Value;
            if (!state.Valid || m_generator == null || !m_generator.IsGenerationReady ||
                state.Seed != m_generator.CurrentSeed || state.Region != m_generator.SelectedRegionIndex) return;
            uint now = TimeManager.Tick;
            angry = now < state.AngryUntilTick;
            revision = state.LineRevision;
            line = state.Line;
            remaining = now < state.LineUntilTick ? (float)TimeManager.TicksToTime(state.LineUntilTick - now) : 0f;
        }
    }
}
