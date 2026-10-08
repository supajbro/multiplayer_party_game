using System.Collections.Generic;
using CouchGuys.Gameplay.Enemies;
using CouchGuys.Gameplay.Delivery;
using CouchGuys.Input;
using CouchGuys.Player;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    /// <summary>Owner input with server-authoritative ammo, hitscan damage, and knockback.</summary>
    [RequireComponent(typeof(PlayerInputReader), typeof(PlayerHealth), typeof(PlayerCouchCarrier))]
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponController : NetworkBehaviour
    {
        private const int WeaponCount = 3;
        private const int RaycastBufferSize = 16;

        [Header("References")]
        [SerializeField] private PlayerInputReader m_input;
        [SerializeField] private PlayerHealth m_health;
        [SerializeField] private PlayerCouchCarrier m_couchCarrier;
        [SerializeField] private CouchGuyAnimationDriver m_animationDriver;
        [SerializeField] private Camera m_aimCamera;
        [SerializeField] private Transform[] m_weaponModels = new Transform[WeaponCount];
        [SerializeField] private Transform[] m_muzzles = new Transform[WeaponCount];
        [SerializeField] private PlayerWeaponEffects m_effects;
        [SerializeField] private NeighbourhoodMap m_map;

        [Header("Weapons")]
        [SerializeField] private WeaponDefinition[] m_weapons = new WeaponDefinition[WeaponCount];
        [SerializeField] private LayerMask m_hitMask = ~0;
        [SerializeField, Min(0f)] private float m_switchDuration = 0.18f;
        [SerializeField, Min(0.5f)] private float m_maximumAimOriginDistance = 3.5f;

        private readonly SyncVar<int> m_equippedIndex = new(-1);
        private readonly int[] m_magazine = new int[WeaponCount];
        private readonly int[] m_reserve = new int[WeaponCount];
        private readonly float[] m_nextServerFireTime = new float[WeaponCount];
        private readonly RaycastHit[] m_hits = new RaycastHit[RaycastBufferSize];
        private readonly Dictionary<PlayerHealth, float> m_playerDamage = new(8);
        private readonly Dictionary<ThiefHealth, float> m_thiefDamage = new(8);
        private float m_switchCompleteAt;
        private float m_reloadCompleteAt;
        private int m_reloadingIndex = -1;
        private float m_nextLocalRequestTime;
        private bool m_localReloading;
        private readonly Quaternion[] m_weaponBaseRotations = new Quaternion[WeaponCount];
        private float m_visualRecoil;

        public PlayerWeaponType EquippedWeapon => (PlayerWeaponType)Mathf.Clamp(m_equippedIndex.Value, 0, 2);
        public string EquippedWeaponName => GetWeapon(m_equippedIndex.Value)?.DisplayName ?? "None";
        public int CurrentMagazine => ValidIndex(m_equippedIndex.Value) ? m_magazine[m_equippedIndex.Value] : 0;
        public int CurrentReserve => ValidIndex(m_equippedIndex.Value) ? m_reserve[m_equippedIndex.Value] : 0;
        public bool IsReloading => m_localReloading || m_reloadingIndex >= 0;

        private void Awake()
        {
            m_input ??= GetComponent<PlayerInputReader>();
            m_health ??= GetComponent<PlayerHealth>();
            m_couchCarrier ??= GetComponent<PlayerCouchCarrier>();
            m_animationDriver ??= GetComponent<CouchGuyAnimationDriver>();
            m_aimCamera ??= GetComponentInChildren<Camera>(true);
            m_effects ??= GetComponent<PlayerWeaponEffects>();
            m_map ??= GetComponent<NeighbourhoodMap>();
            if (m_effects == null) m_effects = gameObject.AddComponent<PlayerWeaponEffects>();
            if (m_weapons == null || m_weapons.Length != WeaponCount || m_weapons[0] == null)
                m_weapons = WeaponDefinition.CreateDefaults();
            ResolveModelReferences();
            SetModelVisibility(-1);
            m_equippedIndex.OnChange += OnEquippedChanged;
        }

        private void OnDestroy() => m_equippedIndex.OnChange -= OnEquippedChanged;

        public override void OnStartServer()
        {
            base.OnStartServer();
            for (int i = 0; i < WeaponCount; i++)
            {
                WeaponDefinition weapon = GetWeapon(i);
                m_magazine[i] = weapon.MagazineCapacity;
                m_reserve[i] = weapon.StartingReserve;
            }
            m_equippedIndex.Value = 0;
            m_switchCompleteAt = Time.time + m_switchDuration;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyEquippedWeapon(m_equippedIndex.Value);
            if (IsOwner) RequestWeaponStateServerRpc();
        }

        private void Update()
        {
            if (IsServerInitialized && m_reloadingIndex >= 0 && Time.time >= m_reloadCompleteAt)
                FinishReloadServer();

            if (!IsOwner || m_input == null || !m_input.isActiveAndEnabled || m_health == null ||
                !m_health.IsAlive || m_health.IsKnockedDown)
                return;

            int selection = m_input.WeaponSelectionPressedThisFrame;
            if (selection >= 0 && selection != m_equippedIndex.Value)
            {
                m_localReloading = false;
                RequestEquipServerRpc(selection);
                m_nextLocalRequestTime = Time.time + m_switchDuration;
            }

            if (m_input.ReloadPressedThisFrame && !m_localReloading)
                RequestReloadServerRpc();

            WeaponDefinition weapon = GetWeapon(m_equippedIndex.Value);
            bool wantsFire = weapon != null && (weapon.Automatic ? m_input.ShootHeld : m_input.ShootPressedThisFrame);
            if (wantsFire && !m_localReloading && Time.time >= m_nextLocalRequestTime &&
                (m_couchCarrier == null || !m_couchCarrier.IsCarrying) && (m_map == null || !m_map.IsOpen))
            {
                Ray ray = m_aimCamera != null
                    ? m_aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                    : new Ray(transform.position + Vector3.up * 1.2f, transform.forward);
                RequestFireServerRpc(ray.origin, ray.direction);
                m_nextLocalRequestTime = Time.time + weapon.FireInterval;
            }
        }

        private void LateUpdate()
        {
            int index = m_equippedIndex.Value;
            if (!ValidIndex(index) || m_weaponModels[index] == null) return;
            m_visualRecoil = Mathf.MoveTowards(m_visualRecoil, 0f, 55f * Time.deltaTime);
            m_weaponModels[index].localRotation = m_weaponBaseRotations[index] *
                Quaternion.Euler(-m_visualRecoil, 0f, 0f);
        }

        [ServerRpc]
        private void RequestEquipServerRpc(int index)
        {
            if (!ValidIndex(index) || index == m_equippedIndex.Value || !m_health.IsAlive) return;
            m_reloadingIndex = -1;
            m_equippedIndex.Value = index;
            m_switchCompleteAt = Time.time + m_switchDuration;
            SendWeaponStateTargetRpc(Owner, index, m_magazine[index], m_reserve[index], false);
        }

        [ServerRpc]
        private void RequestReloadServerRpc()
        {
            BeginReloadServer();
        }

        [Server]
        private void BeginReloadServer()
        {
            int index = m_equippedIndex.Value;
            WeaponDefinition weapon = GetWeapon(index);
            if (weapon == null || m_reloadingIndex >= 0 || Time.time < m_switchCompleteAt ||
                m_magazine[index] >= weapon.MagazineCapacity || m_reserve[index] <= 0 ||
                !m_health.IsAlive || (m_couchCarrier != null && m_couchCarrier.IsCarrying)) return;

            m_reloadingIndex = index;
            m_reloadCompleteAt = Time.time + weapon.ReloadDuration;
            if (Owner.IsValid)
            {
                SendWeaponStateTargetRpc(
                    Owner,
                    index,
                    m_magazine[index],
                    m_reserve[index],
                    true);
            }
        }

        [ServerRpc]
        private void RequestWeaponStateServerRpc()
        {
            int index = Mathf.Clamp(m_equippedIndex.Value, 0, WeaponCount - 1);
            SendWeaponStateTargetRpc(Owner, index, m_magazine[index], m_reserve[index], m_reloadingIndex == index);
        }

        [ServerRpc]
        private void RequestFireServerRpc(Vector3 aimOrigin, Vector3 aimDirection)
        {
            TryFireServer(aimOrigin, aimDirection);
        }

        internal void ConfigureAiWeaponServer(PlayerWeaponType weaponType)
        {
            if (!IsServerInitialized || Owner.IsValid ||
                !ValidIndex((int)weaponType) || !m_health.IsAlive)
            {
                return;
            }

            int index = (int)weaponType;
            m_reloadingIndex = -1;
            m_equippedIndex.Value = index;
            m_switchCompleteAt = Time.time;
        }

        internal bool TryFireAtThiefServer(ThiefHealth target)
        {
            if (!IsServerInitialized || Owner.IsValid ||
                target == null || !target.IsAlive)
            {
                return false;
            }

            int index = m_equippedIndex.Value;
            WeaponDefinition weapon = GetWeapon(index);
            if (weapon == null)
            {
                return false;
            }

            Vector3 origin = GetMuzzlePosition(index, m_health.AimPoint);
            Vector3 aimPoint = target.transform.position + Vector3.up * 0.9f;
            Vector3 direction = aimPoint - origin;
            if (direction.sqrMagnitude > weapon.Range * weapon.Range ||
                direction.sqrMagnitude < 0.01f ||
                !TryGetNearestNonSelfHit(
                    origin,
                    direction.normalized,
                    weapon.Range,
                    out RaycastHit firstHit) ||
                firstHit.collider.GetComponentInParent<ThiefHealth>() != target)
            {
                return false;
            }

            m_animationDriver?.SetPointing(true);
            return TryFireServer(origin, direction.normalized);
        }

        private bool TryFireServer(Vector3 aimOrigin, Vector3 aimDirection)
        {
            if (!IsServerInitialized)
            {
                return false;
            }

            int index = m_equippedIndex.Value;
            WeaponDefinition weapon = GetWeapon(index);
            if (weapon == null || m_reloadingIndex >= 0 || Time.time < m_switchCompleteAt ||
                Time.time < m_nextServerFireTime[index] || !m_health.IsAlive ||
                (m_couchCarrier != null && m_couchCarrier.IsCarrying) ||
                (aimOrigin - m_health.AimPoint).sqrMagnitude >
                m_maximumAimOriginDistance * m_maximumAimOriginDistance ||
                aimDirection.sqrMagnitude < 0.5f)
            {
                return false;
            }

            if (m_magazine[index] <= 0)
            {
                BeginReloadServer();
                return false;
            }

            m_nextServerFireTime[index] = Time.time + weapon.FireInterval;
            m_magazine[index]--;
            Vector3 muzzle = GetMuzzlePosition(index, aimOrigin);
            Vector3 cameraDirection = aimDirection.normalized;
            Vector3 aimPoint = aimOrigin + cameraDirection * weapon.Range;
            if (TryGetNearestNonSelfHit(
                    aimOrigin,
                    cameraDirection,
                    weapon.Range,
                    out RaycastHit aimHit))
            {
                aimPoint = aimHit.point;
            }

            Vector3 muzzleDirection = aimPoint - muzzle;
            if (muzzleDirection.sqrMagnitude < 0.001f)
            {
                muzzleDirection = cameraDirection;
            }

            Vector3[] ends = ResolveHitsServer(muzzle, muzzleDirection.normalized, weapon);
            ShowShotObserversRpc(index, muzzle, ends);
            if (Owner.IsValid)
            {
                SendWeaponStateTargetRpc(
                    Owner,
                    index,
                    m_magazine[index],
                    m_reserve[index],
                    false);
            }

            return true;
        }

        [Server]
        private Vector3[] ResolveHitsServer(Vector3 origin, Vector3 forward, WeaponDefinition weapon)
        {
            m_playerDamage.Clear();
            m_thiefDamage.Clear();
            Vector3[] ends = new Vector3[weapon.Pellets];
            for (int pellet = 0; pellet < weapon.Pellets; pellet++)
            {
                Vector3 direction = ApplySpread(forward, weapon.SpreadDegrees);
                ends[pellet] = origin + direction * weapon.Range;
                if (!TryGetNearestNonSelfHit(origin, direction, weapon.Range, out RaycastHit nearest)) continue;
                ends[pellet] = nearest.point;
                float damage = weapon.DamageAtDistance(nearest.distance);
                PlayerHealth player = nearest.collider.GetComponentInParent<PlayerHealth>();
                if (player != null && player != m_health)
                {
                    m_playerDamage.TryGetValue(player, out float total);
                    m_playerDamage[player] = total + damage;
                    continue;
                }
                ThiefHealth thief = nearest.collider.GetComponentInParent<ThiefHealth>();
                if (thief != null)
                {
                    m_thiefDamage.TryGetValue(thief, out float total);
                    m_thiefDamage[thief] = total + damage;
                }
            }

            float maximumShotDamage = weapon.DamagePerPellet * weapon.Pellets;
            foreach (KeyValuePair<PlayerHealth, float> entry in m_playerDamage)
            {
                float force = weapon.Knockback * Mathf.Lerp(0.25f, 1f,
                    Mathf.Clamp01(entry.Value / maximumShotDamage));
                entry.Key.ApplyDamageServer(entry.Value, forward, force);
            }
            foreach (KeyValuePair<ThiefHealth, float> entry in m_thiefDamage)
            {
                float force = weapon.Knockback * Mathf.Lerp(0.25f, 1f,
                    Mathf.Clamp01(entry.Value / maximumShotDamage));
                entry.Key.ApplyDamageServer(entry.Value, forward, force);
            }
            return ends;
        }

        private bool TryGetNearestNonSelfHit(Vector3 origin, Vector3 direction, float range,
            out RaycastHit nearest)
        {
            int hitCount = Physics.RaycastNonAlloc(origin, direction, m_hits, range,
                m_hitMask, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            nearest = default;
            bool found = false;
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                RaycastHit hit = m_hits[hitIndex];
                if (hit.collider == null || hit.collider.GetComponentInParent<PlayerWeaponController>() == this ||
                    hit.distance >= nearestDistance) continue;
                nearestDistance = hit.distance;
                nearest = hit;
                found = true;
            }
            return found;
        }

        [Server]
        private void FinishReloadServer()
        {
            int index = m_reloadingIndex;
            m_reloadingIndex = -1;
            WeaponDefinition weapon = GetWeapon(index);
            if (weapon == null) return;
            int transfer = Mathf.Min(weapon.MagazineCapacity - m_magazine[index], m_reserve[index]);
            m_magazine[index] += transfer;
            m_reserve[index] -= transfer;
            if (Owner.IsValid)
            {
                SendWeaponStateTargetRpc(
                    Owner,
                    index,
                    m_magazine[index],
                    m_reserve[index],
                    false);
            }
        }

        [TargetRpc]
        private void SendWeaponStateTargetRpc(NetworkConnection connection, int index, int magazine,
            int reserve, bool reloading)
        {
            if (!ValidIndex(index)) return;
            m_magazine[index] = magazine;
            m_reserve[index] = reserve;
            m_localReloading = reloading;
        }

        [ObserversRpc(RunLocally = true)]
        private void ShowShotObserversRpc(int weaponIndex, Vector3 muzzle, Vector3[] ends)
        {
            m_effects?.PlayShot(muzzle, ends);
            m_animationDriver?.PlayWeaponShot((PlayerWeaponType)weaponIndex);
            WeaponDefinition weapon = GetWeapon(weaponIndex);
            if (weapon != null) m_visualRecoil = Mathf.Min(weapon.Recoil * 1.8f, m_visualRecoil + weapon.Recoil);
        }

        private void OnEquippedChanged(int previous, int next, bool asServer) => ApplyEquippedWeapon(next);

        private void ApplyEquippedWeapon(int index)
        {
            SetModelVisibility(index);
            m_animationDriver?.SetEquippedWeapon(ValidIndex(index) ? index + 1 : 0);
        }

        private void SetModelVisibility(int equippedIndex)
        {
            if (m_weaponModels == null) return;
            for (int i = 0; i < m_weaponModels.Length; i++)
                if (m_weaponModels[i] != null)
                {
                    m_weaponModels[i].localRotation = m_weaponBaseRotations[i];
                    m_weaponModels[i].gameObject.SetActive(i == equippedIndex);
                }
            m_visualRecoil = 0f;
        }

        private void ResolveModelReferences()
        {
            if (m_weaponModels == null || m_weaponModels.Length != WeaponCount)
                m_weaponModels = new Transform[WeaponCount];
            if (m_muzzles == null || m_muzzles.Length != WeaponCount)
                m_muzzles = new Transform[WeaponCount];

            string[] names = { "Weapon_Pistol", "Weapon_AssaultRifle", "Weapon_Shotgun" };
            for (int i = 0; i < WeaponCount; i++)
            {
                m_weaponModels[i] ??= FindDescendant(transform, names[i]);
                if (m_weaponModels[i] != null) m_weaponBaseRotations[i] = m_weaponModels[i].localRotation;
                if (m_muzzles[i] == null && m_weaponModels[i] != null)
                    m_muzzles[i] = FindDescendant(m_weaponModels[i], "Muzzle");
            }
        }

        private Vector3 GetMuzzlePosition(int index, Vector3 fallback) =>
            ValidIndex(index) && m_muzzles[index] != null ? m_muzzles[index].position : fallback;

        private WeaponDefinition GetWeapon(int index) =>
            ValidIndex(index) && m_weapons != null && index < m_weapons.Length ? m_weapons[index] : null;

        private static bool ValidIndex(int index) => index >= 0 && index < WeaponCount;

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private static Vector3 ApplySpread(Vector3 forward, float spreadDegrees)
        {
            if (spreadDegrees <= 0f) return forward;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector2 spread = Random.insideUnitCircle * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
            return rotation * new Vector3(spread.x, spread.y, 1f).normalized;
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            m_weapons = WeaponDefinition.CreateDefaults();
            ResolveModelReferences();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            m_switchDuration = Mathf.Max(0f, m_switchDuration);
            m_maximumAimOriginDistance = Mathf.Max(0.5f, m_maximumAimOriginDistance);
            if (m_weapons == null || m_weapons.Length != WeaponCount || m_weapons[0] == null)
                m_weapons = WeaponDefinition.CreateDefaults();
        }
#endif
    }
}
