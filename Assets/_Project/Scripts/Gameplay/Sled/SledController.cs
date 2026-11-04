using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Sled
{
    /// <summary>Cargo attachment and compatibility accessors; CouchCarryController owns all carrying.</summary>
    [RequireComponent(typeof(CouchCarryController))]
    [DisallowMultipleComponent]
    public sealed class SledController : NetworkBehaviour
    {
        public const int MaximumGrabPoints = CouchCarryController.MaximumCarryPoints;
        [SerializeField] private CouchCarryController m_carryController;
        [SerializeField] private SledGrabPoint[] m_grabPoints = new SledGrabPoint[MaximumGrabPoints];
        [SerializeField] private Transform m_couchAnchor;
        [SerializeField] private Transform m_unloadPoint;
        [SerializeField] private Vector3 m_unloadOffset = new(0f, 0f, 2.4f);
        private readonly List<CouchCarryController> m_cargo = new();
        private readonly HashSet<Collider> m_carriers = new();
        private float m_nextMountTime;

        public CouchCarryController CarryController => m_carryController != null ? m_carryController : m_carryController = GetComponent<CouchCarryController>();
        public Transform CouchAnchor => m_couchAnchor;
        public Transform UnloadPoint => m_unloadPoint;
        public Rigidbody SledRigidbody => CarryController.CouchRigidbody;
        public float MaxGrabDistance => CarryController.MaxCarryDistance;
        public int ActiveCarrierCount => CarryController.ActiveCarrierCount;
        public int MountedCouchCount => m_cargo.Count;
        public float StaminaDrainMultiplier => CarryController.StaminaDrainMultiplier;
        public SledGrabPoint GetPoint(int index) => index >= 0 && index < m_grabPoints.Length ? m_grabPoints[index] : null;
        public bool IsPointOccupied(int index) => CarryController.IsPointOccupied(index);
        internal bool TryGrabPointServer(PlayerCouchCarrier carrier, int index) => CarryController.TryGrabPointServer(carrier, index);
        internal PlayerCouchCarrier GetServerCarrier(int index) => CarryController.GetServerCarrier(index);
        internal PlayerCouchCarrier GetFirstHumanCarrierServer(PlayerCouchCarrier except = null) => CarryController.GetFirstHumanCarrierServer(except);
        public float CalculateCarrierMovementSpeedMultiplier() => CarryController.CalculateCarrierMovementSpeedMultiplier();
        public void TeleportServer(Vector3 position, Quaternion rotation) => CarryController.TeleportServer(position, rotation);

        private void OnCollisionStay(Collision collision) => TryMountCollisionServer(collision);

        internal void TryMountCollisionServer(Collision collision)
        {
            if (!IsServerInitialized || m_couchAnchor == null || Time.time < m_nextMountTime || collision.rigidbody == null) return;
            CouchCarryController couch = collision.rigidbody.GetComponent<CouchCarryController>();
            if (couch == null || couch.IsSled || !couch.IsSpawned || couch.IsLoaded || !couch.IsPickupAvailable || couch.ActiveCarrierCount > 0) return;
            Vector3 centre = transform.InverseTransformPoint(couch.transform.TransformPoint(couch.CargoBounds.center));
            if (Mathf.Abs(centre.x) > .45f || Mathf.Abs(centre.z) > .35f || Vector3.Dot(couch.transform.up, transform.up) < .9f) return;
            float top = m_couchAnchor.localPosition.y + StackHeight;
            foreach (ContactPoint contact in collision.contacts)
            {
                if (Mathf.Abs(transform.InverseTransformPoint(contact.point).y - top) > .12f) continue;
                couch.MountOnSledServer(this, GetMountPosition(couch));
                break;
            }
        }

        private float StackHeight
        {
            get
            {
                float height = 0f;
                foreach (var couch in m_cargo)
                    if (couch != null) height += couch.CargoBounds.size.y * couch.transform.localScale.y;
                return height;
            }
        }

        internal Vector3 GetMountPosition(CouchCarryController couch) =>
            Vector3.up * StackHeight + Vector3.Scale(new Vector3(-couch.CargoBounds.center.x,
                -couch.CargoBounds.min.y, -couch.CargoBounds.center.z), couch.transform.localScale);

        // Called on every peer from the couch's replicated attachment state.
        internal void RegisterMountedCouch(CouchCarryController couch)
        {
            if (m_cargo.Contains(couch)) return;
            IgnoreCargoCollisions(couch, true);
            m_cargo.Add(couch);
        }

        internal void ForgetMountedCouch(CouchCarryController couch)
        {
            IgnoreCargoCollisions(couch, false);
            m_cargo.Remove(couch);
            m_nextMountTime = Time.time + 1f;
            if (!IsServerInitialized) return;
            float height = 0f;
            foreach (var remaining in m_cargo)
            {
                if (remaining == null) continue;
                remaining.SetSledMountPositionServer(Vector3.up * height + Vector3.Scale(
                    new Vector3(-remaining.CargoBounds.center.x, -remaining.CargoBounds.min.y, -remaining.CargoBounds.center.z), remaining.transform.localScale));
                height += remaining.CargoBounds.size.y * remaining.transform.localScale.y;
            }
        }

        private void IgnoreCargoCollisions(CouchCarryController couch, bool ignored)
        {
            Collider[] cargoColliders = couch.GetComponentsInChildren<Collider>(true);
            Collider[] assemblyColliders = GetComponentsInChildren<Collider>(true);
            foreach (var own in cargoColliders)
            {
                foreach (var other in assemblyColliders)
                    if (other != own && !other.transform.IsChildOf(couch.transform)) Physics.IgnoreCollision(own, other, ignored);
                foreach (var carrier in m_carriers)
                    if (carrier != null) Physics.IgnoreCollision(own, carrier, ignored);
            }
        }

        internal void SetCargoCarrierCollisionIgnored(Collider carrier, bool ignored)
        {
            if (ignored) m_carriers.Add(carrier); else m_carriers.Remove(carrier);
            foreach (var couch in m_cargo)
                if (couch != null) couch.SetCarrierCollisionIgnored(carrier, ignored);
        }

        [Server]
        internal bool TryUnloadTopCouchServer()
        {
            if (!IsServerInitialized || m_cargo.Count == 0) return false;
            CouchCarryController couch = m_cargo[m_cargo.Count - 1];
            if (couch == null) return false;
            Vector3 position = transform.TransformPoint(m_unloadOffset);
            float ground = position.y;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 4f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<PlayerCouchCarrier>() != null ||
                    hit.normal.y < .65f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                ground = hit.point.y;
            }
            if (float.IsPositiveInfinity(nearest)) return false;
            position.y = ground - couch.CargoBounds.min.y * couch.transform.lossyScale.y + .04f;
            Vector3 centre = position + transform.rotation * Vector3.Scale(couch.CargoBounds.center, couch.transform.lossyScale);
            foreach (var hit in Physics.OverlapBox(centre, Vector3.Scale(couch.CargoBounds.extents, couch.transform.lossyScale) * .98f,
                         transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform)) return false;
            couch.UnloadFromSledServer(position, transform.rotation);
            return true;
        }

        public override void OnStopServer()
        {
            // Remove from the top so remaining cargo never gets reordered while despawning.
            while (m_cargo.Count > 0)
            {
                var couch = m_cargo[m_cargo.Count - 1];
                if (couch != null) couch.UnmountFromSledServer();
                else m_cargo.RemoveAt(m_cargo.Count - 1);
            }
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            foreach (var couch in m_cargo)
                if (couch != null) couch.transform.SetParent(null, true);
            base.OnStopClient();
        }
    }
}
