using System;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    [Serializable]
    public sealed class SuburbsDeliveryStageDefinition
    {
        [SerializeField] private string m_stageId;
        [SerializeField] private string m_displayName;
        [SerializeField, Min(0)] private int m_reward;
        [SerializeField] private bool m_requiresVan;
        [SerializeField] private bool m_finalDelivery;

        public string StageId => m_stageId;
        public string DisplayName => m_displayName;
        public int Reward => m_reward;
        public bool RequiresVan => m_requiresVan;
        public bool FinalDelivery => m_finalDelivery;

        public SuburbsDeliveryStageDefinition(
            string stageId,
            string displayName,
            int reward,
            bool requiresVan,
            bool finalDelivery)
        {
            m_stageId = stageId;
            m_displayName = displayName;
            m_reward = Mathf.Max(0, reward);
            m_requiresVan = requiresVan;
            m_finalDelivery = finalDelivery;
        }
    }
}
