using CouchGuys.Gameplay.Weapons;
using CouchGuys.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Healing
{
    /// <summary>One shared server-authoritative timed consumable action for every healing definition.</summary>
    [DisallowMultipleComponent]
    public sealed class HealingItem : NetworkBehaviour
    {
        [SerializeField] private PlayerWeaponController m_inventory;
        [SerializeField] private PlayerHealth m_health;
        [SerializeField] private CouchGuyAnimationDriver m_animation;
        [SerializeField] private Transform m_effectAnchor;
        private readonly SyncVar<int> m_usingId = new(-1);
        private int m_usingSlot = -1;
        private float m_completeAt, m_nextGesture;
        private GameObject m_useEffect;
        public bool IsUsing => m_usingId.Value >= 0;

        private void Awake()
        {
            m_inventory ??= GetComponent<PlayerWeaponController>();
            m_health ??= GetComponent<PlayerHealth>();
            m_animation ??= GetComponent<CouchGuyAnimationDriver>();
            m_usingId.OnChange += OnUseChanged;
        }
        private void OnDestroy()
        {
            m_usingId.OnChange -= OnUseChanged;
            if (m_useEffect != null) Destroy(m_useEffect);
        }
        public override void OnStartServer() { base.OnStartServer(); m_usingId.Value = -1; }
        public override void OnStartClient() { base.OnStartClient(); PresentUse(m_usingId.Value); }
        public override void OnStopClient() { PresentUse(-1); base.OnStopClient(); }

        [ServerRpc]
        public void RequestUseServerRpc() => TryBeginUseServer();

        [Server]
        public bool TryBeginUseServer()
        {
            var settings = m_inventory.EquippedHealingItem;
            if (IsUsing || settings == null || settings.HealingPercentage <= 0f || !m_inventory.CanHandleWeaponServer ||
                !m_inventory.IsItemSwitchReady || !m_health.CanHeal) return false;
            m_usingSlot = m_inventory.SelectedHotbarSlot;
            m_completeAt = Time.time + settings.UseDuration;
            m_usingId.Value = m_inventory.GetInventoryWeaponIndex(m_usingSlot);
            return true;
        }
        private void Update()
        {
            if (IsServerInitialized && IsUsing)
            {
                var settings = m_inventory.EquippedHealingItem;
                if (!m_inventory.CanHandleWeaponServer || !m_health.CanHeal || settings == null ||
                    m_inventory.SelectedHotbarSlot != m_usingSlot ||
                    m_inventory.GetInventoryWeaponIndex(m_usingSlot) != m_usingId.Value)
                    CancelUseServer();
                else if (Time.time >= m_completeAt)
                {
                    int id = m_usingId.Value;
                    // Both operations run synchronously on the server. A repeated RPC cannot
                    // consume this slot again, and no health values come from the client.
                    if (m_inventory.ConsumeHealingItemServer(m_usingSlot, id))
                    {
                        m_health.TryHealPercentageServer(settings.HealingPercentage);
                        PlayHealObserversRpc(id);
                    }
                    CancelUseServer();
                }
            }
            if (IsClientInitialized && IsUsing && Time.time >= m_nextGesture)
            {
                m_animation?.PlayPunch(false);
                m_nextGesture = Time.time + .6f;
            }
        }
        [Server]
        public void CancelUseServer() { m_usingId.Value = -1; m_usingSlot = -1; }
        private void OnUseChanged(int previous, int next, bool asServer)
        {
            if (!asServer) PresentUse(next);
        }
        private void PresentUse(int id)
        {
            if (m_useEffect != null) Destroy(m_useEffect);
            var settings = HealingItemCatalogue.Load()?.Find(id);
            m_animation?.SetHoldingItem(settings != null);
            if (settings == null) return;
            m_nextGesture = 0f;
            m_useEffect = PlayEffect(settings.UseEffectPrefab, settings.UseAudio, settings.UseDuration + settings.EffectLifetime);
        }
        [ObserversRpc]
        private void PlayHealObserversRpc(int id)
        {
            var settings = HealingItemCatalogue.Load()?.Find(id);
            if (settings != null) PlayEffect(settings.HealEffectPrefab, settings.HealAudio, settings.EffectLifetime);
        }
        private GameObject PlayEffect(GameObject prefab, AudioClip clip, float lifetime)
        {
            Vector3 position = m_effectAnchor != null ? m_effectAnchor.position : transform.position + Vector3.up;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, position);
            if (prefab == null) return null;
            var effect = Instantiate(prefab, position, transform.rotation, transform);
            Destroy(effect, lifetime);
            return effect;
        }
    }
}
