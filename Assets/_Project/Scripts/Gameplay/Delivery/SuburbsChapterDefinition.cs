using System;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Ordered, tunable delivery milestones for the Suburbs chapter.</summary>
    [CreateAssetMenu(
        menuName = "Couch Guys/Gameplay/Suburbs Chapter Definition",
        fileName = "SuburbsChapterDefinition")]
    public sealed class SuburbsChapterDefinition : ScriptableObject
    {
        [Serializable]
        public struct EnemySpawnRange
        {
            [SerializeField, Min(1)] private int m_minimum;
            [SerializeField, Min(1)] private int m_maximum;

            public int Minimum => Mathf.Max(1, m_minimum);
            public int Maximum => Mathf.Max(Minimum, m_maximum);

            public EnemySpawnRange(int minimum, int maximum)
            {
                m_minimum = Mathf.Max(1, minimum);
                m_maximum = Mathf.Max(m_minimum, maximum);
            }
        }

        public const int RequiredStageCount = 7;

        [SerializeField] private SuburbsDeliveryStageDefinition[] m_stages = CreateDefaultStages();
        [Header("Enemy Spawn Counts by Delivery Difficulty")]
        [SerializeField] private EnemySpawnRange m_easyEnemySpawnRange = new(1, 2);
        [SerializeField] private EnemySpawnRange m_mediumEnemySpawnRange = new(2, 4);
        [SerializeField] private EnemySpawnRange m_hardEnemySpawnRange = new(4, 4);

        public int StageCount => m_stages?.Length ?? 0;

        public SuburbsDeliveryStageDefinition GetStage(int index)
        {
            return m_stages != null && index >= 0 && index < m_stages.Length
                ? m_stages[index]
                : null;
        }

        public EnemySpawnRange GetEnemySpawnRange(int deliveryTierIndex)
        {
            return deliveryTierIndex switch
            {
                0 => m_easyEnemySpawnRange,
                1 => m_mediumEnemySpawnRange,
                2 => m_hardEnemySpawnRange,
                _ => m_easyEnemySpawnRange
            };
        }

        public void ResetToDefaults()
        {
            m_stages = CreateDefaultStages();
            m_easyEnemySpawnRange = new EnemySpawnRange(1, 2);
            m_mediumEnemySpawnRange = new EnemySpawnRange(2, 4);
            m_hardEnemySpawnRange = new EnemySpawnRange(4, 4);
        }

        private static SuburbsDeliveryStageDefinition[] CreateDefaultStages()
        {
            return new[]
            {
                new SuburbsDeliveryStageDefinition(
                    "suburbs_01_first_delivery", "Just Around the Corner", 100, false, false),
                new SuburbsDeliveryStageDefinition(
                    "suburbs_02_proper_carrying", "This Looked Closer on the Map", 125, false, false),
                new SuburbsDeliveryStageDefinition(
                    "suburbs_03_van_fund", "Definitely a Safe Neighbourhood", 175, false, false),
                new SuburbsDeliveryStageDefinition(
                    "suburbs_04_van_tutorial", "Van With a Plan", 200, true, false),
                new SuburbsDeliveryStageDefinition(
                    "suburbs_05_hill_start", "Hill Start", 250, true, false),
                new SuburbsDeliveryStageDefinition(
                    "suburbs_06_scenic_route", "The Scenic Route", 300, true, false),
                new SuburbsDeliveryStageDefinition(
                    "suburbs_07_final", "The House on Absolutely the Wrong Hill", 500, true, true)
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (m_stages == null || m_stages.Length != RequiredStageCount)
            {
                Debug.LogWarning(
                    $"The Suburbs chapter requires exactly {RequiredStageCount} delivery stages.",
                    this);
            }
        }
#endif
    }
}
