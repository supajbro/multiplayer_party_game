using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Vehicles;
using UnityEngine;

namespace CouchGuys.Gameplay.Traffic
{
    public sealed partial class TrafficVehicleController
    {
        private readonly List<Collider> m_trafficContactColliders = new();
        private readonly List<Collider> m_playerVehicleColliders = new();

        // Rogue cars share this controller, but retain their combat collisions.
        internal bool IsOrdinaryTraffic => m_rogueSettings == null && GetComponent<RogueCar>() == null;

        private void TickPlayerVehicleContacts()
        {
            if (!IsOrdinaryTraffic) return;
            GetComponentsInChildren(false, m_trafficContactColliders);
            foreach (TruckController truck in TruckController.ActiveTrucks)
            {
                if (truck == null || truck.IsGetawayTruck) continue;
                CheckPlayerVehicleContact(truck.GetComponent<Rigidbody>());
            }
            foreach (MagneticHoverboardController board in MagneticHoverboardController.ActiveBoards)
            {
                if (board != null) CheckPlayerVehicleContact(board.GetComponent<Rigidbody>());
            }
        }

        private void CheckPlayerVehicleContact(Rigidbody vehicle)
        {
            if (vehicle == null) return;
            vehicle.GetComponentsInChildren(false, m_playerVehicleColliders);
            bool touching = IgnoreAndCheckContact(m_rigidbody, vehicle, m_trafficContactColliders,
                m_playerVehicleColliders, Time.fixedDeltaTime,
                m_isDistantSimulation ? m_distantVelocity : m_rigidbody.linearVelocity);
            if (!touching || !IsServerInitialized || IsDestroyed || m_settings == null) return;
            m_health.Value = 0f;
            // The wreck/VFX still launch normally. This contact must not damage or
            // knock down seated players through the explosion's character damage.
            DestroyVehicleServer(false);
        }

        internal static bool IgnoreAndCheckContact(Rigidbody traffic, Rigidbody vehicle,
            List<Collider> trafficColliders, List<Collider> vehicleColliders, float dt, Vector3 trafficVelocity)
        {
            bool touching = false;
            Vector3 travel = (trafficVelocity - (vehicle.isKinematic ? Vector3.zero : vehicle.linearVelocity)) * dt;
            foreach (Collider carCollider in trafficColliders)
            {
                if (carCollider == null || carCollider.isTrigger || !carCollider.enabled ||
                    carCollider.attachedRigidbody != traffic) continue;
                foreach (Collider vehicleCollider in vehicleColliders)
                {
                    if (vehicleCollider == null || vehicleCollider.isTrigger || !vehicleCollider.enabled ||
                        vehicleCollider.attachedRigidbody != vehicle) continue;
                    // Do this before simulation, including for wrecks. Collision
                    // callbacks run after the solver has already imparted an impulse.
                    if (!Physics.GetIgnoreCollision(carCollider, vehicleCollider))
                        Physics.IgnoreCollision(carCollider, vehicleCollider);
                    if (carCollider.bounds.Intersects(vehicleCollider.bounds) &&
                        Physics.ComputePenetration(carCollider, carCollider.transform.position,
                        carCollider.transform.rotation, vehicleCollider, vehicleCollider.transform.position,
                        vehicleCollider.transform.rotation, out _, out _)) touching = true;
                }

                if (touching || travel.sqrMagnitude < .000001f) continue;
                Bounds sweptBounds = carCollider.bounds;
                sweptBounds.Encapsulate(new Bounds(sweptBounds.center + travel, sweptBounds.size));
                bool nearby = false;
                foreach (Collider target in vehicleColliders)
                    if (target != null && target.enabled && !target.isTrigger &&
                        target.attachedRigidbody == vehicle && sweptBounds.Intersects(target.bounds))
                    { nearby = true; break; }
                if (!nearby) continue;
                // Queries still see ignored collider pairs. Relative motion also
                // catches a moving player vehicle hitting a stationary traffic car.
                RaycastHit[] hits;
                Vector3 scale = carCollider.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (carCollider is BoxCollider box)
                    hits = Physics.BoxCastAll(box.transform.TransformPoint(box.center),
                        Vector3.Scale(box.size * .5f, scale), travel.normalized,
                        box.transform.rotation, travel.magnitude, ~0, QueryTriggerInteraction.Ignore);
                else if (carCollider is SphereCollider sphere)
                    hits = Physics.SphereCastAll(sphere.transform.TransformPoint(sphere.center),
                        sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)),
                        travel.normalized, travel.magnitude, ~0, QueryTriggerInteraction.Ignore);
                else continue;
                foreach (RaycastHit hit in hits)
                {
                    if (hit.rigidbody == vehicle && vehicleColliders.Contains(hit.collider))
                    { touching = true; break; }
                }
            }
            return touching;
        }
    }
}
