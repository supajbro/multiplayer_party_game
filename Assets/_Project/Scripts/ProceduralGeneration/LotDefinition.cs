using UnityEngine;

namespace CouchGuys.ProceduralGeneration
{
    public enum LotType
    {
        Residential,
        Commercial,
        Landmark,
        Utility,
        Empty,
        Special
    }

    [CreateAssetMenu(menuName = "Couch Guys/World/Lot Definition", fileName = "Lot_")]
    public sealed class LotDefinition : ScriptableObject
    {
        [SerializeField] private GameObject m_prefab;
        [SerializeField] private LotType m_lotType = LotType.Residential;
        [SerializeField] private Vector2Int m_minimumFootprint = Vector2Int.one;
        [SerializeField, Range(0f, 90f)] private float m_maximumSlope = 25f;
        [SerializeField] private bool m_requiresRoadAccess = true;
        [SerializeField] private bool m_uniquePerMap;

        [Header("Delivery")]
        [SerializeField] private bool m_canBeDeliveryDestination = true;
        [SerializeField] private string m_destinationDisplayName = "House";
        [SerializeField, Min(0.01f)] private float m_deliveryDifficultyModifier = 1f;
        [SerializeField, Min(0.01f)] private float m_deliveryRewardModifier = 1f;

        public GameObject Prefab => m_prefab;
        public LotType LotType => m_lotType;
        public Vector2Int MinimumFootprint => m_minimumFootprint;
        public float MaximumSlope => m_maximumSlope;
        public bool RequiresRoadAccess => m_requiresRoadAccess;
        public bool UniquePerMap => m_uniquePerMap;
        public bool CanBeDeliveryDestination => m_canBeDeliveryDestination;
        public string DestinationDisplayName => m_destinationDisplayName;
        public float DeliveryDifficultyModifier => m_deliveryDifficultyModifier;
        public float DeliveryRewardModifier => m_deliveryRewardModifier;

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_minimumFootprint.x = Mathf.Max(1, m_minimumFootprint.x);
            m_minimumFootprint.y = Mathf.Max(1, m_minimumFootprint.y);
            m_maximumSlope = Mathf.Clamp(m_maximumSlope, 0f, 90f);
            m_deliveryDifficultyModifier = Mathf.Max(0.01f, m_deliveryDifficultyModifier);
            m_deliveryRewardModifier = Mathf.Max(0.01f, m_deliveryRewardModifier);
        }
#endif
    }
}
