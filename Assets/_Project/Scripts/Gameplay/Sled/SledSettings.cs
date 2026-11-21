using UnityEngine;
namespace CouchGuys.Gameplay.Sled
{
    [CreateAssetMenu(menuName = "Couch Guys/Gameplay/Sled Settings", fileName = "SledCarrySettings")]
    public sealed class SledSettings : ScriptableObject
    {
        [SerializeField, Min(0.1f)] private float m_movementSpeedMultiplier = .85f;
        [SerializeField, Min(0f)] private float m_staminaDrainMultiplier = .05f;
        [SerializeField, Min(1)] private int m_maximumCargoItems = 4;
        [SerializeField, Min(.1f)] private float m_groundRecoveryHeight = 1.25f;
        [SerializeField, Min(.02f)] private float m_groundRecoveryInterval = .1f;
        public float GroundRecoveryHeight => Mathf.Max(.1f, m_groundRecoveryHeight);
        public float GroundRecoveryInterval => Mathf.Max(.02f, m_groundRecoveryInterval);
        public int MaximumCargoItems => Mathf.Max(1, m_maximumCargoItems);
        public float MovementSpeedMultiplier => Mathf.Max(.1f, m_movementSpeedMultiplier);
        public float StaminaDrainMultiplier => Mathf.Max(0f, m_staminaDrainMultiplier);
    }
}
