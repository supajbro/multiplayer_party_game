using System;
using UnityEngine;

namespace CouchGuys.Gameplay.Weapons
{
    [Serializable]
    public sealed class WeaponDefinition
    {
        [SerializeField] private PlayerWeaponType m_type;
        [SerializeField] private string m_displayName = "Weapon";
        [SerializeField] private bool m_automatic;
        [SerializeField, Min(0.02f)] private float m_fireInterval = 0.3f;
        [SerializeField, Min(1f)] private float m_range = 40f;
        [SerializeField, Min(1)] private int m_pellets = 1;
        [SerializeField, Min(0f)] private float m_spreadDegrees = 1f;
        [SerializeField, Min(0f)] private float m_damagePerPellet = 20f;
        [SerializeField, Range(0f, 1f)] private float m_minimumDamageMultiplier = 1f;
        [SerializeField, Range(0f, 1f)] private float m_falloffStart = 0.5f;
        [SerializeField, Min(0f)] private float m_recoil = 5f;
        [SerializeField, Min(1)] private int m_magazineCapacity = 12;
        [SerializeField, Min(0)] private int m_startingReserve = 60;
        [SerializeField, Min(0.05f)] private float m_reloadDuration = 1.25f;
        [SerializeField, Min(0f)] private float m_knockback = 3f;

        public PlayerWeaponType Type => m_type;
        public string DisplayName => m_displayName;
        public bool Automatic => m_automatic;
        public float FireInterval => m_fireInterval;
        public float Range => m_range;
        public int Pellets => m_pellets;
        public float SpreadDegrees => m_spreadDegrees;
        public float DamagePerPellet => m_damagePerPellet;
        public float Recoil => m_recoil;
        public int MagazineCapacity => m_magazineCapacity;
        public int StartingReserve => m_startingReserve;
        public float ReloadDuration => m_reloadDuration;
        public float Knockback => m_knockback;

        public float DamageAtDistance(float distance)
        {
            float falloff = Mathf.InverseLerp(m_range * m_falloffStart, m_range, distance);
            return m_damagePerPellet * Mathf.Lerp(1f, m_minimumDamageMultiplier, falloff);
        }

        public static WeaponDefinition[] CreateDefaults()
        {
            return new[]
            {
                Create(PlayerWeaponType.Pistol, "Pistol", false, 0.42f, 45f, 1, 0.8f, 28f, 0.8f, 0.72f, 8f, 12, 60, 1.25f, 2.5f),
                Create(PlayerWeaponType.AssaultRifle, "Assault Rifle", true, 0.095f, 55f, 1, 1.8f, 12f, 0.65f, 0.55f, 3f, 30, 120, 1.8f, 1.5f),
                Create(PlayerWeaponType.Shotgun, "Shotgun", false, 0.9f, 28f, 9, 7.5f, 11f, 0.22f, 0.3f, 14f, 6, 36, 2.2f, 12f)
            };
        }

        private static WeaponDefinition Create(PlayerWeaponType type, string name, bool automatic,
            float interval, float range, int pellets, float spread, float damage,
            float minimumDamage, float falloffStart, float recoil, int magazine,
            int reserve, float reload, float knockback)
        {
            return new WeaponDefinition
            {
                m_type = type, m_displayName = name, m_automatic = automatic,
                m_fireInterval = interval, m_range = range, m_pellets = pellets,
                m_spreadDegrees = spread, m_damagePerPellet = damage,
                m_minimumDamageMultiplier = minimumDamage, m_falloffStart = falloffStart,
                m_recoil = recoil, m_magazineCapacity = magazine,
                m_startingReserve = reserve, m_reloadDuration = reload,
                m_knockback = knockback
            };
        }
    }
}
