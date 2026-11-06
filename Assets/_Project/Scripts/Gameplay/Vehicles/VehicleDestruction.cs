using CouchGuys.Gameplay.Weapons;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Vehicles
{
    /// <summary>Server simulates the wreck; the existing NetworkTransform replicates its pose.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
    public sealed class VehicleDestruction : NetworkBehaviour
    {
        [SerializeField] private RocketExplosionVfx m_explosion;
        [SerializeField, Min(.1f)] private float m_explosionRadius = 5f;
        private bool m_started;
        private float m_despawnAt;

        public override void OnStartServer()
        {
            base.OnStartServer();
            m_started = false;
        }

        // Controllers must mark themselves destroyed and release cargo/occupants first.
        [Server]
        public void DestroyServer(VehicleDestructionSettings settings)
        {
            if (!IsServerInitialized || m_started) return;
            m_started = true;
            var obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (obstacle != null) obstacle.enabled = false;
            m_despawnAt = Time.time + Mathf.Max(.1f, settings.DestroyedVehicleLifetime);
            Vector2 horizontal = Random.insideUnitCircle;
            if (horizontal.sqrMagnitude < .001f) horizontal = Vector2.right;
            horizontal = horizontal.normalized * Mathf.Max(0f, settings.RandomHorizontalForce);
            Vector3 spin = Random.onUnitSphere;
            // Ensure a tumble rather than a nearly pure yaw spin.
            spin.y *= .35f;
            Launch(GetComponent<Rigidbody>(), settings,
                new Vector3(horizontal.x, 0f, horizontal.y), spin.normalized);
            ExplodeObserversRpc(transform.position);
        }

        internal static void Launch(Rigidbody body, VehicleDestructionSettings settings,
            Vector3 horizontal, Vector3 spin)
        {
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.None;
            body.useGravity = true;
            body.detectCollisions = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = .05f;
            body.angularDamping = .1f;
            body.maxAngularVelocity = Mathf.Max(body.maxAngularVelocity, settings.RotationalTorque);
            body.WakeUp();
            body.AddForce(Vector3.up * Mathf.Max(0f, settings.ExplosionLaunchForce) + horizontal,
                ForceMode.VelocityChange);
            body.AddTorque(spin * Mathf.Max(0f, settings.RotationalTorque), ForceMode.VelocityChange);
        }

        [ObserversRpc]
        private void ExplodeObserversRpc(Vector3 position)
        {
            GetComponent<VehicleAudioEmitter>()?.StopLocal();
            if (m_explosion != null)
                RocketExplosionVfx.Play(m_explosion, position + Vector3.up, position, m_explosionRadius);
        }

        private void Update()
        {
            if (IsServerInitialized && m_started && Time.time >= m_despawnAt && IsSpawned)
                Despawn(NetworkObject, DespawnType.Destroy);
        }
    }
}
