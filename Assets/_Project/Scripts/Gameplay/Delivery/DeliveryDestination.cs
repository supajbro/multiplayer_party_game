using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    public enum DeliveryDestinationType
    {
        Standard,
        Commercial,
        Landmark,
        Special
    }

    [DisallowMultipleComponent]
    public sealed class DeliveryDestination : MonoBehaviour
    {
        [SerializeField] private Transform m_dropPosition;
        [SerializeField] private DeliveryDestinationType m_destinationType;
        [SerializeField] private string m_displayName = "House";
        [SerializeField, Min(0.01f)] private float m_difficultyModifier = 1f;
        [SerializeField, Min(0.01f)] private float m_rewardModifier = 1f;
        [SerializeField] private bool m_canReceiveDelivery = true;

        public Transform DropPosition => m_dropPosition;
        public DeliveryDestinationType DestinationType => m_destinationType;
        public string DisplayName => m_displayName;
        public float DifficultyModifier => m_difficultyModifier;
        public float RewardModifier => m_rewardModifier;
        public bool CanReceiveDelivery => m_canReceiveDelivery && m_dropPosition != null;

        public void Initialise(
            Transform dropPosition,
            DeliveryDestinationType destinationType,
            string displayName,
            float difficultyModifier,
            float rewardModifier,
            bool canReceiveDelivery)
        {
            m_dropPosition = dropPosition;
            m_destinationType = destinationType;
            m_displayName = string.IsNullOrWhiteSpace(displayName) ? "Destination" : displayName;
            m_difficultyModifier = Mathf.Max(0.01f, difficultyModifier);
            m_rewardModifier = Mathf.Max(0.01f, rewardModifier);
            m_canReceiveDelivery = canReceiveDelivery;
        }
    }
}
