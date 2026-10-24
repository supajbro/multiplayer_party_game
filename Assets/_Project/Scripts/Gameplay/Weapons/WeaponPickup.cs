using FishNet;
using FishNet.Object;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>One server-owned physical weapon. Claims are serialized on the server.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
    public sealed class WeaponPickup : NetworkBehaviour
    {
        [SerializeField] private PlayerWeaponType m_weaponType;
        private int m_magazine = -1;
        private int m_reserve = -1;
        private bool m_claimed;
        public string DisplayName => m_weaponType == PlayerWeaponType.AssaultRifle ? "Assault Rifle" : m_weaponType.ToString();

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized) GetComponent<Rigidbody>().isKinematic = true;
        }

        public static WeaponPickup SpawnServer(PlayerWeaponType type, Vector3 position, Quaternion rotation,
            Vector3 velocity, int magazine = -1, int reserve = -1)
        {
            if (!InstanceFinder.IsServerStarted) return null;
            GameObject prefab = Resources.Load<GameObject>($"WeaponPickups/{type}");
            if (prefab == null) { Debug.LogError($"Missing weapon pickup prefab: {type}"); return null; }
            WeaponPickup pickup = Instantiate(prefab, position, rotation).GetComponent<WeaponPickup>();
            pickup.m_magazine = magazine;
            pickup.m_reserve = reserve;
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
            if (!player.AcquireWeaponServer(m_weaponType, m_magazine, m_reserve))
            {
                m_claimed = false;
                return false;
            }
            Despawn(NetworkObject);
            return true;
        }
    }
}
