using System.Collections.Generic;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    public readonly struct TrafficObserver
    {
        public readonly Vector3 Position;
        public readonly float Speed;

        public TrafficObserver(Vector3 position, float speed)
        {
            Position = position;
            Speed = Mathf.Max(0f, speed);
        }
    }

    /// <summary>Server decisions use every player's world position, independent of cameras.</summary>
    public static class TrafficSimulationPolicy
    {
        public static bool NeedsFullPhysics(Vector3 position, float speed, bool currentlyDistant,
            IReadOnlyList<TrafficObserver> observers, TrafficSettings settings)
        {
            // Keep physics until the manager has a reliable player snapshot.
            if (observers.Count == 0) return true;
            float radius = currentlyDistant ? settings.FullPhysicsDistance : settings.FullPhysicsReleaseDistance;
            for (int i = 0; i < observers.Count; i++)
            {
                var observer = observers[i];
                float bufferedRadius = radius + (Mathf.Max(0f, speed) + observer.Speed) *
                    (settings.ApproachBufferSeconds + settings.SimulationCheckInterval + settings.DistantUpdateInterval);
                if ((observer.Position - position).sqrMagnitude <= bufferedRadius * bufferedRadius) return true;
            }
            return false;
        }

        public static bool CanDespawn(Vector3 position, float speed,
            IReadOnlyList<TrafficObserver> observers, TrafficSettings settings)
        {
            for (int i = 0; i < observers.Count; i++)
            {
                var observer = observers[i];
                float radius = settings.DespawnDistance + (Mathf.Max(0f, speed) + observer.Speed) *
                    (settings.ApproachBufferSeconds + settings.SimulationCheckInterval);
                if ((observer.Position - position).sqrMagnitude <= radius * radius) return false;
            }
            return true;
        }
    }
}
