using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public sealed partial class NeighbourhoodGenerator
    {
        private readonly List<Rect> m_pedestrianSidewalks = new List<Rect>();
        public IReadOnlyList<Rect> PedestrianSidewalks => m_pedestrianSidewalks;

        public Vector3 SamplePedestrianSidewalk(Vector3 position)
        {
            SetSidewalkHeight(ref position);
            return position;
        }

        private void RecordPedestrianSidewalk(Vector3 first, Vector3 second, Vector3 third, Vector3 fourth)
        {
            if (!ClipSidewalkPieceToPlayableBounds(ref first, ref second, ref third, ref fourth)) return;
            float x0 = Mathf.Min(first.x, second.x, third.x, fourth.x);
            float z0 = Mathf.Min(first.z, second.z, third.z, fourth.z);
            float x1 = Mathf.Max(first.x, second.x, third.x, fourth.x);
            float z1 = Mathf.Max(first.z, second.z, third.z, fourth.z);
            if (x1 - x0 > 0.1f && z1 - z0 > 0.1f)
                m_pedestrianSidewalks.Add(Rect.MinMaxRect(x0, z0, x1, z1));
        }

        private void EnsurePedestrians()
        {
            if (m_createDeliveryNpc && GetComponent<CouchGuys.Gameplay.Pedestrians.PedestrianManager>() == null)
                gameObject.AddComponent<CouchGuys.Gameplay.Pedestrians.PedestrianManager>();
        }
    }
}
