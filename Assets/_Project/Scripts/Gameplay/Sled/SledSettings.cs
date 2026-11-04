using UnityEngine;
namespace CouchGuys.Gameplay.Sled
{
    [CreateAssetMenu(menuName = "Couch Guys/Gameplay/Sled Settings", fileName = "SledCarrySettings")]
    public sealed class SledSettings : ScriptableObject
    {
        [SerializeField, Min(0.1f)] private float m_movementSpeedMultiplier = 2f;
        [SerializeField, Min(0f)] private float m_staminaDrainMultiplier = .35f;
        public float MovementSpeedMultiplier => Mathf.Max(.1f, m_movementSpeedMultiplier);
        public float StaminaDrainMultiplier => Mathf.Max(0f, m_staminaDrainMultiplier);
    }
}
