using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Delivery
{
    /// <summary>Server-authoritative progression for the seven-delivery Suburbs chapter.</summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(DeliveryManager))]
    [DisallowMultipleComponent]
    public sealed class SuburbsChapterManager : NetworkBehaviour
    {
        [SerializeField] private SuburbsChapterDefinition m_definition;
        [SerializeField] private DeliveryManager m_deliveryManager;

        private readonly SyncVar<int> m_currentStageIndex = new();
        private readonly SyncVar<int> m_completedDeliveryCount = new();
        private readonly SyncVar<bool> m_vanUnlocked = new();
        private readonly SyncVar<bool> m_chapterCompleted = new();

        public event Action<int> StageChanged;
        public event Action<int> CompletedDeliveryCountChanged;
        public event Action<bool> VanUnlockedChanged;
        public event Action<bool> ChapterCompletedChanged;

        public int CurrentStageIndex => m_currentStageIndex.Value;
        public int CompletedDeliveryCount => m_completedDeliveryCount.Value;
        public bool VanUnlocked => m_vanUnlocked.Value;
        public bool ChapterCompleted => m_chapterCompleted.Value;
        public SuburbsChapterDefinition Definition => m_definition;
        public int StageCount => m_definition != null
            ? m_definition.StageCount
            : SuburbsChapterDefinition.RequiredStageCount;
        public SuburbsDeliveryStageDefinition CurrentStage => GetStage(m_currentStageIndex.Value);

        private void Awake()
        {
            m_deliveryManager ??= GetComponent<DeliveryManager>();
            m_currentStageIndex.OnChange += OnStageChanged;
            m_completedDeliveryCount.OnChange += OnCompletedDeliveryCountChanged;
            m_vanUnlocked.OnChange += OnVanUnlockedChanged;
            m_chapterCompleted.OnChange += OnChapterCompletedChanged;
        }

        private void OnDestroy()
        {
            m_currentStageIndex.OnChange -= OnStageChanged;
            m_completedDeliveryCount.OnChange -= OnCompletedDeliveryCountChanged;
            m_vanUnlocked.OnChange -= OnVanUnlockedChanged;
            m_chapterCompleted.OnChange -= OnChapterCompletedChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_currentStageIndex.Value = 0;
            m_completedDeliveryCount.Value = 0;
            m_vanUnlocked.Value = false;
            m_chapterCompleted.Value = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            StageChanged?.Invoke(m_currentStageIndex.Value);
            CompletedDeliveryCountChanged?.Invoke(m_completedDeliveryCount.Value);
            VanUnlockedChanged?.Invoke(m_vanUnlocked.Value);
            ChapterCompletedChanged?.Invoke(m_chapterCompleted.Value);
        }

        public void SetDefinition(SuburbsChapterDefinition definition)
        {
            m_definition = definition;
        }

        [Server]
        public bool CanStartCurrentStageServer()
        {
            return !m_chapterCompleted.Value && GetStage(m_currentStageIndex.Value) != null;
        }

        /// <summary>Commits one completed stage and returns its configured base reward.</summary>
        [Server]
        public int CompleteCurrentStageServer(int completedStageIndex)
        {
            if (m_chapterCompleted.Value || completedStageIndex != m_currentStageIndex.Value)
            {
                return 0;
            }

            SuburbsDeliveryStageDefinition completedStage = GetStage(completedStageIndex);
            if (completedStage == null)
            {
                return 0;
            }

            m_completedDeliveryCount.Value++;
            if (completedStageIndex >= 2)
            {
                m_vanUnlocked.Value = true;
            }

            bool isFinalStage = completedStage.FinalDelivery ||
                                completedStageIndex >= StageCount - 1;
            if (isFinalStage)
            {
                m_chapterCompleted.Value = true;
            }
            else
            {
                m_currentStageIndex.Value++;
            }

            return completedStage.Reward;
        }

        public SuburbsDeliveryStageDefinition GetStage(int index)
        {
            SuburbsDeliveryStageDefinition configuredStage = m_definition?.GetStage(index);
            if (configuredStage != null)
            {
                return configuredStage;
            }

            // The generated asset is the normal path. This runtime fallback prevents a
            // missing editor-created asset from breaking the core delivery loop.
            return index switch
            {
                0 => new SuburbsDeliveryStageDefinition(
                    "suburbs_01_first_delivery", "Just Around the Corner", 100, false, false),
                1 => new SuburbsDeliveryStageDefinition(
                    "suburbs_02_proper_carrying", "This Looked Closer on the Map", 125, false, false),
                2 => new SuburbsDeliveryStageDefinition(
                    "suburbs_03_van_fund", "Definitely a Safe Neighbourhood", 175, false, false),
                3 => new SuburbsDeliveryStageDefinition(
                    "suburbs_04_van_tutorial", "Van With a Plan", 200, true, false),
                4 => new SuburbsDeliveryStageDefinition(
                    "suburbs_05_hill_start", "Hill Start", 250, true, false),
                5 => new SuburbsDeliveryStageDefinition(
                    "suburbs_06_scenic_route", "The Scenic Route", 300, true, false),
                6 => new SuburbsDeliveryStageDefinition(
                    "suburbs_07_final", "The House on Absolutely the Wrong Hill", 500, true, true),
                _ => null
            };
        }

        private void OnStageChanged(int previous, int next, bool asServer) =>
            StageChanged?.Invoke(next);

        private void OnCompletedDeliveryCountChanged(int previous, int next, bool asServer) =>
            CompletedDeliveryCountChanged?.Invoke(next);

        private void OnVanUnlockedChanged(bool previous, bool next, bool asServer) =>
            VanUnlockedChanged?.Invoke(next);

        private void OnChapterCompletedChanged(bool previous, bool next, bool asServer) =>
            ChapterCompletedChanged?.Invoke(next);
    }
}
