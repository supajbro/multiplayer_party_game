using FishNet;
using CouchGuys.Gameplay.Healing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Component.Transforming;
using CouchGuys.Gameplay.Sled;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    public struct SledPickupAttachment
    {
        public NetworkObject Sled;
        public int Slot;
        public Vector3 ReleasePosition;
        public Quaternion ReleaseRotation;
    }

    /// <summary>One server-owned physical weapon. Claims are serialized on the server.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
    public sealed class WeaponPickup : NetworkBehaviour
    {
        [SerializeField] private PlayerWeaponType m_weaponType;
        [SerializeField] private HealingItemSettings m_healingItem;
        [Header("Sled Cargo")]
        [SerializeField] private bool m_allowSledStorage = true;
        [SerializeField] private Vector3 m_sledPositionOffset;
        [SerializeField] private Vector3 m_sledRotationOffset;
        private readonly SyncVar<SledPickupAttachment> m_sledAttachment = new();
        private SledController m_appliedSled;
        private Rigidbody m_body;
        public SledController MountedSled => m_sledAttachment.Value.Sled != null
            ? m_sledAttachment.Value.Sled.GetComponent<SledController>() : null;
        internal int SledSlot => m_sledAttachment.Value.Slot;
        public HealingItemSettings HealingSettings => m_healingItem;
        private int m_magazine = -1;
        private int m_reserve = -1;
        private bool m_claimed;
        public string DisplayName => m_healingItem != null ? m_healingItem.DisplayName : m_weaponType == PlayerWeaponType.BaseballBat ? "Baseball Bat" : m_weaponType == PlayerWeaponType.RocketLauncher ? "Rocket Launcher" :
            m_weaponType == PlayerWeaponType.GrappleGun ? "Grapple Gun" :
            m_weaponType == PlayerWeaponType.AssaultRifle ? "Assault Rifle" : m_weaponType.ToString();

        private void Awake()
        {
            m_body = GetComponent<Rigidbody>();
            m_sledAttachment.OnChange += OnSledAttachmentChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) m_body.isKinematic = true;
            if (MountedSled != null) ApplySledAttachment(false);
        }

        private void OnDestroy()
        {
            m_sledAttachment.OnChange -= OnSledAttachmentChanged;
            if (m_appliedSled != null) m_appliedSled.ForgetWeapon(this);
        }

        public override void OnStopServer()
        {
            if (m_appliedSled != null) m_appliedSled.ForgetWeapon(this);
            m_appliedSled = null;
            base.OnStopServer();
        }

        public override void OnStopClient()
        {
            if (m_appliedSled != null) m_appliedSled.ForgetWeapon(this);
            m_appliedSled = null;
            transform.SetParent(null, true);
            base.OnStopClient();
        }

        private void LateUpdate()
        {
            // Also retries attachment if the sled spawns after this pickup on a late joiner.
            if (MountedSled != null) ApplySledAttachment(false);
        }

        private void OnSledAttachmentChanged(SledPickupAttachment previous, SledPickupAttachment next, bool asServer) =>
            ApplySledAttachment(true);

        private void ApplySledAttachment(bool applyReleasePose)
        {
            var sled = MountedSled;
            if (m_appliedSled != null && m_appliedSled != sled) m_appliedSled.ForgetWeapon(this);
            m_appliedSled = sled;
            Transform anchor = sled != null ? sled.GetWeaponAnchor(SledSlot) : null;
            var networkTransform = GetComponent<NetworkTransform>();
            if (networkTransform != null)
            {
                networkTransform.SetSynchronizePosition(anchor == null);
                networkTransform.SetSynchronizeRotation(anchor == null);
            }
            if (anchor != null)
            {
                if (!m_body.isKinematic)
                {
                    m_body.linearVelocity = Vector3.zero;
                    m_body.angularVelocity = Vector3.zero;
                }
                m_body.isKinematic = true;
                transform.SetParent(anchor, false);
                transform.localPosition = m_sledPositionOffset;
                transform.localRotation = Quaternion.Euler(m_sledRotationOffset);
                sled.RegisterWeapon(this);
            }
            else
            {
                transform.SetParent(null, true);
                if (applyReleasePose)
                    transform.SetPositionAndRotation(m_sledAttachment.Value.ReleasePosition, m_sledAttachment.Value.ReleaseRotation);
                m_body.isKinematic = !IsServerInitialized;
            }
        }

        [Server]
        internal bool MountOnSledServer(SledController sled, int slot)
        {
            if (!IsServerInitialized || !IsSpawned || !m_allowSledStorage || m_claimed || MountedSled != null ||
                sled == null || !sled.HasCargoSpace || sled.GetWeaponAnchor(slot) == null) return false;
            m_sledAttachment.Value = new SledPickupAttachment { Sled = sled.NetworkObject, Slot = slot };
            ApplySledAttachment(false);
            return true;
        }

        [Server]
        internal void UnmountFromSledServer()
        {
            var sled = MountedSled;
            if (sled == null) return;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            m_sledAttachment.Value = new SledPickupAttachment { Slot = -1, ReleasePosition = position, ReleaseRotation = rotation };
            ApplySledAttachment(true);
            m_body.linearVelocity = sled.SledRigidbody.GetPointVelocity(position);
            GetComponent<NetworkTransform>()?.Teleport();
        }

        public static WeaponPickup SpawnServer(PlayerWeaponType type, Vector3 position, Quaternion rotation,
            Vector3 velocity, int magazine = -1, int reserve = -1)
        {
            if (!InstanceFinder.IsServerStarted) return null;
            GameObject prefab = type == PlayerWeaponType.BaseballBat
                ? Resources.Load<BaseballBatAssets>(BaseballBatAssets.ResourcesPath)?.PickupPrefab
                : type == PlayerWeaponType.RocketLauncher
                ? Resources.Load<RocketLauncherAssets>("Weapons/RocketLauncherAssets")?.PickupPrefab
                : Resources.Load<GameObject>($"WeaponPickups/{type}");
            if (prefab == null) { Debug.LogError($"Missing weapon pickup prefab: {type}"); return null; }
            WeaponPickup pickup = Instantiate(prefab, position, rotation).GetComponent<WeaponPickup>();
            pickup.m_magazine = magazine;
            pickup.m_reserve = reserve;
            InstanceFinder.ServerManager.Spawn(pickup.NetworkObject);
            pickup.GetComponent<Rigidbody>().linearVelocity = velocity;
            return pickup;
        }

        public static WeaponPickup SpawnHealingServer(HealingItemSettings settings, Vector3 position,
            Quaternion rotation, Vector3 velocity)
        {
            if (!InstanceFinder.IsServerStarted || settings == null || settings.PickupPrefab == null) return null;
            var pickup = Instantiate(settings.PickupPrefab, position, rotation);
            InstanceFinder.ServerManager.Spawn(pickup.NetworkObject);
            pickup.GetComponent<Rigidbody>().linearVelocity = velocity;
            return pickup;
        }

        [Server]
        public bool TryPickupServer(PlayerWeaponController player)
        {
            if (m_claimed || !IsSpawned || player == null || !player.CanHandleWeaponServer ||
                (transform.position - player.transform.position).sqrMagnitude > 2.15f * 2.15f) return false;
            Vector3 origin = player.transform.position + Vector3.up;
            Vector3 offset = transform.position - origin;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude,
                         ~0, QueryTriggerInteraction.Ignore))
                if (hit.transform.root != player.transform.root && hit.transform.root != transform.root) return false;

            m_claimed = true;
            if (!(m_healingItem != null ? player.AcquireHealingItemServer(m_healingItem) :
                player.AcquireWeaponServer(m_weaponType, m_magazine, m_reserve)))
            {
                m_claimed = false;
                return false;
            }
            UnmountFromSledServer();
            Despawn(NetworkObject);
            return true;
        }
    }
}
