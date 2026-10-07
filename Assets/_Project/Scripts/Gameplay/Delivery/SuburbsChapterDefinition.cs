using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Ordered, tunable delivery milestones for the Suburbs chapter.</summary>
    [CreateAssetMenu(
        menuName = "Couch Guys/Gameplay/Suburbs Chapter Definition",
        fileName = "SuburbsChapterDefinition")]
    public sealed class SuburbsChapterDefinition : ScriptableObject
    {
        public const int RequiredStageCount = 7;

        [SerializeField] private SuburbsDeliveryStageDefinition[] m_stages = CreateDefaultStages();

        public int StageCount => m_stages?.Length ?? 0;

        public SuburbsDeliveryStageDefinition GetStage(int index)
        {
            return m_stages != null && index >= 0 && index < m_stages.Length
                ? m_stages[index]
                : null;
        }

        public void ResetToDefaults()
        {
            m_stages = CreateDefaultStages();
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
