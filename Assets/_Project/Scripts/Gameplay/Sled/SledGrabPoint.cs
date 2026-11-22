using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.Gameplay.Sled
{
    /// <summary>One physical, single-occupant grip on a sled.</summary>
    [DisallowMultipleComponent]
    public sealed class SledGrabPoint : MonoBehaviour
    {
        internal static readonly HashSet<SledGrabPoint> ActivePoints = new();
        private void OnEnable() => ActivePoints.Add(this);
        private void OnDisable() => ActivePoints.Remove(this);

        [SerializeField, Range(0, SledController.MaximumGrabPoints - 1)]
        private int m_pointIndex;
        private SledController m_sled;

        public int PointIndex => m_pointIndex;
        public SledController Sled => m_sled != null ? m_sled : m_sled = GetComponentInParent<SledController>();
        public bool IsAvailable => Sled != null && !Sled.IsPointOccupied(m_pointIndex);
    }
}
