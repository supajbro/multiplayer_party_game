using UnityEngine;

namespace CouchGuys.Gameplay.Couch
{
    /// <summary>Designer-facing limits for the couch carrying system.</summary>
    [CreateAssetMenu(
        menuName = "Couch Guys/Gameplay/Couch Carry Settings",
        fileName = "CouchCarrySettings")]
    public sealed class CouchCarrySettings : ScriptableObject
    {
        [SerializeField, Min(0.1f)] private float m_maxCarryDistance = 1f;
        [SerializeField, InspectorName("1 Player Move Speed"), Min(0.1f)]
        private float m_onePlayerMoveSpeed = 2.5f;
        [SerializeField, InspectorName("2 Player Move Speed"), Min(0.1f)]
        private float m_twoPlayerMoveSpeed = 3.5f;
        [SerializeField, InspectorName("3 Player Move Speed"), Min(0.1f)]
        private float m_threePlayerMoveSpeed = 4.5f;
        [SerializeField, InspectorName("4 Player Move Speed"), Min(0.1f)]
        private float m_fourPlayerMoveSpeed = 5.5f;

        public float MaxCarryDistance => m_maxCarryDistance;

        public float GetMoveSpeed(int carrierCount)
        {
            return Mathf.Clamp(carrierCount, 1, CouchCarryController.MaximumCarryPoints) switch
            {
                1 => m_onePlayerMoveSpeed,
                2 => m_twoPlayerMoveSpeed,
                3 => m_threePlayerMoveSpeed,
                _ => m_fourPlayerMoveSpeed
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_maxCarryDistance = Mathf.Max(0.1f, m_maxCarryDistance);
            m_onePlayerMoveSpeed = Mathf.Max(0.1f, m_onePlayerMoveSpeed);
            m_twoPlayerMoveSpeed = Mathf.Max(0.1f, m_twoPlayerMoveSpeed);
            m_threePlayerMoveSpeed = Mathf.Max(0.1f, m_threePlayerMoveSpeed);
            m_fourPlayerMoveSpeed = Mathf.Max(0.1f, m_fourPlayerMoveSpeed);
        }
#endif
    }
}
