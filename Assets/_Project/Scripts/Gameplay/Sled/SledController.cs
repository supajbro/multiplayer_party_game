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
        public static readonly HashSet<SledController> ActiveSleds = new();
        private void OnEnable() => ActiveSleds.Add(this);
        private void OnDisable() => ActiveSleds.Remove(this);
        public const int MaximumGrabPoints = CouchCarryController.MaximumCarryPoints;
        [SerializeField] private CouchCarryController m_carryController;
        [SerializeField] private SledGrabPoint[] m_grabPoints = new SledGrabPoint[MaximumGrabPoints];
        [SerializeField] private Transform m_couchAnchor;
        [SerializeField] private Transform[] m_couchDocks = new Transform[4];
        [SerializeField, Tooltip("Clear half-width/depth inside each dock's frame and clamps.")]
        private Vector2 m_dockHalfExtents = new(1.16f, .60f);
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
        public int MaximumCargoItems => m_settings != null ? m_settings.MaximumCargoItems : 4;
        public int CouchDockCount => m_couchDocks != null ? m_couchDocks.Length : 0;
        public Transform GetCouchDock(int index) => index >= 0 && index < CouchDockCount ? m_couchDocks[index] : null;
        private Vector3 DockPosition(int index) => m_couchAnchor.InverseTransformPoint(GetCouchDock(index).position);
        private static Vector3 CouchOriginOffset(CouchCarryController couch) => Vector3.Scale(
            new Vector3(-couch.CargoBounds.center.x, -couch.CargoBounds.min.y, -couch.CargoBounds.center.z), couch.transform.localScale);

        public bool IsCouchDockOccupied(int index)
        {
            if (m_couchAnchor == null || GetCouchDock(index) == null) return true;
            Vector3 dock = DockPosition(index);
            foreach (var couch in m_cargo)
                if (couch != null && (couch.SledMountPosition - CouchOriginOffset(couch) - dock).sqrMagnitude < .01f)
                    return true;
            return false;
        }

        private int FindAvailableDock(CouchCarryController couch, bool requireOnSurface = false)
        {
            if (couch == null || m_couchAnchor == null) return -1;
            Vector3 size = Vector3.Scale(couch.CargoBounds.size, couch.transform.localScale);
            if (Mathf.Abs(size.x) > 2f * m_dockHalfExtents.x || Mathf.Abs(size.z) > 2f * m_dockHalfExtents.y) return -1;
            if (requireOnSurface && Vector3.Dot(couch.transform.up, transform.up) <= .5f) return -1;
            Vector3 centre = m_couchAnchor.InverseTransformPoint(couch.transform.TransformPoint(couch.CargoBounds.center));
            float halfHeight = Mathf.Abs(size.y) * .5f;
            int nearest = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < CouchDockCount; i++)
            {
                if (IsCouchDockOccupied(i)) continue;
                Vector3 delta = centre - DockPosition(i);
                if (requireOnSurface && (Mathf.Abs(delta.x) > m_dockHalfExtents.x ||
                    Mathf.Abs(delta.z) > m_dockHalfExtents.y || Mathf.Abs(delta.y - halfHeight) > m_loadingHeightTolerance)) continue;
                float candidate = delta.sqrMagnitude;
                if (candidate >= distance) continue;
                distance = candidate;
                nearest = i;
            }
            return nearest;
        }
        public bool HasCargoSpace => CargoCount < MaximumCargoItems;
        public string CargoStatus => $"Cargo: {CargoCount}/{MaximumCargoItems}";
        public string LoadingStatus => HasCargoSpace ? CargoStatus : $"Sled full ({CargoCount}/{MaximumCargoItems})";
        public bool CanLoadFrom(Vector3 position)
        {
            for (int i = 0; i < CouchDockCount; i++)
                if (GetCouchDock(i) != null && Vector3.Distance(position, GetCouchDock(i).position) <= m_loadingInteractionDistance) return true;
            return false;
        }

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
            Vector3 centre = m_couchAnchor.position;
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

        internal bool IsWithinLoadingSurface(CouchCarryController couch) => FindAvailableDock(couch, true) >= 0;

        private bool TryLoadPlacedCouchServer(CouchCarryController couch) =>
            couch != null && couch.ActiveCarrierCount == 0 && IsWithinLoadingSurface(couch) && TryLoadCouchServer(couch);

        [Server]
        internal bool TryLoadCouchServer(CouchCarryController couch)
        {
            if (!IsServerInitialized || !HasCargoSpace || Time.time < m_nextMountTime || couch == null) return false;
            int dock = FindAvailableDock(couch);
            return dock >= 0 && couch.MountOnSledServer(this, DockPosition(dock) + CouchOriginOffset(couch));
        }

        internal bool TryAutoLoadCouchServer(CouchCarryController couch, bool mount)
        {
            if (!IsServerInitialized || !IsSpawned || !HasCargoSpace || Time.time < m_nextMountTime ||
                m_couchAnchor == null || couch == null || couch.IsSled || couch.IsLoaded ||
                !couch.IsPickupAvailable || couch.IsCargoTransitioning) return false;
            if (FindAvailableDock(couch) < 0 || !IsWithinAutoLoadingRange(couch)) return false;
            return !mount || TryLoadCouchServer(couch);
        }

        private bool IsWithinAutoLoadingRange(CouchCarryController couch)
        {
            // An occupied dock is still a loading approach; select the free destination separately.
            for (int i = 0; i < CouchDockCount; i++)
            {
                Transform dock = GetCouchDock(i);
                if (dock == null) continue;
                Vector3 nearest = couch.transform.TransformPoint(couch.CargoBounds.ClosestPoint(
                    couch.transform.InverseTransformPoint(dock.position)));
                if (Vector3.Distance(nearest, dock.position) <= m_loadingInteractionDistance) return true;
            }
            return false;
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

        internal void EncapsulateCargoBounds(ref Bounds bounds)
        {
            foreach (var couch in m_cargo)
            {
                if (couch == null) continue;
                Bounds cargo = couch.CargoBounds;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            bounds.Encapsulate(couch.transform.TransformPoint(cargo.center +
                                Vector3.Scale(cargo.extents, new Vector3(x, y, z))));
            }
            foreach (var weapon in m_weapons)
                if (weapon != null)
                    bounds.Encapsulate(weapon.transform.position);
        }

        internal Vector3 GetMountPosition(CouchCarryController couch)
        {
            int dock = FindAvailableDock(couch);
            return dock >= 0 ? DockPosition(dock) + CouchOriginOffset(couch) : Vector3.zero;
        }

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
            // Each couch keeps its dock when another is removed. Its local pose is
            // already replicated by CouchCarryController, including for late joiners.
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
            // Remove in reverse registration order while despawning.
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
