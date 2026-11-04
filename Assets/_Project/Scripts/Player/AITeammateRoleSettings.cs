using UnityEngine;

namespace CouchGuys.Player
{
    public enum AITeammateRole { HumanSupport, Combat, Carrier, Support }

    [CreateAssetMenu(menuName = "Couch Guys/AI Teammate Role")]
    public sealed class AITeammateRoleSettings : ScriptableObject
    {
        public AITeammateRole Role;
        [Tooltip("Shared couch clearance profile used by existing couch navigation.")]
        public CouchGuys.Gameplay.Vehicles.GetawayTruckSettings CouchNavigationSettings;
        [Min(1f)] public float CombatDetectionRange = 18f;
        [Min(1f)] public float MaximumCombatChaseDistance = 22f;
        [Min(1)] public int CarrierDamageHitThreshold = 3;
        [Min(0.1f)] public float CarrierDamageTimeWindow = 4f;
        [Tooltip("Zero disables revival; higher values win competing revival claims.")]
        [Min(0)] public int RevivalPriority = 1;
        [Min(0.25f)] public float FollowDistance = 1.75f;
        [Min(0.1f)] public float CouchInteractionDistance = 1f;
        [Min(0.1f)] public float StateTransitionCooldown = 1.5f;
        [Min(1f)] public float ReviveAttemptTimeout = 20f;
        [Min(1f)] public float ReviveRetryDelay = 10f;
    }
}
