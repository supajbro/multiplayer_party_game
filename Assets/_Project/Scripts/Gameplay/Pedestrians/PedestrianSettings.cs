using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Pedestrians
{
    [CreateAssetMenu(menuName = "Couch Guys/Pedestrians/Settings")]
    public sealed class PedestrianSettings : ScriptableObject
    {
        [Min(0)] public int Population = 32;
        [Min(1f)] public float MaximumHealth = 50f;
        [Min(0.1f)] public float WalkingSpeed = 1.35f;
        [Min(0.1f)] public float RunningSpeed = 4.5f;
        [Min(0f)] public float GunshotDetectionRadius = 30f;
        [Min(0.1f)] public float FrightenedDuration = 7f;
        [Min(0.1f)] public float CorpseDuration = 12f;
        [Min(0.1f)] public float LethalVehicleSpeed = 3f;
        [Min(0.05f)] public float ThinkInterval = 0.15f;
        [Min(0f)] public float MinimumPlayerSpawnDistance = 18f;
        [Min(1f)] public float ActivationDistance = 130f;
        [Min(1f)] public float DespawnDistance = 180f;
        public NetworkObject[] Characters = System.Array.Empty<NetworkObject>();
    }
}
