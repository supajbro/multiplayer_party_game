using System.Collections.Generic;
using CouchGuys.Gameplay.Couch;
using CouchGuys.Player;
using CouchGuys.Gameplay.Weapons;
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
        [SerializeField] private SledSettings m_settings;
        [SerializeField] private Transform[] m_weaponAnchors;
        [SerializeField, Tooltip("Half-width/depth of the couch loading area around the couch anchor.")]
        private Vector2 m_loadingHalfExtents = new(1.3f, .9f);
        [SerializeField, Min(.05f)] private float m_loadingHeightTolerance = .55f;
        [SerializeField, Min(.1f)] private float m_loadingInteractionDistance = 2.3f;
        private readonly Collider[] m_loadingHits = new Collider[64];
        private readonly List<WeaponPickup> m_weapons = new();
        private float m_nextLoadingScan;
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
        public int CargoCount => m_cargo.Count + m_weapons.Count;
        public int MaximumCargoItems => m_settings != null ? m_settings.MaximumCargoItems : 3;
        public bool HasCargoSpace => CargoCount < MaximumCargoItems;
        public string CargoStatus => $"Cargo: {CargoCount}/{MaximumCargoItems}";
        public string LoadingStatus => HasCargoSpace ? CargoStatus : $"Sled full ({CargoCount}/{MaximumCargoItems})";
        public bool CanLoadFrom(Vector3 position) => m_couchAnchor != null &&
            Vector3.Distance(position, m_couchAnchor.position) <= m_loadingInteractionDistance;

        public static SledController FindLoadingSled(Vector3 position)
        {
            SledController nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var sled in FindObjectsByType<SledController>(FindObjectsSortMode.None))
            {
                if (!sled.IsSpawned || !sled.CanLoadFrom(position)) continue;
                float candidate = (sled.CouchAnchor.position - position).sqrMagnitude;
                if (candidate >= distance) continue;
                nearest = sled;
                distance = candidate;
            }
            return nearest;
        }
        public float StaminaDrainMultiplier => CarryController.StaminaDrainMultiplier;
        public SledGrabPoint GetPoint(int index) => index >= 0 && index < m_grabPoints.Length ? m_grabPoints[index] : null;
        public bool IsPointOccupied(int index) => CarryController.IsPointOccupied(index);
        internal bool TryGrabPointServer(PlayerCouchCarrier carrier, int index) => CarryController.TryGrabPointServer(carrier, index);
        internal PlayerCouchCarrier GetServerCarrier(int index) => CarryController.GetServerCarrier(index);
        internal PlayerCouchCarrier GetFirstHumanCarrierServer(PlayerCouchCarrier except = null) => CarryController.GetFirstHumanCarrierServer(except);
        public float CalculateCarrierMovementSpeedMultiplier() => CarryController.CalculateCarrierMovementSpeedMultiplier();
        public void TeleportServer(Vector3 position, Quaternion rotation) => CarryController.TeleportServer(position, rotation);

        private void FixedUpdate()
        {
            if (!IsServerInitialized || !HasCargoSpace || Time.time < m_nextLoadingScan ||
                Time.time < m_nextMountTime || m_couchAnchor == null) return;
            m_nextLoadingScan = Time.time + .15f;
            // Contact callbacks stop being reliable once lower cargo becomes kinematic.
            Vector3 centre = m_couchAnchor.position + transform.up * StackHeight;
            int count = Physics.OverlapBoxNonAlloc(centre,
                new Vector3(m_loadingHalfExtents.x, m_loadingHeightTolerance + .5f, m_loadingHalfExtents.y),
                m_loadingHits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count && HasCargoSpace; i++)
            {
                var body = m_loadingHits[i].attachedRigidbody;
                if (body == null) continue;
                if (body.TryGetComponent(out CouchCarryController couch)) TryLoadPlacedCouchServer(couch);
                else if (body.TryGetComponent(out WeaponPickup weapon) && weapon.MountedSled == null &&
                    m_couchAnchor.InverseTransformPoint(weapon.transform.position).y >= -.15f)
                    TryStoreWeaponServer(weapon);
            }
        }

        internal void TryMountCollisionServer(Collision collision)
        {
            if (collision.rigidbody != null && collision.rigidbody.TryGetComponent(out CouchCarryController couch))
                TryLoadPlacedCouchServer(couch);
        }

        internal bool IsWithinLoadingSurface(CouchCarryController couch)
        {
            if (couch == null || m_couchAnchor == null) return false;
            Vector3 centre = m_couchAnchor.InverseTransformPoint(couch.transform.TransformPoint(couch.CargoBounds.center));
            float halfHeight = couch.CargoBounds.extents.y * couch.transform.lossyScale.y / m_couchAnchor.lossyScale.y;
            return Mathf.Abs(centre.x) <= m_loadingHalfExtents.x && Mathf.Abs(centre.z) <= m_loadingHalfExtents.y &&
                Mathf.Abs(centre.y - halfHeight - StackHeight) <= m_loadingHeightTolerance &&
                Vector3.Dot(couch.transform.up, transform.up) > .5f;
        }

        private bool TryLoadPlacedCouchServer(CouchCarryController couch) =>
            IsWithinLoadingSurface(couch) && TryLoadCouchServer(couch);

        [Server]
        internal bool TryLoadCouchServer(CouchCarryController couch)
        {
            if (!IsServerInitialized || !HasCargoSpace || Time.time < m_nextMountTime || couch == null) return false;
            return couch.MountOnSledServer(this, GetMountPosition(couch));
        }

        internal Transform GetWeaponAnchor(int slot) => m_weaponAnchors != null && slot >= 0 &&
            slot < m_weaponAnchors.Length ? m_weaponAnchors[slot] : null;

        [Server]
        internal bool TryStoreWeaponServer(WeaponPickup weapon)
        {
            if (!IsServerInitialized || !HasCargoSpace || Time.time < m_nextMountTime || weapon == null ||
                !CanLoadFrom(weapon.transform.position)) return false;
            for (int slot = 0; m_weaponAnchors != null && slot < m_weaponAnchors.Length; slot++)
            {
                if (GetWeaponAnchor(slot) == null || m_weapons.Exists(item => item != null && item.SledSlot == slot)) continue;
                return weapon.MountOnSledServer(this, slot);
            }
            return false;
        }

        internal void RegisterWeapon(WeaponPickup weapon)
        {
            if (m_weapons.Contains(weapon)) return;
            m_weapons.Add(weapon);
            IgnoreCargoCollisions(weapon.gameObject, true);
        }

        internal void ForgetWeapon(WeaponPickup weapon)
        {
            IgnoreCargoCollisions(weapon.gameObject, false);
            m_weapons.Remove(weapon);
            m_nextMountTime = Time.time + 1f;
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
            IgnoreCargoCollisions(couch.gameObject, true);
            m_cargo.Add(couch);
        }

        internal void ForgetMountedCouch(CouchCarryController couch)
        {
            IgnoreCargoCollisions(couch.gameObject, false);
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

        private void IgnoreCargoCollisions(GameObject couch, bool ignored)
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
            foreach (var weapon in m_weapons)
                if (weapon != null)
                    foreach (var hit in weapon.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(hit, carrier, ignored);
        }

        [Server]
        internal bool TryUnloadTopCouchServer(out string reason)
        {
            reason = "No couch to unload.";
            if (!IsServerInitialized || m_cargo.Count == 0) return false;
            CouchCarryController couch = m_cargo[m_cargo.Count - 1];
            if (couch == null || couch.MountedSled != this) return false;
            if (!TryGetUnloadPose(couch, out Vector3 position, out Quaternion rotation, out reason)) return false;
            // Validation is complete before any inventory or attachment state changes.
            couch.UnloadFromSledServer(position, rotation);
            return true;
        }

        private bool IsIgnoredUnloadCollider(Collider hit) => hit.transform.IsChildOf(transform) ||
            hit.GetComponentInParent<PlayerCouchCarrier>() != null;

        private bool TryGetUnloadPose(CouchCarryController couch, out Vector3 position,
            out Quaternion rotation, out string reason)
        {
            // Unload upright even when the sled is tilted, so the bounds do not
            // intersect the supporting floor on an otherwise clear placement.
            rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            position = transform.TransformPoint(m_unloadOffset);
            reason = "Move the sled near clear, level ground to unload a couch.";
            float ground = position.y;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 4f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (IsIgnoredUnloadCollider(hit.collider) ||
                    hit.normal.y < .65f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                ground = hit.point.y;
            }
            if (float.IsPositiveInfinity(nearest)) return false;
            position.y = ground - couch.CargoBounds.min.y * couch.transform.lossyScale.y + .04f;
            Vector3 centre = position + rotation * Vector3.Scale(couch.CargoBounds.center, couch.transform.lossyScale);
            reason = "Clear the space in front of the sled to unload a couch.";
            foreach (var hit in Physics.OverlapBox(centre, Vector3.Scale(couch.CargoBounds.extents, couch.transform.lossyScale) * .98f,
                         rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!IsIgnoredUnloadCollider(hit)) return false;
            reason = string.Empty;
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
            while (m_weapons.Count > 0)
            {
                var weapon = m_weapons[m_weapons.Count - 1];
                if (weapon != null) weapon.UnmountFromSledServer();
                else m_weapons.RemoveAt(m_weapons.Count - 1);
            }
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            foreach (var couch in m_cargo)
                if (couch != null) couch.transform.SetParent(null, true);
            foreach (var weapon in m_weapons)
                if (weapon != null) weapon.transform.SetParent(null, true);
            base.OnStopClient();
        }
    }
}
