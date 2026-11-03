using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    // Monotonic remaining-route progress, so reversing over the same ground cannot
    // continuously renew the recovery deadline. Replanning does not renew it either.
    internal sealed class GetawayRouteProgress
    {
        private float m_best = float.PositiveInfinity;
        private float m_lastProgress;
        internal void Reset(float now) { m_best = float.PositiveInfinity; m_lastProgress = now; }
        internal void Replan() => m_best = float.PositiveInfinity;
        internal void Observe(float remaining, float now, float threshold)
        {
            if (float.IsPositiveInfinity(m_best)) { m_best = remaining; return; }
            if (remaining > m_best - threshold) return;
            m_best = remaining;
            m_lastProgress = now;
        }
        internal bool TimedOut(float now, float timeout) => now - m_lastProgress >= timeout;
    }
}
