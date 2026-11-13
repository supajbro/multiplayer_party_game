using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    [CreateAssetMenu(menuName = "Couch Guys/Vehicles/Hover Truck Settings")]
    public sealed class HoverTruckSettings : ScriptableObject
    {
        [Header("Suspension")]
        [Min(.1f)] public float HoverHeight = .55f;
        [Min(1f)] public float ProbeRange = 3f;
        [Min(0f)] public float Stability = 45f;
        [Min(0f)] public float Damping = 12f;
        [Min(10f)] public float MaximumLiftAcceleration = 55f;
        [Min(0f)] public float AngularDamping = 5f;
        public LayerMask GroundMask = ~0;

        [Header("Presentation")]
        [Range(0f, 15f)] public float Banking = 5f;
        [Min(0f)] public float BobAmplitude = .035f;
        [Min(0f)] public float BobFrequency = 1.4f;
        [Min(.05f)] public float ClampDuration = .5f;
        [Range(0f, 90f)] public float ClampOpenAngle = 32f;
    }
}
