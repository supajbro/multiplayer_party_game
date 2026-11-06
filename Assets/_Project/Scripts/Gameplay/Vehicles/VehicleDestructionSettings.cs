using System;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    [Serializable]
    public sealed class VehicleDestructionSettings
    {
        [Min(0f), Tooltip("Upward velocity impulse in metres per second, independent of mass.")]
        public float ExplosionLaunchForce = 16f;
        [Min(0f), Tooltip("Random horizontal velocity impulse in metres per second.")]
        public float RandomHorizontalForce = 8f;
        [Min(0f), Tooltip("Angular velocity impulse in radians per second.")]
        public float RotationalTorque = 8f;
        [Min(.1f)] public float DestroyedVehicleLifetime = 10f;

        public VehicleDestructionSettings() { }
        public VehicleDestructionSettings(float launch, float horizontal, float torque)
        {
            ExplosionLaunchForce = launch;
            RandomHorizontalForce = horizontal;
            RotationalTorque = torque;
        }
    }
}
