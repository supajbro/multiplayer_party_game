using System.Collections.Generic;
using CouchGuys.UI;
using UnityEngine;

namespace CouchGuys.Gameplay.Shop
{
    /// <summary>Shared category-filtered shop marker. Requests use the player's existing network object.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(DispatcherMenuController))]
    public sealed class WeaponShopTerminal : MonoBehaviour
    {
        private static readonly HashSet<WeaponShopTerminal> s_active = new();
        [SerializeField] private Transform m_interactionPoint;
        [SerializeField, Min(.1f)] private float m_interactionRange = 1.8f;
        [SerializeField] private string m_prompt = "Press E to browse weapons";
        [SerializeField] private ShopItemCategory m_category = ShopItemCategory.Weapon;
        [Tooltip("Optional fallback vehicle spawn marker. A containing depot uses its existing garage markers first.")]
        [SerializeField] private Transform m_vehicleSpawnPoint;
        public ShopItemCategory Category => m_category;
        public Vector3 VehicleSpawnPosition => m_vehicleSpawnPoint != null
            ? m_vehicleSpawnPoint.position : transform.TransformPoint(new Vector3(6f, 0f, 1.5f));
        public Quaternion VehicleSpawnRotation => m_vehicleSpawnPoint != null ? m_vehicleSpawnPoint.rotation : transform.rotation;
        public DispatcherMenuController Menu => GetComponent<DispatcherMenuController>();
        public string Prompt => m_prompt;
        public Vector3 InteractionPosition => m_interactionPoint != null ? m_interactionPoint.position : transform.position;
        public bool IsInRange(Vector3 position, float tolerance = 0f) => isActiveAndEnabled &&
            (InteractionPosition - position).sqrMagnitude <= (m_interactionRange + tolerance) * (m_interactionRange + tolerance);
        private void OnEnable() => s_active.Add(this);
        private void OnDisable() => s_active.Remove(this);

        public static WeaponShopTerminal FindNearby(Vector3 playerPosition)
        {
            WeaponShopTerminal nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var terminal in s_active)
            {
                if (terminal == null || !terminal.IsInRange(playerPosition)) continue;
                float candidate = (terminal.InteractionPosition - playerPosition).sqrMagnitude;
                if (candidate >= distance) continue;
                distance = candidate; nearest = terminal;
            }
            return nearest;
        }

        // Generated depot props need no NetworkObject. The server resolves its own marker,
        // independently validates range, and authorizes every purchase through DeliveryManager.
        public static WeaponShopTerminal Resolve(Vector3 position)
        {
            foreach (var terminal in s_active)
                if (terminal != null && terminal.isActiveAndEnabled &&
                    (terminal.transform.position - position).sqrMagnitude < .01f) return terminal;
            return null;
        }
    }
}
