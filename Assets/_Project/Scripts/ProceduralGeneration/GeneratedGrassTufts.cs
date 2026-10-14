using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    /// <summary>Diagnostics for the single batched, non-colliding decorative grass mesh.</summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedGrassTufts : MonoBehaviour
    {
        [SerializeField] private int m_tuftCount;
        [SerializeField] private int m_rejectedCandidateCount;

        public int TuftCount => m_tuftCount;
        public int RejectedCandidateCount => m_rejectedCandidateCount;

        public void Initialise(int tuftCount, int rejectedCandidateCount)
        {
            m_tuftCount = Mathf.Max(0, tuftCount);
            m_rejectedCandidateCount = Mathf.Max(0, rejectedCandidateCount);
        }
    }
}
